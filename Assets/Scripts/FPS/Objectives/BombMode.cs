using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Bomb (attack / defend), Red vs Blue, best of 7 (first to 4 rounds); the roles swap every round.
    // One random attacker carries the bomb; it drops where its carrier goes down and any attacker can pick
    // it up. Hold E for 4 s inside site A or B (MapLayout.bombSites) to plant it; it explodes 40 s later
    // unless a defender holds E next to it for 6 s. A round is won by the explosion, the defuse, taking out
    // the whole other team (attackers out before a plant = defenders win; a planted bomb still has to be
    // defused), or by the defenders when the 2:30 round clock runs out. No respawns during a round.
    public class BombMode : RoundMode
    {
        public const int ROUNDS_TO_WIN = 4;
        public const float ROUND_SECONDS = 150f;
        public const float PLANT_SECONDS = 4f;
        public const float DEFUSE_SECONDS = 6f;
        public const float FUSE_SECONDS = 40f;
        public const float SITE_RADIUS = 4.5f;
        public const float DEFUSE_RADIUS = 2.2f;
        public const float PICKUP_RADIUS = 1.6f;
        public const float BLAST_RADIUS = 10f;

        public static string MissingOnMap(MapCaps c) => c.bombSites >= 2 ? null : "Not on this map: no bomb sites";

        public enum BombState : byte { None = 0, Carried = 1, Dropped = 2, Planted = 3, Defused = 4, Exploded = 5 }

        public BombState State { get; private set; }
        public ulong Carrier { get; private set; } = ulong.MaxValue;
        public Vector3 BombPos { get; private set; }
        public int PlantedSite { get; private set; } = -1;
        public double ExplodeAt { get; private set; }
        public ulong Actor { get; private set; } = ulong.MaxValue;   // planting / defusing player
        public float ActionProgress { get; private set; }            // 0..1

        public readonly List<Vector3> sites = new List<Vector3>();
        public static string SiteName(int i) => ((char)('A' + Mathf.Clamp(i, 0, 25))).ToString();

        protected override float RoundSeconds => ROUND_SECONDS;
        protected override string AttackRole => "ATTACK";
        protected override string DefendRole => "DEFEND";
        protected override bool ClockPaused => State == BombState.Planted;
        protected override string RoundIntro() => "attacks";

        public float FuseLeft => State == BombState.Planted ? Mathf.Max(0f, (float)(ExplodeAt - Now)) : 0f;

        // ───────────────────────── setup ─────────────────────────
        protected override void OnBegin()
        {
            var map = MapLayout.Current;
            var list = new List<Vector3>();
            if (map != null && map.bombSites != null)
                foreach (var t in map.bombSites) if (t != null) list.Add(t.position);
            SetSites(list);
            BuildVisuals();
        }

        public void SetSites(IList<Vector3> positions)
        {
            sites.Clear();
            for (int i = 0; i < positions.Count && i < 4; i++) sites.Add(positions[i]);
        }

        public int SiteAt(Vector3 p)
        {
            for (int i = 0; i < sites.Count; i++) if (Near(p, sites[i], SITE_RADIUS, 3f)) return i;
            return -1;
        }

        // ───────────────────────── bots ─────────────────────────
        protected override bool BotGoal(ulong botId, out Vector3 pos, out BotTask task)
        {
            pos = default; task = BotTask.GoTo;
            var mn = MatchNet.Instance;
            int team = mn != null ? mn.TeamOf(botId) : 0;
            if ((team != 1 && team != 2) || sites.Count == 0) return false;
            if (RoundStage != Stage.Live && RoundStage != Stage.Prep) return false;
            bool attacker = team == AttackTeam;
            Vector3 myPos = TryGetParticipant(botId, out var me) ? me.position : BombPos;

            if (attacker)
            {
                if (State == BombState.Planted) { pos = BombPos; return true; }                       // guard the planted bomb
                if (State == BombState.Dropped) { pos = BombPos; return true; }                        // pick it up
                if (State == BombState.Carried && Carrier == botId)                                    // I carry it: plant at the nearest site
                { pos = NearestSite(myPos); task = BotTask.Plant; return true; }
                pos = sites[(int)(botId % (ulong)sites.Count)]; return true;                           // push a site
            }
            // defender
            if (State == BombState.Planted) { pos = BombPos; task = BotTask.Defuse; return true; }     // defuse
            pos = sites[(int)(botId % (ulong)sites.Count)]; return true;                               // spread across sites to guard
        }

        protected override bool BotWantsUse(ulong id)
        {
            if (!TryGetParticipant(id, out var p) || !p.active) return false;
            if (State == BombState.Carried && id == Carrier && p.team == AttackTeam && SiteAt(p.position) >= 0) return true;
            if (State == BombState.Planted && p.team == DefendTeam && Near(p.position, BombPos, DEFUSE_RADIUS)) return true;
            return false;
        }

        Vector3 NearestSite(Vector3 from)
        {
            Vector3 best = sites[0]; float bd = float.MaxValue;
            foreach (var s in sites) { float d = (s - from).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
            return best;
        }

        // ───────────────────────── server rules ─────────────────────────
        protected override void OnRoundStart(double now)
        {
            State = BombState.None;
            Carrier = Actor = ulong.MaxValue;
            ActionProgress = 0f;
            PlantedSite = -1;
            ExplodeAt = 0;
            var attackers = new List<ulong>();
            foreach (var p in participants) if (p.team == AttackTeam) attackers.Add(p.id);
            if (attackers.Count > 0)
            {
                Carrier = attackers[Random.Range(0, attackers.Count)];
                State = BombState.Carried;
                BombPos = TryGetParticipant(Carrier, out var cp) ? cp.position : PlayerSpawner.GetTeamZone(AttackTeam).center;
            }
            else
            {
                State = BombState.Dropped;
                BombPos = PlayerSpawner.GetTeamZone(AttackTeam).center;
            }
        }

        protected override void RoundTick(float dt, double now)
        {
            // carrier: the bomb follows them, drops when they're down or gone
            if (State == BombState.Carried)
            {
                if (TryGetParticipant(Carrier, out var c) && c.active && c.team == AttackTeam)
                {
                    if ((BombPos - c.position).sqrMagnitude > 0.04f) MarkDirty();
                    BombPos = c.position;
                }
                else DropBomb(TryGetParticipant(Carrier, out c) ? c.position : BombPos);
            }
            if (!Live) return;

            // pick up a dropped bomb (attackers only)
            if (State == BombState.Dropped)
                foreach (var p in participants)
                    if (p.active && p.team == AttackTeam && Near(p.position, BombPos, PICKUP_RADIUS))
                    {
                        State = BombState.Carried;
                        Carrier = p.id;
                        BombPos = p.position;
                        MarkDirty();
                        Announce($"{NameOf(p.id)} picked up the bomb", AttackTeam);
                        break;
                    }

            // plant: the carrier holds E inside a site
            if (State == BombState.Carried && TryGetParticipant(Carrier, out var pl))
            {
                int site = SiteAt(pl.position);
                if (site >= 0 && pl.active && UseHeld(Carrier))
                {
                    if (Actor != Carrier) { Actor = Carrier; ActionProgress = 0f; }
                    ActionProgress += dt / PLANT_SECONDS;
                    MarkDirty();
                    if (ActionProgress >= 1f) Plant(site, pl, now);
                }
                else if (Actor != ulong.MaxValue) ResetAction();
            }
            else if (State == BombState.Carried && Actor != ulong.MaxValue) ResetAction();

            if (State == BombState.Planted)
            {
                // defuse: one defender at a time holds E next to the bomb
                bool going = Actor != ulong.MaxValue && TryGetParticipant(Actor, out var d) && d.active && d.team == DefendTeam
                             && Near(d.position, BombPos, DEFUSE_RADIUS) && UseHeld(Actor);
                if (!going)
                {
                    if (Actor != ulong.MaxValue) ResetAction();
                    foreach (var p in participants)
                        if (p.active && p.team == DefendTeam && Near(p.position, BombPos, DEFUSE_RADIUS) && UseHeld(p.id))
                        { Actor = p.id; ActionProgress = 0f; going = true; break; }
                }
                if (going)
                {
                    ActionProgress += dt / DEFUSE_SECONDS;
                    MarkDirty();
                    if (ActionProgress >= 1f)
                    {
                        ulong who = Actor;
                        State = BombState.Defused;
                        ResetAction();
                        AddPlayerPoints(who, 1);
                        EndRound(DefendTeam, $"{NameOf(who)} defused the bomb");
                        return;
                    }
                }
                if (now >= ExplodeAt)
                {
                    State = BombState.Exploded;
                    ResetAction();
                    EndRound(AttackTeam, "the bomb exploded");
                    Blast();
                    return;
                }
            }

            // eliminations (a team nobody plays on can't be eliminated)
            CountTeam(AttackTeam, out int am, out int aAlive);
            CountTeam(DefendTeam, out int dm, out int dAlive);
            if (am > 0 && aAlive == 0 && State != BombState.Planted) EndRound(DefendTeam, "attackers eliminated");
            else if (dm > 0 && dAlive == 0) EndRound(AttackTeam, "defenders eliminated");
        }

        void Plant(int site, Participant p, double now)
        {
            State = BombState.Planted;
            PlantedSite = site;
            BombPos = GroundAt(p.position);
            ExplodeAt = now + FUSE_SECONDS;
            Carrier = ulong.MaxValue;
            ResetAction();
            AddPlayerPoints(p.id, 1);
            Announce($"{NameOf(p.id)} planted the bomb at {SiteName(site)}", AttackTeam);
        }

        void ResetAction()
        {
            Actor = ulong.MaxValue;
            ActionProgress = 0f;
            MarkDirty();
        }

        void DropBomb(Vector3 at)
        {
            State = BombState.Dropped;
            Carrier = ulong.MaxValue;
            BombPos = GroundAt(at);
            ResetAction();
            Announce("The bomb was dropped", AttackTeam);
        }

        protected override void OnRoundTimeout() => EndRound(DefendTeam, "time ran out");

        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            if (RoundStage != Stage.Live && RoundStage != Stage.Prep) return;
            if (State == BombState.Carried && Carrier == victim)
            {
                var tr = FindPlayer(victim);
                DropBomb(tr != null ? tr.position : BombPos);
            }
            else if (Actor == victim) ResetAction();
        }

        // Server: the explosion takes out everyone close to it.
        void Blast()
        {
            if (testParticipants != null || !Application.isPlaying) return;
            foreach (var h in FindObjectsByType<Health>())
            {
                if (!h.IsSpawned || !h.IsCombatant || h.isDead.Value) continue;
                if ((h.transform.position - BombPos).sqrMagnitude > BLAST_RADIUS * BLAST_RADIUS) continue;
                h.TakeDamage(1000, h.MatchId, "Bomb");
            }
        }

        // Scoreboard: the bomb carrier, shown to their own team only.
        protected override string NameTag(ulong clientId) =>
            State == BombState.Carried && clientId == Carrier && LocalTeam == AttackTeam
                ? $"  <size=70%><color={Html(Danger)}>BOMB</color></size>" : "";

        // Planting / defusing: you stand still.
        protected override float MoveFactor(ulong clientId) =>
            base.MoveFactor(clientId) * (Actor == clientId && ActionProgress > 0f ? 0f : 1f);

        static Vector3 GroundAt(Vector3 p)
        {
            if (!Application.isPlaying) return p;
            var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity; Vector3 g = p;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Health>() != null) continue;
                if (h.distance < best) { best = h.distance; g = h.point; }
            }
            return g;
        }

        // ───────────────────────── sync ─────────────────────────
        protected override void WriteRoundState(ref FastBufferWriter w)
        {
            w.WriteValueSafe((byte)State);
            w.WriteValueSafe(Carrier);
            w.WriteValueSafe(BombPos);
            w.WriteValueSafe((sbyte)PlantedSite);
            w.WriteValueSafe(ExplodeAt);
            w.WriteValueSafe(Actor);
            w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Clamp01(ActionProgress) * 255f));
        }

        protected override void ReadRoundState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte st);
            r.ReadValueSafe(out ulong carrier);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out sbyte site);
            r.ReadValueSafe(out double explodeAt);
            r.ReadValueSafe(out ulong actor);
            r.ReadValueSafe(out byte prog);
            State = (BombState)st; Carrier = carrier; BombPos = pos; PlantedSite = site;
            ExplodeAt = explodeAt; Actor = actor; ActionProgress = prog / 255f;
        }

        // ───────────────────────── visuals ─────────────────────────
        static readonly Color SiteColor = Hex("#FFD23F");
        Transform bomb;
        Renderer bombLight;
        Transform blast;
        Light blastLight;
        float blastStart = -1f;
        BombState shownState;
        int shownRound;

        void BuildVisuals()
        {
            for (int i = 0; i < sites.Count; i++)
            {
                var z = BuildZone("BombSite_" + SiteName(i), sites[i], SITE_RADIUS, SiteColor, 24);
                var sign = new GameObject("Sign").transform;
                sign.SetParent(z, false);
                sign.localPosition = new Vector3(0, 0, 0);
                Letter(sign, SiteName(i), SiteColor, 2.2f);
            }

            bomb = new GameObject("Bomb").transform;
            bomb.SetParent(transform, false);
            Prim(PrimitiveType.Cube, bomb, new Vector3(0, 0.08f, 0), new Vector3(0.36f, 0.16f, 0.24f), Mat(Hex("#4B5232")), "Case");
            Prim(PrimitiveType.Cube, bomb, new Vector3(-0.06f, 0.17f, 0), new Vector3(0.16f, 0.03f, 0.12f), Mat(Hex("#1C1E19")), "Panel");
            bombLight = Prim(PrimitiveType.Cube, bomb, new Vector3(0.1f, 0.18f, 0.05f), new Vector3(0.04f, 0.03f, 0.04f), Mat(Hex("#FF3B30")), "Led").GetComponent<Renderer>();
            for (int i = -1; i <= 1; i += 2)
                Prim(PrimitiveType.Cylinder, bomb, new Vector3(0, 0.08f, i * 0.06f), new Vector3(0.05f, 0.19f, 0.05f), Mat(Hex("#C7552B")), "Stick").localRotation = Quaternion.Euler(0, 0, 90);

            blast = Prim(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one, Mat(A(Hex("#FF9A4D"), 0.6f), true), "Blast");
            var lgo = new GameObject("BlastLight");
            lgo.transform.SetParent(blast, false);
            blastLight = lgo.AddComponent<Light>();
            blastLight.type = LightType.Point;
            blastLight.color = Hex("#FFB066");
            blastLight.range = BLAST_RADIUS * 3f;
            blast.gameObject.SetActive(false);
        }

        protected override void RoundVisualUpdate()
        {
            if (bomb == null) return;
            if (Round != shownRound) { shownRound = Round; shownState = State; }
            if (State == BombState.Exploded && shownState != BombState.Exploded) blastStart = Time.time;
            shownState = State;

            bool visible = State != BombState.None && State != BombState.Exploded && RoundStage != Stage.Waiting;
            bomb.gameObject.SetActive(visible);
            if (visible)
            {
                if (State == BombState.Carried)
                {
                    var tr = FindPlayer(Carrier);
                    if (tr != null)
                        bomb.SetPositionAndRotation(tr.position + tr.rotation * new Vector3(0f, 1.05f, -0.3f), tr.rotation * Quaternion.Euler(80f, 0, 0));
                    else bomb.SetPositionAndRotation(BombPos + Vector3.up * 1f, Quaternion.identity);
                }
                else bomb.SetPositionAndRotation(BombPos + Vector3.up * 0.02f, Quaternion.Euler(0, (BombPos.x * 37f) % 360f, 0));

                // LED: slow blink when idle, faster as the fuse runs down, solid green once defused
                float rate = State == BombState.Planted ? Mathf.Lerp(8f, 1.2f, FuseLeft / FUSE_SECONDS) : 0.8f;
                bool on = State == BombState.Defused || Mathf.Repeat(Time.time * rate, 1f) < 0.5f;
                bombLight.sharedMaterial = State == BombState.Defused ? Mat(Ready) : on ? Mat(Hex("#FF3B30")) : Mat(Hex("#3A1210"));
            }

            // explosion: a fireball that grows and fades
            if (blastStart >= 0f)
            {
                float t = (Time.time - blastStart) / 1.4f;
                if (t >= 1f) { blast.gameObject.SetActive(false); blastStart = -1f; }
                else
                {
                    blast.gameObject.SetActive(true);
                    blast.position = BombPos + Vector3.up * 1f;
                    blast.localScale = Vector3.one * Mathf.Lerp(1f, BLAST_RADIUS * 1.6f, Mathf.Sqrt(t));
                    blast.GetComponent<Renderer>().sharedMaterial = Mat(A(Hex("#FF9A4D"), Mathf.Round((1f - t) * 10f) / 16f), true);
                    blastLight.intensity = Mathf.Lerp(12f, 0f, t);
                }
            }
        }

        // ───────────────────────── HUD ─────────────────────────
        readonly List<ObjectiveHUD.Marker> siteMarkers = new List<ObjectiveHUD.Marker>();
        ObjectiveHUD.Marker bombMarker;

        protected override void BuildModeHud(RectTransform widget, RectTransform markers)
        {
            for (int i = 0; i < sites.Count; i++) siteMarkers.Add(ObjectiveHUD.Marker.Create(markers, SiteName(i), SiteColor));
            bombMarker = ObjectiveHUD.Marker.Create(markers, "!", Danger);
        }

        protected override string RoleBriefing(bool attack) => attack
            ? "Plant the bomb at " + SitesText() + " · no respawns"
            : "Defend " + SitesText() + " · defuse a planted bomb · no respawns";

        string SitesText()
        {
            if (sites.Count <= 1) return "the site";
            var s = "";
            for (int i = 0; i < sites.Count; i++) s += (i == 0 ? "" : i == sites.Count - 1 ? " or " : ", ") + SiteName(i);
            return s;
        }

        protected override string StatusText(bool attack)
        {
            ulong me = LocalId;
            switch (State)
            {
                case BombState.Carried:
                    if (attack && Carrier == me) return $"<color={Html(Accent)}>YOU HAVE THE BOMB</color> · plant it at {SitesText()}";
                    return attack ? $"{NameOf(Carrier).ToUpperInvariant()} HAS THE BOMB" : $"DEFEND {SitesText()}";
                case BombState.Dropped:
                    return attack ? $"<color={Html(DangerSoft)}>BOMB DROPPED</color> · pick it up" : $"DEFEND {SitesText()}";
                case BombState.Planted:
                    string at = SiteName(PlantedSite);
                    return attack ? $"BOMB PLANTED AT {at} · guard it"
                                  : $"<color={Html(Danger)}>BOMB PLANTED AT {at}</color> · defuse it";
            }
            return null;
        }

        protected override bool LocalAction(out string verb, out float progress, out bool doing)
        {
            verb = null; progress = 0f; doing = false;
            ulong me = LocalId;
            int team = LocalTeam;
            var h = LocalHealth();
            if (h == null || h.isDead.Value) return false;
            Vector3 pos = h.transform.position;
            if (State == BombState.Carried && Carrier == me && team == AttackTeam && SiteAt(pos) >= 0)
            {
                verb = Actor == me && ActionProgress > 0f ? "PLANTING" : "PLANT";
                doing = Actor == me && ActionProgress > 0f;
                progress = ActionProgress;
                return true;
            }
            if (State == BombState.Planted && team == DefendTeam && Near(pos, BombPos, DEFUSE_RADIUS))
            {
                doing = Actor == me && ActionProgress > 0f;
                verb = doing ? "DEFUSING" : "DEFUSE";
                progress = ActionProgress;
                return true;
            }
            return false;
        }

        protected override bool TimerOverride(out float seconds, out string caption)
        {
            seconds = FuseLeft; caption = "BOMB";
            return State == BombState.Planted;
        }

        protected override void UpdateModeHud(Camera cam, RectTransform markers, int myTeam, bool attack)
        {
            ulong me = LocalId;
            var h = LocalHealth();
            Vector3 myPos = h != null ? h.transform.position : Vector3.zero;
            bool live = RoundStage == Stage.Live || RoundStage == Stage.Prep;

            for (int i = 0; i < siteMarkers.Count; i++)
            {
                var m = siteMarkers[i];
                bool planted = State == BombState.Planted;
                bool show = live && (!planted || PlantedSite == i) && !(h != null && Near(myPos, sites[i], SITE_RADIUS, 3f));
                string label = planted ? (attack ? "GUARD" : "DEFUSE") : attack ? "PLANT" : "DEFEND";
                m.SetPulse(planted && PlantedSite == i);
                m.Show(cam, markers, sites[i] + Vector3.up * 3.6f, label, show);
            }

            // the bomb itself: attackers always know where it is; everyone sees it once planted
            bool bombShow = false; string bombLabel = ""; Vector3 at = BombPos;
            if (live)
            {
                switch (State)
                {
                    case BombState.Carried:
                        bombShow = attack && Carrier != me;
                        var tr = FindPlayer(Carrier);
                        if (tr != null) at = tr.position;
                        at += Vector3.up * 1.2f;
                        bombLabel = "BOMB · " + NameOf(Carrier).ToUpperInvariant();
                        break;
                    case BombState.Dropped:
                        bombShow = attack; bombLabel = "PICK UP THE BOMB"; break;
                    case BombState.Planted:
                        bombShow = true; bombLabel = "BOMB " + Mathf.CeilToInt(FuseLeft) + "s"; break;
                }
            }
            bombMarker.SetPulse(State == BombState.Planted || State == BombState.Dropped);
            bombMarker.Show(cam, markers, at + Vector3.up * 0.9f, bombLabel, bombShow);
        }
    }
}
