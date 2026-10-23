using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Medic (Red vs Blue) on the Milsim HIT rule: one BB = out, hand up (ObjectiveMode def hitRule).
    //
    // A HIT player goes down where they stand (kneeling, hand raised) and bleeds out over BLEED_SECONDS.
    // A medic of the same team who is in the game revives them by holding E next to them for
    // REVIVE_SECONDS: back at their spot right away, with a short spawn protection. Each team has
    // one medic per started group of PLAYERS_PER_MEDIC players (picked at match start, volunteers
    // from the lobby first; a medic who leaves is replaced). Medics can't revive themselves.
    //
    // Bleeding out (or giving up with G) means the Milsim walk-back: back to the team zone, respawn
    // there, and that costs the team one ticket. A team out of tickets can't respawn any more; once
    // all of its players are out it loses. Time limit: more tickets left wins, a tie is a draw.
    //
    // The team score (score bar, scoreboard) is the team's tickets; player points are revives.
    public class Medic : ObjectiveMode
    {
        public const float BLEED_SECONDS = 45f;
        public const float REVIVE_SECONDS = 4f;
        public const float REVIVE_RADIUS = 2.2f;      // metres between the medic and the downed player
        public const float REVIVE_PROTECTION = 2f;    // spawn protection after a revive
        public const float GIVE_UP_HOLD = 1f;         // hold G this long to give up
        public const int PLAYERS_PER_MEDIC = 4;
        public const int BASE_TICKETS = 25;           // for a team of 4 (TicketsFor scales it)

        public static readonly Color MedicRed = Hex("#E0352B");
        public static readonly Color MedicWhite = Hex("#F2F2EC");

        // Tickets a team starts with: 25 for four players, 5 fewer per missing player (min 10),
        // 5 more per extra player.
        public static int TicketsFor(int teamSize) =>
            teamSize <= 4 ? 5 + 5 * Mathf.Max(1, teamSize) : BASE_TICKETS + 5 * (teamSize - 4);

        // One medic per started group of four.
        public static int MedicsFor(int teamSize) => teamSize <= 0 ? 0 : (teamSize + PLAYERS_PER_MEDIC - 1) / PLAYERS_PER_MEDIC;

        public enum CasualtyState : byte { Downed = 1, WalkingBack = 2, Eliminated = 3 }

        public class Casualty
        {
            public ulong id;
            public int team;
            public CasualtyState state;
            public double bleedAt;        // server time the bleed-out ends (Downed)
            public Vector3 pos;
            public ulong reviver = NONE;  // medic reviving right now
            public float progress;        // 0..1
        }

        const ulong NONE = ulong.MaxValue;

        public readonly Dictionary<ulong, Casualty> casualties = new Dictionary<ulong, Casualty>();
        public readonly HashSet<ulong> medics = new HashSet<ulong>();
        readonly int[] startTickets = new int[3];
        bool teamsReady;
        float medicCheck;
        readonly System.Random rng = new System.Random();

        // server input (from the clients' messages)
        readonly HashSet<ulong> holding = new HashSet<ulong>();        // medics holding E
        readonly Dictionary<ulong, ulong> lockOn = new Dictionary<ulong, ulong>();   // medic -> casualty
        readonly HashSet<ulong> giveUps = new HashSet<ulong>();

        // Editor logic checks: who is revived (instead of the Health component), volunteers.
        [NonSerialized] public Action<ulong> testRevived;
        [NonSerialized] public HashSet<ulong> testVolunteers;

        public static Medic ActiveMedic => Active as Medic;
        public static bool IsMedic(ulong id) => Active is Medic m && m.medics.Contains(id);
        public int StartTickets(int team) => team == 1 || team == 2 ? startTickets[team] : 0;
        public int Tickets(int team) => TeamScore(team);
        public CasualtyState? StateOf(ulong id) => casualties.TryGetValue(id, out var c) ? c.state : (CasualtyState?)null;

        protected override bool Downed(ulong id) => casualties.TryGetValue(id, out var c) && c.state == CasualtyState.Downed;
        protected override bool HoldsOut(ulong id) =>
            casualties.TryGetValue(id, out var c) && (c.state == CasualtyState.Downed || c.state == CasualtyState.Eliminated);

        // ───────────────────────── setup ─────────────────────────
        protected override void OnBegin() { }

        // Test / server: set the input of a medic directly (the network messages end up here).
        public void ServerSetHolding(ulong id, bool on) { if (on) holding.Add(id); else { holding.Remove(id); lockOn.Remove(id); } }
        public void ServerGiveUp(ulong id) => giveUps.Add(id);

        // ───────────────────────── server rules ─────────────────────────
        public override void ServerTick(float dt)
        {
            GatherParticipants();
            if (!teamsReady) InitTeams();
            medicCheck -= dt;
            if (medicCheck <= 0f) { medicCheck = 1f; UpdateMedics(); }

            double now = Now;
            var gone = new List<ulong>();
            foreach (var c in casualties.Values)
            {
                if (!TryGetParticipant(c.id, out var p)) { gone.Add(c.id); continue; }   // left the match
                if (p.active) { gone.Add(c.id); continue; }                             // back in (zone respawn)
                c.pos = p.position;
            }
            foreach (var id in gone) { casualties.Remove(id); MarkDirty(); }

            // anyone out without an entry yet (the hit hook normally adds them first)
            foreach (var p in participants)
                if (!p.active && (p.team == 1 || p.team == 2) && !casualties.ContainsKey(p.id)) AddDowned(p.id, p.team, p.position, now);

            var bleed = new List<Casualty>();
            foreach (var c in casualties.Values)
                if (c.state == CasualtyState.Downed && (now >= c.bleedAt || giveUps.Contains(c.id))) bleed.Add(c);
            foreach (var c in bleed) BleedOut(c, giveUps.Contains(c.id) && now < c.bleedAt);
            giveUps.Clear();

            UpdateBotMedicHolding();
            TickRevives(dt);
            CheckElimination();
        }

        IEnumerable<(ulong id, int team)> Members()
        {
            if (testParticipants != null)
            {
                foreach (var p in participants) yield return (p.id, p.team);
                yield break;
            }
            var mn = MatchNet.Instance;
            if (mn == null) yield break;
            foreach (var e in mn.Roster) yield return (e.clientId, e.team);
        }

        void InitTeams()
        {
            int[] n = new int[3];
            foreach (var (id, team) in Members()) if (team == 1 || team == 2) n[team]++;
            if (n[1] + n[2] == 0) return;   // roster not in yet
            teamsReady = true;
            for (int t = 1; t <= 2; t++)
            {
                startTickets[t] = TicketsFor(n[t]);
                teamScore[t] = startTickets[t];
            }
            UpdateMedics();
            MarkDirty();
        }

        // Keep MedicsFor(team size) medics per team: volunteers first, then anyone. Medics stay medics.
        void UpdateMedics()
        {
            var members = new Dictionary<ulong, int>();
            foreach (var (id, team) in Members()) if (team == 1 || team == 2) members[id] = team;
            int removed = medics.RemoveWhere(id => !members.ContainsKey(id));
            bool changed = removed > 0;
            for (int t = 1; t <= 2; t++)
            {
                int size = 0, have = 0;
                var vols = new List<ulong>();
                var others = new List<ulong>();
                foreach (var kv in members)
                {
                    if (kv.Value != t) continue;
                    size++;
                    if (medics.Contains(kv.Key)) { have++; continue; }
                    if (IsVolunteerId(kv.Key)) vols.Add(kv.Key); else others.Add(kv.Key);
                }
                int want = MedicsFor(size);
                while (have < want && vols.Count + others.Count > 0)
                {
                    var pool = vols.Count > 0 ? vols : others;
                    int i = rng.Next(pool.Count);
                    ulong pick = pool[i];
                    pool.RemoveAt(i);
                    medics.Add(pick);
                    have++;
                    changed = true;
                    if (removed > 0) Announce($"{NameOf(pick)} is now a {TeamName(t)} medic", t);
                }
            }
            if (changed) MarkDirty();
        }

        bool IsVolunteerId(ulong id) => testVolunteers != null ? testVolunteers.Contains(id) : volunteers.Contains(id);

        // Server: a player was HIT (Milsim rule) -> down in place, bleeding out.
        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            if (casualties.ContainsKey(victim)) return;
            int team = 0; Vector3 pos = Vector3.zero;
            if (TryGetParticipant(victim, out var p)) { team = p.team; pos = p.position; }
            else
            {
                var mn = MatchNet.Instance;
                if (mn != null) team = mn.TeamOf(victim);
                var tr = FindPlayer(victim);
                if (tr != null) pos = tr.position;
            }
            if (team != 1 && team != 2) return;
            AddDowned(victim, team, pos, Now);
            // a medic who was reviving stops (they're out now)
            holding.Remove(victim);
            lockOn.Remove(victim);
        }

        void AddDowned(ulong id, int team, Vector3 pos, double now)
        {
            casualties[id] = new Casualty { id = id, team = team, state = CasualtyState.Downed, bleedAt = now + BLEED_SECONDS, pos = pos };
            MarkDirty();
        }

        void BleedOut(Casualty c, bool gaveUp)
        {
            c.reviver = NONE; c.progress = 0f;
            int t = c.team;
            if (teamScore[t] > 0)
            {
                teamScore[t]--;
                c.state = CasualtyState.WalkingBack;   // Milsim walk-back to the team zone (Health)
                if (teamScore[t] == 0) Announce($"{TeamName(t)} is out of tickets", t);
                else if (teamScore[t] == 5) Announce($"{TeamName(t)}: 5 tickets left", t);
                NotifyScore();
            }
            else
            {
                c.state = CasualtyState.Eliminated;    // no ticket to respawn with
                Announce($"{NameOf(c.id)} is out · no {TeamName(t)} tickets left", t);
            }
            if (gaveUp) Debug.Log($"[Airsoft] Medic: {NameOf(c.id)} gave up");
            MarkDirty();
        }

        void NotifyScore()
        {
            MarkDirty();
            if (testParticipants == null) MatchNet.Instance?.NotifyObjectiveScore();
        }

        bool CanRevive(Participant medic, Casualty c) =>
            c.state == CasualtyState.Downed && c.team == medic.team && c.id != medic.id
            && Near(medic.position, c.pos, REVIVE_RADIUS, 2f);

        // ───────────────────────── bots ─────────────────────────
        // An up bot medic heads to the nearest downed teammate; everyone else fights / roams as usual.
        protected override bool BotGoal(ulong botId, out Vector3 pos, out BotTask task)
        {
            pos = default; task = BotTask.Revive;
            if (!medics.Contains(botId) || casualties.ContainsKey(botId)) return false;
            var mn = MatchNet.Instance;
            int team = mn != null ? mn.TeamOf(botId) : 0;
            Vector3 from = TryGetParticipant(botId, out var mp) ? mp.position : Vector3.zero;
            Casualty best = null; float bd = float.MaxValue;
            foreach (var c in casualties.Values)
            {
                if (c.team != team || c.state != CasualtyState.Downed || c.id == botId) continue;
                float d = (c.pos - from).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            if (best == null) return false;
            pos = best.pos;
            return true;
        }

        // Server: bot medics "hold E" automatically when a downed teammate is within revive range.
        void UpdateBotMedicHolding()
        {
            foreach (var m in medics)
            {
                if (!MatchNet.IsBotId(m)) continue;
                if (!TryGetParticipant(m, out var mp) || !mp.active) { holding.Remove(m); lockOn.Remove(m); continue; }
                bool any = false;
                foreach (var c in casualties.Values) if (CanRevive(mp, c)) { any = true; break; }
                if (any) holding.Add(m); else { holding.Remove(m); lockOn.Remove(m); }
            }
        }

        static Health ReviveHealth(ulong id)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return null;
            if (MatchNet.IsBotId(id))
            {
                var no = MatchNet.Instance != null ? MatchNet.Instance.BotObject(id) : null;
                return no != null ? no.GetComponent<Health>() : null;
            }
            return nm.ConnectedClients.TryGetValue(id, out var cl) && cl.PlayerObject != null
                ? cl.PlayerObject.GetComponent<Health>() : null;
        }

        void TickRevives(float dt)
        {
            var progressed = new HashSet<ulong>();
            var done = new List<(Casualty c, ulong medic)>();
            foreach (var m in holding)
            {
                if (!medics.Contains(m) || !TryGetParticipant(m, out var mp) || !mp.active) { lockOn.Remove(m); continue; }
                Casualty target = null;
                if (lockOn.TryGetValue(m, out ulong tid) && casualties.TryGetValue(tid, out var locked)
                    && CanRevive(mp, locked) && (locked.reviver == m || locked.reviver == NONE) && !progressed.Contains(tid))
                    target = locked;
                if (target == null)
                {
                    float best = float.MaxValue;
                    foreach (var c in casualties.Values)
                    {
                        if (!CanRevive(mp, c) || progressed.Contains(c.id) || (c.reviver != NONE && c.reviver != m)) continue;
                        float d = (c.pos - mp.position).sqrMagnitude;
                        if (d < best) { best = d; target = c; }
                    }
                }
                if (target == null) { lockOn.Remove(m); continue; }
                if (target.reviver != m) { target.reviver = m; target.progress = 0f; }
                lockOn[m] = target.id;
                target.progress += dt / REVIVE_SECONDS;
                progressed.Add(target.id);
                MarkDirty();
                if (target.progress >= 1f) done.Add((target, m));
            }
            foreach (var c in casualties.Values)
                if (!progressed.Contains(c.id) && (c.reviver != NONE || c.progress > 0f)) { c.reviver = NONE; c.progress = 0f; MarkDirty(); }
            foreach (var (c, m) in done) Revive(c, m);
        }

        void Revive(Casualty c, ulong medic)
        {
            casualties.Remove(c.id);
            lockOn.Remove(medic);
            AddPlayerPoints(medic, 1);
            Announce($"{NameOf(medic)} revived {NameOf(c.id)}", c.team);
            if (testParticipants != null) testRevived?.Invoke(c.id);
            else ReviveHealth(c.id)?.ReviveFromHit(REVIVE_PROTECTION);   // player or bot
            MarkDirty();
        }

        // A team with no tickets whose players are all out (down or out for good; nobody up or
        // walking back to a paid respawn) loses. Both at once: draw.
        void CheckElimination()
        {
            if (HasDeclared || !teamsReady) return;
            bool[] lost = new bool[3];
            for (int t = 1; t <= 2; t++)
            {
                if (teamScore[t] > 0) continue;
                int n = 0; bool anyIn = false;
                foreach (var p in participants)
                {
                    if (p.team != t) continue;
                    n++;
                    if (p.active || (casualties.TryGetValue(p.id, out var c) && c.state == CasualtyState.WalkingBack)) anyIn = true;
                }
                lost[t] = n > 0 && !anyIn;
            }
            if (lost[1] && lost[2]) DeclareWinner(0, true);
            else if (lost[1]) { Announce("RED has no one left", 1); DeclareWinner(2); }
            else if (lost[2]) { Announce("BLUE has no one left", 2); DeclareWinner(1); }
        }

        // ───────────────────────── sync ─────────────────────────
        protected override void WriteState(ref FastBufferWriter w)
        {
            w.WriteValueSafe(startTickets[1]);
            w.WriteValueSafe(startTickets[2]);
            w.WriteValueSafe((byte)Mathf.Min(medics.Count, 255));
            foreach (var id in medics) w.WriteValueSafe(id);
            w.WriteValueSafe((byte)Mathf.Min(casualties.Count, 255));
            foreach (var c in casualties.Values)
            {
                w.WriteValueSafe(c.id);
                w.WriteValueSafe((byte)c.team);
                w.WriteValueSafe((byte)c.state);
                w.WriteValueSafe(c.bleedAt);
                w.WriteValueSafe(c.pos);
                w.WriteValueSafe(c.reviver);
                w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Clamp01(c.progress) * 255f));
            }
        }

        protected override void ReadState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out startTickets[1]);
            r.ReadValueSafe(out startTickets[2]);
            r.ReadValueSafe(out byte nm);
            medics.Clear();
            for (int i = 0; i < nm; i++) { r.ReadValueSafe(out ulong id); medics.Add(id); }
            r.ReadValueSafe(out byte nc);
            casualties.Clear();
            for (int i = 0; i < nc; i++)
            {
                var c = new Casualty();
                r.ReadValueSafe(out c.id);
                r.ReadValueSafe(out byte team); c.team = team;
                r.ReadValueSafe(out byte st); c.state = (CasualtyState)st;
                r.ReadValueSafe(out c.bleedAt);
                r.ReadValueSafe(out c.pos);
                r.ReadValueSafe(out c.reviver);
                r.ReadValueSafe(out byte prog); c.progress = prog / 255f;
                casualties[c.id] = c;
            }
        }

        // ───────────────────────── local input (every peer) ─────────────────────────
        bool sentHold;
        float giveUpHeld;
        bool giveUpSent;

        protected override void VisualUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient || !nm.IsListening) return;
            ulong me = nm.LocalClientId;
            var kb = Keyboard.current;
            bool input = kb != null && !HUDManager.InputBlocked;

            bool hold = input && kb.eKey.isPressed && medics.Contains(me) && !casualties.ContainsKey(me);
            if (hold != sentHold) { sentHold = hold; SendHold(nm, hold); }

            if (Downed(me))
            {
                if (input && kb.gKey.isPressed) giveUpHeld += Time.deltaTime; else giveUpHeld = 0f;
                if (giveUpHeld >= GIVE_UP_HOLD && !giveUpSent) { giveUpSent = true; SendGiveUp(nm); }
            }
            else { giveUpHeld = 0f; giveUpSent = false; }
        }

        public float GiveUpProgress => Mathf.Clamp01(giveUpHeld / GIVE_UP_HOLD);

        // ───────────────────────── messages (also the lobby volunteers) ─────────────────────────
        const string MSG_VOL = "med_vol";        // client -> server: volunteer as medic (byte)
        const string MSG_VOLS = "med_vols";      // server -> clients: the volunteers
        const string MSG_HOLD = "med_hold";      // client -> server: holding the revive key (byte)
        const string MSG_GIVEUP = "med_giveup";  // client -> server: give up while downed

        static readonly HashSet<ulong> volunteers = new HashSet<ulong>();
        static CustomMessagingManager medHandlersOn;
        static bool volDirty;
        static float volBroadcastAt, volResendAt;

        // The local player's wish to be a medic (lobby toggle; kept for the session).
        public static bool LocalVolunteer { get; private set; }
        public static bool IsVolunteer(ulong id) => volunteers.Contains(id);

        public static void SetLocalVolunteer(bool on)
        {
            LocalVolunteer = on;
            volResendAt = 0f;   // send right away (LobbySync)
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMedicStatics()
        {
            volunteers.Clear(); medHandlersOn = null; volDirty = false;
            volBroadcastAt = volResendAt = 0f; LocalVolunteer = false;
        }

        // Every frame from ObjectiveMode.Sync (lobby and match).
        public static void LobbySync(NetworkManager nm)
        {
            if (nm == null) return;
            if (!nm.IsListening || nm.ShutdownInProgress) { if (!nm.IsListening) { volunteers.Clear(); medHandlersOn = null; } return; }
            var cmm = nm.CustomMessagingManager;
            if (cmm != null && cmm != medHandlersOn)
            {
                cmm.RegisterNamedMessageHandler(MSG_VOL, OnRecvVolunteer);
                cmm.RegisterNamedMessageHandler(MSG_VOLS, OnRecvVolunteers);
                cmm.RegisterNamedMessageHandler(MSG_HOLD, OnRecvHold);
                cmm.RegisterNamedMessageHandler(MSG_GIVEUP, OnRecvGiveUp);
                medHandlersOn = cmm;
            }
            float t = Time.unscaledTime;
            if (nm.IsServer)
            {
                if (nm.IsClient) SetVolunteer(nm.LocalClientId, LocalVolunteer);   // the host's own choice
                if (volDirty || t >= volBroadcastAt)
                {
                    volunteers.RemoveWhere(id => !nm.ConnectedClients.ContainsKey(id));
                    volDirty = false;
                    volBroadcastAt = t + 2f;
                    using var w = new FastBufferWriter(64, Allocator.Temp, 1024);
                    w.WriteValueSafe((byte)Mathf.Min(volunteers.Count, 255));
                    foreach (var id in volunteers) w.WriteValueSafe(id);
                    cmm.SendNamedMessageToAll(MSG_VOLS, w, NetworkDelivery.Reliable);
                }
            }
            else if (nm.IsConnectedClient && t >= volResendAt && LocalVolunteer != volunteers.Contains(nm.LocalClientId))
            {
                volResendAt = t + 1f;
                using var w = new FastBufferWriter(8, Allocator.Temp);
                w.WriteValueSafe((byte)(LocalVolunteer ? 1 : 0));
                cmm.SendNamedMessage(MSG_VOL, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
            }
        }

        static void SetVolunteer(ulong id, bool on)
        {
            if (on ? volunteers.Add(id) : volunteers.Remove(id)) volDirty = true;
        }

        static void OnRecvVolunteer(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            r.ReadValueSafe(out byte v);
            SetVolunteer(sender, v != 0);
        }

        static void OnRecvVolunteers(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out byte n);
            volunteers.Clear();
            for (int i = 0; i < n; i++) { r.ReadValueSafe(out ulong id); volunteers.Add(id); }
        }

        static void OnRecvHold(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            r.ReadValueSafe(out byte v);
            ActiveMedic?.ServerSetHolding(sender, v != 0);
        }

        static void OnRecvGiveUp(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            ActiveMedic?.ServerGiveUp(sender);
        }

        void SendHold(NetworkManager nm, bool on)
        {
            if (nm.IsServer) { ServerSetHolding(nm.LocalClientId, on); return; }
            if (!nm.IsConnectedClient) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((byte)(on ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessage(MSG_HOLD, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        void SendGiveUp(NetworkManager nm)
        {
            if (nm.IsServer) { ServerGiveUp(nm.LocalClientId); return; }
            if (!nm.IsConnectedClient) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((byte)1);
            nm.CustomMessagingManager.SendNamedMessage(MSG_GIVEUP, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        // ───────────────────────── scoreboard / results text ─────────────────────────
        protected override string NameTag(ulong id)
        {
            string s = "";
            if (medics.Contains(id)) s += $"  <size=70%><color={Html(MedicRed)}>+MEDIC</color></size>";
            if (casualties.TryGetValue(id, out var c))
                s += c.state == CasualtyState.Downed ? $"  <size=70%><color={Html(Accent)}>DOWN</color></size>"
                   : c.state == CasualtyState.Eliminated ? $"  <size=70%><color={Html(Muted)}>OUT</color></size>" : "";
            return s;
        }

        protected override bool ScoreCaption(out string caption, out string value)
        {
            caption = "TICKETS";
            value = startTickets[1] == startTickets[2] ? "of " + startTickets[1] : $"{startTickets[1]}/{startTickets[2]}";
            return true;
        }

        protected override string ResultText(int winnerTeam, bool onTime, bool draw)
        {
            string score = $"{teamScore[1]} : {teamScore[2]} tickets";
            if (draw) return onTime ? score + " · time's up, tickets level" : "both teams ran out of tickets and players";
            if (onTime) return score + " · more tickets left when time ran out";
            return $"{score} · {TeamName(3 - winnerTeam)} ran out of tickets and players";
        }

        // ───────────────────────── HUD ─────────────────────────
        TextMeshProUGUI roleText;
        GameObject roleChip;
        GameObject downedRoot, elimRoot, vignette;
        TextMeshProUGUI downTimer, downMedic, downHint, elimText;
        Image downFill, giveUpFill;
        GameObject reviveRingRoot;
        Image reviveRing;
        TextMeshProUGUI reviveRingText, reviveRingCaption;
        GameObject promptRoot;
        TextMeshProUGUI promptText;
        GameObject promptKey;
        readonly List<ObjectiveHUD.Marker> downMarkers = new List<ObjectiveHUD.Marker>();

        static RectTransform CrossIcon(Transform parent, float size)
        {
            var bg = Box(parent, "MedicCross", MedicWhite);
            bg.LE(size, size);
            Box(bg.transform, "V", MedicRed).rectTransform.At(0.5f, 0.5f, 0, 0, size * 0.26f, size * 0.72f);
            Box(bg.transform, "H", MedicRed).rectTransform.At(0.5f, 0.5f, 0, 0, size * 0.72f, size * 0.26f);
            return bg.rectTransform;
        }

        static Image RingImage(Transform parent, string name, float size, Color c, bool filled)
        {
            var img = Box(parent, name, c);
            img.sprite = RingSprite;
            img.rectTransform.At(0.5f, 0.5f, 0, 0, size, size);
            if (filled)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Radial360;
                img.fillOrigin = (int)Image.Origin360.Top;
                img.fillClockwise = true;
                img.fillAmount = 0f;
            }
            return img;
        }

        public override void BuildHud(RectTransform widget, RectTransform markers)
        {
            // role chip under the ticket bar
            var chip = HRow(widget, "MedicRole", 8, TextAnchor.MiddleCenter);
            chip.padding = new RectOffset(8, 12, 4, 4);
            chip.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            CrossIcon(chip.transform, 16);
            roleText = Label(chip.transform, "", F.BodySemi, 12, Fg, TextAlignmentOptions.MidlineLeft, 0.06f);
            roleChip = chip.gameObject;

            // full-screen layer: vignette + downed / out overlays + revive ring + prompt
            var vig = Box(markers, "DownVignette", A(Hex("#C8281E"), 0.4f));
            vig.sprite = VignetteSprite;
            vig.rectTransform.Fill();
            vignette = vig.gameObject;
            vignette.SetActive(false);

            var col = VCol(markers, "Downed", 8, TextAnchor.UpperCenter, false);
            col.GetComponent<RectTransform>().At(0.5f, 1f, 0, -170, 760, 320);
            Label(col.transform, "YOU'RE HIT · STAY DOWN", F.BodyBold, 12, Hex("#FFB3AA"), TextAlignmentOptions.Center, 0.24f);
            Label(col.transform, "DOWN", F.CondBlack, 96, Fg, TextAlignmentOptions.Center, 0.04f).LE(-1, 88);
            var card = VCol(col.transform, "Card", 8, TextAnchor.MiddleCenter, false);
            card.padding = new RectOffset(22, 22, 12, 12);
            card.gameObject.AddComponent<Image>().color = Scrim(0.82f);
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bleed = VCol(card.transform, "Bleed", 6, TextAnchor.MiddleCenter, true);
            bleed.LE(340, -1);
            downTimer = Label(bleed.transform, "", F.CondBold, 22, Accent, TextAlignmentOptions.Center);
            downFill = Bar(bleed.transform, "Bar", Line, Accent);
            downFill.transform.parent.GetComponent<Image>().LE(-1, 6);
            downMedic = Label(card.transform, "", F.Body, 15, Fg, TextAlignmentOptions.Center);
            var give = HRow(card.transform, "GiveUp", 8, TextAnchor.MiddleCenter);
            KeyCap(give.transform, "G", 11);
            downHint = Label(give.transform, "", F.Body, 13, Muted);
            giveUpFill = Bar(card.transform, "GiveUpBar", Color.clear, Muted);
            giveUpFill.transform.parent.GetComponent<Image>().LE(340, 3);
            downedRoot = col.gameObject;
            downedRoot.SetActive(false);

            var el = VCol(markers, "Out", 8, TextAnchor.UpperCenter, false);
            el.GetComponent<RectTransform>().At(0.5f, 1f, 0, -170, 760, 240);
            Label(el.transform, "NO TICKETS LEFT", F.BodyBold, 12, Hex("#FFB3AA"), TextAlignmentOptions.Center, 0.24f);
            Label(el.transform, "OUT", F.CondBlack, 96, Fg, TextAlignmentOptions.Center, 0.04f).LE(-1, 88);
            elimText = Label(el.transform, "", F.BodySemi, 16, TextSoft, TextAlignmentOptions.Center);
            elimRoot = el.gameObject;
            elimRoot.SetActive(false);

            // revive ring (medic and patient)
            var ring = Rect(markers, "ReviveRing").At(0.5f, 0.5f, 0, 0, 220, 150);
            RingImage(ring, "Back", 84, A(Fg, 0.18f), false).rectTransform.anchoredPosition = new Vector2(0, 30);
            reviveRing = RingImage(ring, "Fill", 84, MedicWhite, true);
            reviveRing.rectTransform.anchoredPosition = new Vector2(0, 30);
            var mid = CrossIcon(ring, 26);
            mid.At(0.5f, 0.5f, 0, 30, 26, 26);
            reviveRingText = Label(ring, "", F.CondBold, 18, Fg, TextAlignmentOptions.Center);
            reviveRingText.rectTransform.At(0.5f, 0.5f, 0, -28, 220, 22);
            reviveRingCaption = Label(ring, "", F.BodyBold, 11, Muted, TextAlignmentOptions.Center, 0.14f);
            reviveRingCaption.rectTransform.At(0.5f, 0.5f, 0, -50, 260, 16);
            reviveRingRoot = ring.gameObject;
            reviveRingRoot.SetActive(false);

            // revive prompt under the crosshair
            var pr = HRow(markers, "RevivePrompt", 8, TextAnchor.MiddleCenter);
            pr.padding = new RectOffset(10, 12, 5, 5);
            pr.gameObject.AddComponent<Image>().color = Scrim(0.82f);
            var prFit = pr.gameObject.AddComponent<ContentSizeFitter>();
            prFit.horizontalFit = prFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var prt = pr.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0, -90);
            promptKey = KeyCap(pr.transform, "E", 12, Ground, Ground).gameObject;
            promptText = Label(pr.transform, "", F.BodyBold, 13, Fg, TextAlignmentOptions.MidlineLeft, 0.08f);
            promptRoot = pr.gameObject;
            promptRoot.SetActive(false);
            // key cap on the light prompt: white text on the dark scrim reads better
            foreach (var t in promptKey.GetComponentsInChildren<TextMeshProUGUI>()) t.color = Fg;
            foreach (var img in promptKey.GetComponentsInChildren<Image>()) img.color = Fg;

            for (int i = 0; i < 8; i++) downMarkers.Add(ObjectiveHUD.Marker.Create(markers, "+", MedicRed));
        }

        public override void UpdateHud(Camera cam, RectTransform markers)
        {
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            ulong me = nm != null ? nm.LocalClientId : 0;
            int myTeam = mn != null ? mn.TeamOf(me) : 0;
            Transform myTr = FindPlayer(me);
            Vector3 myPos = myTr != null ? myTr.position : Vector3.zero;
            double now = Now;
            bool iAmMedic = medics.Contains(me);
            casualties.TryGetValue(me, out var mine);

            // role chip
            if (iAmMedic) roleText.text = mine == null ? "YOU ARE A MEDIC · hold E on downed teammates" : "YOU ARE A MEDIC";
            else
            {
                var names = new List<string>();
                foreach (var id in medics) if (mn != null && mn.TeamOf(id) == myTeam) names.Add(NameOf(id));
                roleText.text = names.Count == 0 ? "NO MEDIC ON YOUR TEAM" : (names.Count == 1 ? "MEDIC: " : "MEDICS: ") + string.Join(", ", names);
            }
            roleChip.SetActive(myTeam != 0);

            // downed / out overlays
            bool downed = mine != null && mine.state == CasualtyState.Downed;
            bool elim = mine != null && mine.state == CasualtyState.Eliminated;
            downedRoot.SetActive(downed);
            elimRoot.SetActive(elim);
            vignette.SetActive(downed || elim);
            if (downed)
            {
                float left = Mathf.Max(0f, (float)(mine.bleedAt - now));
                downTimer.text = "BLEEDING OUT  " + MatchNet.FormatClock(left);
                downTimer.color = left <= 10f ? Danger : Accent;
                SetBar(downFill, left / BLEED_SECONDS);
                if (mine.reviver != NONE) downMedic.text = $"<color={Html(Ready)}>{NameOf(mine.reviver)}</color> is reviving you";
                else
                {
                    float best = float.MaxValue; ulong who = NONE;
                    foreach (var id in medics)
                    {
                        if (id == me || (mn != null && mn.TeamOf(id) != myTeam) || casualties.ContainsKey(id)) continue;
                        var tr = FindPlayer(id);
                        if (tr == null) continue;
                        float d = Vector3.Distance(tr.position, myPos);
                        if (d < best) { best = d; who = id; }
                    }
                    downMedic.text = who == NONE ? $"<color={Html(DangerSoft)}>No medic up on your team</color>"
                        : $"Nearest medic: <color={Html(Fg)}>{NameOf(who)}</color>  <color={Html(Accent)}>{Mathf.RoundToInt(best)} m</color>";
                }
                bool noTickets = TeamScore(myTeam) <= 0;
                downHint.text = noTickets ? "hold to give up · no tickets left: you'd be out"
                                          : "hold to give up · walk back to base (costs 1 ticket)";
                SetBar(giveUpFill, GiveUpProgress);
            }
            if (elim)
                elimText.text = $"No {TeamName(myTeam)} tickets left. A medic can't help now: wait for the round to end.";

            // revive ring: I'm reviving someone, or someone is reviving me
            Casualty ringFor = null; bool asMedic = false;
            if (downed && mine.reviver != NONE) ringFor = mine;
            else foreach (var c in casualties.Values) if (c.reviver == me) { ringFor = c; asMedic = true; break; }
            reviveRingRoot.SetActive(ringFor != null);
            if (ringFor != null)
            {
                reviveRing.fillAmount = ringFor.progress;
                float secs = (1f - ringFor.progress) * REVIVE_SECONDS;
                reviveRingText.text = (asMedic ? "REVIVING " + NameOf(ringFor.id) : NameOf(ringFor.reviver) + " IS REVIVING YOU").ToUpperInvariant();
                reviveRingCaption.text = asMedic ? $"KEEP HOLDING E · {secs:0.0} s" : $"BACK IN {secs:0.0} s";
            }

            // prompt: a downed teammate in reach
            string prompt = null; bool canAct = false;
            if (mine == null && myTr != null && ringFor == null)
            {
                float best = float.MaxValue; Casualty near = null;
                foreach (var c in casualties.Values)
                {
                    if (c.team != myTeam || c.state != CasualtyState.Downed || c.id == me) continue;
                    var tr = FindPlayer(c.id);
                    Vector3 at = tr != null ? tr.position : c.pos;
                    if (!Near(myPos, at, REVIVE_RADIUS, 2f)) continue;
                    float d = (at - myPos).sqrMagnitude;
                    if (d < best) { best = d; near = c; }
                }
                if (near != null)
                {
                    canAct = iAmMedic && near.reviver == NONE;
                    prompt = iAmMedic
                        ? (near.reviver == NONE ? "HOLD TO REVIVE " + NameOf(near.id).ToUpperInvariant() : NameOf(near.reviver).ToUpperInvariant() + " IS ON IT")
                        : "ONLY A MEDIC CAN REVIVE " + NameOf(near.id).ToUpperInvariant();
                }
            }
            promptRoot.SetActive(prompt != null);
            if (prompt != null) { promptText.text = prompt; promptKey.SetActive(canAct); }

            // markers: downed teammates, for medics
            int mi = 0;
            if (iAmMedic && mine == null)
                foreach (var c in casualties.Values)
                {
                    if (mi >= downMarkers.Count) break;
                    if (c.team != myTeam || c.state != CasualtyState.Downed || c.id == me) continue;
                    var tr = FindPlayer(c.id);
                    Vector3 at = tr != null ? tr.position : c.pos;
                    float left = Mathf.Max(0f, (float)(c.bleedAt - now));
                    var m = downMarkers[mi++];
                    m.SetColor(MedicRed);
                    m.SetPulse(left <= 10f || c.reviver != NONE);
                    m.Show(cam, markers, at + Vector3.up * 1.7f, NameOf(c.id).ToUpperInvariant() + "  " + Mathf.CeilToInt(left) + "s", true);
                }
            for (; mi < downMarkers.Count; mi++) downMarkers[mi].Show(cam, markers, Vector3.zero, "", false);
        }
    }
}
