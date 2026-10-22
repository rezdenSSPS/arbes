#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Objective-mode points of the arena ("Town") as a MapLayout, plus the map manifest the lobby reads
    // (Resources/MapManifest.asset, refreshed whenever a scene with a MapLayout is saved).
    //
    // Arena: 80 x 80 m, roads cross at the centre, Red spawns north, Blue south.
    //   Red base (CTF)  ~ ( 6, 0,  33)  north, beside the north road
    //   Blue base (CTF) ~ (-6, 0, -33)  south, beside the south road (point-mirrored)
    //   A container yard (NW)  ~ (-20, 0,  22)
    //   B crossroads, SW corner just off the road ~ (-7.5, 0, -7.5)
    //   C park (SE)            ~ ( 22, 0, -22)
    //   Bomb site A: NE houses, between the three houses ~ ( 23, 0,  15)
    //   Bomb site B: SW small buildings, in front of them ~ (-23, 0, -15)   (point-mirrored)
    //   VIP start: south road near the south wall ~ (0, 0, -35); extraction: north road ~ (0, 0, 35.5)
    // Each point is nudged to the nearest spot clear of props (FindClear), so a rebuilt map still works.
    //
    // Batch:  Unity -batchmode -quit -projectPath <p> -executeMethod AirsoftFPS.Editor.ObjectiveMapSetup.UpdateArenaLayout
    [InitializeOnLoad]
    public static class ObjectiveMapSetup
    {
        const string ArenaPath = "Assets/Scenes/AirsoftArena.unity";
        const string ManifestPath = "Assets/Resources/" + MapManifest.RESOURCE + ".asset";

        public static readonly Vector3 RedBase = new Vector3(6f, 0f, 33f);
        public static readonly Vector3 BlueBase = new Vector3(-6f, 0f, -33f);
        public static readonly Vector3[] ControlPoints =
        {
            new Vector3(-20f, 0f, 22f),    // A: container yard
            new Vector3(-7.5f, 0f, -7.5f), // B: crossroads
            new Vector3(22f, 0f, -22f),    // C: park
        };
        public static readonly Vector3[] BombSites =
        {
            new Vector3(23f, 0f, 15f),     // A: NE houses
            new Vector3(-23f, 0f, -15f),   // B: SW small buildings
        };
        public static readonly Vector3 VipStart = new Vector3(0f, 0f, -35f);
        public static readonly Vector3 VipExtract = new Vector3(0f, 0f, 35.5f);

        static ObjectiveMapSetup()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        static void OnSceneSaved(Scene scene)
        {
            if (FindLayout(scene) != null) UpdateManifest(scene);
        }

        // Adds (or refreshes) the "Town" MapLayout in the open arena scene. AirsoftSceneBuilder calls this
        // while building the arena, after the environment exists (so props can be avoided).
        public static MapLayout AddTownLayout()
        {
            Physics.SyncTransforms();
            var scene = SceneManager.GetActiveScene();
            var layout = FindLayout(scene);
            if (layout == null) layout = new GameObject("MapLayout").AddComponent<MapLayout>();
            var root = layout.transform;
            root.position = Vector3.zero;
            layout.displayName = "Town";

            layout.redBase = Point(root, "RedBase", FindClear(RedBase, 1.6f));
            layout.blueBase = Point(root, "BlueBase", FindClear(BlueBase, 1.6f));
            var cps = new List<Transform>();
            for (int i = 0; i < ControlPoints.Length; i++)
                cps.Add(Point(root, "ControlPoint_" + (char)('A' + i), FindClear(ControlPoints[i], 0.9f)));
            layout.controlPoints = cps.ToArray();
            var sites = new List<Transform>();
            for (int i = 0; i < BombSites.Length; i++)
                sites.Add(Point(root, "BombSite_" + (char)('A' + i), FindClear(BombSites[i], 1.2f)));
            layout.bombSites = sites.ToArray();
            layout.vipStart = Point(root, "VIPStart", FindClear(VipStart, 1.2f));
            layout.vipExtract = Point(root, "VIPExtract", FindClear(VipExtract, 1.2f));
            EditorUtility.SetDirty(layout);

            var sb = new System.Text.StringBuilder("[Airsoft] Map layout 'Town':");
            void Row(string n, Transform t) => sb.Append($"\n  {n,-14} {t.position.x,6:0.0} {t.position.y,5:0.00} {t.position.z,6:0.0}");
            Row("Red base", layout.redBase); Row("Blue base", layout.blueBase);
            for (int i = 0; i < cps.Count; i++) Row("Point " + (char)('A' + i), cps[i]);
            for (int i = 0; i < sites.Count; i++) Row("Bomb site " + (char)('A' + i), sites[i]);
            Row("VIP start", layout.vipStart); Row("VIP extract", layout.vipExtract);
            Debug.Log(sb.ToString());
            return layout;
        }

        [MenuItem("Tools/Airsoft/Update Arena Map Layout (objective points)")]
        public static void UpdateArenaLayout()
        {
            var scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            AddTownLayout();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            UpdateManifest(scene);   // also done by the sceneSaved hook; explicit for batch mode
        }

        static MapLayout FindLayout(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var go in scene.GetRootGameObjects())
            {
                var l = go.GetComponentInChildren<MapLayout>(true);
                if (l != null) return l;
            }
            return null;
        }

        static Transform Point(Transform root, string name, Vector3 pos)
        {
            var t = root.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(root, false); }
            t.position = pos;
            t.rotation = Quaternion.identity;
            return t;
        }

        // Nearest spot (spiral search up to 8 m) where a standing player of `radius` fits, on the ground.
        static Vector3 FindClear(Vector3 want, float radius)
        {
            for (float r = 0f; r <= 8f; r += 0.5f)
            {
                int steps = r == 0f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 0.5f);
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    var p = new Vector3(want.x + Mathf.Cos(a) * r, 0f, want.z + Mathf.Sin(a) * r);
                    float g = GroundY(p);
                    var bottom = new Vector3(p.x, g + 0.3f + radius, p.z);
                    var top = new Vector3(p.x, g + 2.0f, p.z);
                    if (!Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
                        return new Vector3(p.x, g, p.z);
                }
            }
            Debug.LogWarning($"[Airsoft] No clear spot near {want}; using it as is");
            return new Vector3(want.x, GroundY(want), want.z);
        }

        static float GroundY(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, 6f, p.z), Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)
                && hit.point.y < 0.6f)
                return hit.point.y;
            return 0f;
        }

        // Writes the scene's MapLayout summary into Resources/MapManifest.asset.
        public static void UpdateManifest(Scene scene)
        {
            var layout = FindLayout(scene);
            if (layout == null) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var m = AssetDatabase.LoadAssetAtPath<MapManifest>(ManifestPath);
            if (m == null)
            {
                m = ScriptableObject.CreateInstance<MapManifest>();
                AssetDatabase.CreateAsset(m, ManifestPath);
            }
            m.Set(MapCaps.From(layout, scene.name));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
