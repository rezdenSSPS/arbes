using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Lobby: MATCH RULES (rules live in MatchNet.Rules.cs). A RULES button in the bottom bar (with a
    // CUSTOM badge when anything differs from the normal game) opens a window over the lobby with one
    // row per rule. The host changes them; everyone else sees the host's picks, read-only.
    public partial class NetworkUI
    {
        const float RULES_W = 620f, RULES_H = 470f;

        KitButton rulesButton;
        RectTransform rulesBadge;
        GameObject rulesOverlay;
        readonly List<Segmented> ruleSegs = new List<Segmented>();
        readonly List<TextMeshProUGUI> ruleNotes = new List<TextMeshProUGUI>();
        TextMeshProUGUI rulesSub;
        KitButton rulesReset;

        public bool RulesOpen => rulesOverlay != null && rulesOverlay.activeSelf;

        // Bottom bar, after the chat button.
        void BuildRulesButton(Transform bar)
        {
            rulesButton = Button(bar, "RULES", Btn.Quiet, 16, ToggleRules, F.CondBold);
            rulesButton.name = "Btn_Rules";
            rulesButton.LE(104, 48);
            rulesBadge = Badge(rulesButton.transform, "CUSTOM", Accent, Ground, Color.clear, 9);
            rulesBadge.anchorMin = rulesBadge.anchorMax = new Vector2(0.5f, 1f);
            rulesBadge.pivot = new Vector2(0.5f, 0.5f);
            rulesBadge.anchoredPosition = new Vector2(0, 0);
            rulesBadge.gameObject.SetActive(false);
        }

        void BuildRulesPanel(RectTransform stage)
        {
            var dim = Box(stage, "RulesOverlay", Scrim(0.7f), true);
            dim.rectTransform.Fill(-400, -400, -400, -400);
            dim.gameObject.AddComponent<UIClick>().onClick = CloseRules;   // click outside = close
            rulesOverlay = dim.gameObject;

            var panel = Card(dim.transform, "RulesPanel", Surface, LineStrong);
            panel.rectTransform.At(0.5f, 0.5f, 0, 0, RULES_W, RULES_H);
            panel.gameObject.AddComponent<ClickSwallow>();   // clicks inside the window do not reach the backdrop

            Label(panel.transform, "MATCH RULES", F.CondBlack, 28, Fg, TextAlignmentOptions.MidlineLeft, 0.02f)
                .rectTransform.TL(24, 18, 300, 34);
            rulesSub = Label(panel.transform, "", F.Body, 13, Muted, TextAlignmentOptions.MidlineLeft);
            rulesSub.rectTransform.TL(24, 52, 440, 18);
            var esc = KeyButton(panel.transform, "CLOSE", "ESC", Btn.Quiet, 14, CloseRules, 34);
            esc.GetComponent<RectTransform>().anchorMin = esc.GetComponent<RectTransform>().anchorMax =
                esc.GetComponent<RectTransform>().pivot = new Vector2(1f, 1f);
            esc.GetComponent<RectTransform>().anchoredPosition = new Vector2(-20, -20);
            Box(panel.transform, "HeadLine", Line).rectTransform.TL(0, 82, RULES_W, 1);

            ruleSegs.Clear(); ruleNotes.Clear();
            for (int i = 0; i < MatchNet.RULE_COUNT; i++)
            {
                var id = (MatchNet.RuleId)i;
                float y = 98 + i * 52;
                var title = Label(panel.transform, MatchNet.RULE_TITLES[i], F.BodyBold, 12, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
                title.rectTransform.TL(24, y, 160, 20);
                var note = Label(panel.transform, "", F.Body, 11, Faint, TextAlignmentOptions.MidlineLeft);
                note.rectTransform.TL(24, y + 18, 190, 18);
                ruleNotes.Add(note);
                var holder = Rect(panel.transform, "Seg" + i);
                holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(1f, 1f);
                holder.anchoredPosition = new Vector2(-24, -y + 2);
                holder.sizeDelta = new Vector2(380, 38);
                var hl = HRow(holder, "Row", 0, TextAnchor.MiddleRight);
                hl.GetComponent<RectTransform>().Fill();
                var seg = Segments(hl.transform, MatchNet.RuleOptionNames(id), 0, 36, 14,
                    k => MatchNet.Instance?.HostSetRule(id, k), 16f);
                ruleSegs.Add(seg);
            }

            Box(panel.transform, "FootLine", Line).rectTransform.TL(0, RULES_H - 66, RULES_W, 1);
            rulesReset = Button(panel.transform, "RESET TO NORMAL", Btn.Ghost, 15, () => MatchNet.Instance?.HostResetRules(), F.CondSemi);
            rulesReset.GetComponent<RectTransform>().TL(16, RULES_H - 56, 190, 44);
            var done = Button(panel.transform, "DONE", Btn.Primary, 18, CloseRules, F.CondBold, 10);
            var drt = done.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1f, 0f);
            drt.anchoredPosition = new Vector2(-20, 12);
            drt.sizeDelta = new Vector2(130, 44);

            rulesOverlay.SetActive(false);
        }

        void ToggleRules()
        {
            if (RulesOpen) CloseRules();
            else { CloseChat(); rulesOverlay.SetActive(true); RefreshRules(MatchNet.Instance); }
        }

        void CloseRules() { if (rulesOverlay != null) rulesOverlay.SetActive(false); }

        void RulesUpdate(Keyboard kb)
        {
            if (RulesOpen && kb != null && kb.escapeKey.wasPressedThisFrame) CloseRules();
        }

        void RefreshRules(MatchNet mn)
        {
            if (rulesButton == null || mn == null) return;
            rulesBadge.gameObject.SetActive(mn.RulesCustom);
            if (!RulesOpen) return;
            bool lives = MatchNet.ModeAllowsLives(mn.Mode) || mn.RandomModePending;
            rulesSub.text = isHost ? "Your lobby, your rules. Everyone sees them before the match."
                                   : "Set by the host. You can only look.";
            rulesReset.gameObject.SetActive(isHost);
            for (int i = 0; i < ruleSegs.Count; i++)
            {
                var id = (MatchNet.RuleId)i;
                var seg = ruleSegs[i];
                if (seg.Selected != mn.Rule(id)) seg.Select(mn.Rule(id), false);
                seg.SetInteractable(isHost);
                ruleNotes[i].text = id switch
                {
                    MatchNet.RuleId.Health => "players and bots",
                    MatchNet.RuleId.Speed => "walking, running, bots",
                    MatchNet.RuleId.Jump => mn.Rule(id) == 2 ? "low gravity: long, slow jumps" : "how high you jump",
                    MatchNet.RuleId.Respawn => "wait after being eliminated",
                    MatchNet.RuleId.Lives => !lives ? "not used in " + ModeLabel(mn.Mode) : mn.Rule(id) == 0 ? "respawn forever" : "last one standing wins",
                    _ => mn.Rule(id) == 1 ? "reserve never runs out" : "reload from the reserve",
                };
                ruleNotes[i].color = id == MatchNet.RuleId.Lives && !lives ? Danger : Faint;
            }
        }

        class ClickSwallow : MonoBehaviour, IPointerClickHandler { public void OnPointerClick(PointerEventData e) { } }

        static string ModeLabel(MatchNet.GameModeType m) => m switch
        {
            MatchNet.GameModeType.Coop => "Co-op",
            MatchNet.GameModeType.Milsim => "Milsim",
            MatchNet.GameModeType.Medic => "Medic",
            MatchNet.GameModeType.Bomb => "Bomb",
            MatchNet.GameModeType.VipEscort => "VIP",
            MatchNet.GameModeType.Infection => "Infection",
            _ => "this mode",
        };
    }
}
