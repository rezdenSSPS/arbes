using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Match wind: the host picks Off / Light / Strong in the lobby, the server rolls a direction and a speed
    // when the match starts (scaled by the map: indoor CQB maps have none, MapCatalog.MapInfo.wind) and
    // sends them with the match state. Every peer then flies BBs with the same BBBallistics.Wind, so the
    // server's hit validation agrees with what the shooter saw.
    //
    // No asset needed: the wind is two numbers in the match state. It shows as windsocks next to the team
    // spawns and as a small arrow chip under the minimap.
    public static class WindSystem
    {
        public static readonly string[] SETTING_NAMES = { "OFF", "LIGHT", "STRONG" };
        static readonly float[] SETTING_SPEED = { 0f, 2.5f, 5f };   // m/s before the map factor
        public const int DEFAULT_SETTING = 1;

        // Server: wind for a new match on `scene`. dirDeg = compass heading the wind blows towards.
        public static void Roll(int setting, string scene, out float dirDeg, out float speed)
        {
            setting = Mathf.Clamp(setting, 0, SETTING_SPEED.Length - 1);
            speed = SETTING_SPEED[setting] * MapCatalog.WindFor(scene) * Random.Range(0.8f, 1.2f);
            dirDeg = Random.Range(0f, 360f);
            if (speed < 0.05f) speed = 0f;
        }

        public static Vector3 ToVector(float dirDeg, float speed) =>
            speed <= 0f ? Vector3.zero : Quaternion.Euler(0f, dirDeg, 0f) * Vector3.forward * speed;

        // Any peer: apply the match's wind to the BB physics (zero outside a match).
        public static void Apply(bool inMatch, float dirDeg, float speed) =>
            BBBallistics.Wind = inMatch ? ToVector(dirDeg, speed) : Vector3.zero;

        public static string Describe(float speed) =>
            speed <= 0f ? "CALM" : speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m/s";

        // ───────── windsocks (every peer, visual only) ─────────
        static Scene socksScene;
        static bool socksBuilt;

        // MatchNet.Update: once per match scene, put a windsock next to each team's first spawn.
        public static void SyncWorld()
        {
            var w = BBBallistics.Wind;
            var scene = SceneManager.GetActiveScene();
            if (w.sqrMagnitude < 0.01f || !MapCatalog.IsMatchScene(scene.name)) { socksBuilt = false; return; }
            if (socksBuilt && socksScene == scene) return;
            socksScene = scene;
            socksBuilt = true;
            for (int team = 1; team <= 2; team++)
                foreach (var sp in SpawnPoint.All)
                    if (sp != null && sp.team == team)
                    {
                        Vector3 p = sp.transform.position + sp.transform.right * 2.5f;
                        if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                            p = hit.point;
                        WindSock.Create(p);
                        break;
                    }
        }

        // ───────── HUD chip ─────────
        public static void CreateHud(Transform parent) => WindHud.Create(parent);
    }

    // A pole with a striped orange / white sock that points downwind and droops in a light breeze.
    public class WindSock : MonoBehaviour
    {
        Transform sock;
        float phase;

        public static void Create(Vector3 ground)
        {
            var root = new GameObject("WindSock");
            root.transform.position = ground;
            var ws = root.AddComponent<WindSock>();
            ws.phase = Random.Range(0f, 10f);

            var grey = Mat(new Color(0.55f, 0.56f, 0.55f));
            var orange = Mat(new Color(1f, 0.45f, 0.1f));
            var white = Mat(new Color(0.93f, 0.93f, 0.9f));
            Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.06f, 1.6f, 0.06f), grey);

            ws.sock = new GameObject("Sock").transform;
            ws.sock.SetParent(root.transform, false);
            ws.sock.localPosition = new Vector3(0f, 3.1f, 0f);
            for (int i = 0; i < 4; i++)   // tapering rings along +Z
            {
                float s = 0.42f - i * 0.07f;
                var ring = Part(PrimitiveType.Cylinder, ws.sock, new Vector3(0f, 0f, 0.2f + i * 0.36f),
                                new Vector3(s, 0.18f, s), i % 2 == 0 ? orange : white);
                ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        void Update()
        {
            Vector3 w = BBBallistics.Wind;
            float speed = w.magnitude;
            if (sock == null) return;
            if (speed < 0.01f) { sock.gameObject.SetActive(false); return; }
            sock.gameObject.SetActive(true);
            float droop = Mathf.Lerp(55f, 5f, Mathf.InverseLerp(0.5f, 5f, speed));
            float flutter = Mathf.Sin(Time.time * (2f + speed) + phase) * (3f + speed);
            float heading = Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg;
            sock.rotation = Quaternion.Euler(droop + flutter * 0.3f, heading + flutter, 0f);
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);   // never blocks BBs or players
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "WindSock" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }
    }

    // "WIND 3.1 m/s" with an arrow that turns with the camera: up = the wind blows the way you look.
    public class WindHud : MonoBehaviour
    {
        RectTransform arrow;
        TextMeshProUGUI value;
        GameObject chip;

        public static void Create(Transform parent)
        {
            var row = HRow(parent, "Wind", 8, TextAnchor.MiddleLeft);
            row.padding = new RectOffset(10, 10, 5, 5);
            row.gameObject.AddComponent<Image>().color = Scrim(0.72f);
            var fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var hud = row.gameObject.AddComponent<WindHud>();
            hud.chip = row.gameObject;

            Label(row.transform, "WIND", F.BodyBold, 12, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            var holder = Rect(row.transform, "Arrow").LE(18, 18);
            hud.arrow = Rect(holder, "Pivot").At(0.5f, 0.5f, 0, 0, 18, 18);
            Box(hud.arrow, "Shaft", Fg).rectTransform.At(0.5f, 0.5f, 0, -2, 3, 12);
            var head = Box(hud.arrow, "Head", Fg).rectTransform;
            head.At(0.5f, 0.5f, 0, 4, 8, 8);
            head.localRotation = Quaternion.Euler(0f, 0f, 45f);
            hud.value = Label(row.transform, "", F.MonoBold, 12, Fg, TextAlignmentOptions.MidlineLeft, 0.06f);
        }

        void Update()
        {
            Vector3 w = BBBallistics.Wind;
            bool on = w.sqrMagnitude > 0.0001f;
            if (chip.transform.childCount > 0)
                for (int i = 0; i < chip.transform.childCount; i++) chip.transform.GetChild(i).gameObject.SetActive(on);
            var img = chip.GetComponent<Image>();
            if (img != null) img.enabled = on;
            if (!on) return;
            value.text = WindSystem.Describe(w.magnitude);
            var nm = Unity.Netcode.NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            var fps = po != null ? po.GetComponent<FPSController>() : null;
            var cam = fps != null && fps.playerCamera != null ? fps.playerCamera : Camera.main;
            float camYaw = cam != null ? cam.transform.eulerAngles.y : 0f;
            float windYaw = Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg;
            arrow.localRotation = Quaternion.Euler(0f, 0f, -(windYaw - camYaw));
        }
    }
}
