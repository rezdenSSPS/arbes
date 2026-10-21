using UnityEngine;

namespace AirsoftFPS
{
    // Milsim: where a HIT player has to walk back to before they can respawn: the player's own
    // team's spawn point nearest to them (team spawn sides from PlayerSpawner: Red north, Blue south).
    public static class MilsimZone
    {
        public const float RADIUS = 4f;   // metres around the spawn point that count as "in the zone"

        // Zone of `team` (1 = Red, 2 = Blue) closest to `from`.
        public static bool TryGet(int team, Vector3 from, out Vector3 center, out float radius)
        {
            radius = RADIUS;
            center = PlayerSpawner.NearestTeamSpawn(team, from).position;
            return true;
        }

        // Horizontal distance from `pos` to the edge of the zone (0 = inside), or -1 if there is no zone.
        public static float DistanceOutside(int team, Vector3 pos, out Vector3 center)
        {
            if (!TryGet(team, pos, out center, out float r)) return -1f;
            Vector3 d = center - pos; d.y = 0;
            return Mathf.Max(0f, d.magnitude - r);
        }
    }
}
