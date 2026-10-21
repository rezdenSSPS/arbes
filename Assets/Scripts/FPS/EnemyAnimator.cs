using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Procedural animation for the humanoid enemy: a walk cycle driven by the
    // NavMeshAgent's speed, a small aim pose while attacking, and a death topple.
    public class EnemyAnimator : MonoBehaviour
    {
        public Transform model;     // visual root (toppled on death)
        public Transform hipL, hipR, shoulderL, shoulderR;
        public Health health;

        NavMeshAgent agent;
        float walkPhase;
        bool dead;
        Quaternion modelRest;
        AirsoftLook.Avatar avatar;   // low-poly soldier (replaces the primitive body when available)

        void Start()
        {
            agent = GetComponent<NavMeshAgent>();
            if (health == null) health = GetComponent<Health>();
            if (model) modelRest = model.localRotation;

            if (health != null)
            {
                health.isDead.OnValueChanged += OnDeadChanged;
                dead = health.isDead.Value;
            }

            BuildLowPoly();
        }

        void BuildLowPoly()
        {
            var no = GetComponent<NetworkObject>();
            int seed = no != null ? (int)no.NetworkObjectId : Random.Range(0, 9999);
            var rng = new System.Random(seed * 31 + 7);
            var look = new LookData
            {
                body = rng.Next(4),                 // male bodies
                hat = 3,                            // helmet
                hatColor = rng.Next(2) == 0 ? 9 : 6,
                shirt = rng.Next(3) switch { 0 => 6, 1 => 9, _ => 8 },
                pants = rng.Next(2) == 0 ? 6 : 10,
                skin = rng.Next(AirsoftLook.SkinTones.Length),
                hair = rng.Next(3)
            };
            try { avatar = AirsoftLook.BuildAvatar(transform, AirsoftLook.Pack(look)); }
            catch (System.Exception e) { Debug.LogWarning("[Airsoft] enemy avatar: " + e.Message); avatar = null; }
            if (avatar == null) return;

            if (model) foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            if (avatar.rightHand)
            {
                AirsoftGuns.Gun g = null;
                try { g = AirsoftGuns.Build("AK47", 0.68f, null); } catch { }
                if (g != null)
                {
                    g.root.transform.SetPositionAndRotation(avatar.rightHand.position, transform.rotation);
                    g.root.transform.SetParent(avatar.rightHand, true);
                }
            }
        }

        void OnDeadChanged(bool prev, bool curr) => dead = curr;

        void Update()
        {
            if (avatar != null)
            {
                float sp = (agent != null && agent.enabled && agent.isOnNavMesh) ? agent.velocity.magnitude : 0f;
                // enemies move at ~3.5 m/s: map that to the run clip
                AirsoftLook.Animate(avatar, sp > 2.5f ? 7f : sp, dead);
                return;
            }

            if (model == null) return;

            if (dead)
            {
                // Topple forward and sink slightly
                model.localRotation = Quaternion.Slerp(model.localRotation,
                    modelRest * Quaternion.Euler(88f, 0f, 0f), Time.deltaTime * 5f);
                return;
            }

            float speed = (agent != null && agent.enabled && agent.isOnNavMesh)
                ? agent.velocity.magnitude : 0f;

            float swing = 0f;
            if (speed > 0.15f)
            {
                walkPhase += Time.deltaTime * (6f + speed);
                swing = Mathf.Sin(walkPhase) * Mathf.Clamp(speed * 10f, 10f, 40f);
            }
            else
            {
                walkPhase = Mathf.Lerp(walkPhase, 0f, Time.deltaTime * 6f);
                swing = Mathf.Sin(walkPhase) * 2f;
            }

            if (hipL) hipL.localRotation = Quaternion.Euler(swing, 0, 0);
            if (hipR) hipR.localRotation = Quaternion.Euler(-swing, 0, 0);
            // Left arm swings with the walk; right arm stays forward holding the gun
            if (shoulderL) shoulderL.localRotation = Quaternion.Euler(-swing * 0.6f, 0, 0);
            if (shoulderR) shoulderR.localRotation = Quaternion.Euler(-75f, 0, 0);
        }

        void OnDestroy()
        {
            if (health != null) health.isDead.OnValueChanged -= OnDeadChanged;
        }
    }
}
