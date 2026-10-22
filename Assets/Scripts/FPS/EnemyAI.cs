using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

namespace AirsoftFPS
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAI : NetworkBehaviour
    {
        public enum State { Idle, Patrol, Chase, Attack, Dead }

        [Header("AI Settings")]
        public float detectionRange = 25f;
        public float attackRange = 15f;
        public float fireRate = 3f;
        public int damage = 10;
        public float patrolRadius = 20f;
        public float patrolWaitTime = 2f;

        NavMeshAgent agent;
        Health health;
        State state = State.Idle;
        Transform target;
        float nextFireTime;
        float patrolWaitTimer;
        Vector3 patrolTarget;
        float searchTimer;

        // PvP bots (Health.IsBot): BotBrain drives this soldier instead of the co-op AI below.
        // The bot's current weapon slot (WeaponData order), for the gun in its hands on every client.
        public NetworkVariable<int> botWeapon = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        bool IsBot => health != null && health.IsBot;
        static WeaponData[] s_botWeapons;

        public override void OnNetworkSpawn()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();

            if (!IsServer)
            {
                if (agent) agent.enabled = false;
                return;
            }

            // Make sure the agent is actually on the navmesh, otherwise it freezes
            if (agent != null && agent.enabled && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }

            if (IsBot) return;   // BotBrain takes over

            if (health)
            {
                health.isDead.OnValueChanged += (prev, curr) =>
                {
                    if (curr)
                    {
                        state = State.Dead;
                        if (agent && agent.isOnNavMesh) agent.isStopped = true;
                    }
                };
            }

            state = State.Patrol;
            PickNewPatrolTarget();
        }

        void Update()
        {
            if (!IsServer) return;
            if (health == null) health = GetComponent<Health>();
            if (IsBot) return;
            if (state == State.Dead) return;

            FindNearestPlayer();

            switch (state)
            {
                case State.Patrol:
                    Patrol();
                    break;
                case State.Chase:
                    Chase();
                    break;
                case State.Attack:
                    Attack();
                    break;
            }
        }

        void FindNearestPlayer()
        {
            searchTimer -= Time.deltaTime;
            if (searchTimer > 0) return;
            searchTimer = 0.5f;

            float closest = detectionRange;
            Transform best = null;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var pHealth = client.PlayerObject.GetComponent<Health>();
                if (pHealth != null && pHealth.isDead.Value) continue;

                float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
                if (dist < closest)
                {
                    closest = dist;
                    best = client.PlayerObject.transform;
                }
            }

            target = best;

            if (target == null)
            {
                if (state != State.Patrol)
                {
                    state = State.Patrol;
                    PickNewPatrolTarget();
                }
            }
            else if (closest <= attackRange)
                state = State.Attack;
            else
                state = State.Chase;
        }

        void Patrol()
        {
            if (agent == null || !agent.isOnNavMesh) return;

            if (!agent.pathPending && agent.remainingDistance < 1f)
            {
                patrolWaitTimer -= Time.deltaTime;
                if (patrolWaitTimer <= 0)
                    PickNewPatrolTarget();
            }
        }

        void PickNewPatrolTarget()
        {
            if (agent == null || !agent.isOnNavMesh) return;

            Vector3 random = transform.position + Random.insideUnitSphere * patrolRadius;
            random.y = transform.position.y;

            if (NavMesh.SamplePosition(random, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
            {
                patrolTarget = hit.position;
                agent.isStopped = false;
                agent.SetDestination(patrolTarget);
            }
            patrolWaitTimer = patrolWaitTime;
        }

        void Chase()
        {
            if (agent == null || target == null || !agent.isOnNavMesh) return;
            agent.isStopped = false;
            agent.SetDestination(target.position);
        }

        void Attack()
        {
            if (target == null) return;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                Vector3 dir = (target.position - transform.position);
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);
            }

            if (Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + 1f / fireRate;
                ShootAtTarget();
            }
        }

        void ShootAtTarget()
        {
            if (target == null) return;

            // aim from the muzzle height the ray starts at, not from the feet (that line ran 1.2 m too high)
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 aimPoint = target.position + Vector3.up * 1f;
            Vector3 dir = (aimPoint - origin).normalized;
            dir += Random.insideUnitSphere * 0.08f;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, attackRange + 5f))
            {
                var targetHealth = hit.collider.GetComponentInParent<Health>();
                if (targetHealth != null && targetHealth.isPlayer && !targetHealth.isDead.Value)
                    targetHealth.TakeDamage(damage, 0, "", NetworkObject);
            }

            ShootEffectRpc();
        }

        [Rpc(SendTo.Everyone)]
        void ShootEffectRpc()
        {
            AirsoftAudio.Shot(null, transform.position + Vector3.up * 1.4f, false);   // co-op AI: generic AEG
            var flash = GetComponentInChildren<Light>(true);
            if (flash)
            {
                flash.enabled = true;
                Invoke(nameof(HideFlash), 0.06f);
            }
        }

        // A bot fired (BotBrain, server). The server already flies the real BBs; everyone else flies a
        // visual-only copy from the same origin, direction and seed. Muzzle flash everywhere.
        [Rpc(SendTo.Everyone)]
        public void BotShotRpc(int weaponIdx, Vector3 origin, Vector3 dir, uint seed)
        {
            s_botWeapons ??= WeaponData.GetAllWeapons();
            if (weaponIdx >= 0 && weaponIdx < s_botWeapons.Length) AirsoftAudio.Shot(s_botWeapons[weaponIdx], origin, false);
            if (!IsServer)
            {
                if (weaponIdx >= 0 && weaponIdx < s_botWeapons.Length)
                {
                    var w = s_botWeapons[weaponIdx];
                    var anim = GetComponent<EnemyAnimator>();
                    Vector3 muzzle = anim != null ? anim.MuzzlePosition : origin;
                    if ((muzzle - origin).sqrMagnitude > 2.25f) muzzle = origin;
                    int pellets = Mathf.Max(1, w.pelletsPerShot);
                    for (int p = 0; p < pellets; p++)
                        BBProjectile.Fire(w, origin, BBBallistics.PelletDirection(w, dir, seed, p, false), muzzle,
                                          ~0, transform, null);
                }
            }
            var flash = GetComponentInChildren<Light>(true);
            if (flash)
            {
                flash.enabled = true;
                CancelInvoke(nameof(HideFlash));
                Invoke(nameof(HideFlash), 0.05f);
            }
        }

        void HideFlash()
        {
            var flash = GetComponentInChildren<Light>(true);
            if (flash) flash.enabled = false;
        }
    }
}
