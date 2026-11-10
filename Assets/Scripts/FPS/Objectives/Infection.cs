using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Infection (zombie airsoft): SURVIVORS (team 2, Blue side) vs INFECTED (team 1, Red side).
    //
    // The match starts with one infected per started group of PLAYERS_PER_ALPHA players (the "alphas",
    // picked at random by MatchNet.HostStartMatch). Infected carry no replica: they tag survivors at close
    // range with the bang rule (LMB or V), move a bit faster and respawn as usual when a BB takes them out.
    // During the first GRACE_SECONDS the infected are held at their spawn so the survivors can spread out.
    //
    // A survivor who goes down for any reason turns: they switch to the infected team and respawn on the
    // infected side. Survivors win if at least one of them is still standing after SURVIVE_SECONDS;
    // the infected win the moment nobody is left to infect. Late joiners and bots added mid-match join
    // the infected.
    //
    // The team score shows how many players are on each side right now; player points are infections.
    public class Infection : ObjectiveMode
    {
        public const int TEAM_INFECTED = 1;
        public const int TEAM_SURVIVORS = 2;
        public const float GRACE_SECONDS = 8f;
        public const float SURVIVE_SECONDS = 240f;
        public const float INFECTED_SPEED = 1.15f;
        public const int PLAYERS_PER_ALPHA = 5;

        public static readonly Color Toxic = Hex("#8BE04E");

        // How many players start infected.
        public static int AlphasFor(int players) => players < 2 ? 0 : 1 + (players - 1) / PLAYERS_PER_ALPHA;

        public static bool IsInfected(ulong id)
        {
            var mn = MatchNet.Instance;
            return Active is Infection && mn != null && mn.TeamOf(id) == TEAM_INFECTED;
        }

        double graceEnd, endTime;   // server time; 0 = not started yet
        readonly HashSet<ulong> alphas = new HashSet<ulong>();
        int lastSurvivors = -1;
        readonly System.Random rng = new System.Random();

        bool Started => endTime > 0;
        // Not started yet (server: before the first tick, clients: before the first state) counts as grace.
        public bool InGrace => !Started || Now < graceEnd;
        public float GraceLeft => Started ? Mathf.Max(0f, (float)(graceEnd - Now)) : GRACE_SECONDS;
        public float SurviveLeft => Started ? Mathf.Max(0f, (float)(endTime - Now)) : SURVIVE_SECONDS;

        protected override void OnBegin() { }

        // ───────────────────────── rules (server) ─────────────────────────
        public override void ServerTick(float dt)
        {
            var mn = MatchNet.Instance;
            if (mn == null || HasDeclared) return;

            if (!Started)
            {
                graceEnd = Now + GRACE_SECONDS;
                endTime = graceEnd + SURVIVE_SECONDS;
                foreach (var e in mn.Roster) if (e.team == TEAM_INFECTED) alphas.Add(e.clientId);
                Announce($"OUTBREAK · the infected are released in {Mathf.RoundToInt(GRACE_SECONDS)} s", TEAM_INFECTED);
                MarkDirty();
            }

            Count(mn, out int infected, out int survivors);

            // Nobody infected (alpha left the game, or a solo start that someone joined): someone turns.
            if (infected == 0 && survivors >= 2)
            {
                var pool = new List<ulong>();
                foreach (var e in mn.Roster) if (e.team == TEAM_SURVIVORS) pool.Add(e.clientId);
                ulong pick = pool[rng.Next(pool.Count)];
                mn.ServerSetTeam(pick, TEAM_INFECTED);
                alphas.Add(pick);
                Announce($"{NameOf(pick)} has turned!", TEAM_INFECTED);
                Count(mn, out infected, out survivors);
            }

            SetScore(infected, survivors);

            if (survivors == 1 && lastSurvivors > 1)
                foreach (var e in mn.Roster)
                    if (e.team == TEAM_SURVIVORS) { Announce($"{e.name} is the LAST SURVIVOR", TEAM_SURVIVORS); break; }
            lastSurvivors = survivors;

            if (survivors == 0 && infected > 0) { Announce("Nobody left to infect", TEAM_INFECTED); DeclareWinner(TEAM_INFECTED); }
            else if (Now >= endTime && survivors > 0)
            {
                Announce(survivors == 1 ? "The last survivor held out!" : $"{survivors} survivors held out!", TEAM_SURVIVORS);
                DeclareWinner(TEAM_SURVIVORS);
            }
        }

        // A survivor who goes down turns (tagged by the infected, or any other way out).
        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            var mn = MatchNet.Instance;
            if (mn == null || HasDeclared || mn.TeamOf(victim) != TEAM_SURVIVORS) return;
            mn.ServerSetTeam(victim, TEAM_INFECTED);
            if (killer != victim && mn.TeamOf(killer) == TEAM_INFECTED)
            {
                AddPlayerPoints(killer, 1);
                Announce($"{NameOf(killer)} infected {NameOf(victim)}", TEAM_INFECTED);
            }
            else Announce($"{NameOf(victim)} has turned", TEAM_INFECTED);

            Count(mn, out int infected, out int survivors);
            SetScore(infected, survivors);
            if (survivors == 0) { Announce("Nobody left to infect", TEAM_INFECTED); DeclareWinner(TEAM_INFECTED); }
        }

        // Time ran out without a declared result (shouldn't happen: the mode runs its own clock).
        public override void WinnerOnTimeout(out int winnerTeam, out bool draw)
        {
            draw = false;
            winnerTeam = teamScore[TEAM_SURVIVORS] > 0 ? TEAM_SURVIVORS : TEAM_INFECTED;
        }

        static void Count(MatchNet mn, out int infected, out int survivors)
        {
            infected = survivors = 0;
            foreach (var e in mn.Roster)
            {
                if (e.team == TEAM_INFECTED) infected++;
                else if (e.team == TEAM_SURVIVORS) survivors++;
            }
        }

        void SetScore(int infected, int survivors)
        {
            if (teamScore[TEAM_INFECTED] == infected && teamScore[TEAM_SURVIVORS] == survivors) return;
            teamScore[TEAM_INFECTED] = infected;
            teamScore[TEAM_SURVIVORS] = survivors;
            MarkDirty();
        }

        // ───────────────────────── role rules (any peer) ─────────────────────────
        protected override bool AllowsWeapon(ulong id, WeaponData w) => !IsInfected(id);
        protected override bool MeleeOnlyRole(ulong id) => IsInfected(id);

        protected override float MoveFactor(ulong id)
        {
            if (!IsInfected(id)) return 1f;
            return InGrace ? 0f : INFECTED_SPEED;
        }

        // Bots: the infected hunt the nearest survivor (BotBrain closes in and tags them).
        protected override bool BotGoal(ulong botId, out Vector3 pos, out BotTask task)
        {
            pos = default; task = BotTask.None;
            if (!IsInfected(botId) || InGrace) return false;
            var me = FindPlayer(botId);
            var mn = MatchNet.Instance;
            if (me == null || mn == null) return false;
            float best = float.MaxValue;
            foreach (var e in mn.Roster)
            {
                if (e.team != TEAM_SURVIVORS) continue;
                var tr = FindPlayer(e.clientId);
                if (tr == null) continue;
                float d = (tr.position - me.position).sqrMagnitude;
                if (d < best) { best = d; pos = tr.position; }
            }
            if (best == float.MaxValue) return false;
            task = BotTask.GoTo;
            return true;
        }

        // ───────────────────────── network state ─────────────────────────
        protected override void WriteState(ref FastBufferWriter w)
        {
            w.WriteValueSafe(graceEnd);
            w.WriteValueSafe(endTime);
            w.WriteValueSafe(alphas.Count);
            foreach (var id in alphas) w.WriteValueSafe(id);
        }

        protected override void ReadState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out graceEnd);
            r.ReadValueSafe(out endTime);
            r.ReadValueSafe(out int n);
            alphas.Clear();
            for (int i = 0; i < n; i++) { r.ReadValueSafe(out ulong id); alphas.Add(id); }
        }

        // ───────────────────────── scoreboard / results text ─────────────────────────
        protected override string TeamDisplayName(int team) =>
            team == TEAM_INFECTED ? "INFECTED" : team == TEAM_SURVIVORS ? "SURVIVORS" : null;

        protected override string NameTag(ulong id) =>
            alphas.Contains(id) ? $"  <size=70%><color={Html(Toxic)}>ALPHA</color></size>" : "";

        protected override bool ScoreCaption(out string caption, out string value)
        {
            caption = InGrace ? "OUTBREAK IN" : "SURVIVE";
            value = MatchNet.FormatClock(InGrace ? GraceLeft : SurviveLeft);
            return true;
        }

        protected override string ResultText(int winnerTeam, bool onTime, bool draw)
        {
            if (winnerTeam == TEAM_SURVIVORS)
                return teamScore[TEAM_SURVIVORS] == 1 ? "the last survivor held out until the end"
                                                      : $"{teamScore[TEAM_SURVIVORS]} survivors held out until the end";
            return "everyone was infected";
        }

        // ───────────────────────── HUD ─────────────────────────
        TextMeshProUGUI roleText;
        GameObject roleChip;
        Image roleSwatch;
        GameObject graceRoot, vignette;
        TextMeshProUGUI graceTitle, graceCount;
        readonly List<ObjectiveHUD.Marker> survivorMarkers = new List<ObjectiveHUD.Marker>();

        public override void BuildHud(RectTransform widget, RectTransform markers)
        {
            var chip = HRow(widget, "InfectionRole", 8, TextAnchor.MiddleCenter);
            chip.padding = new RectOffset(8, 12, 4, 4);
            chip.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            roleSwatch = Box(chip.transform, "Swatch", Toxic);
            roleSwatch.LE(12, 12);
            roleText = Label(chip.transform, "", F.BodySemi, 12, Fg, TextAlignmentOptions.MidlineLeft, 0.06f);
            roleChip = chip.gameObject;

            var vig = Box(markers, "InfectedVignette", A(Toxic, 0.22f));
            vig.sprite = VignetteSprite;
            vig.rectTransform.Fill();
            vignette = vig.gameObject;
            vignette.SetActive(false);

            var col = VCol(markers, "Grace", 4, TextAnchor.UpperCenter, false);
            col.GetComponent<RectTransform>().At(0.5f, 1f, 0, -170, 760, 200);
            graceTitle = Label(col.transform, "", F.BodyBold, 12, Toxic, TextAlignmentOptions.Center, 0.24f);
            graceCount = Label(col.transform, "", F.CondBlack, 96, Fg, TextAlignmentOptions.Center, 0.04f);
            graceCount.LE(-1, 88);
            graceRoot = col.gameObject;
            graceRoot.SetActive(false);

            for (int i = 0; i < 3; i++) survivorMarkers.Add(ObjectiveHUD.Marker.Create(markers, "!", Blue));
        }

        public override void UpdateHud(Camera cam, RectTransform markers)
        {
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            ulong me = nm != null ? nm.LocalClientId : 0;
            int myTeam = mn != null ? mn.TeamOf(me) : 0;
            bool infected = myTeam == TEAM_INFECTED;

            roleChip.SetActive(myTeam != 0);
            roleSwatch.color = infected ? Toxic : Blue;
            roleText.text = infected
                ? "INFECTED · LMB / V to tag survivors up close"
                : $"SURVIVOR · hold out {MatchNet.FormatClock(SurviveLeft)} · don't let them touch you";

            vignette.SetActive(infected);
            if (vignette.activeSelf)
                vignette.GetComponent<Image>().color = A(Toxic, 0.16f + 0.06f * Mathf.Sin(Time.unscaledTime * 2f));

            bool grace = InGrace && myTeam != 0;
            graceRoot.SetActive(grace);
            if (grace)
            {
                graceTitle.text = infected ? "YOU ARE INFECTED · RELEASED IN" : "THE INFECTED ARE RELEASED IN";
                graceCount.text = Mathf.CeilToInt(GraceLeft).ToString();
            }

            // the last survivors light up for everyone (classic endgame: nowhere to hide)
            int mi = 0;
            int revealAt = mn != null ? Mathf.Clamp(mn.Roster.Count / 4, 1, survivorMarkers.Count) : 1;   // 8 players: last 2
            if (mn != null && Started && teamScore[TEAM_SURVIVORS] > 0 && teamScore[TEAM_SURVIVORS] <= revealAt)
                foreach (var e in mn.Roster)
                {
                    if (mi >= survivorMarkers.Count) break;
                    if (e.team != TEAM_SURVIVORS || e.clientId == me) continue;
                    var tr = FindPlayer(e.clientId);
                    if (tr == null) continue;
                    var m = survivorMarkers[mi++];
                    m.SetColor(Blue);
                    m.SetPulse(teamScore[TEAM_SURVIVORS] == 1);
                    m.Show(cam, markers, tr.position + Vector3.up * 2f, e.name.ToUpperInvariant(), true);
                }
            for (; mi < survivorMarkers.Count; mi++) survivorMarkers[mi].Show(cam, markers, Vector3.zero, "", false);
        }
    }
}
