using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Juggernaut: one player carries the RPK support gun, moves slower and can't sprint; everyone else hunts
    // them. The hunters don't hurt each other. The juggernaut goes down after HitsFor(players) hits (a bang
    // counts as BANG_HITS), whatever the weapon: about 8 hits with 8 players. Whoever takes the juggernaut
    // down becomes the next one, with full health and a fully loaded RPK (a fall or a disconnect hands it to
    // a random player).
    //
    // Points are the roster's kills: a kill as the juggernaut, or taking the juggernaut down. First to
    // MatchNet.JUGG_KILL_TARGET wins; on time-out the most kills (MatchNet, like Free-for-All). Bots hunt the
    // juggernaut, and a bot juggernaut fights everyone.
    public class Juggernaut : ObjectiveMode
    {
        public const string WEAPON = "RPK Support AEG";
        public const float SPEED = 0.85f;
        public const int BANG_HITS = 3;
        const ulong NONE = ulong.MaxValue;

        public static readonly Color Gold = Hex("#F2B33D");

        // Hits that take the juggernaut down: grows with the lobby (8 players: 8 hits).
        public static int HitsFor(int players) => Mathf.Clamp(players, 4, 12);

        // Hits a juggernaut with this much health still takes (any peer: health is a synced NetworkVariable).
        public static int HitsLeft(int hp, int maxHp, int hits)
        {
            int per = Mathf.CeilToInt(maxHp / (float)Mathf.Max(1, hits));
            return Mathf.CeilToInt(hp / (float)Mathf.Max(1, per));
        }

        ulong jugg = NONE;
        int hits = 4;
        readonly System.Random rng = new System.Random();

        public ulong Current => jugg;
        public int HitsToDown => hits;
        public static bool IsJuggernaut(ulong id) => Active is Juggernaut j && j.jugg != NONE && j.jugg == id;

        protected override void OnBegin() { }

        // ───────────────────────── rules (server) ─────────────────────────
        public override void ServerTick(float dt)
        {
            var mn = MatchNet.Instance;
            if (mn == null || HasDeclared) return;
            int h = HitsFor(mn.Roster.Count);
            if (h != hits) { hits = h; MarkDirty(); }
            if (jugg == NONE || !InRoster(mn, jugg))
            {
                ulong pick = RandomPlayer(mn, NONE);
                if (pick != NONE)
                {
                    MakeJuggernaut(pick);
                    Announce($"{NameOf(pick)} is the JUGGERNAUT", 0);
                }
            }
        }

        public override void ServerPlayerOut(ulong victim, ulong killer)
        {
            var mn = MatchNet.Instance;
            if (mn == null || HasDeclared || victim != jugg) return;
            if (killer != victim && InRoster(mn, killer))
            {
                MatchAwards.RecordTakedown(killer);   // Giant Slayer
                MakeJuggernaut(killer);
                Announce($"{NameOf(killer)} took down {NameOf(victim)} · new JUGGERNAUT", 0);
                return;
            }
            ulong pick = RandomPlayer(mn, victim);
            if (pick == NONE) pick = victim;   // alone: stay the juggernaut
            MakeJuggernaut(pick);
            Announce($"{NameOf(victim)} went down · {NameOf(pick)} is the JUGGERNAUT", 0);
        }

        void MakeJuggernaut(ulong id)
        {
            jugg = id;
            MarkDirty();
            var t = FindPlayer(id);
            if (t == null) return;   // not spawned yet: a fresh spawn is at full health anyway
            var h = t.GetComponent<Health>();
            if (h != null) h.ServerHealFull();
            var wc = t.GetComponent<WeaponController>();
            if (wc != null) wc.ServerRefillAmmo();   // a full RPK box (the owner switches to it on its own)
        }

        static bool InRoster(MatchNet mn, ulong id)
        {
            foreach (var e in mn.Roster) if (e.clientId == id) return true;
            return false;
        }

        // A random player other than `except`, preferring someone alive.
        ulong RandomPlayer(MatchNet mn, ulong except)
        {
            var alive = new System.Collections.Generic.List<ulong>();
            var any = new System.Collections.Generic.List<ulong>();
            foreach (var e in mn.Roster)
            {
                if (e.clientId == except) continue;
                any.Add(e.clientId);
                var t = FindPlayer(e.clientId);
                var h = t != null ? t.GetComponent<Health>() : null;
                if (h != null && h.IsSpawned && !h.isDead.Value) alive.Add(e.clientId);
            }
            var pool = alive.Count > 0 ? alive : any;
            return pool.Count > 0 ? pool[rng.Next(pool.Count)] : NONE;
        }

        // ───────────────────────── role rules (any peer) ─────────────────────────

        // The hunters only fight the juggernaut (before one is picked, nobody can be hurt).
        protected override bool NoDamageBetween(ulong attacker, ulong victim) =>
            jugg == NONE || (attacker != jugg && victim != jugg);

        // The juggernaut loses a fixed share of health per hit, whatever hit them.
        protected override int DamageFor(ulong victim, ulong attacker, string weapon, int amount, int victimMax)
        {
            if (victim != jugg || amount <= 0) return amount;
            int per = Mathf.CeilToInt(victimMax / (float)Mathf.Max(1, hits));
            return weapon == "Bang!" ? per * BANG_HITS : per;
        }

        // The RPK is the juggernaut's alone.
        protected override bool AllowsWeapon(ulong id, WeaponData w) =>
            IsJuggernaut(id) ? w.weaponName == WEAPON : w.weaponName != WEAPON;
        protected override bool GrantsWeapon(ulong id, WeaponData w) => IsJuggernaut(id) && w.weaponName == WEAPON;
        protected override float MoveFactor(ulong id) => IsJuggernaut(id) ? SPEED : 1f;
        protected override bool BlocksSprint(ulong id) => IsJuggernaut(id);

        // Bots: hunters head for the juggernaut (they fight whoever they see on the way: only the juggernaut).
        protected override bool BotGoal(ulong botId, out Vector3 pos, out BotTask task)
        {
            pos = default; task = BotTask.None;
            if (jugg == NONE || botId == jugg) return false;
            var t = FindPlayer(jugg);
            if (t == null) return false;
            pos = t.position;
            task = BotTask.GoTo;
            return true;
        }

        // ───────────────────────── network state ─────────────────────────
        protected override void WriteState(ref FastBufferWriter w)
        {
            w.WriteValueSafe(jugg);
            w.WriteValueSafe(hits);
        }

        protected override void ReadState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out jugg);
            r.ReadValueSafe(out hits);
        }

        // ───────────────────────── scoreboard / results text ─────────────────────────
        protected override string NameTag(ulong id) =>
            id == jugg ? $"  <size=70%><color={Html(Gold)}>JUGGERNAUT</color></size>" : "";

        protected override string ResultText(int winnerTeam, bool onTime, bool draw) =>
            onTime ? (draw ? "time's up · tied for most kills" : "time's up · most kills")
                   : $"first to {MatchNet.JUGG_KILL_TARGET} kills";

        // ───────────────────────── HUD ─────────────────────────
        TextMeshProUGUI roleText;
        GameObject roleChip;
        Image roleSwatch;
        ObjectiveHUD.Marker marker;

        public override void BuildHud(RectTransform widget, RectTransform markers)
        {
            var chip = HRow(widget, "JuggRole", 8, TextAnchor.MiddleCenter);
            chip.padding = new RectOffset(8, 12, 4, 4);
            chip.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            roleSwatch = Box(chip.transform, "Swatch", Gold);
            roleSwatch.LE(12, 12);
            roleText = Label(chip.transform, "", F.BodySemi, 12, Fg, TextAlignmentOptions.MidlineLeft, 0.06f);
            roleChip = chip.gameObject;
            marker = ObjectiveHUD.Marker.Create(markers, "J", Gold);
        }

        public override void UpdateHud(Camera cam, RectTransform markers)
        {
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            ulong me = nm != null ? nm.LocalClientId : 0;
            Health jh = null;
            var jt = jugg != NONE ? FindPlayer(jugg) : null;
            if (jt != null) jh = jt.GetComponent<Health>();
            int left = jh != null ? HitsLeft(jh.currentHealth.Value, Mathf.Max(1, jh.maxHealth), hits) : hits;

            roleChip.SetActive(jugg != NONE);
            if (jugg == me)
            {
                roleSwatch.color = Gold;
                roleText.text = $"YOU ARE THE JUGGERNAUT · RPK, no sprint · {left} {(left == 1 ? "hit" : "hits")} left";
            }
            else if (jugg != NONE)
            {
                roleSwatch.color = Gold;
                string name = mn != null ? mn.NameFor(jugg) : "?";
                roleText.text = $"HUNT <color={Html(Gold)}>{name}</color> · {left} / {hits} hits · take them down to become the juggernaut";
            }

            bool show = jugg != NONE && jugg != me && jh != null && !jh.isDead.Value;
            if (show)
            {
                marker.SetColor(Gold);
                marker.SetPulse(left <= 2);
                marker.Show(cam, markers, jt.position + Vector3.up * 2.2f, $"JUGGERNAUT · {left}", true);
            }
            else marker.Show(cam, markers, Vector3.zero, "", false);
        }
    }
}
