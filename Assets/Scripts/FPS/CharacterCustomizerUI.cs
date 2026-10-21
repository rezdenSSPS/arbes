using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Lobby character creator: left-side panel with a live 3D preview and
    // < > pickers for body, hat, colours, skin and hair.  Builds itself whenever
    // the AirsoftLobby scene is loaded; the chosen look is saved and synced via MatchNet.
    public class CharacterCustomizerUI : MonoBehaviour
    {
        const string LobbyScene = "AirsoftLobby";
        const int PreviewLayer = 31;
        static readonly Vector3 StagePos = new Vector3(0f, -500f, 0f);

        LookData look;
        Transform stage;
        Camera previewCam;
        RenderTexture rt;
        ArbesLook.Avatar avatar;
        float spin = 160f;
        bool dragging;
        GameObject canvasRoot;
        bool shown = true;
        readonly System.Collections.Generic.List<(TextMeshProUGUI txt, System.Func<string> get)> rows = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (s.name != LobbyScene) return;
            if (FindAnyObjectByType<CharacterCustomizerUI>() != null) return;
            var go = new GameObject("~CharacterCustomizer");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<CharacterCustomizerUI>();
        }

        void Start()
        {
            int packed = MatchNet.Instance != null ? MatchNet.Instance.LocalLook : ArbesLook.LoadLocal();
            if (packed < 0) packed = ArbesLook.LoadLocal();
            look = ArbesLook.Unpack(packed);
            BuildStage();
            BuildUI();
            RebuildAvatar();
            SetShown(InLobby());
        }

        // ───────────── 3D preview ─────────────
        void BuildStage()
        {
            stage = new GameObject("PreviewStage").transform;
            stage.SetParent(transform, false);
            stage.position = StagePos;

            rt = new RenderTexture(640, 800, 24) { name = "CharacterPreview", antiAliasing = 4 };
            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = new Vector3(0f, 1.15f, 3.4f);
            camGo.transform.LookAt(stage.position + Vector3.up * 0.95f);
            previewCam = camGo.AddComponent<Camera>();
            previewCam.cullingMask = 1 << PreviewLayer;
            previewCam.clearFlags = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 1f);
            previewCam.fieldOfView = 34f;
            previewCam.nearClipPlane = 0.1f; previewCam.farClipPlane = 20f;
            previewCam.targetTexture = rt;
            previewCam.depth = -10;

            AddLight(new Vector3(1.6f, 2.6f, 2.4f), new Color(1f, 0.95f, 0.88f), 9f);
            AddLight(new Vector3(-2.2f, 1.4f, 1.5f), new Color(0.6f, 0.75f, 1f), 5f);
            AddLight(new Vector3(0f, 2.2f, -2.2f), new Color(1f, 0.7f, 0.4f), 6f);

            // little round platform
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(plat.GetComponent<Collider>());
            plat.transform.SetParent(stage, false);
            plat.transform.localPosition = new Vector3(0, -0.03f, 0);
            plat.transform.localScale = new Vector3(1.3f, 0.03f, 1.3f);
            plat.GetComponent<Renderer>().sharedMaterial = ArbesLook.MakeMat(new Color(1f, 0.55f, 0.15f));
            ArbesLook.SetLayer(plat, PreviewLayer);
        }

        void AddLight(Vector3 local, Color c, float intensity)
        {
            var go = new GameObject("PreviewLight");
            go.transform.SetParent(stage, false);
            go.transform.localPosition = local;
            go.layer = PreviewLayer;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = 8f;
            l.cullingMask = 1 << PreviewLayer;
        }

        void RebuildAvatar()
        {
            if (avatar != null && avatar.root) Destroy(avatar.root);
            avatar = null;
            var turn = stage.Find("Turntable");
            if (turn == null)
            {
                turn = new GameObject("Turntable").transform;
                turn.SetParent(stage, false);
                turn.localRotation = Quaternion.Euler(0, spin, 0);
            }
            try { avatar = ArbesLook.BuildAvatar(turn, ArbesLook.Pack(look)); }
            catch (System.Exception e) { Debug.LogWarning("[Arbes] preview failed: " + e.Message); }
            if (avatar != null) ArbesLook.SetLayer(avatar.root, PreviewLayer);
        }

        // Only visible once the player is in a lobby (hosting or connected), not on the connect screen.
        static bool InLobby()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;
            return nm.IsServer || nm.IsConnectedClient;
        }

        void SetShown(bool on)
        {
            shown = on;
            if (canvasRoot) canvasRoot.SetActive(on);
            if (previewCam) previewCam.enabled = on;
        }

        void Update()
        {
            bool want = InLobby();
            if (want != shown) SetShown(want);
            if (!shown) return;
            if (stage == null) return;
            var turn = stage.Find("Turntable");
            if (turn)
            {
                if (!dragging) spin += Time.deltaTime * 25f;
                turn.localRotation = Quaternion.Euler(0, spin, 0);
            }
            if (avatar != null) ArbesLook.Animate(avatar, 0f, false);
        }

        // ───────────── UI ─────────────
        static readonly Color Accent = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.09f, 0.88f);
        static readonly Color BtnBg = new Color(0.16f, 0.17f, 0.21f, 1f);

        void BuildUI()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            var canvasGo = new GameObject("CustomizerCanvas");
            canvasRoot = canvasGo;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = NewRect("Panel", canvasGo.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchoredPosition = new Vector2(28f, 0f);
            panel.sizeDelta = new Vector2(400f, 980f);
            panel.gameObject.AddComponent<Image>().color = PanelBg;

            var title = Text(panel, "Title", "YOUR CHARACTER", 30, Accent, TextAlignmentOptions.Center);
            Place(title.rectTransform, 0, -14, 380, 44);

            // preview
            var prev = NewRect("Preview", panel);
            Place(prev, 0, -62, 360, 450);
            var raw = prev.gameObject.AddComponent<RawImage>();
            raw.texture = rt;
            var drag = prev.gameObject.AddComponent<PreviewDrag>();
            drag.owner = this;

            float y = -520;
            AddRow(panel, ref y, "BODY", () => ArbesLook.BodyNames[look.body], d => look.body = Wrap(look.body + d, ArbesLook.Bodies.Length));
            AddRow(panel, ref y, "HAT", () => ArbesLook.Hats[look.hat], d => look.hat = Wrap(look.hat + d, ArbesLook.Hats.Length));
            AddRow(panel, ref y, "HAT COLOR", () => ArbesLook.PaletteNames[look.hatColor], d => look.hatColor = Wrap(look.hatColor + d, ArbesLook.Palette.Length), () => ArbesLook.Palette[look.hatColor]);
            AddRow(panel, ref y, "SHIRT", () => ArbesLook.PaletteNames[look.shirt], d => look.shirt = Wrap(look.shirt + d, ArbesLook.Palette.Length), () => ArbesLook.Palette[look.shirt]);
            AddRow(panel, ref y, "PANTS", () => ArbesLook.PaletteNames[look.pants], d => look.pants = Wrap(look.pants + d, ArbesLook.Palette.Length), () => ArbesLook.Palette[look.pants]);
            AddRow(panel, ref y, "SKIN", () => ArbesLook.SkinNames[look.skin], d => look.skin = Wrap(look.skin + d, ArbesLook.SkinTones.Length), () => ArbesLook.SkinTones[look.skin]);
            AddRow(panel, ref y, "HAIR", () => ArbesLook.HairNames[look.hair], d => look.hair = Wrap(look.hair + d, ArbesLook.HairColors.Length), () => ArbesLook.HairColors[look.hair]);

            var rnd = Button(panel, "RandomBtn", "RANDOMIZE", () =>
            {
                look = ArbesLook.Unpack(ArbesLook.Random());
                Changed();
            });
            Place(rnd, 0, y - 8, 360, 48);
            var note = Text(panel, "Note", "Tip: drag the preview to rotate. In Team Deathmatch your shirt shows your team colour.",
                15, new Color(0.7f, 0.7f, 0.75f), TextAlignmentOptions.Center);
            note.textWrappingMode = TextWrappingModes.Normal;
            Place(note.rectTransform, 0, y - 62, 360, 44);
        }

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        void AddRow(RectTransform panel, ref float y, string label, System.Func<string> get, System.Action<int> step, System.Func<Color> swatch = null)
        {
            var row = NewRect(label + "Row", panel);
            Place(row, 0, y, 360, 46);

            var lbl = Text(row, "Label", label, 16, new Color(0.65f, 0.67f, 0.72f), TextAlignmentOptions.Left);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(0, 0); lrt.sizeDelta = new Vector2(110, 0);

            var prev = Button(row, "Prev", "<", () => { step(-1); Changed(); });
            var prt = prev; prt.anchorMin = prt.anchorMax = new Vector2(0, 0.5f); prt.pivot = new Vector2(0, 0.5f);
            prt.anchoredPosition = new Vector2(112, 0); prt.sizeDelta = new Vector2(42, 42);

            var val = Text(row, "Value", get(), 19, Color.white, TextAlignmentOptions.Center);
            var vrt = val.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0, 0.5f); vrt.pivot = new Vector2(0, 0.5f);
            vrt.anchoredPosition = new Vector2(158, 0); vrt.sizeDelta = new Vector2(156, 42);
            rows.Add((val, get));

            if (swatch != null)
            {
                var sw = NewRect("Swatch", row);
                sw.anchorMin = sw.anchorMax = new Vector2(0, 0.5f); sw.pivot = new Vector2(0, 0.5f);
                sw.anchoredPosition = new Vector2(160, -17); sw.sizeDelta = new Vector2(152, 5);
                var img = sw.gameObject.AddComponent<Image>();
                img.color = swatch();
                rows.Add((null, () => { img.color = swatch(); return ""; }));
            }

            var next = Button(row, "Next", ">", () => { step(+1); Changed(); });
            next.anchorMin = next.anchorMax = new Vector2(0, 0.5f); next.pivot = new Vector2(0, 0.5f);
            next.anchoredPosition = new Vector2(318, 0); next.sizeDelta = new Vector2(42, 42);

            y -= 52;
        }

        void Changed()
        {
            foreach (var r in rows)
            {
                string s = r.get();
                if (r.txt != null) r.txt.text = s;
            }
            int packed = ArbesLook.Pack(look);
            if (MatchNet.Instance != null) MatchNet.Instance.SetLocalLook(packed);
            else ArbesLook.SaveLocal(packed);
            RebuildAvatar();
        }

        // ───────────── tiny UI helpers ─────────────
        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color c, TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = c; t.alignment = align;
            t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform Button(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = BtnBg;
            var b = rt.gameObject.AddComponent<Button>();
            var cols = b.colors;
            cols.highlightedColor = new Color(1f, 0.75f, 0.5f);
            cols.pressedColor = Accent;
            b.colors = cols;
            b.onClick.AddListener(onClick);
            var t = Text(rt, "Text", label, 20, Color.white, TextAlignmentOptions.Center);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            return rt;
        }

        // drag-to-rotate on the preview image
        class PreviewDrag : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
        {
            public CharacterCustomizerUI owner;
            public void OnBeginDrag(PointerEventData e) { owner.dragging = true; }
            public void OnEndDrag(PointerEventData e) { owner.dragging = false; }
            public void OnDrag(PointerEventData e) { owner.spin -= e.delta.x * 0.5f; }
        }

        void OnDestroy()
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
        }
    }
}
