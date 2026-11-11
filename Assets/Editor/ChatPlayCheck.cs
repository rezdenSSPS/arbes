#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Play-mode check of the lobby chat (MatchNet.Chat.cs + NetworkUI.Chat.cs) through the real lobby:
    // host on a random port -> chat lines arrive with the server's roster name, the server cleans them
    // (rich text, length, empty) and rate-limits; history cap + unread counter; the chat panel opens /
    // closes and the lobby hotkeys (R, M) are off while it is open; the history survives
    // lobby -> match -> lobby and is wiped when the session ends.
    //
    // Batch (graphics on, no -quit: it exits itself):
    //   Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.ChatPlayCheck.RunBatch -logFile <log>
    public static class ChatPlayCheck
    {
        static int stage;
        static double stageStart, runStart;
        static int fails, passes;
        static bool batch;
        static int savedBotFill;
        static int countBefore;
        static NetworkUI ui;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        const BindingFlags ANY = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void RunBatch() { batch = true; Run(); }

        [MenuItem("Tools/Airsoft/Play Check: Lobby Chat")]
        static void Run()
        {
            stage = 0; fails = passes = 0; ui = null;
            savedBotFill = PlayerPrefs.GetInt("Airsoft.Bots", 0);
            PlayerPrefs.SetInt("Airsoft.Bots", 0);   // host-only check: no bots
            PlayerPrefs.Save();
            log.Clear();
            log.AppendLine("[Airsoft] Play check: lobby chat");
            EditorSceneManager.OpenScene("Assets/Scenes/AirsoftLobby.unity", OpenSceneMode.Single);
            runStart = stageStart = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Check(bool ok, string what)
        {
            if (ok) passes++; else fails++;
            log.AppendLine((ok ? "  PASS " : "  FAIL ") + what);
        }

        static void Next() { stage++; stageStart = EditorApplication.timeSinceStartup; }
        static double InStage => EditorApplication.timeSinceStartup - stageStart;

        static void Finish(string why = null)
        {
            if (why != null) { fails++; log.AppendLine("  FAIL " + why); }
            log.AppendLine($"  => {passes} passed, {fails} failed");
            if (fails == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
            EditorApplication.update -= Tick;
            PlayerPrefs.SetInt("Airsoft.Bots", savedBotFill);
            PlayerPrefs.Save();
            if (batch) EditorApplication.Exit(fails == 0 ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, ANY).GetValue(o);

        static MatchNet.ChatLine Last => MatchNet.ChatHistory[MatchNet.ChatHistory.Count - 1];

        static void FakeLine(bool system, ulong sender, string name, string text)
            => typeof(MatchNet).GetMethod("AddChatLine", ANY).Invoke(null, new object[]
               { new MatchNet.ChatLine { system = system, senderId = sender, name = name, text = text } });

        static bool ChatFocused()
        {
            var f = Field<TMP_InputField>(ui, "chatInput");
            return f != null && f.isFocused;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - runStart > 300) { Finish("timed out in stage " + stage); return; }
            if (!EditorApplication.isPlaying) return;
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            try
            {
                switch (stage)
                {
                    case 0:   // start a host on a free port of our own
                        if (nm == null || InStage < 1) return;
                        var utp = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                        if (utp != null) utp.SetConnectionData("127.0.0.1", (ushort)UnityEngine.Random.Range(20000, 40000));
                        Check(nm.StartHost(), "host started");
                        Next(); break;

                    case 1:   // into the lobby screen, first line (rich text gets stripped)
                        ui = UnityEngine.Object.FindAnyObjectByType<NetworkUI>();
                        if (mn == null || mn.Roster.Count == 0 || ui == null || InStage < 1) { if (InStage > 20) Finish("host / lobby never came up"); return; }
                        typeof(NetworkUI).GetMethod("EnterWaitingRoom", ANY).Invoke(ui, new object[] { true });
                        Check(MatchNet.ChatHistory.Count == 0, "chat starts empty (no line for the host opening the room)");
                        Check(!ui.ChatOpen, "chat panel starts closed");
                        Check(MatchNet.SanitizeChat("  a<b>c\n d  ") == "abc  d", "sanitiser: tags stripped, control chars -> space, trimmed");
                        Check(MatchNet.SanitizeChat(" <> \t ") == "", "sanitiser: nothing left -> dropped");
                        mn.SendChat("  hello <b>team</b>  ");
                        Next(); break;

                    case 2:
                        if (InStage < 0.3) return;
                        Check(MatchNet.ChatHistory.Count == 1, "host's line arrived in the history (" + MatchNet.ChatHistory.Count + ")");
                        if (MatchNet.ChatHistory.Count == 0) { Finish("no chat line"); return; }
                        Check(!Last.system && Last.senderId == nm.LocalClientId, "line is a player line from the host");
                        Check(Last.name == mn.NameFor(nm.LocalClientId) && !string.IsNullOrEmpty(Last.name), "sender name is the server roster name (" + Last.name + ")");
                        Check(Last.text == "hello bteam/b", "text cleaned on the server (" + Last.text + ")");
                        Check(MatchNet.ChatUnread == 0, "your own line doesn't count as unread");
                        mn.SendChat(new string('x', 100) + " " + new string('y', 200));
                        mn.SendChat("     ");
                        mn.SendChat("<<>>");
                        Next(); break;

                    case 3:
                        if (InStage < 0.3) return;
                        Check(MatchNet.ChatHistory.Count == 2, "long line kept, empty ones dropped (" + MatchNet.ChatHistory.Count + " lines)");
                        Check(Last.text.Length == MatchNet.CHAT_MAX_LEN, "long line cut to " + MatchNet.CHAT_MAX_LEN + " (" + Last.text.Length + ")");
                        // 2 lines in the window already: one more fits, then the rate limit kicks in
                        mn.SendChat("three");
                        mn.SendChat("four");
                        mn.SendChat("five");
                        Next(); break;

                    case 4:
                        if (InStage < 0.3) return;
                        int players = 0, notices = 0;
                        foreach (var l in MatchNet.ChatHistory) { if (l.system) notices++; else players++; }
                        Check(players == 3, "rate limit: 3 lines per " + MatchNet.CHAT_RATE_WINDOW + " s (" + players + " got through)");
                        Check(notices == 1 && Last.system && Last.text.Contains("too fast"), "rate limit: one 'too fast' notice to the sender");
                        Next(); break;

                    case 5:   // the window has passed: chatting works again
                        if (InStage < MatchNet.CHAT_RATE_WINDOW + 0.3) return;
                        countBefore = MatchNet.ChatHistory.Count;
                        mn.SendChat("after the wait");
                        Next(); break;

                    case 6:
                        if (InStage < 0.3) return;
                        Check(MatchNet.ChatHistory.Count == countBefore + 1 && Last.text == "after the wait", "rate limit resets after the window");
                        // join / rename / leave notices (server side; a real second client isn't part of this check)
                        var onName = typeof(MatchNet).GetMethod("ChatOnName", ANY);
                        onName.Invoke(mn, new object[] { 5UL, "Player 5", "Viper" });
                        Check(Last.system && Last.text == "Viper joined.", "system line when a player joins (" + Last.text + ")");
                        onName.Invoke(mn, new object[] { 5UL, "Viper", "Cobra" });
                        Check(Last.system && Last.text == "Viper is now Cobra.", "system line on a rename (" + Last.text + ")");
                        typeof(MatchNet).GetMethod("ChatOnLeft", ANY).Invoke(mn, new object[] { 5UL });
                        Check(Last.system && Last.text.EndsWith(" left."), "system line when a player leaves (" + Last.text + ")");
                        // lines from someone else: unread counter, history cap
                        FakeLine(false, 77, "Viper", "incoming");
                        FakeLine(true, ulong.MaxValue, "", "Viper joined.");
                        Check(MatchNet.ChatUnread == 1, "unread: other players' lines count, system lines don't (" + MatchNet.ChatUnread + ")");
                        for (int i = 0; i < 70; i++) FakeLine(false, 77, "Viper", "spam " + i);
                        Check(MatchNet.ChatHistory.Count == MatchNet.CHAT_HISTORY, "history capped at " + MatchNet.CHAT_HISTORY + " (" + MatchNet.ChatHistory.Count + ")");
                        Check(Last.text == "spam 69" && MatchNet.ChatHistory[0].text == "spam 10", "oldest lines dropped, newest last");
                        Next(); break;

                    case 7:   // badge on the closed button
                        if (InStage < 0.3) return;
                        var badge = Field<RectTransform>(ui, "chatBadge");
                        Check(badge != null && badge.gameObject.activeInHierarchy, "unread badge shows on the chat button");
                        Check(Field<TextMeshProUGUI>(ui, "chatBadgeText").text == "9+", "badge caps at 9+");
                        Field<KitButton>(ui, "chatButton").onClick.Invoke();   // the chat icon
                        Next(); break;

                    case 8:
                        if (InStage < 0.5) return;
                        Check(ui.ChatOpen, "chat icon opens the panel");
                        Check(MatchNet.ChatUnread == 0 && !Field<RectTransform>(ui, "chatBadge").gameObject.activeSelf, "opening clears the unread badge");
                        Check(ChatFocused(), "text field has the focus when the chat opens");
                        Check(ui.LobbyHotkeysBlocked, "lobby hotkeys (R ready, M mode) are off while the chat is open");
                        ui.CloseChat();   // what Esc does
                        Next(); break;

                    case 9:
                        if (InStage < 0.3) return;
                        Check(!ui.ChatOpen && !ChatFocused(), "closing hides the panel and drops the focus");
                        Check(!ui.LobbyHotkeysBlocked, "lobby hotkeys back once the chat is closed");
                        ui.ToggleChat();
                        Check(ui.ChatOpen, "chat icon opens it again");
                        ui.ToggleChat();
                        Check(!ui.ChatOpen, "chat icon closes the panel");
                        // lobby -> match -> lobby
                        countBefore = MatchNet.ChatHistory.Count;
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;

                    case 10:
                        if (!mn.AllReady()) { if (InStage > 10) Finish("never ready"); return; }
                        mn.HostStartMatch();
                        Next(); break;

                    case 11:
                        if (SceneManager.GetActiveScene().name != MatchNet.CurrentMapScene || InStage < 3) { if (InStage > 60) Finish("arena never loaded"); return; }
                        Check(MatchNet.ChatHistory.Count == countBefore, "history kept in the match");
                        FakeLine(false, 77, "Viper", "gg");   // arrives while nobody looks at the chat
                        typeof(MatchNet).GetMethod("ReturnToLobby", ANY).Invoke(mn, null);
                        Next(); break;

                    case 12:
                        ui = UnityEngine.Object.FindAnyObjectByType<NetworkUI>();
                        if (SceneManager.GetActiveScene().name != "AirsoftLobby" || ui == null || InStage < 2) { if (InStage > 60) Finish("lobby never came back"); return; }
                        Check(MatchNet.ChatHistory.Count == countBefore && Last.text == "gg", "history survives lobby -> match -> lobby");
                        Check(!ui.ChatOpen && Field<RectTransform>(ui, "chatBadge").gameObject.activeInHierarchy
                              && MatchNet.ChatUnread == 1, "back in the lobby: chat closed, 1 unread on the badge");
                        nm.Shutdown();
                        Next(); break;

                    case 13:
                        if (nm.IsListening || nm.ShutdownInProgress || InStage < 1) { if (InStage > 15) Finish("shutdown hung"); return; }
                        Check(MatchNet.ChatHistory.Count == 0 && MatchNet.ChatUnread == 0, "session ended: chat history cleared");
                        Finish(); break;
                }
            }
            catch (Exception e) { Finish("exception: " + e); }
        }
    }
}
#endif
