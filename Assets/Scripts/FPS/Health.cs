using UnityEngine;
using Unity.Netcode;

namespace AirsoftFPS
{
    public class Health : NetworkBehaviour
    {
        [Header("Settings")]
        public int maxHealth = 100;
        public bool isPlayer = true;
        public float respawnDelay = 3f;

        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public System.Action<int, int> OnHealthChanged; // current, max
        public System.Action OnDeath;
        public System.Action OnRespawn;

        ulong lastAttackerClientId;
        string lastAttackerWeapon = "";
        Transform[] spawnPoints;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                currentHealth.Value = maxHealth;

            currentHealth.OnValueChanged += (prev, curr) =>
            {
                OnHealthChanged?.Invoke(curr, maxHealth);
            };

            isDead.OnValueChanged += (prev, curr) =>
            {
                if (curr) OnDeath?.Invoke();
                else OnRespawn?.Invoke();
            };

            // Initial notification
            OnHealthChanged?.Invoke(currentHealth.Value, maxHealth);
        }

        public void TakeDamage(int amount, ulong attackerClientId) => TakeDamage(amount, attackerClientId, "");

        public void TakeDamage(int amount, ulong attackerClientId, string weapon)
        {
            if (!IsServer) return;
            if (isDead.Value) return;

            lastAttackerClientId = attackerClientId;
            lastAttackerWeapon = weapon;
            currentHealth.Value = Mathf.Max(0, currentHealth.Value - amount);

            if (currentHealth.Value <= 0)
                Die();
        }

        void Die()
        {
            if (!IsServer) return;
            isDead.Value = true;

            if (isPlayer)
            {
                // Tell the match director who died (and who got the kill, for FFA scoring)
                MatchNet.Instance?.ReportKill(OwnerClientId, lastAttackerClientId, lastAttackerWeapon);
                // Respawn after delay
                Invoke(nameof(Respawn), respawnDelay);
            }
            else
            {
                // AI enemy: despawn after short delay
                Invoke(nameof(DespawnEnemy), 2f);
            }
        }

        void Respawn()
        {
            if (!IsServer) return;

            // Find spawn point
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                var spawns = GameObject.FindGameObjectsWithTag("SpawnPoint");
                spawnPoints = new Transform[spawns.Length];
                for (int i = 0; i < spawns.Length; i++)
                    spawnPoints[i] = spawns[i].transform;
            }

            Vector3 pos = new Vector3(Random.Range(-15f, 15f), 1.5f, Random.Range(-15f, 15f));
            if (spawnPoints != null && spawnPoints.Length > 0)
                pos = spawnPoints[Random.Range(0, spawnPoints.Length)].position;

            // Teleport player
            var cc = GetComponent<CharacterController>();
            if (cc)
            {
                cc.enabled = false;
                transform.position = pos;
                cc.enabled = true;
            }
            else
            {
                transform.position = pos;
            }

            currentHealth.Value = maxHealth;
            isDead.Value = false;
        }

        void DespawnEnemy()
        {
            if (!IsServer) return;
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}
