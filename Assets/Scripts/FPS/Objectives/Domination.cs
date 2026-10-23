using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Domination (Red vs Blue): control points A / B / C (MapLayout.controlPoints). Stand inside a point's
    // radius to capture it; a point with both teams inside is contested and doesn't move. An enemy point
    // is first neutralised, then captured (twice the time). Every SCORE_INTERVAL seconds each owned point
    // gives its team 1 point; first to SCORE_TARGET wins (the match time limit still applies).
    public class Domination : ObjectiveMode
    {
        public const int SCORE_TARGET = 200;
        public const float SCORE_INTERVAL = 2f;     // seconds per point per owned control point
        public const float CAPTURE_SECONDS = 6f;    // neutral -> owned with one player (more players: faster, max 2x)
        public const float RADIUS = 6f;

        public static string MissingOnMap(MapCaps c) => c.controlPoints >= 2 ? null : "Not on this map: no control points";

        public class Point
        {
            public string label;
            public Vector3 pos;
            public int owner;        // 0 neutral, 1 Red, 2 Blue
            public int capTeam;      // team the progress belongs to
            public float progress;   // 0..1
            public bool contested;
            public int red, blue;    // players inside
            // visuals
            public Transform root, banner;
            public readonly List<Transform> posts = new List<Transform>();
            public Renderer disc;
            public int shownOwner = -1;
        }

        public readonly List<Point> points = new List<Point>();
        float scoreTimer;

        // ───────────────────────── setup ─────────────────────────
        protected override void OnBegin()
        {
            var map = MapLayout.Current;
            var list = new List<Vector3>();
            if (map != null && map.controlPoints != null)
                foreach (var t in map.controlPoints) if (t != null) list.Add(t.position);
            SetPoints(list);
            foreach (var p in points) BuildVisual(p);
        }

        public void SetPoints(IList<Vector3> positions)
        {
            points.Clear();
            for (int i = 0; i < positions.Count && i < 8; i++)
                points.Add(new Point { label = ((char)('A' + i)).ToString(), pos = positions[i] });
        }

        // Bots: head for a point we don't own (capture / neutralise), spreading out by id; if we own them
        // all, fan out to defend. Standing inside the ring is scored by ServerTick.
        protected override bool BotGoal(ulong botId, out Vector3 pos, out BotTask task)
        {
            pos = default; task = BotTask.GoTo;
            var mn = MatchNet.Instance;
            int team = mn != null ? mn.TeamOf(botId) : 0;
            if (points.Count == 0 || (team != 1 && team != 2)) return false;

            var cands = new List<Point>();
            foreach (var p in points) if (p.owner != team) cands.Add(p);
            if (cands.Count == 0) cands = points;

            var pick = cands[(int)(botId % (ulong)cands.Count)];
            float ang = (botId % 8) * Mathf.PI * 2f / 8f;   // off-centre so several bots don't share one spot
            pos = pick.pos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (RADIUS * 0.4f);
            return true;
        }

        // ───────────────────────── server rules ─────────────────────────
        public override void ServerTick(float dt)
        {
            GatherParticipants();
            foreach (var pt in points)
            {
                int r = 0, b = 0;
                foreach (var p in participants)
                {
                    if (!p.active || !Near(p.position, pt.pos, RADIUS, 3f)) continue;
                    if (p.team == 1) r++; else if (p.team == 2) b++;
                }
                bool contested = r > 0 && b > 0;
                if (r != pt.red || b != pt.blue || contested != pt.contested) MarkDirty();
                pt.red = r; pt.blue = b; pt.contested = contested;
                if (contested) continue;   // frozen while both teams are inside

                int team = r > 0 ? 1 : b > 0 ? 2 : 0;
                int n = team == 1 ? r : b;
                float before = pt.progress;
                if (team == 0)
                {
                    // nobody inside: a half-done capture slowly fades
                    pt.progress = Mathf.Max(0f, pt.progress - dt / (CAPTURE_SECONDS * 2f));
                }
                else if (team == pt.owner)
                {
                    // owners push back an enemy's partial capture
                    pt.progress = Mathf.Max(0f, pt.progress - dt / CAPTURE_SECONDS);
                }
                else
                {
                    float rate = Mathf.Min(1f + 0.5f * (n - 1), 2f) / CAPTURE_SECONDS * dt;
                    if (pt.capTeam != team && pt.progress > 0f)
                        pt.progress = Mathf.Max(0f, pt.progress - rate);   // undo the other team's progress first
                    else
                    {
                        pt.capTeam = team;
                        pt.progress += rate;
                        if (pt.progress >= 1f)
                        {
                            pt.progress = 0f;
                            if (pt.owner != 0)
                            {
                                Announce($"{TeamName(team)} neutralised {pt.label}", team);
                                pt.owner = 0;       // keep going: neutral -> theirs
                            }
                            else
                            {
                                pt.owner = team;
                                pt.capTeam = 0;
                                CreditInside(pt, team);
                                Announce($"{TeamName(team)} captured {pt.label}", team);
                            }
                            MarkDirty();
                        }
                    }
                }
                if (pt.progress <= 0f && team != pt.capTeam && pt.capTeam != 0) { pt.capTeam = 0; MarkDirty(); }
                if (!Mathf.Approximately(before, pt.progress)) MarkDirty();
            }

            scoreTimer += dt;
            while (scoreTimer >= SCORE_INTERVAL)
            {
                scoreTimer -= SCORE_INTERVAL;
                int red = 0, blue = 0;
                foreach (var pt in points) { if (pt.owner == 1) red++; else if (pt.owner == 2) blue++; }
                // both teams at once so a tie at the target is decided by the higher score, not team order
                if (red > 0) teamScore[1] += red;
                if (blue > 0) teamScore[2] += blue;
                if (red + blue > 0)
                {
                    teamScore[1] = Mathf.Min(teamScore[1], SCORE_TARGET);
                    teamScore[2] = Mathf.Min(teamScore[2], SCORE_TARGET);
                    if (teamScore[1] == SCORE_TARGET && teamScore[2] == SCORE_TARGET) teamScore[blue > red ? 1 : 2]--;
                    NotifyScore();
                }
            }
        }

        void NotifyScore()
        {
            MarkDirty();
            if (testParticipants == null) MatchNet.Instance?.NotifyObjectiveScore();
        }

        void CreditInside(Point pt, int team)
        {
            foreach (var p in participants)
                if (p.active && p.team == team && Near(p.position, pt.pos, RADIUS, 3f)) AddPlayerPoints(p.id, 1);
        }

        // ───────────────────────── sync ─────────────────────────
        protected override void WriteState(ref FastBufferWriter w)
        {
            w.WriteValueSafe((byte)points.Count);
            foreach (var p in points)
            {
                w.WriteValueSafe((byte)p.owner);
                w.WriteValueSafe((byte)p.capTeam);
                w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Clamp01(p.progress) * 255f));
                w.WriteValueSafe((byte)Mathf.Min(p.red, 255));
                w.WriteValueSafe((byte)Mathf.Min(p.blue, 255));
            }
        }

        protected override void ReadState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte n);
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out byte owner);
                r.ReadValueSafe(out byte cap);
                r.ReadValueSafe(out byte prog);
                r.ReadValueSafe(out byte red);
                r.ReadValueSafe(out byte blue);
                if (i >= points.Count) continue;
                var p = points[i];
                p.owner = owner; p.capTeam = cap; p.progress = prog / 255f;
                p.red = red; p.blue = blue; p.contested = red > 0 && blue > 0;
            }
        }

        // ───────────────────────── visuals ─────────────────────────
        const int POSTS = 28;

        void BuildVisual(Point p)
        {
            p.root = new GameObject("ControlPoint_" + p.label).transform;
            p.root.SetParent(transform, false);
            p.root.position = p.pos;
            var disc = Prim(PrimitiveType.Cylinder, p.root, new Vector3(0, 0.05f, 0), new Vector3(RADIUS * 2f, 0.01f, RADIUS * 2f),
                            Mat(A(TeamTint(0), 0.16f), true), "Area");
            p.disc = disc.GetComponent<Renderer>();
            for (int i = 0; i < POSTS; i++)
            {
                float a = i * Mathf.PI * 2f / POSTS;
                var post = Prim(PrimitiveType.Cube, p.root, new Vector3(Mathf.Cos(a) * RADIUS, 0.12f, Mathf.Sin(a) * RADIUS),
                                new Vector3(0.18f, 0.24f, 0.18f), Mat(TeamTint(0)), "Edge");
                p.posts.Add(post);
            }
            Prim(PrimitiveType.Cylinder, p.root, new Vector3(0, 1.6f, 0), new Vector3(0.08f, 1.6f, 0.08f), Mat(Hex("#D9D6CC")), "Pole");
            p.banner = Prim(PrimitiveType.Cube, p.root, new Vector3(0.45f, 2.75f, 0), new Vector3(0.85f, 0.55f, 0.04f), Mat(TeamTint(0)), "Banner");
        }

        protected override void VisualUpdate()
        {
            foreach (var p in points)
            {
                if (p.root == null) continue;
                if (p.shownOwner != p.owner)
                {
                    p.shownOwner = p.owner;
                    var solid = Mat(TeamTint(p.owner));
                    foreach (var post in p.posts) Recolor(post, solid);
                    Recolor(p.banner, solid);
                    if (p.disc != null) p.disc.sharedMaterial = Mat(A(TeamTint(p.owner), 0.16f), true);
                }
                // the banner climbs with capture progress; flutters a little
                // neutral: low, rises as a team captures; owned: at the top, sinks while being neutralised
                float h = p.owner == 0 ? Mathf.Lerp(0.6f, 2.75f, p.capTeam != 0 ? p.progress : 0f)
                                       : Mathf.Lerp(2.75f, 0.6f, p.progress);
                p.banner.localPosition = new Vector3(0.45f, h, 0);
                p.banner.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 2f + p.pos.x) * 12f, 0);
            }
        }

        // ───────────────────────── HUD ─────────────────────────
        class Tile { public Image bg, frame, bar; public TextMeshProUGUI letter; }
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<ObjectiveHUD.Marker> markers = new List<ObjectiveHUD.Marker>();
        TextMeshProUGUI statusLine;
        GameObject statusChip;

        public override void BuildHud(RectTransform widget, RectTransform markerLayer)
        {
            var row = HRow(widget, "Points", 6, TextAnchor.MiddleCenter);
            foreach (var p in points)
            {
                var t = new Tile();
                t.bg = Box(row.transform, "Point_" + p.label, Scrim(0.78f));
                t.bg.LE(40, 40);
                t.letter = Label(t.bg.transform, p.label, F.CondBlack, 24, Fg, TextAlignmentOptions.Center);
                t.letter.rectTransform.Fill(0, 0, 0, 4);
                t.bar = Bar(t.bg.transform, "Progress", Color.clear, Fg);
                var back = (RectTransform)t.bar.transform.parent;
                back.anchorMin = new Vector2(0, 0); back.anchorMax = new Vector2(1, 0); back.pivot = new Vector2(0.5f, 0);
                back.offsetMin = Vector2.zero; back.offsetMax = new Vector2(0, 4);
                t.frame = Frame(t.bg.transform, Accent, 2f);
                tiles.Add(t);
                markers.Add(ObjectiveHUD.Marker.Create(markerLayer, p.label, TeamTint(0)));
            }
            var chip = HRow(widget, "Status", 0, TextAnchor.MiddleCenter);
            chip.padding = new RectOffset(10, 10, 3, 3);
            chip.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            statusLine = Label(chip.transform, "", F.BodyBold, 12, Fg, TextAlignmentOptions.Center, 0.12f);
            statusChip = chip.gameObject;
            statusChip.SetActive(false);
        }

        public override void UpdateHud(Camera cam, RectTransform markerLayer)
        {
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            ulong me = nm != null ? nm.LocalClientId : 0;
            int myTeam = mn != null ? mn.TeamOf(me) : 0;
            Transform myTr = FindPlayer(me);
            string status = null;

            for (int i = 0; i < points.Count && i < tiles.Count; i++)
            {
                var p = points[i];
                var t = tiles[i];
                Color own = p.owner == 0 ? Fg : TeamColor(p.owner);
                t.bg.color = p.owner == 0 ? Scrim(0.78f) : A(TeamColor(p.owner), 0.92f);
                t.letter.color = p.owner == 0 ? Fg : Ground;
                t.bar.color = p.capTeam == 0 ? Fg : TeamColor(p.capTeam);
                SetBar(t.bar, p.capTeam != 0 ? p.progress : 0f);
                bool pulse = p.contested && Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f;
                t.frame.enabled = p.contested;
                t.frame.color = pulse ? Accent : A(Accent, 0.45f);

                bool inside = myTr != null && Near(myTr.position, p.pos, RADIUS, 3f);
                if (inside && myTeam != 0)
                {
                    if (p.contested) status = $"<color={Html(Accent)}>CONTESTED {p.label}</color>";
                    else if (p.owner == myTeam && p.progress <= 0f) status = $"HOLDING {p.label}";
                    else if (p.owner == myTeam) status = $"DEFENDING {p.label}";
                    else if (p.capTeam == myTeam || p.progress <= 0f)
                        status = (p.owner == 0 ? "CAPTURING " : "NEUTRALISING ") + p.label + "  " + Mathf.RoundToInt(p.progress * 100f) + "%";
                    else status = $"PUSHING BACK {p.label}";
                }

                var m = markers[i];
                m.SetColor(own);
                m.SetPulse(p.contested || (p.capTeam != 0 && p.progress > 0f));
                m.Show(cam, markerLayer, p.pos + Vector3.up * 3.4f,
                       p.contested ? "CONTESTED" : p.owner == 0 ? "CAPTURE" : p.owner == myTeam ? "DEFEND" : "ATTACK", !inside);
            }
            statusChip.SetActive(status != null);
            if (status != null) statusLine.text = status;
        }
    }
}
