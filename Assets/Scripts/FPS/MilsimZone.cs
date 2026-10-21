using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // Milsim: where a HIT player has to walk back to before they can respawn.
    //
    // Fallback until team spawn sides exist (task 5 adds PlayerSpawner.GetTeamZone(team), Red north /
    // Blue south): the arena's "SpawnPoint" objects are split by Z, the northern half belongs to Red
    // and the southern half to Blue, and the zone is the team's spawn point nearest to the player.
    // To swap, make TryGet return PlayerSpawner.GetTeamZone(team) and drop the rest.
    public static class MilsimZone
    {
        public const float RADIUS = 4f;   // metres around the spawn point that count as "in the zone"

        static Transform[] cache;
        static int cacheFrame = -1;
        static readonly List<Transform> s_team = new List<Transform>();

        // Zone of `team` (1 = Red, 2 = Blue) closest to `from`. False if the arena has no spawn points.
        public static bool TryGet(int team, Vector3 from, out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = RADIUS;
            var all = Points();
            if (all.Length == 0) return false;

            s_team.Clear();
            if (all.Length == 1 || team <= 0) s_team.AddRange(all);
            else
            {
                // median Z splits the map: Red takes the north (+Z) half, Blue the south half
                var zs = new float[all.Length];
                for (int i = 0; i < all.Length; i++) zs[i] = all[i].position.z;
                System.Array.Sort(zs);
                float mid = zs.Length % 2 == 1 ? zs[zs.Length / 2] : (zs[zs.Length / 2 - 1] + zs[zs.Length / 2]) * 0.5f;
                foreach (var t in all)
                    if (team == 1 ? t.position.z >= mid : t.position.z < mid) s_team.Add(t);
                if (s_team.Count == 0) s_team.AddRange(all);
            }

            float best = float.MaxValue;
            foreach (var t in s_team)
            {
                Vector3 d = t.position - from; d.y = 0;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; center = t.position; }
            }
            return true;
        }

        // Horizontal distance from `pos` to the edge of the zone (0 = inside), or -1 if there is no zone.
        public static float DistanceOutside(int team, Vector3 pos, out Vector3 center)
        {
            if (!TryGet(team, pos, out center, out float r)) return -1f;
            Vector3 d = center - pos; d.y = 0;
            return Mathf.Max(0f, d.magnitude - r);
        }

        static Transform[] Points()
        {
            // re-scan every 60 frames (scene changes, late-built arenas)
            if (cache == null || Time.frameCount - cacheFrame > 60 || HasNull(cache))
            {
                cacheFrame = Time.frameCount;
                GameObject[] gos;
                try { gos = GameObject.FindGameObjectsWithTag("SpawnPoint"); }
                catch (UnityException) { gos = new GameObject[0]; }
                cache = new Transform[gos.Length];
                for (int i = 0; i < gos.Length; i++) cache[i] = gos[i].transform;
            }
            return cache;
        }

        static bool HasNull(Transform[] a)
        {
            foreach (var t in a) if (t == null) return true;
            return false;
        }
    }
}
