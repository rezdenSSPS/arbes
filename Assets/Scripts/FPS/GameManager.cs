using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using TMPro;

namespace AirsoftFPS
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            // When a player spawns, wire up their HUD
            NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerJoined;

            // If this is the local player already spawned, set up HUD
            if (NetworkManager.Singleton.LocalClient?.PlayerObject != null)
                SetupLocalPlayer(NetworkManager.Singleton.LocalClient.PlayerObject);
        }

        void OnPlayerJoined(ulong clientId)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // Small delay to let player object spawn
                Invoke(nameof(SetupLocalPlayerDelayed), 0.5f);
            }
        }

        void SetupLocalPlayerDelayed()
        {
            var playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (playerObj != null)
                SetupLocalPlayer(playerObj);
        }

        void SetupLocalPlayer(NetworkObject playerObj)
        {
            var health = playerObj.GetComponent<Health>();
            var weaponCtrl = playerObj.GetComponent<WeaponController>();

            // Find HUD in scene
            var hud = FindAnyObjectByType<HUDManager>();
            if (hud != null)
                hud.Setup(health, weaponCtrl);
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerJoined;
        }
    }
}
