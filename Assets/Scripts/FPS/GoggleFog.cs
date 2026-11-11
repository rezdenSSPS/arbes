using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Goggle fog (local player only, purely visual): the lens fogs up while you sprint, while you're out of
    // breath after a run and while you stand in smoke, and clears slowly when you walk (air through the vents).
    //
    // The fog is a low-resolution field over the lens (one value per texel), so it can be wiped in places.
    // F wipes it: a gloved left hand comes up, wipes the top half left to right, drops and wipes the bottom
    // half back, then leaves (WIPE_SECONDS, no aiming or shooting meanwhile). Only what the palm passed over
    // clears (down to WIPE_LEFT smears); the corners it missed stay a little fogged.
    //
    // It's a match rule the host picks in the lobby (MatchNet.FogEnabled): either everyone's lens fogs, at the
    // same rate whatever eyewear they picked (so eyewear stays cosmetic), or nobody's. Added to the owner's
    // player object by FPSController.
    [DisallowMultipleComponent]
    public class GoggleFog : MonoBehaviour
    {
        // Owner: a wipe is in progress (WeaponController: no aiming / shooting meanwhile).
        public static bool Wiping { get; private set; }
        // 0 = clear, 1 = fully fogged (average over the lens).
        public static float Level { get; private set; }

        const float WIPE_SECONDS = 0.95f;
        const float WIPE_COOLDOWN = 1.0f;
        const float WIPE_LEFT = 0.12f;       // the palm leaves a few smears
        const float PROMPT_AT = 0.3f;        // show the "F  WIPE GOGGLES" prompt from this fog level

        // per second
        const float GAIN_SPRINT = 0.055f;
        const float GAIN_PANT = 0.03f;       // out of breath: scaled by the missing stamina
        const float GAIN_SMOKE = 0.12f;
        const float DECAY_MOVING = 0.02f;    // walking: air through the vents
        const float DECAY_IDLE = 0.008f;

        // fog field / texture resolution (bilinear, stretched over the screen)
        const int FW = 160, FH = 90;

        // hand: height as a share of the screen height, and the palm's wiping footprint (screen fractions)
        const float HAND_H = 0.66f;
        const float PALM_RX = 0.13f, PALM_RY = 0.32f;

        public static string LensName(int eyewear) => eyewear == 3 ? "MASK" : eyewear == 2 ? "GLASSES" : "GOGGLES";

        FPSController fps;
        Health health;
        Canvas canvas;
        RectTransform canvasRt, hand;
        Image edge, haze, handImg;
        Texture2D edgeTex, hazeTex;
        Color32[] edgePx, hazePx;
        readonly float[] field = new float[FW * FH];
        readonly float[] atWipe = new float[FW * FH];   // the field when F was pressed (a wipe clears to WIPE_LEFT of it)
        GameObject prompt;
        TextMeshProUGUI promptText;
        float wipeT = -1f, nextWipe, maxFog;
        bool texDirty = true;
        Vector2 lastPalm;
        bool palmDown;
        Vector3 lastPos;
        static AudioClip s_wipeClip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Wiping = false; Level = 0f; }

        void Start()
        {
            fps = GetComponent<FPSController>();
            health = GetComponent<Health>();
            lastPos = transform.position;
            Build();
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            if (edgeTex != null) Destroy(edgeTex);
            if (hazeTex != null) Destroy(hazeTex);
            Wiping = false;
            Level = 0f;
        }

        static int LocalEyewear()
        {
            var mn = MatchNet.Instance;
            var nm = NetworkManager.Singleton;
            int look = mn != null && nm != null ? mn.LookOf(nm.LocalClientId) : -1;
            if (look < 0 && mn != null) look = mn.LocalLook;
            return look < 0 ? 1 : AirsoftLook.Unpack(look).eyewear;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 d = transform.position - lastPos; d.y = 0f;
            lastPos = transform.position;

            bool dead = health != null && health.IsSpawned && health.isDead.Value;
            int eyewear = LocalEyewear();
            var mn = MatchNet.Instance;
            float rate = mn != null && mn.FogEnabled ? 1f : 0f;   // match rule: everyone or nobody
            if (dead || rate <= 0f)
            {
                if (maxFog > 0f) Clear();   // a fresh, clean lens after the respawn
                StopWipe();
                Apply(eyewear);
                return;
            }

            float speed = dt > 0f ? d.magnitude / dt : 0f;
            bool sprint = fps != null && fps.IsSprinting;
            float stamina = fps != null ? fps.Stamina : 1f;
            float gain = 0f;
            if (sprint) gain += GAIN_SPRINT;
            else if (stamina < 0.7f) gain += GAIN_PANT * (1f - stamina);
            var cam = fps != null && fps.playerCamera != null ? fps.playerCamera.transform : transform;
            if (SmokeField.Contains(cam.position)) gain += GAIN_SMOKE;
            float decay = !sprint && speed > 1.5f ? DECAY_MOVING : DECAY_IDLE;
            Grow((gain * rate - decay) * dt);   // the whole lens at once: wiped spots fog up again like the rest

            // wipe (F)
            var kb = Keyboard.current;
            if (wipeT < 0f && kb != null && kb.fKey.wasPressedThisFrame && !HUDManager.InputBlocked
                && Time.time >= nextWipe && Level > 0.03f)
            {
                wipeT = 0f;
                Wiping = true;
                palmDown = false;
                System.Array.Copy(field, atWipe, field.Length);
                PlayWipeSound();
            }
            if (wipeT >= 0f)
            {
                wipeT += dt;
                AnimateHand(wipeT / WIPE_SECONDS);
                if (wipeT >= WIPE_SECONDS)
                {
                    MatchNet.Instance?.SendGoggleWipe();   // the Clear Eyes award goes to players who never wipe
                    StopWipe();
                    nextWipe = Time.time + WIPE_COOLDOWN;
                }
            }

            Apply(eyewear);
        }

        void StopWipe()
        {
            wipeT = -1f;
            Wiping = false;
            palmDown = false;
            if (hand != null && hand.gameObject.activeSelf) hand.gameObject.SetActive(false);
        }

        // ───────── fog field ─────────
        void Grow(float delta)
        {
            if (Mathf.Abs(delta) < 1e-6f || (delta < 0f && maxFog <= 0f)) return;
            float sum = 0f, max = 0f;
            for (int i = 0; i < field.Length; i++)
            {
                float f = Mathf.Clamp01(field[i] + delta);
                field[i] = f;
                sum += f;
                if (f > max) max = f;
            }
            Level = sum / field.Length;
            maxFog = max;
            texDirty = true;
        }

        void Clear()
        {
            System.Array.Clear(field, 0, field.Length);
            Level = maxFog = 0f;
            texDirty = true;
        }

        // The palm moved from a to b (screen fractions 0..1): clear what it passed over, with soft edges.
        void WipeAlong(Vector2 a, Vector2 b)
        {
            float aspect = canvasRt != null && canvasRt.rect.height > 0f ? canvasRt.rect.width / canvasRt.rect.height : 16f / 9f;
            float rx = PALM_RX * (16f / 9f) / aspect;   // the palm keeps its shape on any screen
            int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / (rx * 0.25f)));
            for (int k = 1; k <= steps; k++)
            {
                Vector2 c = Vector2.Lerp(a, b, k / (float)steps);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - rx) * FW)), x1 = Mathf.Min(FW - 1, Mathf.CeilToInt((c.x + rx) * FW));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((c.y - PALM_RY) * FH)), y1 = Mathf.Min(FH - 1, Mathf.CeilToInt((c.y + PALM_RY) * FH));
                for (int y = y0; y <= y1; y++)
                {
                    float dy = ((y + 0.5f) / FH - c.y) / PALM_RY;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = ((x + 0.5f) / FW - c.x) / rx;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r >= 1f) continue;
                        float press = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, r));   // full under the palm
                        int i = y * FW + x;
                        field[i] = Mathf.Min(field[i], Mathf.Lerp(atWipe[i], atWipe[i] * WIPE_LEFT, press));
                    }
                }
            }
            float sum = 0f, max = 0f;
            foreach (var f in field) { sum += f; if (f > max) max = f; }
            Level = sum / field.Length;
            maxFog = max;
            texDirty = true;
        }

        // ───────── the hand ─────────
        // Keyframes (time share of the wipe, palm centre in screen fractions). The palm sits in the lower
        // part of the hand sprite; the fingers reach up past it.
        static readonly (float t, Vector2 p, float rot)[] Path =
        {
            (0.00f, new Vector2(-0.22f, -0.45f), 30f),   // below the screen, bottom left
            (0.14f, new Vector2(0.10f, 0.70f), 14f),     // up onto the top left of the lens
            (0.44f, new Vector2(0.90f, 0.73f), 4f),      // stroke 1: across the top
            (0.55f, new Vector2(0.88f, 0.30f), 10f),     // down the right side
            (0.84f, new Vector2(0.10f, 0.27f), 22f),     // stroke 2: back across the bottom
            (1.00f, new Vector2(-0.25f, -0.55f), 34f),   // away, down left
        };

        void AnimateHand(float u)
        {
            if (hand == null) return;
            u = Mathf.Clamp01(u);
            int k = 0;
            while (k < Path.Length - 2 && u > Path[k + 1].t) k++;
            var (t0, p0, r0) = Path[k];
            var (t1, p1, r1) = Path[k + 1];
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t0, t1, u));
            Vector2 palm = Vector2.Lerp(p0, p1, s);
            // a little press-and-rub wobble while it's on the lens
            bool onLens = u > 0.1f && u < 0.9f;
            if (onLens) palm += new Vector2(0f, Mathf.Sin(u * 38f) * 0.012f);
            float rot = Mathf.Lerp(r0, r1, s) + (onLens ? Mathf.Sin(u * 23f) * 2.5f : 0f);

            if (!hand.gameObject.activeSelf) hand.gameObject.SetActive(true);
            var size = canvasRt.rect.size;
            float hH = size.y * HAND_H;
            hand.sizeDelta = new Vector2(hH * HandAspect, hH);
            // the sprite's palm centre is at 38 % of its height: put that on the palm point
            hand.localRotation = Quaternion.Euler(0f, 0f, -rot);
            Vector2 palmOffset = (Vector2)(hand.localRotation * new Vector3(0f, hH * (0.5f - 0.38f), 0f));
            hand.anchoredPosition = new Vector2(palm.x * size.x, palm.y * size.y) + palmOffset;

            if (onLens)
            {
                if (palmDown) WipeAlong(lastPalm, palm);
                lastPalm = palm;
                palmDown = true;
            }
            else palmDown = false;
        }

        // ───────── drawing ─────────
        void Apply(int eyewear)
        {
            if (canvas == null) return;
            bool show = (maxFog > 0.005f || wipeT >= 0f) && !MatchIntro.Flying;   // the drone has no goggles
            if (canvas.enabled != show) canvas.enabled = show;
            if (show && texDirty) UpdateTextures();

            bool wantPrompt = Level >= PROMPT_AT && wipeT < 0f && !HUDManager.InputBlocked;
            if (prompt.activeSelf != wantPrompt) prompt.SetActive(wantPrompt);
            if (wantPrompt) promptText.text = "WIPE " + LensName(eyewear);
        }

        // Fog texels -> alpha of the two lens layers (rim and haze), each over its own blotchy pattern.
        void UpdateTextures()
        {
            texDirty = false;
            var eb = FogBase(true);
            var hb = FogBase(false);
            for (int i = 0; i < field.Length; i++)
            {
                float f = field[i];
                float edgeA = Mathf.Clamp01(f * 1.25f) * 0.9f;
                float hazeA = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, f)) * 0.6f;
                edgePx[i].a = (byte)(eb[i] * edgeA * 255f);
                hazePx[i].a = (byte)(hb[i] * hazeA * 255f);
            }
            edgeTex.SetPixels32(edgePx);
            edgeTex.Apply(false);
            hazeTex.SetPixels32(hazePx);
            hazeTex.Apply(false);
        }

        void Build()
        {
            canvas = UIKit.Canvas("GoggleFogCanvas", 9);   // under the HUD (10): fog covers the world, not the HUD
            canvas.enabled = false;
            canvasRt = (RectTransform)canvas.transform;

            // the hand is behind the fog (the fog is on the lens, the glove outside it): what it wipes clears
            handImg = Box(canvas.transform, "WipeHand", Color.white);
            handImg.sprite = HandSprite();
            handImg.raycastTarget = false;
            hand = handImg.rectTransform;
            hand.anchorMin = hand.anchorMax = Vector2.zero;
            hand.pivot = new Vector2(0.5f, 0.5f);
            hand.gameObject.SetActive(false);

            haze = FogLayer("FogHaze", out hazeTex, out hazePx);
            edge = FogLayer("FogEdge", out edgeTex, out edgePx);

            var pr = HRow(canvas.transform, "WipePrompt", 8, TextAnchor.MiddleCenter);
            pr.padding = new RectOffset(10, 12, 5, 5);
            pr.gameObject.AddComponent<Image>().color = Scrim(0.82f);
            var fit = pr.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var prt = pr.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0, -130);
            KeyCap(pr.transform, "F", 12);
            promptText = Label(pr.transform, "WIPE GOGGLES", F.BodyBold, 13, Fg, TextAlignmentOptions.MidlineLeft, 0.08f);
            prompt = pr.gameObject;
            prompt.SetActive(false);
        }

        Image FogLayer(string name, out Texture2D tex, out Color32[] px)
        {
            tex = new Texture2D(FW, FH, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            px = new Color32[FW * FH];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(232, 237, 240, 0);
            tex.SetPixels32(px);
            tex.Apply(false);
            var img = Box(canvas.transform, name, Color.white);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, FW, FH), new Vector2(0.5f, 0.5f), 100f);
            img.sprite.hideFlags = HideFlags.DontSave;
            img.raycastTarget = false;
            img.rectTransform.Fill();
            return img;
        }

        static float[] s_edgeBase, s_hazeBase;

        // Milky lens pattern (0..1 per texel): blotchy noise, thicker towards the rim (edge) or all over (haze).
        static float[] FogBase(bool edgeLayer)
        {
            var cached = edgeLayer ? s_edgeBase : s_hazeBase;
            if (cached != null) return cached;
            var a = new float[FW * FH];
            float seed = edgeLayer ? 13.7f : 71.3f;
            float k = 256f / FW;   // same blotch size as the old 256 px textures
            for (int y = 0; y < FH; y++)
                for (int x = 0; x < FW; x++)
                {
                    float nx = (x + 0.5f) / FW * 2f - 1f, ny = (y + 0.5f) / FH * 2f - 1f;
                    float n = Mathf.PerlinNoise(seed + x * 0.03f * k, seed + y * 0.03f * k) * 0.6f
                            + Mathf.PerlinNoise(seed * 2f + x * 0.12f * k, y * 0.12f * k) * 0.4f;
                    float v;
                    if (edgeLayer)
                    {
                        float r = Mathf.Sqrt(nx * nx * 0.85f + ny * ny) / 1.36f;
                        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.95f, r));
                        v = Mathf.Clamp01(rim * 1.25f * (0.45f + 0.8f * n));
                    }
                    else v = Mathf.Clamp01(0.7f + 0.45f * (n - 0.5f));
                    a[y * FW + x] = v;
                }
            if (edgeLayer) s_edgeBase = a; else s_hazeBase = a;
            return a;
        }

        // ───────── the glove (drawn here, no asset) ─────────
        const int HW = 200, HH = 256;
        const float HandAspect = HW / (float)HH;
        static Sprite s_hand;

        // A left tactical glove seen from inside the goggles: palm towards you, fingers up, thumb on the right.
        // Signed-distance shapes, softened a lot (it's a couple of centimetres from your eye).
        static Sprite HandSprite()
        {
            if (s_hand != null) return s_hand;
            var t = new Texture2D(HW, HH, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            var px = new Color32[HW * HH];
            var glove = new Color(0.22f, 0.235f, 0.20f);
            var suede = new Color(0.36f, 0.33f, 0.27f);
            var cuff = new Color(0.15f, 0.16f, 0.14f);
            for (int y = 0; y < HH; y++)
                for (int x = 0; x < HW; x++)
                {
                    // local coords: x -1..1 across, y -1.28..1.28 up (square units)
                    Vector2 p = new Vector2((x + 0.5f) / HW * 2f - 1f, ((y + 0.5f) / HH * 2f - 1f) * (HH / (float)HW));
                    float palm = RoundBox(p - new Vector2(0f, -0.38f), new Vector2(0.50f, 0.46f), 0.22f);
                    float pinky = Capsule(p, new Vector2(-0.44f, 0.02f), new Vector2(-0.52f, 0.52f), 0.135f);
                    float ring = Capsule(p, new Vector2(-0.16f, 0.06f), new Vector2(-0.19f, 0.86f), 0.145f);
                    float middle = Capsule(p, new Vector2(0.13f, 0.06f), new Vector2(0.14f, 0.97f), 0.15f);
                    float index = Capsule(p, new Vector2(0.41f, 0.02f), new Vector2(0.47f, 0.80f), 0.145f);
                    float thumb = Capsule(p, new Vector2(0.42f, -0.62f), new Vector2(0.86f, -0.08f), 0.16f);
                    float fingers = Mathf.Min(Mathf.Min(pinky, ring), Mathf.Min(middle, index));
                    float wrist = RoundBox(p - new Vector2(0.02f, -1.02f), new Vector2(0.44f, 0.26f), 0.08f);
                    float body = SMin(SMin(palm, fingers, 0.08f), thumb, 0.1f);
                    float all = Mathf.Min(body, wrist);

                    float alpha = 1f - Smooth(-0.035f, 0.035f, all);
                    if (alpha <= 0.001f) { px[y * HW + x] = new Color32(0, 0, 0, 0); continue; }

                    Color c = wrist < body - 0.01f ? cuff : glove;
                    // suede palm patch and finger-tip pads
                    float patch = RoundBox(p - new Vector2(0f, -0.38f), new Vector2(0.38f, 0.32f), 0.2f);
                    if (patch < 0f && wrist >= body) c = Color.Lerp(c, suede, 1f - Smooth(-0.04f, 0f, patch));
                    foreach (var tip in new[] { new Vector2(-0.52f, 0.46f), new Vector2(-0.19f, 0.80f), new Vector2(0.14f, 0.91f), new Vector2(0.47f, 0.74f), new Vector2(0.84f, -0.12f) })
                    {
                        float td = (p - tip).magnitude - 0.11f;
                        if (td < 0f) c = Color.Lerp(c, suede, 0.75f * (1f - Smooth(-0.03f, 0f, td)));
                    }
                    // finger creases
                    foreach (float cy in new[] { 0.30f, 0.56f })
                        if (fingers < 0f && Mathf.Abs(p.y - cy - p.x * 0.05f) < 0.018f) c *= 0.72f;
                    // velcro strap on the cuff
                    if (wrist < 0f && Mathf.Abs(p.y + 0.98f) < 0.09f) c = Color.Lerp(c, new Color(0.10f, 0.105f, 0.095f), 0.8f);
                    // roundness: darker at the silhouette, light from above
                    float inner = Smooth(0f, 0.22f, -all);
                    c *= Mathf.Lerp(0.55f, 1f, inner) * Mathf.Lerp(0.85f, 1.08f, Mathf.InverseLerp(-1.2f, 1.1f, p.y));
                    c.a = alpha;
                    px[y * HW + x] = c;
                }
            t.SetPixels32(px);
            t.Apply(false);
            s_hand = Sprite.Create(t, new Rect(0, 0, HW, HH), new Vector2(0.5f, 0.5f), 100f);
            s_hand.hideFlags = HideFlags.DontSave;
            return s_hand;
        }

        // GLSL-style smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead).
        static float Smooth(float e0, float e1, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e0, e1, x));

        static float RoundBox(Vector2 p, Vector2 half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float SMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        // ───────── sound: cloth rubbing over the lens, two strokes (made here, no asset) ─────────
        void PlayWipeSound()
        {
            if (s_wipeClip == null) s_wipeClip = MakeWipeClip();
            AirsoftAudio.Hub?.Play(s_wipeClip, Vector3.zero, false, 0.55f, Random.Range(0.94f, 1.06f), AirsoftAudio.Bus.Sfx, 1f, 2f);
        }

        static AudioClip MakeWipeClip()
        {
            const int rate = 44100;
            int n = (int)(rate * WIPE_SECONDS);
            var data = new float[n];
            var rng = new System.Random(99);
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                // loud while a stroke is on the lens (the Path timing), quiet at the turn
                float env = Stroke(u, 0.14f, 0.44f) + 0.6f * Stroke(u, 0.44f, 0.55f) + Stroke(u, 0.55f, 0.84f);
                float grain = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(u * 140f));   // fabric catching on the lens
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.35f;              // band-pass: soft low-pass ...
                hp = 0.97f * (hp + lp - prev);           // ... minus the rumble
                prev = lp;
                data[i] = hp * env * grain * 0.9f;
            }
            var clip = AudioClip.Create("GoggleWipe", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Stroke(float u, float a, float b)
        {
            if (u < a || u > b) return 0f;
            float x = (u - a) / (b - a);
            return Mathf.Sin(x * Mathf.PI);
        }
    }
}
