using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // Lobby + match director. Self-bootstrapping singleton (like AutoConnect)
    // so it needs no scene wiring and survives the lobby -> arena scene load.
    //
    // Networking uses the NetworkManager's CustomMessagingManager, which works
    // WITHOUT a spawned NetworkObject -- the server owns the roster and broadcasts
    // the whole state to clients whenever it changes.
    public class MatchNet : MonoBehaviour
    {
        public enum GameModeType { Coop = 0, FreeForAll = 1, TeamDeathmatch = 2, GunGame = 3, Milsim = 4, CaptureTheFlag = 5, Domination = 6 }
        public const int MODE_COUNT = 7;
        public enum Phase { Lobby = 0, InMatch = 1, Results = 2 }

        public struct Entry
        {
            public ulong clientId;
            public string name;
            public bool ready;
            public int kills;
            public int deaths;
            public int team;   // 0 = none, 1 = Red, 2 = Blue (team modes: Team Deathmatch, Milsim)
            public int look;   // packed AirsoftLook (-1 = not chosen yet)
        }

        public static MatchNet Instance { get; private set; }

        // Server-authoritative: EnemySpawner checks this so AI only spawns in Co-op.
        public static bool EnemiesEnabled = true;

        // Config
        public const int FFA_KILL_TARGET = 15;
        public const int TDM_KILL_TARGET = 40;
        public const int GUNGAME_WEAPONS = 10;
        public const int MILSIM_HIT_TARGET = 30;   // Milsim: each BB hit scores a point for the shooter's team
        public const float RESULTS_SECONDS = 6f;   // results screen, then everyone returns to the lobby
        // Match time limit (minutes) the host can pick in the lobby; 0 = off. Co-op never has one.
        public static readonly int[] TIME_LIMIT_OPTIONS = { 0, 5, 10, 15 };
        public const int DEFAULT_TIME_LIMIT = 10;
        const string TIME_LIMIT_PREF = "Airsoft.TimeLimit";
        const string MATCH_SCENE = "AirsoftArena";
        const string LOBBY_SCENE = "AirsoftLobby";
        // The map scene the next / current match is played on (objective modes check its MapLayout).
        public static string CurrentMapScene => MATCH_SCENE;

        const string MSG_NAME  = "mn_name";   // client -> server
        const string MSG_READY = "mn_ready";  // client -> server
        const string MSG_STATE = "mn_state";  // server -> clients
        const string MSG_KILL  = "mn_kill";   // server -> clients (kill feed)
        const string MSG_LOOK  = "mn_look";   // client -> server (character look)
        const string MSG_DEATH = "mn_death";  // server -> the victim only (killcam + death screen)

        // Shared view (valid on everyone; authoritative copy lives on the server)
        public GameModeType Mode { get; private set; } = GameModeType.Coop;
        public Phase CurrentPhase { get; private set; } = Phase.Lobby;
        public ulong WinnerClientId { get; private set; } = ulong.MaxValue;
        public int WinnerTeam { get; private set; } = 0;  // 0 none, 1 Red, 2 Blue
        public bool IsDraw { get; private set; }          // match ended on time with an exact tie
        public bool EndedOnTime { get; private set; }     // match ended because the clock ran out
        public int TimeLimitMinutes { get; private set; } = DEFAULT_TIME_LIMIT;
        // Network server time (NetworkManager.ServerTime) when the clock runs out; 0 = not running.
        public double MatchEndTime { get; private set; }
        public IReadOnlyList<Entry> Roster => roster;

        // Raised on everyone whenever the roster/mode/phase changes, so UI can refresh.
        public event Action OnStateChanged;

        // Raised on everyone when a kill happens: (killerName, victimName, weapon).
        // killerName is empty for a non-PvP death (enemy / suicide).
        public event Action<string,string,string> OnKillFeed;

        // Who eliminated the local player, as the server saw it. Sent only to the victim.
        public enum KillerKind : byte { None = 0, Player = 1, Enemy = 2 }
        public struct DeathInfo
        {
            public KillerKind kind;
            public ulong killerClientId;   // ulong.MaxValue for AI enemies / none
            public ulong killerObjectId;   // NetworkObjectId of the killer (players and AI)
            public string killerName, weapon;
            public int killerHp, killerMaxHp;   // killer's health at the moment of the kill (-1 = unknown)
            public int killerTeam;
            public Vector3 killerPos;      // fallback look-at point if the killer object is gone
            public float time;             // local Time.unscaledTime when it arrived
        }

        public DeathInfo LastDeath { get; private set; }
        // Raised on the victim's machine only, when the server reports the local player's death.
        public event Action<DeathInfo> OnLocalDeath;

        readonly List<Entry> roster = new List<Entry>();

        // The name this local player wants to use (sent to the server on connect).
        string localName = "";
        bool localReadyWanted;
        int localLook = -1;

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
            localLook = AirsoftLook.LoadLocal();
            int saved = PlayerPrefs.GetInt(TIME_LIMIT_PREF, DEFAULT_TIME_LIMIT);
            TimeLimitMinutes = Array.IndexOf(TIME_LIMIT_OPTIONS, saved) >= 0 ? saved : DEFAULT_TIME_LIMIT;
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

            if (nm.IsServer) TickMatchClock();
            ObjectiveMode.Sync(this, nm);   // objective modes (CTF, Domination): create / remove the rules object
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

        public int LocalLook => localLook;

        // Character customizer calls this; the look is saved locally and synced to everyone.
        public void SetLocalLook(int look)
        {
            localLook = look;
            AirsoftLook.SaveLocal(look);
            if (nm != null && nm.IsClient && nm.IsConnectedClient)
                SendLookToServer();
        }

        public int LookOf(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return e.look;
            if (nm != null && clientId == nm.LocalClientId) return localLook;
            return -1;
        }

        void SendLookToServer()
        {
            if (nm == null || !nm.IsConnectedClient) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(localLook);
            nm.CustomMessagingManager.SendNamedMessage(MSG_LOOK, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        void OnServerRecvLook(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int look);
            EnsureEntry(sender);
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].clientId == sender) { var e = roster[i]; e.look = look; roster[i] = e; }
            BroadcastState();
        }

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
            var m = Mode;
            for (int i = 0; i < MODE_COUNT; i++)   // skip modes this map can't play
            {
                m = (GameModeType)(((int)m + 1) % MODE_COUNT);
                if (ObjectiveMode.IsAvailable(m, out _)) break;
            }
            Mode = m;
            BroadcastState();
        }

        // Host picks a mode directly (lobby mode cards).
        public void HostSetMode(GameModeType mode)
        {
            if (!IsHost || Mode == mode) return;
            if (!ObjectiveMode.IsAvailable(mode, out _)) return;   // the map lacks this mode's points
            Mode = mode;
            BroadcastState();
        }

        // Host picks the match time limit in the lobby (minutes, one of TIME_LIMIT_OPTIONS; 0 = off).
        public void HostSetTimeLimit(int minutes)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby || TimeLimitMinutes == minutes) return;
            if (Array.IndexOf(TIME_LIMIT_OPTIONS, minutes) < 0) return;
            TimeLimitMinutes = minutes;
            PlayerPrefs.SetInt(TIME_LIMIT_PREF, minutes);
            PlayerPrefs.Save();
            BroadcastState();
        }

        // True when the current mode plays with the time limit (Co-op never does).
        public bool UsesTimeLimit => Mode != GameModeType.Coop && TimeLimitMinutes > 0;

        // Seconds left on the match clock (valid on everyone), or -1 when there is no clock.
        // While the arena is still loading it reads the full limit.
        public float TimeLeft
        {
            get
            {
                if (CurrentPhase == Phase.Lobby || !UsesTimeLimit) return -1f;
                if (MatchEndTime <= 0) return CurrentPhase == Phase.InMatch ? TimeLimitMinutes * 60f : 0f;
                if (CurrentPhase == Phase.Results) return 0f;
                double now = nm != null ? nm.ServerTime.Time : 0;
                return Mathf.Max(0f, (float)(MatchEndTime - now));
            }
        }

        public static string FormatClock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
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
            if (!ObjectiveMode.IsAvailable(Mode, out _)) return;   // lobby shows why on the mode card
            EnemiesEnabled = (Mode == GameModeType.Coop);
            CurrentPhase = Phase.InMatch;
            WinnerClientId = ulong.MaxValue;
            WinnerTeam = 0;
            IsDraw = false;
            EndedOnTime = false;
            MatchEndTime = 0;   // the clock starts once the arena has loaded (TickMatchClock)
            // reset scores (and split into two teams for Team Deathmatch)
            int t = 1;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster[i];
                e.kills = 0; e.deaths = 0;
                e.team = TeamMode ? t : 0;
                if (TeamMode) t = (t == 1) ? 2 : 1;
                roster[i] = e;
            }
            BroadcastState();
            nm.SceneManager.LoadScene(MATCH_SCENE, LoadSceneMode.Single);
        }

        // ───────────────────────── Server-side match logic ─────────────────────────

        // Called by Health on the server when a player dies. killerObject is whoever dealt the
        // last hit if known (AI enemies pass themselves; players are looked up by client id).
        public void ReportKill(ulong victimClientId, ulong killerClientId, string weapon, NetworkObject killerObject = null)
        {
            if (nm == null || !nm.IsServer) return;

            bool pvpMode = Mode != GameModeType.Coop;
            bool teamKill = TeamMode && HasEntry(killerClientId)
                            && TeamOf(killerClientId) == TeamOf(victimClientId);
            bool scored = pvpMode && killerClientId != victimClientId && HasEntry(killerClientId) && !teamKill;
            string killerName = scored ? NameFor(killerClientId) : "";
            RaiseKill(killerName, NameFor(victimClientId), weapon ?? "");
            SendDeathInfo(victimClientId, killerClientId, weapon, killerObject);
            ObjectiveMode.NotifyKill(victimClientId, killerClientId);   // e.g. a flag carrier drops the flag

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
            else if (TeamMode) CheckTeamTarget();

            if (changed || CurrentPhase == Phase.Results) BroadcastState();
        }

        void CheckTeamTarget()
        {
            int target = TeamTarget;
            if (TeamScore(1) >= target) EndMatch(ulong.MaxValue, 1);
            else if (TeamScore(2) >= target) EndMatch(ulong.MaxValue, 2);
        }

        // Called by Health on the server when a BB hits a player in Milsim (no HP: one hit = out).
        // The shooter's team scores a point (kills = hits); the victim's deaths count times hit.
        public void ReportHit(ulong victimClientId, ulong shooterClientId, string weapon)
        {
            if (nm == null || !nm.IsServer || CurrentPhase != Phase.InMatch) return;
            bool scored = shooterClientId != victimClientId && HasEntry(shooterClientId)
                          && TeamOf(shooterClientId) != TeamOf(victimClientId);
            RaiseKill(scored ? NameFor(shooterClientId) : "", NameFor(victimClientId), weapon ?? "");
            ObjectiveMode.NotifyHit(victimClientId, shooterClientId);

            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster[i];
                if (e.clientId == victimClientId) { e.deaths++; roster[i] = e; }
                else if (scored && e.clientId == shooterClientId) { e.kills++; roster[i] = e; }
            }
            CheckTeamTarget();
            BroadcastState();
        }

        void EndMatch(ulong winnerClient, int winnerTeam, bool onTime = false, bool draw = false)
        {
            if (CurrentPhase == Phase.Results) return;
            CurrentPhase = Phase.Results;
            WinnerClientId = draw ? ulong.MaxValue : winnerClient;
            WinnerTeam = draw ? 0 : winnerTeam;
            IsDraw = draw;
            EndedOnTime = onTime;
            Invoke(nameof(ReturnToLobby), RESULTS_SECONDS);
        }

        // Server: start the clock once the arena is the active scene, end the match when it runs out.
        void TickMatchClock()
        {
            if (CurrentPhase != Phase.InMatch || !UsesTimeLimit) return;
            double now = nm.ServerTime.Time;
            if (MatchEndTime <= 0)
            {
                if (SceneManager.GetActiveScene().name != MATCH_SCENE) return;
                MatchEndTime = now + TimeLimitMinutes * 60.0;
                BroadcastState();
                return;
            }
            if (now >= MatchEndTime) EndMatchOnTime();
        }

        // Time ran out: FFA -> most kills; Gun Game -> highest weapon level, then kills, then
        // fewest deaths; TDM -> higher team score. An exact tie is a draw.
        void EndMatchOnTime()
        {
            // objective modes decide themselves (default: higher team score, tie = draw)
            if (ObjectiveMode.DecideOnTimeout(out int objWinner, out bool objDraw))
            {
                EndMatch(ulong.MaxValue, objWinner, true, objDraw);
                BroadcastState();
                return;
            }
            if (TeamMode)
            {
                int red = TeamScore(1), blue = TeamScore(2);
                EndMatch(ulong.MaxValue, red > blue ? 1 : 2, true, red == blue);
            }
            else
            {
                bool gunGame = Mode == GameModeType.GunGame;
                int best = -1; bool tie = false;
                for (int i = 0; i < roster.Count; i++)
                {
                    if (best < 0) { best = i; continue; }
                    int c = gunGame ? CompareGunGame(roster[i], roster[best]) : roster[i].kills.CompareTo(roster[best].kills);
                    if (c > 0) { best = i; tie = false; }
                    else if (c == 0) tie = true;
                }
                if (best < 0) EndMatch(ulong.MaxValue, 0, true, true);   // nobody left to win
                else EndMatch(roster[best].clientId, 0, true, tie);
            }
            BroadcastState();
        }

        // > 0 when a ranks above b in Gun Game.
        int CompareGunGame(Entry a, Entry b)
        {
            int c = GunLevelOf(a.clientId).CompareTo(GunLevelOf(b.clientId));
            if (c == 0) c = a.kills.CompareTo(b.kills);
            if (c == 0) c = b.deaths.CompareTo(a.deaths);
            return c;
        }

        // Host skips the rest of the results countdown.
        public void HostReturnToLobbyNow()
        {
            if (!IsHost || CurrentPhase != Phase.Results) return;
            CancelInvoke(nameof(ReturnToLobby));
            ReturnToLobby();
        }

        void ReturnToLobby()
        {
            if (nm == null || !nm.IsServer) return;
            CurrentPhase = Phase.Lobby;
            WinnerClientId = ulong.MaxValue;
            WinnerTeam = 0;
            IsDraw = false;
            EndedOnTime = false;
            MatchEndTime = 0;
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
            cm.RegisterNamedMessageHandler(MSG_LOOK,  OnServerRecvLook);
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
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MSG_DEATH, OnClientRecvDeath);
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
                SendLookToServer();
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
            WinnerTeam = 0;
            IsDraw = false;
            EndedOnTime = false;
            MatchEndTime = 0;
            LastDeath = default;
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
            roster.Add(new Entry { clientId = clientId, name = DefaultName(clientId), ready = false, kills = 0, deaths = 0, look = (nm != null && clientId == nm.LocalClientId) ? localLook : -1 });
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
            w.WriteValueSafe((byte)((IsDraw ? 1 : 0) | (EndedOnTime ? 2 : 0)));
            w.WriteValueSafe((byte)TimeLimitMinutes);
            w.WriteValueSafe(MatchEndTime);
            w.WriteValueSafe(roster.Count);
            foreach (var e in roster)
            {
                w.WriteValueSafe(e.clientId);
                w.WriteValueSafe(e.name ?? "");
                w.WriteValueSafe(e.ready);
                w.WriteValueSafe(e.kills);
                w.WriteValueSafe(e.deaths);
                w.WriteValueSafe(e.team);
                w.WriteValueSafe(e.look);
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
            reader.ReadValueSafe(out byte endFlags);
            reader.ReadValueSafe(out byte timeLimit);
            reader.ReadValueSafe(out double matchEnd);
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
                reader.ReadValueSafe(out int look);
                roster.Add(new Entry { clientId = cid, name = nm2, ready = ready, kills = kills, deaths = deaths, team = team, look = look });
            }

            Mode = (GameModeType)modeB;
            CurrentPhase = (Phase)phaseB;
            WinnerClientId = winner;
            WinnerTeam = winnerTeam;
            IsDraw = (endFlags & 1) != 0;
            EndedOnTime = (endFlags & 2) != 0;
            TimeLimitMinutes = timeLimit;
            MatchEndTime = matchEnd;
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

        // Server: tell the victim who eliminated them. Everything comes from the server's own
        // hit record (Health.TakeDamage), so a client can't fake its killer or the killer's HP.
        void SendDeathInfo(ulong victim, ulong killer, string weapon, NetworkObject killerObj)
        {
            if (killerObj == null && killer != victim && HasEntry(killer)
                && nm.ConnectedClients.TryGetValue(killer, out var client))
                killerObj = client.PlayerObject;

            var info = new DeathInfo { kind = KillerKind.None, killerClientId = ulong.MaxValue, killerName = "",
                                       weapon = weapon ?? "", killerHp = -1, killerMaxHp = -1 };
            var kh = killerObj != null && killerObj.IsSpawned ? killerObj.GetComponent<Health>() : null;
            bool player = kh != null && kh.isPlayer;
            if (kh != null && !(player && killerObj.OwnerClientId == victim))
            {
                info.kind = player ? KillerKind.Player : KillerKind.Enemy;
                info.killerClientId = player ? killerObj.OwnerClientId : ulong.MaxValue;
                info.killerObjectId = killerObj.NetworkObjectId;
                info.killerName = player ? NameFor(killerObj.OwnerClientId) : "Enemy soldier";
                info.killerTeam = player && TeamMode ? TeamOf(killerObj.OwnerClientId) : 0;
                info.killerHp = kh.currentHealth.Value;
                info.killerMaxHp = kh.maxHealth;
                info.killerPos = killerObj.transform.position;
            }

            if (victim == nm.LocalClientId) { ApplyDeathInfo(info); return; }   // host died
            using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
            w.WriteValueSafe((byte)info.kind);
            w.WriteValueSafe(info.killerClientId);
            w.WriteValueSafe(info.killerObjectId);
            w.WriteValueSafe(info.killerName);
            w.WriteValueSafe(info.weapon);
            w.WriteValueSafe(info.killerHp);
            w.WriteValueSafe(info.killerMaxHp);
            w.WriteValueSafe(info.killerTeam);
            w.WriteValueSafe(info.killerPos);
            nm.CustomMessagingManager.SendNamedMessage(MSG_DEATH, victim, w, NetworkDelivery.Reliable);
        }

        void OnClientRecvDeath(ulong sender, FastBufferReader reader)
        {
            if (nm == null || nm.IsServer || sender != NetworkManager.ServerClientId) return;
            var info = new DeathInfo();
            reader.ReadValueSafe(out byte kind);
            info.kind = (KillerKind)kind;
            reader.ReadValueSafe(out info.killerClientId);
            reader.ReadValueSafe(out info.killerObjectId);
            reader.ReadValueSafe(out info.killerName);
            reader.ReadValueSafe(out info.weapon);
            reader.ReadValueSafe(out info.killerHp);
            reader.ReadValueSafe(out info.killerMaxHp);
            reader.ReadValueSafe(out info.killerTeam);
            reader.ReadValueSafe(out info.killerPos);
            ApplyDeathInfo(info);
        }

        void ApplyDeathInfo(DeathInfo info)
        {
            info.time = Time.unscaledTime;
            LastDeath = info;
            OnLocalDeath?.Invoke(info);
        }

        public int TeamOf(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return e.team;
            return 0;
        }
        public int TeamScore(int team)
        {
            if (ObjectiveMode.TryTeamScore(team, out int obj)) return obj;   // CTF captures / Domination points
            int sc = 0; foreach (var e in roster) if (e.team == team) sc += e.kills; return sc;
        }
        public int GunLevelOf(ulong clientId)
        {
            foreach (var e in roster) if (e.clientId == clientId) return e.kills;
            return 0;
        }
        // Red vs Blue modes (Team Deathmatch, Milsim, Capture the Flag, Domination).
        public static bool IsTeamMode(GameModeType m) => m == GameModeType.TeamDeathmatch || m == GameModeType.Milsim
                                                         || m == GameModeType.CaptureTheFlag || m == GameModeType.Domination;
        public bool TeamMode => IsTeamMode(Mode);
        public bool IsMilsim => Mode == GameModeType.Milsim;
        // Team score that wins the match: kills in TDM, hits in Milsim.
        public int TeamTarget => ObjectiveMode.HasObjective(Mode) ? ObjectiveMode.TargetFor(Mode)
                                 : IsMilsim ? MILSIM_HIT_TARGET : TDM_KILL_TARGET;

        // Server: the active objective changed a team score; end the match if a team reached the target.
        public void NotifyObjectiveScore()
        {
            if (nm == null || !nm.IsServer || CurrentPhase != Phase.InMatch) return;
            CheckTeamTarget();
            if (CurrentPhase == Phase.Results) BroadcastState();
        }

        // Team modes: block damage between teammates (friendly fire off).
        public bool BlocksDamage(ulong attacker, ulong victim)
        {
            if (!TeamMode || attacker == victim) return false;
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
