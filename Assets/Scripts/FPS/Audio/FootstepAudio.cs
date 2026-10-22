using UnityEngine;

namespace AirsoftFPS
{
    // Footsteps, jumps and landings for anything with a Health (players, PvP bots, co-op AI), added at
    // runtime by Health.OnNetworkSpawn. Runs on every peer from the (network-synced) position alone, so
    // nobody has to send a packet per step: horizontal speed picks walk / sprint / crouch, the
    // distance walked times the stride decides when a foot lands, a short ray down finds the surface.
    public class FootstepAudio : MonoBehaviour
    {
        const float GROUND_RAY = 0.6f;       // from 0.3 m above the feet: grounded within ~0.3 m
        const float TELEPORT = 3f;           // a jump this far in one frame is a respawn, not a step

        Health health;
        FPSController fps;
        Vector3 lastPos;
        float speed;              // smoothed horizontal speed (m/s)
        float distance;           // walked since the last step
        bool grounded = true;
        float airTime, minVy;
        Surfaces.Kind ground = Surfaces.Kind.Concrete;
        bool started;

        static readonly RaycastHit[] s_hits = new RaycastHit[8];

        void Start()
        {
            health = GetComponent<Health>();
            fps = GetComponent<FPSController>();
            lastPos = transform.position;
            started = true;
        }

        bool IsLocal => health != null && AirsoftAudio.IsLocalPlayer(health);

        void Update()
        {
            if (!started) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 p = transform.position;
            Vector3 d = p - lastPos;
            lastPos = p;
            if (d.sqrMagnitude > TELEPORT * TELEPORT) { distance = 0f; speed = 0f; return; }

            bool out_ = health != null && health.IsSpawned && health.isDead.Value;
            if (out_) { distance = 0f; speed = 0f; grounded = true; airTime = 0f; return; }

            float h = new Vector2(d.x, d.z).magnitude;
            speed = Mathf.Lerp(speed, h / dt, 1f - Mathf.Exp(-dt * 10f));
            float vy = d.y / dt;

            bool g = Ground(p, out var kind);
            if (!g)
            {
                if (grounded && vy > 2f) Jump();
                grounded = false;
                airTime += dt;
                minVy = Mathf.Min(minVy, vy);
                return;
            }
            ground = kind;
            if (!grounded)
            {
                if (airTime > 0.2f && minVy < -3f) Land(-minVy);
                grounded = true;
                airTime = 0f;
                minVy = 0f;
                distance = 0f;
            }

            if (speed < 0.7f) { distance = Mathf.Min(distance, 0.5f); return; }
            distance += h;
            float stride = Mathf.Clamp(speed * 0.34f, 1.0f, 2.9f);
            if (distance >= stride)
            {
                distance -= stride;
                Step();
            }
        }

        bool Crouching => fps != null && IsLocal ? fps.IsCrouching : speed < 3.2f;
        bool Sprinting => fps != null && IsLocal ? fps.IsSprinting : speed > 6.6f;

        void Step()
        {
            bool crouch = Crouching, sprint = Sprinting && !crouch;
            float vol = crouch ? 0.22f : sprint ? 0.8f : 0.5f;
            float range = crouch ? 10f : sprint ? 34f : 24f;
            if (IsLocal) vol *= 0.6f;   // your own steps sit under everything else
            AirsoftAudio.PlayAt(AirsoftAudio.StepCue(ground), transform.position + Vector3.up * 0.05f, vol, 1.5f, range, 0.06f);
        }

        void Jump()
        {
            AirsoftAudio.PlayAt("jump", transform.position + Vector3.up, IsLocal ? 0.35f : 0.5f, 1.5f, 20f, 0.05f);
        }

        void Land(float fallSpeed)
        {
            float k = Mathf.InverseLerp(3f, 12f, fallSpeed);
            float vol = Mathf.Lerp(0.45f, 1f, k) * (IsLocal ? 0.7f : 1f);
            Vector3 at = transform.position + Vector3.up * 0.05f;
            AirsoftAudio.PlayAt("land", at, vol, 1.5f, 30f, 0.05f);
            AirsoftAudio.PlayAt(AirsoftAudio.StepCue(ground), at, vol * 0.7f, 1.5f, 30f, 0.06f);
        }

        // A short ray down from just above the feet, ignoring our own colliders and triggers.
        bool Ground(Vector3 feet, out Surfaces.Kind kind)
        {
            kind = ground;
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.3f, Vector3.down, s_hits, GROUND_RAY, ~0,
                                            QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider hit = null;
            for (int i = 0; i < n; i++)
            {
                var c = s_hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (s_hits[i].distance < best) { best = s_hits[i].distance; hit = c; }
            }
            if (hit == null) return false;
            kind = Surfaces.Of(hit);
            if (kind == Surfaces.Kind.Body) kind = Surfaces.Kind.Concrete;   // standing on someone
            return true;
        }
    }
}
