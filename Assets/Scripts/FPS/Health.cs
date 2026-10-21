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

        // Spawn protection: invulnerable for a few seconds after (re)spawning, ends early when the
        // player fires (WeaponController.ShootRpc). Server time; 0 = not protected.
        public const float SPAWN_PROTECTION_SECONDS = 3f;
        public NetworkVariable<double> spawnProtectedUntil = new NetworkVariable<double>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public float SpawnProtectionLeft => IsSpawned && isPlayer
            ? Mathf.Max(0f, (float)(spawnProtectedUntil.Value - NetworkManager.ServerTime.Time)) : 0f;
        public bool IsSpawnProtected => SpawnProtectionLeft > 0f;

        public System.Action<int, int> OnHealthChanged; // current, max
        public System.Action OnDeath;
        public System.Action OnRespawn;

        ulong lastAttackerClientId;
        string lastAttackerWeapon = "";
        NetworkObject lastAttackerObject;   // who dealt the last hit (AI enemies pass themselves), for the killcam

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                currentHealth.Value = maxHealth;
            if (IsServer && isPlayer) StartSpawnProtection();
            if (isPlayer && GetComponent<SpawnProtectionFX>() == null) gameObject.AddComponent<SpawnProtectionFX>();

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

        public void TakeDamage(int amount, ulong attackerClientId, string weapon, NetworkObject attackerObject = null)
        {
            if (!IsServer) return;
            if (isDead.Value) return;
            if (IsSpawnProtected) return;

            lastAttackerClientId = attackerClientId;
            lastAttackerWeapon = weapon;
            lastAttackerObject = attackerObject;
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
                MatchNet.Instance?.ReportKill(OwnerClientId, lastAttackerClientId, lastAttackerWeapon, lastAttackerObject);
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

            // Team side / away from enemies (PlayerSpawner). The owner moves the player: its
            // ClientNetworkTransform is owner-authoritative, so a server-side move would be undone.
            var spawn = PlayerSpawner.GetSpawnFor(OwnerClientId);
            RespawnTeleportRpc(spawn.position, spawn.rotation);

            currentHealth.Value = maxHealth;
            StartSpawnProtection();
            isDead.Value = false;
        }

        [Rpc(SendTo.Owner)]
        void RespawnTeleportRpc(Vector3 pos, Quaternion rot)
        {
            var cc = GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(pos, rot, transform.localScale);
            else transform.SetPositionAndRotation(pos, rot);
            if (cc) cc.enabled = true;
        }

        void StartSpawnProtection()
        {
            spawnProtectedUntil.Value = NetworkManager.ServerTime.Time + SPAWN_PROTECTION_SECONDS;
        }

        // Server: firing ends spawn protection early.
        public void EndSpawnProtection()
        {
            if (IsServer && spawnProtectedUntil.Value > 0) spawnProtectedUntil.Value = 0;
        }

        void DespawnEnemy()
        {
            if (!IsServer) return;
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}
