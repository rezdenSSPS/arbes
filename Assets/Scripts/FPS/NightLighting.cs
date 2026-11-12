using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // Night match (MatchNet.NightMode): once a match map is loaded, the sun becomes a weak blue moon, the
    // sky, ambient light, reflections and fog go dark navy, street lamps on the map get a warm light,
    // and the local player carries a headlamp (a spot light on their own camera: everyone lights their
    // own way, other players don't see it). Every machine does this itself from the synced flag, so
    // nothing is networked. The day values are kept so the minimap can still be photographed in
    // daylight (Daylight / Night around its one render).
    public static class NightLighting
    {
        static ulong appliedScene;   // SceneHandle raw data of the darkened scene
        static bool applied;
        static Light headlamp;

        // day values of the current scene, kept for the minimap shot
        struct LightState { public Light l; public float intensity, shadow; public Color color; }
        static readonly List<LightState> suns = new List<LightState>();
        static readonly List<Light> lamps = new List<Light>();
        static UnityEngine.Rendering.AmbientMode dayAmbientMode;
        static Color daySky, dayEquator, dayGround, dayAmbient, dayFog;
        static float dayReflection, dayFogStart, dayFogEnd, dayFogDensity;
        static bool dayFogOn;
        static Material daySkybox, nightSkybox;
        static bool isNight;

        static readonly Color Moon = new Color(0.58f, 0.68f, 1f);
        static readonly Color NightFog = new Color(0.035f, 0.045f, 0.08f);
        static readonly Color LampColor = new Color(1f, 0.78f, 0.48f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { applied = false; headlamp = null; suns.Clear(); lamps.Clear(); isNight = false; }

        public static void Tick(MatchNet mn)
        {
            var scene = SceneManager.GetActiveScene();
            bool want = mn != null && mn.NightMode && MapCatalog.IsMatchScene(scene.name);
            if (!want) { applied = false; isNight = false; return; }
            ulong handle = scene.handle.GetRawData();
            if (!applied || appliedScene != handle)
            {
                applied = true;
                appliedScene = handle;
                Apply();
            }
            EnsureHeadlamp();
        }

        static void Apply()
        {
            suns.Clear(); lamps.Clear();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                    suns.Add(new LightState { l = l, intensity = l.intensity, color = l.color, shadow = l.shadowStrength });

            dayAmbientMode = RenderSettings.ambientMode;
            daySky = RenderSettings.ambientSkyColor; dayEquator = RenderSettings.ambientEquatorColor;
            dayGround = RenderSettings.ambientGroundColor; dayAmbient = RenderSettings.ambientLight;
            dayReflection = RenderSettings.reflectionIntensity;
            dayFogOn = RenderSettings.fog; dayFog = RenderSettings.fogColor;
            dayFogStart = RenderSettings.fogStartDistance; dayFogEnd = RenderSettings.fogEndDistance;
            dayFogDensity = RenderSettings.fogDensity;
            daySkybox = RenderSettings.skybox;
            if (nightSkybox != null) Object.Destroy(nightSkybox);
            nightSkybox = null;
            if (daySkybox != null)
            {
                nightSkybox = new Material(daySkybox) { name = "NightSky" };
                SetF(nightSkybox, "_Exposure", 0.16f);
                SetF(nightSkybox, "_AtmosphereThickness", 0.45f);
                SetF(nightSkybox, "_SunSize", 0.03f);   // the moon
                SetC(nightSkybox, "_SkyTint", new Color(0.22f, 0.28f, 0.55f));
                SetC(nightSkybox, "_GroundColor", new Color(0.05f, 0.06f, 0.08f));
            }

            AddLamps();
            SetNight(true);
        }

        static void SetNight(bool night)
        {
            isNight = night;
            foreach (var s in suns)
            {
                if (s.l == null) continue;
                s.l.intensity = night ? s.intensity * 0.13f : s.intensity;
                s.l.color = night ? Moon : s.color;
                s.l.shadowStrength = night ? 0.55f : s.shadow;
            }
            foreach (var l in lamps) if (l != null) l.enabled = night;
            if (night)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.075f, 0.095f, 0.17f);
                RenderSettings.ambientEquatorColor = new Color(0.05f, 0.06f, 0.1f);
                RenderSettings.ambientGroundColor = new Color(0.025f, 0.028f, 0.04f);
                RenderSettings.reflectionIntensity = 0.15f;
                RenderSettings.fog = true;
                RenderSettings.fogColor = NightFog;
                if (RenderSettings.fogMode == FogMode.Linear)
                {
                    RenderSettings.fogStartDistance = 6f;
                    RenderSettings.fogEndDistance = Mathf.Min(dayFogOn ? dayFogEnd : 999f, 75f);
                }
                else RenderSettings.fogDensity = Mathf.Max(dayFogDensity, 0.028f);
                if (nightSkybox != null) RenderSettings.skybox = nightSkybox;
            }
            else
            {
                RenderSettings.ambientMode = dayAmbientMode;
                RenderSettings.ambientSkyColor = daySky; RenderSettings.ambientEquatorColor = dayEquator;
                RenderSettings.ambientGroundColor = dayGround; RenderSettings.ambientLight = dayAmbient;
                RenderSettings.reflectionIntensity = dayReflection;
                RenderSettings.fog = dayFogOn; RenderSettings.fogColor = dayFog;
                RenderSettings.fogStartDistance = dayFogStart; RenderSettings.fogEndDistance = dayFogEnd;
                RenderSettings.fogDensity = dayFogDensity;
                if (daySkybox != null) RenderSettings.skybox = daySkybox;
            }
        }

        // The minimap is photographed once from above: in daylight, so the map stays readable.
        public static bool Daylight()
        {
            if (!isNight) return false;
            SetNight(false);
            return true;
        }

        public static void Night() { if (applied && !isNight) SetNight(true); }

        // Street lamps on the map (Quaternius street lights, lamp posts, wall lamps): a warm point light
        // just under the top of each, no shadows. Capped so a map full of lamps stays cheap.
        static void AddLamps()
        {
            const int MaxLamps = 32;
            var done = new HashSet<Transform>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (lamps.Count >= MaxLamps) break;
                if (r.GetComponentInParent<NetworkObject>() != null) continue;   // players, bots, pickups
                var root = LampRoot(r.transform);
                if (root == null || !done.Add(root)) continue;
                var b = new Bounds(root.position, Vector3.zero);
                bool any = false;
                foreach (var rr in root.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds);
                }
                if (!any || b.size.y < 1.2f) continue;   // a lamp post, not a table lamp
                var go = new GameObject("NightLamp");
                go.transform.SetParent(root, true);
                go.transform.position = new Vector3(b.center.x, b.max.y - 0.35f, b.center.z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = LampColor;
                l.intensity = 9f;
                l.range = 13f;
                l.shadows = LightShadows.None;
                lamps.Add(l);
            }
        }

        static Transform LampRoot(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                string n = p.name.ToLowerInvariant();
                if (n.Contains("streetlight") || n.Contains("street_light") || n.Contains("lamp") || n.Contains("lantern"))
                    return p;
            }
            return null;
        }

        static void EnsureHeadlamp()
        {
            if (headlamp != null) return;
            var nm = NetworkManager.Singleton;
            var me = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            var fps = me != null ? me.GetComponent<FPSController>() : null;
            if (fps == null || fps.playerCamera == null) return;
            var go = new GameObject("Headlamp");
            go.transform.SetParent(fps.playerCamera.transform, false);
            go.transform.localPosition = new Vector3(0.12f, 0.05f, 0.05f);
            go.transform.localRotation = Quaternion.Euler(4f, -2f, 0f);   // a touch down and inward
            headlamp = go.AddComponent<Light>();
            headlamp.type = LightType.Spot;
            headlamp.color = new Color(1f, 0.95f, 0.86f);
            headlamp.spotAngle = 62f;
            headlamp.innerSpotAngle = 26f;
            headlamp.range = 32f;
            headlamp.intensity = 20f;
            headlamp.shadows = LightShadows.None;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void SetC(Material m, string p, Color v) { if (m.HasProperty(p)) m.SetColor(p, v); }
    }
}
