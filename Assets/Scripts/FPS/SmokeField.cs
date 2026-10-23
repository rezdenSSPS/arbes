using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // Active smoke clouds, tracked on the server so bots (BotBrain.LineOfSight) can't see through smoke.
    // Players get the smoke as a visual cloud (WeaponController spawns the VFX on every client); the cloud
    // volume here is only used for the server-side line-of-sight test against bots and remote shooters.
    public static class SmokeField
    {
        struct Cloud { public Vector3 pos; public float radius; public float until; }
        static readonly List<Cloud> clouds = new List<Cloud>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => clouds.Clear();

        // Server: register a cloud that blocks sight for `duration` seconds.
        public static void Add(Vector3 pos, float radius, float duration)
        {
            Prune();
            clouds.Add(new Cloud { pos = pos, radius = radius, until = Time.time + duration });
        }

        // True if the segment a..b passes through any active smoke cloud.
        public static bool Blocks(Vector3 a, Vector3 b)
        {
            if (clouds.Count == 0) return false;
            Prune();
            for (int i = 0; i < clouds.Count; i++)
                if (SegmentHitsSphere(a, b, clouds[i].pos, clouds[i].radius)) return true;
            return false;
        }

        static void Prune()
        {
            float now = Time.time;
            for (int i = clouds.Count - 1; i >= 0; i--)
                if (now >= clouds[i].until) clouds.RemoveAt(i);
        }

        // Shortest distance from the segment a..b to centre c, compared with r.
        static bool SegmentHitsSphere(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2) : 0f;
            Vector3 closest = a + ab * t;
            return (c - closest).sqrMagnitude <= r * r;
        }
    }
}
