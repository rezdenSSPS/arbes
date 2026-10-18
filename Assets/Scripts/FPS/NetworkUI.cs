using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;
using UnityEngine.InputSystem;
using System.Text;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

namespace AirsoftFPS
{
    // Flow: Connect screen (Host/Join, Online or LAN) -> Waiting room (shows the
    // join code and connected players; host presses START) -> match scene.
    public class NetworkUI : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject connectPanel;
        public GameObject waitingPanel;

        [Header("Connect buttons")]
        public Button hostOnlineButton;
        public Button joinOnlineButton;
        public Button hostLanButton;
        public Button joinLanButton;
        public Button quitButton;
        public TMP_InputField codeInput;   // join code (Online) or host IP (LAN)
        public TextMeshProUGUI statusText;

        [Header("Waiting room")]
        public TextMeshProUGUI waitCodeText;
        public TextMeshProUGUI waitPlayersText;
        public Button startButton;         // host only
        public Button leaveButton;

        const ushort PORT = 7777;
        const int MAX_PLAYERS = 8;
        bool busy;
        bool inWaitingRoom;
        bool isHost;

        // Runtime-built lobby extras (cloned from existing UI, no scene wiring needed)
        TMP_InputField nameInput;
        bool waitingExtrasBuilt;

        // Join code of the current online host session (also shown in the in-game HUD)
        public static string JoinCode = "";

        // Domain reload is disabled (Enter Play Mode Options), so statics survive
        // between play sessions -- reset here so a stale code never shows in the HUD.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => JoinCode = "";

        void Start()
        {
            if (hostOnlineButton) hostOnlineButton.onClick.AddListener(() => _ = OnHostOnline());
            if (joinOnlineButton) joinOnlineButton.onClick.AddListener(() => _ = OnJoinOnline());
            if (hostLanButton) hostLanButton.onClick.AddListener(() => _ = OnHostLan());
            if (joinLanButton) joinLanButton.onClick.AddListener(() => _ = OnJoinLan());
            if (quitButton) quitButton.onClick.AddListener(OnQuit);
            if (startButton) startButton.onClick.AddListener(OnStartMatch);
            if (leaveButton) leaveButton.onClick.AddListener(OnLeave);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1;

            if (connectPanel) connectPanel.SetActive(true);
            if (waitingPanel) waitingPanel.SetActive(false);

            if (statusText)
                statusText.text = "Online: play anywhere with a code.   LAN: same WiFi, use the host's IP.";

            // If we're still connected (e.g. returned here after a match), skip the
            // connect screen and go straight back to the waiting room.
            var net = NetworkManager.Singleton;
            if (net != null && (net.IsServer || net.IsConnectedClient))
                EnterWaitingRoom(net.IsServer, net.IsServer ? ("JOIN CODE:   " + JoinCode) : "CONNECTED");
        }

        void Update()
        {
            if (!inWaitingRoom || !NetworkManager.Singleton) return;
            RefreshWaitingUI();
        }

        // ───────── Lobby roster / ready / mode (built on MatchNet) ─────────
        void BuildWaitingExtras()
        {
            if (waitingExtrasBuilt || waitingPanel == null) return;
            waitingExtrasBuilt = true;

            // Roster text: taller band, top-aligned, below the join-code line.
            if (waitPlayersText != null)
            {
                var rt = waitPlayersText.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, 48f);
                rt.sizeDelta = new Vector2(560f, 180f);
                waitPlayersText.enableAutoSizing = false;
                waitPlayersText.fontSize = 24f;
                waitPlayersText.alignment = TextAlignmentOptions.Top;
                waitPlayersText.textWrappingMode = TextWrappingModes.NoWrap;
                waitPlayersText.richText = true;
            }

            // Slide the Start/Leave button column down to clear the roster.
            if (startButton != null && startButton.transform.parent is RectTransform col)
                col.anchoredPosition = new Vector2(col.anchoredPosition.x, -255f);

            // Name field: clone the working code input, place it above the buttons.
            if (codeInput != null && nameInput == null)
            {
                var go = Instantiate(codeInput.gameObject, waitingPanel.transform);
                go.name = "NameInput";
                nameInput = go.GetComponent<TMP_InputField>();
                if (nameInput != null)
                {
                    nameInput.onValueChanged.RemoveAllListeners();
                    nameInput.onEndEdit.RemoveAllListeners();
                    nameInput.characterLimit = 16;
                    if (nameInput.placeholder is TextMeshProUGUI ph) ph.text = "Your name";
                    var rt = nameInput.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0f, -150f);
                    rt.sizeDelta = new Vector2(360f, 48f);
                    if (MatchNet.Instance != null && !string.IsNullOrEmpty(MatchNet.Instance.LocalName))
                        nameInput.text = MatchNet.Instance.LocalName;
                    nameInput.onEndEdit.AddListener(v => { if (MatchNet.Instance != null) MatchNet.Instance.SetLocalName(v); });
                }
            }
        }

        void CaptureName()
        {
            if (nameInput != null && MatchNet.Instance != null)
                MatchNet.Instance.SetLocalName(nameInput.text);
        }

        void RefreshWaitingUI()
        {
            var mnet = MatchNet.Instance;
            var net = NetworkManager.Singleton;

            var kb = Keyboard.current;
            if (kb != null && mnet != null)
            {
                if (kb.rKey.wasPressedThisFrame) mnet.ToggleReady();
                if (isHost && kb.mKey.wasPressedThisFrame) mnet.HostCycleMode();
            }

            if (waitPlayersText != null)
            {
                if (mnet != null)
                {
                    string modeName = mnet.Mode == MatchNet.GameModeType.FreeForAll ? "Free-for-All Deathmatch" : "Co-op Survival";
                    var sb = new StringBuilder();
                    sb.AppendLine("MODE:  " + modeName);
                    foreach (var e in mnet.Roster)
                    {
                        string tag = e.ready ? "<color=#5BD16A>READY</color>" : "<color=#888888>not ready</color>";
                        string you = (net != null && e.clientId == net.LocalClientId) ? "  (you)" : "";
                        sb.AppendLine(e.name + you + "    " + tag);
                    }
                    sb.AppendLine("[R] ready up" + (isHost ? "        [M] change mode" : ""));
                    if (isHost)
                        sb.AppendLine(mnet.AllReady() ? "<color=#5BD16A>All ready — press START</color>" : "Waiting for everyone to ready up...");
                    waitPlayersText.text = sb.ToString();
                }
            }

            if (startButton != null && isHost)
                startButton.interactable = (mnet != null && mnet.AllReady());
        }

        UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();

        void EnterWaitingRoom(bool host, string codeOrAddress)
        {
            inWaitingRoom = true;
            isHost = host;
            if (connectPanel) connectPanel.SetActive(false);
            if (waitingPanel) waitingPanel.SetActive(true);
            if (waitCodeText) waitCodeText.text = codeOrAddress;
            if (startButton) startButton.gameObject.SetActive(host);
            BuildWaitingExtras();
        }

        // ───────── ONLINE (Relay via Multiplayer Sessions) ─────────
        async Task EnsureServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // If the MPPM auto-connect (or a previous attempt) left the NetworkManager
        // running, shut it down first -- otherwise a new session can't start netcode
        // ("NetworkManager is already connected") and the join silently half-fails.
        async Task ResetNetcodeAsync()
        {
            var auto = GameObject.Find("~AutoConnect");
            if (auto) Destroy(auto);

            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsListening || nm.IsClient || nm.IsServer))
            {
                nm.Shutdown();
                float deadline = Time.realtimeSinceStartup + 3f;
                while (nm != null && nm.IsListening && Time.realtimeSinceStartup < deadline)
                    await Task.Yield();
            }
        }

        async Task OnHostOnline()
        {
            if (busy) return;
            busy = true;
            SetConnectButtons(false);
            try
            {
                await ResetNetcodeAsync();
                if (statusText) statusText.text = "Connecting to Unity servers...";
                await EnsureServicesAsync();

                if (statusText) statusText.text = "Creating online game...";
                CaptureName();
                var options = new SessionOptions { MaxPlayers = MAX_PLAYERS }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                JoinCode = session.Code;

                EnterWaitingRoom(true, "JOIN CODE:   " + session.Code);
            }
            catch (Exception e)
            {
                if (statusText) statusText.text = "Online host failed (is the project linked to Unity Services?): " + e.Message;
                SetConnectButtons(true);
            }
            finally { busy = false; }
        }

        async Task OnJoinOnline()
        {
            if (busy) return;
            string code = codeInput ? codeInput.text.Trim().ToUpper() : "";
            if (string.IsNullOrEmpty(code))
            {
                if (statusText) statusText.text = "Type the host's join code first.";
                return;
            }
            busy = true;
            SetConnectButtons(false);
            JoinCode = "";
            try
            {
                await ResetNetcodeAsync();
                if (statusText) statusText.text = "Connecting to Unity servers...";
                await EnsureServicesAsync();

                if (statusText) statusText.text = "Joining game " + code + " ...";
                CaptureName();
                await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

                EnterWaitingRoom(false, "JOINED:   " + code);
            }
            catch (Exception e)
            {
                if (statusText) statusText.text = "Join failed — check the code. (" + e.Message + ")";
                SetConnectButtons(true);
            }
            finally { busy = false; }
        }

        // ───────── LAN (direct IP) ─────────
        async Task OnHostLan()
        {
            if (busy) return;
            busy = true;
            try
            {
                JoinCode = "";
                SetConnectButtons(false);
                await ResetNetcodeAsync();
                CaptureName();
                Transport.SetConnectionData("0.0.0.0", PORT, "0.0.0.0");
                NetworkManager.Singleton.StartHost();
                EnterWaitingRoom(true, "LAN HOST:   " + GetLocalIPv4());
            }
            finally { busy = false; }
        }

        async Task OnJoinLan()
        {
            if (busy) return;
            busy = true;
            try
            {
                string ip = (codeInput && !string.IsNullOrWhiteSpace(codeInput.text)) ? codeInput.text.Trim() : "127.0.0.1";
                JoinCode = "";
                SetConnectButtons(false);
                await ResetNetcodeAsync();
                CaptureName();
                Transport.SetConnectionData(ip, PORT);
                if (statusText) statusText.text = "Connecting to " + ip + " ...";
                NetworkManager.Singleton.OnClientConnectedCallback += OnLanConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnLanDisconnected;
                NetworkManager.Singleton.StartClient();
            }
            finally { busy = false; }
        }

        void OnLanConnected(ulong id)
        {
            if (id != NetworkManager.Singleton.LocalClientId) return;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLanConnected;
            EnterWaitingRoom(false, "LAN JOINED");
        }

        void OnLanDisconnected(ulong id)
        {
            if (id != NetworkManager.Singleton.LocalClientId) return;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLanDisconnected;
            if (statusText) statusText.text = "Couldn't connect. Check IP / same WiFi — eduroam blocks LAN, use Online.";
            SetConnectButtons(true);
        }

        // ───────── Waiting room actions ─────────
        void OnStartMatch()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (startButton) startButton.interactable = false;
            if (MatchNet.Instance != null)
                MatchNet.Instance.HostStartMatch();   // sets the mode, then loads the arena
            else
                NetworkManager.Singleton.SceneManager.LoadScene("AirsoftArena",
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        void OnLeave()
        {
            JoinCode = "";
            if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
            inWaitingRoom = false;
            if (waitingPanel) waitingPanel.SetActive(false);
            if (connectPanel) connectPanel.SetActive(true);
            SetConnectButtons(true);
            if (statusText) statusText.text = "Left the room.";
        }

        // ───────── shared ─────────
        void SetConnectButtons(bool on)
        {
            if (hostOnlineButton) hostOnlineButton.interactable = on;
            if (joinOnlineButton) joinOnlineButton.interactable = on;
            if (hostLanButton) hostLanButton.interactable = on;
            if (joinLanButton) joinLanButton.interactable = on;
        }

        void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        string GetLocalIPv4()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    s.Connect("8.8.8.8", 65530);
                    var ep = s.LocalEndPoint as IPEndPoint;
                    return ep != null ? ep.Address.ToString() : "127.0.0.1";
                }
            }
            catch { return "127.0.0.1"; }
        }
    }
}
