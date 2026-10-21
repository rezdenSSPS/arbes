using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Character editor: full-screen modal with a live 3D preview, steppers for body
    // and hat, and colour swatch rows. Builds itself whenever the AirsoftLobby scene
    // is loaded; the lobby screen shows the same 3D preview (PreviewTexture). The
    // chosen look is saved locally and synced to everyone via MatchNet.
    public class CharacterCustomizerUI : MonoBehaviour
    {
        const string LobbyScene = "AirsoftLobby";
        const int PreviewLayer = 31;
        static readonly Vector3 StagePos = new Vector3(0f, -500f, 0f);

        public static CharacterCustomizerUI Instance { get; private set; }

        LookData look;        // what the preview shows (working copy while the editor is open)
        LookData savedLook;   // what is saved / synced
        Transform stage;
        Camera previewCam;
        RenderTexture rt;
        AirsoftLook.Avatar avatar;
        float spin = 160f;
        bool dragging;
        GameObject modal;
        readonly System.Collections.Generic.List<System.Action> refreshers = new();

        public Texture PreviewTexture => rt;
        public bool IsOpen => modal != null && modal.activeSelf;
        // Set by the lobby screen while its small preview is visible.
        public bool PreviewWanted { get; set; }
        public string BodyName => AirsoftLook.BodyNames[look.body];
        public Color ShirtColor => AirsoftLook.Palette[savedLook.shirt];

        public event System.Action Closed;

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

        void Awake()
        {
            Instance = this;
            int packed = MatchNet.Instance != null ? MatchNet.Instance.LocalLook : AirsoftLook.LoadLocal();
            if (packed < 0) packed = AirsoftLook.LoadLocal();
            savedLook = look = AirsoftLook.Unpack(packed);
            BuildStage();
            BuildUI();
            RebuildAvatar();
        }

        public void Open()
        {
            look = savedLook;
            RebuildAvatar();
            Refresh();
            modal.SetActive(true);
        }

        public void Close(bool save)
        {
            if (save)
            {
                savedLook = look;
                int packed = AirsoftLook.Pack(savedLook);
                if (MatchNet.Instance != null) MatchNet.Instance.SetLocalLook(packed);
                else AirsoftLook.SaveLocal(packed);
            }
            else
            {
                look = savedLook;
                RebuildAvatar();
            }
            modal.SetActive(false);
            Closed?.Invoke();
        }

        // ───────────── 3D preview ─────────────
        void BuildStage()
        {
            stage = new GameObject("PreviewStage").transform;
            stage.SetParent(transform, false);
            stage.position = StagePos;

            rt = new RenderTexture(640, 820, 24) { name = "CharacterPreview", antiAliasing = 4 };
            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = new Vector3(0f, 1.15f, 3.4f);
            camGo.transform.LookAt(stage.position + Vector3.up * 0.95f);
            previewCam = camGo.AddComponent<Camera>();
            previewCam.cullingMask = 1 << PreviewLayer;
            previewCam.clearFlags = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = Scenery;
            previewCam.fieldOfView = 34f;
            previewCam.nearClipPlane = 0.1f; previewCam.farClipPlane = 20f;
            previewCam.targetTexture = rt;
            previewCam.depth = -10;
            previewCam.enabled = false;

            AddLight(new Vector3(1.6f, 2.6f, 2.4f), new Color(1f, 0.95f, 0.88f), 9f);
            AddLight(new Vector3(-2.2f, 1.4f, 1.5f), new Color(0.6f, 0.75f, 1f), 5f);
            AddLight(new Vector3(0f, 2.2f, -2.2f), new Color(1f, 0.7f, 0.4f), 6f);

            // round orange platform
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(plat.GetComponent<Collider>());
            plat.transform.SetParent(stage, false);
            plat.transform.localPosition = new Vector3(0, -0.03f, 0);
            plat.transform.localScale = new Vector3(1.3f, 0.03f, 1.3f);
            plat.GetComponent<Renderer>().sharedMaterial = AirsoftLook.MakeMat(Accent);
            AirsoftLook.SetLayer(plat, PreviewLayer);
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
            try { avatar = AirsoftLook.BuildAvatar(turn, AirsoftLook.Pack(look)); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] preview failed: " + e.Message); }
            if (avatar != null) AirsoftLook.SetLayer(avatar.root, PreviewLayer);
        }

        void Update()
        {
            // Never linger over the match if the lobby scene is still loaded underneath.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != gameObject.scene)
            {
                if (IsOpen) modal.SetActive(false);
                PreviewWanted = false;
            }
            bool render = IsOpen || PreviewWanted;
            if (previewCam && previewCam.enabled != render) previewCam.enabled = render;
            if (!render || stage == null) return;
            var turn = stage.Find("Turntable");
            if (turn)
            {
                if (!dragging) spin += Time.deltaTime * 25f;
                turn.localRotation = Quaternion.Euler(0, spin, 0);
            }
            if (avatar != null) AirsoftLook.Animate(avatar, 0f, false);

            if (IsOpen)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Close(false);
            }
        }

        public void Rotate(float degrees) => spin += degrees;
        public void SetDragging(bool on) => dragging = on;

        // ───────────── UI ─────────────
        void BuildUI()
        {
            var canvas = UIKit.Canvas("CustomizerCanvas", 20, transform);
            modal = Rect(canvas.transform, "Customizer").Fill().gameObject;
            Box(modal.transform, "BG", Ground, true).rectTransform.Fill();
            var stageRt = Rect(modal.transform, "Stage").At(0.5f, 0.5f, 0, 0, 1280, 720);

            // left: preview
            var left = Box(stageRt, "PreviewPane", Scenery, true);
            left.rectTransform.TL(0, 0, 560, 720);
            left.gameObject.AddComponent<RectMask2D>();
            PreviewImage(left.transform).Fill();
            var head = VCol(left.transform, "Head", 4);
            head.GetComponent<RectTransform>().TL(32, 28, 400, 60);
            Caption(head.transform, "PREVIEW", 11, Fg, 0.16f).LE(-1, 14);
            var bodyName = Label(head.transform, "", F.CondBold, 28, Fg);
            bodyName.LE(-1, 32);
            refreshers.Add(() => bodyName.text = AirsoftLook.BodyNames[look.body].ToUpperInvariant());

            var rot = HRow(left.transform, "Rotate", 10, TextAnchor.MiddleCenter);
            rot.GetComponent<RectTransform>().At(0.5f, 0f, 0, 20, 98, 44);
            foreach (bool right in new[] { false, true })
            {
                bool r = right;
                var b = Button(rot.transform, "", Btn.Ghost, 10, () => Rotate(r ? -45f : 45f));
                b.bgN = Scrim(0.78f); b.bgH = Scrim(0.95f); b.bgP = Ground; b.Refresh();
                b.LE(44, 44);
                Chevron(b.transform, Fg, right);
            }

            // right: controls
            var col = VCol(stageRt, "Controls", 18);
            col.padding = new RectOffset(48, 48, 36, 32);
            col.GetComponent<RectTransform>().TL(560, 0, 720, 720);

            var top = Rect(col.transform, "Header").LE(-1, 40);
            Label(top, "CHARACTER", F.CondBlack, 40, Fg).rectTransform.Fill(0, 0, 200, 0);
            var rnd = Button(top, "RANDOMIZE", Btn.Quiet, 16, () =>
            {
                look = AirsoftLook.Unpack(AirsoftLook.Random());
                Changed();
            });
            rnd.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 150, 40);
            rnd.label.rectTransform.Fill(34, 0, 10, 0);
            var dice = Rect(rnd.transform, "Dice").At(0f, 0.5f, 16, 0, 16, 16);
            Frame(dice, Fg, 2f);
            foreach (var p in new[] { new Vector2(-3.5f, 3.5f), new Vector2(3.5f, -3.5f), new Vector2(3.5f, 3.5f), new Vector2(-3.5f, -3.5f) })
                Box(dice, "Dot", Fg).rectTransform.At(0.5f, 0.5f, p.x, p.y, 2.4f, 2.4f);

            Stepper(col.transform, "BODY", () => AirsoftLook.BodyNames[look.body], () => look.body, AirsoftLook.Bodies.Length,
                d => look.body = Wrap(look.body + d, AirsoftLook.Bodies.Length));
            Stepper(col.transform, "HAT", () => AirsoftLook.Hats[look.hat], () => look.hat, AirsoftLook.Hats.Length,
                d => look.hat = Wrap(look.hat + d, AirsoftLook.Hats.Length));
            Swatches(col.transform, "HAT COLOR", AirsoftLook.PaletteNames, AirsoftLook.Palette, () => look.hatColor, i => look.hatColor = i);
            Swatches(col.transform, "SHIRT", AirsoftLook.PaletteNames, AirsoftLook.Palette, () => look.shirt, i => look.shirt = i);
            Swatches(col.transform, "PANTS", AirsoftLook.PaletteNames, AirsoftLook.Palette, () => look.pants, i => look.pants = i);
            Swatches(col.transform, "SKIN", AirsoftLook.SkinNames, AirsoftLook.SkinTones, () => look.skin, i => look.skin = i);
            Swatches(col.transform, "HAIR", AirsoftLook.HairNames, AirsoftLook.HairColors, () => look.hair, i => look.hair = i);

            Spacer(col.transform);
            var foot = Rect(col.transform, "Footer").LE(-1, 67);
            Box(foot, "Line", Line).rectTransform.At(0.5f, 1f, 0, 0, 624, 1);
            var note = Label(foot, "Saved on this computer and shown to all players. In Team Deathmatch your shirt takes the team color.",
                F.Body, 13, Muted).Wrap();
            note.rectTransform.Fill(0, 19, 260, 0);
            var save = Button(foot, "SAVE", Btn.Primary, 19, () => Close(true), F.CondBlack, 12);
            save.GetComponent<RectTransform>().At(1f, 0f, 0, 0, 104, 48);
            var cancel = Button(foot, "CANCEL", Btn.Quiet, 18, () => Close(false));
            cancel.GetComponent<RectTransform>().At(1f, 0f, -116, 0, 116, 48);

            modal.SetActive(false);
        }

        // A RawImage showing the 3D preview, cropped to fill its parent, drag to rotate.
        public RectTransform PreviewImage(Transform parent)
        {
            var holder = Rect(parent, "Preview3D");
            holder.gameObject.AddComponent<RectMask2D>();
            var img = Rect(holder, "Image");
            var raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = rt;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = rt.width / (float)rt.height;
            var hit = holder.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            holder.gameObject.AddComponent<PreviewDrag>().owner = this;
            raw.raycastTarget = false;
            return holder;
        }

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        void Stepper(Transform col, string label, System.Func<string> value, System.Func<int> index, int count, System.Action<int> step)
        {
            var row = HRow(col, label + "Row", 16, TextAnchor.MiddleLeft);
            row.LE(-1, 46);
            Caption(row.transform, label, 12, Muted, 0.14f).LE(110, 46);
            var box = Card(row.transform, "Box", Surface, Line, 1f);
            box.LE(-1, 46, 1);
            var prev = Button(box.transform, "", Btn.Ghost, 10, () => { step(-1); Changed(); });
            prev.GetComponent<RectTransform>().At(0f, 0.5f, 0, 0, 44, 44);
            Chevron(prev.transform, Fg, false);
            var next = Button(box.transform, "", Btn.Ghost, 10, () => { step(+1); Changed(); });
            next.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 44, 44);
            Chevron(next.transform, Fg, true);
            var v = Label(box.transform, "", F.CondBold, 20, Fg, TextAlignmentOptions.Center, 0.04f);
            v.rectTransform.Fill(44, 0, 44, 0);
            var pos = Label(box.transform, "", F.Mono, 11, Faint, TextAlignmentOptions.MidlineRight);
            pos.rectTransform.Fill(44, 0, 52, 0);
            refreshers.Add(() => { v.text = value().ToUpperInvariant(); pos.text = (index() + 1) + "/" + count; });
        }

        void Swatches(Transform col, string label, string[] names, Color[] colors, System.Func<int> get, System.Action<int> set)
        {
            var row = HRow(col, label + "Row", 16, TextAnchor.MiddleLeft);
            row.LE(-1, 36);
            var lab = VCol(row.transform, "Label", 2, TextAnchor.MiddleLeft);
            lab.LE(110, 36);
            Caption(lab.transform, label, 12, Muted, 0.14f).LE(-1, 15);
            var cur = Label(lab.transform, "", F.Body, 13, Fg);
            cur.LE(-1, 16);

            var sw = HRow(row.transform, "Swatches", 6, TextAnchor.MiddleLeft);
            sw.LE(-1, 36, 1);
            var rings = new Image[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                int idx = i;
                var cell = Rect(sw.transform, names[i]).LE(30, 30);
                var ring = Frame(cell, Accent, 2f, "Ring");
                ring.rectTransform.Fill(-4, -4, -4, -4);
                rings[i] = ring;
                var b = Button(cell, "", Btn.Ghost, 10, () => { set(idx); Changed(); });
                b.GetComponent<RectTransform>().Fill();
                b.bgN = b.bgH = b.bgP = colors[i];
                b.Refresh();
                Frame(b.transform, Ground, 1f);
            }
            refreshers.Add(() =>
            {
                int s = get();
                cur.text = names[s];
                for (int i = 0; i < rings.Length; i++) rings[i].enabled = i == s;
            });
        }

        void Refresh() { foreach (var r in refreshers) r(); }

        void Changed()
        {
            Refresh();
            RebuildAvatar();
        }

        // drag-to-rotate on a preview image
        class PreviewDrag : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
        {
            public CharacterCustomizerUI owner;
            public void OnBeginDrag(PointerEventData e) { owner.dragging = true; }
            public void OnEndDrag(PointerEventData e) { owner.dragging = false; }
            public void OnDrag(PointerEventData e) { owner.spin -= e.delta.x * 0.5f; }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
        }
    }
}
