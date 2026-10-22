#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Builds the "Warehouse" map (Assets/Scenes/AirsoftWarehouse.unity): a 60 x 60 m close-quarters
    // airsoft site. A tall warehouse hall (pallet racking, a container, pallets, forklifts) and a
    // single-storey office block (corridor + six rooms with doorways and windows) form one building
    // across the middle; loading yards with trailers, containers and pallets lie north (Red) and
    // south (Blue), narrow alleys run past both ends. The layout is mirrored across z = 0, so both
    // teams get the same routes. Gameplay objects come from AirsoftSceneBuilder.BuildGameplayObjects.
    //
    // Also renders the top-down map-vote previews (Resources/MapPreviews/<scene>.png) of every map.
    //
    // Menu: Tools/Airsoft/Build Warehouse Map, Tools/Airsoft/Render Map Previews.
    // Batch: -executeMethod AirsoftFPS.Editor.AirsoftWarehouseBuilder.BuildWarehouse, then (separate run)
    //        -executeMethod AirsoftFPS.Editor.AirsoftWarehouseBuilder.RenderMapPreviews
    public static class AirsoftWarehouseBuilder
    {
        public const string ScenePath = "Assets/Scenes/AirsoftWarehouse.unity";
        const string NavMeshFolder = "Assets/Scenes/AirsoftWarehouse";
        const string MatFolder = "Assets/Materials/Warehouse";
        const string PreviewFolder = "Assets/Resources/MapPreviews";
        const string Q = "Assets/ThirdParty/Quaternius/";
        const float Half = 30f;          // playable area -30..30

        // building
        const float BX0 = -27f, BX1 = 27f, BZ = 13f;   // footprint x -27..27, z -13..13
        const float OfficeX = 5f;                      // hall x -27..5, office x 5..27
        const float HallH = 7f, OfficeH = 3.6f, CeilH = 3.2f;
        const float WallT = 0.3f, InnerT = 0.16f;

        // Team spawns: Red (1) north yard, Blue (2) south yard, mirrored.
        public static readonly (Vector3 pos, int team)[] PlayerSpawns =
        {
            (new Vector3(-20, 1, 27), 1), (new Vector3(-6, 1, 27.5f), 1), (new Vector3(9, 1, 27.5f), 1), (new Vector3(23, 1, 26.5f), 1),
            (new Vector3(-20, 1, -27), 2), (new Vector3(-6, 1, -27.5f), 2), (new Vector3(9, 1, -27.5f), 2), (new Vector3(23, 1, -26.5f), 2),
        };

        // Co-op bot spawns: inside the building and the alleys, away from both yards.
        public static readonly Vector3[] EnemySpawns =
        {
            new Vector3(-21, 1, 0), new Vector3(0, 1, 7), new Vector3(14, 1, 5),
            new Vector3(14, 1, -5), new Vector3(-28.5f, 1, -9), new Vector3(28.5f, 1, 9),
        };

        static Transform root;
        static System.Random rng;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ───────────────────────── entry points ─────────────────────────
        [MenuItem("Tools/Airsoft/Build Warehouse Map")]
        public static void BuildWarehouse()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildEnvironment();
            Physics.SyncTransforms();
            var nav = AirsoftSceneBuilder.BuildGameplayObjects(PlayerSpawns, EnemySpawns);
            BuildLayout();

            EditorSceneManager.SaveScene(scene, ScenePath);
            SaveNavMeshAsset(nav);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ObjectiveMapSetup.UpdateManifest(scene);   // lobby: which modes the Warehouse can play (also done on every save)
            AddToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            Debug.Log("[Airsoft] Warehouse map built: " + ScenePath + " (" + PlayerSpawns.Length + " team spawns, "
                      + EnemySpawns.Length + " enemy spawns), added to the build settings.");

            // The map-vote previews must come from a later editor tick: rendered in the same call that
            // built the scene, its lighting is not set up yet and the shot comes out washed out.
            // Batch mode quits before that, so run -executeMethod ...RenderMapPreviews as a second step.
            if (Application.isBatchMode)
                Debug.Log("[Airsoft] Batch mode: render the previews with AirsoftWarehouseBuilder.RenderMapPreviews.");
            else
                EditorApplication.delayCall += RenderMapPreviews;
        }

        [MenuItem("Tools/Airsoft/Render Map Previews")]
        public static void RenderMapPreviews()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string current = SceneManager.GetActiveScene().path;
            foreach (var m in MapCatalog.Maps)
                if (File.Exists(ArenaPathFor(m.scene))) RenderPreviewOfScene(m.scene);
            if (!string.IsNullOrEmpty(current) && File.Exists(current)) EditorSceneManager.OpenScene(current);
            Debug.Log("[Airsoft] Map previews rendered to " + PreviewFolder);
        }

        static string ArenaPathFor(string sceneName) => "Assets/Scenes/" + sceneName + ".unity";

        // Half the side of the square the preview shows (play area plus the boundary walls).
        static float PreviewHalf(string sceneName) => sceneName == "AirsoftWarehouse" ? Half + 1.5f : 42f;

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in list) if (s.path == path) { s.enabled = true; EditorBuildSettings.scenes = list.ToArray(); return; }
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // NavMeshSurface.BuildNavMesh only keeps the data in memory: store it next to the scene,
        // like the NavMeshSurface inspector's Bake button does.
        static void SaveNavMeshAsset(Unity.AI.Navigation.NavMeshSurface nav)
        {
            if (nav == null || nav.navMeshData == null) return;
            if (!AssetDatabase.IsValidFolder(NavMeshFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "AirsoftWarehouse");
            string path = NavMeshFolder + "/NavMesh-NavMeshSurface.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(nav.navMeshData, path);
            EditorUtility.SetDirty(nav);
        }

        // ───────────────────────── map layout (mode points) ─────────────────────────
        static void BuildLayout()
        {
            var go = new GameObject("MapLayout");
            var layout = go.AddComponent<MapLayout>();
            layout.displayName = "Warehouse";
            Transform P(string n, Vector3 p)
            {
                var t = new GameObject(n).transform;
                t.SetParent(go.transform, false);
                t.position = p;
                return t;
            }
            // flag stands: in each yard, behind a jersey barrier between the team's spawns
            layout.redBase = P("RedBase", new Vector3(0, 0, 25.5f));
            layout.blueBase = P("BlueBase", new Vector3(0, 0, -25.5f));
            // A: west hall between the racks, B: east hall by the forklifts, C: office corridor
            layout.controlPoints = new[]
            {
                P("ControlPoint_A", new Vector3(-21, 0, 0)),
                P("ControlPoint_B", new Vector3(-1.5f, 0, 0)),
                P("ControlPoint_C", new Vector3(16, 0, 0)),
            };
            // A: between the rack ends and the hall container, B: east end of the office corridor
            layout.bombSites = new[]
            {
                P("BombSite_A", new Vector3(-12.2f, 0, 0)),
                P("BombSite_B", new Vector3(23.5f, 0, 0)),
            };
            // VIP: from the south-east of the Blue yard through the building to the north-west corner
            layout.vipStart = P("VIPStart", new Vector3(6, 0, -25));
            layout.vipExtract = P("VIPExtract", new Vector3(-27, 0, 27));
        }

        // ───────────────────────── environment ─────────────────────────
        static void BuildEnvironment()
        {
            rng = new System.Random(20261022);
            mats.Clear();
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Warehouse");
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");

            root = new GameObject("Environment").transform;

            Lighting();
            Ground();
            Boundary();
            Hall();
            Office();
            Yards();
            Alleys();
            Skyline();
            Sky();
            PostProcessing();
            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── materials ─────────────────────────
        static Material Mat(string name, Color c, float smooth = 0.15f, float metal = 0f)
        {
            if (mats.TryGetValue(name, out var cached)) return cached;
            string path = MatFolder + "/WH_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static Material Glow(string name, Color c, float intensity)
        {
            var m = Mat(name, c, 0.2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
            EditorUtility.SetDirty(m);
            return m;
        }

        // FBX materials -> project URP materials (same colour / texture)
        static Material FbxMat(Material src, string model)
        {
            string n = src ? src.name : "Default";
            string key = ("Fbx_" + model + "_" + n).Replace(" ", "_").Replace("/", "_").Replace(".", "_");
            if (mats.TryGetValue(key, out var cached)) return cached;
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
            return m;
        }

        // ───────────────────────── primitives ─────────────────────────
        static GameObject Box(string name, Vector3 center, Vector3 size, Material m, Transform parent, float yaw = 0, bool collider = true)
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

        // Box in a group's local space (group carries position + yaw).
        static GameObject LBox(string name, Transform group, Vector3 local, Vector3 size, Material m, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(group, false);
            g.transform.localPosition = local;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.isStatic = true;
            return g;
        }

        static GameObject LCyl(string name, Transform group, Vector3 local, Vector3 size, Material m, Vector3 euler)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name;
            g.transform.SetParent(group, false);
            g.transform.localPosition = local;
            g.transform.localEulerAngles = euler;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.isStatic = true;
            return g;
        }

        static Transform Group(string name, Vector3 pos, float yaw, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent ? parent : root, false);
            t.position = pos;
            t.rotation = Quaternion.Euler(0, yaw, 0);
            return t;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // Quaternius model scaled to a height (byHeight) or footprint, standing on pos.
        static GameObject Place(string rel, Vector3 pos, float yaw, float size, bool byHeight, bool collide, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Q + rel);
            if (prefab == null) { Debug.LogWarning("[Airsoft] missing model " + rel); return null; }
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
                for (int i = 0; i < src.Length; i++) dst[i] = FbxMat(src[i], prefab.name);
                r.sharedMaterials = dst;
            }
            if (collide)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        // ───────────────────────── walls with openings ─────────────────────────
        struct Gap
        {
            public float at, w, bottom, top;
            public Gap(float at, float w, float bottom, float top) { this.at = at; this.w = w; this.bottom = bottom; this.top = top; }
        }

        static Gap Door(float at, float w = 1.5f, float h = 2.2f) => new Gap(at, w, 0f, h);
        static Gap Window(float at, float w, float sill = 1.0f, float top = 2.1f) => new Gap(at, w, sill, top);

        // Axis-aligned wall from a0 to a1 along X (at z = fixedCoord) or along Z (at x = fixedCoord),
        // built from solid pieces around its door / window openings.
        static void Wall(string name, bool alongX, float fixedCoord, float a0, float a1, float h, float t,
                         Material m, Transform parent, params Gap[] gaps)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent ? parent : root, false);
            void Seg(float s0, float s1, float y0, float y1)
            {
                if (s1 - s0 < 0.01f || y1 - y0 < 0.01f) return;
                float c = (s0 + s1) * 0.5f, y = (y0 + y1) * 0.5f;
                var pos = alongX ? new Vector3(c, y, fixedCoord) : new Vector3(fixedCoord, y, c);
                var size = alongX ? new Vector3(s1 - s0, y1 - y0, t) : new Vector3(t, y1 - y0, s1 - s0);
                Box("Piece", pos, size, m, g);
            }
            var sorted = new List<Gap>(gaps);
            sorted.Sort((p, q) => p.at.CompareTo(q.at));
            float cursor = a0;
            foreach (var gp in sorted)
            {
                float g0 = gp.at - gp.w * 0.5f, g1 = gp.at + gp.w * 0.5f;
                Seg(cursor, g0, 0f, h);
                Seg(g0, g1, 0f, gp.bottom);
                Seg(g0, g1, gp.top, h);
                cursor = g1;
            }
            Seg(cursor, a1, 0f, h);
        }

        // ───────────────────────── sections ─────────────────────────
        static void Lighting()
        {
            var lg = new GameObject("Sun");
            lg.transform.SetParent(root, false);
            var sun = lg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.90f, 0.78f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            lg.transform.rotation = Quaternion.Euler(38f, 125f, 0f);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.64f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.48f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.23f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.74f, 0.80f);
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 210f;
        }

        static void Sky()
        {
            // reuse the Town sky if it exists (not modified here), else make one
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Env/AirsoftSky.mat");
            if (sky == null)
            {
                string path = MatFolder + "/WH_Sky.mat";
                sky = AssetDatabase.LoadAssetAtPath<Material>(path);
                var sh = Shader.Find("Skybox/Procedural");
                if (sky == null && sh != null) { sky = new Material(sh); AssetDatabase.CreateAsset(sky, path); }
                if (sky != null)
                {
                    sky.SetFloat("_SunSize", 0.04f);
                    sky.SetFloat("_AtmosphereThickness", 0.9f);
                    sky.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.85f));
                    sky.SetFloat("_Exposure", 1.2f);
                    EditorUtility.SetDirty(sky);
                }
            }
            if (sky != null) RenderSettings.skybox = sky;
        }

        static void Ground()
        {
            var asphalt = Mat("YardAsphalt", new Color(0.30f, 0.31f, 0.32f), 0.15f);
            var g = Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(170, 1, 170), asphalt, root);
            g.name = "Floor";
            var patch = Mat("YardPatch", new Color(0.26f, 0.27f, 0.28f), 0.2f);
            var grass = Mat("Verge", new Color(0.33f, 0.47f, 0.25f), 0.05f);
            var paint = Mat("YardPaint", new Color(0.92f, 0.90f, 0.82f), 0.2f);
            var yellow = Mat("SafetyYellow", new Color(0.95f, 0.74f, 0.12f), 0.25f);
            var t = Group("GroundMarks", Vector3.zero, 0);
            // repaired asphalt patches in the yards
            for (int i = 0; i < 14; i++)
            {
                float z = R(15, 29) * (i % 2 == 0 ? 1 : -1);
                Box("Patch", new Vector3(R(-28, 28), 0.004f, z), new Vector3(R(2, 6), 0.01f, R(1.5f, 4)), patch, t, R(-8, 8), false);
            }
            // parking bays along the boundary walls (mirrored)
            foreach (int s in new[] { -1, 1 })
            {
                for (float x = -26; x <= 26; x += 3.2f)
                    Box("BayLine", new Vector3(x, 0.006f, s * 28.2f), new Vector3(0.12f, 0.01f, 3.4f), paint, t, 0, false);
                // yellow keep-clear hatching in front of the hall doors
                foreach (float dx in new[] { -19f, -3f })
                    for (int k = -2; k <= 2; k++)
                        Box("Hatch", new Vector3(dx + k * 1.0f, 0.007f, s * 14.6f), new Vector3(0.22f, 0.01f, 2.6f), yellow, t, 35 * s, false);
                Box("DoorLine", new Vector3(-11, 0.007f, s * 13.4f), new Vector3(30, 0.01f, 0.12f), yellow, t, 0, false);
            }
            // grass verge outside the site wall
            foreach (int s in new[] { -1, 1 })
            {
                Box("Verge", new Vector3(0, 0.003f, s * 36f), new Vector3(120, 0.01f, 10), grass, t, 0, false);
                Box("Verge", new Vector3(s * 36f, 0.003f, 0), new Vector3(10, 0.01f, 62), grass, t, 0, false);
            }
        }

        static void Boundary()
        {
            var concrete = Mat("SiteWall", new Color(0.62f, 0.61f, 0.58f), 0.1f);
            var cap = Mat("SiteWallCap", new Color(0.50f, 0.49f, 0.47f), 0.1f);
            var b = Group("Boundary", Vector3.zero, 0);
            float h = 2.8f, len = 2 * Half + 0.6f;
            Box("WallN", new Vector3(0, h / 2, Half), new Vector3(len, h, 0.4f), concrete, b);
            Box("WallS", new Vector3(0, h / 2, -Half), new Vector3(len, h, 0.4f), concrete, b);
            Box("WallE", new Vector3(Half, h / 2, 0), new Vector3(0.4f, h, len), concrete, b);
            Box("WallW", new Vector3(-Half, h / 2, 0), new Vector3(0.4f, h, len), concrete, b);
            Box("CapN", new Vector3(0, h + 0.06f, Half), new Vector3(len, 0.12f, 0.55f), cap, b, 0, false);
            Box("CapS", new Vector3(0, h + 0.06f, -Half), new Vector3(len, 0.12f, 0.55f), cap, b, 0, false);
            Box("CapE", new Vector3(Half, h + 0.06f, 0), new Vector3(0.55f, 0.12f, len), cap, b, 0, false);
            Box("CapW", new Vector3(-Half, h + 0.06f, 0), new Vector3(0.55f, 0.12f, len), cap, b, 0, false);
            // pillars every 6 m
            for (float a = -Half; a <= Half + 0.1f; a += 6f)
                foreach (int s in new[] { -1, 1 })
                {
                    Box("Pillar", new Vector3(a, h / 2 + 0.1f, s * Half), new Vector3(0.6f, h + 0.2f, 0.6f), cap, b, 0, false);
                    Box("Pillar", new Vector3(s * Half, h / 2 + 0.1f, a), new Vector3(0.6f, h + 0.2f, 0.6f), cap, b, 0, false);
                }
            // invisible tall blockers keep players and bots inside
            foreach (var (p, sz) in new[] {
                (new Vector3(0, 5, Half), new Vector3(len, 10, 0.4f)), (new Vector3(0, 5, -Half), new Vector3(len, 10, 0.4f)),
                (new Vector3(Half, 5, 0), new Vector3(0.4f, 10, len)), (new Vector3(-Half, 5, 0), new Vector3(0.4f, 10, len)) })
            {
                var inv = Box("InvisibleWall", p, sz, concrete, b);
                Object.DestroyImmediate(inv.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(inv.GetComponent<MeshFilter>());
            }
            // closed truck gates (visual) in the north and south walls
            var gate = Mat("Gate", new Color(0.20f, 0.36f, 0.30f), 0.4f, 0.3f);
            foreach (int s in new[] { -1, 1 })
            {
                var g = Group("Gate", new Vector3(14, 0, s * (Half - 0.25f)), 0, b);
                LBox("Leaf", g, new Vector3(0, 1.6f, 0), new Vector3(7.5f, 3.0f, 0.12f), gate, false);
                for (int i = -3; i <= 3; i++) LBox("Bar", g, new Vector3(i * 1.05f, 1.6f, -s * 0.08f), new Vector3(0.08f, 3.0f, 0.08f), cap, false);
            }
        }

        // ── Warehouse hall: x -27..5, z -13..13, 7 m high ──
        static void Hall()
        {
            var t = Group("Hall", Vector3.zero, 0);
            var wall = Mat("HallWall", new Color(0.66f, 0.70f, 0.72f), 0.3f, 0.2f);
            var plinth = Mat("Plinth", new Color(0.48f, 0.48f, 0.46f), 0.1f);
            var floor = Mat("HallFloor", new Color(0.52f, 0.52f, 0.50f), 0.35f);
            var steel = Mat("Steel", new Color(0.24f, 0.30f, 0.38f), 0.4f, 0.4f);
            var yellow = Mat("SafetyYellow", new Color(0.95f, 0.74f, 0.12f), 0.25f);
            var door = Mat("RollerDoor", new Color(0.78f, 0.36f, 0.16f), 0.3f, 0.2f);

            float x0 = BX0, x1 = OfficeX, h = HallH, e = WallT * 0.5f;
            Box("HallFloor", new Vector3((x0 + x1) / 2, 0.01f, 0), new Vector3(x1 - x0, 0.02f, 2 * BZ), floor, t, 0, false);

            // roller doors (4.5 x 4.5) at x -19 and -3, high window strips, mirrored north / south
            foreach (int s in new[] { -1, 1 })
            {
                Wall(s > 0 ? "WallN" : "WallS", true, s * BZ, x0 - e, x1, h, WallT, wall, t,
                    new Gap(-19, 4.5f, 0, 4.5f), new Gap(-3, 4.5f, 0, 4.5f),
                    Window(-25, 2.4f, 5.0f, 6.2f), Window(-11, 3.5f, 5.0f, 6.2f), Window(2, 2.4f, 5.0f, 6.2f));
                foreach (float dx in new[] { -19f, -3f })
                {
                    // rolled-up door drum and side guides
                    Box("DoorDrum", new Vector3(dx, 4.85f, s * (BZ - 0.45f)), new Vector3(4.9f, 0.7f, 0.6f), door, t, 0, false);
                    Box("Guide", new Vector3(dx - 2.32f, 2.25f, s * (BZ - 0.22f)), new Vector3(0.14f, 4.5f, 0.14f), steel, t, 0, false);
                    Box("Guide", new Vector3(dx + 2.32f, 2.25f, s * (BZ - 0.22f)), new Vector3(0.14f, 4.5f, 0.14f), steel, t, 0, false);
                    // bollards outside
                    foreach (float bx in new[] { dx - 2.7f, dx + 2.7f })
                    {
                        var bol = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        bol.name = "Bollard";
                        bol.transform.SetParent(t, false);
                        bol.transform.position = new Vector3(bx, 0.55f, s * (BZ + 0.5f));
                        bol.transform.localScale = new Vector3(0.25f, 0.55f, 0.25f);
                        bol.GetComponent<Renderer>().sharedMaterial = yellow;
                        bol.isStatic = true;
                    }
                }
                // plinth strip outside (visual)
                Box("Plinth", new Vector3((x0 + x1) / 2 - 0.15f, 0.45f, s * (BZ + 0.17f)), new Vector3(x1 - x0 + 0.3f, 0.9f, 0.05f), plinth, t, 0, false);
            }
            // west wall: personnel door on the centre line, high windows
            Wall("WallW", false, x0, -BZ - e, BZ + e, h, WallT, wall, t,
                Door(0, 1.6f, 2.3f), Window(-7, 3f, 5.0f, 6.2f), Window(7, 3f, 5.0f, 6.2f));
            Box("Plinth", new Vector3(x0 - 0.17f, 0.45f, 0), new Vector3(0.05f, 0.9f, 2 * BZ + 0.3f), plinth, t, 0, false);
            // wall to the office (x = 5): corridor doorway + a door into each corner room
            Wall("WallOffice", false, x1, -BZ, BZ, h, WallT, wall, t,
                Door(0, 2.4f, 2.6f), Door(8.2f), Door(-8.2f));

            // steel columns along the walls
            for (float x = -21; x <= 0; x += 6)
                foreach (int s in new[] { -1, 1 })
                    if (Mathf.Abs(x + 19) > 3f && Mathf.Abs(x + 3) > 3f)
                        Box("Column", new Vector3(x, h / 2, s * (BZ - 0.35f)), new Vector3(0.35f, h, 0.35f), steel, t);

            // racking: two rows per side, along x, forming aisles (mirrored)
            foreach (int s in new[] { -1, 1 })
            {
                Rack(new Vector3(-19.5f, 0, s * 7.6f), 11f, t);
                Rack(new Vector3(-19.5f, 0, s * 4.0f), 11f, t);
                // pallets in the north / south strips and between the rack ends and the container
                PalletStack(new Vector3(-23.5f, 0, s * 11.2f), 0, 2, t);
                PalletStack(new Vector3(-12.5f, 0, s * 10.6f), 90, 3, t);
                PalletStack(new Vector3(-12.0f, 0, s * 5.8f), 0, 1, t);
                // east hall: pallet stacks, a forklift each side
                PalletStack(new Vector3(-3.6f, 0, s * 8.0f), 0, 2, t);
                PalletStack(new Vector3(-2.2f, 0, s * 8.0f), 0, 1, t);
                PalletStack(new Vector3(1.8f, 0, s * 10.8f), 90, 3, t);
                PalletStack(new Vector3(2.4f, 0, s * 4.6f), 0, 2, t);
                Forklift(new Vector3(-5.2f, 0, s * 3.9f), 90, t);
                // floor markings: walkway edges and rack bay lines
                Box("Walkway", new Vector3(-17.5f, 0.022f, s * 3.25f), new Vector3(19, 0.01f, 0.12f), yellow, t, 0, false);
                Box("Walkway", new Vector3(-2.5f, 0.022f, s * 2.3f), new Vector3(14, 0.01f, 0.12f), yellow, t, 0, false);
            }
            // container in the middle of the hall, across the centre lane (blocks the long sightline)
            Container(new Vector3(-9f, 0, 0), 90, new Color(0.18f, 0.40f, 0.62f), t);

            // roof, trusses and lamps: hidden from the minimap / preview (MinimapHidden). The lamps are
            // emissive only: unshadowed point lights would shine through the roof and walls onto the yards.
            var roof = Group("HallRoof", Vector3.zero, 0, t);
            roof.gameObject.AddComponent<MinimapHidden>();
            var roofMat = Mat("Roof", new Color(0.45f, 0.47f, 0.48f), 0.2f);
            Box("Roof", new Vector3((x0 + x1) / 2 - 0.1f, h + 0.15f, 0), new Vector3(x1 - x0 + 0.5f, 0.3f, 2 * BZ + 0.6f), roofMat, roof, 0, false);
            var lampMat = Glow("Lamp", new Color(1f, 0.95f, 0.82f), 2.5f);
            for (float x = -24; x <= 3; x += 6)
            {
                Box("Truss", new Vector3(x, h - 0.35f, 0), new Vector3(0.25f, 0.5f, 2 * BZ), steel, roof, 0, false);
            }
            Box("Purlin", new Vector3(-11, h - 0.6f, 0), new Vector3(x1 - x0, 0.2f, 0.2f), steel, roof, 0, false);
            foreach (float x in new[] { -20.5f, -9f, 1f })
                foreach (int s in new[] { -1, 1 })
                {
                    var p = new Vector3(x, h - 1.0f, s * 6f);
                    Box("LampFixture", p, new Vector3(1.4f, 0.12f, 0.35f), lampMat, roof, 0, false);
                    Box("LampWire", p + Vector3.up * 0.4f, new Vector3(0.03f, 0.8f, 0.03f), steel, roof, 0, false);
                }
        }

        // Pallet racking along x: uprights, beams, two shelf levels; pallets on most bays.
        static void Rack(Vector3 center, float length, Transform parent)
        {
            var g = Group("Rack", center, 0, parent);
            var upright = Mat("RackUpright", new Color(0.16f, 0.32f, 0.62f), 0.35f, 0.3f);
            var beam = Mat("RackBeam", new Color(0.92f, 0.45f, 0.12f), 0.35f, 0.3f);
            var deck = Mat("RackDeck", new Color(0.55f, 0.56f, 0.56f), 0.2f, 0.4f);
            const float depth = 1.1f, height = 4.4f, bay = 2.75f;
            int bays = Mathf.Max(1, Mathf.RoundToInt(length / bay));
            float bw = length / bays;
            for (int i = 0; i <= bays; i++)
            {
                float x = -length / 2 + i * bw;
                foreach (int s in new[] { -1, 1 })
                    LBox("Upright", g, new Vector3(x, height / 2, s * (depth / 2 - 0.05f)), new Vector3(0.1f, height, 0.1f), upright);
            }
            foreach (float y in new[] { 1.55f, 3.1f })
            {
                foreach (int s in new[] { -1, 1 })
                    LBox("Beam", g, new Vector3(0, y, s * (depth / 2 - 0.05f)), new Vector3(length, 0.12f, 0.08f), beam, false);
                LBox("Deck", g, new Vector3(0, y + 0.07f, 0), new Vector3(length, 0.04f, depth - 0.1f), deck);
            }
            // loads: floor level, then the two shelves; some bays stay empty so you can shoot through
            for (int i = 0; i < bays; i++)
            {
                float x = -length / 2 + (i + 0.5f) * bw;
                float[] levels = { 0.02f, 1.63f, 3.18f };
                for (int k = 0; k < levels.Length; k++)
                {
                    if (rng.NextDouble() < (k == 0 ? 0.2 : 0.3)) continue;
                    PalletLoad(g, new Vector3(x - 0.35f, levels[k], 0), k == 2 ? 0.7f : R(0.6f, 1.25f));
                    if (rng.NextDouble() < 0.6) PalletLoad(g, new Vector3(x + 0.95f, levels[k], 0), k == 2 ? 0.7f : R(0.5f, 1.25f), 0.8f);
                }
            }
        }

        // One pallet with a load on it, in a group's local space.
        static void PalletLoad(Transform g, Vector3 local, float loadH, float width = 1.2f)
        {
            var wood = Mat("PalletWood", new Color(0.62f, 0.48f, 0.30f), 0.05f);
            int kind = rng.Next(3);
            var load = kind == 0 ? Mat("Cardboard", new Color(0.70f, 0.55f, 0.36f), 0.05f)
                     : kind == 1 ? Mat("ShrinkWrap", new Color(0.78f, 0.80f, 0.82f), 0.6f)
                     : Mat("BlueDrums", new Color(0.18f, 0.33f, 0.58f), 0.4f);
            LBox("Pallet", g, local + new Vector3(0, 0.07f, 0), new Vector3(width, 0.14f, 1.0f), wood);
            LBox("Load", g, local + new Vector3(0, 0.14f + loadH / 2, 0), new Vector3(width - 0.08f, loadH, 0.95f), load);
        }

        // Free-standing pallet stack (count pallets high, ~1.1 m each counting the load).
        static void PalletStack(Vector3 pos, float yaw, int count, Transform parent)
        {
            var g = Group("PalletStack", pos, yaw + R(-4, 4), parent);
            float y = 0;
            for (int i = 0; i < count; i++)
            {
                float lh = R(0.55f, 0.95f);
                PalletLoad(g, new Vector3(0, y, 0), lh);
                y += 0.14f + lh;
            }
        }

        static void Forklift(Vector3 pos, float yaw, Transform parent)
        {
            var g = Group("Forklift", pos, yaw, parent);
            var body = Mat("ForkliftYellow", new Color(0.95f, 0.70f, 0.10f), 0.45f);
            var dark = Mat("ForkliftDark", new Color(0.12f, 0.12f, 0.13f), 0.3f);
            var tyre = Mat("Tyre", new Color(0.07f, 0.07f, 0.07f), 0.1f);
            LBox("Chassis", g, new Vector3(0, 0.6f, 0), new Vector3(1.15f, 0.8f, 2.2f), body);
            LBox("Counterweight", g, new Vector3(0, 0.75f, -1.0f), new Vector3(1.15f, 0.9f, 0.5f), dark);
            LBox("Seat", g, new Vector3(0, 1.15f, -0.3f), new Vector3(0.6f, 0.35f, 0.6f), dark, false);
            foreach (int sx in new[] { -1, 1 })
            {
                LBox("Post", g, new Vector3(sx * 0.52f, 1.6f, 0.45f), new Vector3(0.07f, 1.4f, 0.07f), dark, false);
                LBox("Post", g, new Vector3(sx * 0.52f, 1.6f, -0.85f), new Vector3(0.07f, 1.4f, 0.07f), dark, false);
                LBox("Mast", g, new Vector3(sx * 0.38f, 1.4f, 1.18f), new Vector3(0.12f, 2.8f, 0.12f), dark);
                LBox("Fork", g, new Vector3(sx * 0.3f, 0.12f, 1.75f), new Vector3(0.12f, 0.06f, 1.1f), dark);
                foreach (float wz in new[] { 0.7f, -0.75f })
                    LCyl("Wheel", g, new Vector3(sx * 0.55f, 0.32f, wz), new Vector3(0.62f, 0.12f, 0.62f), tyre, new Vector3(0, 0, 90));
            }
            LBox("Guard", g, new Vector3(0, 2.32f, -0.2f), new Vector3(1.12f, 0.06f, 1.4f), dark, false);
        }

        // Shipping container from boxes (corrugated look with ribs), 6 x 2.6 x 2.45 m.
        static void Container(Vector3 basePos, float yaw, Color c, Transform parent)
        {
            string key = ColorUtility.ToHtmlStringRGB(c);
            var m = Mat("Container_" + key, c, 0.25f);
            var dark = Mat("Container_" + key + "_D", Color.Lerp(c, Color.black, 0.3f), 0.25f);
            var g = Group("Container", basePos, yaw, parent);
            LBox("Body", g, new Vector3(0, 1.3f, 0), new Vector3(6f, 2.6f, 2.45f), m);
            for (int i = -5; i <= 5; i++)
                foreach (int s in new[] { -1, 1 })
                    LBox("Rib", g, new Vector3(i * 0.52f, 1.3f, s * 1.24f), new Vector3(0.12f, 2.5f, 0.06f), dark, false);
            LBox("Doors", g, new Vector3(3.01f, 1.3f, 0), new Vector3(0.04f, 2.4f, 2.3f), dark, false);
            LBox("Rail", g, new Vector3(0, 2.62f, 0), new Vector3(6.02f, 0.06f, 2.47f), dark, false);
        }

        // ── Office block: x 5..27, z -13..13; corridor along z = 0, three rooms each side ──
        static void Office()
        {
            var t = Group("Office", Vector3.zero, 0);
            var outer = Mat("OfficeOuter", new Color(0.80f, 0.77f, 0.70f), 0.15f);
            var inner = Mat("OfficeInner", new Color(0.86f, 0.85f, 0.80f), 0.1f);
            var floor = Mat("OfficeFloor", new Color(0.42f, 0.46f, 0.44f), 0.3f);
            var trim = Mat("OfficeTrim", new Color(0.30f, 0.32f, 0.34f), 0.2f);
            float x0 = OfficeX, x1 = BX1, e = WallT * 0.5f;

            Box("OfficeFloor", new Vector3((x0 + x1) / 2, 0.012f, 0), new Vector3(x1 - x0, 0.02f, 2 * BZ), floor, t, 0, false);

            foreach (int s in new[] { -1, 1 })
            {
                // outer wall to the yard: door into the middle room, windows into the other two
                Wall(s > 0 ? "OuterN" : "OuterS", true, s * BZ, x0, x1 + e, OfficeH, WallT, outer, t,
                    Window(8.8f, 2.2f), Door(16.2f), Window(23.5f, 2.2f));
                Box("Sill", new Vector3(8.8f, 0.97f, s * (BZ + 0.2f)), new Vector3(2.4f, 0.06f, 0.2f), trim, t, 0, false);
                Box("Sill", new Vector3(23.5f, 0.97f, s * (BZ + 0.2f)), new Vector3(2.4f, 0.06f, 0.2f), trim, t, 0, false);
                // corridor walls (z = +-1.5): a door into each room
                Wall("Corridor", true, s * 1.5f, x0, x1, CeilH, InnerT, inner, t,
                    Door(9f), Door(16.2f), Door(23.5f));
                // room dividers (x = 12.5 and 20) with connecting doors
                Wall("Divider", false, 12.5f, s > 0 ? 1.5f : -BZ, s > 0 ? BZ : -1.5f, CeilH, InnerT, inner, t, Door(s * 10.5f));
                Wall("Divider", false, 20f, s > 0 ? 1.5f : -BZ, s > 0 ? BZ : -1.5f, CeilH, InnerT, inner, t, Door(s * 4.6f));
                // window in the east wall of the end rooms is handled below
                Furniture(s, t);
            }
            // east wall: corridor exit to the east alley, windows into the end rooms
            Wall("OuterE", false, x1, -BZ - e, BZ + e, OfficeH, WallT, outer, t,
                Door(0, 1.6f, 2.3f), Window(9.5f, 2.2f), Window(-9.5f, 2.2f));
            // the hall wall at x = 5 is part of Hall()

            // ceiling + flat roof: hidden from the minimap / preview
            var roof = Group("OfficeRoof", Vector3.zero, 0, t);
            roof.gameObject.AddComponent<MinimapHidden>();
            var ceil = Mat("Ceiling", new Color(0.88f, 0.88f, 0.86f), 0.1f);
            var roofMat = Mat("OfficeRoofTop", new Color(0.36f, 0.36f, 0.37f), 0.1f);
            Box("Ceiling", new Vector3((x0 + x1) / 2, CeilH + 0.06f, 0), new Vector3(x1 - x0, 0.12f, 2 * BZ), ceil, roof, 0, false);
            Box("RoofTop", new Vector3((x0 + x1) / 2 + e / 2, OfficeH - 0.1f, 0), new Vector3(x1 - x0 + e, 0.2f, 2 * BZ + WallT), roofMat, roof, 0, false);
            Box("AirCon", new Vector3(18, OfficeH + 0.5f, 4), new Vector3(2.2f, 1.0f, 1.4f), trim, roof, 0, false);
            Box("AirCon", new Vector3(11, OfficeH + 0.5f, -5), new Vector3(2.2f, 1.0f, 1.4f), trim, roof, 0, false);
            var panel = Glow("CeilingPanel", new Color(1f, 0.97f, 0.9f), 2.0f);
            foreach (var p in new[] { new Vector3(8.8f, 0, 7), new Vector3(16.2f, 0, 7), new Vector3(23.5f, 0, 7),
                                      new Vector3(8.8f, 0, -7), new Vector3(16.2f, 0, -7), new Vector3(23.5f, 0, -7),
                                      new Vector3(10, 0, 0), new Vector3(21, 0, 0) })
            {
                var lp = new Vector3(p.x, CeilH - 0.02f, p.z);
                Box("Panel", lp, new Vector3(1.2f, 0.04f, 0.6f), panel, roof, 0, false);
            }

            // corridor clutter: two vending machines (half cover, point-symmetric)
            var vend = Mat("Vending", new Color(0.70f, 0.12f, 0.12f), 0.5f);
            Box("Vending", new Vector3(13.6f, 0.95f, -1.05f), new Vector3(1.0f, 1.9f, 0.75f), vend, t);
            Box("Vending", new Vector3(19.0f, 0.95f, 1.05f), new Vector3(1.0f, 1.9f, 0.75f), vend, t);
        }

        // Furniture of the three rooms on one side (s = +1 north, -1 south).
        static void Furniture(int s, Transform parent)
        {
            var desk = Mat("Desk", new Color(0.55f, 0.42f, 0.30f), 0.25f);
            var metal = Mat("Cabinet", new Color(0.48f, 0.52f, 0.55f), 0.35f, 0.3f);
            var screen = Mat("Screen", new Color(0.08f, 0.09f, 0.11f), 0.8f);
            var server = Mat("ServerRack", new Color(0.13f, 0.14f, 0.16f), 0.4f);
            var led = Glow("ServerLed", new Color(0.3f, 1f, 0.45f), 2f);

            void Desk(float x, float z, float yaw)
            {
                var g = Group("Desk", new Vector3(x, 0, s * z), yaw, parent);
                LBox("Top", g, new Vector3(0, 0.74f, 0), new Vector3(1.6f, 0.05f, 0.8f), desk);
                LBox("Panel", g, new Vector3(0, 0.38f, 0.3f), new Vector3(1.55f, 0.7f, 0.05f), desk);
                LBox("SideL", g, new Vector3(-0.77f, 0.37f, 0), new Vector3(0.05f, 0.72f, 0.78f), desk);
                LBox("SideR", g, new Vector3(0.77f, 0.37f, 0), new Vector3(0.05f, 0.72f, 0.78f), desk);
                LBox("Monitor", g, new Vector3(0.1f, 0.98f, 0.15f), new Vector3(0.55f, 0.36f, 0.04f), screen, false);
            }

            // room 1 (x 5..12.5): open office, four desks
            Desk(7.4f, 5.2f, 0); Desk(10.2f, 5.2f, 0); Desk(7.4f, 9.6f, 180); Desk(10.2f, 9.6f, 180);
            Box("Cabinet", new Vector3(11.9f, 0.65f, s * 12.4f), new Vector3(0.9f, 1.3f, 0.6f), metal, parent);
            // room 2 (x 12.5..20): meeting room, long table and cabinets
            Box("Table", new Vector3(16.6f, 0.74f, s * 7.6f), new Vector3(1.4f, 0.06f, 4.0f), desk, parent);
            Box("TableBase", new Vector3(16.6f, 0.36f, s * 7.6f), new Vector3(0.9f, 0.72f, 3.2f), desk, parent);
            Box("Cabinet", new Vector3(14.4f, 0.65f, s * 12.5f), new Vector3(1.8f, 1.3f, 0.6f), metal, parent);
            Box("Whiteboard", new Vector3(19.85f, 1.6f, s * 9.5f), new Vector3(0.04f, 1.1f, 2.2f), Mat("Whiteboard", Color.white, 0.6f), parent, 0, false);
            // room 3 (x 20..27): server / storage room, racks and boxes
            for (int i = 0; i < 3; i++)
            {
                var g = Group("ServerRack", new Vector3(21.6f + i * 0.85f, 0, s * 8.6f), 0, parent);
                LBox("Body", g, new Vector3(0, 1.0f, 0), new Vector3(0.8f, 2.0f, 1.0f), server);
                LBox("Leds", g, new Vector3(0, 1.4f, -s * 0.51f), new Vector3(0.5f, 0.6f, 0.02f), led, false);
            }
            PalletStack(new Vector3(25.4f, 0, s * 4.2f), 0, 1, parent);
            Box("Boxes", new Vector3(25.8f, 0.45f, s * 11.8f), new Vector3(1.2f, 0.9f, 1.4f), Mat("Cardboard", new Color(0.70f, 0.55f, 0.36f), 0.05f), parent);
        }

        // ── Loading yards, north (Red) and south (Blue), mirrored ──
        static void Yards()
        {
            foreach (int s in new[] { -1, 1 })
            {
                var t = Group(s > 0 ? "YardNorth" : "YardSouth", Vector3.zero, 0);
                // semi-trailer + truck west of the middle (blocks the view out of the x -3 roller door)
                Trailer(new Vector3(-10f, 0, s * 18.5f), 0, s > 0 ? new Color(0.85f, 0.85f, 0.83f) : new Color(0.80f, 0.20f, 0.16f), t);
                Truck(new Vector3(-2.2f, 0, s * 18.5f), 0, s > 0 ? new Color(0.15f, 0.35f, 0.70f) : new Color(0.20f, 0.45f, 0.25f), t);
                // containers: one across the west end, a two-high stack in front of the office
                Container(new Vector3(-24f, 0, s * 22f), 90, new Color(0.75f, 0.25f, 0.18f), t);
                Container(new Vector3(17f, 0, s * 20f), 0, new Color(0.25f, 0.55f, 0.30f), t);
                Container(new Vector3(17f, 2.6f, s * 20f), s * 5f, new Color(0.90f, 0.60f, 0.15f), t);
                Container(new Vector3(26f, 0, s * 18.6f), 90, new Color(0.55f, 0.55f, 0.58f), t);
                // pallets, crates, barriers
                PalletStack(new Vector3(6f, 0, s * 17.2f), 0, 2, t);
                PalletStack(new Vector3(7.4f, 0, s * 17.2f), 0, 1, t);
                PalletStack(new Vector3(9f, 0, s * 23.2f), 90, 2, t);
                PalletStack(new Vector3(-14.5f, 0, s * 24.5f), 0, 2, t);
                PalletStack(new Vector3(-19.5f, 0, s * 23.2f), 0, 2, t);
                PalletStack(new Vector3(-21f, 0, s * 15.8f), 90, 1, t);
                Barrier(new Vector3(0f, 0, s * 22.5f), 0, t);
                Barrier(new Vector3(-6.5f, 0, s * 23.5f), 8 * s, t);
                Barrier(new Vector3(12.5f, 0, s * 15.6f), 90, t);
                CrateStack(new Vector3(-27.6f, 0, s * 16f), t);
                CrateStack(new Vector3(22f, 0, s * 24.5f), t);
                // yard lights in the corners
                Place("Street/FBX/Streetlight_Single.fbx", new Vector3(-28.8f, 0, s * 28.8f), s > 0 ? 135 : 45, 5.5f, true, true, t);
                Place("Street/FBX/Streetlight_Single.fbx", new Vector3(28.8f, 0, s * 28.8f), s > 0 ? -135 : -45, 5.5f, true, true, t);
            }
        }

        static void Trailer(Vector3 pos, float yaw, Color c, Transform parent)
        {
            var g = Group("Trailer", pos, yaw, parent);
            var body = Mat("Trailer_" + ColorUtility.ToHtmlStringRGB(c), c, 0.35f);
            var dark = Mat("TrailerChassis", new Color(0.14f, 0.14f, 0.15f), 0.3f);
            var tyre = Mat("Tyre", new Color(0.07f, 0.07f, 0.07f), 0.1f);
            LBox("Box", g, new Vector3(0, 2.6f, 0), new Vector3(12f, 2.7f, 2.5f), body);
            LBox("Chassis", g, new Vector3(0, 1.05f, 0), new Vector3(12f, 0.3f, 1.0f), dark);
            for (int i = -5; i <= 5; i++)
                foreach (int sz in new[] { -1, 1 })
                    LBox("Rib", g, new Vector3(i * 1.08f, 2.6f, sz * 1.26f), new Vector3(0.08f, 2.66f, 0.04f), dark, false);
            // rear axles (one collider block) and front landing legs
            LBox("Axles", g, new Vector3(-4.6f, 0.55f, 0), new Vector3(2.6f, 1.1f, 2.3f), dark).GetComponent<Renderer>().enabled = false;
            foreach (float ax in new[] { -5.2f, -4.0f })
                foreach (int sz in new[] { -1, 1 })
                    LCyl("Wheel", g, new Vector3(ax, 0.5f, sz * 0.95f), new Vector3(1.0f, 0.2f, 1.0f), tyre, new Vector3(90, 0, 0));
            foreach (int sz in new[] { -1, 1 })
                LBox("Leg", g, new Vector3(3.6f, 0.6f, sz * 0.7f), new Vector3(0.15f, 1.2f, 0.15f), dark);
        }

        static void Truck(Vector3 pos, float yaw, Color c, Transform parent)
        {
            var g = Group("Truck", pos, yaw, parent);
            var paint = Mat("Truck_" + ColorUtility.ToHtmlStringRGB(c), c, 0.55f);
            var glass = Mat("TruckGlass", new Color(0.18f, 0.24f, 0.30f), 0.9f);
            var dark = Mat("TrailerChassis", new Color(0.14f, 0.14f, 0.15f), 0.3f);
            var tyre = Mat("Tyre", new Color(0.07f, 0.07f, 0.07f), 0.1f);
            LBox("Frame", g, new Vector3(-0.6f, 0.9f, 0), new Vector3(4.2f, 0.4f, 1.1f), dark);
            LBox("Cab", g, new Vector3(0.6f, 2.15f, 0), new Vector3(1.8f, 2.1f, 2.4f), paint);
            LBox("Windshield", g, new Vector3(1.51f, 2.55f, 0), new Vector3(0.04f, 0.9f, 2.1f), glass, false);
            LBox("Bumper", g, new Vector3(1.55f, 0.75f, 0), new Vector3(0.2f, 0.5f, 2.4f), dark);
            foreach (float ax in new[] { 0.8f, -1.6f })
                foreach (int sz in new[] { -1, 1 })
                    LCyl("Wheel", g, new Vector3(ax, 0.5f, sz * 1.05f), new Vector3(1.0f, 0.18f, 1.0f), tyre, new Vector3(90, 0, 0));
            LBox("Wheels", g, new Vector3(-0.4f, 0.5f, 0), new Vector3(3.4f, 1.0f, 2.3f), dark).GetComponent<Renderer>().enabled = false;
        }

        static void Barrier(Vector3 pos, float yaw, Transform parent)
        {
            var concrete = Mat("Barrier", new Color(0.66f, 0.64f, 0.60f), 0.1f);
            var g = Group("JerseyBarrier", pos, yaw, parent);
            LBox("Base", g, new Vector3(0, 0.25f, 0), new Vector3(3f, 0.5f, 0.7f), concrete);
            LBox("Top", g, new Vector3(0, 0.75f, 0), new Vector3(3f, 0.5f, 0.35f), concrete);
        }

        static void CrateStack(Vector3 p, Transform parent)
        {
            float s = 1.15f;
            for (int i = 0; i < 3; i++)
                Place("Props_Platformer/FBX/Crate.fbx", p + new Vector3((i % 2) * (s + 0.05f) - s * 0.5f, 0, (i / 2) * (s + 0.05f) - s * 0.5f), R(-6, 6), s, false, true, parent);
            Place("Props_Platformer/FBX/Crate.fbx", p + new Vector3(0, s, -s * 0.5f), R(0, 20), s, false, true, parent);
        }

        // ── Alleys past the building ends (x -30..-27 and 27..30) ──
        static void Alleys()
        {
            var t = Group("Alleys", Vector3.zero, 0);
            var bin = Mat("Dumpster", new Color(0.18f, 0.42f, 0.30f), 0.35f, 0.2f);
            var lid = Mat("DumpsterLid", new Color(0.12f, 0.12f, 0.12f), 0.3f);
            foreach (int s in new[] { -1, 1 })
            {
                // west: pallets; east: dumpsters (each half cover, leaves a gap to pass)
                PalletStack(new Vector3(-28.9f, 0, s * 6.5f), 90, 2, t);
                var d = Group("Dumpster", new Vector3(28.95f, 0, s * 6.5f), 0, t);
                LBox("Body", d, new Vector3(0, 0.65f, 0), new Vector3(1.2f, 1.3f, 2.0f), bin);
                LBox("Lid", d, new Vector3(0, 1.33f, 0), new Vector3(1.25f, 0.06f, 2.05f), lid, false);
            }
            // gas bottles cage on the west wall, by the hall door
            var cage = Mat("Cage", new Color(0.35f, 0.38f, 0.36f), 0.3f, 0.5f);
            Box("GasCage", new Vector3(-28.9f, 0.9f, -2.4f), new Vector3(1.4f, 1.8f, 1.6f), cage, t);
        }

        static readonly string[] BigBuildings =
        {
            "Buildings/FBX/Building1_Large.fbx", "Buildings/FBX/Building2_Large.fbx", "Buildings/FBX/Building3_Big.fbx",
            "Buildings/FBX/Building4.fbx", "Buildings/FBX/Building1_Small.fbx", "Buildings/FBX/Building2_Small.fbx",
        };

        // Industrial estate beyond the site wall (no collision).
        static void Skyline()
        {
            var sky = Group("Skyline", Vector3.zero, 0);
            int idx = 0;
            for (int side = 0; side < 4; side++)
            {
                for (float a = -52; a <= 52; a += 16)
                {
                    float d = 48 + R(-2, 5);
                    Vector3 p; float yaw;
                    switch (side)
                    {
                        case 0: p = new Vector3(a, 0, d); yaw = 180; break;
                        case 1: p = new Vector3(a, 0, -d); yaw = 0; break;
                        case 2: p = new Vector3(d, 0, a); yaw = -90; break;
                        default: p = new Vector3(-d, 0, a); yaw = 90; break;
                    }
                    string m = BigBuildings[idx++ % BigBuildings.Length];
                    float h = m.Contains("Small") ? R(9, 12) : R(14, 22);
                    Place(m, p, yaw, h, true, false, sky);
                }
            }
            // warehouses next door (big plain sheds)
            var shed = Mat("ShedWall", new Color(0.58f, 0.62f, 0.66f), 0.2f);
            var shedRoof = Mat("ShedRoof", new Color(0.40f, 0.30f, 0.26f), 0.2f);
            foreach (var (p, sz) in new[] { (new Vector3(-24, 0, 39), new Vector3(26, 9, 10)), (new Vector3(22, 0, -39), new Vector3(28, 8, 10)),
                                             (new Vector3(39, 0, 12), new Vector3(10, 10, 24)), (new Vector3(-39, 0, -14), new Vector3(10, 8, 26)) })
            {
                Box("Shed", p + Vector3.up * sz.y / 2, sz, shed, sky, 0, false);
                Box("ShedRoof", p + Vector3.up * (sz.y + 0.15f), sz + new Vector3(0.6f, -sz.y + 0.3f, 0.6f), shedRoof, sky, 0, false);
            }
            // trees on the verge
            for (int i = 0; i < 34; i++)
            {
                float a = R(0, 360) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * R(33, 42);
                p.x = Mathf.Clamp(p.x, -40, 40); p.z = Mathf.Clamp(p.z, -40, 40);
                if (Mathf.Abs(p.x) < Half + 2.5f && Mathf.Abs(p.z) < Half + 2.5f) continue;
                Place("Nature/FBX/Tree" + (1 + i % 4) + ".fbx", p, R(0, 360), R(6, 9.5f), true, false, sky);
            }
        }

        static void PostProcessing()
        {
            string path = "Assets/Settings/AirsoftWarehouseProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.6f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.55f);
            ca.contrast.Override(14f);
            ca.saturation.Override(4f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.25f);
            vig.smoothness.Override(0.45f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(-4f);
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

        // ───────────────────────── map previews ─────────────────────────
        static void RenderPreviewOfScene(string sceneName)
        {
            EditorSceneManager.OpenScene(ArenaPathFor(sceneName), OpenSceneMode.Single);
            RenderPreviewOfOpenScene(sceneName, PreviewHalf(sceneName));
        }

        // Top-down orthographic shot of the open scene (roofs hidden), saved as
        // Resources/MapPreviews/<sceneName>.png for the lobby map cards.
        static void RenderPreviewOfOpenScene(string sceneName, float half)
        {
            const int size = 512;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(PreviewFolder)) AssetDatabase.CreateFolder("Assets/Resources", "MapPreviews");
            string path = PreviewFolder + "/" + sceneName + ".png";

            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var go = new GameObject("PreviewCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.125f, 0.145f, 0.11f);
            cam.targetTexture = rt;
            go.transform.SetPositionAndRotation(new Vector3(0f, 120f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.antialiasing = AntialiasingMode.None;

            var hidden = new List<Renderer>();
            foreach (var mh in Object.FindObjectsByType<MinimapHidden>(FindObjectsInactive.Exclude))
                foreach (var r in mh.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderSettings.fog = fog;
                foreach (var r in hidden) if (r != null) r.enabled = true;
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Debug.Log("[Airsoft] Map preview rendered: " + path);
        }
    }
}
#endif
