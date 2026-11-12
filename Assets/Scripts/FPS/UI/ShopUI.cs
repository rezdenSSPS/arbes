using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Armory shop on the main menu: spend Credits to buy weapons (ShopCatalog). Built from the UI kit
    // the same way as ProfileUI — a button on the menu opens a full-screen panel. Every replica has a
    // card: the model turning in 3D (ShopShowcase), its name, what it is, damage / rate / magazine and
    // the price (or STARTER / OWNED / the rank it needs). Buying updates PlayerProfile.owned;
    // weapon-ownership gating in WeaponController then makes them usable.
    public static class ShopUI
    {
        public static void AddToMenu(RectTransform screen, RectTransform stage)
        {
            var panel = BuildPanel(screen);
            var btn = Button(stage, "ARMORY", Btn.Secondary, 16, panel.Open, F.CondBold);
            btn.GetComponent<RectTransform>().At(1f, 1f, -48, -124, 292, 44);
        }

        class Row
        {
            public string id; public int cost, rank; public bool starter;
            public TextMeshProUGUI price, status; public KitButton buy; public RawImage view;
        }

        const float PANEL_W = 1180f, PANEL_H = 672f;
        const float CARD_W = 214f, CARD_H = 222f, GAP = 12f;

        static ShopPanel BuildPanel(RectTransform screen)
        {
            var root = Rect(screen, "ShopPanel").Fill();
            var backdrop = Box(root, "Backdrop", Scrim(0.86f), true);
            backdrop.rectTransform.Fill();
            var panel = root.gameObject.AddComponent<ShopPanel>();
            backdrop.gameObject.AddComponent<UIClick>().onClick = panel.Close;

            var card = Card(root, "Card", Surface, Line, 1f);
            card.rectTransform.At(0.5f, 0.5f, 0, 0, PANEL_W, PANEL_H);
            var col = VCol(card.transform, "Col", 12);
            col.padding = new RectOffset(32, 32, 24, 20);
            col.GetComponent<RectTransform>().Fill();

            // header
            var head = Rect(col.transform, "Header").LE(-1, 58);
            Caption(head, "ARMORY  ·  CREDITS ARE EARNED EVERY MATCH", 12, Muted, 0.16f).rectTransform.TL(0, 0, 600, 14);
            Label(head, "BUY WEAPONS", F.CondBlack, 38, Fg).rectTransform.TL(0, 16, 460, 42);
            var close = KeyButton(head, "CLOSE", "ESC", Btn.Quiet, 16, panel.Close);
            float closeW = close.Rt().sizeDelta.x;
            close.Rt().At(1f, 0.5f, 0, 0, closeW, 40);
            panel.credits = Label(head, "", F.MonoBold, 22, Accent, TextAlignmentOptions.MidlineRight);
            panel.credits.rectTransform.At(1f, 0.5f, -(closeW + 20), 0, 220, 30);

            // every replica: a spinning model, name, what it is, a few numbers, price / buy
            var gridRt = Rect(col.transform, "Weapons");
            gridRt.LE(-1, CARD_H * 2 + GAP);
            var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CARD_W, CARD_H);
            grid.spacing = new Vector2(GAP, GAP);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = ShopShowcase.Cols;
            grid.childAlignment = TextAnchor.UpperCenter;

            var all = WeaponData.GetAllWeapons();
            for (int i = 0; i < all.Length && i < ShopShowcase.Cols * ShopShowcase.Rows; i++)
                panel.rows.Add(BuildCard(gridRt, all[i], i, panel));

            // BB weight: four cells; click an owned one to load it, an unowned one to buy (and load) it
            Caption(col.transform, "BB WEIGHT  ·  HEAVIER = SLOWER, BUT STEADIER IN THE WIND", 12, Muted, 0.16f).LE(-1, 14);
            var bbRow = HRow(col.transform, "BBs", 8);
            bbRow.LE(-1, 56);
            bbRow.childControlWidth = bbRow.childControlHeight = true;
            bbRow.childForceExpandWidth = true;
            for (int i = 0; i < ShopCatalog.Bbs.Count; i++)
            {
                int idx = i;
                var it = ShopCatalog.Bbs[i];
                var b = Button(bbRow.transform, "", Btn.Quiet, 15, () =>
                {
                    if (!ShopCatalog.OwnedBb(idx) && PlayerProfile.Level >= it.rank) PlayerProfile.Buy(it.id, it.cost);
                    if (ShopCatalog.OwnedBb(idx)) PlayerProfile.SetBbWeight(idx);
                    panel.Refresh();
                }, F.CondBold);
                b.LE(-1, 56, 1f);
                b.label.richText = true;
                b.label.lineSpacing = -10f;
                panel.bbButtons.Add(b);
            }


            root.gameObject.SetActive(false);
            root.SetAsLastSibling();
            return panel;
        }

        static Row BuildCard(RectTransform grid, WeaponData w, int index, ShopPanel panel)
        {
            var row = new Row { id = w.weaponName, starter = ShopCatalog.IsStarter(w.weaponName), rank = 1 };
            string label = w.weaponName;
            if (ShopCatalog.TryGet(w.weaponName, out var item)) { row.cost = item.cost; row.rank = item.rank; label = item.label; }

            var c = Card(grid, "W_" + w.weaponName, Raised, Line, 1f);
            var ct = c.transform;

            // the replica, turning slowly on a soft spot
            var spot = Box(ct, "Spot", A(Ground, 0.55f));
            spot.rectTransform.TL(1, 1, CARD_W - 2, 112);
            row.view = Rect(ct, "View").TL(4, 4, CARD_W - 8, 108).gameObject.AddComponent<RawImage>();
            row.view.texture = null;   // set on open (the showcase is built on the first open)
            row.view.uvRect = ShopShowcase.CellUV(index);
            row.view.raycastTarget = false;

            Label(ct, label.ToUpperInvariant(), F.CondBold, 19, Fg).rectTransform.TL(12, 116, CARD_W - 24, 24);
            var desc = Label(ct, ShopCatalog.Describe(w.weaponName), F.Body, 11.5f, Muted, TextAlignmentOptions.TopLeft).Wrap(-8f);
            desc.rectTransform.TL(12, 141, CARD_W - 22, 32);
            string dmg = w.pelletsPerShot > 1 ? w.damage + "×" + w.pelletsPerShot : w.damage.ToString();
            var stats = Label(ct, $"DMG {dmg}  ·  {w.fireRate:0.#}/S  ·  {w.magSize} RDS", F.MonoBold, 10, Faint);
            stats.rectTransform.TL(12, 174, CARD_W - 24, 14);

            Box(ct, "Sep", Line).rectTransform.TL(0, 190, CARD_W, 1);
            row.price = Label(ct, "", F.MonoBold, 15, Accent);
            row.price.rectTransform.TL(12, 191, 110, 31);
            row.status = Label(ct, "", F.BodyBold, 11, Ready, TextAlignmentOptions.MidlineRight, 0.12f);
            row.status.rectTransform.At(1f, 0f, -12, 0, 110, 31);
            var r = row;
            row.buy = Button(ct, "BUY", Btn.Primary, 14, () =>
            {
                if (PlayerProfile.Level >= r.rank) PlayerProfile.Buy(r.id, r.cost);
                panel.Refresh();
            }, F.CondBold);
            row.buy.GetComponent<RectTransform>().At(1f, 0f, -6, 4, 88, 24);
            return row;
        }

        class ShopPanel : MonoBehaviour
        {
            public TextMeshProUGUI credits;
            public readonly List<Row> rows = new List<Row>();
            public readonly List<KitButton> bbButtons = new List<KitButton>();

            public void Open() { Refresh(); gameObject.SetActive(true); transform.SetAsLastSibling(); }
            public void Close() => gameObject.SetActive(false);

            void OnEnable() => ShopShowcase.Show(true);
            void OnDisable() => ShopShowcase.Show(false);

            void Update()
            {
                ShopShowcase.Spin(Time.unscaledDeltaTime);
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            }

            public void Refresh()
            {
                credits.text = "◆ " + ProfileUI.Num(PlayerProfile.Credits) + " CR";
                int level = PlayerProfile.Level;
                int cr = PlayerProfile.Credits;
                foreach (var row in rows)
                {
                    bool owned = row.starter || PlayerProfile.Owns(row.id);
                    bool rankOk = level >= row.rank;
                    bool affordable = cr >= row.cost;
                    row.price.text = row.starter ? "FREE" : "◆ " + ProfileUI.Num(row.cost);
                    row.price.color = owned ? Faint : Accent;
                    row.buy.gameObject.SetActive(!owned && rankOk);
                    row.status.gameObject.SetActive(owned || !rankOk);
                    if (owned) { row.status.text = row.starter ? "STARTER" : "OWNED"; row.status.color = Ready; }
                    else if (!rankOk) { row.status.text = "RANK " + row.rank; row.status.color = DangerSoft; }
                    if (!owned && rankOk)
                    {
                        row.buy.label.text = affordable ? "BUY" : "NEED CR";
                        var img = row.buy.GetComponent<Image>();
                        if (img != null) img.color = affordable ? Accent : Scrim(0.5f);
                    }
                    row.view.texture = ShopShowcase.Texture;
                    row.view.color = rankOk || owned ? Color.white : new Color(1f, 1f, 1f, 0.45f);   // locked: faded
                }

                int loaded = PlayerProfile.BbIndex;
                for (int i = 0; i < bbButtons.Count && i < ShopCatalog.Bbs.Count; i++)
                {
                    var it = ShopCatalog.Bbs[i];
                    var b = bbButtons[i];
                    bool owned = ShopCatalog.OwnedBb(i);
                    string sub = i == loaded ? "LOADED"
                        : owned ? (it.cost == 0 ? "FREE · LOAD" : "OWNED · LOAD")
                        : level < it.rank ? "RANK " + it.rank
                        : ProfileUI.Num(it.cost) + " CR" + (cr >= it.cost ? "" : " · NEED CR");   // (the condensed font has no ◆)
                    b.label.text = $"{it.label}\n<size=60%>{sub}</size>";
                    b.SetStyle(i == loaded ? Btn.Primary : Btn.Quiet);
                    b.interactable = owned || (level >= it.rank && cr >= it.cost);
                    b.Refresh();
                }
            }
        }
    }
}
