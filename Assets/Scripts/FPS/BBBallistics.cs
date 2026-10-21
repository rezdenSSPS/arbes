using UnityEngine;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Shared airsoft BB flight model. The shooter, every remote client (visual copy) and the
    // server (hit validation) all run this same fixed-step code from the same origin, direction
    // and seed, so they agree on where each BB went.
    //
    // Forces per step: gravity, hop-up lift (backspin, perpendicular to the velocity, fading out
    // over hopTime and with speed) and quadratic air drag. Semi-implicit Euler at 60 Hz.
    public static class BBBallistics
    {
        public const float STEP = 1f / 60f;          // seconds per simulated segment
        public const float MAX_FLIGHT_TIME = 3f;     // a BB that is still flying after this is gone

        public struct State
        {
            public Vector3 pos;
            public Vector3 vel;
            public float time;       // seconds since the shot
            public float traveled;   // path length so far (metres)
        }

        public static State Launch(WeaponData w, Vector3 origin, Vector3 dir)
        {
            return new State { pos = origin, vel = dir.normalized * w.muzzleVelocity, time = 0f, traveled = 0f };
        }

        // Advances one STEP. Returns false once the BB has used up its range or flight time.
        public static bool Step(WeaponData w, ref State s)
        {
            if (s.time >= MAX_FLIGHT_TIME || s.traveled >= w.range) return false;

            Vector3 v = s.vel;
            float speed = v.magnitude;
            Vector3 acc = Vector3.down * w.bbGravity - v * (w.drag * speed);

            float fade = w.hopTime > 0f ? 1f - s.time / w.hopTime : 0f;
            if (fade > 0f && speed > 0.01f)
            {
                Vector3 vh = v / speed;
                Vector3 lift = Vector3.up - vh * Vector3.Dot(Vector3.up, vh);
                float lm = lift.magnitude;
                if (lm > 1e-4f)
                    acc += lift / lm * (w.bbGravity * w.hopUp * fade * Mathf.Min(1f, speed / w.muzzleVelocity));
            }

            Vector3 prev = s.pos;
            s.vel = v + acc * STEP;
            s.pos = prev + s.vel * STEP;
            s.traveled += Vector3.Distance(prev, s.pos);
            s.time += STEP;
            return true;
        }

        // ───── Deterministic pellet spread ─────
        // Each BB of a shot gets its direction from (seed, pellet index), so a remote copy and
        // the server reproduce the shooter's spread exactly.
        public static Vector3 PelletDirection(WeaponData w, Vector3 baseDir, uint seed, int pellet, bool aimed)
        {
            float spread = aimed ? w.spread * 0.3f : w.spread;
            uint h = Hash(seed ^ (uint)(pellet * 0x9E3779B9u));
            float sx = (Unit(ref h) * 2f - 1f) * spread;
            float sy = (Unit(ref h) * 2f - 1f) * spread;
            Vector3 f = baseDir.sqrMagnitude > 1e-6f ? baseDir.normalized : Vector3.forward;
            return Quaternion.LookRotation(f) * (Quaternion.Euler(sx, sy, 0f) * Vector3.forward);
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x == 0 ? 1u : x;
        }

        static float Unit(ref uint x)   // xorshift32 -> [0,1)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return (x >> 8) * (1f / 16777216f);
        }

        // ───── Collision along a segment ─────
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        // Nearest collider between a and b, skipping anything under `ignoreRoot` (the shooter).
        public static bool CastSegment(Vector3 a, Vector3 b, LayerMask mask, Transform ignoreRoot, out RaycastHit hit)
        {
            hit = default;
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 1e-5f) return false;
            int n = Physics.RaycastNonAlloc(a, d / dist, s_hits, dist, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            return found;
        }

        // ───── Server-side check: does this BB's path reach `point`? ─────
        public enum PathResult { Hit, Blocked, OffPath }

        // Re-simulates one pellet against static world geometry only (networked objects move, and
        // the shooter's own BB already stopped at the target). Finds the closest approach of the
        // path to `point` before the first wall. `timeAt` = flight time at that closest approach,
        // `miss` = its distance from `point`.
        public static PathResult TracePath(WeaponData w, Vector3 origin, Vector3 dir, Vector3 point,
                                           LayerMask mask, float pathTolerance, float endMargin,
                                           out float timeAt, out float miss)
        {
            var s = Launch(w, origin, dir);
            float bestSq = float.MaxValue;
            timeAt = 0f;
            bool blocked = false;

            while (!blocked)
            {
                Vector3 a = s.pos;
                float t0 = s.time, len0 = s.traveled;
                if (!Step(w, ref s)) break;
                Vector3 b = s.pos;

                // Static geometry on this segment cuts the path short (with a small margin so a hit on
                // someone pressed against a wall, or a muzzle poking into a wall, still counts).
                Vector3 d = b - a;
                float segLen = d.magnitude;
                if (segLen > 1e-5f && StaticHit(a, d / segLen, segLen, mask, len0, endMargin, out float hd))
                {
                    b = a + d / segLen * Mathf.Min(segLen + endMargin, hd + endMargin);
                    blocked = true;
                }

                Vector3 ab = b - a;
                float u = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
                float dSq = (a + ab * u - point).sqrMagnitude;
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    timeAt = t0 + STEP * u;
                }
                // well past the point and getting further away: stop early
                else if (bestSq <= pathTolerance * pathTolerance && dSq > (pathTolerance + 5f) * (pathTolerance + 5f))
                    break;
            }

            miss = Mathf.Sqrt(bestSq);
            if (bestSq <= pathTolerance * pathTolerance) return PathResult.Hit;
            return blocked ? PathResult.Blocked : PathResult.OffPath;
        }

        // First non-networked collider along a ray (ignores hits within endMargin of the muzzle).
        static bool StaticHit(Vector3 a, Vector3 dir, float dist, LayerMask mask, float traveledBefore,
                              float endMargin, out float hitDist)
        {
            hitDist = float.MaxValue;
            int n = Physics.RaycastNonAlloc(a, dir, s_hits, dist, mask, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (traveledBefore + h.distance < endMargin) continue;
                if (h.collider.GetComponentInParent<NetworkObject>() != null) continue;
                if (h.distance < hitDist) { hitDist = h.distance; found = true; }
            }
            return found;
        }
    }
}
