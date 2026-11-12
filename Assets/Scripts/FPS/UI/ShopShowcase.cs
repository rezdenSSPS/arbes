using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // The Armory's spinning weapons. All ten replicas stand in a 5 x 2 grid on a hidden stage far below
    // the world; one orthographic camera films the whole grid into one render texture with a transparent
    // background, and each shop card shows its own cell of it (CellUV). The camera only runs while the
    // shop is open. Built on the first open and kept (it is destroyed with the play session).
    public static class ShopShowcase
    {
        const int Layer = 29;                 // showcase only (head portraits use 30, character preview 31)
        public const int Cols = 5, Rows = 2;
        const int CellPxW = 384, CellPxH = 200;
        const float CellW = 1.6f;             // world size of one cell (same aspect as the pixels)
        const float CellH = CellW * CellPxH / CellPxW;
        const float SpinDegPerSec = 38f;
        static readonly Vector3 StagePos = new Vector3(0f, -3000f, 0f);

        static Transform stage;
        static Camera cam;
        static RenderTexture rt;
        static readonly List<Transform> spinners = new List<Transform>();

        public static Texture Texture { get { Ensure(); return rt; } }

        // The part of the texture that shows weapon i (row-major, i = 0 top-left).
        public static Rect CellUV(int i)
        {
            int c = i % Cols, r = i / Cols;
            return new Rect(c / (float)Cols, 1f - (r + 1) / (float)Rows, 1f / Cols, 1f / Rows);
        }

        public static void Show(bool on)
        {
            if (!on && stage == null) return;
            Ensure();
            stage.gameObject.SetActive(on);
            cam.enabled = on;
            if (on)   // nothing else films the stage (it is far away anyway)
                foreach (var c in Camera.allCameras) if (c != cam) c.cullingMask &= ~(1 << Layer);
        }

        public static void Spin(float dt)
        {
            foreach (var s in spinners) if (s != null) s.Rotate(0f, SpinDegPerSec * dt, 0f, Space.World);
        }

        static void Ensure()
        {
            if (rt == null || !rt.IsCreated())
            {
                rt = new RenderTexture(CellPxW * Cols, CellPxH * Rows, 24, RenderTextureFormat.ARGB32)
                     { name = "ShopShowcase", antiAliasing = 4 };
                rt.Create();
                if (cam != null) cam.targetTexture = rt;
            }
            if (stage != null) return;

            var go = new GameObject("ShopShowcaseStage") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            stage = go.transform;
            stage.position = StagePos;
            spinners.Clear();

            var camGo = new GameObject("ShowcaseCam");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);   // a little from above
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = CellH * Rows * 0.5f;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 20f;
            cam.allowHDR = false; cam.allowMSAA = true;
            cam.targetTexture = rt;

            // the grid stands 8 m in front of the camera, square to its view
            var t = camGo.transform;
            Vector3 centre = t.position + t.forward * 8f;
            var weapons = WeaponData.GetAllWeapons();
            for (int i = 0; i < weapons.Length && i < Cols * Rows; i++)
            {
                int c = i % Cols, r = i / Cols;
                Vector3 pos = centre + t.right * ((c - (Cols - 1) * 0.5f) * CellW) + t.up * (((Rows - 1) * 0.5f - r) * CellH);
                var holder = new GameObject("Weapon" + i).transform;
                holder.SetParent(stage, true);
                holder.position = pos;
                PlaceGun(holder, weapons[i]);
                holder.rotation = Quaternion.Euler(0f, 90f + i * 14f, 0f);   // side-on first, each a little ahead
                spinners.Add(holder);

                AddLight(pos + t.right * 1.1f + t.up * 1.1f - t.forward * 2.2f, new Color(1f, 0.95f, 0.88f), 5.5f);
                AddLight(pos - t.right * 1.5f + t.up * 0.5f - t.forward * 1.5f, new Color(0.62f, 0.75f, 1f), 3f);
                AddLight(pos + t.up * 1f + t.forward * 1.5f, new Color(1f, 0.78f, 0.55f), 3.5f);   // rim
            }
            AirsoftLook.SetLayer(go, Layer);
            stage.gameObject.SetActive(false);
        }

        // Builds the replica, fits it into the cell whichever way it faces and centres it on the holder.
        static void PlaceGun(Transform holder, WeaponData w)
        {
            AirsoftGuns.Gun g = null;
            try { g = AirsoftGuns.Build(w.model, 1f, holder, w.weaponName); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] showcase " + w.weaponName + ": " + e.Message); }
            if (g == null || g.root == null) return;

            var b = new Bounds(holder.position, Vector3.zero);
            bool any = false;
            foreach (var r in g.root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            float len = Mathf.Max(b.size.z, b.size.x, 0.01f);   // barrel = +Z after Build
            float s = Mathf.Min(CellW * 0.9f / len, CellH * 0.72f / Mathf.Max(b.size.y, 0.01f));
            g.root.transform.localPosition = -(b.center - holder.position);
            holder.localScale = Vector3.one * s;
        }

        static void AddLight(Vector3 pos, Color c, float intensity)
        {
            var go = new GameObject("ShowcaseLight");
            go.transform.SetParent(stage, true);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = 3.4f;   // reaches its own weapon, hardly the neighbours
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Layer;
        }
    }
}
