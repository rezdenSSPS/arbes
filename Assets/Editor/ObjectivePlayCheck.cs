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
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.ObjectivePlayCheck.RunBatch -objMode CTF|DOM
    // (relies on Enter Play Mode Options with domain reload off, as set in this project)
    public static class ObjectivePlayCheck
    {
        static MatchNet.GameModeType mode;
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
            Run(m == "DOM" ? MatchNet.GameModeType.Domination : MatchNet.GameModeType.CaptureTheFlag);
        }

        [MenuItem("Tools/Airsoft/Play Check: Capture the Flag")]
        static void RunCtf() => Run(MatchNet.GameModeType.CaptureTheFlag);

        [MenuItem("Tools/Airsoft/Play Check: Domination")]
        static void RunDom() => Run(MatchNet.GameModeType.Domination);

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
                        Check(nm.StartHost(), "host started");
                        Next(); break;
                    case 1:   // mode + ready + start
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        Check(ObjectiveMode.IsAvailable(mode, out string why), "mode available on the arena map (" + (why ?? "ok") + ")");
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
                        if (mode == MatchNet.GameModeType.CaptureTheFlag) TickCtf(nm, mn); else TickDom(nm, mn);
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
    }
}
#endif
