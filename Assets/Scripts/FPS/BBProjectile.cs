using UnityEngine;
using System.Collections.Generic;

namespace AirsoftFPS
{
    // One flying BB: steps BBBallistics at a fixed rate, raycasts every segment and draws a small
    // bright ball with a short trail. The shooter's copy reports what it hit through `onHit`;
    // remote copies are visual only. Pooled, because automatics fire 10-15 of these a second.
    public class BBProjectile : MonoBehaviour
    {
        const float BB_SIZE = 0.03f;          // drawn a bit larger than a real 6 mm BB so it reads on screen
        const float CONVERGE_DIST = 4f;       // the drawn BB leaves the muzzle and joins the true (eye) path by here
        const int MAX_STEPS_PER_FRAME = 8;    // after a hitch, catch up a little at a time

        public delegate void HitHandler(RaycastHit hit);

        WeaponData weapon;
        BBBallistics.State state;
        Vector3 prevPos;
        float accum;
        Vector3 muzzleOffset;
        LayerMask mask;
        Transform ignoreRoot;
        HitHandler onHit;
        bool flying;
        float fadeOut;     // > 0: BB is spent, trail is shrinking away

        TrailRenderer trail;
        MeshRenderer ball;

        static readonly Stack<BBProjectile> s_pool = new Stack<BBProjectile>();
        static Material s_ballMat, s_trailMat;
        static Mesh s_sphere;

        public static void Fire(WeaponData w, Vector3 origin, Vector3 dir, Vector3 muzzle, LayerMask mask,
                                Transform ignoreRoot, HitHandler onHit)
        {
            BBProjectile bb = null;
            while (s_pool.Count > 0 && bb == null) bb = s_pool.Pop();   // pooled ones die with their scene
            if (bb == null) bb = Create();

            bb.weapon = w;
            bb.state = BBBallistics.Launch(w, origin, dir);
            bb.prevPos = origin;
            bb.accum = 0f;
            bb.muzzleOffset = muzzle - origin;
            bb.mask = mask;
            bb.ignoreRoot = ignoreRoot;
            bb.onHit = onHit;
            bb.flying = true;
            bb.fadeOut = 0f;

            bb.transform.position = muzzle;
            bb.ball.enabled = true;
            bb.trail.Clear();
            bb.gameObject.SetActive(true);
            bb.trail.emitting = true;

            // First segment right away, so a point-blank hit lands this frame.
            bb.Advance(BBBallistics.STEP);
        }

        static BBProjectile Create()
        {
            var go = new GameObject("BB");
            var bb = go.AddComponent<BBProjectile>();

            if (s_sphere == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s_sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            go.AddComponent<MeshFilter>().sharedMesh = s_sphere;
            bb.ball = go.AddComponent<MeshRenderer>();
            bb.ball.sharedMaterial = BallMat();
            bb.ball.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bb.ball.receiveShadows = false;
            go.transform.localScale = Vector3.one * BB_SIZE;

            bb.trail = go.AddComponent<TrailRenderer>();
            bb.trail.sharedMaterial = TrailMat();
            bb.trail.time = 0.05f;
            bb.trail.minVertexDistance = 0.05f;
            bb.trail.widthMultiplier = BB_SIZE;
            bb.trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.96f, 0.75f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.4f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            bb.trail.colorGradient = g;
            bb.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bb.trail.receiveShadows = false;
            bb.trail.numCapVertices = 2;
            return bb;
        }

        void Update()
        {
            if (fadeOut > 0f)
            {
                fadeOut -= Time.deltaTime;
                if (fadeOut <= 0f) Recycle();
                return;
            }
            if (!flying) return;
            Advance(Time.deltaTime);
        }

        void Advance(float dt)
        {
            accum += dt;
            int steps = 0;
            while (flying && accum >= BBBallistics.STEP && steps < MAX_STEPS_PER_FRAME)
            {
                accum -= BBBallistics.STEP;
                steps++;

                prevPos = state.pos;
                if (!BBBallistics.Step(weapon, ref state)) { Stop(prevPos); return; }

                if (BBBallistics.CastSegment(prevPos, state.pos, mask, ignoreRoot, out RaycastHit hit))
                {
                    state.pos = hit.point;
                    Stop(hit.point);
                    AirsoftAudio.Impact(hit);   // every peer flies its own copy of each BB: one impact sound each
                    var cb = onHit;
                    onHit = null;
                    cb?.Invoke(hit);
                    return;
                }
            }
            if (steps == MAX_STEPS_PER_FRAME) accum = Mathf.Min(accum, BBBallistics.STEP);

            // Draw between the last two simulated points, sliding from the muzzle onto the real path.
            float k = Mathf.Clamp01(accum / BBBallistics.STEP);
            transform.position = Visual(Vector3.Lerp(prevPos, state.pos, k), state.traveled);
        }

        Vector3 Visual(Vector3 simPos, float traveled)
        {
            return simPos + muzzleOffset * Mathf.Clamp01(1f - traveled / CONVERGE_DIST);
        }

        void Stop(Vector3 at)
        {
            flying = false;
            transform.position = Visual(at, state.traveled);
            ball.enabled = false;
            trail.emitting = false;
            fadeOut = trail.time + 0.02f;   // let the trail catch up, then recycle
        }

        void Recycle()
        {
            onHit = null;
            weapon = null;
            ignoreRoot = null;
            gameObject.SetActive(false);
            s_pool.Push(this);
        }

        static Material BallMat()
        {
            if (s_ballMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                var c = new Color(1f, 0.97f, 0.82f);
                s_ballMat = new Material(sh);
                s_ballMat.color = c;
                if (s_ballMat.HasProperty("_BaseColor")) s_ballMat.SetColor("_BaseColor", c);
            }
            return s_ballMat;
        }

        static Material TrailMat()
        {
            if (s_trailMat == null)
                s_trailMat = new Material(Shader.Find("Sprites/Default"));
            return s_trailMat;
        }
    }
}
