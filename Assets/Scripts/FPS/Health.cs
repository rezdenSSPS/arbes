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

        // Milsim: one BB hit = out. The player stays alive but is HIT until they walk back to
        // their team's zone (MilsimZone) and wait there hitRespawnTime seconds.
        [Header("Milsim")]
        public float hitRespawnTime = 3f;

        public NetworkVariable<bool> isHit = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // Server time (NetworkManager.ServerTime) when the in-zone respawn finishes; 0 = not in the zone yet.
        public NetworkVariable<double> hitRespawnAt = new NetworkVariable<double>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool IsOut => isHit.Value;
        public System.Action<bool> OnHitChanged;   // true = just got hit, false = back in the game

        public System.Action<int, int> OnHealthChanged; // current, max
        public System.Action OnDeath;
        public System.Action OnRespawn;

        ulong lastAttackerClientId;
        string lastAttackerWeapon = "";
        NetworkObject lastAttackerObject;   // who dealt the last hit (AI enemies pass themselves), for the killcam
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

            isHit.OnValueChanged += (prev, curr) => OnHitChanged?.Invoke(curr);

            // Initial notification
            OnHealthChanged?.Invoke(currentHealth.Value, maxHealth);
        }

        // HIT players don't stop BBs: the BB flies on through them (BBBallistics.CastSegment).
        public static bool PassesBBs(Collider c)
        {
            if (c == null) return false;
            var h = c.GetComponentInParent<Health>();
            return h != null && h.IsSpawned && h.isHit.Value;
        }

        public void TakeDamage(int amount, ulong attackerClientId) => TakeDamage(amount, attackerClientId, "");

        public void TakeDamage(int amount, ulong attackerClientId, string weapon, NetworkObject attackerObject = null)
        {
            if (!IsServer) return;
            if (isDead.Value || isHit.Value) return;

            if (isPlayer && MatchNet.Instance != null && MatchNet.Instance.IsMilsim)
            {
                MarkHit(attackerClientId, weapon);
                return;
            }

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

        // ───── Milsim ─────
        void MarkHit(ulong shooterClientId, string weapon)
        {
            isHit.Value = true;
            hitRespawnAt.Value = 0;
            MatchNet.Instance?.ReportHit(OwnerClientId, shooterClientId, weapon);
        }

        void Update()
        {
            if (IsServer && IsSpawned && isHit.Value) TickHitRespawn();
        }

        // Server: once the HIT player stands in their team's zone, count down and bring them back.
        // Leaving the zone cancels the countdown.
        void TickHitRespawn()
        {
            var mn = MatchNet.Instance;
            var nm = NetworkManager;
            if (nm == null) return;
            double now = nm.ServerTime.Time;
            int team = mn != null ? mn.TeamOf(OwnerClientId) : 0;
            float outside = MilsimZone.DistanceOutside(team, transform.position, out _);
            bool inZone = outside <= 0f;   // no zone in this arena (-1): respawn where they stand

            if (!inZone) { if (hitRespawnAt.Value != 0) hitRespawnAt.Value = 0; return; }
            if (hitRespawnAt.Value == 0) { hitRespawnAt.Value = now + hitRespawnTime; return; }
            if (now >= hitRespawnAt.Value) ClearHit();
        }

        void ClearHit()
        {
            currentHealth.Value = maxHealth;
            hitRespawnAt.Value = 0;
            isHit.Value = false;
        }

        void DespawnEnemy()
        {
            if (!IsServer) return;
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}
