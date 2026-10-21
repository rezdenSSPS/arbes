using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // Flat polygons for uGUI (low-poly menu art, the diagonal panel edge).
    // Points use the design's SVG convention: origin at the frame's top-left, y down.
    // Each polygon is scanline-rasterized once into a small texture (with
    // anti-aliased edges) and shown with a plain Image.
    public static class UIPoly
    {
        // Returns the frame RectTransform; position/size it like any rect.
        public static RectTransform Create(Transform parent, string name, Color color, params Vector2[] pts)
        {
            var frame = UIKit.Rect(parent, name);
            if (pts == null || pts.Length < 3) return frame;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            float w = maxX - minX, h = maxY - minY;
            float scale = Mathf.Min(1f, 2048f / Mathf.Max(w, h));
            int tw = Mathf.Max(1, Mathf.CeilToInt(w * scale)), th = Mathf.Max(1, Mathf.CeilToInt(h * scale));

            var px = new Color32[tw * th];
            var cov = new float[tw];
            var xs = new List<float>();
            for (int y = 0; y < th; y++)
            {
                System.Array.Clear(cov, 0, tw);
                // 4 sub-scanlines per row for vertical anti-aliasing
                for (int sub = 0; sub < 4; sub++)
                {
                    float sy = minY + (y + (sub + 0.5f) / 4f) / scale;
                    xs.Clear();
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                        if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy))
                            xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                    xs.Sort();
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                    {
                        float x0 = (xs[k] - minX) * scale, x1 = (xs[k + 1] - minX) * scale;
                        int i0 = Mathf.Max(0, Mathf.FloorToInt(x0)), i1 = Mathf.Min(tw - 1, Mathf.FloorToInt(x1));
                        for (int i = i0; i <= i1; i++)
                            cov[i] += Mathf.Clamp01(Mathf.Min(i + 1, x1) - Mathf.Max(i, x0)) * 0.25f;
                    }
                }
                int row = (th - 1 - y) * tw;   // texture rows start at the bottom
                for (int i = 0; i < tw; i++)
                    px[row + i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(cov[i]) * 255));
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0f, 1f), 100f);
            sprite.hideFlags = HideFlags.DontSave;

            var img = UIKit.Box(frame, "Shape", color);
            img.sprite = sprite;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = img.rectTransform.pivot = new Vector2(0f, 1f);
            img.rectTransform.anchoredPosition = new Vector2(minX, -minY);
            img.rectTransform.sizeDelta = new Vector2(w, h);
            return frame;
        }
    }
}
