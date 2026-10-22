using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Lobby map vote (part of MatchNet). Every player votes for one map of MapCatalog; the map with
    // the most votes is played. A tie goes to the host's vote, and the host may lock a map outright.
    // Votes travel in their own messages next to the roster state, so the rest of MatchNet is unchanged:
    // the server re-sends them with every BroadcastState and whenever a vote changes.
    public partial class MatchNet
    {
        const string MSG_MAP_VOTE  = "mn_mapvote";   // client -> server: map index (-1 = no vote)
        const string MSG_MAP_STATE = "mn_maps";      // server -> clients: votes, host lock, current map

        // clientId -> voted map index (server-authoritative; mirrored on clients)
        readonly Dictionary<ulong, int> mapVotes = new Dictionary<ulong, int>();

        // Map the host locked in the lobby (-1 = the vote decides).
        public int HostMapLock { get; private set; } = -1;

        // Map of the current (or last played) match; index into MapCatalog.Maps.
        public int MatchMapIndex { get; private set; } = 0;

        // Scene of the current / last match.
        public string MatchScene => MapCatalog.SceneOf(MatchMapIndex);

        // Lobby: the map START MATCH would load; match / results: the map being played (CurrentMapScene).
        string VotedOrMatchScene => CurrentPhase == Phase.Lobby ? MapCatalog.SceneOf(WinningMap) : MatchScene;

        public int MapVoteOf(ulong clientId) => mapVotes.TryGetValue(clientId, out int v) ? v : -1;

        public int LocalMapVote => nm != null ? MapVoteOf(nm.LocalClientId) : -1;

        // Votes for a map, counting only players still in the room.
        public int MapVotes(int map)
        {
            int n = 0;
            foreach (var e in roster) if (MapVoteOf(e.clientId) == map) n++;
            return n;
        }

        // The map START MATCH would load right now: the host lock, else the most votes. Ties go to the
        // host's vote, then to the map played last, then to the first one in the catalog.
        public int WinningMap
        {
            get
            {
                if (MapCatalog.Valid(HostMapLock)) return HostMapLock;
                int best = -1;
                for (int i = 0; i < MapCatalog.Count; i++) best = System.Math.Max(best, MapVotes(i));
                if (best <= 0) return MapCatalog.Valid(MatchMapIndex) ? MatchMapIndex : 0;
                int hostVote = MapVoteOf(NetworkManager.ServerClientId);
                if (MapCatalog.Valid(hostVote) && MapVotes(hostVote) == best) return hostVote;
                if (MapCatalog.Valid(MatchMapIndex) && MapVotes(MatchMapIndex) == best) return MatchMapIndex;
                for (int i = 0; i < MapCatalog.Count; i++) if (MapVotes(i) == best) return i;
                return 0;
            }
        }

        // Local player votes for a map (clicking the voted map again takes the vote back).
        public void VoteMap(int map)
        {
            if (nm == null || !nm.IsConnectedClient || CurrentPhase != Phase.Lobby) return;
            if (map == LocalMapVote) map = -1;
            if (!MapCatalog.Valid(map)) map = -1;
            if (nm.IsServer) { SetVote(nm.LocalClientId, map); BroadcastMapVote(); return; }
            SetVote(nm.LocalClientId, map);   // show it right away; the server's state follows
            OnStateChanged?.Invoke();
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(map);
            nm.CustomMessagingManager.SendNamedMessage(MSG_MAP_VOTE, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        // Host locks a map regardless of the votes (-1 unlocks: the vote decides again).
        public void HostLockMap(int map)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby) return;
            if (!MapCatalog.Valid(map)) map = -1;
            if (map == HostMapLock) return;
            HostMapLock = map;
            BroadcastMapVote();
        }

        void SetVote(ulong clientId, int map)
        {
            if (MapCatalog.Valid(map)) mapVotes[clientId] = map;
            else mapVotes.Remove(clientId);
        }

        // Server: pick the map when the match starts (HostStartMatch).
        void ChooseMatchMap() => MatchMapIndex = WinningMap;

        // Server: a fresh vote for the next match (ReturnToLobby).
        void ResetMapVote()
        {
            mapVotes.Clear();
            HostMapLock = -1;
        }

        // Session ended locally (HandleClientStopped).
        void ClearMapVoteState()
        {
            mapVotes.Clear();
            HostMapLock = -1;
            MatchMapIndex = 0;
        }

        void RegisterMapVoteServerHandlers(CustomMessagingManager cm)
            => cm.RegisterNamedMessageHandler(MSG_MAP_VOTE, OnServerRecvMapVote);

        void RegisterMapVoteClientHandlers(CustomMessagingManager cm)
            => cm.RegisterNamedMessageHandler(MSG_MAP_STATE, OnClientRecvMapState);

        void OnServerRecvMapVote(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int map);
            if (CurrentPhase != Phase.Lobby || !HasEntry(sender)) return;
            SetVote(sender, map);
            BroadcastMapVote();
        }

        void BroadcastMapVote()
        {
            OnStateChanged?.Invoke();
            SendMapVoteState();
        }

        // Server -> everyone. Also called at the end of BroadcastState so late joiners get it.
        void SendMapVoteState()
        {
            if (nm == null || !nm.IsServer || !nm.IsListening) return;
            using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
            w.WriteValueSafe(HostMapLock);
            w.WriteValueSafe(MatchMapIndex);
            int n = 0;
            foreach (var e in roster) if (MapCatalog.Valid(MapVoteOf(e.clientId))) n++;
            w.WriteValueSafe(n);
            foreach (var e in roster)
            {
                int v = MapVoteOf(e.clientId);
                if (!MapCatalog.Valid(v)) continue;
                w.WriteValueSafe(e.clientId);
                w.WriteValueSafe(v);
            }
            nm.CustomMessagingManager.SendNamedMessageToAll(MSG_MAP_STATE, w, NetworkDelivery.Reliable);
        }

        void OnClientRecvMapState(ulong sender, FastBufferReader reader)
        {
            if (nm != null && nm.IsServer) return;   // the host has the authoritative copy
            reader.ReadValueSafe(out int hostLock);
            reader.ReadValueSafe(out int matchMap);
            reader.ReadValueSafe(out int count);
            mapVotes.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong cid);
                reader.ReadValueSafe(out int v);
                SetVote(cid, v);
            }
            HostMapLock = MapCatalog.Valid(hostLock) ? hostLock : -1;
            MatchMapIndex = MapCatalog.Valid(matchMap) ? matchMap : 0;
            OnStateChanged?.Invoke();
        }
    }
}
