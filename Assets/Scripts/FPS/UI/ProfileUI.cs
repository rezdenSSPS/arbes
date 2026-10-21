using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Profile UI built from the UI kit: the level chip + stats panel on the main menu and the
    // XP card on the results screen. NetworkUI / HUDManager call one builder each.
    public static class ProfileUI
    {
        public static string Callsign
        {
            get
            {
                string n = MatchNet.Instance != null ? MatchNet.Instance.LocalName : "";
                return string.IsNullOrWhiteSpace(n) ? "NO CALLSIGN" : n;
            }
        }

        public static string Num(int v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

        // Small padlock (shackle + body), centred in a size x size box.
        public static RectTransform LockIcon(Transform parent, Color c, float size = 14f)
        {
            var rt = Rect(parent, "Lock");
            rt.sizeDelta = new Vector2(size, size);
            var shackle = Rect(rt, "Shackle").At(0.5f, 1f, 0, 0, size * 0.56f, size * 0.62f);
            Frame(shackle, c, Mathf.Max(1.5f, size * 0.13f));
            Box(rt, "Body", c).rectTransform.At(0.5f, 0f, 0, 0, size * 0.84f, size * 0.55f);
            return rt;
        }

        // ───────────── main menu ─────────────
        // Adds the profile chip (top right of the menu stage) and the stats panel (over the whole screen).
        public static void AddToMenu(RectTransform screen, RectTransform stage)
        {
            var panel = BuildPanel(screen);
            var chip = Card(stage, "ProfileChip", Scrim(0.86f), Line, 1f);
            chip.rectTransform.At(1f, 1f, -48, -40, 292, 72);
            var view = chip.gameObject.AddComponent<ProfileChipView>();
            chip.gameObject.AddComponent<UIClick>().onClick = panel.Open;

            var lvl = Box(chip.transform, "Level", Accent);
            Chamfer(lvl, 10);
            lvl.rectTransform.TL(12, 12, 48, 48);
            Label(lvl.transform, "LVL", F.BodyBold, 9, Ground, TextAlignmentOptions.Top, 0.14f).rectTransform.Fill(0, 5, 0, 0);
            view.level = Label(lvl.transform, "1", F.CondBlack, 26, Ground, TextAlignmentOptions.Bottom);
            view.level.rectTransform.Fill(0, 0, 0, 3);

            view.callsign = Label(chip.transform, "", F.CondBold, 20, Fg);
            view.callsign.overflowMode = TextOverflowModes.Ellipsis;
            view.callsign.rectTransform.TL(72, 10, 150, 26);
            var open = Label(chip.transform, "PROFILE", F.BodyBold, 10, Muted, TextAlignmentOptions.MidlineRight, 0.14f);
            open.rectTransform.TL(200, 12, 80, 20);
            view.fill = Bar(chip.transform, "Xp", Line, Accent);
            view.fill.transform.parent.GetComponent<RectTransform>().TL(72, 40, 208, 4);
            view.xp = Label(chip.transform, "", F.Mono, 11, Muted);
            view.xp.rectTransform.TL(72, 47, 208, 16);
        }

        class ProfileChipView : MonoBehaviour
        {
            public TextMeshProUGUI level, callsign, xp;
            public Image fill;
            float t;

            void Update()
            {
                if ((t -= Time.unscaledDeltaTime) > 0f) return;
                t = 0.25f;
                int total = PlayerProfile.Data.totalXp;
                var (into, need) = PlayerProfile.Progress(total);
                level.text = PlayerProfile.Level.ToString();
                callsign.text = Callsign.ToUpperInvariant();
                xp.text = $"{Num(into)} / {Num(need)} XP";
                SetBar(fill, into / (float)need);
            }
        }

        // ───────────── stats panel ─────────────
        static ProfilePanel BuildPanel(RectTransform screen)
        {
            var root = Rect(screen, "ProfilePanel").Fill();
            var backdrop = Box(root, "Backdrop", Scrim(0.86f), true);
            backdrop.rectTransform.Fill();
            var panel = root.gameObject.AddComponent<ProfilePanel>();
            backdrop.gameObject.AddComponent<UIClick>().onClick = panel.Close;

            var card = Card(root, "Card", Surface, Line, 1f);
            card.rectTransform.At(0.5f, 0.5f, 0, 0, 780, 604);
            var col = VCol(card.transform, "Col", 16);
            col.padding = new RectOffset(40, 40, 32, 28);
            col.GetComponent<RectTransform>().Fill();

            // header: callsign + close
            var head = Rect(col.transform, "Header").LE(-1, 58);
            Caption(head, "PROFILE", 12, Muted, 0.16f).rectTransform.TL(0, 0, 300, 14);
            panel.callsign = Label(head, "", F.CondBlack, 40, Fg);
            panel.callsign.rectTransform.TL(0, 16, 520, 42);
            var close = Button(head, "CLOSE", Btn.Quiet, 16, panel.Close);
            close.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 120, 40);
            close.label.rectTransform.Fill(14, 0, 46, 0);
            var esc = KeyCap(close.transform, "ESC", 10);
            esc.anchorMin = esc.anchorMax = esc.pivot = new Vector2(1f, 0.5f);
            esc.anchoredPosition = new Vector2(-12, 0);

            // level + XP bar
            var lvlRow = HRow(col.transform, "LevelRow", 20, TextAnchor.MiddleLeft);
            lvlRow.LE(-1, 76);
            var lvl = Box(lvlRow.transform, "Level", Accent);
            Chamfer(lvl, 14);
            lvl.LE(76, 76);
            Label(lvl.transform, "LEVEL", F.BodyBold, 10, Ground, TextAlignmentOptions.Top, 0.16f).rectTransform.Fill(0, 9, 0, 0);
            panel.level = Label(lvl.transform, "1", F.CondBlack, 40, Ground, TextAlignmentOptions.Bottom);
            panel.level.rectTransform.Fill(0, 0, 0, 6);
            var xpCol = VCol(lvlRow.transform, "Xp", 8, TextAnchor.MiddleLeft);
            xpCol.LE(-1, 76, 1);
            var xpTop = Rect(xpCol.transform, "Top").LE(-1, 22);
            panel.xpInto = Label(xpTop, "", F.CondBold, 20, Fg);
            panel.xpInto.rectTransform.Fill();
            panel.xpTotal = Label(xpTop, "", F.Mono, 12, Muted, TextAlignmentOptions.MidlineRight);
            panel.xpTotal.rectTransform.Fill();
            panel.fill = Bar(xpCol.transform, "Bar", Line, Accent);
            panel.fill.transform.parent.GetComponent<Image>().LE(-1, 8);
            panel.xpNext = Label(xpCol.transform, "", F.Body, 13, Muted);
            panel.xpNext.LE(-1, 16);

            // stats grid: 3 rows x 4
            Caption(col.transform, "LIFETIME STATS", 12, Muted, 0.16f).LE(-1, 14);
            var grid = VCol(col.transform, "Grid", 1);
            grid.padding = new RectOffset(1, 1, 1, 1);
            grid.gameObject.AddComponent<Image>().color = Line;
            string[][] keys =
            {
                new[] { "MATCHES", "WINS", "WIN RATE", "BEST STREAK" },
                new[] { "KILLS", "DEATHS", "K/D", "TOTAL XP" },
                new[] { "HITS DEALT", "HITS TAKEN", "FAVORITE WEAPON" },
            };
            foreach (var row in keys)
            {
                var r = HRow(grid.transform, "Row", 1, TextAnchor.MiddleLeft, true, true);
                r.LE(-1, 64);
                foreach (var k in row)
                {
                    var cell = VCol(r.transform, k, 4, TextAnchor.MiddleLeft);
                    cell.padding = new RectOffset(16, 16, 8, 8);
                    cell.gameObject.AddComponent<Image>().color = Raised;
                    cell.LE(0, 64, k == "FAVORITE WEAPON" ? 2f : 1f);   // preferred 0: columns split evenly
                    var v = Label(cell.transform, "0", F.CondBlack, 28, Fg);
                    v.LE(-1, 30);
                    Label(cell.transform, k, F.BodyBold, 10, Muted, TextAlignmentOptions.MidlineLeft, 0.14f).LE(-1, 13);
                    panel.stats.Add(v);
                }
            }

            // next unlocks
            Caption(col.transform, "NEXT UNLOCKS", 12, Muted, 0.16f).LE(-1, 14);
            panel.unlockRow = HRow(col.transform, "Unlocks", 8, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            panel.unlockRow.LE(-1, 30);
            Spacer(col.transform);
            Label(col.transform, "Stats and XP are saved on this computer. Kills, hits, finished matches and wins earn XP; levels unlock hats, eyewear and colours in the character editor.",
                F.Body, 12, Faint).Wrap().LE(-1, 30);

            root.gameObject.SetActive(false);
            root.SetAsLastSibling();
            return panel;
        }

        class ProfilePanel : MonoBehaviour
        {
            public TextMeshProUGUI callsign, level, xpInto, xpTotal, xpNext;
            public Image fill;
            public RectTransform unlockRow;
            public readonly List<TextMeshProUGUI> stats = new List<TextMeshProUGUI>();

            public void Open()
            {
                Refresh();
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
            }

            public void Close() => gameObject.SetActive(false);

            void Update()
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            }

            void Refresh()
            {
                var d = PlayerProfile.Data;
                int lv = PlayerProfile.Level;
                var (into, need) = PlayerProfile.Progress(d.totalXp);
                callsign.text = Callsign.ToUpperInvariant();
                level.text = lv.ToString();
                xpInto.text = $"{Num(into)} / {Num(need)} XP";
                xpTotal.text = Num(d.totalXp) + " XP total";
                xpNext.text = $"{Num(need - into)} XP to level {lv + 1}";
                SetBar(fill, into / (float)need);

                string fav = PlayerProfile.FavoriteWeapon;
                string[] v =
                {
                    Num(d.matchesPlayed), Num(d.wins), d.matchesPlayed > 0 ? Mathf.RoundToInt(100f * d.wins / d.matchesPlayed) + "%" : "-",
                    Num(d.bestStreak),
                    Num(d.kills), Num(d.deaths), (d.deaths == 0 ? d.kills : d.kills / (float)d.deaths).ToString("0.00"),
                    Num(d.totalXp),
                    Num(d.hitsDealt), Num(d.hitsTaken), string.IsNullOrEmpty(fav) ? "-" : fav.ToUpperInvariant(),
                };
                for (int i = 0; i < stats.Count && i < v.Length; i++) stats[i].text = v[i];

                ClearChildren(unlockRow);
                int shown = 0;
                for (int l = lv + 1; l <= 30 && shown < 5; l++)
                    foreach (var item in AirsoftLook.UnlocksAt(l))
                    {
                        if (shown++ >= 5) break;
                        var chip = Card(unlockRow, "Unlock", Raised, Line, 1f);
                        var h = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                        h.padding = new RectOffset(10, 12, 0, 0); h.spacing = 8;
                        h.childAlignment = TextAnchor.MiddleLeft;
                        h.childControlWidth = h.childControlHeight = true;
                        h.childForceExpandWidth = h.childForceExpandHeight = false;
                        chip.LE(-1, 30);
                        chip.transform.Find("Frame").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                        LockIcon(chip.transform, Faint, 12).LE(12, 12);
                        Label(chip.transform, "LVL " + l, F.MonoBold, 11, Accent).LE(-1, 30);
                        Label(chip.transform, item, F.BodySemi, 13, TextSoft).LE(-1, 30);
                    }
                if (shown == 0) Label(unlockRow, "Everything is unlocked.", F.Body, 13, Muted).LE(-1, 30);
            }
        }

        // ───────────── results screen ─────────────
        // XP card for the "YOUR MATCH" column: XP gained, breakdown, level bar and a level-up notice.
        public static void AddToResults(Transform column)
        {
            var card = Card(column, "ProfileXp", Surface, Line, 1f);
            card.LE(-1, 164);
            var view = card.gameObject.AddComponent<ResultsXpView>();
            var col = VCol(card.transform, "Col", 6);
            col.padding = new RectOffset(14, 14, 12, 12);
            col.GetComponent<RectTransform>().Fill();

            var top = Rect(col.transform, "Top").LE(-1, 30);
            Caption(top, "XP EARNED", 12, Muted, 0.16f).rectTransform.Fill();
            view.gained = Label(top, "", F.CondBlack, 30, Accent, TextAlignmentOptions.MidlineRight);
            view.gained.rectTransform.Fill();

            var lvl = Rect(col.transform, "Level").LE(-1, 18);
            view.level = Label(lvl, "", F.CondBold, 17, Fg);
            view.level.rectTransform.Fill();
            view.progress = Label(lvl, "", F.Mono, 11, Muted, TextAlignmentOptions.MidlineRight);
            view.progress.rectTransform.Fill();

            view.fill = Bar(col.transform, "Bar", Line, Accent);
            view.fill.transform.parent.GetComponent<Image>().LE(-1, 6);

            view.breakdown = Label(col.transform, "", F.Body, 12, Muted);
            view.breakdown.overflowMode = TextOverflowModes.Ellipsis;
            view.breakdown.LE(-1, 16);

            view.levelUp = Box(col.transform, "LevelUp", Accent);
            Chamfer(view.levelUp, 8);
            view.levelUp.LE(-1, 40);
            view.levelUpText = Label(view.levelUp.transform, "", F.BodyBold, 12, Ground, TextAlignmentOptions.MidlineLeft, 0.04f).Wrap();
            view.levelUpText.overflowMode = TextOverflowModes.Truncate;
            view.levelUpText.rectTransform.Fill(10, 2, 10, 2);
        }

        class ResultsXpView : MonoBehaviour
        {
            const float ANIM = 1.8f;
            public TextMeshProUGUI gained, level, progress, breakdown, levelUpText;
            public Image fill, levelUp;
            int shownSerial = -1;
            float animStart;
            float shownAt;

            void OnEnable() { shownSerial = -1; shownAt = Time.unscaledTime; }

            void Update()
            {
                var m = PlayerProfile.LastMatch;
                // The tracker commits the match in the same frame the results appear (or the next one);
                // anything committed before this screen opened belongs to an older match.
                bool fresh = m != null && m.time >= shownAt - 1f;
                if (!fresh)
                {
                    gained.text = "";
                    level.text = "LEVEL " + PlayerProfile.Level;
                    var (i0, n0) = PlayerProfile.Progress(PlayerProfile.Data.totalXp);
                    progress.text = $"{Num(i0)} / {Num(n0)} XP";
                    SetBar(fill, i0 / (float)n0);
                    breakdown.text = "";
                    levelUp.gameObject.SetActive(false);
                    return;
                }
                if (m.serial != shownSerial) { shownSerial = m.serial; animStart = Time.unscaledTime + 0.3f; }

                float k = Mathf.Clamp01((Time.unscaledTime - animStart) / ANIM);
                k = 1f - (1f - k) * (1f - k) * (1f - k);   // ease out
                int xp = Mathf.RoundToInt(Mathf.Lerp(m.xpBefore, m.xpAfter, k));
                int lv = PlayerProfile.LevelFor(xp);
                var (into, need) = PlayerProfile.Progress(xp);
                gained.text = "+" + Num(Mathf.RoundToInt(m.Gained * k)) + " XP";
                level.text = "LEVEL " + lv;
                progress.text = $"{Num(into)} / {Num(need)} XP";
                SetBar(fill, into / (float)need);

                var parts = new List<string>();
                foreach (var (what, x) in m.lines) parts.Add($"{what} <color={Html(Fg)}>+{x}</color>");
                breakdown.text = parts.Count > 0 ? string.Join("  ·  ", parts) : "No XP this match";

                bool up = lv > m.LevelBefore;
                levelUp.gameObject.SetActive(up);
                if (up)
                {
                    string unl = m.unlocked.Count > 0 ? "\n<size=11>New: " + string.Join(", ", m.unlocked) + "</size>" : "";
                    levelUpText.text = $"LEVEL UP!  NOW LEVEL {m.LevelAfter}{unl}";
                    float pulse = 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime - animStart) * 6f);
                    levelUp.color = Color.Lerp(Accent, AccentHi, pulse);
                }
            }
        }
    }
}
