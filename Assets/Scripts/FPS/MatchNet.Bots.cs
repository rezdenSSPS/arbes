using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // Bots that fill empty slots in PvP (FFA, TDM, Gun Game, Milsim) so a small group can still play.
    //
    // A bot is a normal roster entry (team, kills, deaths, Gun Game level, Milsim HIT) whose id is
    // BOT_ID_BASE + n, far above any NGO client id, so it can't collide with a real player. The host
    // picks "Fill with bots" (Off / up to 4 / up to 8 players in total) and a difficulty in the lobby;
    // bot entries are always ready. In the arena each bot entry gets an AI soldier (the Enemy prefab,
    // Health.botId set, driven by BotBrain on the server). Co-op and objective modes have no bots.
    public partial class MatchNet
    {
        public const ulong BOT_ID_BASE = 1UL << 62;
        public static bool IsBotId(ulong id) => id >= BOT_ID_BASE && id != ulong.MaxValue;

        public enum BotDifficulty { Easy = 0, Normal = 1, Hard = 2 }
        public static readonly int[] BOT_FILL_OPTIONS = { 0, 4, 8 };   // total players incl. bots; 0 = off
        public static readonly string[] BOT_DIFFICULTY_NAMES = { "Easy", "Normal", "Hard" };
        const int BOT_MAX_ROSTER = 8;
        const string BOT_FILL_PREF = "Airsoft.BotFill";
        const string BOT_DIFFICULTY_PREF = "Airsoft.BotDifficulty";
        const string MSG_BOTS = "mn_bots";   // server -> clients (bot settings, for the lobby)

        static readonly string[] Callsigns =
        {
            "Viper", "Ghost", "Hawk", "Rook", "Maverick", "Nomad", "Saber", "Bandit", "Falcon", "Wolf",
            "Cobra", "Raven", "Jester", "Tango", "Echo", "Blaze", "Frost", "Havoc", "Reaper", "Specter",
            "Kilo", "Ranger", "Shadow", "Mako", "Badger", "Lynx", "Hornet", "Gator"
        };

        // Shared view (host setting, synced to clients for the lobby).
        public int BotFill { get; private set; }
        public BotDifficulty BotSkill { get; private set; } = BotDifficulty.Normal;

        // Modes that get bots. Explicit list: modes added later (objectives) stay bot-free.
        public static bool ModeHasBots(GameModeType m) =>
            m == GameModeType.FreeForAll || m == GameModeType.TeamDeathmatch ||
            m == GameModeType.GunGame || m == GameModeType.Milsim;

        public bool BotsActive => ModeHasBots(Mode) && BotFill > 0;

        public int BotCount
        {
            get { int n = 0; foreach (var e in roster) if (IsBotId(e.clientId)) n++; return n; }
        }

        // ───────── host API (lobby) ─────────

        public void HostSetBotFill(int total)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby || BotFill == total) return;
            if (System.Array.IndexOf(BOT_FILL_OPTIONS, total) < 0) return;
            BotFill = total;
            PlayerPrefs.SetInt(BOT_FILL_PREF, total);
            PlayerPrefs.Save();
            SendBotSettings();
            SyncBotRoster();
            OnStateChanged?.Invoke();
        }

        public void HostSetBotDifficulty(int difficulty)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby || (int)BotSkill == difficulty) return;
            if (difficulty < 0 || difficulty >= BOT_DIFFICULTY_NAMES.Length) return;
            BotSkill = (BotDifficulty)difficulty;
            PlayerPrefs.SetInt(BOT_DIFFICULTY_PREF, difficulty);
            PlayerPrefs.Save();
            SendBotSettings();
            OnStateChanged?.Invoke();
        }

        // ───────── runtime (server) ─────────

        readonly Dictionary<ulong, NetworkObject> botObjects = new Dictionary<ulong, NetworkObject>();
        ulong nextBotSerial;
        CustomMessagingManager botsCmm;
        bool botPrefsLoaded;
        float botTick;
        int botSentClients = -1;
        Scene botNavScene;             // the arena the navmesh was checked for
        float botSceneSince = -1f;     // when the arena became active (bots wait for the players)
        GameObject botPrefab;

        // The live bot soldier for a bot id (server), or null.
        public NetworkObject BotObject(ulong botId)
        {
            if (!IsBotId(botId)) return null;
            return botObjects.TryGetValue(botId, out var no) && no != null && no.IsSpawned ? no : null;
        }

        // Called from Update every frame.
        void BotsUpdate()
        {
            if (nm == null) return;
            if (!botPrefsLoaded)
            {
                botPrefsLoaded = true;
                int fill = PlayerPrefs.GetInt(BOT_FILL_PREF, 0);
                BotFill = System.Array.IndexOf(BOT_FILL_OPTIONS, fill) >= 0 ? fill : 0;
                BotSkill = (BotDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(BOT_DIFFICULTY_PREF, 1), 0, BOT_DIFFICULTY_NAMES.Length - 1);
            }

            // A new session has a new messaging manager: (re)register the bot settings handler.
            var cmm = (nm.IsServer || nm.IsClient) ? nm.CustomMessagingManager : null;
            if (cmm != null && cmm != botsCmm)
            {
                botsCmm = cmm;
                cmm.RegisterNamedMessageHandler(MSG_BOTS, OnClientRecvBots);
                botSentClients = -1;
                botObjects.Clear();
            }

            if (!nm.IsServer || !nm.IsListening) return;
            botTick -= Time.unscaledDeltaTime;
            if (botTick > 0f) return;
            botTick = 0.25f;

            int clients = nm.ConnectedClientsIds.Count;
            if (clients != botSentClients) { botSentClients = clients; SendBotSettings(); }

            SyncBotRoster();
            SpawnMissingBots();
        }

        // Keeps the roster at "Fill with bots" players: adds bots for empty slots, removes bots when
        // players join (or the mode has no bots), and keeps every bot ready.
        void SyncBotRoster()
        {
            if (nm == null || !nm.IsServer || CurrentPhase == Phase.Results) return;
            bool changed = false;

            int humans = 0, bots = 0;
            foreach (var e in roster) { if (IsBotId(e.clientId)) bots++; else humans++; }
            int want = BotsActive ? Mathf.Clamp(BotFill - humans, 0, Mathf.Max(0, BOT_MAX_ROSTER - humans)) : 0;

            while (bots > want)
            {
                int i = BotToRemove();
                if (i < 0) break;
                DespawnBot(roster[i].clientId);
                roster.RemoveAt(i);
                bots--; changed = true;
            }
            while (bots < want)
            {
                roster.Add(NewBot());
                bots++; changed = true;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                if (!IsBotId(roster[i].clientId) || roster[i].ready) continue;
                var e = roster[i]; e.ready = true; roster[i] = e; changed = true;   // bots never block "all ready"
            }

            // objects of bots that left the roster
            if (botObjects.Count > 0)
            {
                var gone = new List<ulong>();
                foreach (var kv in botObjects) if (!HasEntry(kv.Key) || kv.Value == null) gone.Add(kv.Key);
                foreach (var id in gone) DespawnBot(id);
            }

            if (changed) BroadcastState();
        }

        // Last bot in the roster; mid-match in a team mode, one from the bigger team.
        int BotToRemove()
        {
            int big = 0;
            if (CurrentPhase == Phase.InMatch && TeamMode)
            {
                int red = 0, blue = 0;
                foreach (var e in roster) { if (e.team == 1) red++; else if (e.team == 2) blue++; }
                big = red > blue ? 1 : blue > red ? 2 : 0;
            }
            int fallback = -1;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                if (!IsBotId(roster[i].clientId)) continue;
                if (big == 0 || roster[i].team == big) return i;
                if (fallback < 0) fallback = i;
            }
            return fallback;
        }

        Entry NewBot()
        {
            ulong id = BOT_ID_BASE + (++nextBotSerial);
            var rng = new System.Random(unchecked((int)nextBotSerial * 7919 + System.Environment.TickCount));

            string name = null;
            for (int tries = 0; tries < 40 && name == null; tries++)
            {
                string n = "BOT " + Callsigns[rng.Next(Callsigns.Length)];
                bool used = false;
                foreach (var e in roster) if (e.name == n) { used = true; break; }
                if (!used) name = n;
            }
            if (name == null) name = "BOT " + nextBotSerial;

            int team = 0;
            if (CurrentPhase == Phase.InMatch && TeamMode)
            {
                int red = 0, blue = 0;
                foreach (var e in roster) { if (e.team == 1) red++; else if (e.team == 2) blue++; }
                team = red <= blue ? 1 : 2;
            }
            return new Entry { clientId = id, name = name, ready = true, kills = 0, deaths = 0, team = team,
                               look = AirsoftLook.Random(rng) };
        }

        // Every bot entry in a running PvP match has a soldier in the arena.
        void SpawnMissingBots()
        {
            if (CurrentPhase != Phase.InMatch || !ModeHasBots(Mode)) { botSceneSince = -1f; return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.name != MATCH_SCENE) { botSceneSince = -1f; return; }
            if (botSceneSince < 0f) botSceneSince = Time.unscaledTime;

            // Wait until the players are in (PlayerSpawner spawns them once everyone loaded the arena).
            bool playersIn = false;
            foreach (var c in nm.ConnectedClientsList) if (c.PlayerObject != null) { playersIn = true; break; }
            if (!playersIn && Time.unscaledTime - botSceneSince < 10f) return;

            if (botNavScene != scene)
            {
                botNavScene = scene;
                PrepareBotArena();
            }
            if (botPrefab == null) return;

            foreach (var e in roster)
            {
                if (!IsBotId(e.clientId)) continue;
                if (BotObject(e.clientId) != null) continue;
                SpawnBot(e);
            }
        }

        // Finds the AI soldier prefab and makes sure the arena has a navmesh (the Co-op spawner only
        // bakes it when enemies are enabled).
        void PrepareBotArena()
        {
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            if (spawner != null && spawner.enemyPrefab != null) botPrefab = spawner.enemyPrefab;
            if (botPrefab == null && nm.NetworkConfig != null && nm.NetworkConfig.Prefabs != null)
                foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
                    if (p != null && p.Prefab != null && p.Prefab.GetComponent<EnemyAI>() != null) { botPrefab = p.Prefab; break; }
            if (botPrefab == null) { Debug.LogWarning("[Airsoft] Bots: no AI soldier prefab found"); return; }

            Vector3 probe = PlayerSpawner.GetSpawnFor(ulong.MaxValue).position;
            if (!NavMesh.SamplePosition(probe, out _, 4f, NavMesh.AllAreas))
            {
                var surface = spawner != null && spawner.navSurface != null ? spawner.navSurface
                            : Object.FindAnyObjectByType<NavMeshSurface>();
                if (surface != null) surface.BuildNavMesh();
                else Debug.LogWarning("[Airsoft] Bots: arena has no NavMeshSurface");
            }
        }

        void SpawnBot(Entry e)
        {
            var pose = PlayerSpawner.GetSpawnFor(e.clientId);
            Vector3 pos = pose.position;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 6f, NavMesh.AllAreas)) pos = hit.position;

            var go = Instantiate(botPrefab, pos, pose.rotation);
            go.name = e.name;
            var no = go.GetComponent<NetworkObject>();
            var h = go.GetComponent<Health>();
            if (no == null || h == null) { Destroy(go); return; }

            h.botId.Value = e.clientId;   // before Spawn: clients get it with the spawn
            h.maxHealth = Health.BOT_MAX_HEALTH;
            h.respawnDelay = 3f;
            var brain = go.AddComponent<BotBrain>();
            brain.Setup(e.clientId, BotSkill);

            no.Spawn(true);
            botObjects[e.clientId] = no;
        }

        void DespawnBot(ulong id)
        {
            if (botObjects.TryGetValue(id, out var no))
            {
                botObjects.Remove(id);
                if (no != null && no.IsSpawned) no.Despawn(true);
            }
        }

        // ───────── settings sync ─────────

        void SendBotSettings()
        {
            if (nm == null || !nm.IsServer || !nm.IsListening) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((byte)BotFill);
            w.WriteValueSafe((byte)BotSkill);
            nm.CustomMessagingManager.SendNamedMessageToAll(MSG_BOTS, w, NetworkDelivery.Reliable);
        }

        void OnClientRecvBots(ulong sender, FastBufferReader reader)
        {
            if (nm == null || nm.IsServer || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out byte fill);
            reader.ReadValueSafe(out byte skill);
            BotFill = fill;
            BotSkill = (BotDifficulty)Mathf.Clamp(skill, 0, BOT_DIFFICULTY_NAMES.Length - 1);
            OnStateChanged?.Invoke();
        }
    }
}
