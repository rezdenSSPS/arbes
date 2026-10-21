#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AirsoftFPS.Editor
{
    // Builds the low-poly "town" environment of the arena: grass, crossing roads with
    // sidewalks, houses / containers / park, a skyline of buildings outside the play area,
    // street furniture, sky, fog and post-processing.  Gameplay objects (spawns, NavMesh,
    // GameManager, HUD) are still created by AirsoftSceneBuilder.BuildArenaScene.
    public static class ArbesMapBuilder
    {
        const string Q = "Assets/ThirdParty/Quaternius/";
        const float Half = 40f;     // playable area is -40..40
        const float Road = 4f;      // road half-width
        const float Walk = 2.2f;    // sidewalk width

        static readonly Vector3[] Keep =
        {
            // player spawns
            new Vector3(-30,0,30), new Vector3(30,0,30), new Vector3(-30,0,-30), new Vector3(30,0,-30),
            new Vector3(0,0,35), new Vector3(0,0,-35), new Vector3(-35,0,0), new Vector3(35,0,0),
            // enemy spawns
            new Vector3(-25,0,25), new Vector3(25,0,-25), new Vector3(-25,0,-10), new Vector3(25,0,10), new Vector3(0,0,0)
        };

        static Transform root;
        static System.Random rng;
        static readonly Dictionary<string, Material> envMats = new Dictionary<string, Material>();

        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static bool Clear(Vector3 p, float r)
        {
            foreach (var k in Keep)
            {
                var d = p - k; d.y = 0;
                if (d.magnitude < r) return false;
            }
            return true;
        }

        static bool OnRoad(Vector3 p, float margin) =>
            Mathf.Abs(p.x) < Road + Walk + margin || Mathf.Abs(p.z) < Road + Walk + margin;

        // ───────────────────────── entry ─────────────────────────
        public static void BuildEnvironment()
        {
            rng = new System.Random(20261021);
            envMats.Clear();
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Env")) AssetDatabase.CreateFolder("Assets/Materials", "Env");
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");

            root = new GameObject("Environment").transform;

            Lighting();
            Ground();
            Roads();
            Boundary();
            Quadrants();
            StreetFurniture();
            Skyline();
            Physics.SyncTransforms();
            Scatter();
            Sky();
            PostProcessing();

            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── materials ─────────────────────────
        static Material Mat(string name, Color c, float smooth = 0.15f)
        {
            string path = "Assets/Materials/Env/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // FBX materials -> project URP materials (same colour / texture)
        static Material EnvMat(Material src, string model)
        {
            string n = src ? src.name : "Default";
            string key = (model + "_" + n).Replace(" ", "_").Replace("/", "_").Replace(".", "_");
            if (envMats.TryGetValue(key, out var cached)) return cached;
            Color c = Color.white;
            Texture tex = null;
            if (src)
            {
                if (src.HasProperty("_BaseColor")) c = src.GetColor("_BaseColor");
                else if (src.HasProperty("_Color")) c = src.GetColor("_Color");
                if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
            }
            var m = Mat(key, c, 0.1f);
            if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            envMats[key] = m;
            return m;
        }

        // ───────────────────────── primitives ─────────────────────────
        static GameObject Box(string name, Vector3 center, Vector3 size, Material m, float yaw = 0, bool collider = true, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent ? parent : root, false);
            g.transform.position = center;
            g.transform.rotation = Quaternion.Euler(0, yaw, 0);
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.isStatic = true;
            return g;
        }

        static GameObject Cyl(string name, Vector3 center, Vector3 size, Material m, Vector3 euler, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = center;
            g.transform.localEulerAngles = euler;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.isStatic = true;
            return g;
        }

        // ───────────────────────── FBX placement ─────────────────────────
        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // size = target height (byHeight) or target footprint (largest horizontal side)
        static GameObject Place(string rel, Vector3 pos, float yaw, float size, bool byHeight = true, bool collide = true, Transform parent = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Q + rel);
            if (prefab == null) { Debug.LogWarning("[Arbes] missing model " + rel); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent ? parent : root, false);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * prefab.transform.rotation;
            go.transform.localScale = prefab.transform.localScale;
            var b = WorldBounds(go);
            float cur = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            if (cur > 1e-4f) go.transform.localScale = prefab.transform.localScale * (size / cur);
            b = WorldBounds(go);
            go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = EnvMat(src[i], prefab.name);
                r.sharedMaterials = dst;
            }
            if (collide)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        // ───────────────────────── sections ─────────────────────────
        static Light sun;

        static void Lighting()
        {
            var lg = new GameObject("Sun");
            lg.transform.SetParent(root, false);
            sun = lg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            lg.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.52f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.22f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.88f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 260f;
        }

        static void Sky()
        {
            string path = "Assets/Materials/Env/ArbesSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Skybox/Procedural");
            if (sky == null && sh != null) { sky = new Material(sh); AssetDatabase.CreateAsset(sky, path); }
            if (sky == null) return;
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.95f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.44f, 0.40f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // a few low-poly clouds
            var cloudMat = Mat("Cloud", new Color(1f, 1f, 1f), 0f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f + R(-10, 10);
                float d = R(70, 140);
                var p = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * d, R(45, 70), Mathf.Cos(a * Mathf.Deg2Rad) * d);
                var c = Place("Props_Platformer/FBX/Cloud" + (1 + i % 5) + ".fbx", p, R(0, 360), R(18, 30), false, false);
                if (c)
                {
                    foreach (var r in c.GetComponentsInChildren<Renderer>())
                    {
                        var ms = r.sharedMaterials;
                        for (int k = 0; k < ms.Length; k++) ms[k] = cloudMat;
                        r.sharedMaterials = ms;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
            }
        }

        static void Ground()
        {
            var grass = Mat("Grass", new Color(0.36f, 0.55f, 0.26f), 0.05f);
            var g = Box("Ground", new Vector3(0, -0.5f, 0), new Vector3(240, 1, 240), grass);
            g.name = "Floor";
            // slightly darker grass patches for variation
            var grass2 = Mat("GrassDark", new Color(0.30f, 0.48f, 0.22f), 0.05f);
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(R(-Half, Half), 0.005f, R(-Half, Half));
                if (OnRoad(p, 1f)) continue;
                Box("GrassPatch", p, new Vector3(R(3, 8), 0.01f, R(3, 8)), grass2, R(0, 90), false);
            }
        }

        static void Roads()
        {
            var asphalt = Mat("Asphalt", new Color(0.20f, 0.21f, 0.23f), 0.2f);
            var walk = Mat("Sidewalk", new Color(0.62f, 0.61f, 0.58f), 0.1f);
            var curb = Mat("Curb", new Color(0.75f, 0.74f, 0.71f), 0.1f);
            var line = Mat("RoadLine", new Color(0.95f, 0.93f, 0.85f), 0.1f);
            var yellow = Mat("RoadYellow", new Color(0.95f, 0.75f, 0.15f), 0.1f);
            var roads = new GameObject("Roads").transform; roads.SetParent(root, false);

            float L = 2 * Half + 160f; // extend to the horizon
            // asphalt
            Box("RoadX", new Vector3(0, 0.01f, 0), new Vector3(L, 0.02f, Road * 2), asphalt, 0, false, roads);
            Box("RoadZ", new Vector3(0, 0.011f, 0), new Vector3(Road * 2, 0.02f, L), asphalt, 0, false, roads);

            // sidewalks (4 corners x 2 directions), raised 15 cm
            float inner = Road, outer = Road + Walk;
            float segLen = L / 2 - outer;
            float mid = outer + segLen / 2;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    // along X
                    Box("Sidewalk", new Vector3(sx * mid, 0.075f, sz * (inner + Walk / 2)), new Vector3(segLen, 0.15f, Walk), walk, 0, true, roads);
                    // along Z
                    Box("Sidewalk", new Vector3(sx * (inner + Walk / 2), 0.075f, sz * mid), new Vector3(Walk, 0.15f, segLen), walk, 0, true, roads);
                    // corner square
                    Box("SidewalkCorner", new Vector3(sx * (inner + Walk / 2), 0.075f, sz * (inner + Walk / 2)), new Vector3(Walk, 0.15f, Walk), walk, 0, true, roads);
                    // curb edges
                    Box("Curb", new Vector3(sx * mid, 0.09f, sz * (inner + 0.1f)), new Vector3(segLen, 0.18f, 0.2f), curb, 0, false, roads);
                    Box("Curb", new Vector3(sx * (inner + 0.1f), 0.09f, sz * mid), new Vector3(0.2f, 0.18f, segLen), curb, 0, false, roads);
                }

            // centre dashes
            for (float t = outer + 2; t < L / 2; t += 5f)
                foreach (int s in new[] { -1, 1 })
                {
                    Box("Dash", new Vector3(s * t, 0.025f, 0), new Vector3(2.4f, 0.01f, 0.18f), yellow, 0, false, roads);
                    Box("Dash", new Vector3(0, 0.026f, s * t), new Vector3(0.18f, 0.01f, 2.4f), yellow, 0, false, roads);
                }
            // zebra crossings around the intersection
            foreach (int s in new[] { -1, 1 })
                for (int i = -3; i <= 3; i++)
                {
                    Box("Zebra", new Vector3(i * 1.05f, 0.027f, s * (outer + 1.6f)), new Vector3(0.55f, 0.01f, 2.6f), line, 0, false, roads);
                    Box("Zebra", new Vector3(s * (outer + 1.6f), 0.027f, i * 1.05f), new Vector3(2.6f, 0.01f, 0.55f), line, 0, false, roads);
                }
        }

        static void Boundary()
        {
            var concrete = Mat("Concrete", new Color(0.66f, 0.64f, 0.60f), 0.1f);
            var stripe = Mat("BarrierStripe", new Color(0.85f, 0.25f, 0.15f), 0.1f);
            var b = new GameObject("Boundary").transform; b.SetParent(root, false);
            float h = 1.3f;
            // low visible walls + invisible tall blockers (keeps players and bots inside)
            Box("WallN", new Vector3(0, h / 2, Half), new Vector3(2 * Half + 1, h, 0.6f), concrete, 0, true, b);
            Box("WallS", new Vector3(0, h / 2, -Half), new Vector3(2 * Half + 1, h, 0.6f), concrete, 0, true, b);
            Box("WallE", new Vector3(Half, h / 2, 0), new Vector3(0.6f, h, 2 * Half + 1), concrete, 0, true, b);
            Box("WallW", new Vector3(-Half, h / 2, 0), new Vector3(0.6f, h, 2 * Half + 1), concrete, 0, true, b);
            foreach (var (p, s) in new[] {
                (new Vector3(0, 5, Half), new Vector3(2 * Half + 1, 10, 0.6f)), (new Vector3(0, 5, -Half), new Vector3(2 * Half + 1, 10, 0.6f)),
                (new Vector3(Half, 5, 0), new Vector3(0.6f, 10, 2 * Half + 1)), (new Vector3(-Half, 5, 0), new Vector3(0.6f, 10, 2 * Half + 1)) })
            {
                var inv = Box("InvisibleWall", p, s, concrete, 0, true, b);
                Object.DestroyImmediate(inv.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(inv.GetComponent<MeshFilter>());
            }
            // red/white stripes where the roads leave the arena
            foreach (var p in new[] { new Vector3(0, h + 0.05f, Half), new Vector3(0, h + 0.05f, -Half) })
                Box("Stripe", p, new Vector3(Road * 2, 0.1f, 0.62f), stripe, 0, false, b);
            foreach (var p in new[] { new Vector3(Half, h + 0.05f, 0), new Vector3(-Half, h + 0.05f, 0) })
                Box("Stripe", p, new Vector3(0.62f, 0.1f, Road * 2), stripe, 0, false, b);
        }

        // Shipping container made of boxes (corrugated look with ribs)
        static void Container(Vector3 basePos, float yaw, Color c)
        {
            var m = Mat("Container_" + ColorUtility.ToHtmlStringRGB(c), c, 0.25f);
            var dark = Mat("Container_" + ColorUtility.ToHtmlStringRGB(c) + "_D", Color.Lerp(c, Color.black, 0.3f), 0.25f);
            var g = new GameObject("Container").transform; g.SetParent(root, false);
            g.position = basePos; g.rotation = Quaternion.Euler(0, yaw, 0);
            var body = Box("Body", Vector3.zero, Vector3.one, m, 0, true, g);
            body.transform.localPosition = new Vector3(0, 1.3f, 0); body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(6f, 2.6f, 2.45f);
            for (int i = -5; i <= 5; i++)
            {
                foreach (int s in new[] { -1, 1 })
                {
                    var rib = Box("Rib", Vector3.zero, Vector3.one, dark, 0, false, g);
                    rib.transform.localPosition = new Vector3(i * 0.52f, 1.3f, s * 1.24f);
                    rib.transform.localRotation = Quaternion.identity;
                    rib.transform.localScale = new Vector3(0.12f, 2.5f, 0.06f);
                }
            }
            var door = Box("Doors", Vector3.zero, Vector3.one, dark, 0, false, g);
            door.transform.localPosition = new Vector3(3.01f, 1.3f, 0); door.transform.localRotation = Quaternion.identity;
            door.transform.localScale = new Vector3(0.04f, 2.4f, 2.3f);
        }

        // Simple low-poly car (cover on the roads)
        static void Car(Vector3 pos, float yaw, Color c)
        {
            var paint = Mat("Car_" + ColorUtility.ToHtmlStringRGB(c), c, 0.6f);
            var glass = Mat("CarGlass", new Color(0.18f, 0.24f, 0.30f), 0.9f);
            var tyre = Mat("Tyre", new Color(0.08f, 0.08f, 0.08f), 0.1f);
            var light = Mat("CarLight", new Color(1f, 0.95f, 0.7f), 0.8f);
            var g = new GameObject("Car").transform; g.SetParent(root, false);
            g.position = pos; g.rotation = Quaternion.Euler(0, yaw, 0);
            var body = Box("Body", Vector3.zero, Vector3.one, paint, 0, true, g);
            body.transform.localPosition = new Vector3(0, 0.75f, 0); body.transform.localRotation = Quaternion.identity; body.transform.localScale = new Vector3(1.9f, 0.75f, 4.3f);
            var cab = Box("Cabin", Vector3.zero, Vector3.one, glass, 0, true, g);
            cab.transform.localPosition = new Vector3(0, 1.4f, -0.2f); cab.transform.localRotation = Quaternion.identity; cab.transform.localScale = new Vector3(1.7f, 0.6f, 2.3f);
            var roof = Box("Roof", Vector3.zero, Vector3.one, paint, 0, false, g);
            roof.transform.localPosition = new Vector3(0, 1.72f, -0.25f); roof.transform.localRotation = Quaternion.identity; roof.transform.localScale = new Vector3(1.72f, 0.06f, 2.0f);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    Cyl("Wheel", new Vector3(sx * 0.92f, 0.38f, sz * 1.35f), new Vector3(0.76f, 0.14f, 0.76f), tyre, new Vector3(0, 0, 90), g);
            foreach (int sx in new[] { -1, 1 })
            {
                var hl = Box("Headlight", Vector3.zero, Vector3.one, light, 0, false, g);
                hl.transform.localPosition = new Vector3(sx * 0.6f, 0.85f, 2.16f); hl.transform.localRotation = Quaternion.identity; hl.transform.localScale = new Vector3(0.35f, 0.15f, 0.04f);
            }
        }

        static void Barrier(Vector3 pos, float yaw)
        {
            var concrete = Mat("Concrete", new Color(0.66f, 0.64f, 0.60f), 0.1f);
            var g = new GameObject("JerseyBarrier").transform; g.SetParent(root, false);
            g.position = pos; g.rotation = Quaternion.Euler(0, yaw, 0);
            var a = Box("Base", Vector3.zero, Vector3.one, concrete, 0, true, g);
            a.transform.localPosition = new Vector3(0, 0.25f, 0); a.transform.localRotation = Quaternion.identity; a.transform.localScale = new Vector3(3f, 0.5f, 0.7f);
            var b = Box("Top", Vector3.zero, Vector3.one, concrete, 0, true, g);
            b.transform.localPosition = new Vector3(0, 0.75f, 0); b.transform.localRotation = Quaternion.identity; b.transform.localScale = new Vector3(3f, 0.5f, 0.35f);
        }

        static void Sandbags(Vector3 pos, float yaw, int len)
        {
            var bag = Mat("Sandbag", new Color(0.72f, 0.64f, 0.46f), 0.05f);
            var g = new GameObject("Sandbags").transform; g.SetParent(root, false);
            g.position = pos; g.rotation = Quaternion.Euler(0, yaw, 0);
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < len; i++)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    s.name = "Bag";
                    s.transform.SetParent(g, false);
                    float off = (row % 2) * 0.35f;
                    s.transform.localPosition = new Vector3(-len * 0.35f + i * 0.7f + off, 0.17f + row * 0.3f, 0);
                    s.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    s.transform.localScale = new Vector3(0.36f, 0.38f, 0.5f);
                    s.GetComponent<Renderer>().sharedMaterial = bag;
                    Object.DestroyImmediate(s.GetComponent<Collider>());
                    s.isStatic = true;
                }
            // one simple collider for the whole wall
            var col = new GameObject("Collider"); col.transform.SetParent(g, false);
            col.transform.localPosition = new Vector3(0, 0.45f, 0);
            var bc = col.AddComponent<BoxCollider>(); bc.size = new Vector3(len * 0.7f + 0.3f, 0.9f, 0.55f);
            col.isStatic = true;
        }

        static void CrateStack(Vector3 p, int count)
        {
            float s = 1.2f;
            for (int i = 0; i < count; i++)
            {
                Vector3 q = p + new Vector3((i % 2) * (s + 0.05f), 0, (i / 2 % 2) * (s + 0.05f));
                Place("Props_Platformer/FBX/Crate.fbx", q, R(-6, 6), s);
            }
            if (count >= 3) Place("Props_Platformer/FBX/Crate.fbx", p + new Vector3(s * 0.5f, s, 0), R(0, 20), s);
        }

        static void Quadrants()
        {
            // ── NE: residential ──
            Place("Buildings/FBX/House1.fbx", new Vector3(17, 0, 25), 180, 7.5f);
            Place("Buildings/FBX/House2.fbx", new Vector3(32, 0, 20), -90, 7.5f);
            Place("Buildings/FBX/House2.fbx", new Vector3(13, 0, 13), 0, 6.5f);
            Sandbags(new Vector3(22, 0, 34), 0, 5);
            CrateStack(new Vector3(36, 0, 36), 3);

            // ── NW: container yard ──
            Container(new Vector3(-13, 0, 16), 0, new Color(0.75f, 0.25f, 0.18f));
            Container(new Vector3(-13, 2.6f, 16), 6, new Color(0.18f, 0.40f, 0.65f));
            Container(new Vector3(-32, 0, 14), 90, new Color(0.25f, 0.55f, 0.30f));
            Container(new Vector3(-19, 0, 32), 0, new Color(0.90f, 0.60f, 0.15f));
            Container(new Vector3(-36, 0, 34), 90, new Color(0.55f, 0.55f, 0.58f));
            CrateStack(new Vector3(-10, 0, 27), 4);
            CrateStack(new Vector3(-28, 0, 19), 2);
            Place("Props_Platformer/FBX/YellowBox.fbx", new Vector3(-19, 0, 23), 15, 1.1f);
            Place("Props_Platformer/FBX/YellowBox.fbx", new Vector3(-20.2f, 0, 23.4f), -10, 1.1f);

            // ── SE: park ──
            var park = new[] { new Vector3(12, 0, -13), new Vector3(19, 0, -33), new Vector3(33, 0, -13),
                               new Vector3(12, 0, -34), new Vector3(36, 0, -36), new Vector3(28, 0, -19), new Vector3(16, 0, -22) };
            for (int i = 0; i < park.Length; i++)
                Place("Nature/FBX/Tree" + (1 + i % 4) + ".fbx", park[i], R(0, 360), R(6.5f, 9f));
            Place("Nature/FBX/Rock1.fbx", new Vector3(21, 0, -15), R(0, 360), 1.6f, false);
            Place("Nature/FBX/Rock2.fbx", new Vector3(31, 0, -28), R(0, 360), 2.2f, false);
            Place("Nature/FBX/Rock3.fbx", new Vector3(10, 0, -26), R(0, 360), 1.8f, false);
            Sandbags(new Vector3(24, 0, -9), 0, 5);

            // ── SW: small buildings ──
            Place("Buildings/FBX/Building3_Small.fbx", new Vector3(-17, 0, -25), 0, 9f);
            Place("Buildings/FBX/Building2_Small.fbx", new Vector3(-33, 0, -16), 90, 8f);
            CrateStack(new Vector3(-12, 0, -13), 3);
            Container(new Vector3(-30, 0, -36), 0, new Color(0.65f, 0.20f, 0.20f));
            Sandbags(new Vector3(-24, 0, -32), 90, 4);

            // ── road cover ──
            Car(new Vector3(-2.1f, 0, 19), 4, new Color(0.80f, 0.15f, 0.15f));
            Car(new Vector3(2.1f, 0, -23), 178, new Color(0.15f, 0.35f, 0.75f));
            Car(new Vector3(18, 0, 2.1f), 92, new Color(0.92f, 0.92f, 0.9f));
            Car(new Vector3(-21, 0, -2.0f), -86, new Color(0.95f, 0.75f, 0.15f));
            Car(new Vector3(-12, 0, 2.2f), 70, new Color(0.2f, 0.2f, 0.22f));
            Barrier(new Vector3(1.5f, 0, 10), 90);
            Barrier(new Vector3(-1.5f, 0, -12), 90);
            Barrier(new Vector3(11, 0, -1.5f), 0);
            Barrier(new Vector3(-28, 0, 1.6f), 10);
            Barrier(new Vector3(28, 0, -1.8f), -8);
        }

        static void StreetFurniture()
        {
            float side = Road + Walk - 0.4f;
            // streetlights along both roads
            for (float t = 12; t < Half - 2; t += 12)
                foreach (int s in new[] { -1, 1 })
                {
                    Place("Street/FBX/Streetlight_Single.fbx", new Vector3(t * s, 0.15f, side * s), s > 0 ? 180 : 0, 5.5f);
                    Place("Street/FBX/Streetlight_Single.fbx", new Vector3(side * -s, 0.15f, t * s), s > 0 ? 90 : -90, 5.5f);
                }
            // traffic lights on the intersection corners
            Place("Street/FBX/TrafficLight.fbx", new Vector3(side, 0.15f, side), 225, 4.6f);
            Place("Street/FBX/TrafficLight.fbx", new Vector3(-side, 0.15f, -side), 45, 4.6f);
            Place("Street/FBX/TrafficLight_2.fbx", new Vector3(-side, 0.15f, side), 135, 4.6f);
            Place("Street/FBX/TrafficLight_2.fbx", new Vector3(side, 0.15f, -side), -45, 4.6f);
            Place("Street/FBX/Sign_Stop.fbx", new Vector3(side, 0.15f, 9), 180, 2.6f);
            Place("Street/FBX/Sign_NoParking.fbx", new Vector3(-side, 0.15f, -20), 0, 2.6f);
            Place("Street/FBX/Sign_Triangle.fbx", new Vector3(24, 0.15f, side), -90, 2.6f);
        }

        static readonly string[] BigBuildings =
        {
            "Buildings/FBX/Building1_Large.fbx", "Buildings/FBX/Building2_Large.fbx", "Buildings/FBX/Building3_Big.fbx",
            "Buildings/FBX/Building4.fbx", "Buildings/FBX/Building1_Small.fbx", "Buildings/FBX/Building2_Small.fbx",
            "Buildings/FBX/House1.fbx", "Buildings/FBX/House2.fbx"
        };

        static void Skyline()
        {
            var sky = new GameObject("Skyline").transform; sky.SetParent(root, false);
            int idx = 0;
            for (int side = 0; side < 4; side++)
            {
                for (float t = -58; t <= 58; t += 17)
                {
                    if (Mathf.Abs(t) < 12) continue; // keep the road gap open
                    float d = 55 + R(-2, 4);
                    Vector3 p; float yaw;
                    switch (side)
                    {
                        case 0: p = new Vector3(t, 0, d); yaw = 180; break;
                        case 1: p = new Vector3(t, 0, -d); yaw = 0; break;
                        case 2: p = new Vector3(d, 0, t); yaw = -90; break;
                        default: p = new Vector3(-d, 0, t); yaw = 90; break;
                    }
                    string m = BigBuildings[idx++ % BigBuildings.Length];
                    float h = m.Contains("House") ? 8f : m.Contains("Small") ? R(10, 13) : R(15, 24);
                    Place(m, p, yaw, h, true, false, sky);
                }
            }
            // trees between the arena wall and the skyline
            for (int i = 0; i < 40; i++)
            {
                float a = R(0, 360) * Mathf.Deg2Rad;
                float d = R(43, 50);
                var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * d;
                p.x = Mathf.Clamp(p.x, -50, 50); p.z = Mathf.Clamp(p.z, -50, 50);
                if (Mathf.Abs(p.x) < Road + Walk + 2 || Mathf.Abs(p.z) < Road + Walk + 2) continue;
                if (Mathf.Abs(p.x) < Half + 2 && Mathf.Abs(p.z) < Half + 2) continue;
                Place("Nature/FBX/Tree" + (1 + i % 4) + ".fbx", p, R(0, 360), R(6, 10), true, false, sky);
            }
        }

        static void Scatter()
        {
            var deco = new GameObject("Decoration").transform; deco.SetParent(root, false);
            // grass tufts (no collision)
            for (int i = 0; i < 160; i++)
            {
                var p = new Vector3(R(-Half + 1, Half - 1), 0, R(-Half + 1, Half - 1));
                if (OnRoad(p, 0.3f)) continue;
                var g = Place("Nature/FBX/Grass" + (1 + i % 3) + ".fbx", p, R(0, 360), R(0.35f, 0.6f), true, false, deco);
                if (g) foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            // bushes (soft cover, no collision so bots can walk through)
            int placed = 0;
            for (int i = 0; i < 80 && placed < 26; i++)
            {
                var p = new Vector3(R(-Half + 2, Half - 2), 0, R(-Half + 2, Half - 2));
                if (OnRoad(p, 1f) || !Clear(p, 4f)) continue;
                if (Physics.CheckSphere(p + Vector3.up * 0.8f, 1.2f)) continue;
                Place("Nature/FBX/Bush" + (1 + i % 3) + ".fbx", p, R(0, 360), R(1.0f, 1.6f), true, false, deco);
                placed++;
            }
            // a few rocks
            placed = 0;
            for (int i = 0; i < 60 && placed < 10; i++)
            {
                var p = new Vector3(R(-Half + 2, Half - 2), 0, R(-Half + 2, Half - 2));
                if (OnRoad(p, 1f) || !Clear(p, 5f)) continue;
                if (Physics.CheckSphere(p + Vector3.up * 0.8f, 1.6f)) continue;
                Place("Nature/FBX/Rock" + (1 + i % 3) + ".fbx", p, R(0, 360), R(1.0f, 1.8f), false, true, deco);
                placed++;
            }
        }

        static void PostProcessing()
        {
            string path = "Assets/Settings/ArbesArenaProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.45f);
            ca.contrast.Override(12f);
            ca.saturation.Override(14f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcess Volume");
            go.transform.SetParent(root, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 1;
            v.sharedProfile = profile;
        }
    }
}
#endif
