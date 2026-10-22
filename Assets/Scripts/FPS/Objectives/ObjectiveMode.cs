using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // Base of the objective game modes (Capture the Flag, Domination; bomb / VIP escort / medic later).
    //
    // One instance lives in the match scene on every peer while a match of that mode runs: MatchNet
    // calls Sync every frame, which creates it once the map is loaded (it dies with the scene). The
    // server runs the rules (ServerTick + the kill / hit hooks) and owns the team score and per-player
    // objective points; clients get the state through a named message (no NetworkObject needed, like
    // MatchNet) and only drive visuals and HUD. MatchNet asks the active objective for the team score,
    // the score target and the winner when time runs out.
    //
    // A new mode: add a GameModeType value, register it in `defs`, subclass this and implement
    // OnBegin / ServerTick / WriteState / ReadState (+ BuildHud / UpdateHud for its HUD widget).
    public abstract class ObjectiveMode : MonoBehaviour
    {
        public static ObjectiveMode Active { get; private set; }

        // ───────────────────────── registry ─────────────────────────
        struct Def
        {
            public Type type;
            public int target;          // team score that wins
            public string unit;         // "captures", "points"
            public string pointsLabel;  // scoreboard column
            public Func<MapCaps, string> missing;   // null = map has what the mode needs, else a short reason
            public bool hitRule;        // Milsim HIT rule: one BB = out (no HP), hand up
            public bool ownClock;       // round modes run their own round timer (no match time limit)
        }

        static readonly Dictionary<MatchNet.GameModeType, Def> defs = new Dictionary<MatchNet.GameModeType, Def>
        {
            [MatchNet.GameModeType.CaptureTheFlag] = new Def
            {
                type = typeof(CaptureTheFlag), target = CaptureTheFlag.CAPTURE_TARGET, unit = "captures",
                pointsLabel = "CAPS", missing = CaptureTheFlag.MissingOnMap
            },
            [MatchNet.GameModeType.Domination] = new Def
            {
                type = typeof(Domination), target = Domination.SCORE_TARGET, unit = "points",
                pointsLabel = "CAPS", missing = Domination.MissingOnMap
            },
            [MatchNet.GameModeType.Medic] = new Def
            {
                type = typeof(Medic), target = 0, unit = "tickets",   // no score race: tickets run down
                pointsLabel = "REVIVES", missing = null, hitRule = true
            },
            [MatchNet.GameModeType.Bomb] = new Def
            {
                type = typeof(BombMode), target = BombMode.ROUNDS_TO_WIN, unit = "rounds",
                pointsLabel = "OBJ", missing = BombMode.MissingOnMap, ownClock = true
            },
            [MatchNet.GameModeType.VipEscort] = new Def
            {
                type = typeof(VipEscort), target = VipEscort.ROUNDS_TO_WIN, unit = "rounds",
                pointsLabel = "OBJ", missing = VipEscort.MissingOnMap, ownClock = true
            },
        };

        public static bool HasObjective(MatchNet.GameModeType m) => defs.ContainsKey(m);

        // Team score that wins an objective mode (0 = not an objective mode).
        public static int TargetFor(MatchNet.GameModeType m) => defs.TryGetValue(m, out var d) ? d.target : 0;

        // " captures" / " points" ("" for non-objective modes), for "first team to 3 captures".
        public static string UnitFor(MatchNet.GameModeType m) => defs.TryGetValue(m, out var d) ? " " + d.unit : "";

        public static string PointsLabelFor(MatchNet.GameModeType m) => defs.TryGetValue(m, out var d) ? d.pointsLabel : "";

        // Plays with the Milsim HIT rule (one BB = out, raised hand) instead of HP.
        public static bool UsesHitRule(MatchNet.GameModeType m) => defs.TryGetValue(m, out var d) && d.hitRule;

        // Round modes (Bomb, VIP escort) time their own rounds: the lobby's match time limit doesn't apply.
        public static bool HasOwnClock(MatchNet.GameModeType m) => defs.TryGetValue(m, out var d) && d.ownClock;

        // Can the current match map play this mode? reason = short text for the lobby card when not.
        public static bool IsAvailable(MatchNet.GameModeType m, out string reason)
        {
            reason = null;
            if (!defs.TryGetValue(m, out var d) || d.missing == null) return true;
            var caps = MapManifest.For(MatchNet.CurrentMapScene);
            if (!caps.known) { reason = "Not available: this map has no layout data"; return false; }
            reason = d.missing(caps);
            return reason == null;
        }

        // ───────────────────────── MatchNet hooks (all null-safe) ─────────────────────────

        // Every frame on every peer: create / remove the objective for the current mode and scene.
        public static void Sync(MatchNet mn, NetworkManager nm)
        {
            if (mn == null || nm == null) return;
            EnsureHandlers(nm);
            Medic.LobbySync(nm);   // medic volunteers (lobby) + medic input messages
            bool want = nm.IsListening && mn.CurrentPhase != MatchNet.Phase.Lobby && HasObjective(mn.Mode)
                        && SceneManager.GetActiveScene().name == MatchNet.CurrentMapScene;
            if (want && (Active == null || Active.Mode != mn.Mode))
            {
                if (Active != null) Destroy(Active.gameObject);
                Create(mn.Mode);
            }
            else if (!want && Active != null && (mn.CurrentPhase == MatchNet.Phase.Lobby || !nm.IsListening))
            {
                Destroy(Active.gameObject);
                Active = null;
            }
        }

        static void Create(MatchNet.GameModeType mode)
        {
            var go = new GameObject("~Objective_" + mode);   // in the active (match) scene: dies with it
            var obj = (ObjectiveMode)go.AddComponent(defs[mode].type);
            obj.Mode = mode;
            Active = obj;
            obj.OnBegin();
        }

        public static void NotifyKill(ulong victim, ulong killer)
        {
            if (Active != null && IsServerNow) Active.ServerPlayerOut(victim, killer);
        }

        // Milsim HIT rule: a HIT player is "out" just like a dead one.
        public static void NotifyHit(ulong victim, ulong shooter)
        {
            if (Active != null && IsServerNow) Active.ServerPlayerOut(victim, shooter);
        }

        public static bool TryTeamScore(int team, out int score)
        {
            score = 0;
            if (Active == null || team < 1 || team > 2) return false;
            score = Active.teamScore[team];
            return true;
        }

        // Time ran out: who wins? false = no objective (MatchNet decides as before).
        public static bool DecideOnTimeout(out int winnerTeam, out bool draw)
        {
            winnerTeam = 0; draw = false;
            if (Active == null) return false;
            Active.WinnerOnTimeout(out winnerTeam, out draw);
            return true;
        }

        // FPSController: e.g. a flag carrier can't sprint.
        public static bool CannotSprint(ulong clientId) => Active != null && Active.BlocksSprint(clientId);

        // ── round modes (Bomb, VIP escort): respawn, spawn, movement and weapon rules ──

        // Server (Health.Respawn): keep this dead player down for now (e.g. no respawn during a bomb round).
        public static bool HoldsRespawn(ulong clientId) => Active != null && Active.HoldRespawn(clientId);

        // Server (PlayerSpawner): where this player spawns instead of the usual team side. false = no override.
        public static bool TrySpawnFor(ulong clientId, out Pose pose)
        {
            pose = default;
            return Active != null && Active.SpawnOverride(clientId, out pose);
        }

        // Owner (FPSController): walk speed factor (0 = frozen, e.g. before a round starts).
        public static float SpeedFactor(ulong clientId) => Active != null ? Active.MoveFactor(clientId) : 1f;

        // Owner + server (WeaponController): may this player use this weapon (e.g. the VIP: sidearms only)?
        public static bool WeaponAllowed(ulong clientId, WeaponData w) => Active == null || w == null || Active.AllowsWeapon(clientId, w);

        // Local HUD: the local player is dead and watching a teammate (no death screen).
        public static bool LocalSpectating => Active != null && Active.IsLocalSpectating;

        // Local death screen: true = this mode decides the respawn. delay = seconds from death, or -1 when the
        // player stays down (heldText says until when, e.g. "the next round").
        public static bool LocalRespawnRule(out float delay, out string heldText)
        {
            delay = 0f; heldText = null;
            return Active != null && Active.RespawnRule(out delay, out heldText);
        }

        protected virtual bool HoldRespawn(ulong clientId) => false;
        protected virtual bool SpawnOverride(ulong clientId, out Pose pose) { pose = default; return false; }
        protected virtual float MoveFactor(ulong clientId) => 1f;
        protected virtual bool AllowsWeapon(ulong clientId, WeaponData w) => true;
        protected virtual bool IsLocalSpectating => false;
        protected virtual bool RespawnRule(out float delay, out string heldText) { delay = 0f; heldText = null; return false; }

        public static int PointsOf(ulong clientId) =>
            Active != null && Active.playerPoints.TryGetValue(clientId, out int p) ? p : 0;

        // Downed in place (Medic): can't move, kneels (FPSController, PlayerAvatar). Any peer.
        public static bool IsDowned(ulong clientId) => Active != null && Active.Downed(clientId);

        // Server: keep this HIT player out (no Milsim walk-back respawn) for now. Health.TickHitRespawn.
        // Clients: the Milsim HIT overlay stays hidden (the mode shows its own).
        public static bool HoldsHit(ulong clientId) => Active != null && Active.HoldsOut(clientId);

        // Scoreboard: extra text after a player's name ("" = none).
        public static string TagFor(ulong clientId) => Active != null ? Active.NameTag(clientId) ?? "" : "";

        // HUD team score bar: replaces "FIRST TO <target>" (false = keep it).
        public static bool TryScoreCaption(out string caption, out string value)
        {
            caption = value = null;
            return Active != null && Active.ScoreCaption(out caption, out value);
        }

        // Results banner subtitle (null = the default "first team to ..." / "higher score ...").
        public static string ResultLine(int winnerTeam, bool onTime, bool draw) =>
            Active != null ? Active.ResultText(winnerTeam, onTime, draw) : null;

        // The mode ended the match itself (e.g. a team out of tickets): MatchNet asks this when
        // NotifyObjectiveScore runs. false = no result yet.
        public static bool TryDeclaredResult(out int winnerTeam, out bool draw)
        {
            winnerTeam = 0; draw = false;
            if (Active == null || !Active.HasDeclared) return false;
            winnerTeam = Active.DeclaredWinner; draw = Active.DeclaredDraw;
            return true;
        }

        // Objective events ("Alex took the BLUE flag"): text + team colour (0 = neutral). Raised on everyone.
        public static event Action<string, int> OnAnnounce;

        // Bots (or anything not a connected client's player object) can join the objective logic here.
        public static event Action<List<Participant>> CollectExtraParticipants;
        public static Func<ulong, Transform> ResolveExtraTransform;

        // ───────────────────────── instance ─────────────────────────
        public MatchNet.GameModeType Mode { get; private set; }

        protected readonly int[] teamScore = new int[3];
        protected readonly Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();

        public int TeamScore(int team) => team >= 1 && team <= 2 ? teamScore[team] : 0;
        public int PointsFor(ulong id) => playerPoints.TryGetValue(id, out int p) ? p : 0;
        public int ScoreTarget => TargetFor(Mode);
        public string PointsLabel => PointsLabelFor(Mode);
        public string Unit => UnitFor(Mode);

        public struct Participant
        {
            public ulong id;
            public int team;
            public Vector3 position;
            public bool active;   // alive and not HIT
        }

        protected readonly List<Participant> participants = new List<Participant>();

        // Test hooks (editor logic checks): fixed participants and clock instead of the network.
        [NonSerialized] public Func<List<Participant>> testParticipants;
        [NonSerialized] public double testNow = -1;

        // Editor logic checks: an objective that isn't Active and isn't networked (set testParticipants first).
        public static ObjectiveMode CreateDetached(MatchNet.GameModeType mode, GameObject go)
        {
            var o = (ObjectiveMode)go.AddComponent(defs[mode].type);
            o.Mode = mode;
            return o;
        }

        bool dirty = true;
        float sinceSend;

        protected static bool IsServerNow
        {
            get { var nm = NetworkManager.Singleton; return nm == null || nm.IsServer; }
        }

        protected bool IsServer => testParticipants != null || IsServerNow;

        protected double Now
        {
            get
            {
                if (testNow >= 0) return testNow;
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
            }
        }

        // Setup on every peer (map points, visuals).
        protected abstract void OnBegin();
        // Server only, while the match is running.
        public abstract void ServerTick(float dt);
        // Server: a player was eliminated (death, or HIT in Milsim).
        public virtual void ServerPlayerOut(ulong victim, ulong killer) { }
        protected virtual bool BlocksSprint(ulong clientId) => false;
        protected virtual bool Downed(ulong clientId) => false;
        protected virtual bool HoldsOut(ulong clientId) => false;
        protected virtual string NameTag(ulong clientId) => "";
        protected virtual bool ScoreCaption(out string caption, out string value) { caption = value = null; return false; }
        protected virtual string ResultText(int winnerTeam, bool onTime, bool draw) => null;

        // Match result decided by the mode (server). Read by MatchNet through TryDeclaredResult.
        public bool HasDeclared { get; private set; }
        public int DeclaredWinner { get; private set; }
        public bool DeclaredDraw { get; private set; }

        protected void DeclareWinner(int winnerTeam, bool draw = false)
        {
            if (HasDeclared) return;
            HasDeclared = true;
            DeclaredWinner = draw ? 0 : winnerTeam;
            DeclaredDraw = draw;
            dirty = true;
            if (testParticipants == null) MatchNet.Instance?.NotifyObjectiveScore();
        }

        public virtual void WinnerOnTimeout(out int winnerTeam, out bool draw)
        {
            draw = teamScore[1] == teamScore[2];
            winnerTeam = draw ? 0 : teamScore[1] > teamScore[2] ? 1 : 2;
        }

        // Mode-specific state after the shared header (scores, player points).
        protected abstract void WriteState(ref FastBufferWriter w);
        protected abstract void ReadState(ref FastBufferReader r);
        // Client: called after a state update arrived.
        protected virtual void OnStateApplied() { }
        // Every frame on every peer (visuals).
        protected virtual void VisualUpdate() { }

        // HUD (ObjectiveHUD calls these on the local machine).
        public virtual void BuildHud(RectTransform widget, RectTransform markers) { }
        public virtual void UpdateHud(Camera cam, RectTransform markers) { }

        protected void MarkDirty() => dirty = true;

        protected void AddTeamScore(int team, int amount)
        {
            if (team < 1 || team > 2 || amount == 0) return;
            teamScore[team] += amount;
            dirty = true;
            if (testParticipants == null) MatchNet.Instance?.NotifyObjectiveScore();
        }

        protected void AddPlayerPoints(ulong id, int amount)
        {
            playerPoints.TryGetValue(id, out int p);
            playerPoints[id] = p + amount;
            dirty = true;
        }

        protected void Announce(string text, int team)
        {
            OnAnnounce?.Invoke(text, team);
            var nm = NetworkManager.Singleton;
            if (testParticipants != null || nm == null || !nm.IsServer || !nm.IsListening) return;
            using var w = new FastBufferWriter(128, Allocator.Temp, 1024);
            w.WriteValueSafe((byte)Mode);
            w.WriteValueSafe(text ?? "");
            w.WriteValueSafe(team);
            nm.CustomMessagingManager.SendNamedMessageToAll(MSG_EVENT, w, NetworkDelivery.Reliable);
        }

        protected static string TeamName(int team) => team == 1 ? "RED" : team == 2 ? "BLUE" : "";

        protected static string NameOf(ulong id) => MatchNet.Instance != null ? MatchNet.Instance.NameFor(id) : "Player " + id;

        // Server: who can take part right now (connected players' avatars + extras such as bots).
        protected void GatherParticipants()
        {
            participants.Clear();
            if (testParticipants != null) { participants.AddRange(testParticipants()); return; }
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            if (nm == null || !nm.IsServer) return;
            foreach (var id in nm.ConnectedClientsIds)
            {
                if (!nm.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null) continue;
                var h = cl.PlayerObject.GetComponent<Health>();
                participants.Add(new Participant
                {
                    id = id,
                    team = mn != null ? mn.TeamOf(id) : 0,
                    position = cl.PlayerObject.transform.position,
                    // a remote player who just respawned is still at the death spot on the server until
                    // its teleport arrives: not active until then (no flag pickups / captures from there)
                    active = h == null || (h.IsSpawned && !h.isDead.Value && !h.IsOut && !h.ServerAwaitingRespawnMove)
                });
            }
            CollectExtraParticipants?.Invoke(participants);
        }

        protected bool TryGetParticipant(ulong id, out Participant p)
        {
            foreach (var q in participants) if (q.id == id) { p = q; return true; }
            p = default;
            return false;
        }

        protected static bool Near(Vector3 a, Vector3 b, float radius, float height = 2.5f)
        {
            if (Mathf.Abs(a.y - b.y) > height) return false;
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        // Any peer: the avatar transform of a player (or bot) id, cached briefly.
        readonly Dictionary<ulong, (Transform t, float until)> transformCache = new Dictionary<ulong, (Transform, float)>();

        protected Transform FindPlayer(ulong id)
        {
            if (transformCache.TryGetValue(id, out var c) && c.t != null && Time.unscaledTime < c.until) return c.t;
            Transform found = null;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                // NGO only lets the server (or the owner itself) look up player objects; on a client it
                // logs an error for anyone else, so clients scan the spawned Health components instead.
                if (nm.IsServer || id == nm.LocalClientId)
                {
                    var no = nm.SpawnManager?.GetPlayerNetworkObject(id);
                    if (no != null) found = no.transform;
                }
                if (found == null)
                    foreach (var h in FindObjectsByType<Health>())
                        if (h.isPlayer && h.IsSpawned && h.OwnerClientId == id && h.NetworkObject.IsPlayerObject) { found = h.transform; break; }
            }
            if (found == null && ResolveExtraTransform != null) found = ResolveExtraTransform(id);
            transformCache[id] = (found, Time.unscaledTime + 0.5f);
            return found;
        }

        protected virtual void Update()
        {
            var mn = MatchNet.Instance;
            var nm = NetworkManager.Singleton;
            if (mn != null && nm != null && nm.IsServer)
            {
                if (mn.CurrentPhase == MatchNet.Phase.InMatch) ServerTick(Time.deltaTime);
                sinceSend += Time.unscaledDeltaTime;
                if ((dirty && sinceSend >= 0.1f) || sinceSend >= 1f) Broadcast(nm);
            }
            VisualUpdate();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        // ───────────────────────── networking ─────────────────────────
        const string MSG_STATE = "obj_state";   // server -> clients
        const string MSG_EVENT = "obj_event";   // server -> clients
        static CustomMessagingManager handlersOn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = null; handlersOn = null;
            OnAnnounce = null; CollectExtraParticipants = null; ResolveExtraTransform = null;
        }

        static void EnsureHandlers(NetworkManager nm)
        {
            var cmm = nm.IsListening ? nm.CustomMessagingManager : null;
            if (cmm == null || cmm == handlersOn) return;
            cmm.RegisterNamedMessageHandler(MSG_STATE, OnRecvState);
            cmm.RegisterNamedMessageHandler(MSG_EVENT, OnRecvEvent);
            handlersOn = cmm;
        }

        void Broadcast(NetworkManager nm)
        {
            dirty = false;
            sinceSend = 0f;
            if (!nm.IsListening) return;
            var w = new FastBufferWriter(512, Allocator.Temp, 32 * 1024);
            try
            {
                w.WriteValueSafe((byte)Mode);
                w.WriteValueSafe(teamScore[1]);
                w.WriteValueSafe(teamScore[2]);
                w.WriteValueSafe(playerPoints.Count);
                foreach (var kv in playerPoints) { w.WriteValueSafe(kv.Key); w.WriteValueSafe(kv.Value); }
                WriteState(ref w);
                nm.CustomMessagingManager.SendNamedMessageToAll(MSG_STATE, w, NetworkDelivery.ReliableSequenced);
            }
            finally { w.Dispose(); }
        }

        static void OnRecvState(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out byte mode);
            var a = Active;
            if (a == null || (byte)a.Mode != mode) return;   // not loaded yet: the next full update fixes it
            r.ReadValueSafe(out a.teamScore[1]);
            r.ReadValueSafe(out a.teamScore[2]);
            r.ReadValueSafe(out int n);
            a.playerPoints.Clear();
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out ulong id);
                r.ReadValueSafe(out int p);
                a.playerPoints[id] = p;
            }
            a.ReadState(ref r);
            a.OnStateApplied();
        }

        static void OnRecvEvent(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out byte mode);
            r.ReadValueSafe(out string text);
            r.ReadValueSafe(out int team);
            if (Active == null || (byte)Active.Mode != mode) return;
            OnAnnounce?.Invoke(text, team);
        }

        // ───────────────────────── shared visual helpers ─────────────────────────
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

        // Opaque coloured material (URP Lit is always in builds), or transparent (Sprites/Default).
        protected static Material Mat(Color c, bool transparent = false)
        {
            if (mats.TryGetValue(c, out var m) && m != null) return m;
            Shader sh = transparent ? Shader.Find("Sprites/Default") : Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            m = new Material(sh) { name = "Objective_" + ColorUtility.ToHtmlStringRGBA(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            mats[c] = m;
            return m;
        }

        // A primitive without a collider (visual only: never blocks BBs or players).
        protected static Transform Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material m, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        protected static void Recolor(Transform t, Material m)
        {
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
        }

        public static Color TeamTint(int team) => team == 1 ? UIKit.Red : team == 2 ? UIKit.Blue : UIKit.Hex("#C9CCBF");
    }
}
