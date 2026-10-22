using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;
using System.Collections.Generic;

namespace AirsoftFPS
{
    // Plain MonoBehaviour so it runs even if in-scene NetworkObject spawning
    // misbehaves. It spawns networked enemies directly on the host/server.
    public class EnemySpawner : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public Transform[] spawnPoints;
        public NavMeshSurface navSurface;
        public int maxEnemies = 8;
        public float spawnInterval = 2.5f;
        public int enemiesPerWave = 3;
        public float waveDelay = 8f;

        public int CurrentWave { get; private set; } = 1;

        int enemiesAlive;
        float nextSpawnTime;
        int spawnedThisWave;
        bool started;
        readonly List<NetworkObject> activeEnemies = new List<NetworkObject>();

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer) return;

            // Only spawn AI waves in modes that use them (Co-op). PvP (FFA) turns it off.
            if (!MatchNet.EnemiesEnabled) return;

            if (!started)
            {
                started = true;
                // Bake the navmesh at runtime so agents always have a surface to walk on
                if (navSurface != null) navSurface.BuildNavMesh();
                nextSpawnTime = Time.time + 1.5f;
            }

            activeEnemies.RemoveAll(e => e == null || !e.IsSpawned);
            enemiesAlive = activeEnemies.Count;

            if (Time.time < nextSpawnTime) return;

            int waveMax = enemiesPerWave + (CurrentWave - 1) * 2;

            if (spawnedThisWave >= waveMax)
            {
                // Whole wave spawned — wait until cleared, then advance
                if (enemiesAlive == 0)
                {
                    CurrentWave++;
                    MatchNet.Instance?.ServerSetCoopWave(CurrentWave);
                    spawnedThisWave = 0;
                    nextSpawnTime = Time.time + waveDelay;
                }
            }
            else if (enemiesAlive < maxEnemies)
            {
                SpawnEnemy();
                spawnedThisWave++;
                nextSpawnTime = Time.time + spawnInterval;
            }
        }

        void SpawnEnemy()
        {
            if (enemyPrefab == null) return;

            Vector3 basePos;
            if (spawnPoints != null && spawnPoints.Length > 0)
                basePos = spawnPoints[Random.Range(0, spawnPoints.Length)].position;
            else
                basePos = new Vector3(Random.Range(-30f, 30f), 1f, Random.Range(-30f, 30f));

            // Snap onto the navmesh so the agent initialises correctly
            Vector3 pos = basePos;
            if (NavMesh.SamplePosition(basePos, out NavMeshHit navHit, 12f, NavMesh.AllAreas))
                pos = navHit.position;

            var enemy = Instantiate(enemyPrefab, pos, Quaternion.identity);
            var netObj = enemy.GetComponent<NetworkObject>();
            if (netObj == null) { Destroy(enemy); return; }

            // Max health before Spawn: Health.OnNetworkSpawn sends it (netMaxHealth) with the spawn.
            var hp = enemy.GetComponent<Health>();
            if (hp != null) hp.maxHealth = 60 + CurrentWave * 10;

            netObj.Spawn();
            activeEnemies.Add(netObj);

            var ai = enemy.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.detectionRange = 60f;
                ai.attackRange = 18f;
                ai.damage = 8 + CurrentWave * 2;
                ai.fireRate = Mathf.Min(2f + CurrentWave * 0.3f, 6f);
            }
        }
    }
}
