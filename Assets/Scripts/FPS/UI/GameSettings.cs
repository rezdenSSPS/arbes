using UnityEngine;

namespace AirsoftFPS
{
    // Player settings (Controls / Video / Audio / Gameplay), persisted in PlayerPrefs.
    // Gameplay code reads the static values; UI edits them and calls Save().
    public static class GameSettings
    {
        const string P = "Airsoft.Settings.";

        // Controls
        public static float Sensitivity;      // multiplier on FPSController.mouseSensitivity (0.1 – 5)
        public static float ScopedSensitivity; // 0.1 – 1 (fraction of normal while aiming)
        public static bool InvertY;
        public static bool ToggleCrouch;      // true = press to toggle, false = hold

        // Video
        public static int FieldOfView;        // hip-fire FOV, 60 – 110
        public static int ResolutionW, ResolutionH;
        public static bool Fullscreen;
        public static bool VSync;
        public static int Quality;            // 0 low, 1 medium, 2 high
        public static bool PostProcessing;
        public static bool ShowFps;

        // Audio (0 – 1)
        public static float MasterVolume, SfxVolume, MusicVolume, AnnouncerVolume;

        // Gameplay
        public static int CrosshairColor;     // index into CrosshairColors
        public static int CrosshairSize;      // arm length in px, 6 – 24
        public static bool ShowHitMarker;
        public static bool ShowHealthBars;

        public static readonly string[] CrosshairColorNames = { "White", "Blaze", "Green", "Cyan", "Yellow" };
        public static readonly Color[] CrosshairColors =
        {
            UIKit.Hex("#ECEDE6"), UIKit.Hex("#FF7A1A"), UIKit.Hex("#7BD389"), UIKit.Hex("#5FE3E8"), UIKit.Hex("#FFE25A")
        };

        public static Color CrosshairTint => CrosshairColors[Mathf.Clamp(CrosshairColor, 0, CrosshairColors.Length - 1)];

        public static event System.Action Changed;

        static bool loaded;
        static float baseLodBias = -1f;

        // Domain reload is disabled, so drop listeners left over from the last play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEvents() { Changed = null; baseLodBias = -1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            loaded = false;
            Load();
            Apply();
        }

        public static void EnsureLoaded() { if (!loaded) Load(); }

        public static void Load()
        {
            loaded = true;
            Sensitivity       = PlayerPrefs.GetFloat(P + "Sens", 1f);
            ScopedSensitivity = PlayerPrefs.GetFloat(P + "ScopedSens", 0.5f);
            InvertY           = PlayerPrefs.GetInt(P + "InvertY", 0) == 1;
            ToggleCrouch      = PlayerPrefs.GetInt(P + "ToggleCrouch", 1) == 1;

            FieldOfView    = PlayerPrefs.GetInt(P + "Fov", 60);
            ResolutionW    = PlayerPrefs.GetInt(P + "ResW", 0);
            ResolutionH    = PlayerPrefs.GetInt(P + "ResH", 0);
            Fullscreen     = PlayerPrefs.GetInt(P + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            VSync          = PlayerPrefs.GetInt(P + "VSync", 0) == 1;
            Quality        = PlayerPrefs.GetInt(P + "Quality", 2);
            PostProcessing = PlayerPrefs.GetInt(P + "PostFx", 1) == 1;
            ShowFps        = PlayerPrefs.GetInt(P + "ShowFps", 0) == 1;

            MasterVolume    = PlayerPrefs.GetFloat(P + "VolMaster", 0.8f);
            SfxVolume       = PlayerPrefs.GetFloat(P + "VolSfx", 0.9f);
            MusicVolume     = PlayerPrefs.GetFloat(P + "VolMusic", 0.5f);
            AnnouncerVolume = PlayerPrefs.GetFloat(P + "VolVoice", 0.7f);

            CrosshairColor = PlayerPrefs.GetInt(P + "XColor", 0);
            CrosshairSize  = PlayerPrefs.GetInt(P + "XSize", 12);
            ShowHitMarker  = PlayerPrefs.GetInt(P + "HitMarker", 1) == 1;
            ShowHealthBars = PlayerPrefs.GetInt(P + "HealthBars", 1) == 1;
            Clamp();
        }

        static void Clamp()
        {
            Sensitivity = Mathf.Clamp(Sensitivity, 0.1f, 5f);
            ScopedSensitivity = Mathf.Clamp(ScopedSensitivity, 0.1f, 1f);
            FieldOfView = Mathf.Clamp(FieldOfView, 60, 110);
            Quality = Mathf.Clamp(Quality, 0, 2);
            CrosshairColor = Mathf.Clamp(CrosshairColor, 0, CrosshairColors.Length - 1);
            CrosshairSize = Mathf.Clamp(CrosshairSize, 6, 24);
            MasterVolume = Mathf.Clamp01(MasterVolume); SfxVolume = Mathf.Clamp01(SfxVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume); AnnouncerVolume = Mathf.Clamp01(AnnouncerVolume);
        }

        public static void Save()
        {
            Clamp();
            PlayerPrefs.SetFloat(P + "Sens", Sensitivity);
            PlayerPrefs.SetFloat(P + "ScopedSens", ScopedSensitivity);
            PlayerPrefs.SetInt(P + "InvertY", InvertY ? 1 : 0);
            PlayerPrefs.SetInt(P + "ToggleCrouch", ToggleCrouch ? 1 : 0);
            PlayerPrefs.SetInt(P + "Fov", FieldOfView);
            PlayerPrefs.SetInt(P + "ResW", ResolutionW);
            PlayerPrefs.SetInt(P + "ResH", ResolutionH);
            PlayerPrefs.SetInt(P + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(P + "VSync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(P + "Quality", Quality);
            PlayerPrefs.SetInt(P + "PostFx", PostProcessing ? 1 : 0);
            PlayerPrefs.SetInt(P + "ShowFps", ShowFps ? 1 : 0);
            PlayerPrefs.SetFloat(P + "VolMaster", MasterVolume);
            PlayerPrefs.SetFloat(P + "VolSfx", SfxVolume);
            PlayerPrefs.SetFloat(P + "VolMusic", MusicVolume);
            PlayerPrefs.SetFloat(P + "VolVoice", AnnouncerVolume);
            PlayerPrefs.SetInt(P + "XColor", CrosshairColor);
            PlayerPrefs.SetInt(P + "XSize", CrosshairSize);
            PlayerPrefs.SetInt(P + "HitMarker", ShowHitMarker ? 1 : 0);
            PlayerPrefs.SetInt(P + "HealthBars", ShowHealthBars ? 1 : 0);
            PlayerPrefs.Save();
            Apply();
        }

        public static void ResetToDefaults()
        {
            foreach (var k in new[] { "Sens", "ScopedSens", "InvertY", "ToggleCrouch", "Fov", "ResW", "ResH", "Fullscreen",
                         "VSync", "Quality", "PostFx", "ShowFps", "VolMaster", "VolSfx", "VolMusic", "VolVoice",
                         "XColor", "XSize", "HitMarker", "HealthBars" })
                PlayerPrefs.DeleteKey(P + k);
            Load();
        }

        // Push engine-level settings (resolution, vsync, quality, volume).
        public static void Apply()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            AudioListener.volume = MasterVolume;

            int levels = QualitySettings.names.Length;
            if (levels > 0)
            {
                // LOW = the lowest quality level, MEDIUM/HIGH = the highest one (MEDIUM with a lower LOD bias).
                int want = Quality == 0 ? 0 : levels - 1;
                if (QualitySettings.GetQualityLevel() != want || baseLodBias < 0f)
                {
                    QualitySettings.SetQualityLevel(want, true);
                    baseLodBias = QualitySettings.lodBias;   // the level's own value
                }
                QualitySettings.lodBias = baseLodBias * (Quality == 1 ? 0.7f : 1f);
                QualitySettings.vSyncCount = VSync ? 1 : 0;
            }

#if !UNITY_EDITOR
            int w = ResolutionW > 0 ? ResolutionW : Screen.currentResolution.width;
            int h = ResolutionH > 0 ? ResolutionH : Screen.currentResolution.height;
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
                Screen.SetResolution(w, h, mode);
#endif
            Changed?.Invoke();
        }
    }
}
