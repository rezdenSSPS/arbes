#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Host-only play-mode check of the game audio through the real flow (lobby -> host -> Milsim with
    // bots -> arena). Checks the mixer and its volume parameters against Settings > Audio, the menu
    // music, UI sounds, then in the match: start sting, ambience, footsteps from movement, a jump and a
    // landing, a shot with its BB impact, reload, weapon switch, the Milsim "HIT!" call; the bots add
    // remote shots / steps / impacts (reported, not required: they may not meet the host in time).
    //
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.AudioPlayCheck.RunBatch
    public static class AudioPlayCheck
    {
        const string BOT_FILL_PREF = "Airsoft.BotFill";   // MatchNet.Bots; restored afterwards
        static int stage, fails, passes;
        static double stageStart, runStart;
        static bool batch;
        static bool hadBotPref;
        static int oldBotPref;
        static float moveTime;
        static Dictionary<string, int> before;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void RunBatch() { batch = true; Run(); }

        // -audioReport on any batch run (e.g. the objective play checks): list the cues that played on exit.
        [InitializeOnLoadMethod]
        static void HookReport()
        {
            if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-audioReport") < 0) return;
            EditorApplication.quitting += () =>
            {
                var cues = new List<string>();
                foreach (var kv in AirsoftAudio.Played) cues.Add(kv.Key + " x" + kv.Value);
                cues.Sort(StringComparer.Ordinal);
                Debug.Log("[Airsoft] audio report: " + string.Join(", ", cues));
            };
        }

        [MenuItem("Tools/Airsoft/Play Check: Audio")]
        static void RunMenu() { batch = false; Run(); }

        static void Run()
        {
            stage = 0; fails = passes = 0;
            log.Clear();
            log.AppendLine("[Airsoft] Audio play check");
            hadBotPref = PlayerPrefs.HasKey(BOT_FILL_PREF);
            oldBotPref = PlayerPrefs.GetInt(BOT_FILL_PREF, 0);
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

        static void Info(string what) => log.AppendLine("  info " + what);

        static void Next() { stage++; stageStart = EditorApplication.timeSinceStartup; }
        static double InStage => EditorApplication.timeSinceStartup - stageStart;

        static int N(string cue) => AirsoftAudio.Played.TryGetValue(cue, out int n) ? n : 0;

        static int Prefix(string prefix)
        {
            int n = 0;
            foreach (var kv in AirsoftAudio.Played) if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) n += kv.Value;
            return n;
        }

        static int Since(string prefix)
        {
            int was = 0;
            if (before != null) foreach (var kv in before) if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) was += kv.Value;
            return Prefix(prefix) - was;
        }

        static void Mark() => before = new Dictionary<string, int>(AirsoftAudio.Played);

        static void Finish(string why = null)
        {
            if (why != null) { fails++; log.AppendLine("  FAIL " + why); }
            var cues = new List<string>();
            foreach (var kv in AirsoftAudio.Played) cues.Add(kv.Key + " x" + kv.Value);
            cues.Sort(StringComparer.Ordinal);
            log.AppendLine("  played: " + string.Join(", ", cues));
            log.AppendLine($"  => {passes} passed, {fails} failed");
            if (fails == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
            EditorApplication.update -= Tick;
            GameSettings.Load();   // nothing was saved; drop the test values
            if (hadBotPref) PlayerPrefs.SetInt(BOT_FILL_PREF, oldBotPref); else PlayerPrefs.DeleteKey(BOT_FILL_PREF);
            PlayerPrefs.Save();
            if (batch) EditorApplication.Exit(fails == 0 ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static Transform Me()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        }

        static bool VolumeIs(string param, float slider)
        {
            return AirsoftAudio.Mixer.GetFloat(param, out float db) && Mathf.Abs(db - AirsoftAudio.ToDb(slider)) < 0.05f;
        }

        static void CheckVolumes(string when)
        {
            GameSettings.MasterVolume = 0.5f; GameSettings.SfxVolume = 0.25f;
            GameSettings.MusicVolume = 0f; GameSettings.AnnouncerVolume = 1f;
            AirsoftAudio.ApplyVolumes();
            Check(VolumeIs(AirsoftAudio.P_MASTER, 0.5f) && VolumeIs(AirsoftAudio.P_SFX, 0.25f) && VolumeIs(AirsoftAudio.P_MUSIC, 0f)
                  && VolumeIs(AirsoftAudio.P_UI, 1f), when + ": mixer volumes follow the settings (50% -> -12 dB, 25% -> -24 dB, 0 -> -80 dB, 100% -> 0 dB)");
            GameSettings.Load();
            AirsoftAudio.ApplyVolumes();
            Check(VolumeIs(AirsoftAudio.P_MASTER, GameSettings.MasterVolume) && VolumeIs(AirsoftAudio.P_MUSIC, GameSettings.MusicVolume),
                  when + ": saved settings restored on the mixer");
        }

        static object Invoke(object target, string method, params object[] args)
        {
            foreach (var m in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == method && m.GetParameters().Length >= args.Length)
                {
                    var ps = m.GetParameters();
                    var full = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                        full[i] = i < args.Length ? args[i]
                                : ps[i].DefaultValue is DBNull || ps[i].DefaultValue == Type.Missing ? null : ps[i].DefaultValue;
                    return m.Invoke(target, full);
                }
            throw new MissingMethodException(target.GetType().Name, method);
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - runStart > 300) { Finish("timed out in stage " + stage); return; }
            if (!EditorApplication.isPlaying) return;
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            try
            {
                // the bots shoot back: keep the host protected while it walks / fires / reloads (Milsim: a
                // HIT player can't use the gun), until the stage that tests the HIT call itself
                if (stage >= 5 && stage <= 11 && Me() != null)
                {
                    var hp = Me().GetComponent<Health>();
                    if (hp.IsSpawned && !hp.IsOut) hp.spawnProtectedUntil.Value = nm.ServerTime.Time + 30;
                }
                switch (stage)
                {
                    case 0:   // menu: mixer, clips, music, UI
                        if (InStage < 1.5) return;
                        var cfg = AudioSettings.GetConfiguration();
                        Info($"audio output {AudioSettings.outputSampleRate} Hz, {cfg.speakerMode}, dsp buffer {cfg.dspBufferSize}");
                        Check(GameObject.Find("~AirsoftAudio") != null, "audio hub is up (DontDestroyOnLoad)");
                        Check(AirsoftAudio.HasMixer, "mixer loaded from Resources/AirsoftAudio/AirsoftMixer");
                        Check(AirsoftAudio.BankCount >= 50, "sound banks loaded: " + AirsoftAudio.BankCount);
                        Check(AirsoftAudio.CurrentMusic == "music_menu", "menu music selected in the lobby scene (" + AirsoftAudio.CurrentMusic + ")");
                        Info("menu music source playing: " + AirsoftAudio.MusicPlaying);
                        Check(Mathf.Approximately(AudioListener.volume, 1f), "AudioListener.volume left at 1 (the mixer does the master volume)");
                        CheckVolumes("menu");
                        // UI: a KitButton (hover + click) and a check row (click)
                        var canvasGo = new GameObject("AudioCheckCanvas", typeof(Canvas));
                        var btn = UIKit.Button(canvasGo.transform, "TEST", UIKit.Btn.Secondary, 16, null);
                        var row = UIKit.CheckRow(canvasGo.transform, "Test", false, null);
                        var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                        int hov = N("ui_hover"), clk = N("ui_click"), tog = N("ui_toggle");
                        ExecuteEvents.Execute(btn.gameObject, ev, ExecuteEvents.pointerEnterHandler);
                        ExecuteEvents.Execute(btn.gameObject, ev, ExecuteEvents.pointerClickHandler);
                        ExecuteEvents.Execute(row.gameObject, ev, ExecuteEvents.pointerClickHandler);
                        Check(N("ui_hover") == hov + 1, "button hover sound");
                        Check(N("ui_click") == clk + 1, "button click sound");
                        Check(N("ui_toggle") == tog + 1, "check row click sound");
                        UnityEngine.Object.Destroy(canvasGo);
                        Next(); break;
                    case 1:   // host
                        if (nm == null) return;
                        var utp = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                        if (utp != null) utp.SetConnectionData("127.0.0.1", (ushort)UnityEngine.Random.Range(20000, 40000));
                        Check(nm.StartHost(), "host started");
                        Next(); break;
                    case 2:   // Milsim with bots, ready
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        mn.HostSetMode(MatchNet.GameModeType.Milsim);
                        mn.HostSetBotFill(4);
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;
                    case 3:
                        if (!mn.AllReady()) { if (InStage > 10) Finish("never ready"); return; }
                        Check(N("ui_ready") >= 1, "ready chime");
                        mn.HostStartMatch();
                        Next(); break;
                    case 4:   // arena up
                        if (SceneManager.GetActiveScene().name != MatchNet.CurrentMapScene || Me() == null || InStage < 2)
                        { if (InStage > 60) Finish("arena / player never came up"); return; }
                        Check(N("match_start") >= 1 && N("whistle") >= 1, "match start sting + whistle");
                        string amb = MatchNet.CurrentMapScene.Contains("Warehouse") ? "amb_indoor" : "amb_outdoor";
                        Check(!GameSettings.Ambience || AirsoftAudio.CurrentMusic == amb, "ambience in the match (" + AirsoftAudio.CurrentMusic + ")");
                        Check(Me().GetComponent<FootstepAudio>() != null, "player has footsteps");
                        CheckVolumes("match");
                        Mark();
                        moveTime = 0f;
                        Next(); break;
                    case 5:   // walk around for 3 s (turning every second in case a wall is in the way)
                    {
                        var me = Me();
                        var cc = me.GetComponent<CharacterController>();
                        float dt = Mathf.Min(Time.deltaTime, 0.05f);
                        moveTime += dt;
                        var dir = Quaternion.Euler(0, 90f * Mathf.Floor(moveTime), 0) * me.forward;
                        if (cc != null && cc.enabled) cc.Move(dir * 5.5f * dt + Vector3.down * 2f * dt);
                        if (moveTime < 3f) return;
                        Check(Since("step_") >= 3, "footsteps while walking: " + Since("step_"));
                        // jump: up 2.5 m, then fall back down
                        Mark();
                        if (cc) cc.enabled = false;
                        me.position += Vector3.up * 2.5f;
                        if (cc) cc.enabled = true;
                        Next(); break;
                    }
                    case 6:
                        if (InStage < 2.5) return;
                        Check(Since("jump") >= 1, "jump sound (left the ground going up)");
                        Check(Since("land") >= 1, "landing sound after the fall");
                        Next(); break;
                    case 7:   // shoot at the ground in front, reload, switch
                    {
                        var me = Me();
                        var wc = me.GetComponent<WeaponController>();
                        var fps = me.GetComponent<FPSController>();
                        if (fps.cameraHolder) fps.cameraHolder.localRotation = Quaternion.Euler(55f, 0, 0);
                        Mark();
                        Invoke(wc, "FireBBs", wc.CurrentWeapon);
                        Check(Since("shot_" + AirsoftAudio.ShotKind(wc.CurrentWeapon)) == 1, "own shot: shot_" + AirsoftAudio.ShotKind(wc.CurrentWeapon));
                        Next(); break;
                    }
                    case 8:
                        if (InStage < 1) return;
                        Check(Since("impact_") >= 1, "BB impact sound");
                        Mark();
                        Invoke(Me().GetComponent<WeaponController>(), "StartReload");
                        Check(Since("reload_out") == 1, "reload: mag out");
                        Next(); break;
                    case 9:
                    {
                        var wc = Me().GetComponent<WeaponController>();
                        if (wc.IsReloading) { if (InStage > 6) Finish("reload never finished"); return; }
                        Check(Since("reload_in") == 1, "reload: mag in");
                        Check(AirsoftAudio.ShotKind(wc.CurrentWeapon) == "aeg" || Since("reload_rack") == 1, "reload: slide / bolt (gas & spring guns)");
                        Mark();
                        Invoke(wc, "SwitchWeaponRpc", (wc.CurrentSlot % 10));
                        Next(); break;
                    }
                    case 10:   // shoot a bot of the other team point-blank: hit marker + kill (Milsim: hit) confirm
                    {
                        if (InStage < 1) return;
                        Check(Since("switch") >= 1, "weapon switch sound");
                        Health target = null;
                        int myTeam = mn.TeamOf(nm.LocalClientId);
                        foreach (var hb in UnityEngine.Object.FindObjectsByType<Health>())
                            if (hb.IsBot && hb.IsSpawned && !hb.isDead.Value && !hb.IsOut && mn.TeamOf(hb.MatchId) != myTeam) { target = hb; break; }
                        if (target == null) { Info("no enemy bot to shoot at"); stage = 13; stageStart = EditorApplication.timeSinceStartup; return; }
                        target.EndSpawnProtection();
                        var me = Me();
                        var cc = me.GetComponent<CharacterController>();
                        Vector3 at = target.transform.position + target.transform.forward * 2.5f;
                        if (cc) cc.enabled = false;
                        me.position = at;
                        if (cc) cc.enabled = true;
                        Physics.SyncTransforms();
                        var fps = me.GetComponent<FPSController>();
                        Vector3 flat = target.transform.position - at; flat.y = 0;
                        me.rotation = Quaternion.LookRotation(flat);
                        Vector3 eye = fps.cameraHolder.position;
                        Vector3 chest = target.transform.position + Vector3.up * 1.3f;
                        float pitch = -Mathf.Atan2(chest.y - eye.y, flat.magnitude) * Mathf.Rad2Deg;
                        fps.cameraHolder.localRotation = Quaternion.Euler(pitch, 0, 0);
                        Mark();
                        var wc = me.GetComponent<WeaponController>();
                        Invoke(wc, "FireBBs", wc.CurrentWeapon);
                        Next(); break;
                    }
                    case 11:
                        if ((Since("hitmarker") < 1 || Since("killconfirm") < 1) && InStage < 3) return;
                        Check(Since("hitmarker") >= 1, "hit marker tick");
                        Check(Since("killconfirm") >= 1, "hit confirm (Milsim: a hit is an elimination)");
                        Check(Since("hit_call") >= 1, "the bot called \"HIT!\"");
                        // Milsim: a BB hit = HIT! call
                        Mark();
                        var h = Me().GetComponent<Health>();
                        h.EndSpawnProtection();
                        h.TakeDamage(10, nm.LocalClientId, "test");
                        Next(); break;
                    case 12:
                        if (InStage < 0.5) return;
                        Check(Me().GetComponent<Health>().IsOut, "host is HIT");
                        Check(Since("hit_call") >= 1, "\"HIT!\" call");
                        Check(Since("hurt") >= 1, "hit feedback for the victim");
                        Mark();
                        Next(); break;
                    case 13:   // let the bots play for a while
                        if (InStage < 15) return;
                        Info("bots, 15 s: shots " + Since("shot_") + ", steps " + Since("step_") + ", impacts " + Since("impact_")
                             + ", HIT calls " + Since("hit_call") + ", hit markers " + Since("hitmarker"));
                        Check(Since("step_") > 0, "bot footsteps heard");
                        Finish(); break;
                }
            }
            catch (Exception e) { Finish("exception: " + e); }
        }
    }
}
#endif
