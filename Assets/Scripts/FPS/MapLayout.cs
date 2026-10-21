using UnityEngine;

namespace AirsoftFPS
{
    // Mode-specific points of a match map, placed in the map's scene. Game modes read them from
    // MapLayout.Current; a point a map doesn't have stays null and that mode should fall back or be
    // unavailable on that map. Team spawns live on SpawnPoint components, not here.
    public class MapLayout : MonoBehaviour
    {
        public static MapLayout Current { get; private set; }

        [Tooltip("Name shown in the lobby map vote and on the scoreboard")]
        public string displayName = "Town";

        [Header("Team bases (Capture the Flag flag stands)")]
        public Transform redBase;
        public Transform blueBase;

        [Header("Domination control points (A, B, C)")]
        public Transform[] controlPoints = new Transform[0];

        [Header("Bomb sites (A, B)")]
        public Transform[] bombSites = new Transform[0];

        [Header("VIP escort")]
        public Transform vipStart;
        public Transform vipExtract;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void OnEnable() => Current = this;
        void OnDisable() { if (Current == this) Current = null; }

        void OnDrawGizmos()
        {
            DrawPoint(redBase, new Color(0.94f, 0.35f, 0.31f), 1.2f);
            DrawPoint(blueBase, new Color(0.49f, 0.72f, 1f), 1.2f);
            foreach (var t in controlPoints) DrawPoint(t, new Color(1f, 0.48f, 0.1f), 3f);
            foreach (var t in bombSites) DrawPoint(t, new Color(1f, 0.85f, 0.2f), 2.5f);
            DrawPoint(vipStart, Color.white, 1f);
            DrawPoint(vipExtract, new Color(0.48f, 0.83f, 0.54f), 2f);
        }

        static void DrawPoint(Transform t, Color c, float r)
        {
            if (t == null) return;
            Gizmos.color = c;
            Gizmos.DrawWireSphere(t.position, r);
        }
    }
}
