#if UNITY_EDITOR
using System;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Host-only play-mode check of RANDOM MODE through the real game flow (3 bots so every mode
    // can be drawn): lobby -> host -> M cycles to RANDOM MODE -> START -> the arena runs a real mode the map
    // can play -> back to the lobby (still RANDOM) -> RANDOM MAP too -> START -> a different mode on a map
    // that can play it.
    //
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.RandomModePlayCheck.RunBatch
    // (relies on Enter Play Mode Options with domain reload off, as set in this project)
    public static class RandomModePlayCheck
    {
        const string BOT_FILL_PREF = "Airsoft.Bots";
        static int stage, savedBotFill, round;
        static double stageStart, runStart;
        static int fails, passes;
        static bool batch;
        static MatchNet.GameModeType firstMode;
        static int firstMap;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void RunBatch() { batch = true; Run(); }

        [MenuItem("Tools/Airsoft/Play Check: Random Mode")]
        static void Run()
        {
            stage = 0; round = 0; fails = passes = 0;
            savedBotFill = PlayerPrefs.GetInt(BOT_FILL_PREF, 0);
            PlayerPrefs.SetInt(BOT_FILL_PREF, 3);   // 3 bots: random may draw any mode (alone = Co-op only)
            PlayerPrefs.Save();
            log.Clear();
            log.AppendLine("[Airsoft] Play check RANDOM MODE");
            EditorSceneManager.OpenScene("Assets/Scenes/AirsoftLobby.unity", OpenSceneMode.Single);
            runStart = stageStart = EditorApplication.timeSinceStartup;
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
            PlayerPrefs.SetInt(BOT_FILL_PREF, savedBotFill);
            PlayerPrefs.Save();
            if (batch) EditorApplication.Exit(fails == 0 ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static Transform Me()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
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
                    case 0:   // start a host on a free port of our own
                        if (nm == null || InStage < 1) return;
                        var utp = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                        if (utp != null) utp.SetConnectionData("127.0.0.1", (ushort)UnityEngine.Random.Range(20000, 40000));
                        Check(nm.StartHost(), "host started");
                        Next(); break;

                    case 1:   // pick a mode, then M cycles through every mode to RANDOM MODE
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        // the lobby screen, as after HOST ON LAN (the check started the host itself)
                        var ui = UnityEngine.Object.FindAnyObjectByType<NetworkUI>();
                        if (ui != null) typeof(NetworkUI).GetMethod("EnterWaitingRoom", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, new object[] { true });
                        mn.HostLockMap(0);   // round 1 on Town
                        mn.HostSetMode(MatchNet.GameModeType.TeamDeathmatch);
                        Check(!mn.RandomMode && mn.Mode == MatchNet.GameModeType.TeamDeathmatch, "host picked TDM");
                        mn.HostSetRandomMode();
                        Check(mn.RandomMode && mn.RandomModePending, "host picked RANDOM MODE");
                        mn.HostCycleMode();
                        Check(!mn.RandomMode && mn.Mode == MatchNet.GameModeType.Coop, "M after RANDOM: Co-op");
                        for (int i = 1; i < MatchNet.MODE_COUNT; i++) mn.HostCycleMode();
                        Check(!mn.RandomMode && mn.Mode == (MatchNet.GameModeType)(MatchNet.MODE_COUNT - 1), "M through every mode: " + mn.Mode);
                        mn.HostCycleMode();
                        Check(mn.RandomMode, "M after the last mode: RANDOM MODE again");
                        Check(mn.RandomModeAvailable(out string why), "random is available (" + (why ?? "ok") + ")");
                        Check(mn.BotsActive, "lobby with RANDOM: bots may fill the slots");
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;

                    case 2:   // bots in, everyone ready: START draws the mode
                        if (mn.Roster.Count < 4 || !mn.AllReady()) { if (InStage > 15) Finish("never 4 ready (" + mn.Roster.Count + ")"); return; }
                        CheckLobbyUi(true);
                        mn.HostStartMatch();
                        Check(mn.CurrentPhase == MatchNet.Phase.InMatch, "match started");
                        Check(mn.RandomMode && !mn.RandomModePending, "in the match: random flag kept, nothing pending");
                        Check(ObjectiveMode.IsAvailableOn(mn.Mode, mn.MatchScene, out _), $"drawn {mn.Mode} is available on {mn.MatchScene}");
                        if (!MatchNet.ModeHasBots(mn.Mode)) Check(mn.BotCount == 0, "drawn mode without bots: the bots left");
                        firstMode = mn.Mode;
                        firstMap = mn.MatchMapIndex;
                        log.AppendLine($"  round 1: {mn.Mode} on {MapCatalog.NameOf(mn.MatchMapIndex)}");
                        Next(); break;

                    case 3:   // arena up with the drawn mode
                    case 6:
                        if (SceneManager.GetActiveScene().name != mn.MatchScene || Me() == null || InStage < 2)
                        { if (InStage > 60) Finish("arena / player never came up"); return; }
                        bool obj = ObjectiveMode.HasObjective(mn.Mode);
                        if (obj && ObjectiveMode.Active == null) { if (InStage > 20) Finish("objective never came up"); return; }
                        Check(!obj || ObjectiveMode.Active.Mode == mn.Mode, $"arena {SceneManager.GetActiveScene().name} runs {mn.Mode}" + (obj ? " (objective up)" : ""));
                        Check(MatchNet.EnemiesEnabled == (mn.Mode == MatchNet.GameModeType.Coop), "AI enemies only in Co-op");
                        if (stage == 6) { Finish(); return; }
                        // back to the lobby (as after the results)
                        typeof(MatchNet).GetMethod("ReturnToLobby", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mn, null);
                        Next(); break;

                    case 4:   // lobby again: still RANDOM
                        if (SceneManager.GetActiveScene().name != "AirsoftLobby" || mn.CurrentPhase != MatchNet.Phase.Lobby || InStage < 2)
                        { if (InStage > 60) Finish("never back in the lobby"); return; }
                        Check(mn.RandomMode && mn.RandomModePending, "back in the lobby: RANDOM MODE still picked");
                        mn.HostLockMap(MapCatalog.RANDOM);   // round 2: RANDOM MAP + RANDOM MODE
                        Check(mn.RandomMapPending, "host locked RANDOM MAP");
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;

                    case 5:
                        if (mn.Roster.Count < 4 || !mn.AllReady()) { if (InStage > 15) Finish("never 4 ready again"); return; }
                        CheckLobbyUi(true);
                        mn.HostStartMatch();
                        Check(mn.CurrentPhase == MatchNet.Phase.InMatch, "match 2 started");
                        Check(mn.Mode != firstMode, $"match 2 draws another mode ({firstMode} -> {mn.Mode})");
                        Check(mn.MatchMapIndex != firstMap, $"random map: another map ({MapCatalog.NameOf(firstMap)} -> {MapCatalog.NameOf(mn.MatchMapIndex)})");
                        Check(ObjectiveMode.IsAvailableOn(mn.Mode, mn.MatchScene, out _), $"drawn pair valid: {mn.Mode} on {mn.MatchScene}");
                        log.AppendLine($"  round 2: {mn.Mode} on {MapCatalog.NameOf(mn.MatchMapIndex)}");
                        Next(); break;
                }
            }
            catch (Exception e) { Finish("exception: " + e); }
        }

        // The lobby shows the RANDOM MODE card picked and no real mode card picked.
        static void CheckLobbyUi(bool random)
        {
            var ui = UnityEngine.Object.FindAnyObjectByType<NetworkUI>();
            if (ui == null) { Check(false, "lobby UI found"); return; }
            var f = typeof(NetworkUI).GetField("randomModeFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            var frame = f != null ? f.GetValue(ui) as UnityEngine.UI.Image : null;
            Check(frame != null && (frame.color == UIKit.Accent) == random, "lobby: RANDOM MODE card " + (random ? "framed as picked" : "not picked"));
        }
    }
}
#endif
