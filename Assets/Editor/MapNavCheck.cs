#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace AirsoftFPS.Editor
{
    // Checks every map in MapCatalog: each spawn point, co-op enemy spawn and mode point (bases, control
    // points, bomb sites, VIP start / extract) stands on the baked NavMesh, and the bots can walk from the
    // first spawn to every one of them (one connected map, nothing walled in).
    //
    // Menu: Tools/Airsoft/Check Map NavMesh. Batch: -executeMethod AirsoftFPS.Editor.MapNavCheck.RunBatch
    public static class MapNavCheck
    {
        static int fails, passes;
        static readonly StringBuilder log = new StringBuilder();

        static void Check(bool ok, string what)
        {
            if (ok) passes++; else fails++;
            if (!ok) log.AppendLine("  FAIL " + what);
        }

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        [MenuItem("Tools/Airsoft/Check Map NavMesh")]
        public static bool Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            fails = passes = 0;
            log.Clear();
            log.AppendLine("[Airsoft] Map NavMesh check");
            foreach (var m in MapCatalog.Maps)
            {
                string path = "Assets/Scenes/" + m.scene + ".unity";
                if (!File.Exists(path)) { Check(false, m.scene + ": scene missing"); continue; }
                try { CheckScene(m.scene, path); }
                catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION " + m.scene + " " + e); }
            }
            log.AppendLine($"  => {passes} passed, {fails} failed");
            if (fails == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
            return fails == 0;
        }

        static void CheckScene(string name, string path)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var pts = new List<(string, Vector3)>();
            foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude))
                pts.Add(("spawn " + sp.name, sp.transform.position));
            var enemies = GameObject.Find("EnemySpawnPoints");
            if (enemies != null) foreach (Transform t in enemies.transform) pts.Add(("enemy " + t.name, t.position));
            var layout = Object.FindAnyObjectByType<MapLayout>();
            Check(layout != null, name + ": MapLayout");
            if (layout != null)
            {
                void Add(Transform t, string n) { Check(t != null, $"{name}: {n} set"); if (t != null) pts.Add((n, t.position)); }
                Add(layout.redBase, "RedBase");
                Add(layout.blueBase, "BlueBase");
                for (int i = 0; i < layout.controlPoints.Length; i++) Add(layout.controlPoints[i], "ControlPoint " + i);
                for (int i = 0; i < layout.bombSites.Length; i++) Add(layout.bombSites[i], "BombSite " + i);
                Add(layout.vipStart, "VIPStart");
                Add(layout.vipExtract, "VIPExtract");
            }
            Check(pts.Count > 8, $"{name}: points found ({pts.Count})");

            var onMesh = new List<(string, Vector3)>();
            foreach (var (n, p) in pts)
            {
                bool hit = NavMesh.SamplePosition(p, out var h, 1.6f, NavMesh.AllAreas);
                Check(hit, $"{name}: {n} {p} on the NavMesh");
                if (hit) onMesh.Add((n, h.position));
            }
            if (onMesh.Count < 2) return;
            var from = onMesh[0];
            var path2 = new NavMeshPath();
            for (int i = 1; i < onMesh.Count; i++)
            {
                bool ok = NavMesh.CalculatePath(from.Item2, onMesh[i].Item2, NavMesh.AllAreas, path2) &&
                          path2.status == NavMeshPathStatus.PathComplete;
                Check(ok, $"{name}: path {from.Item1} -> {onMesh[i].Item1}");
            }
            log.AppendLine($"  {name}: {onMesh.Count}/{pts.Count} points on the NavMesh");
        }
    }
}
#endif
