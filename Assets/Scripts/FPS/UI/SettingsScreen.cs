using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Full-screen settings overlay (Controls / Video / Audio / Gameplay).
    // Used from the main menu and from the in-match pause screen.
    public class SettingsScreen : MonoBehaviour
    {
        System.Action onClose;
        readonly List<GameObject> panes = new List<GameObject>();
        readonly List<KitButton> tabs = new List<KitButton>();
        readonly List<Image> tabLines = new List<Image>();
        readonly List<System.Action> refreshers = new List<System.Action>();

        public bool IsOpen => gameObject.activeSelf;

        // Frame the screen was closed with Esc, so the pause menu ignores that same key press.
        public static int LastEscCloseFrame = -1;

        public static SettingsScreen Create(Transform canvas, System.Action onClose)
        {
            var root = Rect(canvas, "SettingsScreen").Fill();
            var s = root.gameObject.AddComponent<SettingsScreen>();
            s.onClose = onClose;
            s.Build(root);
            root.gameObject.SetActive(false);
            return s;
        }

        public void Open()
        {
            GameSettings.EnsureLoaded();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            foreach (var r in refreshers) r();
            SelectTab(0);
        }

        public void Close()
        {
            GameSettings.Save();
            gameObject.SetActive(false);
            onClose?.Invoke();
        }

        void Build(RectTransform root)
        {
            var bg = Box(root, "BG", Ground, true);
            bg.rectTransform.Fill();
            var stage = Rect(root, "Stage").At(0.5f, 0.5f, 0, 0, 1280, 720);

            Label(stage, "SETTINGS", F.CondBlack, 44, Fg, TextAlignmentOptions.MidlineLeft, 0.02f).rectTransform.TL(64, 48, 400, 44);

            var actions = HRow(stage, "Actions", 12, TextAnchor.MiddleRight);
            actions.GetComponent<RectTransform>().At(1f, 1f, -64, -48, 600, 44);
            Button(actions.transform, "RESET TO DEFAULT", Btn.Ghost, 17, () =>
            {
                GameSettings.ResetToDefaults();
                AirsoftAudio.ApplyVolumes();
                foreach (var r in refreshers) r();
            }, F.CondSemi).LE(190, 44);
            Button(actions.transform, "SAVE & BACK", Btn.Primary, 17, Close, F.CondBold, 10).LE(150, 44);

            // tabs
            var tabRow = Rect(stage, "Tabs").TL(64, 120, 1152, 48);
            Box(tabRow, "Line", Line).rectTransform.At(0f, 0f, 0, 0, 1152, 1);
            string[] names = { "CONTROLS", "VIDEO", "AUDIO", "GAMEPLAY" };
            float x = 0;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = Button(tabRow, names[i], Btn.Tab, 18, () => SelectTab(idx));
                b.label.characterSpacing = 10;
                float w = b.label.preferredWidth + 44;
                b.GetComponent<RectTransform>().TL(x, 0, w, 48);
                var line = Box(b.transform, "Underline", Accent);
                line.rectTransform.At(0.5f, 0f, 0, 0, w, 3);
                tabs.Add(b); tabLines.Add(line);
                x += w + 4;
            }

            var content = Rect(stage, "Content").TL(64, 196, 1152, 480);
            panes.Add(BuildControls(content));
            panes.Add(BuildVideo(content));
            panes.Add(BuildAudio(content));
            panes.Add(BuildGameplay(content));
        }

        void SelectTab(int i)
        {
            for (int k = 0; k < panes.Count; k++)
            {
                panes[k].SetActive(k == i);
                tabs[k].SetSelectedLook(k == i);
                tabLines[k].enabled = k == i;
            }
        }

        // ───────────── panes ─────────────
        static RectTransform Column(Transform pane, int index, float width = 552)
        {
            var col = VCol(pane, "Col" + index, 26);
            var rt = col.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(index * 600, 0);
            rt.sizeDelta = new Vector2(width, 480);
            return rt;
        }

        GameObject BuildControls(Transform content)
        {
            var pane = Rect(content, "Controls").Fill();
            var c1 = Column(pane, 0);
            SliderRow(c1, "Mouse sensitivity", 0.1f, 5f, false,
                () => GameSettings.Sensitivity, v => GameSettings.Sensitivity = Mathf.Round(v * 20f) / 20f, v => v.ToString("0.00"));
            SliderRow(c1, "Scoped sensitivity (sniper)", 10, 100, true,
                () => GameSettings.ScopedSensitivity * 100f, v => GameSettings.ScopedSensitivity = v / 100f, v => Mathf.RoundToInt(v) + "%");
            Check(c1, "Invert Y axis", () => GameSettings.InvertY, v => GameSettings.InvertY = v);
            Check(c1, "Toggle crouch (instead of hold)", () => GameSettings.ToggleCrouch, v => GameSettings.ToggleCrouch = v);

            var tableBox = Rect(pane, "Binds").TL(600, 0, 552, Binds.Length * 40);
            Frame(tableBox, Line, 1f);
            for (int i = 0; i < Binds.Length; i++)
            {
                var row = Rect(tableBox, "Bind").TL(0, i * 40, 552, 40);
                if (i < Binds.Length - 1) Box(row, "Sep", Raised).rectTransform.At(0.5f, 0f, 0, 0, 550, 1);
                Label(row, Binds[i].action, F.Body, 15, TextSoft).rectTransform.Fill(16, 0, 200, 0);
                var key = KeyCap(row, Binds[i].key, 12);
                key.anchorMin = key.anchorMax = key.pivot = new Vector2(1f, 0.5f);
                key.anchoredPosition = new Vector2(-16, 0);
            }
            return pane.gameObject;
        }

        GameObject BuildVideo(Transform content)
        {
            var pane = Rect(content, "Video").Fill();
            var c1 = Column(pane, 0);
            SliderRow(c1, "Field of view", 60, 110, true,
                () => GameSettings.FieldOfView, v => GameSettings.FieldOfView = Mathf.RoundToInt(v), v => Mathf.RoundToInt(v) + "°");
            ResolutionRow(c1);
            Check(c1, "Fullscreen", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);
            Check(c1, "V-Sync", () => GameSettings.VSync, v => GameSettings.VSync = v);

            var c2 = Column(pane, 1);
            var q = VCol(c2, "Quality", 10);
            Label(q.transform, "Graphics quality", F.Body, 16, Fg).LE(-1, 22);
            var holder = HRow(q.transform, "SegHolder", 0);
            var seg = Segments(holder.transform, new[] { "LOW", "MEDIUM", "HIGH" }, GameSettings.Quality, 42, 16,
                i => GameSettings.Quality = i);
            refreshers.Add(() => seg.Select(GameSettings.Quality, false));
            Check(c2, "Post-processing (bloom, fog)", () => GameSettings.PostProcessing, v => GameSettings.PostProcessing = v);
            Check(c2, "Show FPS counter", () => GameSettings.ShowFps, v => GameSettings.ShowFps = v);
            return pane.gameObject;
        }

        GameObject BuildAudio(Transform content)
        {
            var pane = Rect(content, "Audio").Fill();
            var c1 = Column(pane, 0, 560);
            // every change goes straight to the mixer, so you hear it while dragging
            SliderRow(c1, "Master volume", 0, 100, true, () => GameSettings.MasterVolume * 100f,
                v => { GameSettings.MasterVolume = v / 100f; AirsoftAudio.ApplyVolumes(); }, v => Mathf.RoundToInt(v) + "%");
            SliderRow(c1, "Weapons & effects", 0, 100, true, () => GameSettings.SfxVolume * 100f,
                v => { GameSettings.SfxVolume = v / 100f; AirsoftAudio.ApplyVolumes(); }, v => Mathf.RoundToInt(v) + "%");
            SliderRow(c1, "Music", 0, 100, true, () => GameSettings.MusicVolume * 100f,
                v => { GameSettings.MusicVolume = v / 100f; AirsoftAudio.ApplyVolumes(); }, v => Mathf.RoundToInt(v) + "%");
            SliderRow(c1, "Interface & announcer", 0, 100, true, () => GameSettings.AnnouncerVolume * 100f,
                v => { GameSettings.AnnouncerVolume = v / 100f; AirsoftAudio.ApplyVolumes(); }, v => Mathf.RoundToInt(v) + "%");
            Check(c1, "In-match ambience (wind, room tone)", () => GameSettings.Ambience, v => GameSettings.Ambience = v);
            return pane.gameObject;
        }

        GameObject BuildGameplay(Transform content)
        {
            var pane = Rect(content, "Gameplay").Fill();
            var c1 = Column(pane, 0);

            var colorBlock = VCol(c1, "CrosshairColor", 12);
            Label(colorBlock.transform, "Crosshair color", F.Body, 16, Fg).LE(-1, 22);
            var swRow = HRow(colorBlock.transform, "Swatches", 10);
            swRow.padding = new RectOffset(5, 5, 5, 5);
            var rings = new List<Image>();
            for (int i = 0; i < GameSettings.CrosshairColors.Length; i++)
            {
                int idx = i;
                var cell = Rect(swRow.transform, "Swatch").LE(44, 44);
                var ring = Frame(cell, Fg, 2f, "Ring");
                ring.rectTransform.Fill(-5, -5, -5, -5);
                rings.Add(ring);
                var b = Button(cell, "", Btn.Ghost, 10, () => { GameSettings.CrosshairColor = idx; RefreshAll(); });
                b.GetComponent<RectTransform>().Fill();
                b.bgN = b.bgH = b.bgP = GameSettings.CrosshairColors[i];
                b.Refresh();
            }
            refreshers.Add(() => { for (int i = 0; i < rings.Count; i++) rings[i].enabled = i == GameSettings.CrosshairColor; });

            SliderRow(c1, "Crosshair size", 6, 24, true, () => GameSettings.CrosshairSize,
                v => { GameSettings.CrosshairSize = Mathf.RoundToInt(v); RefreshAll(); }, v => Mathf.RoundToInt(v) + " px");
            Check(c1, "Show hit marker", () => GameSettings.ShowHitMarker, v => GameSettings.ShowHitMarker = v);
            Check(c1, "Show enemy health bars", () => GameSettings.ShowHealthBars, v => GameSettings.ShowHealthBars = v);
            Check(c1, "Killcam (look at who eliminated you)", () => GameSettings.Killcam, v => GameSettings.Killcam = v);

            // live preview
            var prev = Box(pane, "Preview", Hex("#4F5A62"));
            prev.rectTransform.TL(600, 0, 552, 300);
            Caption(prev.transform, "PREVIEW", 11, Fg).rectTransform.TL(14, 12, 200, 14);
            var cross = Rect(prev.transform, "Crosshair").At(0.5f, 0.5f, 0, 0, 80, 80);
            var arms = CrosshairArms(cross);
            refreshers.Add(() => LayoutCrosshair(arms, GameSettings.CrosshairSize, GameSettings.CrosshairTint));

            // minimap, under the preview
            var mm = VCol(pane, "Minimap", 18);
            mm.GetComponent<RectTransform>().TL(600, 326, 552, 154);
            Check(mm.transform, "Show minimap", () => GameSettings.ShowMinimap, v => GameSettings.ShowMinimap = v);
            var orient = VCol(mm.transform, "Orientation", 10);
            Label(orient.transform, "Minimap orientation", F.Body, 16, Fg).LE(-1, 22);
            var holder = HRow(orient.transform, "SegHolder", 0);
            var seg = Segments(holder.transform, new[] { "ROTATING", "NORTH UP" }, GameSettings.MinimapRotate ? 0 : 1, 40, 15,
                i => GameSettings.MinimapRotate = i == 0);
            refreshers.Add(() => seg.Select(GameSettings.MinimapRotate ? 0 : 1, false));
            return pane.gameObject;
        }

        void RefreshAll() { foreach (var r in refreshers) r(); }

        // ───────────── shared crosshair (also used by the HUD) ─────────────
        public static Image[] CrosshairArms(Transform parent)
        {
            var arms = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var shadow = Box(parent, "Shadow" + i, A(Ground, 0.6f));
                arms[i] = Box(shadow.transform, "Arm" + i, Fg);
                arms[i].rectTransform.Fill(1, 1, 1, 1);
            }
            return arms;
        }

        // Four arms of length `size` around a gap, 2px wide, with a 1px dark outline.
        public static void LayoutCrosshair(Image[] arms, int size, Color c, float gap = 7f)
        {
            Vector2[] dir = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < 4; i++)
            {
                var shadow = (RectTransform)arms[i].transform.parent;
                bool vertical = i < 2;
                shadow.anchorMin = shadow.anchorMax = shadow.pivot = new Vector2(0.5f, 0.5f);
                shadow.sizeDelta = vertical ? new Vector2(4, size + 2) : new Vector2(size + 2, 4);
                shadow.anchoredPosition = dir[i] * (gap + size * 0.5f);
                arms[i].color = c;
            }
        }

        // ───────────── row builders ─────────────
        void SliderRow(Transform col, string label, float min, float max, bool whole,
            System.Func<float> get, System.Action<float> set, System.Func<float, string> fmt)
        {
            var block = VCol(col, "SliderRow", 10);
            var head = Rect(block.transform, "Head").LE(-1, 22);
            Label(head, label, F.Body, 16, Fg).rectTransform.Fill(0, 0, 120, 0);
            var val = Label(head, fmt(get()), F.MonoBold, 15, Accent, TextAlignmentOptions.MidlineRight);
            val.rectTransform.Fill(300, 0, 0, 0);
            var s = UIKit.Slider(block.transform, min, max, get(), whole, v => { set(v); val.text = fmt(get()); });
            refreshers.Add(() => { s.SetValueWithoutNotify(get()); val.text = fmt(get()); });
        }

        void Check(Transform col, string label, System.Func<bool> get, System.Action<bool> set)
        {
            var t = CheckRow(col, label, get(), v => set(v));
            refreshers.Add(() => t.isOn = get());
        }

        void ResolutionRow(Transform col)
        {
            var sizes = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!sizes.Contains(v)) sizes.Add(v);
            }
            if (sizes.Count == 0) sizes.Add(new Vector2Int(Screen.width, Screen.height));
            sizes.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));

            int Current()
            {
                var want = new Vector2Int(GameSettings.ResolutionW > 0 ? GameSettings.ResolutionW : Screen.currentResolution.width,
                                          GameSettings.ResolutionH > 0 ? GameSettings.ResolutionH : Screen.currentResolution.height);
                int i = sizes.IndexOf(want);
                return i < 0 ? 0 : i;
            }

            var block = VCol(col, "Resolution", 8);
            Label(block.transform, "Resolution", F.Body, 16, Fg).LE(-1, 22);
            var box = Card(block.transform, "Stepper", Surface, LineStrong, 1.5f);
            box.LE(-1, 46);
            var val = Label(box.transform, "", F.Body, 16, Fg, TextAlignmentOptions.Center);
            val.rectTransform.Fill(46, 0, 46, 0);
            void Show() { var s = sizes[Current()]; val.text = $"{s.x} × {s.y}"; }
            void Step(int d)
            {
                int i = (Current() + d + sizes.Count) % sizes.Count;
                GameSettings.ResolutionW = sizes[i].x; GameSettings.ResolutionH = sizes[i].y;
                Show();
            }
            var prev = Button(box.transform, "", Btn.Ghost, 10, () => Step(+1));
            prev.GetComponent<RectTransform>().At(0f, 0.5f, 0, 0, 46, 46);
            Chevron(prev.transform, Fg, false);
            var next = Button(box.transform, "", Btn.Ghost, 10, () => Step(-1));
            next.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 46, 46);
            Chevron(next.transform, Fg, true);
            refreshers.Add(Show);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { LastEscCloseFrame = Time.frameCount; Close(); }
        }
    }
}
