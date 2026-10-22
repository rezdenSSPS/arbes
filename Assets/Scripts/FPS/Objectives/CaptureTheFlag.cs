using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Capture the Flag (Red vs Blue). Each team's flag stands at its base (MapLayout.redBase / blueBase).
    // Touch the enemy flag to take it (it rides on your back, you can't sprint), bring it to your own
    // stand while your own flag is home to score. A carrier who dies (or is HIT in Milsim rules) drops
    // it; the owning team returns a dropped flag by touching it, or it goes home by itself after 20 s.
    public class CaptureTheFlag : ObjectiveMode
    {
        public const int CAPTURE_TARGET = 3;
        public const float AUTO_RETURN_SECONDS = 20f;
        public const float TOUCH_RADIUS = 1.8f;     // pick up / return
        public const float CAPTURE_RADIUS = 2.6f;   // around your own stand

        public static string MissingOnMap(MapCaps c) => c.redBase && c.blueBase ? null : "Not on this map: no flag bases";

        public enum FlagState : byte { Home = 0, Carried = 1, Dropped = 2 }

        public class Flag
        {
            public int team;
            public Vector3 home;
            public FlagState state;
            public ulong carrier = ulong.MaxValue;
            public Vector3 pos;          // where it is (dropped spot / last carrier position)
            public double returnAt;      // server time a dropped flag goes home
            public Transform visual;     // pole + cloth (moves)
            public Transform stand;      // base plate (stays)
        }

        readonly Flag[] flags = { null, new Flag { team = 1 }, new Flag { team = 2 } };

        public Flag FlagOf(int team) => team == 1 || team == 2 ? flags[team] : null;

        public bool IsCarrying(ulong id, out int flagTeam)
        {
            for (int t = 1; t <= 2; t++)
                if (flags[t].state == FlagState.Carried && flags[t].carrier == id) { flagTeam = t; return true; }
            flagTeam = 0;
            return false;
        }

        protected override bool BlocksSprint(ulong clientId) => IsCarrying(clientId, out _);

        // ───────────────────────── setup ─────────────────────────
        protected override void OnBegin()
        {
            var map = MapLayout.Current;
            Vector3 red = map != null && map.redBase != null ? map.redBase.position : PlayerSpawner.GetTeamZone(1).center;
            Vector3 blue = map != null && map.blueBase != null ? map.blueBase.position : PlayerSpawner.GetTeamZone(2).center;
            SetHomes(red, blue);
            for (int t = 1; t <= 2; t++) BuildVisual(flags[t]);
        }

        public void SetHomes(Vector3 red, Vector3 blue)
        {
            flags[1].home = flags[1].pos = red;
            flags[2].home = flags[2].pos = blue;
        }

        // ───────────────────────── server rules ─────────────────────────
        public override void ServerTick(float dt)
        {
            GatherParticipants();
            double now = Now;

            // carriers first: follow them, drop when they're out or gone
            for (int t = 1; t <= 2; t++)
            {
                var f = flags[t];
                if (f.state == FlagState.Carried)
                {
                    if (TryGetParticipant(f.carrier, out var p) && p.active && p.team == 3 - t)
                    {
                        if ((f.pos - p.position).sqrMagnitude > 0.04f) MarkDirty();
                        f.pos = p.position;
                    }
                    else Drop(f, TryGetParticipant(f.carrier, out p) ? p.position : f.pos, now);
                }
                else if (f.state == FlagState.Dropped && (now >= f.returnAt || f.pos.y < -20f))
                {
                    ReturnHome(f);
                    Announce($"{TeamName(t)} flag returned to base", t);
                }
            }

            foreach (var p in participants)
            {
                if (!p.active || (p.team != 1 && p.team != 2)) continue;
                var own = flags[p.team];
                var enemy = flags[3 - p.team];

                // return your own dropped flag
                if (own.state == FlagState.Dropped && Near(p.position, own.pos, TOUCH_RADIUS))
                {
                    ReturnHome(own);
                    Announce($"{NameOf(p.id)} returned the {TeamName(own.team)} flag", p.team);
                }

                // take the enemy flag
                if ((enemy.state == FlagState.Home && Near(p.position, enemy.home, TOUCH_RADIUS)) ||
                    (enemy.state == FlagState.Dropped && Near(p.position, enemy.pos, TOUCH_RADIUS)))
                {
                    enemy.state = FlagState.Carried;
                    enemy.carrier = p.id;
                    enemy.pos = p.position;
                    MarkDirty();
                    Announce($"{NameOf(p.id)} took the {TeamName(enemy.team)} flag", p.team);
                }

                // score: enemy flag to your stand while yours is home
                if (enemy.state == FlagState.Carried && enemy.carrier == p.id && own.state == FlagState.Home
                    && Near(p.position, own.home, CAPTURE_RADIUS))
                {
                    ReturnHome(enemy);
                    AddPlayerPoints(p.id, 1);
                    Announce($"{NameOf(p.id)} captured the {TeamName(enemy.team)} flag", p.team);
                    AddTeamScore(p.team, 1);   // may end the match
                }
            }
        }

        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            for (int t = 1; t <= 2; t++)
            {
                var f = flags[t];
                if (f.state != FlagState.Carried || f.carrier != victim) continue;
                Vector3 at = f.pos;
                var tr = FindPlayer(victim);
                if (tr != null) at = tr.position;
                Drop(f, at, Now);
            }
        }

        void Drop(Flag f, Vector3 at, double now)
        {
            f.state = FlagState.Dropped;
            f.carrier = ulong.MaxValue;
            f.pos = GroundAt(at);
            f.returnAt = now + AUTO_RETURN_SECONDS;
            MarkDirty();
            Announce($"{TeamName(f.team)} flag dropped", f.team);
        }

        void ReturnHome(Flag f)
        {
            f.state = FlagState.Home;
            f.carrier = ulong.MaxValue;
            f.pos = f.home;
            f.returnAt = 0;
            MarkDirty();
        }

        // Snap a drop spot to the ground below it (ignoring players).
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
        protected override void WriteState(ref FastBufferWriter w)
        {
            for (int t = 1; t <= 2; t++)
            {
                var f = flags[t];
                w.WriteValueSafe((byte)f.state);
                w.WriteValueSafe(f.carrier);
                w.WriteValueSafe(f.pos);
                w.WriteValueSafe(f.returnAt);
            }
        }

        protected override void ReadState(ref FastBufferReader r)
        {
            for (int t = 1; t <= 2; t++)
            {
                var f = flags[t];
                r.ReadValueSafe(out byte st);
                f.state = (FlagState)st;
                r.ReadValueSafe(out f.carrier);
                r.ReadValueSafe(out f.pos);
                r.ReadValueSafe(out f.returnAt);
            }
        }

        // ───────────────────────── visuals ─────────────────────────
        void BuildVisual(Flag f)
        {
            Color c = TeamTint(f.team);
            var stand = new GameObject(TeamName(f.team) + "FlagStand").transform;
            stand.SetParent(transform, false);
            stand.position = f.home;
            Prim(PrimitiveType.Cylinder, stand, new Vector3(0, 0.04f, 0), new Vector3(1.6f, 0.04f, 1.6f), Mat(Hex("#2A2D26")), "Plate");
            Prim(PrimitiveType.Cylinder, stand, new Vector3(0, 0.09f, 0), new Vector3(1.2f, 0.02f, 1.2f), Mat(c), "Ring");
            f.stand = stand;

            var v = new GameObject(TeamName(f.team) + "Flag").transform;
            v.SetParent(transform, false);
            Prim(PrimitiveType.Cylinder, v, new Vector3(0, 1.25f, 0), new Vector3(0.06f, 1.25f, 0.06f), Mat(Hex("#D9D6CC")), "Pole");
            Prim(PrimitiveType.Cube, v, new Vector3(0.5f, 2.15f, 0), new Vector3(0.95f, 0.6f, 0.03f), Mat(c), "Cloth");
            Prim(PrimitiveType.Sphere, v, new Vector3(0, 2.55f, 0), Vector3.one * 0.12f, Mat(Hex("#D9D6CC")), "Knob");
            f.visual = v;
        }

        protected override void VisualUpdate()
        {
            for (int t = 1; t <= 2; t++)
            {
                var f = flags[t];
                if (f.visual == null) continue;
                switch (f.state)
                {
                    case FlagState.Carried:
                        var tr = FindPlayer(f.carrier);
                        if (tr != null)
                        {
                            // on the carrier's back, leaning back a little
                            f.visual.SetPositionAndRotation(tr.position + tr.rotation * new Vector3(0.18f, 0.55f, -0.32f),
                                                            tr.rotation * Quaternion.Euler(-14f, 0f, 0f));
                            f.visual.localScale = Vector3.one * 0.7f;
                        }
                        else { f.visual.SetPositionAndRotation(f.pos, Quaternion.identity); f.visual.localScale = Vector3.one * 0.7f; }
                        break;
                    case FlagState.Dropped:
                        f.visual.SetPositionAndRotation(f.pos + Vector3.up * (0.1f + 0.08f * Mathf.Sin(Time.time * 3f)),
                                                        Quaternion.Euler(0f, Time.time * 60f, 0f));
                        f.visual.localScale = Vector3.one * 0.8f;
                        break;
                    default:
                        f.visual.SetPositionAndRotation(f.home + Vector3.up * 0.1f, Quaternion.identity);
                        f.visual.localScale = Vector3.one;
                        break;
                }
            }
        }

        // ───────────────────────── HUD ─────────────────────────
        class Chip { public Image swatch, frame; public TextMeshProUGUI name, status; }
        readonly Chip[] chips = new Chip[3];
        GameObject carryBanner;
        TextMeshProUGUI carryTitle, carryHint;
        readonly ObjectiveHUD.Marker[] flagMarkers = new ObjectiveHUD.Marker[3];
        ObjectiveHUD.Marker baseMarker;

        public override void BuildHud(RectTransform widget, RectTransform markers)
        {
            var row = HRow(widget, "Flags", 6, TextAnchor.MiddleCenter);
            for (int t = 1; t <= 2; t++)
            {
                var chip = new Chip();
                var r = HRow(row.transform, TeamName(t), 8, TextAnchor.MiddleLeft);
                r.padding = new RectOffset(8, 10, 4, 4);
                r.gameObject.AddComponent<Image>().color = Scrim(0.78f);
                chip.frame = Frame(r.transform, TeamColor(t), 1f);
                chip.frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                chip.swatch = Box(r.transform, "Swatch", TeamColor(t));
                chip.swatch.LE(10, 18);
                chip.name = Label(r.transform, TeamName(t) + " FLAG", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
                chip.status = Label(r.transform, "HOME", F.CondBold, 15, Fg);
                chip.status.LE(110, 20);
                chip.status.overflowMode = TextOverflowModes.Ellipsis;
                chip.frame.transform.SetAsLastSibling();
                chips[t] = chip;
            }

            var banner = VCol(widget, "Carrying", 0, TextAnchor.MiddleCenter, false);
            banner.padding = new RectOffset(14, 14, 4, 6);
            banner.gameObject.AddComponent<Image>().color = A(Accent, 0.9f);
            carryTitle = Label(banner.transform, "YOU HAVE THE FLAG", F.CondBlack, 18, Ground, TextAlignmentOptions.Center, 0.04f);
            carryHint = Label(banner.transform, "", F.BodySemi, 12, Ground, TextAlignmentOptions.Center);
            carryBanner = banner.gameObject;
            carryBanner.SetActive(false);

            for (int t = 1; t <= 2; t++) flagMarkers[t] = ObjectiveHUD.Marker.Create(markers, "F", TeamColor(t));
            baseMarker = ObjectiveHUD.Marker.Create(markers, "H", Accent);
        }

        public override void UpdateHud(Camera cam, RectTransform markers)
        {
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            ulong me = nm != null ? nm.LocalClientId : 0;
            int myTeam = mn != null ? mn.TeamOf(me) : 0;
            double now = Now;
            bool carrying = IsCarrying(me, out _);

            for (int t = 1; t <= 2; t++)
            {
                var f = flags[t];
                var chip = chips[t];
                if (chip == null) continue;
                string status; Color col = Fg;
                switch (f.state)
                {
                    case FlagState.Carried:
                        status = f.carrier == me ? "YOU" : NameOf(f.carrier).ToUpperInvariant();
                        col = Accent;
                        break;
                    case FlagState.Dropped:
                        status = "DROPPED " + Mathf.CeilToInt(Mathf.Max(0f, (float)(f.returnAt - now))) + "s";
                        col = DangerSoft;
                        break;
                    default: status = "HOME"; break;
                }
                chip.status.text = status;
                chip.status.color = col;
                chip.frame.enabled = f.state != FlagState.Home;

                // world marker: where the flag is now (not the one on your own back)
                var m = flagMarkers[t];
                Vector3 at = f.state == FlagState.Home ? f.home : f.pos;
                if (f.state == FlagState.Carried) { var tr = FindPlayer(f.carrier); if (tr != null) at = tr.position; }
                string label = f.state == FlagState.Home ? (t == myTeam ? "YOUR FLAG" : TeamName(t) + " FLAG")
                    : f.state == FlagState.Dropped ? (t == myTeam ? "RETURN" : "TAKE")
                    : (t == myTeam ? "KILL CARRIER" : "ESCORT");
                m.Show(cam, markers, at + Vector3.up * 2.9f, label, !(f.state == FlagState.Carried && f.carrier == me));
                m.SetPulse(f.state != FlagState.Home);
            }

            // carrying: banner + marker on your own stand
            carryBanner.SetActive(carrying);
            if (carrying && myTeam != 0)
            {
                bool ownHome = flags[myTeam].state == FlagState.Home;
                carryHint.text = ownHome ? "Bring it to your base · you can't sprint" : "Your flag must be home to score";
                baseMarker.Show(cam, markers, flags[myTeam].home + Vector3.up * 1.2f, "SCORE HERE", true);
            }
            else baseMarker.Show(cam, markers, Vector3.zero, "", false);
        }
    }
}
