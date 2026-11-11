using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AirsoftFPS
{
    // Small head-and-shoulders portraits of a player's character (their AirsoftLook: body, skin, hair,
    // hat, eyewear), used next to names in the lobby, the scoreboard and the "eliminated by" cards.
    // Each look is built once on a hidden stage far below the world, photographed by its own camera
    // into a render texture with a transparent background, and kept for the session; the UI puts it
    // on a coloured tile (team colour / shirt colour). Look -1 (not chosen yet) has no portrait.
    public static class HeadPortrait
    {
        const int Layer = 30;            // portrait stage only (the character preview uses 31)
        const int Size = 128;            // texture px
        static readonly Vector3 StagePos = new Vector3(0f, -2000f, 0f);

        static Transform stage;
        static Camera cam;
        static readonly Dictionary<int, RenderTexture> cache = new Dictionary<int, RenderTexture>();

        // The portrait of a packed look, or null for "no look yet" / a failed build.
        public static Texture Get(int packedLook)
        {
            if (packedLook < 0) return null;
            if (cache.TryGetValue(packedLook, out var rt) && rt != null && rt.IsCreated()) return rt;
            rt = Render(packedLook);
            cache[packedLook] = rt;
            return rt;
        }

        // A tile (coloured square) with the portrait on top. Returns the portrait image; the tile is its parent.
        public static RawImage Create(Transform parent, string name, float size, out Image tile)
        {
            tile = UIKit.Box(parent, name, UIKit.Raised);
            tile.LE(size, size);
            var img = UIKit.Rect(tile.transform, "Head").Fill().gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        // Fills a tile made by Create: background colour + the look's head (or just the colour).
        public static void Show(RawImage img, Image tile, int packedLook, Color background)
        {
            var tex = Get(packedLook);
            img.texture = tex;
            img.enabled = tex != null;
            // with a head on it the tile is a darker version of the colour, so the face stands out
            tile.color = tex != null ? Color.Lerp(background, UIKit.Ground, 0.35f) : background;
        }

        static void EnsureStage()
        {
            if (stage != null) return;
            var go = new GameObject("HeadPortraitStage");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            stage = go.transform;
            stage.position = StagePos;

            var camGo = new GameObject("PortraitCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            camGo.transform.SetParent(stage, false);
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 10f;
            cam.allowHDR = false; cam.allowMSAA = true;

            AddLight(new Vector3(1.2f, 2.4f, 1.8f), new Color(1f, 0.95f, 0.88f), 6f);
            AddLight(new Vector3(-1.6f, 1.9f, 1.2f), new Color(0.65f, 0.78f, 1f), 3.5f);
            AddLight(new Vector3(0f, 2.3f, -1.5f), new Color(1f, 0.75f, 0.5f), 4f);
        }

        static void AddLight(Vector3 local, Color c, float intensity)
        {
            var go = new GameObject("PortraitLight");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(stage, false);
            go.transform.localPosition = local;
            go.layer = Layer;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = 6f;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Layer;
        }

        static RenderTexture Render(int look)
        {
            EnsureStage();
            AirsoftLook.Avatar av = null;
            try { av = AirsoftLook.BuildAvatar(stage, look); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] head portrait failed: " + e.Message); }
            if (av == null || av.root == null || av.head == null)
            {
                if (av != null && av.root) Object.DestroyImmediate(av.root);
                return null;
            }
            AirsoftLook.SetLayer(av.root, Layer);
            foreach (var r in av.root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Head box: from just under the chin up to the top of the hair or the tallest hat.
            float headY = av.head.position.y;
            float top = headY + 0.2f;
            foreach (var r in av.root.GetComponentsInChildren<Renderer>())
                if (r.enabled && !(r is SkinnedMeshRenderer)) top = Mathf.Max(top, r.bounds.max.y);   // hats, eyewear
            var headTop = FindDeep(av.root.transform, "Head_end") ?? FindDeep(av.root.transform, "HeadTop_end");
            if (headTop != null) top = Mathf.Max(top, headTop.position.y + 0.03f);
            top = Mathf.Min(top, headY + 0.75f);                     // a top hat does not shrink the face to a dot
            float bottom = headY - 0.17f;                            // chin and a bit of the collar
            float half = (top - bottom) * 0.5f * 1.08f;
            float midY = (top + bottom) * 0.5f;

            // three-quarter view from the front-right, slightly from above
            Vector3 centre = new Vector3(stage.position.x, midY, stage.position.z);
            Quaternion rot = Quaternion.Euler(6f, 200f, 0f);   // character faces +Z, camera looks back at it
            cam.transform.SetPositionAndRotation(centre - rot * Vector3.forward * 3f, rot);
            cam.orthographicSize = half;

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "HeadPortrait", antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            Object.DestroyImmediate(av.root);
            return rt;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
