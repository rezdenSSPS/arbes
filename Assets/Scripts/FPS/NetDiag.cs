using System.Text;
using System.IO;
using UnityEngine;
using Unity.Netcode;

namespace AirsoftFPS
{
    // Self-bootstrapping network/camera diagnostic. Runs in the main editor and each
    // MPPM virtual player with no scene wiring. Writes a status file into the project
    // folder (one per instance, keyed by the MPPM -name arg) and draws the same text
    // on-screen. Temporary -- removed once multiplayer spawn is confirmed working.
    public class NetDiag : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Editor/MPPM diagnostics only -- never show debug overlay in shipped builds.
            if (!Application.isEditor) return;
            var go = new GameObject("~NetDiag");
            DontDestroyOnLoad(go);
            go.AddComponent<NetDiag>();
        }

        string text = "NetDiag starting...";
        float timer;
        string root;
        string instance = "Main";
        GUIStyle style;

        void Start()
        {
            // Project folder on this Mac (editor: parent of Assets; build: known path).
            root = Application.isEditor
                ? Directory.GetParent(Application.dataPath).FullName
                : "/Users/denisreznicek/arbes";

            // MPPM launches each editor instance with "-name Player1/Player2/..."
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-name") { instance = args[i + 1]; break; }
            if (!Application.isEditor) instance = "Build";
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            text = Build();
            try { File.WriteAllText(Path.Combine(root, "netdiag_" + instance + ".txt"), text); } catch { }
        }

        string Build()
        {
            var sb = new StringBuilder();
            sb.Append("instance=").Append(instance).Append('\n');
            sb.Append("scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');
            sb.Append("time=").Append(Time.realtimeSinceStartup.ToString("F1")).Append('\n');

            var nm = NetworkManager.Singleton;
            if (nm == null) { sb.Append("NetworkManager=NULL\n"); return sb.ToString(); }

            sb.Append("IsListening=").Append(nm.IsListening)
              .Append(" IsServer=").Append(nm.IsServer)
              .Append(" IsClient=").Append(nm.IsClient)
              .Append(" IsHost=").Append(nm.IsHost)
              .Append(" IsConnectedClient=").Append(nm.IsConnectedClient).Append('\n');
            sb.Append("LocalClientId=").Append(nm.LocalClientId).Append('\n');

            bool lcNull = nm.LocalClient == null;
            sb.Append("LocalClient=").Append(lcNull ? "NULL" : "ok").Append('\n');
            if (!lcNull)
            {
                var po = nm.LocalClient.PlayerObject;
                if (po == null) sb.Append("PlayerObject=NULL\n");
                else sb.Append("PlayerObject=").Append(po.name)
                       .Append(" IsOwner=").Append(po.IsOwner)
                       .Append(" IsSpawned=").Append(po.IsSpawned)
                       .Append(" pos=").Append(po.transform.position.ToString("F1")).Append('\n');
            }

            int spawnedCount = 0;
            if (nm.SpawnManager != null && nm.SpawnManager.SpawnedObjectsList != null)
                spawnedCount = nm.SpawnManager.SpawnedObjectsList.Count;
            sb.Append("SpawnedObjects=").Append(spawnedCount).Append('\n');

            if (nm.IsServer && nm.ConnectedClientsIds != null)
            {
                sb.Append("ServerClients=");
                foreach (var id in nm.ConnectedClientsIds) sb.Append(id).Append(' ');
                sb.Append('\n');
            }

            var cams = Camera.allCameras;
            sb.Append("EnabledCameras=").Append(cams.Length).Append(" [");
            foreach (var c in cams) sb.Append(c.name).Append(c.enabled ? "(on) " : "(off) ");
            sb.Append("]\n");
            sb.Append("Camera.main=").Append(Camera.main ? Camera.main.name : "NULL").Append('\n');

            return sb.ToString();
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.fontSize = 16;
                style.normal.textColor = Color.yellow;
                style.alignment = TextAnchor.UpperLeft;
            }
            GUI.Label(new Rect(14, 60, 900, 500), text, style);
        }
    }
}
