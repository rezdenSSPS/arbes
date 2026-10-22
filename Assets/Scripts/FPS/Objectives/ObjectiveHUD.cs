using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // HUD host for the active ObjectiveMode: a widget slot under the team score bar, a full-screen
    // layer for world markers (flags, control points) and short objective announcements. HUDManager
    // creates it inside its HUD root, so it hides with the HUD (pause, killcam, results). The mode
    // itself builds and drives its widgets (ObjectiveMode.BuildHud / UpdateHud).
    public class ObjectiveHUD : MonoBehaviour
    {
        RectTransform widget, markers;
        ObjectiveMode builtFor;
        GameObject toast;
        TextMeshProUGUI toastText;
        Image toastSwatch;
        float toastUntil;
        readonly Queue<(string text, int team)> pending = new Queue<(string, int)>();

        // top = HUDManager's top-centre column (the widget goes right after the team score bar at index);
        // hudRoot = the HUD's full-screen root (markers go behind the other HUD elements).
        public static ObjectiveHUD Create(GameObject host, Transform top, int siblingIndex, Transform hudRoot)
        {
            var h = host.AddComponent<ObjectiveHUD>();
            var col = VCol(top, "Objective", 6, TextAnchor.UpperCenter, false);
            col.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, top.childCount - 1));
            h.widget = (RectTransform)col.transform;

            h.markers = Rect(hudRoot, "ObjectiveMarkers").Fill();
            h.markers.SetSiblingIndex(Mathf.Min(1, hudRoot.childCount - 1));   // just above the low-health vignette

            var t = HRow(col.transform, "Toast", 8, TextAnchor.MiddleCenter);
            t.padding = new RectOffset(10, 12, 4, 4);
            t.gameObject.AddComponent<Image>().color = Scrim(0.85f);
            h.toastSwatch = Box(t.transform, "Swatch", Accent);
            h.toastSwatch.LE(4, 16);
            h.toastText = Label(t.transform, "", F.BodySemi, 13, Fg);
            h.toast = t.gameObject;
            h.toast.SetActive(false);

            ObjectiveMode.OnAnnounce += h.Announce;
            return h;
        }

        void OnDestroy() => ObjectiveMode.OnAnnounce -= Announce;

        void Announce(string text, int team)
        {
            if (pending.Count > 4) pending.Dequeue();
            pending.Enqueue((text, team));
        }

        void Update()
        {
            var obj = ObjectiveMode.Active;
            if (obj != builtFor)
            {
                // rebuild for the new mode (keep the toast, which is the last child)
                for (int i = widget.childCount - 1; i >= 0; i--)
                {
                    var c = widget.GetChild(i);
                    if (c.gameObject != toast) Destroy(c.gameObject);
                }
                ClearChildren(markers);
                builtFor = obj;
                pending.Clear();
                toast.SetActive(false);
                if (obj != null)
                {
                    obj.BuildHud(widget, markers);
                    toast.transform.SetAsLastSibling();
                }
            }
            widget.gameObject.SetActive(obj != null);
            markers.gameObject.SetActive(obj != null);
            if (obj == null) return;

            obj.UpdateHud(LocalCamera(), markers);

            if (Time.unscaledTime >= toastUntil)
            {
                if (pending.Count > 0)
                {
                    var (text, team) = pending.Dequeue();
                    toastText.text = text;
                    toastSwatch.color = team == 0 ? Accent : TeamColor(team);
                    toast.SetActive(true);
                    toastUntil = Time.unscaledTime + 3f;
                }
                else toast.SetActive(false);
            }
        }

        static Camera LocalCamera()
        {
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            var fps = po != null ? po.GetComponent<FPSController>() : null;
            if (fps != null && fps.playerCamera != null && fps.playerCamera.isActiveAndEnabled) return fps.playerCamera;
            return Camera.main;
        }

        // ───────────────────────── world marker ─────────────────────────
        // A disc with a letter and a caption + distance under it, pinned to the screen edge when the
        // point is off screen or behind the camera.
        public class Marker
        {
            public RectTransform rt;
            Image disc, ring;
            TextMeshProUGUI letter, caption;
            bool pulse;
            Color color;

            public static Marker Create(RectTransform layer, string letter, Color c)
            {
                var m = new Marker();
                m.rt = Rect(layer, "Marker_" + letter).At(0.5f, 0.5f, 0, 0, 120, 56);
                m.ring = Box(m.rt, "Ring", Ground);
                m.ring.sprite = DiscSprite;
                m.ring.rectTransform.At(0.5f, 1f, 0, 0, 30, 30);
                m.disc = Box(m.rt, "Disc", c);
                m.disc.sprite = DiscSprite;
                m.disc.rectTransform.At(0.5f, 1f, 0, -3, 24, 24);
                m.letter = Label(m.disc.transform, letter, F.CondBlack, 16, Ground, TextAlignmentOptions.Center);
                m.letter.rectTransform.Fill();
                m.caption = Label(m.rt, "", F.BodyBold, 10, Fg, TextAlignmentOptions.Top, 0.1f);
                m.caption.rectTransform.At(0.5f, 1f, 0, -32, 160, 24);
                var sh = m.caption.gameObject.AddComponent<Shadow>();
                sh.effectColor = A(Ground, 0.9f);
                sh.effectDistance = new Vector2(1, -1);
                m.color = c;
                m.rt.gameObject.SetActive(false);
                return m;
            }

            public void SetColor(Color c) { color = c; }
            public void SetPulse(bool on) { pulse = on; }

            public void Show(Camera cam, RectTransform layer, Vector3 world, string label, bool visible)
            {
                if (rt == null) return;
                if (!visible || cam == null) { rt.gameObject.SetActive(false); return; }
                rt.gameObject.SetActive(true);

                Vector3 sp = cam.WorldToScreenPoint(world);
                bool behind = sp.z < 0f;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out Vector2 lp);
                if (behind)
                {
                    lp = -lp;
                    if (lp.sqrMagnitude < 1f) lp = Vector2.down;
                    lp = lp.normalized * 100000f;
                }
                Vector2 half = layer.rect.size * 0.5f - new Vector2(60f, 40f);
                bool edge = behind || Mathf.Abs(lp.x) > half.x || Mathf.Abs(lp.y) > half.y;
                if (edge)
                {
                    float k = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(lp.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(lp.y)));
                    if (k < 1f) lp *= k;
                }
                rt.anchoredPosition = lp + new Vector2(0, 14f);

                float dist = Vector3.Distance(cam.transform.position, world);
                caption.text = string.IsNullOrEmpty(label) ? Mathf.RoundToInt(dist) + " m"
                    : $"{label}  <color={Html(Muted)}>{Mathf.RoundToInt(dist)} m</color>";
                caption.gameObject.SetActive(!edge || dist < 200f);

                float a = pulse ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) : 1f;
                disc.color = A(color, a);
                ring.color = pulse ? A(Accent, a) : A(Ground, 0.8f);
                float s = edge ? 0.8f : 1f;
                rt.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
