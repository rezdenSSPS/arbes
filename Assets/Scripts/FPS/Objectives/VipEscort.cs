using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // VIP escort, Red vs Blue, first to 3 rounds; the roles swap every round. One random player of the
    // escorting team is the VIP: marked for everyone, sidearms only, can't sprint and walks a little slower.
    // The escorts start around MapLayout.vipStart and must get the VIP to vipExtract (the VIP holds E there
    // for 3 s). The hunters win the round by taking the VIP down or when the 3:00 round clock runs out.
    // Everyone else respawns 6 s after going down (escorts on the start side, hunters on the extraction
    // side but not right on it); the VIP doesn't (VIP down = round lost).
    public class VipEscort : RoundMode
    {
        public const int ROUNDS_TO_WIN = 3;
        public const float ROUND_SECONDS = 180f;
        public const float EXTRACT_SECONDS = 3f;
        public const float EXTRACT_RADIUS = 4f;
        public const float RESPAWN_DELAY = 6f;          // seconds from going down (not the VIP)
        public const float VIP_SPEED = 0.85f;
        public const float HUNTER_SPAWN_CLEARANCE = 15f; // hunters never spawn this close to the extraction

        public static string MissingOnMap(MapCaps c) => c.vipStart && c.vipExtract ? null : "Not on this map: no VIP route";

        public static bool IsSidearm(WeaponData w) => w != null && (w.model == "Pistol" || w.model == "Revolver");

        public ulong Vip { get; private set; } = ulong.MaxValue;
        public Vector3 VipPos { get; private set; }
        public float ExtractProgress { get; private set; }
        public Vector3 StartPoint { get; private set; }
        public Vector3 ExtractPoint { get; private set; }
        public int EscortSide { get; private set; } = 2;   // spawn side (team points) nearest the VIP start

        readonly Dictionary<ulong, double> downAt = new Dictionary<ulong, double>();

        protected override float RoundSeconds => ROUND_SECONDS;
        protected override string AttackRole => "ESCORT";
        protected override string DefendRole => "HUNT";
        protected override string RoundIntro() => Vip != ulong.MaxValue ? "escorts " + NameOf(Vip) : "escorts the VIP";

        // ───────────────────────── setup ─────────────────────────
        protected override void OnBegin()
        {
            var map = MapLayout.Current;
            Vector3 s = map != null && map.vipStart != null ? map.vipStart.position : PlayerSpawner.GetTeamZone(2).center;
            Vector3 e = map != null && map.vipExtract != null ? map.vipExtract.position : PlayerSpawner.GetTeamZone(1).center;
            SetRoute(s, e);
            BuildVisuals();
        }

        public void SetRoute(Vector3 start, Vector3 extract)
        {
            StartPoint = start;
            ExtractPoint = extract;
            VipPos = start;
            // the escorts' side = the team spawn side closest to the start
            float d1 = SideDistance(1, start), d2 = SideDistance(2, start);
            EscortSide = d1 < d2 ? 1 : 2;
        }

        static float SideDistance(int team, Vector3 p)
        {
            float best = float.PositiveInfinity;
            foreach (var sp in SpawnPoint.All)
                if (sp != null && sp.team == team) best = Mathf.Min(best, (sp.transform.position - p).sqrMagnitude);
            if (float.IsPositiveInfinity(best)) best = (PlayerSpawner.GetTeamZone(team).center - p).sqrMagnitude;
            return best;
        }

        // ───────────────────────── server rules ─────────────────────────
        protected override void OnRoundStart(double now)
        {
            downAt.Clear();
            ExtractProgress = 0f;
            VipPos = StartPoint;
            var escorts = new List<ulong>();
            foreach (var p in participants) if (p.team == AttackTeam) escorts.Add(p.id);
            Vip = escorts.Count > 0 ? escorts[Random.Range(0, escorts.Count)] : ulong.MaxValue;
        }

        // ───────────────────────── bots ─────────────────────────
        protected override bool BotGoal(ulong botId, out Vector3 pos, out BotTask task)
        {
            pos = default; task = BotTask.GoTo;
            var mn = MatchNet.Instance;
            int team = mn != null ? mn.TeamOf(botId) : 0;
            if ((team != 1 && team != 2) || Vip == ulong.MaxValue) return false;
            if (RoundStage != Stage.Live && RoundStage != Stage.Prep) return false;

            if (botId == Vip) { pos = ExtractPoint; task = BotTask.Deliver; return true; }   // VIP beelines to the extraction
            pos = TryGetParticipant(Vip, out var v) ? v.position : VipPos;                    // escorts & hunters converge on the VIP
            return true;
        }

        // The VIP "holds E" to extract once it's standing on the extraction point.
        protected override bool BotWantsUse(ulong id) =>
            id == Vip && TryGetParticipant(id, out var p) && p.active && Near(p.position, ExtractPoint, EXTRACT_RADIUS, 3f);

        protected override void RoundTick(float dt, double now)
        {
            if (Vip == ulong.MaxValue)   // the escorting team has nobody: the hunters take the round
            {
                if (Live) EndRound(DefendTeam, "no VIP to escort");
                return;
            }
            if (!TryGetParticipant(Vip, out var v))
            {
                if (Live) EndRound(DefendTeam, "the VIP left");
                return;
            }
            if ((VipPos - v.position).sqrMagnitude > 0.04f) MarkDirty();
            VipPos = v.position;
            if (!Live) return;
            if (!v.active) { EndRound(DefendTeam, "VIP down"); return; }

            bool extracting = Near(v.position, ExtractPoint, EXTRACT_RADIUS, 3f) && UseHeld(Vip);
            if (extracting)
            {
                ExtractProgress += dt / EXTRACT_SECONDS;
                MarkDirty();
                if (ExtractProgress >= 1f)
                {
                    ExtractProgress = 1f;
                    AddPlayerPoints(Vip, 1);
                    EndRound(AttackTeam, $"{NameOf(Vip)} was extracted");
                }
            }
            else if (ExtractProgress > 0f) { ExtractProgress = 0f; MarkDirty(); }
        }

        protected override void OnRoundTimeout() => EndRound(DefendTeam, "time ran out");

        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            downAt[victim] = Now;
            if (!Live || victim != Vip) return;
            if (killer != victim && MatchNet.Instance != null && MatchNet.Instance.TeamOf(killer) == DefendTeam)
                AddPlayerPoints(killer, 1);
            EndRound(DefendTeam, killer != victim && killer != ulong.MaxValue ? $"{NameOf(killer)} took down the VIP" : "VIP down");
        }

        // The VIP stays down; everyone else is back RESPAWN_DELAY s after going down. Between rounds nobody
        // respawns by themselves (the next round brings everyone back).
        protected override bool HoldRespawn(ulong clientId)
        {
            if (RoundStage == Stage.Waiting) return false;
            if (RoundStage != Stage.Live || clientId == Vip) return true;
            return downAt.TryGetValue(clientId, out double t) && Now < t + RESPAWN_DELAY;
        }

        protected override bool SpawnOverride(ulong clientId, out Pose pose)
        {
            pose = default;
            if (RoundStage == Stage.Waiting) return false;
            int team = MatchNet.Instance != null ? MatchNet.Instance.TeamOf(clientId) : 0;
            if (team != 1 && team != 2) return false;
            pose = SidePose(team == AttackTeam, Random.Range(0, 64));
            return true;
        }

        // A spawn point of a role's side: escorts on the start side, hunters on the other side away from the extraction.
        Pose SidePose(bool escort, int pick)
        {
            int side = escort ? EscortSide : 3 - EscortSide;
            var pts = SidePoints(side);
            if (!escort)
            {
                var far = pts.FindAll(t => (t.position - ExtractPoint).sqrMagnitude > HUNTER_SPAWN_CLEARANCE * HUNTER_SPAWN_CLEARANCE);
                if (far.Count > 0) pts = far;
            }
            if (pts.Count == 0) return new Pose(escort ? StartPoint : PlayerSpawner.GetTeamZone(side).center, Quaternion.identity);
            var t = pts[((pick % pts.Count) + pts.Count) % pts.Count];
            return new Pose(t.position, t.rotation);
        }

        protected override Pose RoundSpawnPose(ulong id, int team, int index)
        {
            Quaternion toExtract = FaceTowards(StartPoint, ExtractPoint);
            if (team != AttackTeam) return SidePose(false, index + Round);
            if (id == Vip) return new Pose(StartPoint + Vector3.up * 0.1f, toExtract);
            // the escorts in a half circle around the VIP, facing the way out
            int k = index;
            for (int tries = 0; tries < 8; tries++, k++)
            {
                float a = (k % 8) * 45f + 22.5f;
                Vector3 p = StartPoint + Quaternion.Euler(0, a, 0) * Vector3.forward * 2.6f + Vector3.up * 0.1f;
                if (!Application.isPlaying || !Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.7f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                    return new Pose(p, toExtract);
            }
            return SidePose(true, index);
        }

        protected override bool AllowsWeapon(ulong clientId, WeaponData w) =>
            RoundStage == Stage.Waiting || clientId != Vip || IsSidearm(w);

        protected override string NameTag(ulong clientId) =>
            clientId == Vip && RoundStage != Stage.Waiting ? $"  <size=70%><color={Html(VipColor)}>VIP</color></size>" : "";

        protected override bool BlocksSprint(ulong clientId) => RoundStage != Stage.Waiting && clientId == Vip;

        protected override float MoveFactor(ulong clientId) =>
            base.MoveFactor(clientId) * (RoundStage != Stage.Waiting && clientId == Vip ? VIP_SPEED : 1f);

        // ───────────────────────── local respawn / spectating ─────────────────────────
        protected override bool LocalOutForRound => RoundStage == Stage.Over || (RoundStage == Stage.Live && LocalId == Vip);

        protected override bool RespawnRule(out float delay, out string heldText)
        {
            delay = RESPAWN_DELAY; heldText = null;
            if (RoundStage == Stage.Waiting) return false;
            if (RoundStage == Stage.Over || RoundStage == Stage.Prep) { delay = -1f; heldText = "Next round in " + Mathf.CeilToInt(StageLeft) + " s"; }
            else if (LocalId == Vip) { delay = -1f; heldText = "The VIP is down"; }
            return true;
        }

        // ───────────────────────── sync ─────────────────────────
        protected override void WriteRoundState(ref FastBufferWriter w)
        {
            w.WriteValueSafe(Vip);
            w.WriteValueSafe(VipPos);
            w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Clamp01(ExtractProgress) * 255f));
        }

        protected override void ReadRoundState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out ulong vip);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out byte prog);
            Vip = vip; VipPos = pos; ExtractProgress = prog / 255f;
        }

        // ───────────────────────── visuals ─────────────────────────
        static readonly Color VipColor = Hex("#FFD23F");
        Transform vipMark, smoke;

        void BuildVisuals()
        {
            var ex = BuildZone("Extraction", ExtractPoint, EXTRACT_RADIUS, Ready, 24);
            smoke = Prim(PrimitiveType.Cylinder, ex, new Vector3(0, 6f, 0), new Vector3(1.4f, 6f, 1.4f), Mat(A(Ready, 0.18f), true), "Smoke");
            BuildZone("VipStart", StartPoint, 1.4f, Hex("#D9D6CC"), 10);

            vipMark = new GameObject("VipMark").transform;
            vipMark.SetParent(transform, false);
            var d = Prim(PrimitiveType.Cube, vipMark, Vector3.zero, Vector3.one * 0.32f, Mat(VipColor), "Diamond");
            d.localRotation = Quaternion.Euler(45f, 0f, 45f);
            vipMark.gameObject.SetActive(false);
        }

        protected override void RoundVisualUpdate()
        {
            if (vipMark == null) return;
            bool show = Vip != ulong.MaxValue && Vip != LocalId && (RoundStage == Stage.Prep || RoundStage == Stage.Live);
            Transform tr = show ? FindPlayer(Vip) : null;
            var h = tr != null ? tr.GetComponent<Health>() : null;
            show &= h == null || !h.isDead.Value;
            vipMark.gameObject.SetActive(show);
            if (show)
            {
                Vector3 at = tr != null ? tr.position : VipPos;
                vipMark.SetPositionAndRotation(at + Vector3.up * (2.35f + 0.06f * Mathf.Sin(Time.time * 3f)), Quaternion.Euler(0, Time.time * 90f, 0));
            }
            if (smoke != null) smoke.localScale = new Vector3(1.4f + 0.15f * Mathf.Sin(Time.time * 1.7f), 6f, 1.4f + 0.15f * Mathf.Cos(Time.time * 1.3f));
        }

        // ───────────────────────── HUD ─────────────────────────
        ObjectiveHUD.Marker vipMarker, extractMarker;

        protected override void BuildModeHud(RectTransform widget, RectTransform markers)
        {
            vipMarker = ObjectiveHUD.Marker.Create(markers, "V", VipColor);
            extractMarker = ObjectiveHUD.Marker.Create(markers, "E", Ready);
        }

        protected override string RoleBriefing(bool attack)
        {
            if (LocalId == Vip) return "You are the VIP · sidearms only, no sprint · reach the extraction";
            return attack ? $"Escort {NameOf(Vip)} to the extraction · respawn in {RESPAWN_DELAY:0} s"
                          : $"Take down the VIP {NameOf(Vip)} before they reach the extraction";
        }

        protected override string StatusText(bool attack)
        {
            if (Vip == ulong.MaxValue) return "NO VIP THIS ROUND";
            if (LocalId == Vip) return $"<color={Html(VipColor)}>YOU ARE THE VIP</color> · get to the extraction";
            return attack ? $"ESCORT <color={Html(VipColor)}>{NameOf(Vip).ToUpperInvariant()}</color> TO THE EXTRACTION"
                          : $"TAKE DOWN THE VIP <color={Html(VipColor)}>{NameOf(Vip).ToUpperInvariant()}</color>";
        }

        protected override bool LocalAction(out string verb, out float progress, out bool doing)
        {
            verb = null; progress = ExtractProgress; doing = false;
            if (LocalId != Vip) return false;
            var h = LocalHealth();
            if (h == null || h.isDead.Value || !Near(h.transform.position, ExtractPoint, EXTRACT_RADIUS, 3f)) return false;
            doing = ExtractProgress > 0f;
            verb = doing ? "EXTRACTING" : "EXTRACT";
            return true;
        }

        protected override void UpdateModeHud(Camera cam, RectTransform markers, int myTeam, bool attack)
        {
            bool live = RoundStage == Stage.Live || RoundStage == Stage.Prep;
            bool meVip = LocalId == Vip;
            Vector3 at = VipPos;
            var tr = Vip != ulong.MaxValue ? FindPlayer(Vip) : null;
            if (tr != null) at = tr.position;
            vipMarker.SetPulse(!attack);
            vipMarker.Show(cam, markers, at + Vector3.up * 2.9f, attack ? "PROTECT" : "VIP", live && Vip != ulong.MaxValue && !meVip);

            var h = LocalHealth();
            bool inside = h != null && Near(h.transform.position, ExtractPoint, EXTRACT_RADIUS, 3f);
            extractMarker.SetPulse(ExtractProgress > 0f);
            extractMarker.Show(cam, markers, ExtractPoint + Vector3.up * 3f, attack ? "EXTRACTION" : "DEFEND EXTRACTION", live && !(meVip && inside));
        }
    }
}
