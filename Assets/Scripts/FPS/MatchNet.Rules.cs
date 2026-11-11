using System;
using Unity.Netcode;
using UnityEngine;

namespace AirsoftFPS
{
    // Custom match rules the host sets in the lobby (MATCH RULES window): health, move speed, jump,
    // respawn time, lives and infinite ammo. Kept as option indices, saved in the host's PlayerPrefs and
    // sent to everyone with the match state (owners need speed / jump / ammo, the HUD needs the rest).
    // The defaults are the normal game (100 HP, 100 % speed, normal jump, 3 s, unlimited lives, normal ammo).
    public partial class MatchNet
    {
        public enum RuleId { Health, Speed, Jump, Respawn, Lives, Ammo }
        public const int RULE_COUNT = 6;

        public static readonly string[] RULE_TITLES = { "HEALTH", "MOVE SPEED", "JUMP", "RESPAWN TIME", "LIVES", "AMMO" };
        public static readonly int[] HEALTH_OPTIONS = { 50, 100, 150, 200 };        // % of normal HP
        public static readonly int[] SPEED_OPTIONS = { 75, 100, 125, 150 };         // % of normal speed
        public static readonly string[] JUMP_NAMES = { "NORMAL", "HIGH", "MOON" };
        public static readonly int[] RESPAWN_OPTIONS = { 1, 3, 6, 10 };             // seconds
        public static readonly int[] LIVES_OPTIONS = { 0, 1, 3, 5 };                // 0 = unlimited
        public static readonly string[] AMMO_NAMES = { "NORMAL", "INFINITE" };
        static readonly byte[] RULE_DEFAULTS = { 1, 1, 0, 1, 0, 0 };
        const string RULES_PREF = "Airsoft.MatchRules";

        readonly byte[] rules = (byte[])RULE_DEFAULTS.Clone();

        public static string[] RuleOptionNames(RuleId id) => id switch
        {
            RuleId.Health => Array.ConvertAll(HEALTH_OPTIONS, v => v + " HP"),
            RuleId.Speed => Array.ConvertAll(SPEED_OPTIONS, v => v + "%"),
            RuleId.Jump => JUMP_NAMES,
            RuleId.Respawn => Array.ConvertAll(RESPAWN_OPTIONS, v => v + " S"),
            RuleId.Lives => Array.ConvertAll(LIVES_OPTIONS, v => v == 0 ? "∞" : v.ToString()),
            _ => AMMO_NAMES,
        };

        public int Rule(RuleId id) => rules[(int)id];
        public bool RuleIsDefault(RuleId id) => rules[(int)id] == RULE_DEFAULTS[(int)id];
        public bool RulesCustom { get { for (int i = 0; i < RULE_COUNT; i++) if (rules[i] != RULE_DEFAULTS[i]) return true; return false; } }

        // Effective values
        public float HealthFactor => HEALTH_OPTIONS[Rule(RuleId.Health)] / 100f;
        public float RuleSpeedFactor => SPEED_OPTIONS[Rule(RuleId.Speed)] / 100f;
        public float JumpHeightFactor => Rule(RuleId.Jump) switch { 1 => 2f, 2 => 3f, _ => 1f };
        public float GravityFactor => Rule(RuleId.Jump) == 2 ? 0.35f : 1f;   // moon: floaty
        public float RespawnSeconds => RESPAWN_OPTIONS[Rule(RuleId.Respawn)];
        public int Lives => LIVES_OPTIONS[Rule(RuleId.Lives)];
        public bool InfiniteAmmo => Rule(RuleId.Ammo) == 1;

        // Static shortcuts for gameplay code (normal values when there is no match director).
        public static float SpeedRule => Instance != null ? Instance.RuleSpeedFactor : 1f;
        public static float JumpRule => Instance != null ? Instance.JumpHeightFactor : 1f;
        public static float GravityRule => Instance != null ? Instance.GravityFactor : 1f;
        public static bool InfiniteAmmoRule => Instance != null && Instance.InfiniteAmmo;

        // Lives count in the respawn modes where "last one standing" makes sense. Round modes (Bomb, VIP),
        // One in the Chamber (own lives), Infection, Juggernaut, Milsim / Medic (own hit rules) and Co-op don't use it.
        public static bool ModeAllowsLives(GameModeType m) =>
            m == GameModeType.FreeForAll || m == GameModeType.TeamDeathmatch || m == GameModeType.GunGame
            || m == GameModeType.CaptureTheFlag || m == GameModeType.Domination;

        public bool LivesActive => Lives > 0 && ModeAllowsLives(Mode) && CurrentPhase != Phase.Lobby;

        // Lives left for a player or bot (-1 = unlimited / not used). Deaths count from the roster.
        public int LivesLeft(ulong id)
        {
            if (!LivesActive) return -1;
            foreach (var e in roster) if (e.clientId == id) return Mathf.Max(0, Lives - e.deaths);
            return Lives;
        }

        public bool OutOfLives(ulong id) => LivesActive && LivesLeft(id) == 0;

        public void HostSetRule(RuleId id, int index)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby) return;
            int max = RuleOptionNames(id).Length;
            if (index < 0 || index >= max || rules[(int)id] == index) return;
            rules[(int)id] = (byte)index;
            SaveRules();
            BroadcastState();
        }

        public void HostResetRules()
        {
            if (!IsHost || CurrentPhase != Phase.Lobby) return;
            Array.Copy(RULE_DEFAULTS, rules, RULE_COUNT);
            SaveRules();
            BroadcastState();
        }

        void LoadRules()
        {
            string s = PlayerPrefs.GetString(RULES_PREF, "");
            for (int i = 0; i < RULE_COUNT; i++)
            {
                int max = RuleOptionNames((RuleId)i).Length;
                int v = i < s.Length ? s[i] - '0' : RULE_DEFAULTS[i];
                rules[i] = (byte)(v >= 0 && v < max ? v : RULE_DEFAULTS[i]);
            }
        }

        void SaveRules()
        {
            var c = new char[RULE_COUNT];
            for (int i = 0; i < RULE_COUNT; i++) c[i] = (char)('0' + rules[i]);
            PlayerPrefs.SetString(RULES_PREF, new string(c));
            PlayerPrefs.Save();
        }

        void WriteRules(FastBufferWriter w)
        {
            for (int i = 0; i < RULE_COUNT; i++) w.WriteValueSafe(rules[i]);
        }

        void ReadRules(FastBufferReader r)
        {
            for (int i = 0; i < RULE_COUNT; i++)
            {
                r.ReadValueSafe(out byte v);
                rules[i] = v;
            }
        }

        // Server, after a death was counted: with limited lives the match ends when only one player (FFA,
        // Gun Game) or one team (team modes) still has lives.
        void CheckLivesWin()
        {
            if (!LivesActive || CurrentPhase != Phase.InMatch || roster.Count < 2) return;
            if (TeamMode)
            {
                bool red = false, blue = false;
                foreach (var e in roster)
                {
                    if (e.deaths >= Lives) continue;
                    if (e.team == 1) red = true; else if (e.team == 2) blue = true;
                }
                if (red && blue) return;
                EndMatch(ulong.MaxValue, red ? 1 : blue ? 2 : 0, false, !red && !blue);
            }
            else
            {
                int alive = 0; ulong last = ulong.MaxValue;
                foreach (var e in roster) if (e.deaths < Lives) { alive++; last = e.clientId; }
                if (alive > 1) return;
                EndMatch(last, 0, false, alive == 0);
            }
        }
    }
}
