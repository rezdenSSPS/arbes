using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Menu / lobby background: a slideshow of real game frames (Resources/MenuShots, taken from matches
    // with bots by the editor tool MenuShotsCapture: first person with a replica, over a bot's shoulder,
    // a chase cam) with slow pan-and-zoom and cross-fades. Each picture's file name says the map and mode
    // (m<map>_g<mode>_<n>), so the menu can label what is on screen. No pictures = Create returns null.
    public class MenuShowcase : MonoBehaviour
    {
        const float SLIDE = 6.5f;   // seconds per picture
        const float FADE = 1.3f;    // cross-fade

        public struct Slide { public Texture2D tex; public int map; public MatchNet.GameModeType mode; }

        // The picture on screen changed (map, mode): the menu updates its labels.
        public event Action<Slide> OnSlide;
        public Slide Current => slides.Count > 0 ? slides[order[index % order.Count]] : default;
        public int Count => slides.Count;

        static List<Slide> s_slides;
        readonly List<Slide> slides = new List<Slide>();
        readonly List<int> order = new List<int>();
        RawImage back, front;
        RectTransform area;
        int index;
        float shownAt;
        Vector4 backPan, frontPan;   // (start x, start y, end x, end y) uv offsets, as fractions of the free room
        float backZoom, frontZoom;
        bool fading;

        static List<Slide> Load()
        {
            if (s_slides != null) return s_slides;
            s_slides = new List<Slide>();
            foreach (var t in Resources.LoadAll<Texture2D>("MenuShots"))
            {
                // m<map>_g<mode>_<n>
                var parts = t.name.Split('_');
                if (parts.Length < 2 || !int.TryParse(parts[0].TrimStart('m'), out int map)
                    || !int.TryParse(parts[1].TrimStart('g'), out int mode)) continue;
                t.wrapMode = TextureWrapMode.Clamp;
                s_slides.Add(new Slide { tex = t, map = map, mode = (MatchNet.GameModeType)mode });
            }
            return s_slides;
        }

        // Fills `parent` with the slideshow. dim: 0 = full colour (main menu art), up to 1 = hidden under
        // the ground colour (lobby: the pictures only breathe behind the panels).
        public static MenuShowcase Create(RectTransform parent, float dim, bool edgeFades)
        {
            var list = Load();
            if (list.Count == 0) return null;
            var root = Rect(parent, "Showcase").Fill();
            var sc = root.gameObject.AddComponent<MenuShowcase>();
            sc.area = root;
            sc.slides.AddRange(list);
            for (int i = 0; i < list.Count; i++) sc.order.Add(i);
            // shuffle, but never two pictures of the same match in a row
            for (int i = sc.order.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (sc.order[i], sc.order[j]) = (sc.order[j], sc.order[i]); }
            for (int i = 1; i < sc.order.Count; i++)
                if (SameMatch(list[sc.order[i]], list[sc.order[i - 1]]))
                    for (int k = i + 1; k < sc.order.Count; k++)
                        if (!SameMatch(list[sc.order[k]], list[sc.order[i - 1]])) { (sc.order[i], sc.order[k]) = (sc.order[k], sc.order[i]); break; }

            sc.back = Raw(root, "Back");
            sc.front = Raw(root, "Front");
            if (dim > 0f) Box(root, "Dim", A(Ground, dim)).rectTransform.Fill();
            if (edgeFades)
            {
                // into the dark menu panel on the left, and darker at the bottom for the labels
                var l = Gradient(root, "FadeLeft", true, A(Ground, 0.95f), A(Ground, 0f));
                l.rectTransform.anchorMin = Vector2.zero; l.rectTransform.anchorMax = new Vector2(0.38f, 1f);
                l.rectTransform.offsetMin = l.rectTransform.offsetMax = Vector2.zero;
                var b = Gradient(root, "FadeBottom", false, A(Ground, 0.85f), A(Ground, 0f));
                b.rectTransform.anchorMin = Vector2.zero; b.rectTransform.anchorMax = new Vector2(1f, 0.32f);
                b.rectTransform.offsetMin = b.rectTransform.offsetMax = Vector2.zero;
                var t = Gradient(root, "FadeTop", false, A(Ground, 0f), A(Ground, 0.45f));
                t.rectTransform.anchorMin = new Vector2(0f, 0.8f); t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            }
            sc.index = 0;
            sc.Show(sc.back, ref sc.backPan, ref sc.backZoom, sc.Current.tex);
            sc.front.color = new Color(1, 1, 1, 0);
            sc.shownAt = Time.unscaledTime;
            return sc;
        }

        static bool SameMatch(Slide a, Slide b) => a.map == b.map && a.mode == b.mode;

        static RawImage Raw(Transform parent, string name)
        {
            var img = Rect(parent, name).Fill().gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        // A two-colour ramp (from -> to): horizontal = left to right, otherwise bottom to top.
        static RawImage Gradient(Transform parent, string name, bool horizontal, Color from, Color to)
        {
            var img = Rect(parent, name).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            const int n = 64;
            var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float u = i / (n - 1f);
                var c = Color.Lerp(from, to, u * u * (3f - 2f * u));
                if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, i, c);
            }
            tex.Apply();
            img.texture = tex;
            return img;
        }

        void Show(RawImage img, ref Vector4 pan, ref float zoom, Texture2D tex)
        {
            img.texture = tex;
            img.color = Color.white;
            zoom = UnityEngine.Random.Range(1.05f, 1.12f);
            // drift from one side to the other (random direction), a little vertical too
            float sx = UnityEngine.Random.value < 0.5f ? 0.15f : 0.85f;
            float sy = UnityEngine.Random.Range(0.35f, 0.65f);
            pan = new Vector4(sx, sy, 1f - sx, UnityEngine.Random.Range(0.35f, 0.65f));
            OnSlide?.Invoke(Current);
        }

        // "Cover" the area with the picture, zoomed in by zoom (growing over the slide) and panned.
        void Frame(RawImage img, Vector4 pan, float zoom, float u)
        {
            if (img.texture == null) return;
            var r = area.rect;
            if (r.width < 1f || r.height < 1f) return;
            float aArea = r.width / r.height, aTex = img.texture.width / (float)img.texture.height;
            float w = 1f, h = 1f;
            if (aArea < aTex) w = aArea / aTex; else h = aTex / aArea;
            float z = zoom * (1f + 0.06f * u);
            w /= z; h /= z;
            float e = u * u * (3f - 2f * u) * 0.6f + u * 0.4f;
            float px = Mathf.Lerp(pan.x, pan.z, e), py = Mathf.Lerp(pan.y, pan.w, e);
            img.uvRect = new Rect((1f - w) * px, (1f - h) * py, w, h);
        }

        void Update()
        {
            if (slides.Count == 0) return;
            float age = Time.unscaledTime - shownAt;
            float u = Mathf.Clamp01(age / (SLIDE + FADE));
            Frame(back, backPan, backZoom, u);

            if (slides.Count < 2) return;
            if (!fading && age >= SLIDE)
            {
                fading = true;
                index++;
                Show(front, ref frontPan, ref frontZoom, Current.tex);
                front.color = new Color(1, 1, 1, 0);
            }
            if (fading)
            {
                float f = Mathf.Clamp01((age - SLIDE) / FADE);
                front.color = new Color(1, 1, 1, f * f * (3f - 2f * f));
                Frame(front, frontPan, frontZoom, Mathf.Clamp01((age - SLIDE) / (SLIDE + FADE)));
                if (f >= 1f)
                {
                    // the front picture becomes the back one; its pan keeps going from where it is
                    (back, front) = (front, back);
                    (backPan, frontPan) = (frontPan, backPan);
                    (backZoom, frontZoom) = (frontZoom, backZoom);
                    back.transform.SetSiblingIndex(0);
                    front.color = new Color(1, 1, 1, 0);
                    shownAt = Time.unscaledTime - FADE;
                    fading = false;
                }
            }
        }
    }
}
