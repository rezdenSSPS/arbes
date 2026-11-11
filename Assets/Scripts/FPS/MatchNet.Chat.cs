using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace AirsoftFPS
{
    // Lobby chat (UI: NetworkUI.Chat.cs). Client -> server -> everyone, through named messages like the
    // rest of MatchNet. The server owns everything that matters: it cleans the text, rate-limits each
    // client and stamps the sender's name from its own roster (a client can't speak as someone else).
    // The history is static so it survives lobby -> match -> lobby; it is wiped when the session ends.
    public partial class MatchNet
    {
        const string MSG_CHAT_SEND = "mn_chat_send";   // client -> server: the typed text
        const string MSG_CHAT      = "mn_chat";        // server -> clients: one finished line

        public const int CHAT_MAX_LEN = 120;           // characters per message (after cleaning)
        public const int CHAT_HISTORY = 60;            // lines kept
        public const int CHAT_RATE_COUNT = 3;          // at most this many messages ...
        public const float CHAT_RATE_WINDOW = 2f;      // ... per this many seconds, per client

        public struct ChatLine
        {
            public bool system;      // join / leave / rename / "too fast" notices
            public ulong senderId;   // ulong.MaxValue for system lines
            public string name;      // the server's roster name at the time it was sent
            public string text;
        }

        static readonly List<ChatLine> chatHistory = new List<ChatLine>();
        public static IReadOnlyList<ChatLine> ChatHistory => chatHistory;
        // Bumped whenever the history changes, so the UI knows when to redraw.
        public static int ChatVersion { get; private set; }
        // Lines from other players that arrived while the chat panel was closed.
        public static int ChatUnread { get; private set; }
        static int chatSeenFrame = -10;

        // Server only
        readonly Dictionary<ulong, Queue<double>> chatSendTimes = new Dictionary<ulong, Queue<double>>();
        readonly Dictionary<ulong, double> chatSlowNoticeAt = new Dictionary<ulong, double>();
        readonly HashSet<ulong> chatAnnounced = new HashSet<ulong>();   // clients whose "joined" line went out

        // Domain reload is off (Enter Play Mode Options): start every play session with an empty chat.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetChatStatics()
        {
            chatHistory.Clear();
            ChatVersion = 0;
            ChatUnread = 0;
            chatSeenFrame = -10;
        }

        // UI: call every frame while the chat panel is open and visible (new lines then don't count as unread).
        public static void MarkChatSeen()
        {
            chatSeenFrame = Time.frameCount;
            ChatUnread = 0;
        }

        static bool ChatBeingRead => Time.frameCount - chatSeenFrame <= 1;

        // ───────── local player ─────────

        public void SendChat(string text)
        {
            if (nm == null || !nm.IsConnectedClient || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > CHAT_MAX_LEN * 2) text = text.Substring(0, CHAT_MAX_LEN * 2);   // the server cuts it to size
            using var w = new FastBufferWriter(64 + text.Length * 2, Allocator.Temp);
            w.WriteValueSafe(text);
            nm.CustomMessagingManager.SendNamedMessage(MSG_CHAT_SEND, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }

        // The team a player's name is coloured with in the lobby: their match team if they have one,
        // otherwise the side the lobby shows them on (alternating by roster order, as the match splits them).
        public int LobbyTeamOf(ulong clientId)
        {
            if (!TeamMode) return 0;
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].clientId == clientId) return roster[i].team != 0 ? roster[i].team : (i % 2 == 0 ? 1 : 2);
            return 0;
        }

        // Server-side cleaning: no TMP rich text ('<' '>'), no control characters, trimmed, at most CHAT_MAX_LEN.
        // Returns "" for a message that is empty after cleaning (it is then dropped).
        public static string SanitizeChat(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(Mathf.Min(s.Length, CHAT_MAX_LEN * 4));
            foreach (char c in s)
            {
                if (c == '<' || c == '>') continue;
                sb.Append(char.IsControl(c) ? ' ' : c);
            }
            string t = sb.ToString().Trim();
            if (t.Length > CHAT_MAX_LEN)
            {
                int cut = CHAT_MAX_LEN;
                if (char.IsHighSurrogate(t[cut - 1])) cut--;   // don't split an emoji / surrogate pair
                t = t.Substring(0, cut).TrimEnd();
            }
            return t;
        }

        // ───────── server ─────────

        void RegisterChatServerHandlers(CustomMessagingManager cm)
            => cm.RegisterNamedMessageHandler(MSG_CHAT_SEND, OnServerRecvChat);

        void RegisterChatClientHandlers(CustomMessagingManager cm)
            => cm.RegisterNamedMessageHandler(MSG_CHAT, OnClientRecvChat);

        void OnServerRecvChat(ulong sender, FastBufferReader reader)
        {
            string raw;
            try { reader.ReadValueSafe(out raw); }
            catch (Exception) { return; }   // malformed: drop it
            if (!HasEntry(sender)) return;
            string text = SanitizeChat(raw);
            if (text.Length == 0) return;
            if (!ChatRateOk(sender))
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (!chatSlowNoticeAt.TryGetValue(sender, out double last) || now - last > CHAT_RATE_WINDOW)
                {
                    chatSlowNoticeAt[sender] = now;
                    ServerSendChat(new ChatLine { system = true, senderId = ulong.MaxValue, name = "", text = "You're sending messages too fast." }, sender);
                }
                return;
            }
            ServerSendChat(new ChatLine { system = false, senderId = sender, name = NameFor(sender), text = text });
        }

        bool ChatRateOk(ulong sender)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!chatSendTimes.TryGetValue(sender, out var q)) chatSendTimes[sender] = q = new Queue<double>();
            while (q.Count > 0 && now - q.Peek() >= CHAT_RATE_WINDOW) q.Dequeue();
            if (q.Count >= CHAT_RATE_COUNT) return false;
            q.Enqueue(now);
            return true;
        }

        void ServerSystemChat(string text) =>
            ServerSendChat(new ChatLine { system = true, senderId = ulong.MaxValue, name = "", text = text });

        // Server: add the line here (the host is a client too) and send it to everyone else,
        // or only to `onlyTo` (a notice for one player).
        void ServerSendChat(ChatLine line, ulong onlyTo = ulong.MaxValue)
        {
            if (nm == null || !nm.IsServer) return;
            bool toHost = onlyTo == ulong.MaxValue || onlyTo == nm.LocalClientId;
            if (toHost && nm.IsClient) AddChatLine(line);
            if (nm.ShutdownInProgress || nm.CustomMessagingManager == null) return;
            using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
            w.WriteValueSafe(line.system);
            w.WriteValueSafe(line.senderId);
            w.WriteValueSafe(line.name ?? "");
            w.WriteValueSafe(line.text ?? "");
            if (onlyTo == ulong.MaxValue)
                nm.CustomMessagingManager.SendNamedMessageToAll(MSG_CHAT, w, NetworkDelivery.ReliableSequenced);
            else if (onlyTo != nm.LocalClientId)
                nm.CustomMessagingManager.SendNamedMessage(MSG_CHAT, onlyTo, w, NetworkDelivery.ReliableSequenced);
        }

        void OnClientRecvChat(ulong sender, FastBufferReader reader)
        {
            if (nm != null && nm.IsServer) return;   // the host added it already (ServerSendChat)
            var line = new ChatLine();
            try
            {
                reader.ReadValueSafe(out line.system);
                reader.ReadValueSafe(out line.senderId);
                reader.ReadValueSafe(out line.name);
                reader.ReadValueSafe(out line.text);
            }
            catch (Exception) { return; }
            AddChatLine(line);
        }

        static void AddChatLine(ChatLine line)
        {
            chatHistory.Add(line);
            if (chatHistory.Count > CHAT_HISTORY) chatHistory.RemoveRange(0, chatHistory.Count - CHAT_HISTORY);
            ChatVersion++;
            var nmLocal = NetworkManager.Singleton;
            bool mine = nmLocal != null && line.senderId == nmLocal.LocalClientId;
            if (!line.system && !mine && !ChatBeingRead) ChatUnread++;
        }

        // Server, from OnServerRecvName: the first name a client sends means it has joined; later ones are renames.
        void ChatOnName(ulong sender, string oldName, string newName)
        {
            if (nm == null || !nm.IsServer) return;
            if (chatAnnounced.Add(sender))
            {
                if (sender != nm.LocalClientId) ServerSystemChat(newName + " joined.");   // not for the host opening the room
            }
            else if (oldName != newName) ServerSystemChat(oldName + " is now " + newName + ".");
        }

        // Server, from HandleClientDisconnected (before the roster entry goes).
        void ChatOnLeft(ulong clientId)
        {
            chatSendTimes.Remove(clientId);
            chatSlowNoticeAt.Remove(clientId);
            if (chatAnnounced.Remove(clientId)) ServerSystemChat(NameFor(clientId) + " left.");
        }

        // The session ended locally (HandleClientStopped): a fresh session starts with an empty chat.
        void ClearChatSession()
        {
            chatHistory.Clear();
            ChatVersion++;
            ChatUnread = 0;
            chatSendTimes.Clear();
            chatSlowNoticeAt.Clear();
            chatAnnounced.Clear();
        }
    }
}
