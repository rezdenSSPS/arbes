using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // The match maps the lobby can vote on. Index 0 is the default map. Each entry is a scene in the
    // build settings; its top-down preview (rendered by the editor builders) lives in
    // Resources/MapPreviews/<scene>.png. Mode points of the loaded map come from MapLayout.Current.
    public static class MapCatalog
    {
        public struct MapInfo
        {
            public string scene;   // scene name in the build settings
            public string name;    // shown on the lobby map cards
            public float wind;     // how much of the lobby wind setting reaches this map (0 = indoor, no wind)
        }

        public static readonly MapInfo[] Maps =
        {
            new MapInfo { scene = "AirsoftArena", name = "Town", wind = 1f },            // 80 x 80 m: streets, houses, park, container yard
            new MapInfo { scene = "AirsoftWarehouse", name = "Warehouse", wind = 0f },   // 60 x 60 m CQB: hall, office block, loading yards
            new MapInfo { scene = "AirsoftOutpost", name = "Outpost", wind = 0.8f },     // 68 x 47 m walled compound: two spawn rooms, open MID, six corridors
            new MapInfo { scene = "AirsoftForest", name = "Forest", wind = 0.6f },       // 80 x 80 m woodland: camps, dry creek, two cabins (trees shelter)
        };

        public static int Count => Maps.Length;

        public static bool Valid(int i) => i >= 0 && i < Maps.Length;

        public static string SceneOf(int i) => Maps[Valid(i) ? i : 0].scene;

        public static string NameOf(int i) => Maps[Valid(i) ? i : 0].name;

        // Wind factor of a map scene (0 for unknown scenes).
        public static float WindFor(string scene)
        {
            int i = IndexOfScene(scene);
            return i >= 0 ? Maps[i].wind : 0f;
        }

        public static int IndexOfScene(string scene)
        {
            for (int i = 0; i < Maps.Length; i++) if (Maps[i].scene == scene) return i;
            return -1;
        }

        // True for any match map scene (spawning, match clock, HUD checks).
        public static bool IsMatchScene(string scene) => IndexOfScene(scene) >= 0;

        // Name of the map being played: the scene's MapLayout, else the catalog entry of the active scene.
        public static string CurrentDisplayName
        {
            get
            {
                var layout = MapLayout.Current;
                if (layout != null && !string.IsNullOrEmpty(layout.displayName)) return layout.displayName;
                int i = IndexOfScene(SceneManager.GetActiveScene().name);
                return NameOf(i >= 0 ? i : 0);
            }
        }

        static Texture2D[] previews;

        // Top-down preview for the map card, or null if none was rendered (the card shows an icon).
        public static Texture2D Preview(int i)
        {
            if (!Valid(i)) return null;
            if (previews == null || previews.Length != Maps.Length) previews = new Texture2D[Maps.Length];
            if (previews[i] == null) previews[i] = Resources.Load<Texture2D>("MapPreviews/" + Maps[i].scene);
            return previews[i];
        }
    }
}
