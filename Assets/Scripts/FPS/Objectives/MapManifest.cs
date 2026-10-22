using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // What mode-specific points a map scene has (a summary of its MapLayout). The lobby is a different
    // scene than the match map, so it can't look at the map's MapLayout; it reads this summary instead
    // to grey out modes the map can't play. The editor writes it to Resources/MapManifest.asset whenever
    // a scene with a MapLayout is saved (MapManifestEditor); in the match scene the live MapLayout wins.
    [Serializable]
    public struct MapCaps
    {
        public string scene;
        public string displayName;
        public bool redBase, blueBase;
        public int controlPoints;
        public int bombSites;
        public bool vipStart, vipExtract;
        public bool known;   // false = no layout data for this scene

        public static MapCaps From(MapLayout l, string scene)
        {
            var c = new MapCaps { scene = scene, known = l != null };
            if (l == null) return c;
            c.displayName = l.displayName;
            c.redBase = l.redBase != null;
            c.blueBase = l.blueBase != null;
            c.controlPoints = CountSet(l.controlPoints);
            c.bombSites = CountSet(l.bombSites);
            c.vipStart = l.vipStart != null;
            c.vipExtract = l.vipExtract != null;
            return c;
        }

        static int CountSet(Transform[] ts)
        {
            int n = 0;
            if (ts != null) foreach (var t in ts) if (t != null) n++;
            return n;
        }
    }

    public class MapManifest : ScriptableObject
    {
        public const string RESOURCE = "MapManifest";
        public List<MapCaps> maps = new List<MapCaps>();

        static MapManifest cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = null;

        public static MapManifest Load()
        {
            if (cached == null) cached = Resources.Load<MapManifest>(RESOURCE);
            return cached;
        }

        // Caps of a map scene: the live MapLayout when that scene is loaded, else the baked summary.
        public static MapCaps For(string scene)
        {
            var live = MapLayout.Current;
            if (live != null && live.gameObject.scene.name == scene) return MapCaps.From(live, scene);
            var m = Load();
            if (m != null)
                foreach (var c in m.maps)
                    if (c.scene == scene) return c;
            return new MapCaps { scene = scene, known = false };
        }

        public void Set(MapCaps caps)
        {
            for (int i = 0; i < maps.Count; i++)
                if (maps[i].scene == caps.scene) { maps[i] = caps; return; }
            maps.Add(caps);
        }
    }
}
