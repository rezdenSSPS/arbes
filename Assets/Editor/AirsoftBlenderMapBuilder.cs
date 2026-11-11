#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AirsoftFPS.Editor
{
    // Builds the maps modelled in Blender (tools/Blender/build_*.py → Assets/Models/Maps/<Map>.fbx) into
    // playable scenes: "Outpost" (AirsoftOutpost.unity, the walled 68 x 47 m compound from the design PDF)
    // and "Forest" (AirsoftForest.unity, 80 x 80 m woodland).
    //
    // The FBX carries everything the scene needs, by object name:
    //   "VIS_..."  visual only (no collider)       "MMH_..." visual only, hidden on the minimap / preview
    //   "GP_..."   gameplay points (empties):  GP_Spawn_R_n / GP_Spawn_B_n / GP_Spawn_F_n (Red / Blue / anyone),
    //              GP_Enemy_n (co-op bots), GP_RedBase, GP_BlueBase, GP_CP_A..C, GP_Bomb_A..B, GP_VipStart,
    //              GP_VipExtract. Each is dropped onto the ground below it.
    //   "Probe_*"  axis check (Blender east / north / up must come out as Unity +X / +Z / +Y)
    //   anything else gets a MeshCollider. Blender material names become URP materials "<Map>_<name>", so
    //   footstep / impact sounds still work by name (Surfaces.cs).
    //
    // Menu: Tools/Airsoft/Build Outpost Map, Tools/Airsoft/Build Forest Map.
    // Batch: -executeMethod AirsoftFPS.Editor.AirsoftBlenderMapBuilder.BuildAll, then (separate run)
    //        -executeMethod AirsoftFPS.Editor.AirsoftWarehouseBuilder.RenderMapPreviews
    public static class AirsoftBlenderMapBuilder
    {
        class Config
        {
            public string map;          // "Outpost": FBX, display name, material prefix
            public string scene;        // scene name
            public float halfX, halfZ;  // invisible boundary walls
            public float previewHalf;   // half side of the top-down map-vote preview
            public Color sun, fogColor, ambSky, ambEquator, ambGround;
            public float sunIntensity, sunPitch, sunYaw, fogStart, fogEnd;
            public float exposure, saturation, temperature;
        }

        static readonly Config Outpost = new Config
        {
            map = "Outpost", scene = "AirsoftOutpost", halfX = 34f, halfZ = 23.5f, previewHalf = 36f,
            sun = new Color(1f, 0.90f, 0.76f), sunIntensity = 1.4f, sunPitch = 46f, sunYaw = 140f,
            ambSky = new Color(0.60f, 0.64f, 0.72f), ambEquator = new Color(0.52f, 0.49f, 0.44f), ambGround = new Color(0.30f, 0.27f, 0.22f),
            fogColor = new Color(0.78f, 0.74f, 0.66f), fogStart = 45f, fogEnd = 190f,
            exposure = 0.5f, saturation = 2f, temperature = 6f,
        };

        static readonly Config Forest = new Config
        {
            map = "Forest", scene = "AirsoftForest", halfX = 40f, halfZ = 40f, previewHalf = 42f,
            sun = new Color(1f, 0.93f, 0.80f), sunIntensity = 1.25f, sunPitch = 52f, sunYaw = 115f,
            ambSky = new Color(0.52f, 0.60f, 0.66f), ambEquator = new Color(0.36f, 0.42f, 0.33f), ambGround = new Color(0.18f, 0.20f, 0.14f),
            fogColor = new Color(0.58f, 0.66f, 0.62f), fogStart = 25f, fogEnd = 140f,
            exposure = 0.6f, saturation = 8f, temperature = -2f,
        };

        static readonly Config[] All = { Outpost, Forest };

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Transform root;

        // Preview size for a scene built here (AirsoftWarehouseBuilder.RenderMapPreviews asks), else -1.
        public static float PreviewHalfFor(string scene)
        {
            foreach (var c in All) if (c.scene == scene) return c.previewHalf;
            return -1f;
        }

        [MenuItem("Tools/Airsoft/Build Outpost Map")]
        public static void BuildOutpost() => BuildInteractive(Outpost);

        [MenuItem("Tools/Airsoft/Build Forest Map")]
        public static void BuildForest() => BuildInteractive(Forest);

        public static void BuildAll()
        {
            foreach (var c in All) Build(c);
        }

        static void BuildInteractive(Config cfg)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(cfg);
            if (!Application.isBatchMode) EditorApplication.delayCall += AirsoftWarehouseBuilder.RenderMapPreviews;
        }

        // ───────────────────────── build ─────────────────────────
        static void Build(Config cfg)
        {
            string fbx = "Assets/Models/Maps/" + cfg.map + ".fbx";
            string scenePath = "Assets/Scenes/" + cfg.scene + ".unity";
            if (!ConfigureImporter(fbx)) { Debug.LogError("[Airsoft] missing " + fbx + " (run tools/Blender/build_*.py)"); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            mats.Clear();
            root = new GameObject("Environment").transform;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = cfg.map + "Model";
            model.transform.SetParent(root, false);

            if (!CheckAxes(model.transform)) return;
            var points = CollectPoints(model.transform);
            SetUpMeshes(model.transform, cfg);

            Lighting(cfg);
            Sky();
            Boundary(cfg);
            PostProcessing(cfg);
            Physics.SyncTransforms();

            var spawns = new List<(Vector3 pos, int team)>();
            var enemies = new List<Vector3>();
            foreach (var kv in points)
            {
                string n = kv.Key;
                Vector3 g = Ground(kv.Value);
                if (n.StartsWith("GP_Spawn_R_")) spawns.Add((g + Vector3.up, 1));
                else if (n.StartsWith("GP_Spawn_B_")) spawns.Add((g + Vector3.up, 2));
                else if (n.StartsWith("GP_Spawn_F_")) spawns.Add((g + Vector3.up, 0));
                else if (n.StartsWith("GP_Enemy_")) enemies.Add(g + Vector3.up);
            }
            var nav = AirsoftSceneBuilder.BuildGameplayObjects(spawns.ToArray(), enemies.ToArray());
            BuildLayout(cfg, points);

            EditorSceneManager.SaveScene(scene, scenePath);
            SaveNavMeshAsset(nav, cfg.scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ObjectiveMapSetup.UpdateManifest(scene);
            AddToBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Airsoft] {cfg.map} map built: {scenePath} ({spawns.Count} spawns, {enemies.Count} enemy spawns)");
        }

        static bool ConfigureImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return false;
            bool dirty = false;
            void Set<T>(T cur, T want, System.Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(cur, want)) return;
                apply(want);
                dirty = true;
            }
            Set(mi.globalScale, 1f, v => mi.globalScale = v);
            Set(mi.useFileScale, true, v => mi.useFileScale = v);
            Set(mi.bakeAxisConversion, true, v => mi.bakeAxisConversion = v);
            Set(mi.importCameras, false, v => mi.importCameras = v);
            Set(mi.importLights, false, v => mi.importLights = v);
            Set(mi.importAnimation, false, v => mi.importAnimation = v);
            Set(mi.animationType, ModelImporterAnimationType.None, v => mi.animationType = v);
            Set(mi.importBlendShapes, false, v => mi.importBlendShapes = v);
            Set(mi.meshCompression, ModelImporterMeshCompression.Off, v => mi.meshCompression = v);
            Set(mi.isReadable, true, v => mi.isReadable = v);
            Set(mi.importNormals, ModelImporterNormals.Import, v => mi.importNormals = v);
            Set(mi.materialImportMode, ModelImporterMaterialImportMode.ImportViaMaterialDescription, v => mi.materialImportMode = v);
            if (dirty) mi.SaveAndReimport();
            return true;
        }

        static bool CheckAxes(Transform model)
        {
            Transform Find(string n)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
                return null;
            }
            var o = Find("Probe_Origin");
            var e = Find("Probe_East");
            var n = Find("Probe_North");
            var u = Find("Probe_Up");
            if (o == null || e == null || n == null || u == null) { Debug.LogError("[Airsoft] FBX has no Probe_* empties"); return false; }
            Vector3 de = e.position - o.position, dn = n.position - o.position, du = u.position - o.position;
            bool ok = Vector3.Distance(o.position, Vector3.zero) < 0.05f && Vector3.Distance(de, new Vector3(10, 0, 0)) < 0.05f
                   && Vector3.Distance(dn, new Vector3(0, 0, 10)) < 0.05f && Vector3.Distance(du, new Vector3(0, 10, 0)) < 0.05f;
            if (!ok)
            {
                Debug.LogError($"[Airsoft] FBX axes are off: origin {o.position}, east {de}, north {dn}, up {du}");
                return false;
            }
            foreach (var t in new[] { o, e, n, u }) Object.DestroyImmediate(t.gameObject);
            return true;
        }

        static Dictionary<string, Vector3> CollectPoints(Transform model)
        {
            var d = new SortedDictionary<string, Vector3>(System.StringComparer.Ordinal);
            var dead = new List<GameObject>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("GP_")) continue;
                d[t.name] = t.position;
                dead.Add(t.gameObject);
            }
            foreach (var g in dead) Object.DestroyImmediate(g);
            return new Dictionary<string, Vector3>(d);
        }

        static void SetUpMeshes(Transform model, Config cfg)
        {
            foreach (var mr in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var go = mr.gameObject;
                string n = go.name;
                var src = mr.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = MapMat(cfg.map, src[i]);
                mr.sharedMaterials = dst;
                mr.shadowCastingMode = ShadowCastingMode.On;
                go.isStatic = true;
                if (n.StartsWith("MMH_")) go.AddComponent<MinimapHidden>();
                if (n.StartsWith("VIS_") || n.StartsWith("MMH_")) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        // FBX material → project URP material "<Map>_<name>" with the same colour.
        static Material MapMat(string map, Material src)
        {
            string n = src != null ? src.name : "Default";
            int dot = n.IndexOf('.');      // Blender duplicates: "Name.001"
            if (dot > 0) n = n.Substring(0, dot);
            string key = map + "_" + n;
            if (mats.TryGetValue(key, out var m)) return m;
            Color c = Color.gray;
            float smooth = 0.12f, metal = 0f;
            if (src != null)
            {
                if (src.HasProperty("_BaseColor")) c = src.GetColor("_BaseColor");
                else if (src.HasProperty("_Color")) c = src.GetColor("_Color");
                if (src.HasProperty("_Metallic")) metal = Mathf.Clamp01(src.GetFloat("_Metallic"));
            }
            // The FBX importer turns Blender's (linear) base colours into sRGB values; the map scripts pick
            // their colours as plain sRGB numbers, so take them back.
            c = c.linear;
            c.a = 1f;
            string folder = "Assets/Materials/" + map;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials", map);
            string path = folder + "/" + key + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", metal > 0.3f ? 0.35f : smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            mats[key] = m;
            return m;
        }

        // The ground (first collider hit) under a point; the point itself if nothing is there.
        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }

        // ───────────────────────── scene dressing ─────────────────────────
        static void Lighting(Config cfg)
        {
            var lg = new GameObject("Sun");
            lg.transform.SetParent(root, false);
            var sun = lg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = cfg.sun;
            sun.intensity = cfg.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            lg.transform.rotation = Quaternion.Euler(cfg.sunPitch, cfg.sunYaw, 0f);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = cfg.ambSky;
            RenderSettings.ambientEquatorColor = cfg.ambEquator;
            RenderSettings.ambientGroundColor = cfg.ambGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = cfg.fogColor;
            RenderSettings.fogStartDistance = cfg.fogStart;
            RenderSettings.fogEndDistance = cfg.fogEnd;
        }

        static void Sky()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Env/AirsoftSky.mat");
            if (sky == null)
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh != null) sky = new Material(sh);
            }
            if (sky != null) RenderSettings.skybox = sky;
        }

        static void Boundary(Config cfg)
        {
            var b = new GameObject("Boundary").transform;
            b.SetParent(root, false);
            float x = cfg.halfX, z = cfg.halfZ;
            foreach (var (p, sz) in new[] {
                (new Vector3(0, 5, z), new Vector3(2 * x + 1, 12, 0.4f)), (new Vector3(0, 5, -z), new Vector3(2 * x + 1, 12, 0.4f)),
                (new Vector3(x, 5, 0), new Vector3(0.4f, 12, 2 * z + 1)), (new Vector3(-x, 5, 0), new Vector3(0.4f, 12, 2 * z + 1)) })
            {
                var w = new GameObject("InvisibleWall");
                w.transform.SetParent(b, false);
                w.transform.position = p;
                w.AddComponent<BoxCollider>().size = sz;
                w.isStatic = true;
            }
        }

        static void PostProcessing(Config cfg)
        {
            string path = "Assets/Settings/Airsoft" + cfg.map + "Profile.asset";
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.6f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(cfg.exposure);
            ca.contrast.Override(12f);
            ca.saturation.Override(cfg.saturation);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.25f);
            vig.smoothness.Override(0.45f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(cfg.temperature);
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

        // ───────────────────────── mode points ─────────────────────────
        static void BuildLayout(Config cfg, Dictionary<string, Vector3> points)
        {
            var go = new GameObject("MapLayout");
            var layout = go.AddComponent<MapLayout>();
            layout.displayName = cfg.map;
            Transform P(string gp, string name)
            {
                if (!points.TryGetValue(gp, out var p)) { Debug.LogWarning("[Airsoft] " + cfg.map + " has no " + gp); return null; }
                var t = new GameObject(name).transform;
                t.SetParent(go.transform, false);
                t.position = Ground(p);
                return t;
            }
            layout.redBase = P("GP_RedBase", "RedBase");
            layout.blueBase = P("GP_BlueBase", "BlueBase");
            layout.controlPoints = new[] { P("GP_CP_A", "ControlPoint_A"), P("GP_CP_B", "ControlPoint_B"), P("GP_CP_C", "ControlPoint_C") };
            layout.bombSites = new[] { P("GP_Bomb_A", "BombSite_A"), P("GP_Bomb_B", "BombSite_B") };
            layout.vipStart = P("GP_VipStart", "VIPStart");
            layout.vipExtract = P("GP_VipExtract", "VIPExtract");
        }

        // ───────────────────────── scene bookkeeping ─────────────────────────
        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in list) if (s.path == path) { s.enabled = true; EditorBuildSettings.scenes = list.ToArray(); return; }
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void SaveNavMeshAsset(Unity.AI.Navigation.NavMeshSurface nav, string scene)
        {
            if (nav == null || nav.navMeshData == null) return;
            string folder = "Assets/Scenes/" + scene;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scenes", scene);
            string path = folder + "/NavMesh-NavMeshSurface.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(nav.navMeshData, path);
            EditorUtility.SetDirty(nav);
        }
    }
}
#endif
