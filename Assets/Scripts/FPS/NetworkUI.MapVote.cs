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
    // vote breaks ties, and the host can LOCK a map regardless of the votes.
    public partial class NetworkUI
    {
        class MapCard
        {
            public KitButton btn, lockBtn;
            public Image frame, share;
            public CanvasGroup group;
            public TextMeshProUGUI votes;
            public GameObject next, yours, locked;
        }

        readonly List<MapCard> mapCards = new List<MapCard>();
        TextMeshProUGUI mapHint;

        void BuildMapVote(RectTransform col)
        {
            const float W = 280f, HeadH = 16f, CardH = 112f, ThumbH = 64f, Gap = 8f;
            var block = Rect(col, "MapVote");
            block.LE(-1, HeadH + 6f + CardH);

            var head = Rect(block, "Head").TL(0, 0, W, HeadH);
            Caption(head, "MAP VOTE", 12, Muted, 0.16f).rectTransform.Fill();
            mapHint = Label(head, "", F.Body, 11, Muted, TextAlignmentOptions.MidlineRight);
            mapHint.rectTransform.Fill();

            int n = Mathf.Max(1, MapCatalog.Count);
            float w = (W - Gap * (n - 1)) / n;
            for (int i = 0; i < MapCatalog.Count; i++)
            {
                int map = i;
                var c = new MapCard();
                c.btn = Button(block, "", Btn.Ghost, 10, () => { if (MatchNet.Instance != null) MatchNet.Instance.VoteMap(map); });
                c.btn.GetComponent<RectTransform>().TL(i * (w + Gap), HeadH + 6f, w, CardH);
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
                var locked = Badge(thumb.transform, "LOCKED", Ground, Accent, Accent, 9);
                locked.anchorMin = locked.anchorMax = locked.pivot = new Vector2(1f, 1f);
                locked.anchoredPosition = new Vector2(-4, -4);
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
        }

        void RefreshMapVote(MatchNet mnet, NetworkManager net)
        {
            if (mapCards.Count == 0) return;
            bool lobby = mnet.CurrentPhase == MatchNet.Phase.Lobby;
            int winner = mnet.WinningMap;
            int mine = mnet.LocalMapVote;
            int locked = mnet.HostMapLock;
            int total = 0;
            for (int i = 0; i < mapCards.Count; i++) total += mnet.MapVotes(i);

            for (int i = 0; i < mapCards.Count; i++)
            {
                var c = mapCards[i];
                bool win = i == winner;
                int v = mnet.MapVotes(i);
                c.btn.bgN = c.btn.bgP = win ? Raised : Surface;
                c.btn.bgH = Raised;
                c.btn.bgD = c.btn.bgN;
                c.btn.interactable = lobby;
                c.btn.Refresh();
                c.frame.color = win ? Accent : Line;
                c.frame.pixelsPerUnitMultiplier = 4f / (win ? 2f : 1f);
                c.group.alpha = (locked >= 0 && !win) ? 0.55f : 1f;
                c.votes.text = v + (v == 1 ? " vote" : " votes");
                c.votes.color = v > 0 ? Fg : Muted;
                SetBar(c.share, total > 0 ? (float)v / total : 0f);
                c.next.SetActive(win);
                c.yours.SetActive(i == mine);
                c.locked.SetActive(i == locked);
                c.lockBtn.gameObject.SetActive(isHost);
                c.lockBtn.SetLabel(i == locked ? "UNLOCK" : "LOCK");
                c.lockBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(i == locked ? 60 : 46, 22);
            }

            mapHint.text = locked >= 0 ? "Locked by the host"
                         : mine < 0 ? "Click a map to vote" : "Most votes wins";
        }
    }
}
