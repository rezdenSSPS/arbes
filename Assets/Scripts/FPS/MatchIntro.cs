using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Match-start intro, on every peer. While the server holds everyone frozen (MatchNet.IntroFrozen, until
    // MatchNet.MatchGoTime) a "drone" camera shows the map: three slow orbiting shots over the places the
    // mode is about (bases, control points, bomb sites, VIP route), cut with quick dips to black, then it
    // swoops down into the local player's eyes. A big 3-2-1 follows, then GO and everyone can move.
    // The timeline is read from the server clock, so all players see the same moment; a client that
    // loads late just joins the intro where it is (or only gets the countdown). Added by HUDManager.
    public class MatchIntro : MonoBehaviour
    {
        const int SHOTS = 3;
        const float SHOT = 1.8f;                                   // seconds per drone shot
        const float SWOOP = MatchNet.INTRO_FLY - SHOTS * SHOT;    // into the player's eyes (1.6 s)
        const float DIP = 0.2f;                                    // dip-to-black at a cut, each side
        const float DRONE_FOV = 58f;
        const float BAR_H = 64f;                                   // letterbox bars, reference px

        // The drone camera owns the screen (HUD and goggle fog step aside).
        public static bool Flying { get; private set; }

        struct Shot { public Vector3 target, fromPos, toPos; }

        readonly List<Shot> shots = new List<Shot>();
        Camera cam, playerCam;
        FPSController fps;
        double runFor;              // MatchGoTime the current intro belongs to
        float swoopFromFov;
        Vector3 swoopFrom;
        Quaternion swoopFromRot;
        bool swoopReady;
        int lastCount = -1;
        float goShownAt = -10f;
        AudioSource droneVoice;
        static AudioClip s_droneClip;

        // UI
        RectTransform overlay;
        Image topBar, bottomBar, fade, recDot;
        TextMeshProUGUI caption, title, goal, countText, recText;
        CanvasGroup info, titleGroup;

        public void Build(Transform root)
        {
            overlay = Rect(root, "MatchIntro").Fill();
            overlay.SetSiblingIndex(1);   // right above the HUD, under death / pause / results screens
            fade = Box(overlay, "Fade", Color.black);
            fade.rectTransform.Fill();
            topBar = Box(overlay, "BarTop", Color.black);
            topBar.rectTransform.anchorMin = new Vector2(0, 1); topBar.rectTransform.anchorMax = new Vector2(1, 1);
            topBar.rectTransform.pivot = new Vector2(0.5f, 1); topBar.rectTransform.sizeDelta = new Vector2(0, BAR_H);
            bottomBar = Box(overlay, "BarBottom", Color.black);
            bottomBar.rectTransform.anchorMin = new Vector2(0, 0); bottomBar.rectTransform.anchorMax = new Vector2(1, 0);
            bottomBar.rectTransform.pivot = new Vector2(0.5f, 0); bottomBar.rectTransform.sizeDelta = new Vector2(0, BAR_H);

            // drone feed tag, top-left inside the picture
            var recRt = Rect(overlay, "Rec").At(0, 1, 40, -BAR_H - 18, 360, 20);
            info = recRt.gameObject.AddComponent<CanvasGroup>();
            recDot = Box(recRt, "Dot", Danger);
            recDot.rectTransform.At(0, 0.5f, 0, 0, 10, 10);
            recText = Label(recRt, "DRONE FEED", F.MonoBold, 13, Fg, TextAlignmentOptions.MidlineLeft, 0.12f);
            recText.rectTransform.Fill(18);

            // map + mode title, bottom-left above the bar
            var block = Rect(overlay, "Title").At(0, 0, 40, BAR_H + 22, 760, 110);
            titleGroup = block.gameObject.AddComponent<CanvasGroup>();
            var stripe = Box(block, "Stripe", Accent);
            stripe.rectTransform.TL(0, 8, 6, 64);
            caption = Label(block, "", F.MonoBold, 13, Accent, TextAlignmentOptions.MidlineLeft, 0.14f);
            caption.rectTransform.TL(20, 0, 700, 18);
            title = Label(block, "", F.CondBlack, 52, Fg, TextAlignmentOptions.MidlineLeft, 0.02f);
            title.rectTransform.TL(18, 16, 740, 58);
            goal = Label(block, "", F.Body, 16, TextSoft, TextAlignmentOptions.MidlineLeft);
            goal.rectTransform.TL(20, 76, 700, 22);

            countText = Label(overlay, "", F.CondBlack, 150, Fg, TextAlignmentOptions.Center);
            countText.rectTransform.At(0.5f, 0.5f, 0, 40, 600, 220);
            countText.outlineWidth = 0.12f;
            countText.outlineColor = new Color32(0, 0, 0, 160);

            overlay.gameObject.SetActive(false);
        }

        void Update()
        {
            var mn = MatchNet.Instance;
            var nm = NetworkManager.Singleton;
            if (mn == null || nm == null || !nm.IsListening || mn.CurrentPhase != MatchNet.Phase.InMatch
                || mn.MatchGoTime <= 0 || mn.IntroLength <= 0f || !FindLocalPlayer(nm))
            {
                StopFlying();
                if (overlay != null && overlay.gameObject.activeSelf) overlay.gameObject.SetActive(false);
                return;
            }

            float left = mn.IntroLeft;
            float elapsed = mn.IntroLength - left;

            // a new intro (new match): plan the shots once, if there is still time to fly
            if (runFor != mn.MatchGoTime)
            {
                runFor = mn.MatchGoTime;
                lastCount = -1;
                goShownAt = -10f;
                swoopReady = false;
                shots.Clear();
                if (elapsed < MatchNet.INTRO_FLY - 0.4f) PlanShots(mn);
                SetTitles(mn);
            }

            bool flyNow = shots.Count > 0 && elapsed < MatchNet.INTRO_FLY && left > 0f;
            if (flyNow) Fly(elapsed);
            else StopFlying();

            // 3-2-1 once the camera is back in the player's eyes, then GO
            int count = left > 0f && left <= MatchNet.INTRO_COUNT ? Mathf.CeilToInt(left) : 0;
            if (count != lastCount)
            {
                if (count > 0 && !(ObjectiveMode.Active is RoundMode))   // round modes beep their own PREP 3-2-1
                    AirsoftAudio.Play2D("countdown", 0.45f);
                if (count == 0 && lastCount > 0)
                {
                    goShownAt = Time.unscaledTime;
                    if (!(ObjectiveMode.Active is RoundMode)) AirsoftAudio.Play2D("countdown_go", 0.35f);
                }
                lastCount = count;
            }

            float goAge = Time.unscaledTime - goShownAt;
            bool showGo = goAge < 0.8f;
            bool any = flyNow || count > 0 || showGo;
            overlay.gameObject.SetActive(any);
            if (!any) return;

            // letterbox: in with the drone, out during the swoop
            float bars = flyNow ? Mathf.Clamp01(Mathf.Min(elapsed / 0.4f, (MatchNet.INTRO_FLY - elapsed) / (SWOOP * 0.8f))) : 0f;
            bars = Mathf.SmoothStep(0f, 1f, bars);
            topBar.rectTransform.sizeDelta = new Vector2(0, BAR_H * bars);
            bottomBar.rectTransform.sizeDelta = new Vector2(0, BAR_H * bars);

            // dips to black at the cuts (and a soft fade in at the very start)
            float a = 0f;
            if (flyNow)
            {
                a = Mathf.Clamp01(1f - elapsed / 0.35f);
                for (int i = 1; i <= SHOTS; i++)
                {
                    float d = Mathf.Abs(elapsed - i * SHOT);
                    a = Mathf.Max(a, 1f - d / DIP);
                }
            }
            fade.color = new Color(0, 0, 0, Mathf.Clamp01(a));

            float infoA = flyNow ? Mathf.Clamp01(Mathf.Min((elapsed - 0.3f) / 0.4f, (SHOTS * SHOT - elapsed) / 0.4f)) : 0f;
            info.alpha = infoA;
            titleGroup.alpha = infoA;
            recDot.enabled = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;

            if (count > 0)
            {
                float frac = count - left;   // 0 when the number appears .. 1 when the next one takes over
                float pop = 1f + 0.35f * Mathf.Pow(1f - Mathf.Clamp01(frac * 4f), 2f);
                countText.text = count.ToString();
                countText.color = A(count == 1 ? Accent : Fg, Mathf.Clamp01(Mathf.Min(frac * 6f, (1f - frac) * 5f + 0.25f)));
                countText.rectTransform.localScale = Vector3.one * pop;
            }
            else if (showGo)
            {
                countText.text = "GO";
                countText.color = A(Accent, 1f - Mathf.Clamp01((goAge - 0.4f) / 0.4f));
                countText.rectTransform.localScale = Vector3.one * (1f + 0.25f * goAge);
            }
            else countText.text = "";
        }

        bool FindLocalPlayer(NetworkManager nm)
        {
            if (fps != null && playerCam != null) return true;
            var po = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po == null) return false;
            fps = po.GetComponent<FPSController>();
            playerCam = fps != null ? fps.playerCamera : null;
            return playerCam != null;
        }

        // ───────── drone shots ─────────
        void PlanShots(MatchNet mn)
        {
            var layout = MapLayout.Current;
            var focus = new List<Vector3>();
            var all = new List<Vector3>();
            void AddT(List<Vector3> l, Transform t) { if (t != null) l.Add(t.position); }
            if (layout != null)
            {
                switch (mn.Mode)
                {
                    case MatchNet.GameModeType.CaptureTheFlag:
                        AddT(focus, layout.redBase); AddT(focus, layout.blueBase); break;
                    case MatchNet.GameModeType.Domination:
                        foreach (var t in layout.controlPoints) AddT(focus, t); break;
                    case MatchNet.GameModeType.Bomb:
                        foreach (var t in layout.bombSites) AddT(focus, t); break;
                    case MatchNet.GameModeType.VipEscort:
                        AddT(focus, layout.vipStart); AddT(focus, layout.vipExtract); break;
                }
                AddT(all, layout.redBase); AddT(all, layout.blueBase);
                foreach (var t in layout.controlPoints) AddT(all, t);
                foreach (var t in layout.bombSites) AddT(all, t);
            }
            foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude)) all.Add(sp.transform.position);
            if (all.Count == 0 && focus.Count == 0) return;

            Vector3 center = Vector3.zero;
            foreach (var p in all) center += p;
            foreach (var p in focus) center += p;
            center /= all.Count + focus.Count;

            // the mode's places first, then whatever lies farthest from what we've shown (a spread of the map)
            var pick = new List<Vector3>();
            foreach (var p in focus) if (pick.Count < SHOTS) pick.Add(p);
            while (pick.Count < SHOTS && all.Count > 0)
            {
                Vector3 best = all[0]; float bestD = -1f;
                foreach (var p in all)
                {
                    float d = pick.Count == 0 ? Random.value : float.MaxValue;
                    foreach (var q in pick) d = Mathf.Min(d, (p - q).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = p; }
                }
                if (pick.Count > 0 && bestD < 9f) pick.Add(center);   // only one area: show the middle too
                else pick.Add(best);
            }

            float side = Random.value < 0.5f ? 1f : -1f;
            for (int i = 0; i < pick.Count; i++)
            {
                Vector3 target = pick[i] + Vector3.up * 1.2f;
                Vector3 outward = pick[i] - center; outward.y = 0f;
                float baseAng = outward.sqrMagnitude > 1f ? Mathf.Atan2(outward.z, outward.x) * Mathf.Rad2Deg : Random.Range(0f, 360f);
                shots.Add(PlanOrbit(target, baseAng, side));
                side = -side;
            }
        }

        // Orbit a point: try headings around it until both ends of the move have a clear view.
        static Shot PlanOrbit(Vector3 target, float baseAng, float side)
        {
            const float sweep = 28f;
            for (int k = 0; k < 12; k++)
            {
                float ang = baseAng + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 30f;
                float lift = k < 8 ? 0f : 6f;
                Vector3 from = OrbitPos(target, ang, 17f, 9f + lift);
                Vector3 to = OrbitPos(target, ang + sweep * side, 13.5f, 7f + lift);
                if (Clear(target, from) && Clear(target, to) && Clear(from, to))
                    return new Shot { target = target, fromPos = from, toPos = to };
            }
            // nothing clear (deep in a building): look straight down from high above
            return new Shot { target = target, fromPos = target + new Vector3(0.5f, 24f, -3f), toPos = target + new Vector3(0.3f, 18f, -2f) };
        }

        static Vector3 OrbitPos(Vector3 c, float angDeg, float r, float h)
        {
            float a = angDeg * Mathf.Deg2Rad;
            return c + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
        }

        static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            return len < 0.01f || !Physics.SphereCast(a, 0.35f, d / len, out _, len, ~0, QueryTriggerInteraction.Ignore);
        }

        void Fly(float elapsed)
        {
            if (cam == null) StartFlying();
            float flyT = SHOTS * SHOT;
            Vector3 eye = playerCam.transform.position;
            Quaternion eyeRot = playerCam.transform.rotation;
            float eyeFov = playerCam.fieldOfView;

            if (elapsed < flyT)
            {
                int i = Mathf.Clamp(Mathf.FloorToInt(elapsed / SHOT), 0, shots.Count - 1);
                float u = Mathf.Clamp01((elapsed - i * SHOT) / SHOT);
                var s = shots[i];
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Lerp(0.15f, 0.85f, u));   // keeps moving through the cut
                Vector3 pos = Vector3.Lerp(s.fromPos, s.toPos, e);
                // hovering drone: a little drift and wobble
                float t = Time.unscaledTime;
                pos += new Vector3(Mathf.PerlinNoise(t * 0.7f, 1.3f) - 0.5f, Mathf.PerlinNoise(t * 0.6f, 4.1f) - 0.5f, Mathf.PerlinNoise(t * 0.8f, 7.7f) - 0.5f) * 0.25f;
                Quaternion rot = Quaternion.LookRotation(s.target - pos);
                rot *= Quaternion.Euler((Mathf.PerlinNoise(t * 1.1f, 2.2f) - 0.5f) * 1.2f, 0f, (Mathf.PerlinNoise(t * 0.9f, 5.5f) - 0.5f) * 2.5f);
                cam.transform.SetPositionAndRotation(pos, rot);
                cam.fieldOfView = DRONE_FOV;
                swoopReady = false;
            }
            else
            {
                // swoop: from above and behind the player down into their eyes
                if (!swoopReady)
                {
                    swoopReady = true;
                    Vector3 back = -playerCam.transform.forward; back.y = 0f;
                    back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
                    Vector3 off = back * 9f + Vector3.up * 7f;
                    float len = off.magnitude;
                    if (Physics.SphereCast(eye, 0.3f, off / len, out RaycastHit hit, len, ~0, QueryTriggerInteraction.Ignore))
                        off = off / len * Mathf.Max(1.5f, hit.distance - 0.4f);
                    if (off.magnitude < 3f) off = Vector3.up * 12f;   // boxed in: drop in from straight above
                    swoopFrom = eye + off;
                    swoopFromRot = Quaternion.LookRotation(eye + playerCam.transform.forward * 6f - swoopFrom);
                    swoopFromFov = DRONE_FOV + 6f;
                }
                float u = Mathf.Clamp01((elapsed - flyT) / SWOOP);
                float e = Mathf.SmoothStep(0f, 1f, u);
                float ep = 1f - Mathf.Pow(1f - u, 3f);   // position eases out: fast start, soft landing
                Vector3 pos = Vector3.Lerp(swoopFrom, eye, ep);
                pos += Vector3.up * Mathf.Sin(u * Mathf.PI) * 1.2f;   // a little arc
                cam.transform.SetPositionAndRotation(pos, Quaternion.Slerp(swoopFromRot, eyeRot, e));
                cam.fieldOfView = Mathf.Lerp(swoopFromFov, eyeFov, e);
            }
        }

        void StartFlying()
        {
            var go = new GameObject("IntroDroneCam");
            cam = go.AddComponent<Camera>();
            cam.CopyFrom(playerCam);
            cam.depth = playerCam.depth + 1;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 400f);
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = GameSettings.PostProcessing;
            urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            playerCam.enabled = false;   // its AudioListener keeps working
            Flying = true;

            if (s_droneClip == null) s_droneClip = MakeDroneClip();
            droneVoice = AirsoftAudio.Hub?.Play(s_droneClip, Vector3.zero, false, 0.32f, Random.Range(0.96f, 1.04f), AirsoftAudio.Bus.Sfx, 1f, 2f);
        }

        void StopFlying()
        {
            if (cam != null) Destroy(cam.gameObject);
            cam = null;
            if (playerCam != null && !playerCam.enabled) playerCam.enabled = true;
            if (droneVoice != null && droneVoice.isPlaying && droneVoice.clip == s_droneClip && Flying) droneVoice.Stop();
            droneVoice = null;
            Flying = false;
        }

        void OnDestroy() => StopFlying();

        void SetTitles(MatchNet mn)
        {
            int mi = MapCatalog.IndexOfScene(mn.MatchScene);
            string map = mi >= 0 ? MapCatalog.NameOf(mi) : mn.MatchScene;
            caption.text = "DRONE RECON  ·  " + map.ToUpperInvariant();
            title.text = ModeName(mn.Mode).ToUpperInvariant();
            goal.text = ModeDesc(mn.Mode);
            recText.text = "REC  ·  " + map.ToUpperInvariant();
        }

        // Quadcopter hum for the flyover: four slightly detuned rotors (rich buzz with a slow beat),
        // a breath of air noise, swelling in and dying away as the camera lands in the player's eyes.
        static AudioClip MakeDroneClip()
        {
            const int rate = 44100;
            float len = MatchNet.INTRO_FLY;
            int n = (int)(rate * len);
            var data = new float[n];
            var rng = new System.Random(77);
            float[] f = { 148f, 151.5f, 155f, 159.5f };
            float noise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                for (int r = 0; r < f.Length; r++)
                {
                    float ph = t * f[r] + r * 0.17f;
                    // a soft saw: first harmonics only
                    v += Mathf.Sin(2f * Mathf.PI * ph) * 0.6f + Mathf.Sin(4f * Mathf.PI * ph) * 0.28f
                       + Mathf.Sin(6f * Mathf.PI * ph) * 0.14f + Mathf.Sin(8f * Mathf.PI * ph) * 0.07f;
                }
                noise += ((float)(rng.NextDouble() * 2.0 - 1.0) - noise) * 0.12f;
                v = v * 0.16f + noise * 0.5f;
                float env = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((len - t) / 1.4f);
                env *= 0.85f + 0.15f * Mathf.Sin(t * 2.1f);   // drifting closer / farther
                data[i] = v * env;
            }
            var clip = AudioClip.Create("DroneFlyover", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
