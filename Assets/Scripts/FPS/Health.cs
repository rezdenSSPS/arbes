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

        // maxHealth synced from the server (enemy waves and bots change it per spawn; clients only know the prefab value)
        public NetworkVariable<int> netMaxHealth = new NetworkVariable<int>(0,
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
        public float SpawnProtectionLeft => IsSpawned && IsCombatant
            ? Mathf.Max(0f, (float)(spawnProtectedUntil.Value - NetworkManager.ServerTime.Time)) : 0f;
        public bool IsSpawnProtected => SpawnProtectionLeft > 0f;

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

        // PvP bots (BotBrain): an AI soldier (Enemy prefab) that plays as a roster entry. It has
        // no owning client, so it is identified by its bot id (MatchNet.BOT_ID_BASE + n) instead.
        // Set by the server before Spawn, so clients have it in OnNetworkSpawn.
        public const ulong NO_BOT = ulong.MaxValue;
        public const int BOT_MAX_HEALTH = 100;   // same as a player
        public NetworkVariable<ulong> botId = new NetworkVariable<ulong>(NO_BOT,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        public bool IsBot => botId.Value != NO_BOT;
        // Plays like a player (roster entry, scoring, spawn protection, Milsim HIT, respawn).
        public bool IsCombatant => isPlayer || IsBot;
        // Roster id: the owning client for players, the bot id for bots.
        public ulong MatchId => IsBot ? botId.Value : OwnerClientId;
        public System.Action<bool> OnHitChanged;   // true = just got hit, false = back in the game

        public System.Action<int, int> OnHealthChanged; // current, max
        public System.Action OnDeath;
        public System.Action OnRespawn;

        ulong lastAttackerClientId;
        string lastAttackerWeapon = "";
        NetworkObject lastAttackerObject;   // who dealt the last hit (AI enemies pass themselves), for the killcam

        public override void OnNetworkSpawn()
        {
            if (IsBot) maxHealth = BOT_MAX_HEALTH;
            if (IsServer)
            {
                netMaxHealth.Value = maxHealth;
                currentHealth.Value = maxHealth;
            }
            else if (netMaxHealth.Value > 0) maxHealth = netMaxHealth.Value;
            netMaxHealth.OnValueChanged += (prev, curr) =>
            {
                if (curr <= 0) return;
                maxHealth = curr;
                OnHealthChanged?.Invoke(currentHealth.Value, maxHealth);
            };
            if (IsServer && IsCombatant) StartSpawnProtection();
            if (IsCombatant && GetComponent<SpawnProtectionFX>() == null) gameObject.AddComponent<SpawnProtectionFX>();
            if (GetComponent<FootstepAudio>() == null) gameObject.AddComponent<FootstepAudio>();

            currentHealth.OnValueChanged += (prev, curr) =>
            {
                OnHealthChanged?.Invoke(curr, maxHealth);
                AirsoftAudio.OnHealthChanged(this, prev, curr);
            };

            isDead.OnValueChanged += (prev, curr) =>
            {
                if (curr) OnDeath?.Invoke();
                else OnRespawn?.Invoke();
                AirsoftAudio.OnDeadChanged(this, curr);
            };

            isHit.OnValueChanged += (prev, curr) =>
            {
                OnHitChanged?.Invoke(curr);
                AirsoftAudio.OnHitChanged(this, curr);
            };

            // Initial notification
            OnHealthChanged?.Invoke(currentHealth.Value, maxHealth);
        }

        // HIT (and dead) players don't stop BBs: the BB flies on through them (BBBallistics.CastSegment).
        public static bool PassesBBs(Collider c)
        {
            if (c == null) return false;
            var h = c.GetComponentInParent<Health>();
            return h != null && h.IsSpawned && (h.isHit.Value || h.isDead.Value);
        }

        public void TakeDamage(int amount, ulong attackerClientId) => TakeDamage(amount, attackerClientId, "");

        public void TakeDamage(int amount, ulong attackerClientId, string weapon, NetworkObject attackerObject = null)
        {
            if (!IsServer) return;
            if (isDead.Value || isHit.Value) return;
            if (IsSpawnProtected) return;

            if (IsCombatant && MatchNet.Instance != null && MatchNet.Instance.UsesHitRule)   // Milsim, Medic
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

            if (IsCombatant)
            {
                // Tell the match director who died (and who got the kill, for FFA scoring)
                MatchNet.Instance?.ReportKill(MatchId, lastAttackerClientId, lastAttackerWeapon, lastAttackerObject);
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
            // round modes (Bomb, VIP escort): stay down until the mode lets you back in
            if (IsCombatant && ObjectiveMode.HoldsRespawn(MatchId)) { Invoke(nameof(Respawn), 0.25f); return; }

            // Team side / away from enemies (PlayerSpawner). The owner moves the player: its
            // ClientNetworkTransform is owner-authoritative, so a server-side move would be undone.
            var spawn = PlayerSpawner.GetSpawnFor(MatchId);
            var bot = IsBot ? GetComponent<BotBrain>() : null;
            if (bot != null) bot.Teleport(spawn.position, spawn.rotation);   // bots: the server moves them
            else RespawnTeleportRpc(spawn.position, spawn.rotation);

            // A remote owner moves its own avatar (ClientNetworkTransform): until its teleport comes back
            // the server still has the body at the death spot. Keep it dead until then, or it could be shot
            // or pick up a flag there (health and protection are restored when it is really back).
            if (bot == null && ExpectOwnerMove(spawn.position)) return;
            currentHealth.Value = maxHealth;
            StartSpawnProtection();
            isDead.Value = false;
        }

        // ───── waiting for a remote owner's respawn teleport (server) ─────
        const double OWNER_MOVE_TIMEOUT = 2.0;
        Vector3 pendingSpawnPos;
        double pendingSpawnUntil;   // 0 = not waiting

        // Server: true while the owner of this avatar hasn't arrived at its new spawn point yet
        // (objectives ignore the player meanwhile).
        public bool ServerAwaitingRespawnMove => pendingSpawnUntil > 0;

        bool ExpectOwnerMove(Vector3 pos)
        {
            if (IsOwnedByServer) return false;   // the host's own avatar teleports at once
            pendingSpawnPos = pos;
            pendingSpawnUntil = NetworkManager.ServerTime.Time + OWNER_MOVE_TIMEOUT;
            return true;
        }

        void TickOwnerMove()
        {
            Vector3 d = transform.position - pendingSpawnPos; d.y = 0;
            bool arrived = d.sqrMagnitude < 4f;
            if (!arrived && NetworkManager.ServerTime.Time < pendingSpawnUntil) return;
            pendingSpawnUntil = 0;
            if (isDead.Value)
            {
                currentHealth.Value = maxHealth;
                StartSpawnProtection();   // protection counts from when they are really back
                isDead.Value = false;
            }
        }

        // Server: bring this player back right now at pose, alive with full health (round modes: everyone
        // at their side when a new round starts, dead or alive).
        public void ServerForceRespawn(Pose pose)
        {
            if (!IsServer || !IsSpawned) return;
            CancelInvoke(nameof(Respawn));
            pendingSpawnUntil = 0;   // back right now: no longer waiting for an earlier respawn move
            var bot = IsBot ? GetComponent<BotBrain>() : null;
            if (bot != null) bot.Teleport(pose.position, pose.rotation);
            else RespawnTeleportRpc(pose.position, pose.rotation);
            currentHealth.Value = maxHealth;
            hitRespawnAt.Value = 0;
            isHit.Value = false;
            StartSpawnProtection();
            // No waiting for the owner's teleport here: round modes freeze everyone (spawn-protected) in Prep,
            // and marking the new round's players inactive made Bomb drop a remote carrier's bomb.
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

        // ───── Milsim ─────
        void MarkHit(ulong shooterClientId, string weapon)
        {
            isHit.Value = true;
            hitRespawnAt.Value = 0;
            MatchNet.Instance?.ReportHit(MatchId, shooterClientId, weapon);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (pendingSpawnUntil > 0) TickOwnerMove();
            if (isHit.Value) TickHitRespawn();
        }

        // Server: once the HIT player stands in their team's zone, count down and bring them back.
        // Leaving the zone cancels the countdown.
        void TickHitRespawn()
        {
            var mn = MatchNet.Instance;
            var nm = NetworkManager;
            if (nm == null) return;
            double now = nm.ServerTime.Time;
            // the objective keeps them out for now (Medic: downed waiting for a medic, or no tickets left)
            if (ObjectiveMode.HoldsHit(MatchId)) { if (hitRespawnAt.Value != 0) hitRespawnAt.Value = 0; return; }
            int team = mn != null ? mn.TeamOf(MatchId) : 0;
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
            StartSpawnProtection();
        }

        // Server: back in the game right where they are (Medic revive), with a short spawn protection.
        public void ReviveFromHit(float protectSeconds)
        {
            if (!IsServer || !isHit.Value) return;
            ClearHit();
            spawnProtectedUntil.Value = NetworkManager.ServerTime.Time + protectSeconds;
        }

        void DespawnEnemy()
        {
            if (!IsServer) return;
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}
