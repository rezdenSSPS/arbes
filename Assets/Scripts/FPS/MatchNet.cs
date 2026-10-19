using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // Lobby + match director. Self-bootstrapping singleton (like NetDiag/AutoConnect)
    // so it needs no scene wiring and survives the lobby -> arena scene load.
    //
    // Networking uses the NetworkManager's CustomMessagingManager, which works
    // WITHOUT a spawned NetworkObject -- the server owns the roster and broadcasts
    // the whole state to clients whenever it changes.
    public class MatchNet : MonoBehaviour
    {
        public enum GameModeType { Coop = 0, FreeForAll = 1, TeamDeathmatch = 2, GunGame = 3 }
        public enum Phase { Lobby = 0, InMatch = 1, Results = 2 }

        public struct Entry
        {
            public ulong clientId;
            public string name;
            public bool ready;
            public int kills;
            public int deaths;
            public int team;   // 0 = none, 1 = Red, 2 = Blue (Team Deathmatch)
        }

        public static MatchNet Instance { get; private set; }

        // Server-authoritative: EnemySpawner checks this so AI only spawns in Co-op.
        public static bool EnemiesEnabled = true;

        // Config
        public const int FFA_KILL_TARGET = 15;
        public const int TDM_KILL_TARGET = 40;
        public const int GUNGAME_WEAPONS = 10;
        const string MATCH_SCENE = "AirsoftArena";
        const string LOBBY_SCENE = "AirsoftLobby";

        const string MSG_NAME  = "mn_name";   // client -> server
        const string MSG_READY = "mn_ready";  // client -> server
        const string MSG_STATE = "mn_state";  // server -> clients
        const string MSG_KILL  = "mn_kill";   // server -> clients (kill feed)

        // Shared view (valid on everyone; authoritative copy lives on the server)
        public GameModeType Mode { get; private set; } = GameModeType.Coop;
        public Phase CurrentPhase { get; private set; } = Phase.Lobby;
        public ulong WinnerClientId { get; private set; } = ulong.MaxValue;
        public int WinnerTeam { get; private set; } = 0;  // 0 none, 1 Red, 2 Blue
        public IReadOnlyList<Entry> Roster => roster;

        // Raised on everyone whenever the roster/mode/phase changes, so UI can refresh.
        public event Action OnStateChanged;

        // Raised on everyone when a kill happens: (killerName, victimName, weapon).
        // killerName is empty for a non-PvP death (enemy / suicide).
        public event Action<string,string,string> OnKillFeed;

        readonly List<Entry> roster = new List<Entry>();

        // The name this local player wants to use (sent to the server on connect).
        string localName = "";
        bool localReadyWanted;

        NetworkManager nm;
        bool serverHandlersUp;
        bool clientHandlersUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("~MatchNet");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MatchNet>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            localName = PlayerPrefs.GetString("Airsoft.PlayerName", "");
        }

        void Update()
        {
            if (nm == null)
            {
                nm = NetworkManager.Singleton;
                if (nm == null) return;
                nm.OnServerStarted      += HandleServerStarted;
                nm.OnClientConnectedCallback  += HandleClientConnected;
                nm.OnClientDisconnectCallback += HandleClientDisconnected;
                nm.OnClientStopped      += HandleClientStopped;
            }

            // Host is both server and client; make sure client handlers are up too.
            if (nm.IsClient && !clientHandlersUp) RegisterClientHandlers();
            if (nm.IsServer && !serverHandlersUp) RegisterServerHandlers();
        }

        // ───────────────────────── Local player API (UI calls these) ─────────────────────────

        public void SetLocalName(string n)
        {
            n = Sanitize(n);
            localName = n;
            PlayerPrefs.SetString("Airsoft.PlayerName", n);
            PlayerPrefs.Save();
            if (nm != null && nm.IsClient && nm.IsConnectedClient)
                SendNameToServer();
        }

        public string LocalName => localName;

        public bool LocalReady
        {
            get
            {
                if (nm == null) return false;
                foreach (var e in roster) if (e.clientId == nm.LocalClientId) return e.ready;
                return false;
            }
        }

        public void ToggleReady()
        {
            if (nm == null || !nm.IsConnectedClient) return;
            localReadyWanted = !LocalReady;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(localReadyWanted);
            nm.CustomMessagingManager.SendNamedMessage(MSG_READY, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        public bool IsHost => nm != null && nm.IsServer;

        public void HostCycleMode()
        {
            if (!IsHost) return;
            Mode = (GameModeType)(((int)Mode + 1) % 4);
            BroadcastState();
        }

        public bool AllReady()
        {
            if (roster.Count == 0) return false;
            foreach (var e in roster) if (!e.ready) return false;
            return true;
        }

        // Host presses START. Sets up the mode, then loads the arena over the network.
        public void HostStartMatch()
        {
            if (!IsHost) return;
            EnemiesEnabled = (Mode == GameModeType.Coop);
            CurrentPhase = Phase.InMatch;
            WinnerClientId = ulong.MaxValue;
            WinnerTeam = 0;
            // reset scores (and split into two teams for Team Deathmatch)
            int t = 1;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster[i];
                e.kills = 0; e.deaths = 0;
                e.team = (Mode == GameModeType.TeamDeathmatch) ? t : 0;
                if (Mode == GameModeType.TeamDeathmatch) t = (t == 1) ? 2 : 1;
                roster[i] = e;
            }
            BroadcastState();
            nm.SceneManager.LoadScene(MATCH_SCENE, LoadSceneMode.Single);
        }

        // ───────────────────────── Server-side match logic ─────────────────────────

        // Called by Health on the server when a player dies.
        public void ReportKill(ulong victimClientId, ulong killerClientId, string weapon)
        {
            if (nm == null || !nm.IsServer) return;

            bool pvpMode = Mode == GameModeType.FreeForAll || Mode == GameModeType.TeamDeathmatch || Mode == GameModeType.GunGame;
            bool teamKill = Mode == GameModeType.TeamDeathmatch && HasEntry(killerClientId)
                            && TeamOf(killerClientId) == TeamOf(victimClientId);
            bool scored = pvpMode && killerClientId != victimClientId && HasEntry(killerClientId) && !teamKill;
            string killerName = scored ? NameFor(killerClientId) : "";
            RaiseKill(killerName, NameFor(victimClientId), weapon ?? "");

            bool changed = false;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster[i];
                if (e.clientId == victimClientId) { e.deaths++; roster[i] = e; changed = true; }
                else if (scored && e.clientId == killerClientId) { e.kills++; roster[i] = e; changed = true; }
            }

            // Win conditions
            if (Mode == GameModeType.FreeForAll)
            {
                foreach (var e in roster) if (e.kills >= FFA_KILL_TARGET) { EndMatch(e.clientId, 0); break; }
            }
            else if (Mode == GameModeType.GunGame)
            {
                foreach (var e in roster) if (e.kills >= GUNGAME_WEAPONS) { EndMatch(e.clientId, 0); break; }
            }
            else if (Mode == GameModeType.TeamDeathmatch)
            {
                if (TeamScore(1) >= TDM_KILL_TARGET) EndMatch(ulong.MaxValue, 1);
                else if (TeamScore(2) >= TDM_KILL_TARGET) EndMatch(ulong.MaxValue, 2);
            }

            if (changed || CurrentPhase == Phase.Results) BroadcastState();
        }

        void EndMatch(ulong winnerClient, int winnerTeam)
        {
            if (CurrentPhase == Phase.Results) return;
            CurrentPhase = Phase.Results;
            WinnerClientId = winnerClient;
            WinnerTeam = winnerTeam;
            Invoke(nameof(ReturnToLobby), 6f);
        }

        void ReturnToLobby()
        {
            if (nm == null || !nm.IsServer) return;
            CurrentPhase = Phase.Lobby;
            WinnerClientId = ulong.MaxValue;
            WinnerTeam = 0;
            EnemiesEnabled = true;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster[i]; e.ready = false; e.kills = 0; e.deaths = 0; e.team = 0; roster[i] = e;
            }
            BroadcastState();
            nm.SceneManager.LoadScene(LOBBY_SCENE, LoadSceneMode.Single);
        }

        // ───────────────────────── Networking plumbing ─────────────────────────

        void HandleServerStarted() => RegisterServerHandlers();

        void RegisterServerHandlers()
        {
            if (serverHandlersUp || nm == null || !nm.IsServer) return;
            var cm = nm.CustomMessagingManager;
            cm.RegisterNamedMessageHandler(MSG_NAME,  OnServerRecvName);
            cm.RegisterNamedMessageHandler(MSG_READY, OnServerRecvReady);
            serverHandlersUp = true;

            // Make sure the host's own entry exists.
            EnsureEntry(nm.LocalClientId);
            BroadcastState();
        }

        void RegisterClientHandlers()
        {
            if (clientHandlersUp || nm == null) return;
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MSG_STATE, OnClientRecvState);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MSG_KILL, OnClientRecvKill);
            clientHandlersUp = true;
        }

        void HandleClientConnected(ulong clientId)
        {
            if (nm.IsServer)
            {
                EnsureEntry(clientId);
                BroadcastState();
            }

            // When *I* finish connecting, push my chosen name to the server.
            if (clientId == nm.LocalClientId && nm.IsClient)
            {
                RegisterClientHandlers();
                SendNameToServer();
            }
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (nm == null || !nm.IsServer) return;
            for (int i = roster.Count - 1; i >= 0; i--)
                if (roster[i].clientId == clientId) roster.RemoveAt(i);
            BroadcastState();
        }

        void HandleClientStopped(bool wasHost)
        {
            // Session ended locally -- wipe state so a fresh lobby starts clean.
            roster.Clear();
            CurrentPhase = Phase.Lobby;
            WinnerClientId = ulong.MaxValue;
            serverHandlersUp = false;
            clientHandlersUp = false;
            OnStateChanged?.Invoke();
        }

        void SendNameToServer()
        {
            if (nm == null || !nm.IsConnectedClient) return;
            string n = string.IsNullOrWhiteSpace(localName) ? DefaultName(nm.LocalClientId) : localName;
            using var w = new FastBufferWriter(128, Allocator.Temp);
            w.WriteValueSafe(n);
            nm.CustomMessagingManager.SendNamedMessage(MSG_NAME, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        void OnServerRecvName(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string n);
            n = Sanitize(n);
            EnsureEntry(sender);
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].clientId == sender) { var e = roster[i]; e.name = n; roster[i] = e; }
            BroadcastState();
        }

        void OnServerRecvReady(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool ready);
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].clientId == sender) { var e = roster[i]; e.ready = ready; roster[i] = e; }
            BroadcastState();
        }

        void EnsureEntry(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return;
            roster.Add(new Entry { clientId = clientId, name = DefaultName(clientId), ready = false, kills = 0, deaths = 0 });
        }

        void BroadcastState()
        {
            OnStateChanged?.Invoke(); // refresh the host's own UI immediately

            if (nm == null || !nm.IsServer) return;

            using var w = new FastBufferWriter(1024, Allocator.Temp, 64 * 1024);
            w.WriteValueSafe((byte)Mode);
            w.WriteValueSafe((byte)CurrentPhase);
            w.WriteValueSafe(WinnerClientId);
            w.WriteValueSafe(WinnerTeam);
            w.WriteValueSafe(roster.Count);
            foreach (var e in roster)
            {
                w.WriteValueSafe(e.clientId);
                w.WriteValueSafe(e.name ?? "");
                w.WriteValueSafe(e.ready);
                w.WriteValueSafe(e.kills);
                w.WriteValueSafe(e.deaths);
                w.WriteValueSafe(e.team);
            }
            nm.CustomMessagingManager.SendNamedMessageToAll(MSG_STATE, w, NetworkDelivery.Reliable);
        }

        void OnClientRecvState(ulong sender, FastBufferReader reader)
        {
            // The host already has the authoritative list; ignore its own echo.
            if (nm != null && nm.IsServer) return;

            reader.ReadValueSafe(out byte modeB);
            reader.ReadValueSafe(out byte phaseB);
            reader.ReadValueSafe(out ulong winner);
            reader.ReadValueSafe(out int winnerTeam);
            reader.ReadValueSafe(out int count);

            roster.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong cid);
                reader.ReadValueSafe(out string nm2);
                reader.ReadValueSafe(out bool ready);
                reader.ReadValueSafe(out int kills);
                reader.ReadValueSafe(out int deaths);
                reader.ReadValueSafe(out int team);
                roster.Add(new Entry { clientId = cid, name = nm2, ready = ready, kills = kills, deaths = deaths, team = team });
            }

            Mode = (GameModeType)modeB;
            CurrentPhase = (Phase)phaseB;
            WinnerClientId = winner;
            WinnerTeam = winnerTeam;
            EnemiesEnabled = (Mode == GameModeType.Coop);
            OnStateChanged?.Invoke();
        }

        // ───────────────────────── helpers ─────────────────────────

        bool HasEntry(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return true;
            return false;
        }

        void RaiseKill(string killer, string victim, string weapon)
        {
            OnKillFeed?.Invoke(killer, victim, weapon); // local (host)
            if (nm == null || !nm.IsServer) return;
            using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
            w.WriteValueSafe(killer ?? "");
            w.WriteValueSafe(victim ?? "");
            w.WriteValueSafe(weapon ?? "");
            nm.CustomMessagingManager.SendNamedMessageToAll(MSG_KILL, w, NetworkDelivery.Reliable);
        }

        void OnClientRecvKill(ulong sender, FastBufferReader reader)
        {
            if (nm != null && nm.IsServer) return; // host already raised it locally
            reader.ReadValueSafe(out string killer);
            reader.ReadValueSafe(out string victim);
            reader.ReadValueSafe(out string weapon);
            OnKillFeed?.Invoke(killer, victim, weapon);
        }

        public int TeamOf(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return e.team;
            return 0;
        }
        public int TeamScore(int team)
        {
            int sc = 0; foreach (var e in roster) if (e.team == team) sc += e.kills; return sc;
        }
        public int GunLevelOf(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return e.kills;
            return 0;
        }
        // Team Deathmatch: block damage between teammates (friendly fire off).
        public bool BlocksDamage(ulong attacker, ulong victim)
        {
            if (Mode != GameModeType.TeamDeathmatch || attacker == victim) return false;
            if (!HasEntry(attacker) || !HasEntry(victim)) return false;
            return TeamOf(attacker) == TeamOf(victim);
        }

        public string NameFor(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return e.name;
            return DefaultName(clientId);
        }

        static string DefaultName(ulong clientId) => clientId == 0 ? "Host" : "Player " + clientId;

        static string Sanitize(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return "";
            n = n.Trim();
            if (n.Length > 16) n = n.Substring(0, 16);
            return n;
        }

        void OnDestroy()
        {
            if (nm != null)
            {
                nm.OnServerStarted      -= HandleServerStarted;
                nm.OnClientConnectedCallback  -= HandleClientConnected;
                nm.OnClientDisconnectCallback -= HandleClientDisconnected;
                nm.OnClientStopped      -= HandleClientStopped;
            }
        }
    }
}
