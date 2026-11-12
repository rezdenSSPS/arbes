using UnityEngine;

namespace AirsoftFPS
{
    // NIGHT (lobby toggle, host picks, everyone sees): the match is played at night. The host's pick is
    // saved in PlayerPrefs and sent with the match rules; NightLighting darkens the arena on every
    // machine, and bots / co-op enemies spot players from shorter range (NightSightFactor).
    public partial class MatchNet
    {
        const string NIGHT_PREF = "Airsoft.Night";
        public const float NIGHT_SIGHT = 0.6f;   // bots see 60 % as far at night

        public bool NightMode { get; private set; }

        // True while a night match is loaded or running (not in the lobby).
        public static bool NightNow => Instance != null && Instance.NightMode && Instance.CurrentPhase != Phase.Lobby;
        public static float NightSightFactor => NightNow ? NIGHT_SIGHT : 1f;

        public void HostSetNight(bool on)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby || NightMode == on) return;
            NightMode = on;
            PlayerPrefs.SetInt(NIGHT_PREF, on ? 1 : 0);
            PlayerPrefs.Save();
            BroadcastState();
        }

        void LoadNight() => NightMode = PlayerPrefs.GetInt(NIGHT_PREF, 0) == 1;
    }
}
