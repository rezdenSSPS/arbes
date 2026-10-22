using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AirsoftFPS
{
    // "Field tactical" design system: colour tokens, the three typefaces (as TMP
    // font assets) and small builders for every uGUI widget the screens use.
    // All screens are built in code at a 1280x720 reference resolution, so the
    // pixel values match the design canvas 1:1.
    public static class UIKit
    {
        // ───────────── colour tokens ─────────────
        public static readonly Color Ground     = Hex("#12140F");
        public static readonly Color Surface    = Hex("#1B1E17");
        public static readonly Color Raised     = Hex("#252920");
        public static readonly Color Line       = Hex("#3A3F32");
        public static readonly Color LineStrong = Hex("#4A5040");
        public static readonly Color Fg       = Hex("#ECEDE6");
        public static readonly Color TextSoft   = Hex("#D9D6CC");
        public static readonly Color Muted      = Hex("#A3A898");
        public static readonly Color Faint      = Hex("#8A8F80");
        public static readonly Color Accent     = Hex("#FF7A1A");
        public static readonly Color AccentHi   = Hex("#FF9A4D");
        public static readonly Color Ready      = Hex("#7BD389");
        public static readonly Color Red        = Hex("#F05A4F");
        public static readonly Color Blue       = Hex("#7DB8FF");
        public static readonly Color Danger     = Hex("#FF6B5E");
        public static readonly Color DangerSoft = Hex("#FF8A7E");
        public static readonly Color DangerLine = Hex("#6B2E28");
        public static readonly Color Scenery    = Hex("#2B3138");
        public static readonly Color Disabled   = Hex("#2C3026");
        public static readonly Color DisabledFg = Hex("#6E7364");

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        public static Color A(Color c, float a) { c.a = a; return c; }
        public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // Translucent ground used behind HUD widgets.
        public static Color Scrim(float a = 0.72f) => A(Ground, a);

        public static Color TeamColor(int team) => team == 1 ? Red : team == 2 ? Blue : Fg;

        // ───────────── fonts ─────────────
        public enum F { CondSemi, CondBold, CondBlack, Body, BodyMed, BodySemi, BodyBold, Mono, MonoBold }

        static readonly string[] FontFiles =
        {
            "BarlowCondensed-SemiBold", "BarlowCondensed-Bold", "BarlowCondensed-ExtraBold",
            "Barlow-Regular", "Barlow-Medium", "Barlow-SemiBold", "Barlow-Bold",
            "JetBrainsMono-Medium", "JetBrainsMono-Bold"
        };
        static readonly bool[] FontIsBold = { true, true, true, false, false, true, true, false, true };

        static readonly TMP_FontAsset[] fontCache = new TMP_FontAsset[FontFiles.Length];
        static readonly bool[] fontTried = new bool[FontFiles.Length];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < fontCache.Length; i++) { fontCache[i] = null; fontTried[i] = false; }
            sprites.Clear();
        }

        // Fonts live as .ttf in Resources/AirsoftUI/Fonts and become dynamic TMP font
        // assets on first use. Missing files fall back to TMP's default font.
        public static TMP_FontAsset Font(F f, out bool isFallback)
        {
            int i = (int)f;
            if (!fontTried[i])
            {
                fontTried[i] = true;
                var ttf = Resources.Load<Font>("AirsoftUI/Fonts/" + FontFiles[i]);
                if (ttf != null)
                {
                    try
                    {
                        var fa = TMP_FontAsset.CreateFontAsset(ttf, 72, 7, GlyphRenderMode.SDFAA, 1024, 1024,
                            AtlasPopulationMode.Dynamic, true);
                        if (fa != null)
                        {
                            fa.name = FontFiles[i] + " SDF";
                            var def = TMP_Settings.defaultFontAsset;
                            if (def != null)
                            {
                                fa.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                                fa.fallbackFontAssetTable.Add(def);
                            }
                            fontCache[i] = fa;
                        }
                    }
                    catch (System.Exception e) { Debug.LogWarning("[Airsoft UI] font " + FontFiles[i] + ": " + e.Message); }
                }
            }
            isFallback = fontCache[i] == null;
            return fontCache[i] != null ? fontCache[i] : TMP_Settings.defaultFontAsset;
        }

        public static void ApplyFont(TMP_Text t, F f)
        {
            t.font = Font(f, out bool fallback);
            t.fontStyle = (fallback && FontIsBold[(int)f]) ? FontStyles.Bold : FontStyles.Normal;
        }

        // ───────────── procedural sprites ─────────────
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        // 16px square, 4px opaque frame -> scale the frame with pixelsPerUnitMultiplier.
        public static Sprite FrameSprite => GetSprite("frame", () => MakeFrame(16, 4, 4));
        // Key-cap frame: 4px sides/top, 8px bottom (1.5px + 3px bottom at multiplier 2.67).
        public static Sprite KeySprite => GetSprite("key", () => MakeFrame(16, 4, 8));
        // 64px square with the top-right and bottom-left corners cut at 45deg (cut = 16px).
        public static Sprite ChamferSprite => GetSprite("chamfer", () => MakeChamfer(64, 16, false));
        public static Sprite ChamferFrameSprite => GetSprite("chamferFrame", () => MakeChamfer(64, 16, true));
        public static Sprite RingSprite => GetSprite("ring", () => MakeRing(128, 0.86f));
        public static Sprite DiscSprite => GetSprite("disc", () => MakeRing(128, 0f));
        public static Sprite VignetteSprite => GetSprite("vignette", MakeVignette);

        static Sprite GetSprite(string key, System.Func<Sprite> make)
        {
            if (!sprites.TryGetValue(key, out var s) || s == null) { s = make(); sprites[key] = s; }
            return s;
        }

        static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            return t;
        }

        static Sprite MakeFrame(int size, int side, int bottom)
        {
            var t = NewTex(size, size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool on = x < side || x >= size - side || y < bottom || y >= size - side;
                    px[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            t.SetPixels32(px); t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(side, bottom, side, side));
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        static Sprite MakeChamfer(int size, int cut, bool frameOnly)
        {
            var t = NewTex(size, size);
            var px = new Color32[size * size];
            const float stroke = 4f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = x + 0.5f, cy = y + 0.5f;
                    // signed distance (in px, positive = inside) to the two diagonal cuts and the edges
                    float dTR = ((size - cx) + (size - cy) - cut) / 1.41421f;
                    float dBL = (cx + cy - cut) / 1.41421f;
                    float dEdge = Mathf.Min(Mathf.Min(cx, size - cx), Mathf.Min(cy, size - cy));
                    float d = Mathf.Min(Mathf.Min(dTR, dBL), dEdge);
                    float a = Mathf.Clamp01(d + 0.5f);
                    if (frameOnly) a *= 1f - Mathf.Clamp01(d - stroke + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            int b = cut + 4;
            var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        static Sprite MakeRing(int size, float innerFrac)
        {
            var t = NewTex(size, size);
            var px = new Color32[size * size];
            float r = size * 0.5f, inner = r * innerFrac;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d) * (innerFrac > 0 ? Mathf.Clamp01(d - inner) : 1f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        static Sprite MakeVignette()
        {
            const int size = 128;
            var t = NewTex(size, size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f, ny = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny) / 1.41421f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, d));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        // ───────────── canvas ─────────────
        public static Canvas Canvas(string name, int sortOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortOrder;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1280, 720);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ───────────── rect helpers ─────────────
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // Anchor + pivot at the same normalized point, then position/size in reference px.
        public static RectTransform At(this RectTransform rt, float ax, float ay, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // Top-left based placement inside a parent (design coordinates: x right, y down).
        public static RectTransform TL(this RectTransform rt, float x, float y, float w, float h)
            => rt.At(0f, 1f, x, -y, w, h);

        public static RectTransform Fill(this RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static T LE<T>(this T c, float w = -1, float h = -1, float flexW = -1, float flexH = -1) where T : Component
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return c;
        }

        public static HorizontalLayoutGroup HRow(Transform parent, string name, float spacing,
            TextAnchor align = TextAnchor.MiddleLeft, bool expandW = false, bool expandH = false)
        {
            var rt = Rect(parent, name);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing; g.childAlignment = align;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = expandW; g.childForceExpandHeight = expandH;
            return g;
        }

        public static VerticalLayoutGroup VCol(Transform parent, string name, float spacing,
            TextAnchor align = TextAnchor.UpperLeft, bool expandW = true, bool expandH = false)
        {
            var rt = Rect(parent, name);
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing; g.childAlignment = align;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = expandW; g.childForceExpandHeight = expandH;
            return g;
        }

        public static RectTransform Spacer(Transform parent, float w = -1, float h = -1, float flex = 1)
        {
            var rt = Rect(parent, "Spacer");
            rt.LE(w, h, w < 0 ? flex : -1, h < 0 ? flex : -1);
            return rt;
        }

        // ───────────── primitives ─────────────
        public static Image Box(Transform parent, string name, Color c, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        // A frame (border only) stretched over its parent.
        public static Image Frame(Transform parent, Color c, float px, string name = "Frame")
        {
            var img = Box(parent, name, c);
            img.sprite = FrameSprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            img.pixelsPerUnitMultiplier = 4f / Mathf.Max(0.25f, px);
            img.rectTransform.Fill();
            img.transform.SetAsLastSibling();
            return img;
        }

        // A card: filled box + 1px frame.
        public static Image Card(Transform parent, string name, Color bg, Color line, float px = 1f)
        {
            var img = Box(parent, name, bg, true);
            if (line.a > 0) Frame(img.transform, line, px);
            return img;
        }

        public static Image Chamfer(Image img, float cut)
        {
            img.sprite = ChamferSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, cut);
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, F font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float spacingEm = 0f, string name = "Label")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyFont(t, font);
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.characterSpacing = spacingEm * 100f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        // Small uppercase caption, e.g. "CALLSIGN", "PLAYERS".
        public static TextMeshProUGUI Caption(Transform parent, string text, float size = 11f, Color? color = null, float spacing = 0.14f)
            => Label(parent, text, F.BodySemi, size, color ?? Muted, TextAlignmentOptions.MidlineLeft, spacing, "Caption");

        public static TextMeshProUGUI Wrap(this TextMeshProUGUI t, float lineSpacing = 0f)
        {
            t.textWrappingMode = TextWrappingModes.Normal;
            t.lineSpacing = lineSpacing;
            return t;
        }

        // Progress bar: returns the fill image; drive it with SetBar.
        public static Image Bar(Transform parent, string name, Color bg, Color fill)
        {
            var back = Box(parent, name, bg);
            var f = Box(back.transform, "Fill", fill);
            f.rectTransform.anchorMin = Vector2.zero;
            f.rectTransform.anchorMax = new Vector2(0f, 1f);
            f.rectTransform.pivot = new Vector2(0f, 0.5f);
            f.rectTransform.offsetMin = f.rectTransform.offsetMax = Vector2.zero;
            return f;
        }

        public static void SetBar(Image fill, float pct)
        {
            if (fill == null) return;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(pct), 1f);
        }

        public static RectTransform Badge(Transform parent, string text, Color bg, Color fg, Color border, float size = 10f)
        {
            var box = Box(parent, "Badge", bg);
            if (border.a > 0) Frame(box.transform, border, 1f);
            var h = box.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(5, 5, 3, 3);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var fitter = box.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = Label(box.transform, text, F.BodyBold, size, fg, TextAlignmentOptions.Center, 0.14f);
            t.gameObject.name = "Text";
            // the frame must not take part in the layout
            var fr = box.transform.Find("Frame");
            if (fr) { fr.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; fr.SetAsLastSibling(); }
            return box.rectTransform;
        }

        // Keyboard key cap: mono text in a 1.5px frame with a 3px bottom edge.
        public static RectTransform KeyCap(Transform parent, string key, float size = 11f, Color? fg = null, Color? line = null)
        {
            var box = Rect(parent, "Key_" + key);
            var h = box.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(7, 7, 1, 4);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var fitter = box.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(box, key, F.MonoBold, size, fg ?? Fg, TextAlignmentOptions.Center);
            var fr = Box(box, "Frame", line ?? LineStrong);
            fr.sprite = KeySprite; fr.type = Image.Type.Sliced; fr.fillCenter = false;
            fr.pixelsPerUnitMultiplier = 4f / 1.5f;
            fr.rectTransform.Fill();
            fr.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return box;
        }

        // ───────────── buttons ─────────────
        public enum Btn { Primary, Secondary, Quiet, Danger, Ghost, Tab, Segment }

        public static KitButton Button(Transform parent, string text, Btn style, float fontSize, UnityAction onClick,
            F font = F.CondBold, float chamfer = 14f)
        {
            var rt = Rect(parent, "Btn_" + text);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.raycastTarget = true;
            var b = rt.gameObject.AddComponent<KitButton>();
            b.bg = bg;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };

            if (style == Btn.Primary) Chamfer(bg, chamfer);
            if (style == Btn.Secondary || style == Btn.Quiet || style == Btn.Danger)
                b.frame = Frame(rt, Color.white, 1.5f);

            var lbl = Label(rt, text, font, fontSize, Fg, TextAlignmentOptions.Center, 0.08f, "Text");
            lbl.rectTransform.Fill(12, 0, 12, 0);
            b.label = lbl;
            b.SetStyle(style);
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        // ───────────── inputs ─────────────
        public static TMP_InputField Input(Transform parent, string placeholder, F font, float size, int limit,
            float spacingEm = 0f, bool upper = false)
        {
            var rt = Rect(parent, "Input");
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = Surface;
            var frame = Frame(rt, LineStrong, 1.5f);

            var area = Rect(rt, "TextArea");
            area.Fill(14, 0, 14, 0);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = Label(area, placeholder, font, size, A(Faint, 0.9f), TextAlignmentOptions.MidlineLeft, spacingEm, "Placeholder");
            ph.rectTransform.Fill();
            var tx = Label(area, "", font, size, Fg, TextAlignmentOptions.MidlineLeft, spacingEm, "Text");
            tx.rectTransform.Fill();
            tx.richText = false;

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = tx;
            input.placeholder = ph;
            input.characterLimit = limit;
            input.caretColor = Accent;
            input.customCaretColor = true;
            input.selectionColor = A(Accent, 0.35f);
            input.transition = Selectable.Transition.None;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            if (upper) input.characterValidation = TMP_InputField.CharacterValidation.None;
            if (upper) input.onValidateInput = (s, i, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            input.onSelect.AddListener(_ => frame.color = Accent);
            input.onDeselect.AddListener(_ => frame.color = LineStrong);
            input.text = "";
            return input;
        }

        // Checkbox on the right of a label row; returns the Toggle (row is clickable).
        public static Toggle CheckRow(Transform parent, string label, bool on, UnityAction<bool> changed)
        {
            var row = Rect(parent, "Check_" + label);
            row.LE(-1, 30);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var l = Label(row, label, F.Body, 16, Fg);
            l.rectTransform.Fill(0, 0, 40, 0);

            var box = Rect(row, "Box").At(1f, 0.5f, 0, 0, 22, 22);
            var boxBg = box.gameObject.AddComponent<Image>();
            boxBg.color = Surface;
            Frame(box, LineStrong, 1.5f);
            var check = Box(box, "Check", Accent);
            check.rectTransform.Fill(0, 0, 0, 0);
            CheckMark(check.transform, Ground);

            var tg = row.gameObject.AddComponent<Toggle>();
            tg.targetGraphic = hit;
            tg.transition = Selectable.Transition.None;
            tg.navigation = new Navigation { mode = Navigation.Mode.None };
            tg.SetIsOnWithoutNotify(on);
            check.gameObject.SetActive(on);
            tg.onValueChanged.AddListener(v => check.gameObject.SetActive(v));
            if (changed != null) tg.onValueChanged.AddListener(changed);
            return tg;
        }

        // A check mark drawn from two rotated bars (no glyph dependency).
        public static RectTransform CheckMark(Transform parent, Color c, float scale = 1f)
        {
            var root = Rect(parent, "CheckMark").At(0.5f, 0.5f, 0, 0, 16 * scale, 16 * scale);
            var a = Box(root, "Short", c).rectTransform;
            a.At(0.5f, 0.5f, -3.2f * scale, -1.2f * scale, 7f * scale, 2.6f * scale);
            a.localRotation = Quaternion.Euler(0, 0, -45);
            var b = Box(root, "Long", c).rectTransform;
            b.At(0.5f, 0.5f, 1.8f * scale, 0.6f * scale, 12f * scale, 2.6f * scale);
            b.localRotation = Quaternion.Euler(0, 0, 45);
            return root;
        }

        // Chevron (< or >) drawn from two bars.
        public static RectTransform Chevron(Transform parent, Color c, bool right, float size = 12f)
        {
            var root = Rect(parent, right ? "ChevronR" : "ChevronL").At(0.5f, 0.5f, 0, 0, size, size);
            float s = right ? 1f : -1f;
            var a = Box(root, "A", c).rectTransform;
            a.At(0.5f, 0.5f, 0, size * 0.22f, size * 0.72f, 2.2f);
            a.localRotation = Quaternion.Euler(0, 0, -45 * s);
            var b = Box(root, "B", c).rectTransform;
            b.At(0.5f, 0.5f, 0, -size * 0.22f, size * 0.72f, 2.2f);
            b.localRotation = Quaternion.Euler(0, 0, 45 * s);
            return root;
        }

        // Slider with a 4px track, accent fill and a square handle.
        public static Slider Slider(Transform parent, float min, float max, float value, bool whole, UnityAction<float> changed)
        {
            var rt = Rect(parent, "Slider");
            rt.LE(-1, 22);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var track = Box(rt, "Track", Line).rectTransform;
            track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f);
            track.sizeDelta = new Vector2(0, 4); track.anchoredPosition = Vector2.zero;

            var fillArea = Rect(rt, "FillArea");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(0, 4); fillArea.anchoredPosition = Vector2.zero;
            var fill = Box(fillArea, "Fill", Accent).rectTransform;
            fill.sizeDelta = Vector2.zero;

            var handleArea = Rect(rt, "HandleArea").Fill(7, 0, 7, 0);
            var handle = Box(handleArea, "Handle", Fg, true);
            handle.rectTransform.sizeDelta = new Vector2(14, 18);

            var s = rt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.transition = Selectable.Transition.ColorTint;
            var cb = s.colors; cb.highlightedColor = new Color(1f, 0.85f, 0.7f); cb.pressedColor = AccentHi; s.colors = cb;
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole;
            s.SetValueWithoutNotify(value);
            if (changed != null) s.onValueChanged.AddListener(changed);
            return s;
        }

        // Segmented control (e.g. ONLINE | LAN, LOW | MEDIUM | HIGH).
        public static Segmented Segments(Transform parent, string[] options, int selected, float height, float fontSize,
            UnityAction<int> picked, float padX = 22f)
        {
            var row = HRow(parent, "Segments", 0, TextAnchor.MiddleLeft);
            row.padding = new RectOffset(2, 2, 2, 2);
            var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var frame = Frame(row.transform, LineStrong, 1.5f);
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var seg = row.gameObject.AddComponent<Segmented>();
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = Button(row.transform, options[i], Btn.Segment, fontSize, () => seg.Select(idx, true));
                b.label.characterSpacing = 10f;
                b.LE(b.label.preferredWidth + padX * 2, height);
                seg.buttons.Add(b);
            }
            frame.transform.SetAsLastSibling();
            seg.onPick = picked;
            seg.Select(selected, false);
            return seg;
        }

        // ───────────── misc ─────────────
        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string ModeName(MatchNet.GameModeType m) => m switch
        {
            MatchNet.GameModeType.FreeForAll => "Free-for-All",
            MatchNet.GameModeType.TeamDeathmatch => "Team Deathmatch",
            MatchNet.GameModeType.GunGame => "Gun Game",
            MatchNet.GameModeType.Milsim => "Milsim",
            MatchNet.GameModeType.CaptureTheFlag => "Capture the Flag",
            MatchNet.GameModeType.Domination => "Domination",
            MatchNet.GameModeType.Medic => "Medic",
            MatchNet.GameModeType.Bomb => "Bomb",
            MatchNet.GameModeType.VipEscort => "VIP Escort",
            _ => "Co-op Survival"
        };

        public static string ModeDesc(MatchNet.GameModeType m) => m switch
        {
            MatchNet.GameModeType.FreeForAll => $"Every player for themselves. First to {MatchNet.FFA_KILL_TARGET} kills.",
            MatchNet.GameModeType.TeamDeathmatch => $"Red vs Blue. First team to {MatchNet.TDM_KILL_TARGET} kills.",
            MatchNet.GameModeType.GunGame => $"Each kill = next weapon. Finish all {MatchNet.GUNGAME_WEAPONS}.",
            MatchNet.GameModeType.Milsim => $"One BB = out: walk back to respawn. First to {MatchNet.MILSIM_HIT_TARGET}.",
            MatchNet.GameModeType.CaptureTheFlag => $"Grab the enemy flag, bring it home. First to {CaptureTheFlag.CAPTURE_TARGET}.",
            MatchNet.GameModeType.Domination => $"Hold points A, B and C to score. First to {Domination.SCORE_TARGET}.",
            MatchNet.GameModeType.Medic => $"One BB = down. Medics revive (hold E). {Medic.BASE_TICKETS} tickets.",
            MatchNet.GameModeType.Bomb => $"Plant at A or B, or defuse it. No respawns. First to {BombMode.ROUNDS_TO_WIN} rounds.",
            MatchNet.GameModeType.VipEscort => $"Escort your VIP to the extraction, or take them down. First to {VipEscort.ROUNDS_TO_WIN}.",
            _ => "Team up against waves of AI enemies."
        };

        public static readonly (string action, string key)[] Binds =
        {
            ("Move", "W A S D"), ("Sprint", "SHIFT"), ("Crouch", "C / CTRL"), ("Jump", "SPACE"),
            ("Fire", "LMB"), ("Aim / scope", "RMB"), ("Reload", "R"), ("Weapons", "1 – 0"),
            ("Scoreboard", "TAB"), ("Interact", "E"), ("Pause", "ESC")
        };
    }

    // Button whose look (bg / frame / label colours) follows its interaction state.
    public class KitButton : UnityEngine.UI.Button
    {
        public Image bg;
        public Image frame;
        public TextMeshProUGUI label;
        public Color bgN, bgH, bgP, bgD, fgN, fgH, fgD, lineN, lineH, lineD;
        public bool selectedLook;   // for tabs / segments
        public Color bgSel, fgSel, lineSel;
        public UIKit.Btn style;

        public void SetStyle(UIKit.Btn s)
        {
            style = s;
            Color clear = Color.clear;
            switch (s)
            {
                case UIKit.Btn.Primary:
                    bgN = UIKit.Accent; bgH = UIKit.AccentHi; bgP = UIKit.Hex("#E86A10"); bgD = UIKit.Disabled;
                    fgN = fgH = UIKit.Ground; fgD = UIKit.DisabledFg; lineN = lineH = lineD = clear; break;
                case UIKit.Btn.Secondary:
                    bgN = clear; bgH = UIKit.A(UIKit.Fg, 0.08f); bgP = UIKit.A(UIKit.Fg, 0.16f); bgD = clear;
                    fgN = fgH = UIKit.Fg; fgD = UIKit.DisabledFg;
                    lineN = UIKit.Fg; lineH = UIKit.Fg; lineD = UIKit.Line; break;
                case UIKit.Btn.Quiet:
                    bgN = clear; bgH = UIKit.A(UIKit.Fg, 0.06f); bgP = UIKit.A(UIKit.Fg, 0.12f); bgD = clear;
                    fgN = fgH = UIKit.Fg; fgD = UIKit.DisabledFg;
                    lineN = UIKit.LineStrong; lineH = UIKit.Fg; lineD = UIKit.Line; break;
                case UIKit.Btn.Danger:
                    bgN = clear; bgH = UIKit.A(UIKit.Danger, 0.12f); bgP = UIKit.A(UIKit.Danger, 0.2f); bgD = clear;
                    fgN = UIKit.DangerSoft; fgH = UIKit.Hex("#FFB3AA"); fgD = UIKit.DisabledFg;
                    lineN = UIKit.DangerLine; lineH = UIKit.Danger; lineD = UIKit.Line; break;
                case UIKit.Btn.Ghost:
                    bgN = bgH = bgP = bgD = clear;
                    fgN = UIKit.Muted; fgH = UIKit.Fg; fgD = UIKit.DisabledFg;
                    lineN = lineH = lineD = clear; break;
                case UIKit.Btn.Tab:
                    bgN = bgH = bgP = bgD = clear;
                    fgN = UIKit.Muted; fgH = UIKit.Fg; fgD = UIKit.DisabledFg;
                    lineN = lineH = lineD = clear;
                    bgSel = clear; fgSel = UIKit.Fg; break;
                case UIKit.Btn.Segment:
                    bgN = clear; bgH = UIKit.A(UIKit.Fg, 0.08f); bgP = UIKit.A(UIKit.Fg, 0.16f); bgD = clear;
                    fgN = UIKit.Muted; fgH = UIKit.Fg; fgD = UIKit.DisabledFg;
                    lineN = lineH = lineD = clear;
                    bgSel = UIKit.Fg; fgSel = UIKit.Ground; break;
            }
            Refresh(true);
        }

        public void SetSelectedLook(bool on) { selectedLook = on; Refresh(true); }

        public void SetLabel(string s) { if (label) label.text = s; }

        public void Refresh(bool instant = true) => DoStateTransition(currentSelectionState, instant);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (bg == null) return;
            bool dis = state == SelectionState.Disabled;
            bool hov = state == SelectionState.Highlighted;
            bool prs = state == SelectionState.Pressed;
            Color b = dis ? bgD : prs ? bgP : hov ? bgH : bgN;
            Color f = dis ? fgD : hov || prs ? fgH : fgN;
            Color l = dis ? lineD : hov || prs ? lineH : lineN;
            if (selectedLook && !dis) { b = (hov && bgSel.a == 0) ? bgH : bgSel; f = fgSel; if (lineSel.a > 0) l = lineSel; }
            bg.color = b;
            if (label) label.color = f;
            if (frame) frame.color = l;
        }
    }

    // Row of mutually exclusive KitButtons.
    public class Segmented : MonoBehaviour
    {
        public readonly List<KitButton> buttons = new List<KitButton>();
        public UnityAction<int> onPick;
        public int Selected { get; private set; } = -1;

        public void Select(int i, bool notify)
        {
            Selected = i;
            for (int k = 0; k < buttons.Count; k++) buttons[k].SetSelectedLook(k == i);
            if (notify) onPick?.Invoke(i);
        }
    }

    // Spins a RectTransform (connecting spinner).
    public class UISpin : MonoBehaviour
    {
        public float speed = -360f;
        void Update() => transform.Rotate(0, 0, speed * Time.unscaledDeltaTime);
    }

    // Invokes a callback when the element is clicked (for non-Button graphics).
    public class UIClick : MonoBehaviour, IPointerClickHandler
    {
        public System.Action onClick;
        public void OnPointerClick(PointerEventData e) => onClick?.Invoke();
    }
}
