using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Lobby map vote (part of NetworkUI), under the character preview: one card per MapCatalog map
    // with its top-down preview and vote count. Everyone clicks a card to vote (again to take the vote
    // back); the card that will be played is framed in orange ("NEXT"). Most votes wins, the host's
    // vote breaks ties, and the host can LOCK a map regardless of the votes: the locked card says
    // "LOCKED BY HOST", its button turns into UNLOCK, and a caption under the header explains it.
    public partial class NetworkUI
    {
        class MapCard
        {
            public KitButton btn, lockBtn;
            public Image frame, share;
            public CanvasGroup group;
            public TextMeshProUGUI votes;
            public float votesX, votesW;   // RANDOM MAP row: where the votes text sits (the lock badge takes its place)
            public GameObject next, yours, locked;
            public int map;        // MapCatalog index, or MapCatalog.RANDOM for the slim RANDOM MAP row
        }

        readonly List<MapCard> mapCards = new List<MapCard>();
        TextMeshProUGUI mapHint, mapLockNote;

        void BuildMapVote(RectTransform col)
        {
            // up to two cards side by side; more maps wrap into rows of two with shorter preview strips
            const float W = 280f, HeadH = 16f, NoteH = 14f, Gap = 8f;
            int n = Mathf.Max(1, MapCatalog.Count);
            int cols = Mathf.Min(n, 2), rows = (n + cols - 1) / cols;
            float ThumbH = rows > 1 ? 40f : 64f;
            float CardH = ThumbH + 48f;
            const float RandH = 32f;   // RANDOM MAP: a slim row under the map cards
            float gridH = rows * CardH + (rows - 1) * Gap;
            var block = Rect(col, "MapVote");
            float top = HeadH + 2f + NoteH + 6f;   // header, the LOCK caption, then the cards
            block.LE(-1, top + gridH + Gap + RandH);

            var head = Rect(block, "Head").TL(0, 0, W, HeadH);
            Caption(head, "MAP VOTE", 12, Muted, 0.16f).rectTransform.Fill();
            mapHint = Label(head, "", F.Body, 11, Muted, TextAlignmentOptions.MidlineRight);
            mapHint.rectTransform.Fill();
            // what LOCK does (host) / that the host locked the map (everyone); RefreshMapVote sets the text
            mapLockNote = Label(block, "", F.Body, 11, Muted);
            mapLockNote.overflowMode = TextOverflowModes.Ellipsis;
            mapLockNote.rectTransform.TL(0, HeadH + 2f, W, NoteH);

            float w = (W - Gap * (cols - 1)) / cols;
            for (int i = 0; i < MapCatalog.Count; i++)
            {
                int map = i;
                var c = new MapCard { map = map };
                c.btn = Button(block, "", Btn.Ghost, 10, () => { if (MatchNet.Instance != null) MatchNet.Instance.VoteMap(map); });
                c.btn.GetComponent<RectTransform>().TL((i % cols) * (w + Gap), top + (i / cols) * (CardH + Gap), w, CardH);
                c.btn.label.gameObject.SetActive(false);
                c.group = c.btn.gameObject.AddComponent<CanvasGroup>();

                // preview strip (rendered by the editor map builders: a centre band of the top-down shot),
                // or the map's initial when there is no preview
                var thumb = Box(c.btn.transform, "Preview", Scenery);
                thumb.rectTransform.TL(1, 1, w - 2, ThumbH);
                var tex = MapCatalog.Preview(map);
                if (tex != null)
                {
                    var raw = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
                    raw.transform.SetParent(thumb.transform, false);
                    raw.texture = tex;
                    raw.raycastTarget = false;
                    float vh = Mathf.Clamp01(ThumbH / (w - 2));
                    raw.uvRect = new Rect(0f, 0.5f - vh / 2f, 1f, vh);
                    raw.rectTransform.Fill();
                }
                else
                {
                    Label(thumb.transform, MapCatalog.NameOf(map).Substring(0, 1).ToUpperInvariant(), F.CondBlack, 40, Muted,
                        TextAlignmentOptions.Center).rectTransform.Fill();
                }
                var next = Badge(thumb.transform, "NEXT", Accent, Ground, Color.clear, 9);
                next.anchorMin = next.anchorMax = next.pivot = new Vector2(0f, 1f);
                next.anchoredPosition = new Vector2(4, -4);
                c.next = next.gameObject;
                // the locked card is always the one played: this badge stands in for NEXT there
                var locked = Badge(thumb.transform, "LOCKED BY HOST", Accent, Ground, Color.clear, 9);
                locked.anchorMin = locked.anchorMax = locked.pivot = new Vector2(0f, 1f);
                locked.anchoredPosition = new Vector2(4, -4);
                c.locked = locked.gameObject;
                var yours = Badge(thumb.transform, "YOUR VOTE", Scrim(0.85f), Fg, Fg, 9);
                yours.anchorMin = yours.anchorMax = yours.pivot = new Vector2(0f, 0f);
                yours.anchoredPosition = new Vector2(4, 4);
                c.yours = yours.gameObject;

                // host only: lock this map regardless of the votes (click again to unlock)
                c.lockBtn = Button(c.btn.transform, "LOCK", Btn.Quiet, 10, () =>
                {
                    var mn = MatchNet.Instance;
                    if (isHost && mn != null) mn.HostLockMap(mn.HostMapLock == map ? -1 : map);
                });
                c.lockBtn.GetComponent<RectTransform>().At(1f, 0f, -6, 6, 52, 22);

                // name, votes, share of the votes
                Label(c.btn.transform, MapCatalog.NameOf(map).ToUpperInvariant(), F.CondBold, 17, Fg, TextAlignmentOptions.MidlineLeft, 0.02f)
                    .rectTransform.TL(8, ThumbH + 4, w - 16, 20);
                c.votes = Label(c.btn.transform, "", F.BodySemi, 12, Muted);
                c.votes.rectTransform.TL(8, ThumbH + 24, w - 66, 18);
                c.share = Bar(c.btn.transform, "Share", Line, Accent);
                ((RectTransform)c.share.transform.parent).At(0f, 0f, 1, 1, w - 2, 3);

                c.frame = Frame(c.btn.transform, Line, 1f);
                mapCards.Add(c);
            }
            BuildRandomMapRow(block, top + gridH + Gap, W, RandH);
        }

        // "? RANDOM MAP · 2 votes · yours" with the share bar and the host's LOCK, under the map cards.
        void BuildRandomMapRow(RectTransform block, float y, float w, float h)
        {
            var c = new MapCard { map = MapCatalog.RANDOM };
            c.btn = Button(block, "", Btn.Ghost, 10, () => { if (MatchNet.Instance != null) MatchNet.Instance.VoteMap(MapCatalog.RANDOM); });
            c.btn.GetComponent<RectTransform>().TL(0, y, w, h);
            c.btn.label.gameObject.SetActive(false);
            c.group = c.btn.gameObject.AddComponent<CanvasGroup>();
            var dice = Box(c.btn.transform, "Dice", Scenery);
            dice.rectTransform.TL(5, 5, h - 10, h - 10);
            Label(dice.transform, "?", F.CondBlack, 17, Accent, TextAlignmentOptions.Center).rectTransform.Fill();
            var name = Label(c.btn.transform, "RANDOM MAP", F.CondBold, 16, Fg, TextAlignmentOptions.MidlineLeft, 0.02f);
            float nameW = Mathf.Min(104f, Mathf.Ceil(name.preferredWidth) + 2f);
            name.rectTransform.TL(h + 2, 0, nameW, h);
            c.votesX = h + 2 + nameW + 8;
            c.votesW = w - c.votesX - 64;   // room for the host's LOCK / UNLOCK on the right
            c.votes = Label(c.btn.transform, "", F.BodySemi, 11, Muted, TextAlignmentOptions.MidlineLeft);
            c.votes.rectTransform.TL(c.votesX, 0, c.votesW, h);
            c.share = Bar(c.btn.transform, "Share", Line, Accent);
            ((RectTransform)c.share.transform.parent).At(0f, 0f, 1, 1, w - 2, 2);
            c.lockBtn = Button(c.btn.transform, "LOCK", Btn.Quiet, 10, () =>
            {
                var mn = MatchNet.Instance;
                if (isHost && mn != null) mn.HostLockMap(mn.HostMapLock == MapCatalog.RANDOM ? -1 : MapCatalog.RANDOM);
            });
            c.lockBtn.GetComponent<RectTransform>().At(1f, 0.5f, -5, 0, 52, 22);
            // states show in the frame (NEXT), the votes text (yours), a LOCKED BY HOST badge in place of
            // the votes (they don't count while locked) and the lock button (UNLOCK)
            c.next = new GameObject("Next");
            c.next.transform.SetParent(c.btn.transform, false);
            c.yours = new GameObject("Yours");
            c.yours.transform.SetParent(c.btn.transform, false);
            var locked = Badge(c.btn.transform, "LOCKED BY HOST", Accent, Ground, Color.clear, 9);
            locked.anchorMin = locked.anchorMax = locked.pivot = new Vector2(0f, 0.5f);
            locked.anchoredPosition = new Vector2(c.votesX, 0);
            c.locked = locked.gameObject;
            c.frame = Frame(c.btn.transform, Line, 1f);
            mapCards.Add(c);
        }

        void RefreshMapVote(MatchNet mnet, NetworkManager net)
        {
            if (mapCards.Count == 0) return;
            bool lobby = mnet.CurrentPhase == MatchNet.Phase.Lobby;
            int winner = mnet.WinningMap;
            int mine = mnet.LocalMapVote;
            int locked = mnet.HostMapLock;
            int total = 0;
            foreach (var mc in mapCards) total += mnet.MapVotes(mc.map);

            for (int i = 0; i < mapCards.Count; i++)
            {
                var c = mapCards[i];
                int map = c.map;
                bool win = map == winner;
                int v = mnet.MapVotes(map);
                c.btn.bgN = c.btn.bgP = win ? Raised : Surface;
                c.btn.bgH = Raised;
                c.btn.bgD = c.btn.bgN;
                c.btn.interactable = lobby;
                c.btn.Refresh();
                c.frame.color = win ? Accent : Line;
                c.frame.pixelsPerUnitMultiplier = 4f / (win ? 2f : 1f);
                c.group.alpha = (MapCatalog.ValidChoice(locked) && !win) ? 0.55f : 1f;
                c.votes.text = v + (v == 1 ? " vote" : " votes");
                c.votes.color = v > 0 ? Fg : Muted;
                SetBar(c.share, total > 0 ? (float)v / total : 0f);
                c.next.SetActive(win && map != locked);   // the LOCKED BY HOST badge says it instead
                c.yours.SetActive(map == mine);
                c.locked.SetActive(map == locked);
                if (map == MapCatalog.RANDOM) c.votes.gameObject.SetActive(map != locked);
                c.lockBtn.gameObject.SetActive(isHost);
                c.lockBtn.SetLabel(map == locked ? "UNLOCK" : "LOCK");
                c.lockBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(map == locked ? 60 : 46, 22);
                if (map == MapCatalog.RANDOM && map == mine) c.votes.text += " · yours";
            }

            bool isLocked = MapCatalog.ValidChoice(locked);
            mapHint.text = isLocked ? ""   // the caption under the header says it
                         : mine < 0 ? "Click a map to vote" : "Most votes wins";
            mapLockNote.text = isHost
                ? (isLocked ? "Locked by you: votes are ignored until you UNLOCK."
                            : "LOCK = you pick the map yourself, votes are ignored.")
                : (isLocked ? "The host locked the map: votes are ignored."
                            : "Most votes wins; the host's vote breaks ties.");
            mapLockNote.color = isLocked ? AccentHi : Muted;
        }
    }
}
