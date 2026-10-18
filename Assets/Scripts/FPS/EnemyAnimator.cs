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
        }

        void OnDeadChanged(bool prev, bool curr) => dead = curr;

        void Update()
        {
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
