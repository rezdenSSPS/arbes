using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Armory shop on the main menu: spend Credits to buy weapons (ShopCatalog). Built from the UI kit
    // the same way as ProfileUI — a button on the menu opens a full-screen panel. Buying updates
    // PlayerProfile.owned; weapon-ownership gating in WeaponController then makes them usable.
    public static class ShopUI
    {
        public static void AddToMenu(RectTransform screen, RectTransform stage)
        {
            var panel = BuildPanel(screen);
            var btn = Button(stage, "ARMORY", Btn.Secondary, 16, panel.Open, F.CondBold);
            btn.GetComponent<RectTransform>().At(1f, 1f, -48, -124, 292, 44);
        }

        class Row { public ShopCatalog.Item item; public TextMeshProUGUI status; public KitButton buy; }

        static ShopPanel BuildPanel(RectTransform screen)
        {
            var root = Rect(screen, "ShopPanel").Fill();
            var backdrop = Box(root, "Backdrop", Scrim(0.86f), true);
            backdrop.rectTransform.Fill();
            var panel = root.gameObject.AddComponent<ShopPanel>();
            backdrop.gameObject.AddComponent<UIClick>().onClick = panel.Close;

            var card = Card(root, "Card", Surface, Line, 1f);
            card.rectTransform.At(0.5f, 0.5f, 0, 0, 820, 680);
            var col = VCol(card.transform, "Col", 14);
            col.padding = new RectOffset(40, 40, 32, 28);
            col.GetComponent<RectTransform>().Fill();

            // header
            var head = Rect(col.transform, "Header").LE(-1, 58);
            Caption(head, "ARMORY", 12, Muted, 0.16f).rectTransform.TL(0, 0, 300, 14);
            Label(head, "BUY WEAPONS", F.CondBlack, 38, Fg).rectTransform.TL(0, 16, 460, 42);
            var close = KeyButton(head, "CLOSE", "ESC", Btn.Quiet, 16, panel.Close);
            float closeW = close.Rt().sizeDelta.x;
            close.Rt().At(1f, 0.5f, 0, 0, closeW, 40);
            panel.credits = Label(head, "", F.MonoBold, 22, Accent, TextAlignmentOptions.MidlineRight);
            panel.credits.rectTransform.At(1f, 0.5f, -(closeW + 20), 0, 220, 30);

            Caption(col.transform, "WEAPONS", 12, Muted, 0.16f).LE(-1, 14);
            var list = VCol(col.transform, "List", 8);

            foreach (var item in ShopCatalog.Weapons)
            {
                var it = item;
                var r = HRow(list.transform, it.id, 12, TextAnchor.MiddleLeft);
                r.LE(-1, 50);
                r.padding = new RectOffset(16, 16, 4, 4);
                r.gameObject.AddComponent<Image>().color = Raised;

                var info = VCol(r.transform, "Info", 2, TextAnchor.MiddleLeft);
                info.LE(0, 42, 1f);   // grows
                Label(info.transform, it.label, F.CondBold, 19, Fg).LE(-1, 21);
                Label(info.transform, it.note, F.Body, 12, Muted).LE(-1, 15);

                var cost = Label(r.transform, "", F.MonoBold, 16, Accent, TextAlignmentOptions.MidlineRight);
                cost.LE(120, 24);

                var status = Label(r.transform, "", F.BodyBold, 12, Ready, TextAlignmentOptions.MidlineRight, 0.12f);
                status.LE(90, 24);

                var buy = Button(r.transform, "BUY", Btn.Primary, 15, () =>
                {
                    if (PlayerProfile.Level >= it.rank) PlayerProfile.Buy(it.id, it.cost);
                    panel.Refresh();
                }, F.CondBold);
                buy.GetComponent<RectTransform>().LE(96, 40);

                cost.text = "◆ " + ProfileUI.Num(it.cost);
                panel.rows.Add(new Row { item = it, status = status, buy = buy });
            }

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

            Spacer(col.transform);
            Label(col.transform, "Credits are earned every match. Buying a weapon unlocks it for your loadout; higher-tier weapons also need a rank. Everything is saved on this computer.",
                F.Body, 12, Faint).Wrap().LE(-1, 32);

            root.gameObject.SetActive(false);
            root.SetAsLastSibling();
            return panel;
        }

        class ShopPanel : MonoBehaviour
        {
            public TextMeshProUGUI credits;
            public readonly List<Row> rows = new List<Row>();
            public readonly List<KitButton> bbButtons = new List<KitButton>();

            public void Open() { Refresh(); gameObject.SetActive(true); transform.SetAsLastSibling(); }
            public void Close() => gameObject.SetActive(false);

            void Update()
            {
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
                    bool owned = PlayerProfile.Owns(row.item.id);
                    bool rankOk = level >= row.item.rank;
                    bool affordable = cr >= row.item.cost;
                    row.buy.gameObject.SetActive(!owned && rankOk);
                    row.status.gameObject.SetActive(owned || !rankOk);
                    if (owned) { row.status.text = "OWNED"; row.status.color = Ready; }
                    else if (!rankOk) { row.status.text = "RANK " + row.item.rank; row.status.color = DangerSoft; }
                    if (!owned && rankOk)
                    {
                        row.buy.label.text = affordable ? "BUY" : "NEED CR";
                        var img = row.buy.GetComponent<Image>();
                        if (img != null) img.color = affordable ? Accent : Scrim(0.5f);
                    }
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
                        : "◆ " + ProfileUI.Num(it.cost) + (cr >= it.cost ? "" : " · NEED CR");
                    b.label.text = $"{it.label}\n<size=60%>{sub}</size>";
                    b.SetStyle(i == loaded ? Btn.Primary : Btn.Quiet);
                    b.interactable = owned || (level >= it.rank && cr >= it.cost);
                    b.Refresh();
                }
            }
        }
    }
}
