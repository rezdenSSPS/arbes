using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // One in the Chamber: everyone for themselves with the 1911 only. Each life starts with a single BB and
    // no spares; any BB hit takes the target out (so does a bang, which is all you have left once the BB is
    // gone). Every kill puts one more BB in your gun. Everyone has LIVES lives: out of lives = out of the
    // match (you watch the rest). The last player with lives left wins; on time-out the most kills, then
    // the most lives left (MatchNet). Players and bots who join mid-match get LATE_LIVES.
    //
    // Player points are the lives left (synced with the base state, shown on the scoreboard).
    public class OneInTheChamber : ObjectiveMode
    {
        public const string WEAPON = "1911 GBB Pistol";
        public const int LIVES = 3;
        public const int LATE_LIVES = 1;

        public static readonly Color Brass = Hex("#E0B252");

        bool started;
        int lastAlive = -1;

        public bool Started => started;
        public int LivesOf(ulong id) => PointsFor(id);

        protected override void OnBegin() { }

        // ───────────────────────── rules (server) ─────────────────────────
        public override void ServerTick(float dt)
        {
            var mn = MatchNet.Instance;
            if (mn == null || HasDeclared) return;
            if (!started)
            {
                if (mn.Roster.Count == 0) return;
                started = true;
                foreach (var e in mn.Roster) playerPoints[e.clientId] = LIVES;
                Announce($"ONE IN THE CHAMBER · 1 BB, {LIVES} lives · every kill = +1 BB", 0);
                MarkDirty();
            }
            foreach (var e in mn.Roster)
                if (!playerPoints.ContainsKey(e.clientId)) { playerPoints[e.clientId] = LATE_LIVES; MarkDirty(); }
            CheckLastStanding(mn);
        }

        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            var mn = MatchNet.Instance;
            if (mn == null || HasDeclared || !started) return;
            if (PointsFor(victim) > 0)
            {
                AddPlayerPoints(victim, -1);
                if (PointsFor(victim) == 0) Announce($"{NameOf(victim)} is out of lives", 0);
            }
            if (killer != victim && InRoster(mn, killer)) GiveBB(killer);
            CheckLastStanding(mn);
        }

        void GiveBB(ulong id)
        {
            var t = FindPlayer(id);
            if (t == null) return;
            var wc = t.GetComponent<WeaponController>();
            if (wc != null) { wc.ServerGiveAmmo(wc.IndexOfWeapon(WEAPON), 1); return; }
            var bot = t.GetComponent<BotBrain>();
            if (bot != null) bot.GiveAmmo(1);
        }

        void CheckLastStanding(MatchNet mn)
        {
            if (mn.Roster.Count < 2) return;   // alone (warming up): the clock decides
            int alive = 0;
            ulong last = ulong.MaxValue;
            foreach (var e in mn.Roster)
                if (PointsFor(e.clientId) > 0) { alive++; last = e.clientId; }
            if (alive == 1 && lastAlive != 1) Announce($"{NameOf(last)} is the LAST ONE STANDING", 0);
            lastAlive = alive;
            if (alive <= 1) DeclarePlayerWinner(alive == 1 ? last : ulong.MaxValue);
        }

        static bool InRoster(MatchNet mn, ulong id)
        {
            foreach (var e in mn.Roster) if (e.clientId == id) return true;
            return false;
        }

        // ───────────────────────── rules (any peer) ─────────────────────────
        protected override bool AllowsWeapon(ulong id, WeaponData w) => w.weaponName == WEAPON;
        protected override bool GrantsWeapon(ulong id, WeaponData w) => w.weaponName == WEAPON;

        protected override bool StartAmmo(ulong id, WeaponData w, out int mag, out int reserve)
        {
            mag = 1; reserve = 0;
            return w.weaponName == WEAPON;
        }

        // Any hit is a kill.
        protected override int DamageFor(ulong victim, ulong attacker, string weapon, int amount, int victimMax) =>
            amount > 0 ? Mathf.Max(amount, 9999) : amount;

        bool OutOfLives(ulong id) => started && PointsFor(id) <= 0;

        protected override bool HoldRespawn(ulong id) => OutOfLives(id);

        protected override bool RespawnRule(out float delay, out string heldText)
        {
            delay = 0f; heldText = null;
            var nm = NetworkManager.Singleton;
            if (nm == null || !OutOfLives(nm.LocalClientId)) return false;
            delay = -1f;
            heldText = "OUT OF LIVES";
            return true;
        }

        protected override bool Progress(ulong id, out float fraction, out string text)
        {
            int lives = PointsFor(id);
            fraction = lives / (float)LIVES;
            text = !started ? $"{LIVES} lives" : lives <= 0 ? "OUT" : lives == 1 ? "1 life" : $"{lives} lives";
            return true;
        }

        // ───────────────────────── network state ─────────────────────────
        protected override void WriteState(ref FastBufferWriter w) => w.WriteValueSafe(started);
        protected override void ReadState(ref FastBufferReader r) => r.ReadValueSafe(out started);

        // ───────────────────────── scoreboard / results text ─────────────────────────
        protected override string NameTag(ulong id) =>
            OutOfLives(id) ? $"  <size=70%><color={Html(Muted)}>OUT</color></size>" : "";

        protected override string ResultText(int winnerTeam, bool onTime, bool draw)
        {
            if (onTime) return draw ? "time's up · tied for most kills" : "time's up · most kills";
            return draw ? "nobody left standing" : "last one standing";
        }

        // ───────────────────────── HUD ─────────────────────────
        TextMeshProUGUI roleText;
        GameObject roleChip;
        readonly List<Image> pips = new List<Image>();

        public override void BuildHud(RectTransform widget, RectTransform markers)
        {
            var chip = HRow(widget, "OitcRole", 6, TextAnchor.MiddleCenter);
            chip.padding = new RectOffset(10, 12, 4, 4);
            chip.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            Label(chip.transform, "LIVES", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            for (int i = 0; i < LIVES; i++)
            {
                var p = Box(chip.transform, "Life" + i, Brass);
                p.LE(10, 10);
                pips.Add(p);
            }
            roleText = Label(chip.transform, "", F.BodySemi, 12, Fg, TextAlignmentOptions.MidlineLeft, 0.06f);
            roleChip = chip.gameObject;
        }

        public override void UpdateHud(Camera cam, RectTransform markers)
        {
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            ulong me = nm != null ? nm.LocalClientId : 0;
            int lives = started ? PointsFor(me) : LIVES;
            for (int i = 0; i < pips.Count; i++) pips[i].color = i < lives ? Brass : A(Muted, 0.35f);
            int alive = 0;
            if (mn != null) foreach (var e in mn.Roster) if (!started || PointsFor(e.clientId) > 0) alive++;
            roleText.text = lives <= 0
                ? $"  OUT · {alive} still standing"
                : $"  1 hit = out · kill = +1 BB · {alive} standing";
            roleChip.SetActive(mn != null);
        }
    }
}
