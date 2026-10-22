#if UNITY_EDITOR
using System;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Host-only play-mode check of the Medic mode through the real game flow: lobby -> host ->
    // volunteer + pick Medic -> ready -> START -> arena. Then the host is HIT (real Health path),
    // stays down (no zone respawn, no self-revive), gives up / bleeds out (-1 ticket, Milsim
    // walk-back to the team zone), gets revived through Health.ReviveFromHit, and finally runs
    // out of tickets with everyone out: BLUE wins.
    //
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.MedicPlayCheck.RunBatch [-skipBleed]
    public static class MedicPlayCheck
    {
        static int stage;
        static double stageStart, runStart;
        static int fails, passes;
        static bool batch, skipBleed;
        static int startTickets;
        static int mapIndex = -1;   // -map N: host locks that map (MapCatalog index), else the default
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void RunBatch()
        {
            batch = true;
            var args = Environment.GetCommandLineArgs();
            skipBleed = Array.IndexOf(args, "-skipBleed") >= 0;
            mapIndex = -1;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-map" && int.TryParse(args[i + 1], out int mi)) mapIndex = mi;
            Run();
        }

        [MenuItem("Tools/Airsoft/Play Check: Medic")]
        static void RunMenu() { batch = false; skipBleed = false; mapIndex = -1; Run(); }

        static void Run()
        {
            stage = 0; fails = passes = 0;
            log.Clear();
            log.AppendLine("[Airsoft] Play check Medic");
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
            if (batch) EditorApplication.Exit(fails == 0 ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static Transform Me()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        }

        static Health MyHealth => Me() != null ? Me().GetComponent<Health>() : null;

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

        static Vector3 Zone()
        {
            MilsimZone.TryGet(1, Me().position, out Vector3 c, out _);
            return c;
        }

        static void HitMe()
        {
            var h = MyHealth;
            h.EndSpawnProtection();
            h.TakeDamage(9999, NetworkManager.Singleton.LocalClientId, "test");
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - runStart > 600) { Finish("timed out in stage " + stage); return; }
            if (!EditorApplication.isPlaying) return;
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            try
            {
                var med = Medic.ActiveMedic;
                ulong me = nm != null ? nm.LocalClientId : 0;
                switch (stage)
                {
                    case 0:
                        if (nm == null || InStage < 1) return;
                        Check(nm.StartHost(), "host started");
                        Next(); break;
                    case 1:
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        if (mapIndex >= 0) mn.HostLockMap(mapIndex);
                        Check(ObjectiveMode.IsAvailable(MatchNet.GameModeType.Medic, out string why), "Medic available on " + MatchNet.CurrentMapScene + " (" + (why ?? "ok") + ")");
                        mn.HostSetMode(MatchNet.GameModeType.Medic);
                        Check(mn.Mode == MatchNet.GameModeType.Medic && mn.TeamMode, "host picked Medic (a team mode)");
                        Check(!mn.BotsActive, "no bots in Medic");
                        Medic.SetLocalVolunteer(true);
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;
                    case 2:
                        if (!mn.AllReady() || !Medic.IsVolunteer(me)) { if (InStage > 10) Finish("never ready / volunteer not registered"); return; }
                        Check(true, "host volunteered as medic (lobby)");
                        mn.HostStartMatch();
                        Next(); break;
                    case 3:
                        if (SceneManager.GetActiveScene().name != MatchNet.CurrentMapScene || med == null || Me() == null || InStage < 2)
                        { if (InStage > 60) Finish("arena / objective / player never came up"); return; }
                        Check(mn.TeamOf(me) == 1, "host is on RED");
                        Check(Medic.IsMedic(me), "the volunteer is RED's medic");
                        startTickets = mn.TeamScore(1);
                        Check(startTickets == Medic.TicketsFor(1), $"RED starts with {startTickets} tickets (1 player)");
                        Check(GameObject.Find("ObjectiveMarkers") != null && GameObject.Find("MedicRole") != null, "HUD built the medic widgets");
                        Next(); break;
                    case 4:   // HIT -> down in place
                        if (InStage < 3.5) return;   // spawn protection over
                        Check(GameObject.Find("MedicArmband") != null, "medic armband on the avatar");
                        HitMe();
                        Check(MyHealth.isHit.Value, "HIT rule: one BB = out (no HP loss)");
                        Check(med.StateOf(me) == Medic.CasualtyState.Downed, "HIT player is downed");
                        Check(ObjectiveMode.IsDowned(me) && ObjectiveMode.HoldsHit(me), "downed: can't move, no walk-back respawn");
                        Teleport(Zone());   // even in the team zone ...
                        med.ServerSetHolding(me, true);   // ... and holding E himself
                        Next(); break;
                    case 5:
                        if (InStage < MyHealth.hitRespawnTime + Medic.REVIVE_SECONDS + 0.5) return;
                        Check(MyHealth.isHit.Value && med.StateOf(me) == Medic.CasualtyState.Downed, "still down: no zone respawn while downed, no self-revive");
                        Check(MyHealth.hitRespawnAt.Value == 0, "Milsim respawn countdown held");
                        Check(GameObject.Find("Downed") != null && GameObject.Find("Downed").activeInHierarchy, "downed overlay shown");
                        med.ServerSetHolding(me, false);
                        med.ServerGiveUp(me);
                        Next(); break;
                    case 6:   // gave up -> walk back (-1 ticket)
                        if (med.StateOf(me) != Medic.CasualtyState.WalkingBack) { if (InStage > 2) Finish("give up didn't start the walk-back"); return; }
                        Check(mn.TeamScore(1) == startTickets - 1, $"give up costs a ticket ({mn.TeamScore(1)})");
                        Next(); break;
                    case 7:
                        Teleport(Zone());
                        if (MyHealth.isHit.Value)
                        {
                            if (InStage > 10)
                                Finish($"never respawned in the team zone (state {med.StateOf(me)}, holds {ObjectiveMode.HoldsHit(me)}, respawnAt {MyHealth.hitRespawnAt.Value:0.0}, now {nm.ServerTime.Time:0.0}, outside {MilsimZone.DistanceOutside(1, Me().position, out _):0.0}, pos {Me().position})");
                            return;
                        }
                        Check(true, "respawned in the team zone after the walk-back");
                        Next(); break;
                    case 8:   // revive through Health
                        if (InStage < 3.5) return;
                        Check(med.StateOf(me) == null, "back in: casualty cleared");
                        Teleport(Zone() + Vector3.forward * 12f);
                        HitMe();
                        Next(); break;
                    case 9:
                        if (InStage < 0.5) return;
                        var before = Me().position;
                        MyHealth.ReviveFromHit(Medic.REVIVE_PROTECTION);
                        Check(!MyHealth.isHit.Value && (Me().position - before).sqrMagnitude < 0.01f, "revive: back in at the same spot");
                        Check(MyHealth.SpawnProtectionLeft > 1f && MyHealth.SpawnProtectionLeft <= Medic.REVIVE_PROTECTION + 0.05f, $"short spawn protection ({MyHealth.SpawnProtectionLeft:0.0} s)");
                        Next(); break;
                    case 10:
                        if (InStage < 0.5) return;
                        Check(med.StateOf(me) == null && mn.TeamScore(1) == startTickets - 1, "revive cleared the casualty, no ticket spent");
                        if (skipBleed) { stage = 13; stageStart = EditorApplication.timeSinceStartup; return; }
                        Next(); break;
                    case 11:
                        if (InStage < 2) return;
                        HitMe();
                        Next(); break;
                    case 12:   // bleed out for real
                        if (med.StateOf(me) == Medic.CasualtyState.Downed) { if (InStage > Medic.BLEED_SECONDS + 5) Finish("never bled out"); return; }
                        Check(InStage >= Medic.BLEED_SECONDS - 1 && med.StateOf(me) == Medic.CasualtyState.WalkingBack, $"bled out after {InStage:0.0} s, walking back");
                        Check(mn.TeamScore(1) == startTickets - 2, "bleed-out costs a ticket");
                        Next(); break;
                    case 13:   // drain the tickets: hit, give up, respawn
                        Teleport(Zone());
                        if (MyHealth.isHit.Value) { if (InStage > 15) Finish("drain: no respawn"); return; }
                        if (mn.TeamScore(1) == 0) { Next(); return; }
                        if (MyHealth.IsSpawnProtected && InStage < 0.2) return;
                        HitMe();
                        med.ServerGiveUp(me);
                        stageStart = EditorApplication.timeSinceStartup;
                        break;
                    case 14:   // 0 tickets: out for good -> everyone on RED out -> BLUE wins
                        Check(mn.CurrentPhase == MatchNet.Phase.InMatch, "0 tickets but still in: match goes on");
                        HitMe();
                        med.ServerGiveUp(me);
                        Next(); break;
                    case 15:
                        if (mn.CurrentPhase != MatchNet.Phase.Results) { if (InStage > 3) Finish("out of tickets and players didn't end the match"); return; }
                        Check(med.StateOf(me) == Medic.CasualtyState.Eliminated, "no tickets: out for good");
                        Check(mn.WinnerTeam == 2 && !mn.IsDraw && !mn.EndedOnTime, "RED out of tickets and players: BLUE wins");
                        Finish(); break;
                }
            }
            catch (Exception e) { Finish("exception: " + e); }
        }
    }
}
#endif
