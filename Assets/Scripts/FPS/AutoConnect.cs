using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
#if UNITY_EDITOR
using UnityEditor;

#endif

namespace AirsoftFPS
{
    // One-press local co-op testing with Multiplayer Play Mode (MPPM).
    // Press Play in the main editor: it auto-hosts (when the toggle is ON), waits for
    // the virtual player to connect, then loads the arena so the networked scene load
    // propagates to the already-connected client (the same reliable path the real
    // START button uses). Loopback (127.0.0.1) -- no lobby, no code, no .app rebuild.
    // Editor-only; does nothing in a standalone build.
    public static class AutoConnect
    {
        const string PrefKey  = "Airsoft.MPPMAutoConnect";
        const string MenuPath = "Tools/Airsoft/MPPM Auto-Connect (loopback)";

#if UNITY_EDITOR
        static bool Enabled => EditorPrefs.GetBool(PrefKey, false);

        [MenuItem(MenuPath)]
        static void Toggle() => EditorPrefs.SetBool(PrefKey, !Enabled);

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
#if UNITY_EDITOR
            bool isMain = Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
            // Main editor auto-hosts only when the toggle is ON (so normal solo play
            // still uses the lobby). Virtual players exist only for MP testing and run
            // as a separate process that does NOT share this editor's prefs, so they
            // always auto-join regardless of the toggle.
            if (isMain && !Enabled) return;

            var go = new GameObject("~AutoConnect");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<AutoConnectRunner>().asHost = isMain;
#endif
        }
    }

    public class AutoConnectRunner : MonoBehaviour
    {
        public bool asHost;
        const ushort PORT = 7777;

        float t;
        bool startedNet;
        bool loadedArena;

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            t += Time.unscaledDeltaTime;

            if (asHost)
            {
                if (!startedNet && t >= 1.0f)
                {
                    startedNet = true;
                    if (!nm.IsListening)
                    {
                        nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", PORT, "0.0.0.0");
                        nm.StartHost();
                    }
                }

                // Wait for the client to connect before loading the arena, so the
                // networked scene load reaches it. Fall back after 9s (solo / no client).
                if (startedNet && !loadedArena && nm.IsServer)
                {
                    bool clientJoined = nm.ConnectedClientsIds.Count >= 2;
                    if (clientJoined || t >= 9.0f)
                    {
                        loadedArena = true;
                        nm.SceneManager.LoadScene("AirsoftArena",
                            UnityEngine.SceneManagement.LoadSceneMode.Single);
                        enabled = false;
                    }
                }
            }
            else
            {
                // Client just connects and waits; the host's networked scene load pulls
                // it into the arena and PlayerSpawner gives it a player.
                if (!startedNet && t >= 2.0f)
                {
                    startedNet = true;
                    if (!nm.IsClient && !nm.IsListening)
                    {
                        nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", PORT);
                        nm.StartClient();
                    }
                    enabled = false;
                }
            }
        }
    }
}
