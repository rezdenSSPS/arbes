using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Lobby chat (networking: MatchNet.Chat.cs). A speech-bubble button next to LEAVE in the bottom bar,
    // with an unread counter; it opens a panel above the bar: the history (scrolls, newest at the bottom)
    // and a text field. Enter sends (Enter also opens the chat), Esc or the button closes it.
    // While the panel is open the lobby hotkeys (R ready, M mode) are off, so typing never toggles anything.
    public partial class NetworkUI
    {
        const float CHAT_W = 460f, CHAT_H = 380f;

        KitButton chatButton;
        RectTransform chatBadge;
        TextMeshProUGUI chatBadgeText;
        GameObject chatPanel;
        ScrollRect chatScroll;
        RectTransform chatContent;
        TMP_InputField chatInput;
        GameObject chatEmpty;
        readonly List<TextMeshProUGUI> chatLabels = new List<TextMeshProUGUI>();
        int chatShownVersion = -1, chatShownMode = -1, chatShownRoster = -1;
        int chatSnapFrames;
        bool chatRefocus, chatWasTyping, chatOverlayBefore;

        public bool ChatOpen => chatPanel != null && chatPanel.activeSelf;
        // R (ready) / M (mode) must not fire while typing anywhere (chat, callsign) or while the chat is open.
        public bool LobbyHotkeysBlocked => ChatOpen || TypingInField();

        // Bottom bar, right after LEAVE.
        void BuildChatButton(Transform bar)
        {
            chatButton = Button(bar, "", Btn.Quiet, 10, ToggleChat);
            chatButton.name = "Btn_Chat";
            chatButton.LE(48, 48);
            var icon = Rect(chatButton.transform, "ChatIcon").At(0.5f, 0.5f, 0, 1, 24, 22);
            ChatBubbleIcon(icon, Fg);

            chatBadge = Badge(chatButton.transform, "0", Accent, Ground, Color.clear, 10);
            chatBadge.anchorMin = chatBadge.anchorMax = Vector2.one;
            chatBadge.pivot = new Vector2(0.5f, 0.5f);
            chatBadge.anchoredPosition = new Vector2(-3, -3);
            chatBadgeText = chatBadge.GetComponentInChildren<TextMeshProUGUI>();
            chatBadgeText.characterSpacing = 0;
            chatBadge.gameObject.SetActive(false);
        }

        // A speech bubble outline (tail bottom-left) with three dots, 24 × 22.
        static void ChatBubbleIcon(RectTransform icon, Color c)
        {
            const float S = 2f;   // drawn at 2× and scaled down: crisp at 1080p too
            Vector2 P(float x, float y) => new Vector2(x * S, y * S);
            var shape = UIPoly.Create(icon, "Bubble", c,
                // outline, then into the hole along a bridge and round it the other way (even-odd fill leaves it empty)
                P(3, 1), P(21, 1), P(23, 3), P(23, 14), P(21, 16), P(11, 16), P(6, 21), P(6, 16), P(3, 16), P(1, 14), P(1, 3),
                P(3, 1),
                P(4, 3), P(3, 4), P(3, 13), P(4, 14), P(20, 14), P(21, 13), P(21, 4), P(20, 3), P(4, 3));
            shape.anchorMin = shape.anchorMax = shape.pivot = new Vector2(0f, 1f);
            shape.anchoredPosition = Vector2.zero;
            shape.sizeDelta = new Vector2(24 * S, 22 * S);
            shape.localScale = Vector3.one / S;
            for (int i = 0; i < 3; i++)
                Box(icon, "Dot" + i, c).rectTransform.TL(6.75f + i * 4f, 7.25f, 2.5f, 2.5f);
        }

        // Above the bottom bar, bottom-left (over the mode list while it is open).
        void BuildChatPanel(RectTransform stage)
        {
            var panel = Card(stage, "ChatPanel", Surface, LineStrong);
            panel.rectTransform.At(0f, 0f, 40, 100, CHAT_W, CHAT_H);
            chatPanel = panel.gameObject;

            // header
            var head = Rect(panel.transform, "Head").TL(0, 0, CHAT_W, 44);
            var cap = HRow(head, "Caption", 8);
            cap.GetComponent<RectTransform>().Fill(16, 0, 16, 0);
            var mini = Rect(cap.transform, "Icon").LE(16, 15);
            var miniIcon = Rect(mini, "Bubble").At(0.5f, 0.5f, 0, 0, 24, 22);
            miniIcon.localScale = Vector3.one * (16f / 24f);
            ChatBubbleIcon(miniIcon, Accent);
            Caption(cap.transform, "LOBBY CHAT", 12, Fg, 0.16f).LE(-1, 16);
            Spacer(cap.transform);
            KeyCap(cap.transform, "ESC", 10, Muted, Line);
            Label(cap.transform, "close", F.Body, 12, Muted).LE(-1, 16);
            Box(panel.transform, "HeadLine", Line).rectTransform.TL(0, 44, CHAT_W, 1);

            // history
            var view = Rect(panel.transform, "History");
            view.Fill(0, 45, 0, 64);
            view.gameObject.AddComponent<Image>().color = Color.clear;   // catches the wheel / drag over the gaps
            view.gameObject.AddComponent<RectMask2D>();
            var content = VCol(view, "Content", 5, TextAnchor.LowerLeft);
            content.padding = new RectOffset(16, 22, 10, 10);
            chatContent = content.GetComponent<RectTransform>();
            // anchored to the bottom: a short history sits right above the text field, like any game chat
            chatContent.anchorMin = Vector2.zero; chatContent.anchorMax = new Vector2(1f, 0f); chatContent.pivot = new Vector2(0.5f, 0f);
            chatContent.offsetMin = chatContent.offsetMax = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var barRt = Rect(view, "Scrollbar");
            barRt.anchorMin = new Vector2(1f, 0f); barRt.anchorMax = Vector2.one; barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-10f, 8f); barRt.offsetMax = new Vector2(-6f, -8f);
            barRt.gameObject.AddComponent<Image>().color = A(Line, 0.5f);
            var handle = Box(Rect(barRt, "Area").Fill(), "Handle", Muted).rectTransform.Fill();
            var bar = barRt.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle;
            bar.targetGraphic = handle.GetComponent<Image>();

            chatScroll = view.gameObject.AddComponent<ScrollRect>();
            chatScroll.horizontal = false;
            chatScroll.vertical = true;
            chatScroll.movementType = ScrollRect.MovementType.Clamped;
            chatScroll.scrollSensitivity = 30f;
            chatScroll.inertia = false;
            chatScroll.viewport = view;
            chatScroll.content = chatContent;
            chatScroll.verticalScrollbar = bar;
            chatScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var empty = Label(view, "No messages yet. Say hi to the squad!", F.Body, 13, Faint, TextAlignmentOptions.Center);
            empty.rectTransform.Fill();
            chatEmpty = empty.gameObject;

            // text field + SEND
            Box(panel.transform, "InputLine", Line).rectTransform.At(0.5f, 0f, 0, 63, CHAT_W, 1);
            var row = HRow(panel.transform, "InputRow", 8);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = Vector2.zero; rowRt.anchorMax = new Vector2(1f, 0f); rowRt.pivot = new Vector2(0.5f, 0f);
            rowRt.offsetMin = new Vector2(12, 12); rowRt.offsetMax = new Vector2(-12, 52);
            chatInput = Input(row.transform, "Type a message…", F.Body, 14, MatchNet.CHAT_MAX_LEN);
            chatInput.LE(-1, 40, 1);
            chatInput.lineType = TMP_InputField.LineType.SingleLine;
            chatInput.onSubmit.AddListener(SubmitChat);
            Button(row.transform, "SEND", Btn.Secondary, 15, () => SubmitChat(chatInput.text)).LE(76, 40);

            chatPanel.SetActive(false);
        }

        public void ToggleChat()
        {
            if (ChatOpen) CloseChat(); else OpenChat();
        }

        public void OpenChat()
        {
            if (chatPanel == null) return;
            chatPanel.SetActive(true);
            chatPanel.transform.SetAsLastSibling();
            MatchNet.MarkChatSeen();
            chatShownVersion = -1;   // redraw with the latest lines
            chatSnapFrames = 3;
            chatButton.lineN = chatButton.lineH = Accent;
            chatButton.Refresh();
            FocusChat();
        }

        public void CloseChat()
        {
            if (chatPanel == null) return;
            chatRefocus = false;
            if (chatInput != null && chatInput.isFocused) chatInput.DeactivateInputField();
            var es = EventSystem.current;
            if (es != null && chatInput != null && es.currentSelectedGameObject == chatInput.gameObject) es.SetSelectedGameObject(null);
            chatPanel.SetActive(false);
            if (chatButton != null) chatButton.SetStyle(Btn.Quiet);
        }

        void FocusChat()
        {
            if (chatInput == null || !chatInput.isActiveAndEnabled) return;
            chatInput.Select();
            chatInput.ActivateInputField();
        }

        void SubmitChat(string text)
        {
            if (!string.IsNullOrWhiteSpace(text) && MatchNet.Instance != null) MatchNet.Instance.SendChat(text);
            chatInput.SetTextWithoutNotify("");
            chatRefocus = true;    // TMP drops the focus after Enter: take it back next frame
            chatSnapFrames = 3;    // jump to your own line
        }

        // Called every frame from Update while in the lobby (before the hotkeys).
        void ChatUpdate(Keyboard kb, bool overlay)
        {
            if (chatPanel == null) return;
            bool typing = TypingInField();
            // the key that closes an overlay (Esc in the customizer) must not also close / open the chat
            bool keysOk = kb != null && !overlay && !chatOverlayBefore;
            bool enter = keysOk && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                         && !typing && !chatWasTyping;   // not the Enter that just finished typing a name

            if (ChatOpen)
            {
                if (keysOk && kb.escapeKey.wasPressedThisFrame) CloseChat();
                else if (enter) FocusChat();
            }
            else if (enter) OpenChat();

            if (ChatOpen)
            {
                MatchNet.MarkChatSeen();
                if (chatRefocus) { chatRefocus = false; FocusChat(); }
                RedrawChatIfChanged();
                if (chatSnapFrames > 0)
                {
                    chatSnapFrames--;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
                    chatScroll.verticalNormalizedPosition = 0f;
                }
            }

            int unread = MatchNet.ChatUnread;
            bool showBadge = unread > 0 && !ChatOpen;
            if (chatBadge.gameObject.activeSelf != showBadge) chatBadge.gameObject.SetActive(showBadge);
            if (showBadge) chatBadgeText.text = unread > 9 ? "9+" : unread.ToString();

            chatWasTyping = TypingInField();
            chatOverlayBefore = overlay;
        }

        void RedrawChatIfChanged()
        {
            var mnet = MatchNet.Instance;
            var net = NetworkManager.Singleton;
            int mode = mnet != null ? (int)mnet.Mode : -1;
            int rosterN = mnet != null ? mnet.Roster.Count : -1;
            if (chatShownVersion == MatchNet.ChatVersion && chatShownMode == mode && chatShownRoster == rosterN) return;
            bool newLines = chatShownVersion != MatchNet.ChatVersion;
            chatShownVersion = MatchNet.ChatVersion;
            chatShownMode = mode;
            chatShownRoster = rosterN;

            var history = MatchNet.ChatHistory;
            ulong me = net != null ? net.LocalClientId : ulong.MaxValue;
            while (chatLabels.Count < history.Count)
            {
                var l = Label(chatContent, "", F.Body, 14, TextSoft, TextAlignmentOptions.TopLeft, 0f, "Line");
                l.Wrap();
                chatLabels.Add(l);
            }
            for (int i = 0; i < chatLabels.Count; i++)
            {
                var l = chatLabels[i];
                bool on = i < history.Count;
                if (l.gameObject.activeSelf != on) l.gameObject.SetActive(on);
                if (!on) continue;
                var line = history[i];
                if (line.system)
                {
                    l.text = NoTags(line.text);
                    l.color = Muted;
                    continue;
                }
                int team = mnet != null ? mnet.LobbyTeamOf(line.senderId) : 0;
                Color nameColor = line.senderId == me ? Accent : team != 0 ? TeamColor(team) : Fg;
                l.text = "<color=" + Html(nameColor) + "><b>" + NoTags(line.name) + "</b></color>   " + NoTags(line.text);
                l.color = TextSoft;
            }
            chatEmpty.SetActive(history.Count == 0);

            // follow new lines when you are at the bottom (scrolled up to read: stay there), always after your own
            bool atBottom = chatScroll.verticalNormalizedPosition <= 0.02f || chatContent.rect.height <= chatScroll.viewport.rect.height + 1f;
            bool mineLast = history.Count > 0 && !history[history.Count - 1].system && history[history.Count - 1].senderId == me;
            if (newLines && (atBottom || mineLast)) chatSnapFrames = Mathf.Max(chatSnapFrames, 2);
        }

        // Rich text off for player text: the server strips '<' '>' already, names are cleaned here.
        static string NoTags(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "").Replace(">", "");
    }
}
