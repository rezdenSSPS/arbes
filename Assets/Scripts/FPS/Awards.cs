using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace AirsoftFPS
{
    // Post-match awards ("GHOST · went the whole match without being hit"). The server watches the match
    // (hits, kills, bangs, goggle wipes, juggernaut takedowns), picks up to MAX_AWARDS when it ends and
    // sends them with the match state (MatchNet.Awards), so the results screen and every player's profile
    // see them in the same update. Each award gives its holder a small XP and Credits bonus
    // (PlayerProfile.Commit). No awards in co-op.
    public static class MatchAwards
    {
        public enum Id : byte { Ghost, Rambo, BangMaster, Sharpshooter, PatientZero, LastSurvivor, GiantSlayer, ClearEyes }

        public struct Award
        {
            public Id id;
            public ulong clientId;
            public int value;   // streak, bangs, metres, infections, takedowns, kills (per award)
        }

        public const int MAX_AWARDS = 4;
        public const int XP = 25;
        public const int CREDITS = 40;

        // thresholds
        const int RAMBO_STREAK = 4;
        const int BANG_MIN = 2;
        const float SHARP_MIN_METRES = 25f;
        const int GHOST_MIN_KILLS = 2;
        const int CLEAR_MIN_KILLS = 3;
        const int PATIENT_ZERO_MIN = 2;

        class Stats
        {
            public int hitsTaken, kills, streak, bestStreak, bangs, takedowns, wipes;
            public float longest;
        }

        static readonly Dictionary<ulong, Stats> stats = new Dictionary<ulong, Stats>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => stats.Clear();

        static Stats Of(ulong id)
        {
            if (!stats.TryGetValue(id, out var s)) stats[id] = s = new Stats();
            return s;
        }

        // ───────── server: recording ─────────
        public static void Reset() => stats.Clear();

        // Health.TakeDamage: a combatant was hit (BB or bang) by attacker.
        public static void RecordHit(ulong victim, ulong attacker, string weapon, Vector3 victimPos)
        {
            var mn = MatchNet.Instance;
            if (mn == null || mn.CurrentPhase != MatchNet.Phase.InMatch) return;
            Of(victim).hitsTaken++;
            if (attacker == victim || string.IsNullOrEmpty(weapon) || weapon == "Bang!") return;
            var t = Body(attacker);
            if (t == null) return;
            float d = Vector3.Distance(t.position, victimPos);
            var s = Of(attacker);
            if (d > s.longest) s.longest = d;
        }

        // MatchNet.ReportKill / ReportHit: victim is out; scored = it counts for the killer.
        public static void RecordKill(ulong victim, ulong killer, string weapon, bool scored)
        {
            var mn = MatchNet.Instance;
            if (mn == null || mn.CurrentPhase != MatchNet.Phase.InMatch) return;
            Of(victim).streak = 0;
            if (!scored) return;
            var s = Of(killer);
            s.kills++;
            s.streak++;
            if (s.streak > s.bestStreak) s.bestStreak = s.streak;
            if (weapon == "Bang!") s.bangs++;
        }

        // Juggernaut: killer took the juggernaut down.
        public static void RecordTakedown(ulong killer) => Of(killer).takedowns++;

        // A player wiped their goggles (MatchNet message from GoggleFog).
        public static void RecordWipe(ulong id) => Of(id).wipes++;

        static Transform Body(ulong id)
        {
            var nm = NetworkManager.Singleton;
            if (MatchNet.IsBotId(id)) return ObjectiveMode.ResolveExtraTransform?.Invoke(id);
            var no = nm != null && nm.IsServer ? nm.SpawnManager?.GetPlayerNetworkObject(id) : null;
            return no != null ? no.transform : null;
        }

        // ───────── server: picking the awards when the match ends ─────────
        public static List<Award> Decide(MatchNet mn)
        {
            var list = new List<Award>();
            if (mn == null || mn.Mode == MatchNet.GameModeType.Coop) return list;
            var given = new Dictionary<ulong, int>();

            void Give(Id id, ulong who, int value)
            {
                if (list.Count >= MAX_AWARDS || who == ulong.MaxValue) return;
                given.TryGetValue(who, out int n);
                if (n >= 2) return;   // spread them around
                given[who] = n + 1;
                list.Add(new Award { id = id, clientId = who, value = value });
            }

            // the best player by some number (ties: the earlier roster entry), only if it reaches min
            ulong Best(System.Func<ulong, float> score, float min, out float value, System.Func<ulong, bool> filter = null)
            {
                ulong best = ulong.MaxValue; value = 0f;
                foreach (var e in mn.Roster)
                {
                    if (filter != null && !filter(e.clientId)) continue;
                    float v = score(e.clientId);
                    if (v >= min && v > value) { value = v; best = e.clientId; }
                }
                return best;
            }

            float v;
            // mode awards first
            if (mn.Mode == MatchNet.GameModeType.Infection)
            {
                if (mn.WinnerTeam == Infection.TEAM_SURVIVORS)
                {
                    int survivors = 0; ulong last = ulong.MaxValue;
                    foreach (var e in mn.Roster) if (e.team == Infection.TEAM_SURVIVORS) { survivors++; last = e.clientId; }
                    if (survivors == 1) Give(Id.LastSurvivor, last, 0);
                }
                ulong pz = Best(id => ObjectiveMode.PointsOf(id), PATIENT_ZERO_MIN, out v);
                Give(Id.PatientZero, pz, (int)v);
            }
            if (mn.Mode == MatchNet.GameModeType.Juggernaut)
                Give(Id.GiantSlayer, Best(id => Of(id).takedowns, 1, out v), (int)v);

            Give(Id.Rambo, Best(id => Of(id).bestStreak, RAMBO_STREAK, out v), (int)v);
            Give(Id.Sharpshooter, Best(id => Of(id).longest, SHARP_MIN_METRES, out v), Mathf.RoundToInt(v));
            Give(Id.Ghost, Best(id => Of(id).kills, GHOST_MIN_KILLS, out v, id => Of(id).hitsTaken == 0), (int)v);
            Give(Id.BangMaster, Best(id => Of(id).bangs, BANG_MIN, out v), (int)v);
            if (mn.FogEnabled)   // bots never wipe: players only
                Give(Id.ClearEyes, Best(id => Of(id).kills, CLEAR_MIN_KILLS, out v, id => !MatchNet.IsBotId(id) && Of(id).wipes == 0), (int)v);
            return list;
        }

        // ───────── text ─────────
        public static string Title(Id id) => id switch
        {
            Id.Ghost => "GHOST",
            Id.Rambo => "RAMBO",
            Id.BangMaster => "BANG MASTER",
            Id.Sharpshooter => "SHARPSHOOTER",
            Id.PatientZero => "PATIENT ZERO",
            Id.LastSurvivor => "LAST SURVIVOR",
            Id.GiantSlayer => "GIANT SLAYER",
            _ => "CLEAR EYES",
        };

        public static string Detail(Award a) => a.id switch
        {
            Id.Ghost => $"{a.value} kills without taking a hit",
            Id.Rambo => $"{a.value} kills in a row",
            Id.BangMaster => $"{a.value} bang eliminations",
            Id.Sharpshooter => $"longest hit · {a.value} m",
            Id.PatientZero => $"{a.value} survivors infected",
            Id.LastSurvivor => "held out alone to the end",
            Id.GiantSlayer => a.value == 1 ? "took the juggernaut down" : $"took the juggernaut down {a.value}×",
            _ => $"{a.value} kills, never wiped the lens",
        };

        public static string Glyph(Id id) => id switch
        {
            Id.Ghost => "G",
            Id.Rambo => "R",
            Id.BangMaster => "!",
            Id.Sharpshooter => "+",
            Id.PatientZero => "Z",
            Id.LastSurvivor => "1",
            Id.GiantSlayer => "J",
            _ => "O",
        };
    }
}
