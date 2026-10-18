using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace AirsoftFPS
{
    // Lives on the NetworkManager (DontDestroyOnLoad). Players do NOT auto-spawn
    // on connect; instead they spawn only once the match scene has loaded, so the
    // lobby/waiting room has no avatars. Clients who join after the match started
    // are dropped straight in.
    public class PlayerSpawner : MonoBehaviour
    {
        public GameObject playerPrefab;
        public string matchScene = "AirsoftArena";

        bool matchLive;

        void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnServerStarted -= OnServerStarted;
            nm.OnClientConnectedCallback -= OnClientConnected;
            if (nm.SceneManager != null)
                nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        void OnServerStarted()
        {
            matchLive = false;
            var sm = NetworkManager.Singleton.SceneManager;
            if (sm != null)
            {
                sm.OnLoadEventCompleted -= OnLoadEventCompleted; // avoid double-subscribe on re-host
                sm.OnLoadEventCompleted += OnLoadEventCompleted;
            }
        }

        void OnLoadEventCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (sceneName != matchScene) return;

            matchLive = true;
            foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
                SpawnFor(id);
        }

        void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (matchLive) SpawnFor(clientId); // late joiner drops straight into the match
        }

        void SpawnFor(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (playerPrefab == null) return;
            if (!nm.ConnectedClients.ContainsKey(clientId)) return;
            if (nm.ConnectedClients[clientId].PlayerObject != null) return; // already has an avatar

            var go = Instantiate(playerPrefab, SpawnPoint(), Quaternion.identity);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        }

        Vector3 SpawnPoint()
        {
            var points = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (points != null && points.Length > 0)
                return points[Random.Range(0, points.Length)].transform.position + Vector3.up * 0.5f;
            return new Vector3(Random.Range(-10f, 10f), 2f, Random.Range(-10f, 10f));
        }
    }
}
