#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Host-only play-mode check of the lobby MATCH RULES through the real game flow: lobby -> host with 3 bots
    // (bot slider: exact count, clamped to 7)
    // -> FFA -> rules 200 HP, 150 % speed, moon jump, 1 s respawn, 1 life, infinite ammo -> START -> the
    // arena uses them (health, respawn delay, speed / jump factors, the HUD lives chip) -> two bots are
    // eliminated and stay down (out of lives) -> the last bot falls -> the host wins as the last one standing.
    //
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.RulesPlayCheck.RunBatch
    public static class RulesPlayCheck
    {
        const string BOT_FILL_PREF = "Airsoft.Bots";
        const string RULES_PREF = "Airsoft.MatchRules";
        static int stage, savedBotFill, savedMode = -1;
        static string savedRules;
        static double stageStart, runStart;
        static int fails, passes;
        static bool batch;
        static readonly List<Health> bots = new List<Health>();
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void RunBatch() { batch = true; Run(); }

        [MenuItem("Tools/Airsoft/Play Check: Match Rules")]
        static void Run()
        {
            stage = 0; fails = passes = 0;
            savedBotFill = PlayerPrefs.GetInt(BOT_FILL_PREF, 0);
            savedRules = PlayerPrefs.GetString(RULES_PREF, "");
            PlayerPrefs.SetInt(BOT_FILL_PREF, 3);
            PlayerPrefs.Save();
            log.Clear();
            log.AppendLine("[Airsoft] Play check MATCH RULES");
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
            PlayerPrefs.SetString(RULES_PREF, savedRules);   // the host's own rules back
            PlayerPrefs.Save();
            if (batch) EditorApplication.Exit(fails == 0 ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static Health Me()
        {
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            return po != null ? po.GetComponent<Health>() : null;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - runStart > 300) { Finish("timed out in stage " + stage); return; }
            if (!EditorApplication.isPlaying) return;
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            try
            {
                switch (stage)
                {
                    case 0:
                        if (nm == null || InStage < 1) return;
                        var utp = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                        if (utp != null) utp.SetConnectionData("127.0.0.1", (ushort)Random.Range(20000, 40000));
                        Check(nm.StartHost(), "host started");
                        Next(); break;
                    case 1:
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        mn.HostResetRules();
                        Check(!mn.RulesCustom, "reset: the normal rules");
                        Check(mn.HealthFactor == 1f && mn.RuleSpeedFactor == 1f && mn.RespawnSeconds == 3f && mn.Lives == 0 && !mn.InfiniteAmmo,
                              "normal rules = 100 HP, 100 % speed, 3 s, unlimited lives, normal ammo");
                        mn.HostSetMode(MatchNet.GameModeType.FreeForAll);
                        Check(mn.BotsWanted == 3, $"bot slider: 3 bots from the saved count ({mn.BotsWanted})");
                        mn.HostSetBots(12);
                        Check(mn.BotsWanted == MatchNet.BOT_MAX && mn.BotCount == 7, $"bot slider clamps to 7 and the roster fills ({mn.BotCount})");
                        mn.HostSetBots(3);
                        Check(mn.BotCount == 3 && PlayerPrefs.GetInt(BOT_FILL_PREF, -1) == 3, "back to exactly 3 bots, saved for the host");
                        mn.HostSetRule(MatchNet.RuleId.Health, 3);
                        mn.HostSetRule(MatchNet.RuleId.Speed, 3);
                        mn.HostSetRule(MatchNet.RuleId.Jump, 2);
                        mn.HostSetRule(MatchNet.RuleId.Respawn, 0);
                        mn.HostSetRule(MatchNet.RuleId.Lives, 1);
                        mn.HostSetRule(MatchNet.RuleId.Ammo, 1);
                        Check(mn.RulesCustom, "custom rules set");
                        Check(mn.HealthFactor == 2f && mn.RuleSpeedFactor == 1.5f && mn.JumpHeightFactor == 3f && mn.GravityFactor < 1f
                              && mn.RespawnSeconds == 1f && mn.Lives == 1 && mn.InfiniteAmmo, "200 HP, 150 %, moon jump, 1 s, 1 life, infinite ammo");
                        Check(PlayerPrefs.GetString(RULES_PREF, "") == "332011", "rules saved for the host (" + PlayerPrefs.GetString(RULES_PREF, "") + ")");
                        Check(!mn.LivesActive && mn.LivesLeft(nm.LocalClientId) < 0, "lives not counted in the lobby");
                        Check(MatchNet.ModeAllowsLives(MatchNet.GameModeType.FreeForAll) && !MatchNet.ModeAllowsLives(MatchNet.GameModeType.Bomb)
                              && !MatchNet.ModeAllowsLives(MatchNet.GameModeType.Coop), "lives: FFA yes, Bomb / Co-op no");
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;
                    case 2:
                        if (!mn.AllReady()) { if (InStage > 10) Finish("never ready"); return; }
                        mn.HostStartMatch();
                        Next(); break;
                    case 3:   // arena up, bots spawned
                    {
                        if (SceneManager.GetActiveScene().name != MatchNet.CurrentMapScene || Me() == null || mn.MatchGoTime <= 0 || InStage < 3)
                        { if (InStage > 60) Finish("arena / player never came up"); return; }
                        bots.Clear();
                        foreach (var h in Object.FindObjectsByType<Health>(FindObjectsSortMode.None)) if (h.IsBot) bots.Add(h);
                        if (bots.Count < 3) { if (InStage > 30) Finish("bots never spawned (" + bots.Count + ")"); return; }
                        var me = Me();
                        Check(me.maxHealth == 200 && me.currentHealth.Value == 200, $"player has 200 HP ({me.currentHealth.Value}/{me.maxHealth})");
                        Check(bots.TrueForAll(b => b.maxHealth == 200), "bots have 200 HP");
                        Check(Mathf.Approximately(me.respawnDelay, 1f), "respawn delay 1 s");
                        Check(Mathf.Approximately(MatchNet.SpeedRule, 1.5f), "move speed factor 1.5 for the owner");
                        Check(MatchNet.JumpRule == 3f && MatchNet.GravityRule < 1f, "moon jump for the owner");
                        Check(MatchNet.InfiniteAmmoRule, "infinite ammo for the owner");
                        Check(mn.LivesActive && mn.LivesLeft(nm.LocalClientId) == 1, "1 life left");
                        var chip = GameObject.Find("Lives");
                        Check(chip != null && chip.activeInHierarchy, "HUD shows the lives chip");
                        // two bots fall (spawn protection off: bots that came in late would shrug off the hit)
                        foreach (var b in bots) b.spawnProtectedUntil.Value = 0;
                        bots[0].TakeDamage(9999, nm.LocalClientId, "check");
                        bots[1].TakeDamage(9999, nm.LocalClientId, "check");
                        Next(); break;
                    }
                    case 4:   // they stay down: out of lives
                        if (InStage < 2.5) return;
                        Check(bots[0].isDead.Value && bots[1].isDead.Value, "eliminated bots stay down after the 1 s respawn");
                        Check(mn.OutOfLives(bots[0].MatchId) && mn.OutOfLives(bots[1].MatchId), "they are out of lives");
                        Check(ObjectiveMode.HoldsRespawn(bots[0].MatchId), "respawn held for them");
                        Check(mn.CurrentPhase == MatchNet.Phase.InMatch, "match goes on with 2 players left");
                        bots[2].TakeDamage(9999, nm.LocalClientId, "check");
                        Next(); break;
                    case 5:   // last one standing
                        if (mn.CurrentPhase != MatchNet.Phase.Results) { if (InStage > 5) Finish("match did not end with one player left"); return; }
                        Check(mn.WinnerClientId == nm.LocalClientId, "host wins as the last one standing");
                        Finish();
                        break;
                }
            }
            catch (System.Exception e) { Finish("exception: " + e); }
        }
    }
}
#endif
