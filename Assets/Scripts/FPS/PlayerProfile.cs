using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace AirsoftFPS
{
    // Local player profile: lifetime stats, XP and level. Saved as JSON in
    // Application.persistentDataPath, so it belongs to this computer (not synced to anyone).
    [Serializable]
    public class ProfileData
    {
        public int version = 1;
        public int matchesPlayed, wins, kills, deaths, hitsDealt, hitsTaken, bestStreak, totalXp;
        public List<WeaponKills> weaponKills = new List<WeaponKills>();
        // The look saved before unlocks existed: its items stay usable at any level.
        public int legacyLook = -1;
        public int revives;   // Medic mode: teammates revived (older profiles load it as 0)
    }

    [Serializable]
    public class WeaponKills { public string weapon; public int kills; }

    // What the results screen shows for the match that just ended.
    public class MatchXp
    {
        public int serial;
        public float time;   // Time.unscaledTime when committed
        public int xpBefore, xpAfter;
        public int kills, hits, enemyKills;
        public int revives;   // Medic mode
        public bool won, completed;
        public readonly List<(string what, int xp)> lines = new List<(string, int)>();
        public readonly List<string> unlocked = new List<string>();
        public int Gained => xpAfter - xpBefore;
        public int LevelBefore => PlayerProfile.LevelFor(xpBefore);
        public int LevelAfter => PlayerProfile.LevelFor(xpAfter);
    }

    public static class PlayerProfile
    {
        // ───────────── XP rules ─────────────
        public const int XP_KILL = 20;         // player kill (PvP modes)
        public const int XP_ENEMY_KILL = 10;   // AI soldier (co-op)
        public const int XP_HIT = 3;           // every server-confirmed hit
        public const int XP_MATCH = 50;        // finished a match (co-op: stayed 2+ minutes)
        public const int XP_WIN = 100;         // won it (FFA / Gun Game winner, or winning team)
        public const int XP_REVIVE = 15;       // revived a teammate (Medic)

        // XP needed to go from `level` to the next: 150, 225, 300, ... Level 10 = 4 050 XP,
        // roughly three evenings of matches.
        public static int XpToNext(int level) => 150 + 75 * (Mathf.Max(1, level) - 1);

        public static int LevelFor(int xp)
        {
            int level = 1;
            while (level < 99 && xp >= XpToNext(level)) { xp -= XpToNext(level); level++; }
            return level;
        }

        // XP needed to reach the start of `level`.
        public static int XpAtLevel(int level)
        {
            int sum = 0;
            for (int l = 1; l < level; l++) sum += XpToNext(l);
            return sum;
        }

        // Progress inside the current level: (xp into level, xp the level needs).
        public static (int into, int need) Progress(int xp)
        {
            int level = LevelFor(xp);
            return (xp - XpAtLevel(level), XpToNext(level));
        }

        // ───────────── store ─────────────
        const string FileName = "airsoft_profile.json";
        static ProfileData s_data;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_data = null; LastMatch = null; Changed = null; }

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static ProfileData Data
        {
            get
            {
                if (s_data == null) Load();
                return s_data;
            }
        }

        public static int Level => LevelFor(Data.totalXp);
        public static MatchXp LastMatch { get; private set; }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    s_data = JsonUtility.FromJson<ProfileData>(File.ReadAllText(FilePath));
            }
            catch (Exception e) { Debug.LogWarning("[Airsoft] profile could not be read, starting fresh: " + e.Message); }
            if (s_data != null) { s_data.weaponKills ??= new List<WeaponKills>(); return; }

            // First run with profiles: whatever look was saved before keeps its items.
            s_data = new ProfileData { legacyLook = PlayerPrefs.GetInt(AirsoftLook.PrefKey, -1) };
            Save();
        }

        public static void Save()
        {
            if (s_data == null) return;
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s_data, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("[Airsoft] profile could not be saved: " + e.Message); }
        }

        public static string FavoriteWeapon
        {
            get
            {
                WeaponKills best = null;
                foreach (var w in Data.weaponKills) if (best == null || w.kills > best.kills) best = w;
                return best != null && best.kills > 0 ? best.weapon : "";
            }
        }

        public static void AddWeaponKill(string weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return;
            foreach (var w in Data.weaponKills) if (w.weapon == weapon) { w.kills++; return; }
            Data.weaponKills.Add(new WeaponKills { weapon = weapon, kills = 1 });
        }

        // Applies a finished (or abandoned) match: stats + XP, saves, and remembers it for the results screen.
        public static void Commit(MatchXp m, int deaths, int hitsTaken, int bestStreak, List<string> weaponKills)
        {
            var d = Data;
            m.xpBefore = d.totalXp;
            int xp = 0;
            if (m.completed) { xp += XP_MATCH; m.lines.Add(("Match", XP_MATCH)); }
            if (m.won) { xp += XP_WIN; m.lines.Add(("Win", XP_WIN)); }
            if (m.kills > 0) { xp += m.kills * XP_KILL; m.lines.Add((m.kills == 1 ? "1 kill" : m.kills + " kills", m.kills * XP_KILL)); }
            if (m.enemyKills > 0) { xp += m.enemyKills * XP_ENEMY_KILL; m.lines.Add((m.enemyKills + " enemies", m.enemyKills * XP_ENEMY_KILL)); }
            if (m.hits > 0) { xp += m.hits * XP_HIT; m.lines.Add((m.hits == 1 ? "1 hit" : m.hits + " hits", m.hits * XP_HIT)); }
            if (m.revives > 0) { xp += m.revives * XP_REVIVE; m.lines.Add((m.revives == 1 ? "1 revive" : m.revives + " revives", m.revives * XP_REVIVE)); }

            if (m.completed) d.matchesPlayed++;
            if (m.won) d.wins++;
            d.kills += m.kills + m.enemyKills;
            d.deaths += deaths;
            d.hitsDealt += m.hits;
            d.hitsTaken += hitsTaken;
            d.revives += m.revives;
            d.bestStreak = Mathf.Max(d.bestStreak, bestStreak);
            foreach (var w in weaponKills) AddWeaponKill(w);
            d.totalXp += xp;
            m.xpAfter = d.totalXp;
            for (int l = m.LevelBefore + 1; l <= m.LevelAfter; l++) m.unlocked.AddRange(AirsoftLook.UnlocksAt(l));
            m.serial = (LastMatch?.serial ?? 0) + 1;
            m.time = Time.unscaledTime;
            LastMatch = m;
            Save();
            Changed?.Invoke();
        }
    }

    // Watches the match this client is in and turns what it already receives (roster scores,
    // kill feed, hit confirmations, own health) into profile stats. Runs on every client;
    // nothing new goes over the network.
    public class ProfileTracker : MonoBehaviour
    {
        const float COOP_MIN_SECONDS = 120f;   // co-op has no end screen: count it after 2 minutes

        bool active;
        MatchXp match;
        float startTime;
        int baseKills, baseDeaths, seenKills, seenDeaths;
        int deaths, hitsTaken, streak, bestStreak;
        readonly List<string> weaponKills = new List<string>();
        MatchNet hooked;

        // local player
        NetworkObject player;
        WeaponController weapon;
        Health health;
        int lastHp = -1;

        // co-op: enemies we hit recently, to credit the kill if they go down right after
        readonly List<(Health h, float t)> recentHits = new List<(Health, float)>();
        readonly HashSet<Health> credited = new HashSet<Health>();
        static readonly Collider[] s_overlap = new Collider[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<ProfileTracker>() != null) return;
            var go = new GameObject("~ProfileTracker");
            DontDestroyOnLoad(go);
            go.AddComponent<ProfileTracker>();
            _ = PlayerProfile.Data;   // create the profile (and grandfather the current look) on first run
        }

        void Update()
        {
            var mnet = MatchNet.Instance;
            if (mnet != hooked)
            {
                if (hooked != null) hooked.OnKillFeed -= OnKill;
                hooked = mnet;
                if (hooked != null) hooked.OnKillFeed += OnKill;
            }
            var nm = NetworkManager.Singleton;
            bool connected = nm != null && nm.IsClient && mnet != null;
            var phase = connected ? mnet.CurrentPhase : MatchNet.Phase.Lobby;

            if (phase == MatchNet.Phase.InMatch && !active) Begin(mnet, nm);
            if (active)
            {
                if (phase == MatchNet.Phase.Results) End(mnet, nm, true);
                else if (phase != MatchNet.Phase.InMatch) End(mnet, nm, false);
                else Track(mnet, nm);
            }
        }

        void Begin(MatchNet mnet, NetworkManager nm)
        {
            active = true;
            match = new MatchXp();
            startTime = Time.unscaledTime;
            deaths = hitsTaken = streak = bestStreak = 0;
            weaponKills.Clear();
            recentHits.Clear();
            credited.Clear();
            Mine(mnet, nm, out baseKills, out baseDeaths);
            seenKills = baseKills; seenDeaths = baseDeaths;
            lastHp = -1;
        }

        void End(MatchNet mnet, NetworkManager nm, bool results)
        {
            active = false;
            Unhook();
            if (results)
            {
                match.completed = true;
                ulong me = nm != null ? nm.LocalClientId : ulong.MaxValue;
                if (!mnet.IsDraw)
                {
                    if (MatchNet.IsTeamMode(mnet.Mode))   // TDM, Milsim, CTF, Domination
                        match.won = mnet.WinnerTeam != 0 && mnet.WinnerTeam == mnet.TeamOf(me);
                    else if (mnet.Mode != MatchNet.GameModeType.Coop)
                        match.won = mnet.WinnerClientId == me;
                }
            }
            else
            {
                // Left early or the host went back: only co-op counts as played (it never ends by itself).
                bool coop = mnet != null && mnet.Mode == MatchNet.GameModeType.Coop;
                match.completed = coop && Time.unscaledTime - startTime >= COOP_MIN_SECONDS;
            }
            PlayerProfile.Commit(match, deaths, hitsTaken, bestStreak, weaponKills);
            match = null;
        }

        void OnApplicationQuit()
        {
            if (active) End(MatchNet.Instance, NetworkManager.Singleton, false);
        }

        static bool Mine(MatchNet mnet, NetworkManager nm, out int kills, out int deaths)
        {
            kills = deaths = 0;
            if (mnet == null || nm == null) return false;
            foreach (var e in mnet.Roster)
                if (e.clientId == nm.LocalClientId) { kills = e.kills; deaths = e.deaths; return true; }
            return false;
        }

        void Track(MatchNet mnet, NetworkManager nm)
        {
            // Scores: the roster is authoritative (server-counted kills and deaths).
            if (Mine(mnet, nm, out int k, out int d))
            {
                if (k > seenKills && mnet.Mode != MatchNet.GameModeType.Coop)
                {
                    int n = k - seenKills;
                    match.kills += n;
                    AddStreak(n);
                }
                if (d > seenDeaths) { deaths += d - seenDeaths; streak = 0; }
                seenKills = Mathf.Max(seenKills, k);
                seenDeaths = Mathf.Max(seenDeaths, d);
            }

            // Medic: revives are the objective points (server-counted, synced with the objective state).
            if (ObjectiveMode.Active is Medic) match.revives = Mathf.Max(match.revives, ObjectiveMode.PointsOf(nm.LocalClientId));

            // Local player: confirmed hits (weapon) and hits taken (own health going down).
            var po = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po != player)
            {
                Unhook();
                player = po;
                if (player != null)
                {
                    weapon = player.GetComponent<WeaponController>();
                    health = player.GetComponent<Health>();
                    if (weapon != null) weapon.OnHitMarker += OnHit;
                }
                lastHp = -1;
            }
            if (health != null && health.IsSpawned)
            {
                int hp = health.currentHealth.Value;
                if (lastHp >= 0 && hp < lastHp) hitsTaken++;
                lastHp = hp;
            }

            // Co-op: an enemy we just hit went down -> our kill.
            for (int i = recentHits.Count - 1; i >= 0; i--)
            {
                var (h, t) = recentHits[i];
                if (h == null || Time.unscaledTime - t > 0.75f) { recentHits.RemoveAt(i); continue; }
                if (h.isDead.Value && credited.Add(h))
                {
                    match.enemyKills++;
                    AddStreak(1);
                    if (weapon != null && !string.IsNullOrEmpty(weapon.CurrentWeaponName)) weaponKills.Add(weapon.CurrentWeaponName);
                    recentHits.RemoveAt(i);
                }
            }
        }

        void AddStreak(int n)
        {
            streak += n;
            bestStreak = Mathf.Max(bestStreak, streak);
        }

        void Unhook()
        {
            if (weapon != null) weapon.OnHitMarker -= OnHit;
            weapon = null; health = null; player = null;
        }

        void OnHit(Vector3 point)
        {
            if (!active) return;
            match.hits++;
            int n = Physics.OverlapSphereNonAlloc(point, 1.2f, s_overlap, ~0, QueryTriggerInteraction.Collide);
            Health best = null; float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = s_overlap[i].GetComponentInParent<Health>();
                if (h == null || h.IsCombatant || credited.Contains(h)) continue;   // co-op AI only (bots score via the roster)
                float dist = (s_overlap[i].ClosestPoint(point) - point).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = h; }
            }
            if (best != null) recentHits.Add((best, Time.unscaledTime));
        }

        // Kill feed: which weapon our kills were made with (for "favorite weapon").
        void OnKill(string killer, string victim, string weaponName)
        {
            if (!active || string.IsNullOrEmpty(killer) || killer == victim) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || MatchNet.Instance == null) return;
            if (killer == MatchNet.Instance.NameFor(nm.LocalClientId)) weaponKills.Add(weaponName);
        }

        void OnDestroy()
        {
            if (hooked != null) hooked.OnKillFeed -= OnKill;
            Unhook();
        }
    }
}
