#if UNITY_EDITOR
using System;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Host-only play-mode check of an objective mode through the real game flow: lobby -> host ->
    // pick the mode -> ready -> START -> arena; then the host's avatar is teleported around the map
    // (flag stands / control points from the arena's MapLayout) and the server-side results are checked.
    //
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.ObjectivePlayCheck.RunBatch -objMode CTF|DOM|BOMB|VIP [-objMap 1]
    // (relies on Enter Play Mode Options with domain reload off, as set in this project)
    public static class ObjectivePlayCheck
    {
        static MatchNet.GameModeType mode;
        static int mapIndex = -1;   // -objMap <MapCatalog index>: the host locks that map (-1 = the default / vote)
        static int stage;
        static double stageStart, runStart;
        static int fails, passes;
        static bool batch;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void RunBatch()
        {
            batch = true;
            var args = Environment.GetCommandLineArgs();
            string m = "CTF";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-objMode") m = args[i + 1];
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-objMap" && int.TryParse(args[i + 1], out int mi)) mapIndex = mi;
            Run(m == "DOM" ? MatchNet.GameModeType.Domination : m == "BOMB" ? MatchNet.GameModeType.Bomb
                : m == "VIP" ? MatchNet.GameModeType.VipEscort : MatchNet.GameModeType.CaptureTheFlag);
        }

        [MenuItem("Tools/Airsoft/Play Check: Capture the Flag")]
        static void RunCtf() => Run(MatchNet.GameModeType.CaptureTheFlag);

        [MenuItem("Tools/Airsoft/Play Check: Domination")]
        static void RunDom() => Run(MatchNet.GameModeType.Domination);

        [MenuItem("Tools/Airsoft/Play Check: Bomb")]
        static void RunBomb() => Run(MatchNet.GameModeType.Bomb);

        [MenuItem("Tools/Airsoft/Play Check: VIP Escort")]
        static void RunVip() => Run(MatchNet.GameModeType.VipEscort);

        static void Run(MatchNet.GameModeType m)
        {
            mode = m; stage = 0; fails = passes = 0;
            log.Clear();
            log.AppendLine("[Airsoft] Play check " + m);
            EditorSceneManager.OpenScene("Assets/Scenes/AirsoftLobby.unity", OpenSceneMode.Single);
            runStart = EditorApplication.timeSinceStartup;
            stageStart = runStart;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Check(bool ok, string what)
        {
            if (ok) passes++; else fails++;
            log.AppendLine((ok ? "  PASS " : "  FAIL ") + what);
        }

        static void Next() { stage++; stageStart = EditorApplication.timeSinceStartup; }
        static double InStage => EditorApplication.timeSinceStartup - stageStart;

        static void Finish(string why = null)
        {
            if (why != null) { fails++; log.AppendLine("  FAIL " + why); }
            log.AppendLine($"  => {passes} passed, {fails} failed");
            if (fails == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
            EditorApplication.update -= Tick;
            int code = fails == 0 ? 0 : 1;
            if (batch) EditorApplication.Exit(code);
            else EditorApplication.isPlaying = false;
        }

        static Transform Me()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        }

        static void Teleport(Vector3 p)
        {
            var me = Me();
            if (me == null) return;
            var cc = me.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            me.position = p + Vector3.up * 0.1f;
            if (cc) cc.enabled = true;
            Physics.SyncTransforms();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - runStart > 600) { Finish("timed out in stage " + stage); return; }
            if (!EditorApplication.isPlaying) return;
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            try
            {
                switch (stage)
                {
                    case 0:   // start a host
                        if (nm == null || InStage < 1) return;
                        // a free port of our own (another editor may already listen on the default 7777)
                        var utp = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                        if (utp != null) utp.SetConnectionData("127.0.0.1", (ushort)UnityEngine.Random.Range(20000, 40000));
                        Check(nm.StartHost(), "host started");
                        Next(); break;
                    case 1:   // mode + ready + start
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        if (mapIndex >= 0) mn.HostLockMap(mapIndex);
                        Check(ObjectiveMode.IsAvailable(mode, out string why), "mode available on " + MatchNet.CurrentMapScene + " (" + (why ?? "ok") + ")");
                        mn.HostSetMode(mode);
                        Check(mn.Mode == mode, "host picked " + mode);
                        Check(mn.TeamMode, "it is a team mode");
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;
                    case 2:
                        if (!mn.AllReady()) { if (InStage > 10) Finish("never ready"); return; }
                        mn.HostStartMatch();
                        Next(); break;
                    case 3:   // arena loaded, objective + avatar up
                        if (SceneManager.GetActiveScene().name != MatchNet.CurrentMapScene || ObjectiveMode.Active == null || Me() == null || InStage < 2)
                        { if (InStage > 60) Finish("arena / objective / player never came up"); return; }
                        Check(ObjectiveMode.Active.Mode == mode, "objective object created for " + mode);
                        Check(MapLayout.Current != null, "arena has a MapLayout (" + (MapLayout.Current ? MapLayout.Current.displayName : "-") + ")");
                        Check(mn.TeamOf(nm.LocalClientId) == 1, "host is on Red");
                        Check(GameObject.Find("ObjectiveMarkers") != null, "HUD built the objective marker layer");
                        Next(); break;
                    default:
                        if (mode == MatchNet.GameModeType.CaptureTheFlag) TickCtf(nm, mn);
                        else if (mode == MatchNet.GameModeType.Bomb) TickBomb(nm, mn);
                        else if (mode == MatchNet.GameModeType.VipEscort) TickVip(nm, mn);
                        else TickDom(nm, mn);
                        break;
                }
            }
            catch (Exception e) { Finish("exception: " + e); }
        }

        // ───────── CTF: take the blue flag, die with it, re-take it, score 3 times ─────────
        static void TickCtf(NetworkManager nm, MatchNet mn)
        {
            var ctf = (CaptureTheFlag)ObjectiveMode.Active;
            var map = MapLayout.Current;
            ulong me = nm.LocalClientId;
            var blue = ctf != null ? ctf.FlagOf(2) : null;
            switch (stage)
            {
                case 4: Teleport(map.blueBase.position); Next(); break;
                case 5:
                    if (blue.state != CaptureTheFlag.FlagState.Carried) { if (InStage > 5) Finish("touching the blue flag didn't pick it up"); return; }
                    Check(blue.carrier == me, "host carries the blue flag");
                    Check(ObjectiveMode.CannotSprint(me), "carrier can't sprint");
                    Teleport(new Vector3(0, 0.2f, -16));
                    Next(); break;
                case 6:
                    if (InStage < 0.5) return;
                    var h = Me().GetComponent<Health>();
                    h.EndSpawnProtection();
                    h.TakeDamage(9999, me, "test");
                    Check(blue.state == CaptureTheFlag.FlagState.Dropped, "carrier killed: flag dropped");
                    Next(); break;
                case 7:
                    if (Me().GetComponent<Health>().isDead.Value) { if (InStage > 10) Finish("never respawned"); return; }
                    Check(blue.state == CaptureTheFlag.FlagState.Dropped, "flag still lying there after the respawn");
                    Next(); break;
                case 8:
                    // keep putting the avatar on the flag (the respawn teleport may land after ours)
                    if (blue.state != CaptureTheFlag.FlagState.Carried) { Teleport(blue.pos); if (InStage > 5) Finish("couldn't re-take the dropped flag"); return; }
                    Check(true, "re-took the dropped flag");
                    Teleport(map.redBase.position);
                    Next(); break;
                case 9:
                    if (mn.TeamScore(1) < 1) { if (InStage > 5) Finish("capture didn't score"); return; }
                    Check(mn.TeamScore(1) == 1 && blue.state == CaptureTheFlag.FlagState.Home, "capture scored (Red 1) and the flag went home");
                    Check(ObjectiveMode.PointsOf(me) == 1, "scoreboard capture point");
                    Next(); break;
                case 10: case 12:
                    Teleport(map.blueBase.position);
                    if (blue.state == CaptureTheFlag.FlagState.Carried) { Teleport(map.redBase.position); Next(); }
                    else if (InStage > 5) Finish("pickup failed");
                    break;
                case 11: case 13:
                    if (mn.TeamScore(1) < (stage == 11 ? 2 : 3)) { if (InStage > 5) Finish("capture failed"); return; }
                    Next(); break;
                case 14:
                    if (mn.CurrentPhase != MatchNet.Phase.Results) { if (InStage > 3) Finish("3 captures didn't end the match"); return; }
                    Check(mn.WinnerTeam == 1 && !mn.IsDraw && !mn.EndedOnTime, "3 captures: RED wins");
                    Finish(); break;
            }
        }

        // ───────── Domination: capture A and B, score ticks up ─────────
        static void TickDom(NetworkManager nm, MatchNet mn)
        {
            var dom = (Domination)ObjectiveMode.Active;
            var map = MapLayout.Current;
            switch (stage)
            {
                case 4:
                    Check(dom.points.Count == 3, "3 control points from the MapLayout");
                    Teleport(map.controlPoints[0].position + Vector3.right * 2f); Next(); break;
                case 5:
                    if (dom.points[0].owner != 1) { if (InStage > Domination.CAPTURE_SECONDS + 4) Finish("A wasn't captured"); return; }
                    Check(InStage >= Domination.CAPTURE_SECONDS - 0.5, $"A captured by Red after {InStage:0.0} s");
                    Check(ObjectiveMode.PointsOf(nm.LocalClientId) == 1, "capture point credited");
                    Teleport(map.controlPoints[1].position); Next(); break;
                case 6:
                    if (dom.points[1].owner != 1) { if (InStage > Domination.CAPTURE_SECONDS + 4) Finish("B wasn't captured"); return; }
                    Check(true, "B captured");
                    Next(); break;
                case 7:
                    if (InStage < Domination.SCORE_INTERVAL * 3 + 0.2) return;
                    int s = mn.TeamScore(1);
                    Check(s >= 5 && mn.TeamScore(2) == 0, $"Red score ticks with 2 points held ({s})");
                    Check(mn.TeamTarget == Domination.SCORE_TARGET, "target 200");
                    Check(mn.CurrentPhase == MatchNet.Phase.InMatch, "match still running");
                    Finish(); break;
            }
        }

        static Health MyHealth() => Me() != null ? Me().GetComponent<Health>() : null;

        static void Kill()
        {
            var h = MyHealth();
            h.EndSpawnProtection();
            h.TakeDamage(9999, NetworkManager.Singleton.LocalClientId, "test");
        }

        // ───────── Bomb (host alone on Red): plant at A, it explodes (and takes the host out), the host
        // stays down until round 2; round 2 the host defends and goes down (defenders eliminated) ─────────
        static void TickBomb(NetworkManager nm, MatchNet mn)
        {
            var bm = (BombMode)ObjectiveMode.Active;
            var map = MapLayout.Current;
            ulong me = nm.LocalClientId;
            switch (stage)
            {
                case 4:
                    if (bm.RoundStage != RoundMode.Stage.Prep) { if (InStage > 10) Finish("round 1 never started"); return; }
                    Check(bm.Round == 1 && bm.AttackTeam == 1, "round 1: Red (host) attacks");
                    Check(bm.State == BombMode.BombState.Carried && bm.Carrier == me, "the host carries the bomb");
                    Check(ObjectiveMode.SpeedFactor(me) == 0f, "frozen in prep");
                    Check(!mn.UsesTimeLimit && mn.TimeLeft < 0f, "no match clock (timed rounds)");
                    Check(bm.sites.Count == 2, "2 bomb sites from the MapLayout");
                    Next(); break;
                case 5:
                    if (bm.RoundStage != RoundMode.Stage.Live) { if (InStage > 10) Finish("round never went live"); return; }
                    Check(ObjectiveMode.SpeedFactor(me) == 1f, "moving after prep");
                    Teleport(map.bombSites[0].position);
                    bm.testUse = id => id == me;   // hold E
                    Next(); break;
                case 6:
                    if (bm.State != BombMode.BombState.Planted) { if (InStage > BombMode.PLANT_SECONDS + 4) Finish("plant failed"); return; }
                    Check(InStage >= BombMode.PLANT_SECONDS - 0.5, $"planted at A after {InStage:0.0} s");
                    Check(bm.PlantedSite == 0 && bm.FuseLeft > BombMode.FUSE_SECONDS - 2f, "planted at site A, fuse running");
                    Check(ObjectiveMode.PointsOf(me) == 1, "plant point");
                    bm.testUse = null;
                    Next(); break;
                case 7:
                    if (bm.State != BombMode.BombState.Exploded) { if (InStage > BombMode.FUSE_SECONDS + 4) Finish("never exploded"); return; }
                    Check(InStage >= BombMode.FUSE_SECONDS - 1.5, $"exploded after {InStage:0.0} s");
                    Check(bm.RoundStage == RoundMode.Stage.Over && bm.RoundWinner == 1 && mn.TeamScore(1) == 1, "explosion: RED wins round 1 (1 : 0)");
                    Next(); break;
                case 8:
                    if (InStage < 0.5) return;
                    Check(MyHealth().isDead.Value, "the blast took out the host");
                    Next(); break;
                case 9:
                    if (InStage < 4) return;
                    Check(MyHealth().isDead.Value && ObjectiveMode.LocalRespawnRule(out float d, out _) && d < 0f, "still down after 4 s (no respawn until the next round)");
                    Next(); break;
                case 10:
                    if (bm.Round != 2) { if (InStage > 10) Finish("round 2 never started"); return; }
                    Check(bm.AttackTeam == 2, "round 2: roles swapped (host defends)");
                    Next(); break;
                case 11:
                    if (InStage < 0.5) return;
                    Check(!MyHealth().isDead.Value, "everyone back for the new round");
                    var zone = PlayerSpawner.GetTeamZone(1);
                    Check(zone.Contains(new Vector3(Me().position.x, zone.center.y, Me().position.z)), "respawned on the Red side");
                    Check(bm.State == BombMode.BombState.Dropped, "no Blue attackers: the bomb lies at their side");
                    Next(); break;
                case 12:
                    if (bm.RoundStage != RoundMode.Stage.Live) { if (InStage > 10) Finish("round 2 never went live"); return; }
                    Kill();
                    Next(); break;
                case 13:
                    if (bm.RoundStage != RoundMode.Stage.Over) { if (InStage > 3) Finish("eliminating the defenders didn't end the round"); return; }
                    Check(bm.RoundWinner == 2 && mn.TeamScore(2) == 1, "defenders eliminated: BLUE wins round 2 (1 : 1)");
                    Check(mn.CurrentPhase == MatchNet.Phase.InMatch && mn.TeamTarget == BombMode.ROUNDS_TO_WIN, "match goes on (first to 4)");
                    Finish(); break;
            }
        }

        // ───────── VIP (host alone on Red): VIP, extracts; round 2 nobody to escort; round 3 VIP again,
        // taken down; round 4 nobody to escort -> Red reaches 3 and wins ─────────
        static void TickVip(NetworkManager nm, MatchNet mn)
        {
            var vm = (VipEscort)ObjectiveMode.Active;
            var map = MapLayout.Current;
            ulong me = nm.LocalClientId;
            var rifle = new WeaponData { weaponName = "AK", model = "AK47" };
            switch (stage)
            {
                case 4:
                    if (vm.RoundStage != RoundMode.Stage.Prep) { if (InStage > 10) Finish("round 1 never started"); return; }
                    Check(vm.Round == 1 && vm.AttackTeam == 1 && vm.Vip == me, "round 1: Red escorts, the host is the VIP");
                    Next(); break;
                case 5:
                    if (InStage < 1) return;
                    Check((Me().position - map.vipStart.position).sqrMagnitude < 4f, "the VIP starts at the VIP start");
                    Check(!ObjectiveMode.WeaponAllowed(me, rifle) && ObjectiveMode.CannotSprint(me), "VIP: no rifles, no sprint");
                    var wc = Me().GetComponent<WeaponController>();
                    Check(wc != null && VipEscort.IsSidearm(wc.CurrentWeapon), "VIP holds a sidearm (" + (wc != null ? wc.CurrentWeaponName : "-") + ")");
                    Next(); break;
                case 6:
                    if (vm.RoundStage != RoundMode.Stage.Live) { if (InStage > 10) Finish("round never went live"); return; }
                    Check(Mathf.Approximately(ObjectiveMode.SpeedFactor(me), VipEscort.VIP_SPEED), "VIP walks slower");
                    Teleport(map.vipExtract.position);
                    vm.testUse = id => id == me;
                    Next(); break;
                case 7:
                    if (vm.RoundStage != RoundMode.Stage.Over) { if (InStage > VipEscort.EXTRACT_SECONDS + 4) Finish("extraction failed"); return; }
                    Check(InStage >= VipEscort.EXTRACT_SECONDS - 0.5, $"extracted after {InStage:0.0} s");
                    Check(vm.RoundWinner == 1 && mn.TeamScore(1) == 1 && ObjectiveMode.PointsOf(me) == 1, "VIP extracted: RED wins round 1");
                    vm.testUse = null;
                    Next(); break;
                case 8:
                    if (vm.Round != 2 || vm.RoundStage != RoundMode.Stage.Live) { if (InStage > 15) Finish("round 2 never went live"); return; }
                    Check(vm.AttackTeam == 2 && vm.Vip == ulong.MaxValue && ObjectiveMode.WeaponAllowed(me, rifle), "round 2: Blue escorts (nobody there), host may use rifles");
                    Next(); break;
                case 9:
                    if (vm.RoundStage != RoundMode.Stage.Over) { if (InStage > 3) Finish("no-VIP round didn't end"); return; }
                    Check(vm.RoundWinner == 1 && mn.TeamScore(1) == 2, "no VIP to escort: RED (hunters) win round 2");
                    Next(); break;
                case 10:
                    if (vm.Round != 3 || vm.RoundStage != RoundMode.Stage.Live) { if (InStage > 15) Finish("round 3 never went live"); return; }
                    Check(vm.Vip == me, "round 3: the host is the VIP again");
                    Kill();
                    Next(); break;
                case 11:
                    if (vm.RoundStage != RoundMode.Stage.Over) { if (InStage > 3) Finish("killing the VIP didn't end the round"); return; }
                    Check(vm.RoundWinner == 2 && mn.TeamScore(2) == 1, "VIP down: BLUE (hunters) win round 3 (2 : 1)");
                    Next(); break;
                case 12:
                    if (InStage < 4) return;
                    Check(MyHealth().isDead.Value, "the VIP stays down");
                    Next(); break;
                case 13:
                    if (mn.CurrentPhase != MatchNet.Phase.Results) { if (InStage > 20) Finish("match never ended"); return; }
                    Check(mn.WinnerTeam == 1 && mn.TeamScore(1) == VipEscort.ROUNDS_TO_WIN && !mn.EndedOnTime, "RED reaches 3 rounds and wins the match");
                    Finish(); break;
            }
        }
    }
}
#endif
