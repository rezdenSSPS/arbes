using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Lobby RANDOM MODE card (part of NetworkUI): the first card of the mode list, with the "?" dice of the
    // RANDOM MAP row. The host picks it (click, or M cycles to it after the last mode); the server draws the
    // real mode at START (MatchNet.RandomMode.cs).
    public partial class NetworkUI
    {
        // ScrollModeIntoView key of the random card (real modes use their GameModeType value).
        const int RANDOM_MODE_KEY = MatchNet.MODE_COUNT;

        KitButton randomModeBtn;
        Image randomModeFrame;
        CanvasGroup randomModeGroup;
        TextMeshProUGUI randomModeDesc;

        void BuildRandomModeCard(Transform parent, float cardH, bool compact)
        {
            var b = Button(parent, "", Btn.Ghost, 10, () =>
            {
                if (isHost && MatchNet.Instance != null) MatchNet.Instance.HostSetRandomMode();
            });
            b.LE(-1, cardH);
            b.label.gameObject.SetActive(false);
            randomModeGroup = b.gameObject.AddComponent<CanvasGroup>();
            randomModeFrame = Frame(b.transform, Line, 1f);

            float dice = compact ? 28f : 36f, pad = compact ? 10f : 14f;
            var box = Box(b.transform, "Dice", Scenery);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = box.rectTransform.pivot = new Vector2(0f, 0.5f);
            box.rectTransform.anchoredPosition = new Vector2(pad, 0f);
            box.rectTransform.sizeDelta = new Vector2(dice, dice);
            Label(box.transform, "?", F.CondBlack, compact ? 19 : 23, Accent, TextAlignmentOptions.Center).rectTransform.Fill();

            var txt = VCol(b.transform, "Text", compact ? 1 : 2, TextAnchor.MiddleLeft);
            int left = Mathf.RoundToInt(pad + dice + 10f);
            txt.padding = compact ? new RectOffset(left, 14, 5, 5) : new RectOffset(left, 16, 9, 9);
            txt.GetComponent<RectTransform>().Fill();
            var title = Label(txt.transform, "RANDOM MODE", F.CondBold, compact ? 18 : 20, Fg, TextAlignmentOptions.MidlineLeft, 0.02f);
            title.LE(-1, compact ? 20 : 22);
            title.overflowMode = TextOverflowModes.Overflow;
            randomModeDesc = Label(txt.transform, "", F.Body, 12, Muted);
            if (compact) { randomModeDesc.overflowMode = TextOverflowModes.Ellipsis; randomModeDesc.LE(-1, 16); }
            else randomModeDesc.Wrap().LE(-1, 30);
            randomModeBtn = b;
        }

        void RefreshRandomModeCard(MatchNet mnet)
        {
            if (randomModeBtn == null) return;
            bool sel = mnet.RandomModePending;
            bool avail = mnet.RandomModeAvailable(out string why);
            randomModeBtn.bgN = randomModeBtn.bgP = sel ? Raised : Surface;
            randomModeBtn.bgH = isHost && avail ? Raised : randomModeBtn.bgN;
            randomModeBtn.bgD = randomModeBtn.bgN;
            randomModeBtn.interactable = isHost && avail;
            randomModeBtn.Refresh();
            randomModeFrame.color = sel ? (avail ? Accent : DangerLine) : Line;
            randomModeFrame.pixelsPerUnitMultiplier = 4f / (sel ? 2f : 1f);
            randomModeGroup.alpha = !avail ? 0.45f : (!isHost && !sel) ? 0.55f : 1f;
            randomModeDesc.text = !avail ? why
                : mnet.RandomMapPending ? "Map and mode are both drawn at the start."
                : "Any mode this map plays, drawn at start.";
            randomModeDesc.color = avail ? Muted : DangerSoft;
        }
    }
}
