using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Flow: Main menu (Online / LAN, host or join) -> Connecting (progress / error)
    // -> Lobby (mode, roster, ready; host presses START) -> match scene.
    // The whole UI is built in code with UIKit; networking is unchanged.
    public class NetworkUI : MonoBehaviour
    {
        // Old scene-wired UI. Kept so existing scenes still load; hidden at runtime.
        [Header("Legacy UI (hidden at runtime)")]
        public GameObject connectPanel;
        public GameObject waitingPanel;
        public Button hostOnlineButton;
        public Button joinOnlineButton;
        public Button hostLanButton;
        public Button joinLanButton;
        public Button quitButton;
        public TMP_InputField codeInput;
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI waitCodeText;
        public TextMeshProUGUI waitPlayersText;
        public Button startButton;
        public Button leaveButton;

        const ushort PORT = 7777;
        const int MAX_PLAYERS = 8;
        bool busy;
        bool inWaitingRoom;
        bool isHost;
        bool cancelRequested;

        // Join code of the current online session (also shown in the in-game HUD)
        public static string JoinCode = "";
        // What the lobby header shows for LAN games (host IP / joined address)
        static string LanAddress = "";
        static bool LastWasLan;

        // Domain reload is disabled (Enter Play Mode Options), so statics survive
        // between play sessions -- reset here so a stale code never shows in the HUD.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { JoinCode = ""; LanAddress = ""; LastWasLan = false; }

        // ───────── built UI ─────────
        Canvas canvas;
        GameObject menuScreen, connectScreen, lobbyScreen;
        SettingsScreen settings;

        // menu
        bool lanTab;
        GameObject onlinePane, lanPane;
        TMP_InputField nameInput, codeField, ipField;
        TextMeshProUGUI menuHint;
        readonly List<KitButton> connectButtons = new List<KitButton>();

        // connecting
        GameObject progressPane, errorPane;
        Image cardFrame, progressFill;
        TextMeshProUGUI connCaption, connTitle, errTitle, errBody, errTips, errDetails;
        readonly List<(RectTransform done, RectTransform current, RectTransform pending, TextMeshProUGUI text)> stepRows = new();
        string[] steps = new string[0];
        Func<Task> retryAction;

        // lobby
        TextMeshProUGUI lobbyCodeCaption, lobbyCode, playerCount, hintText, readyLabel;
        KitButton readyButton, startMatchButton;
        RectTransform readyKey;
        TMP_InputField lobbyName;
        GameObject hostModeKey, clientModeNote, tdmNote;
        readonly List<(KitButton btn, Image frame, CanvasGroup group)> modeCards = new();
        readonly List<Slot> slots = new List<Slot>();
        float copyFlashUntil;
        float refreshTimer;

        class Slot
        {
            public Image bg, frame, swatch, swatchFrame;
            public TextMeshProUGUI name;
            public GameObject host, you, ready, notReady;
        }

        void Start()
        {
            // Hide the old scene-built panels (this component sits on the old canvas).
            foreach (Transform child in transform) child.gameObject.SetActive(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1;

            BuildUI();
            ShowScreen(menuScreen);

            // If we're still connected (e.g. returned here after a match), skip the
            // connect screen and go straight back to the lobby.
            var net = NetworkManager.Singleton;
            if (net != null && (net.IsServer || net.IsConnectedClient))
                EnterWaitingRoom(net.IsServer);
        }

        void Update()
        {
            // Only show while the lobby is the active scene (clients can keep it loaded
            // for a moment while the arena comes in).
            bool lobbyActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene() == gameObject.scene;
            if (canvas != null && canvas.gameObject.activeSelf != lobbyActive) canvas.gameObject.SetActive(lobbyActive);
            if (!lobbyActive) return;

            if (!inWaitingRoom || !NetworkManager.Singleton) return;

            // Host left / connection dropped: back to the menu.
            var nmNow = NetworkManager.Singleton;
            if (!busy && !nmNow.IsServer && !nmNow.IsClient)
            {
                inWaitingRoom = false;
                JoinCode = "";
                ShowScreen(menuScreen);
                MenuHint("Disconnected from the room.");
                return;
            }

            var mnet = MatchNet.Instance;
            var kb = Keyboard.current;
            bool overlay = (settings != null && settings.IsOpen)
                           || (CharacterCustomizerUI.Instance != null && CharacterCustomizerUI.Instance.IsOpen);
            if (kb != null && mnet != null && !overlay && !TypingInField())
            {
                if (kb.rKey.wasPressedThisFrame) mnet.ToggleReady();
                if (isHost && kb.mKey.wasPressedThisFrame) mnet.HostCycleMode();
            }

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.1f;
                RefreshLobby();
            }
        }

        // The menu canvas is a separate root object: it must go away with the lobby scene.
        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            if (CharacterCustomizerUI.Instance != null) CharacterCustomizerUI.Instance.PreviewWanted = false;
        }

        static bool TypingInField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            var f = go != null ? go.GetComponent<TMP_InputField>() : null;
            return f != null && f.isFocused;
        }

        UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();

        void ShowScreen(GameObject screen)
        {
            menuScreen.SetActive(screen == menuScreen);
            connectScreen.SetActive(screen == connectScreen);
            lobbyScreen.SetActive(screen == lobbyScreen);
            if (CharacterCustomizerUI.Instance != null)
                CharacterCustomizerUI.Instance.PreviewWanted = screen == lobbyScreen;
        }

        void EnterWaitingRoom(bool host)
        {
            inWaitingRoom = true;
            isHost = host;
            if (lobbyName != null && MatchNet.Instance != null) lobbyName.SetTextWithoutNotify(MatchNet.Instance.LocalName);
            startMatchButton.gameObject.SetActive(host);
            hostModeKey.SetActive(host);
            clientModeNote.SetActive(!host);
            ShowScreen(lobbyScreen);
            RefreshLobby();
        }

        // ───────── ONLINE (Relay via Multiplayer Sessions) ─────────
        async Task EnsureServicesAsync()
        {
            SetStep(0);
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            SetStep(1);
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
            cancelRequested = false;
            LastWasLan = false;
            SetConnectButtons(false);
            retryAction = OnHostOnline;
            ShowProgress("HOSTING ONLINE GAME", "Creating your room…",
                "Connecting to Unity servers", "Signing in", "Creating online game (Relay)", "Opening lobby");
            try
            {
                await ResetNetcodeAsync();
                await EnsureServicesAsync();

                SetStep(2);
                CaptureName();
                var options = new SessionOptions { MaxPlayers = MAX_PLAYERS }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                JoinCode = session.Code;

                SetStep(3);
                if (cancelRequested) { AbortConnection(); return; }
                EnterWaitingRoom(true);
            }
            catch (Exception e)
            {
                if (cancelRequested) { AbortConnection(); return; }
                ShowError("HOSTING ONLINE GAME", "Couldn’t create the room",
                    "Online games go through Unity’s servers. Hosting failed before the room was ready.",
                    "Check your internet connection · Make sure the project is linked to Unity Services · Try LAN on the same Wi-Fi",
                    e.Message);
            }
            finally { busy = false; SetConnectButtons(true); }
        }

        async Task OnJoinOnline()
        {
            if (busy) return;
            string code = codeField ? codeField.text.Trim().ToUpper() : "";
            if (string.IsNullOrEmpty(code))
            {
                MenuHint("Type the host’s join code first.");
                return;
            }
            busy = true;
            cancelRequested = false;
            LastWasLan = false;
            SetConnectButtons(false);
            JoinCode = "";
            retryAction = OnJoinOnline;
            ShowProgress("JOINING " + code, "Joining the room…",
                "Connecting to Unity servers", "Signing in", "Joining room " + code, "Opening lobby");
            try
            {
                await ResetNetcodeAsync();
                await EnsureServicesAsync();

                SetStep(2);
                CaptureName();
                await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                JoinCode = code;

                SetStep(3);
                if (cancelRequested) { AbortConnection(); return; }
                EnterWaitingRoom(false);
            }
            catch (Exception e)
            {
                if (cancelRequested) { AbortConnection(); return; }
                ShowError("JOINING " + code, "Couldn’t join the room",
                    "The code doesn’t match any open room. The host may have left, or the match has already started.",
                    "Check the 6 characters with the host · Ask the host to stay in the lobby · On school Wi-Fi use Online, not LAN",
                    e.Message);
            }
            finally { busy = false; SetConnectButtons(true); }
        }

        // ───────── LAN (direct IP) ─────────
        async Task OnHostLan()
        {
            if (busy) return;
            busy = true;
            try
            {
                JoinCode = "";
                LastWasLan = true;
                SetConnectButtons(false);
                await ResetNetcodeAsync();
                CaptureName();
                Transport.SetConnectionData("0.0.0.0", PORT, "0.0.0.0");
                NetworkManager.Singleton.StartHost();
                LanAddress = GetLocalIPv4();
                EnterWaitingRoom(true);
            }
            finally { busy = false; SetConnectButtons(true); }
        }

        async Task OnJoinLan()
        {
            if (busy) return;
            busy = true;
            try
            {
                string ip = (ipField && !string.IsNullOrWhiteSpace(ipField.text)) ? ipField.text.Trim() : "127.0.0.1";
                JoinCode = "";
                LastWasLan = true;
                LanAddress = ip;
                cancelRequested = false;
                SetConnectButtons(false);
                retryAction = OnJoinLan;
                ShowProgress("JOINING " + ip, "Connecting…", "Reaching " + ip + ":" + PORT, "Opening lobby");
                await ResetNetcodeAsync();
                CaptureName();
                Transport.SetConnectionData(ip, PORT);
                NetworkManager.Singleton.OnClientConnectedCallback += OnLanConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnLanDisconnected;
                NetworkManager.Singleton.StartClient();
            }
            finally { busy = false; SetConnectButtons(true); }
        }

        void OnLanConnected(ulong id)
        {
            if (id != NetworkManager.Singleton.LocalClientId) return;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLanConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLanDisconnected;
            SetStep(1);
            EnterWaitingRoom(false);
        }

        void OnLanDisconnected(ulong id)
        {
            if (id != NetworkManager.Singleton.LocalClientId) return;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLanConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLanDisconnected;
            if (cancelRequested) return;
            ShowError("JOINING " + LanAddress, "Couldn’t connect",
                "No game answered at " + LanAddress + ". The host must press HOST ON LAN first, and both computers must be on the same network.",
                "Check the IP address with the host · Use the same Wi-Fi · School networks (eduroam) block LAN, use Online",
                "Timed out connecting to " + LanAddress + ":" + PORT);
        }

        void CancelConnecting()
        {
            cancelRequested = true;
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnLanConnected;
                nm.OnClientDisconnectCallback -= OnLanDisconnected;
                if (!busy && (nm.IsListening || nm.IsClient)) nm.Shutdown();
            }
            ShowScreen(menuScreen);
            MenuHint("");
        }

        // A connection finished after the player pressed CANCEL: drop it again.
        void AbortConnection()
        {
            JoinCode = "";
            if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
            ShowScreen(menuScreen);
        }

        // ───────── Waiting room actions ─────────
        void OnStartMatch()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (MatchNet.Instance != null && !MatchNet.Instance.AllReady()) return;
            startMatchButton.interactable = false;
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
            ShowScreen(menuScreen);
            SetConnectButtons(true);
            MenuHint("Left the room.", false);
        }

        // ───────── shared ─────────
        void CaptureName()
        {
            if (nameInput != null && MatchNet.Instance != null)
                MatchNet.Instance.SetLocalName(nameInput.text);
        }

        void SetConnectButtons(bool on)
        {
            foreach (var b in connectButtons) if (b) b.interactable = on;
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

        // ════════════════════════ UI construction ════════════════════════
        void BuildUI()
        {
            canvas = UIKit.Canvas("AirsoftMenuCanvas", 10);
            menuScreen = BuildMenu(canvas.transform);
            connectScreen = BuildConnecting(canvas.transform);
            lobbyScreen = BuildLobby(canvas.transform);
            settings = SettingsScreen.Create(canvas.transform, null);
        }

        // ───────── main menu ─────────
        GameObject BuildMenu(Transform root)
        {
            var screen = Rect(root, "MenuScreen").Fill();
            Box(screen, "Ground", Ground, true).rectTransform.Fill();

            // right half: low-poly town illustration, bleeding to the screen edge
            var art = Box(screen, "Art", Scenery);
            art.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            art.rectTransform.anchorMax = Vector2.one;
            art.rectTransform.offsetMin = new Vector2(-120, 0);
            art.rectTransform.offsetMax = Vector2.zero;

            var stage = Rect(screen, "Stage").At(0.5f, 0.5f, 0, 0, 1280, 720);
            var town = Rect(stage, "Town").TL(520, 0, 760, 720);
            BuildTown(town);
            // dark left panel with a diagonal edge
            var left = Box(screen, "LeftFill", Ground);
            left.rectTransform.anchorMin = Vector2.zero;
            left.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            left.rectTransform.offsetMin = Vector2.zero;
            left.rectTransform.offsetMax = new Vector2(-640, 0);
            UIPoly.Create(stage, "Diagonal", Ground,
                new Vector2(0, -200), new Vector2(640, -200), new Vector2(640, 0), new Vector2(525, 720),
                new Vector2(525, 920), new Vector2(0, 920)).TL(0, 0, 640, 720);

            // left column
            var col = VCol(stage, "Column", 28);
            col.GetComponent<RectTransform>().TL(64, 48, 416, 632);

            var logo = HRow(col.transform, "Logo", 12);
            logo.LE(-1, 54);
            Box(logo.transform, "Bar", Accent).LE(14, 38);
            Label(logo.transform, "AIRSOFT", F.CondBlack, 54, Fg, TextAlignmentOptions.MidlineLeft, 0.04f).LE(-1, 54);

            var nameBlock = VCol(col.transform, "Callsign", 8);
            Caption(nameBlock.transform, "CALLSIGN").LE(-1, 14);
            nameInput = Input(nameBlock.transform, "Your name", F.Body, 16, 16);
            nameInput.LE(-1, 46);
            if (MatchNet.Instance != null) nameInput.SetTextWithoutNotify(MatchNet.Instance.LocalName);
            nameInput.onEndEdit.AddListener(v => { if (MatchNet.Instance != null) MatchNet.Instance.SetLocalName(v); });

            var connect = VCol(col.transform, "Connect", 18);
            var tabHolder = HRow(connect.transform, "TabHolder", 0);
            Segments(tabHolder.transform, new[] { "ONLINE", "LAN" }, LastWasLan ? 1 : 0, 40, 16, i => SetLanTab(i == 1));

            // ONLINE
            onlinePane = VCol(connect.transform, "OnlinePane", 16).gameObject;
            var hostOnline = HostButton(onlinePane.transform, "HOST GAME", "you get a code", F.BodySemi, () => _ = OnHostOnline());
            Divider(onlinePane.transform, "OR JOIN A FRIEND");
            var codeBlock = VCol(onlinePane.transform, "Code", 8);
            Caption(codeBlock.transform, "JOIN CODE").LE(-1, 14);
            var codeRow = HRow(codeBlock.transform, "Row", 10);
            codeField = Input(codeRow.transform, "K7M2QX", F.MonoBold, 24, 6, 0.3f, true);
            codeField.LE(-1, 56, 1);
            codeField.onSubmit.AddListener(v => _ = OnJoinOnline());
            var joinOnline = Button(codeRow.transform, "JOIN", Btn.Secondary, 20, () => _ = OnJoinOnline());
            joinOnline.LE(104, 56);

            // LAN
            lanPane = VCol(connect.transform, "LanPane", 16).gameObject;
            var hostLan = HostButton(lanPane.transform, "HOST ON LAN", "port " + PORT, F.Mono, () => _ = OnHostLan());
            Divider(lanPane.transform, "OR JOIN ON SAME WI-FI");
            var ipBlock = VCol(lanPane.transform, "Ip", 8);
            Caption(ipBlock.transform, "HOST IP ADDRESS").LE(-1, 14);
            var ipRow = HRow(ipBlock.transform, "Row", 10);
            ipField = Input(ipRow.transform, "192.168.1.24", F.Mono, 20, 40, 0.04f);
            ipField.LE(-1, 56, 1);
            ipField.onSubmit.AddListener(v => _ = OnJoinLan());
            var joinLan = Button(ipRow.transform, "JOIN", Btn.Secondary, 20, () => _ = OnJoinLan());
            joinLan.LE(104, 56);
            Label(ipBlock.transform, "School networks (eduroam) often block LAN. Use Online instead.", F.Body, 13, Muted).Wrap().LE(-1, 18);

            menuHint = Label(connect.transform, "", F.BodyMed, 14, DangerSoft).Wrap();
            menuHint.LE(-1, 18);

            connectButtons.AddRange(new[] { hostOnline, joinOnline, hostLan, joinLan });

            Spacer(col.transform);
            var links = HRow(col.transform, "Links", 28);
            links.LE(-1, 44);
            Button(links.transform, "SETTINGS", Btn.Ghost, 18, () => settings.Open(), F.CondSemi).LE(84, 44);
            Button(links.transform, "CHARACTER", Btn.Ghost, 18, () =>
            {
                if (CharacterCustomizerUI.Instance != null) CharacterCustomizerUI.Instance.Open();
            }, F.CondSemi).LE(96, 44);
            Button(links.transform, "QUIT", Btn.Ghost, 18, OnQuit, F.CondSemi).LE(44, 44);
            foreach (Transform l in links.transform) l.GetComponent<KitButton>().label.rectTransform.Fill();

            // bottom-right info chips
            var info = VCol(stage, "Info", 10, TextAnchor.LowerRight, false);
            info.GetComponent<RectTransform>().At(1f, 0f, -48, 40, 700, 70);
            var chips = HRow(info.transform, "Modes", 8, TextAnchor.MiddleRight);
            foreach (var m in new[] { "CO-OP SURVIVAL", "FREE-FOR-ALL", "TEAM DEATHMATCH", "GUN GAME" })
                Badge(chips.transform, m, Scrim(0.78f), Fg, Color.clear, 11);
            var meta = Badge(info.transform, $"Up to {MAX_PLAYERS} players · 10 weapons · v{Application.version}", Scrim(0.78f), Fg, Color.clear, 13);
            var metaText = meta.GetComponentInChildren<TextMeshProUGUI>();
            ApplyFont(metaText, F.Body); metaText.characterSpacing = 0;

            SetLanTab(LastWasLan);
            return screen.gameObject;
        }

        void BuildTown(RectTransform t)
        {
            Color mount = Hex("#39413F");
            UIPoly.Create(t, "Mountains", mount, new Vector2(0, 420), new Vector2(120, 300), new Vector2(230, 380),
                new Vector2(360, 250), new Vector2(470, 350), new Vector2(600, 230), new Vector2(760, 330),
                new Vector2(1600, 330), new Vector2(1600, 1100), new Vector2(0, 1100)).Fill();
            Block(t, 80, 330, 120, 170, Hex("#4A4F3E"));
            UIPoly.Create(t, "Roof", Hex("#5A5F48"), new Vector2(70, 330), new Vector2(140, 280), new Vector2(210, 330)).Fill();
            Block(t, 420, 300, 170, 200, Hex("#3F4535"));
            Block(t, 440, 330, 34, 34, Scenery);
            Block(t, 500, 330, 34, 34, Scenery);
            Block(t, 250, 420, 150, 80, Hex("#6B5A3A"));
            Block(t, 250, 420, 150, 10, Hex("#7E6B46"));
            Block(t, 0, 500, 1600, 600, Hex("#262A21"));
            UIPoly.Create(t, "Road", Hex("#30352A"), new Vector2(300, 500), new Vector2(460, 500), new Vector2(600, 808), new Vector2(160, 808)).Fill();
            Block(t, 520, 440, 90, 60, Hex("#55606A"));
            Block(t, 610, 470, 60, 30, Hex("#55606A"));
        }

        static void Block(RectTransform parent, float x, float y, float w, float h, Color c)
            => Box(parent, "Block", c).rectTransform.TL(x, y, w, h);

        KitButton HostButton(Transform parent, string text, string sub, F subFont, UnityEngine.Events.UnityAction click)
        {
            var b = Button(parent, text, Btn.Primary, 24, click, F.CondBlack, 14);
            b.LE(-1, 60);
            b.label.alignment = TextAlignmentOptions.MidlineLeft;
            b.label.rectTransform.Fill(24, 0, 24, 0);
            var s = Label(b.transform, sub, subFont, 13, Ground, TextAlignmentOptions.MidlineRight, 0.02f);
            s.rectTransform.Fill(24, 0, 24, 0);
            return b;
        }

        static void Divider(Transform parent, string text)
        {
            var row = HRow(parent, "Divider", 12, TextAnchor.MiddleCenter);
            row.LE(-1, 16);
            Box(row.transform, "L", Line).LE(-1, 1, 1);
            Label(row.transform, text, F.BodySemi, 12, Faint, TextAlignmentOptions.Center, 0.14f).LE(-1, 16, 0);
            Box(row.transform, "R", Line).LE(-1, 1, 1);
        }

        void SetLanTab(bool lan)
        {
            lanTab = lan;
            onlinePane.SetActive(!lan);
            lanPane.SetActive(lan);
            MenuHint("");
        }

        void MenuHint(string msg, bool error = true)
        {
            if (menuHint == null) return;
            menuHint.text = msg;
            menuHint.color = error ? DangerSoft : Muted;
        }

        // ───────── connecting / error ─────────
        GameObject BuildConnecting(Transform root)
        {
            var screen = Rect(root, "ConnectingScreen").Fill();
            Box(screen, "Ground", Ground, true).rectTransform.Fill();

            var card = Box(screen, "Card", Surface, true);
            card.rectTransform.At(0.5f, 0.5f, 0, 0, 560, 400);
            cardFrame = Frame(card.transform, Line, 1f);
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cardCol = card.gameObject.AddComponent<VerticalLayoutGroup>();
            cardCol.padding = new RectOffset(40, 40, 40, 40);
            cardCol.spacing = 28;
            cardCol.childControlWidth = cardCol.childControlHeight = true;
            cardCol.childForceExpandWidth = true; cardCol.childForceExpandHeight = false;
            cardFrame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            connCaption = Label(card.transform, "", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.16f);
            connCaption.LE(-1, 14);

            // progress
            progressPane = VCol(card.transform, "Progress", 28).gameObject;
            var head = HRow(progressPane.transform, "Head", 20);
            head.LE(-1, 56);
            var spinner = Rect(head.transform, "Spinner").LE(56, 56);
            var ring = Box(spinner, "Ring", Line); ring.sprite = RingSprite; ring.rectTransform.Fill();
            var arc = Box(spinner, "Arc", Accent); arc.sprite = RingSprite; arc.rectTransform.Fill();
            arc.type = Image.Type.Filled; arc.fillMethod = Image.FillMethod.Radial360; arc.fillAmount = 0.25f;
            arc.gameObject.AddComponent<UISpin>();
            connTitle = Label(head.transform, "", F.CondBold, 36, Fg);
            connTitle.LE(-1, 56, 1);
            var list = VCol(progressPane.transform, "Steps", 14);
            for (int i = 0; i < 4; i++)
            {
                var row = HRow(list.transform, "Step", 12);
                row.LE(-1, 22);
                var icon = Rect(row.transform, "Icon").LE(20, 20);
                var done = CheckMark(icon, Ready, 1.1f);
                var cur = Box(icon, "Current", Accent).rectTransform.At(0.5f, 0.5f, 0, 0, 10, 10);
                var pend = Rect(icon, "Pending").At(0.5f, 0.5f, 0, 0, 10, 10);
                Frame(pend, LineStrong, 1.5f);
                var txt = Label(row.transform, "", F.Body, 16, Fg);
                txt.LE(-1, 22, 1);
                stepRows.Add((done, cur, pend, txt));
            }
            progressFill = Bar(progressPane.transform, "Bar", Line, Accent);
            progressFill.transform.parent.GetComponent<Image>().LE(-1, 4);
            var cancelRow = HRow(progressPane.transform, "CancelRow", 0);
            Button(cancelRow.transform, "CANCEL", Btn.Quiet, 17, CancelConnecting).LE(120, 44);

            // error
            errorPane = VCol(card.transform, "Error", 28).gameObject;
            var eh = HRow(errorPane.transform, "Head", 20);
            eh.LE(-1, 56);
            var icon2 = Rect(eh.transform, "Icon").LE(56, 56);
            var er = Box(icon2, "Ring", Danger); er.sprite = RingSprite; er.rectTransform.Fill(2, 2, 2, 2);
            Label(icon2, "!", F.CondBlack, 34, Danger, TextAlignmentOptions.Center).rectTransform.Fill();
            errTitle = Label(eh.transform, "", F.CondBold, 36, Fg).Wrap(-20);
            errTitle.LE(-1, 56, 1);
            errBody = Label(errorPane.transform, "", F.Body, 16, TextSoft).Wrap(10);
            var tipBox = Card(errorPane.transform, "Tips", Ground, Line, 1f);
            var tipCol = tipBox.gameObject.AddComponent<VerticalLayoutGroup>();
            tipCol.padding = new RectOffset(16, 16, 14, 14); tipCol.spacing = 6;
            tipCol.childControlWidth = tipCol.childControlHeight = true; tipCol.childForceExpandHeight = false;
            tipBox.transform.Find("Frame").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Label(tipBox.transform, "TRY THIS", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f).LE(-1, 14);
            errTips = Label(tipBox.transform, "", F.Body, 14, Fg).Wrap(12);
            var eb = HRow(errorPane.transform, "Buttons", 12);
            eb.LE(-1, 48);
            Button(eb.transform, "TRY AGAIN", Btn.Primary, 18, () => { if (retryAction != null) _ = retryAction(); }, F.CondBold, 12).LE(150, 48);
            Button(eb.transform, "BACK TO MENU", Btn.Quiet, 18, () => { ShowScreen(menuScreen); MenuHint(""); }).LE(170, 48);
            errDetails = Label(errorPane.transform, "", F.Mono, 12, Faint).Wrap();

            return screen.gameObject;
        }

        void ShowProgress(string caption, string title, params string[] stepNames)
        {
            steps = stepNames;
            connCaption.text = caption;
            connCaption.color = Muted;
            connTitle.text = title.ToUpperInvariant();
            cardFrame.color = Line;
            progressPane.SetActive(true);
            errorPane.SetActive(false);
            for (int i = 0; i < stepRows.Count; i++)
            {
                stepRows[i].text.transform.parent.gameObject.SetActive(i < steps.Length);
                if (i < steps.Length) stepRows[i].text.text = steps[i];
            }
            SetStep(0);
            ShowScreen(connectScreen);
        }

        void SetStep(int current)
        {
            if (stepRows.Count == 0) return;
            for (int i = 0; i < steps.Length && i < stepRows.Count; i++)
            {
                var r = stepRows[i];
                r.done.gameObject.SetActive(i < current);
                r.current.gameObject.SetActive(i == current);
                r.pending.gameObject.SetActive(i > current);
                r.text.color = i > current ? Faint : Fg;
                ApplyFont(r.text, i == current ? F.BodySemi : F.Body);
            }
            SetBar(progressFill, steps.Length > 0 ? (current + 0.6f) / steps.Length : 0f);
        }

        void ShowError(string caption, string title, string body, string tips, string details)
        {
            connCaption.text = caption;
            connCaption.color = DangerSoft;
            cardFrame.color = DangerLine;
            errTitle.text = title.ToUpperInvariant();
            errBody.text = body;
            errTips.text = tips;
            errDetails.text = string.IsNullOrEmpty(details) ? "" : "Details: " + details;
            progressPane.SetActive(false);
            errorPane.SetActive(true);
            ShowScreen(connectScreen);
        }

        // ───────── lobby ─────────
        GameObject BuildLobby(Transform root)
        {
            var screen = Rect(root, "LobbyScreen").Fill();
            Box(screen, "Ground", Ground, true).rectTransform.Fill();

            var stage = Rect(screen, "Stage").At(0.5f, 0.5f, 0, 0, 1280, 720);
            // header rule and bottom bar run past the 1280 stage to the screen edges
            Box(stage, "TopLine", Line).rectTransform.At(0.5f, 1f, 0, -84, 4000, 1);
            var bottom = Box(stage, "BottomBar", Surface);
            bottom.rectTransform.At(0.5f, 0f, 0, -400, 4000, 488);
            Box(bottom.transform, "Line", Line).rectTransform.At(0.5f, 1f, 0, 0, 4000, 1);

            // header
            var logo = HRow(stage, "Logo", 10);
            logo.GetComponent<RectTransform>().TL(40, 0, 400, 84);
            Box(logo.transform, "Bar", Accent).LE(9, 26);
            Label(logo.transform, "AIRSOFT", F.CondBlack, 34, Fg, TextAlignmentOptions.MidlineLeft, 0.04f).LE(-1, 34);
            Spacer(logo.transform, 4, 10, 0);
            Label(logo.transform, "LOBBY", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.16f).LE(-1, 14);

            var codeRow = HRow(stage, "Code", 14, TextAnchor.MiddleRight);
            codeRow.GetComponent<RectTransform>().At(1f, 1f, -40, 0, 600, 84);
            var codeCol = VCol(codeRow.transform, "Text", 2, TextAnchor.MiddleRight, false);
            lobbyCodeCaption = Label(codeCol.transform, "", F.BodySemi, 11, Muted, TextAlignmentOptions.MidlineRight, 0.14f);
            lobbyCodeCaption.LE(-1, 14);
            lobbyCode = Label(codeCol.transform, "", F.MonoBold, 26, Accent, TextAlignmentOptions.MidlineRight, 0.2f);
            lobbyCode.LE(-1, 28);
            var copy = Button(codeRow.transform, "", Btn.Quiet, 10, CopyCode);
            copy.LE(44, 44);
            var front = Rect(copy.transform, "IconFront").At(0.5f, 0.5f, 2.5f, -2.5f, 12, 12);
            Frame(front, Fg, 2f);
            var back = Rect(copy.transform, "IconBack").At(0.5f, 0.5f, -2.5f, 2.5f, 12, 12);
            Frame(back, Fg, 2f);

            // columns
            BuildModeColumn(Rect(stage, "Modes").TL(40, 112, 300, 492));
            BuildPlayerColumn(Rect(stage, "Players").TL(372, 112, 556, 492));
            BuildCharacterColumn(Rect(stage, "Character").TL(960, 112, 280, 492));

            // bottom bar contents
            var bar = HRow(stage, "Bar", 16);
            var barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = barRt.anchorMax = barRt.pivot = new Vector2(0.5f, 0f);
            barRt.anchoredPosition = Vector2.zero;
            barRt.sizeDelta = new Vector2(1200, 88);
            Button(bar.transform, "LEAVE", Btn.Danger, 18, OnLeave).LE(100, 48);
            hintText = Label(bar.transform, "", F.Body, 15, Muted, TextAlignmentOptions.MidlineRight);
            hintText.LE(-1, 48, 1);
            readyButton = Button(bar.transform, "READY UP", Btn.Secondary, 21, () => MatchNet.Instance?.ToggleReady(), F.CondBlack);
            readyButton.LE(176, 56);
            readyButton.label.rectTransform.Fill(18, 0, 50, 0);
            readyKey = KeyCap(readyButton.transform, "R", 12);
            readyKey.anchorMin = readyKey.anchorMax = readyKey.pivot = new Vector2(1f, 0.5f);
            readyKey.anchoredPosition = new Vector2(-20, 0);
            readyLabel = readyButton.label;
            startMatchButton = Button(bar.transform, "START MATCH", Btn.Primary, 22, OnStartMatch, F.CondBlack, 14);
            startMatchButton.LE(196, 56);

            return screen.gameObject;
        }

        void BuildModeColumn(RectTransform colRt)
        {
            var col = colRt.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = 12; col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true; col.childForceExpandHeight = false;

            var head = Rect(colRt, "Head").LE(-1, 18);
            Caption(head, "GAME MODE", 12, Muted, 0.16f).rectTransform.Fill();
            var key = KeyCap(head, "M", 11);
            key.anchorMin = key.anchorMax = key.pivot = new Vector2(1f, 0.5f);
            key.anchoredPosition = Vector2.zero;
            hostModeKey = key.gameObject;

            for (int i = 0; i < 4; i++)
            {
                var mode = (MatchNet.GameModeType)i;
                var b = Button(colRt, "", Btn.Ghost, 10, () =>
                {
                    if (isHost && MatchNet.Instance != null) MatchNet.Instance.HostSetMode(mode);
                });
                b.LE(-1, 86);
                var group = b.gameObject.AddComponent<CanvasGroup>();
                var frame = Frame(b.transform, Line, 1f);
                var txt = VCol(b.transform, "Text", 4);
                txt.padding = new RectOffset(16, 16, 13, 13);
                txt.GetComponent<RectTransform>().Fill();
                Label(txt.transform, ModeName(mode).ToUpperInvariant(), F.CondBold, 21, Fg, TextAlignmentOptions.MidlineLeft, 0.02f).LE(-1, 24);
                Label(txt.transform, ModeDesc(mode), F.Body, 13, Muted).Wrap().LE(-1, 36);
                b.label.gameObject.SetActive(false);
                modeCards.Add((b, frame, group));
            }

            var note = HRow(colRt, "ClientNote", 8);
            note.LE(-1, 18);
            var lockIcon = Rect(note.transform, "Lock").LE(14, 14);
            Box(lockIcon, "Body", Muted).rectTransform.At(0.5f, 0f, 0, 0, 12, 8);
            var shackle = Rect(lockIcon, "Shackle").At(0.5f, 1f, 0, 0, 8, 8);
            Frame(shackle, Muted, 2f);
            Label(note.transform, "The host picks the mode", F.Body, 13, Muted).LE(-1, 18);
            clientModeNote = note.gameObject;
        }

        void BuildPlayerColumn(RectTransform colRt)
        {
            var head = Rect(colRt, "Head").TL(0, 0, 556, 22);
            Caption(head, "PLAYERS", 12, Muted, 0.16f).rectTransform.Fill();
            playerCount = Label(head, "", F.CondBold, 18, Fg, TextAlignmentOptions.MidlineRight, 0.06f);
            playerCount.rectTransform.Fill();

            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                float x = (i % 2) * 282, y = 34 + (i / 2) * 62;
                var s = new Slot();
                s.bg = Box(colRt, "Slot" + i, Raised);
                s.bg.rectTransform.TL(x, y, 274, 54);
                s.frame = Frame(s.bg.transform, Line, 1f);
                var row = HRow(s.bg.transform, "Row", 10);
                row.padding = new RectOffset(12, 12, 0, 0);
                row.GetComponent<RectTransform>().Fill();
                s.swatch = Box(row.transform, "Swatch", Fg);
                s.swatch.LE(28, 28);
                s.swatchFrame = Frame(s.swatch.transform, LineStrong, 1f);
                s.name = Label(row.transform, "", F.BodySemi, 15, Fg);
                s.name.overflowMode = TextOverflowModes.Ellipsis;
                s.name.LE(-1, 54, 1);
                var badges = HRow(row.transform, "Badges", 4);
                s.host = Badge(badges.transform, "HOST", Accent, Ground, Color.clear).gameObject;
                s.you = Badge(badges.transform, "YOU", Color.clear, Fg, Fg).gameObject;
                s.ready = Badge(badges.transform, "READY", Ready, Ground, Color.clear).gameObject;
                s.notReady = Badge(badges.transform, "NOT READY", Color.clear, Muted, LineStrong).gameObject;
                slots.Add(s);
            }

            var note = Label(colRt, "Teams are split automatically when the match starts. Friendly fire is off.", F.Body, 13, Muted).Wrap();
            note.rectTransform.TL(0, 34 + 4 * 62 + 4, 556, 36);
            tdmNote = note.gameObject;
        }

        void BuildCharacterColumn(RectTransform colRt)
        {
            var col = colRt.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = 14; col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true; col.childForceExpandHeight = false;

            Caption(colRt, "YOUR CHARACTER", 12, Muted, 0.16f).LE(-1, 16);
            var prev = Box(colRt, "Preview", Scenery, true);
            prev.LE(-1, -1, -1, 1);
            previewBox = prev.rectTransform;
            AttachPreview();
            var chip = Badge(prev.transform, "3D preview · drag to rotate", Scrim(0.78f), Fg, Color.clear, 11);
            var chipText = chip.GetComponentInChildren<TextMeshProUGUI>();
            ApplyFont(chipText, F.Body); chipText.characterSpacing = 0;
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 1f);
            chip.anchoredPosition = new Vector2(12, -12);

            var nameBlock = VCol(colRt, "Callsign", 6);
            Caption(nameBlock.transform, "CALLSIGN").LE(-1, 14);
            lobbyName = Input(nameBlock.transform, "Your name", F.Body, 15, 16);
            lobbyName.LE(-1, 42);
            lobbyName.onEndEdit.AddListener(v =>
            {
                if (MatchNet.Instance != null) MatchNet.Instance.SetLocalName(v);
                if (nameInput != null) nameInput.SetTextWithoutNotify(v);
            });

            Button(colRt, "CUSTOMIZE", Btn.Secondary, 17, () =>
            {
                if (CharacterCustomizerUI.Instance != null) CharacterCustomizerUI.Instance.Open();
            }).LE(-1, 44);
        }

        RectTransform previewBox;
        bool previewAttached;

        void AttachPreview()
        {
            if (previewAttached || previewBox == null || CharacterCustomizerUI.Instance == null) return;
            previewAttached = true;
            var img = CharacterCustomizerUI.Instance.PreviewImage(previewBox);
            img.Fill();
            img.SetAsFirstSibling();
        }

        void CopyCode()
        {
            string v = !string.IsNullOrEmpty(JoinCode) ? JoinCode : LanAddress;
            if (string.IsNullOrEmpty(v)) return;
            GUIUtility.systemCopyBuffer = v;
            copyFlashUntil = Time.unscaledTime + 1f;
            RefreshLobby();
        }

        void RefreshLobby()
        {
            var mnet = MatchNet.Instance;
            var net = NetworkManager.Singleton;
            if (mnet == null || net == null) return;
            AttachPreview();

            // header
            bool online = !string.IsNullOrEmpty(JoinCode);
            if (Time.unscaledTime < copyFlashUntil)
            {
                lobbyCodeCaption.text = "COPIED TO CLIPBOARD";
                lobbyCodeCaption.color = Ready;
            }
            else
            {
                lobbyCodeCaption.text = online ? "ONLINE · JOIN CODE" : isHost ? "LAN · HOST IP" : "LAN · CONNECTED TO";
                lobbyCodeCaption.color = Muted;
            }
            lobbyCode.text = online ? JoinCode : LanAddress;
            lobbyCode.fontSize = online ? 26 : 22;
            lobbyCode.characterSpacing = online ? 20 : 4;

            // modes
            for (int i = 0; i < modeCards.Count; i++)
            {
                var (btn, frame, group) = modeCards[i];
                bool sel = (int)mnet.Mode == i;
                btn.bgN = btn.bgP = sel ? Raised : Surface;
                btn.bgH = isHost ? Raised : btn.bgN;
                btn.bgD = btn.bgN;
                btn.interactable = isHost;
                btn.Refresh();
                frame.color = sel ? Accent : Line;
                frame.pixelsPerUnitMultiplier = 4f / (sel ? 2f : 1f);
                group.alpha = (!isHost && !sel) ? 0.55f : 1f;
            }
            bool tdm = mnet.Mode == MatchNet.GameModeType.TeamDeathmatch;
            tdmNote.SetActive(tdm);

            // roster
            var roster = mnet.Roster;
            playerCount.text = roster.Count + " / " + MAX_PLAYERS;
            if (lobbyName.placeholder is TMP_Text ph) ph.text = mnet.NameFor(net.LocalClientId);
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (i >= roster.Count)
                {
                    s.bg.color = Color.clear;
                    s.frame.color = A(Line, 0.8f);
                    s.frame.pixelsPerUnitMultiplier = 4f;
                    s.swatch.color = Color.clear;
                    s.swatchFrame.color = LineStrong;
                    s.name.text = "Waiting for player…";
                    s.name.color = Faint;
                    ApplyFont(s.name, F.Body);
                    s.host.SetActive(false); s.you.SetActive(false); s.ready.SetActive(false); s.notReady.SetActive(false);
                    continue;
                }
                var e = roster[i];
                bool you = e.clientId == net.LocalClientId;
                s.bg.color = you ? Surface : Raised;
                s.frame.color = you ? Accent : Line;
                s.frame.pixelsPerUnitMultiplier = 4f / (you ? 1.5f : 1f);
                int look = e.look >= 0 ? e.look : (you ? mnet.LocalLook : -1);
                Color shirt = look >= 0 ? AirsoftLook.Palette[AirsoftLook.Unpack(look).shirt] : Faint;
                s.swatch.color = tdm ? (i % 2 == 0 ? Red : Blue) : shirt;
                s.swatchFrame.color = Color.clear;
                s.name.text = e.name;
                s.name.color = Fg;
                ApplyFont(s.name, F.BodySemi);
                s.host.SetActive(e.clientId == NetworkManager.ServerClientId);
                s.you.SetActive(you);
                s.ready.SetActive(e.ready);
                s.notReady.SetActive(!e.ready);
            }

            // ready / start
            bool ready = mnet.LocalReady;
            readyButton.SetStyle(Btn.Secondary);
            if (ready)
            {
                readyButton.bgN = readyButton.bgH = readyButton.bgP = Ready;
                readyButton.fgN = readyButton.fgH = Ground;
                readyButton.lineN = readyButton.lineH = Ready;
                readyButton.Refresh();
            }
            readyLabel.text = ready ? "READY" : "READY UP";
            foreach (var t in readyKey.GetComponentsInChildren<TextMeshProUGUI>()) t.color = ready ? Ground : Fg;
            foreach (var img in readyKey.GetComponentsInChildren<Image>()) img.color = ready ? Ground : Fg;

            int waiting = 0;
            foreach (var e in roster) if (!e.ready) waiting++;
            bool allReady = mnet.AllReady();
            if (isHost)
            {
                startMatchButton.interactable = allReady;
                hintText.text = allReady ? "Everyone is ready. Start the match."
                    : "Waiting for " + waiting + (waiting == 1 ? " player" : " players") + " to ready up…";
                hintText.color = allReady ? Ready : Muted;
            }
            else
            {
                hintText.text = ready ? "Waiting for the host to start…" : "Press R when you’re ready.";
                hintText.color = Muted;
            }
        }
    }
}
