using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Round HUD minimap (top-left, under the join-code chip). The arena is photographed
    // once from above by an orthographic camera into a small RenderTexture (static
    // geometry only: everything networked is hidden for that one render), then panned
    // and turned under a fixed player arrow. Teammates show as dots in their team
    // colour (TDM) or white (Co-op); enemies are never shown.
    // Settings > Gameplay: "Show minimap" and rotating / north-up.
    public class Minimap : MonoBehaviour
    {
        const float Size = 168f;          // on-screen diameter (1280x720 reference)
        const float ViewMeters = 56f;     // world metres across the minimap
        const float ArenaHalf = 46f;      // photographed area: -46..46 (playable -40..40 plus the walls)
        const int TexSize = 512;
        const float Ppm = Size / ViewMeters;
        const float DotEdge = Size * 0.5f - 8f;

        GameObject content;
        RectTransform pivot, arrow, north, dotLayer;
        RawImage map;
        RenderTexture tex;
        float readyAt = -1f;
        float scanTimer;
        readonly List<FPSController> players = new List<FPSController>();
        readonly List<Image> dots = new List<Image>();

        public static Minimap Create(Transform parent)
        {
            var holder = Rect(parent, "Minimap").LE(Size, Size);
            var mm = holder.gameObject.AddComponent<Minimap>();
            mm.Build(holder);
            return mm;
        }

        void Build(RectTransform holder)
        {
            content = Rect(holder, "Content").Fill().gameObject;

            var mask = Box(content.transform, "Mask", Hex("#20251C"));
            mask.sprite = DiscSprite;
            mask.rectTransform.Fill();
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            pivot = Rect(mask.transform, "Pivot").At(0.5f, 0.5f, 0, 0, 0, 0);
            map = new GameObject("Map", typeof(RectTransform)).AddComponent<RawImage>();
            map.transform.SetParent(pivot, false);
            map.raycastTarget = false;
            map.color = new Color(0.86f, 0.88f, 0.84f, 1f);   // a touch darker so markers pop
            map.rectTransform.At(0.5f, 0.5f, 0, 0, ArenaHalf * 2f * Ppm, ArenaHalf * 2f * Ppm);
            map.enabled = false;

            dotLayer = Rect(mask.transform, "Dots").Fill();

            // local player: arrow pointing the way you face
            arrow = Rect(mask.transform, "You").At(0.5f, 0.5f, 0, 0, 26, 26);
            var shadow = UIPoly.Create(arrow, "Outline", A(Ground, 0.85f),
                new Vector2(13, 0), new Vector2(25, 25), new Vector2(13, 19), new Vector2(1, 25));
            shadow.At(0.5f, 0.5f, 0, 0, 26, 26);
            var tip = UIPoly.Create(arrow, "Arrow", Accent,
                new Vector2(13, 3), new Vector2(22, 22), new Vector2(13, 17), new Vector2(4, 22));
            tip.At(0.5f, 0.5f, 0, 0, 26, 26);

            var ring = Box(content.transform, "Ring", A(LineStrong, 0.95f));
            ring.sprite = RingSprite;
            ring.rectTransform.Fill(-2, -2, -2, -2);

            north = Rect(content.transform, "North").At(0.5f, 0.5f, 0, 0, 18, 18);
            var nBg = Box(north, "Bg", A(Ground, 0.9f));
            nBg.sprite = DiscSprite;
            nBg.rectTransform.Fill();
            Label(north, "N", F.BodyBold, 10, Accent, TextAlignmentOptions.Center).rectTransform.Fill();
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            var me = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            bool show = GameSettings.ShowMinimap && me != null;
            if (content.activeSelf != show) content.SetActive(show);
            if (me == null) { readyAt = -1f; return; }

            // Photograph the arena once, shortly after the local player appears (scene settled).
            // Again only if the GPU dropped the texture (e.g. device reset).
            if (tex == null || !tex.IsCreated())
            {
                if (readyAt < 0f) readyAt = Time.unscaledTime + 0.5f;
                if (Time.unscaledTime >= readyAt) RenderArena();
            }
            if (!show) return;

            Vector3 p = me.transform.position;
            float yaw = me.transform.eulerAngles.y;
            bool rotate = GameSettings.MinimapRotate;
            // Rotating: turn the map by your yaw so your facing points up. North-up: turn the arrow instead.
            var mapRot = Quaternion.Euler(0, 0, rotate ? yaw : 0f);
            pivot.localRotation = mapRot;
            map.rectTransform.anchoredPosition = new Vector2(-p.x, -p.z) * Ppm;
            arrow.localRotation = Quaternion.Euler(0, 0, rotate ? 0f : -yaw);
            north.anchoredPosition = mapRot * new Vector3(0, Size * 0.5f - 2f, 0);

            UpdateTeammates(nm, me, p, mapRot);
        }

        void UpdateTeammates(NetworkManager nm, NetworkObject me, Vector3 p, Quaternion mapRot)
        {
            scanTimer -= Time.unscaledDeltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = 0.5f;
                players.Clear();
                players.AddRange(FindObjectsByType<FPSController>());
            }

            var mn = MatchNet.Instance;
            var mode = mn != null ? mn.Mode : MatchNet.GameModeType.Coop;
            int myTeam = mn != null ? mn.TeamOf(me.OwnerClientId) : 0;
            int used = 0;
            foreach (var pl in players)
            {
                if (pl == null || pl.NetworkObject == me || !pl.IsSpawned) continue;
                // Teammates only: everyone in Co-op, your own team in TDM. FFA / Gun Game: nobody.
                int team = 0;
                if (mode == MatchNet.GameModeType.TeamDeathmatch)
                {
                    team = mn.TeamOf(pl.OwnerClientId);
                    if (team == 0 || team != myTeam) continue;
                }
                else if (mode != MatchNet.GameModeType.Coop) continue;
                var h = pl.GetComponent<Health>();
                if (h != null && h.isDead.Value) continue;

                Vector3 d = pl.transform.position - p;
                Vector2 off = mapRot * new Vector3(d.x * Ppm, d.z * Ppm, 0);
                if (off.magnitude > DotEdge) off = off.normalized * DotEdge;   // pin far teammates to the rim
                var dot = Dot(used++);
                dot.rectTransform.anchoredPosition = off;
                dot.transform.GetChild(0).GetComponent<Image>().color = team != 0 ? TeamColor(team) : Fg;
            }
            for (int i = 0; i < dots.Count; i++) dots[i].gameObject.SetActive(i < used);
        }

        Image Dot(int i)
        {
            while (dots.Count <= i)
            {
                var outer = Box(dotLayer, "Mate", A(Ground, 0.85f));
                outer.sprite = DiscSprite;
                outer.rectTransform.At(0.5f, 0.5f, 0, 0, 12, 12);
                var inner = Box(outer.transform, "Fill", Fg);
                inner.sprite = DiscSprite;
                inner.rectTransform.Fill(2, 2, 2, 2);
                dots.Add(outer);
            }
            return dots[i];
        }

        // One top-down orthographic shot of the arena's static geometry into a RenderTexture.
        void RenderArena()
        {
            readyAt = -1f;
            if (tex != null) { tex.Release(); Destroy(tex); }
            tex = new RenderTexture(TexSize, TexSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "MinimapArena", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false
            };
            tex.Create();

            var go = new GameObject("MinimapCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = ArenaHalf;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#20251C");
            cam.targetTexture = tex;
            // Looking straight down with +Z (north) at the top of the image and +X to the right.
            go.transform.SetPositionAndRotation(new Vector3(0f, 120f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
            urp.antialiasing = AntialiasingMode.None;
            urp.requiresDepthTexture = false;
            urp.requiresColorTexture = false;

            // Static geometry only: hide players, bots, pickups (anything networked) for this render.
            var hiddenR = new List<Renderer>();
            var hiddenC = new List<Canvas>();
            foreach (var no in FindObjectsByType<NetworkObject>())
            {
                foreach (var r in no.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hiddenR.Add(r); }
                foreach (var c in no.GetComponentsInChildren<Canvas>())
                    if (c.enabled) { c.enabled = false; hiddenC.Add(c); }
            }
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;   // fog would wash out a shot from 120 m up
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = tex };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
            }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] minimap render failed: " + e.Message); }
            finally
            {
                RenderSettings.fog = fog;
                foreach (var r in hiddenR) if (r != null) r.enabled = true;
                foreach (var c in hiddenC) if (c != null) c.enabled = true;
                cam.targetTexture = null;
                Destroy(go);
            }

            map.texture = tex;
            map.enabled = true;
        }

        void OnDestroy()
        {
            if (tex != null) { tex.Release(); Destroy(tex); }
        }
    }
}
