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
    // F wipes it: a sweep across the lens for WIPE_SECONDS, during which you can't aim or shoot.
    //
    // It's a match rule the host picks in the lobby (MatchNet.FogEnabled): either everyone's lens fogs, at the
    // same rate whatever eyewear they picked (so eyewear stays cosmetic), or nobody's. Added to the owner's
    // player object by FPSController.
    [DisallowMultipleComponent]
    public class GoggleFog : MonoBehaviour
    {
        // Owner: a wipe is in progress (WeaponController: no aiming / shooting meanwhile).
        public static bool Wiping { get; private set; }
        // 0 = clear, 1 = fully fogged.
        public static float Level { get; private set; }

        const float WIPE_SECONDS = 0.7f;
        const float WIPE_COOLDOWN = 1.2f;
        const float WIPE_LEFT = 0.12f;       // a wipe leaves a few smears
        const float PROMPT_AT = 0.3f;        // show the "F  WIPE GOGGLES" prompt from this fog level

        // per second
        const float GAIN_SPRINT = 0.055f;
        const float GAIN_PANT = 0.03f;       // out of breath: scaled by the missing stamina
        const float GAIN_SMOKE = 0.12f;
        const float DECAY_MOVING = 0.02f;    // walking: air through the vents
        const float DECAY_IDLE = 0.008f;

        public static string LensName(int eyewear) => eyewear == 3 ? "MASK" : eyewear == 2 ? "GLASSES" : "GOGGLES";

        FPSController fps;
        Health health;
        Canvas canvas;
        Image edge, haze;
        GameObject prompt;
        TextMeshProUGUI promptText;
        float fog, wipeT = -1f, nextWipe;
        Vector3 lastPos;

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
                fog = 0f;   // a fresh, clean lens after the respawn
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
            fog = Mathf.Clamp01(fog + (gain * rate - decay) * dt);

            // wipe (F)
            var kb = Keyboard.current;
            if (wipeT < 0f && kb != null && kb.fKey.wasPressedThisFrame && !HUDManager.InputBlocked
                && Time.time >= nextWipe && fog > 0.03f)
            {
                wipeT = 0f;
                Wiping = true;
            }
            if (wipeT >= 0f)
            {
                wipeT += dt;
                if (wipeT >= WIPE_SECONDS)
                {
                    fog *= WIPE_LEFT;
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
        }

        void Apply(int eyewear)
        {
            Level = fog;
            if (canvas == null) return;
            bool show = fog > 0.005f;
            if (canvas.enabled != show) canvas.enabled = show;
            if (!show) return;

            float edgeA = Mathf.Clamp01(fog * 1.25f) * 0.9f;
            float hazeA = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, fog)) * 0.6f;
            float fill = wipeT >= 0f ? 1f - Mathf.SmoothStep(0f, 1f, wipeT / WIPE_SECONDS) : 1f;
            edge.color = new Color(1f, 1f, 1f, edgeA);
            haze.color = new Color(1f, 1f, 1f, hazeA);
            edge.fillAmount = haze.fillAmount = fill;

            bool wantPrompt = fog >= PROMPT_AT && wipeT < 0f && !HUDManager.InputBlocked;
            if (prompt.activeSelf != wantPrompt) prompt.SetActive(wantPrompt);
            if (wantPrompt) promptText.text = "WIPE " + LensName(eyewear);
        }

        // ───────── visuals ─────────
        void Build()
        {
            canvas = UIKit.Canvas("GoggleFogCanvas", 9);   // under the HUD (10): fog covers the world, not the HUD
            canvas.enabled = false;

            haze = FogLayer("FogHaze", FogSprite(false));
            edge = FogLayer("FogEdge", FogSprite(true));

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

        Image FogLayer(string name, Sprite sprite)
        {
            var img = Box(canvas.transform, name, Color.white);
            img.sprite = sprite;
            img.rectTransform.Fill();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Right;   // the wipe sweeps left to right
            img.fillAmount = 1f;
            return img;
        }

        static Sprite s_edge, s_haze;

        // Milky lens texture: blotchy noise, thicker towards the rim (edge) or all over (haze).
        static Sprite FogSprite(bool edgeLayer)
        {
            var cached = edgeLayer ? s_edge : s_haze;
            if (cached != null) return cached;
            const int W = 256, H = 144;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            var px = new Color32[W * H];
            float seed = edgeLayer ? 13.7f : 71.3f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float nx = (x + 0.5f) / W * 2f - 1f, ny = (y + 0.5f) / H * 2f - 1f;
                    float n = Mathf.PerlinNoise(seed + x * 0.03f, seed + y * 0.03f) * 0.6f
                            + Mathf.PerlinNoise(seed * 2f + x * 0.12f, y * 0.12f) * 0.4f;
                    float a;
                    if (edgeLayer)
                    {
                        float r = Mathf.Sqrt(nx * nx * 0.85f + ny * ny) / 1.36f;
                        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.95f, r));
                        a = Mathf.Clamp01(rim * 1.25f * (0.45f + 0.8f * n));
                    }
                    else a = Mathf.Clamp01(0.7f + 0.45f * (n - 0.5f));
                    px[y * W + x] = new Color32(232, 237, 240, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.DontSave;
            if (edgeLayer) s_edge = s; else s_haze = s;
            return s;
        }
    }
}
