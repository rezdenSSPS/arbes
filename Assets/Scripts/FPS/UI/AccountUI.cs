using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Sign-in panel on the main menu: simple local username + password (AccountStore). Signing in switches
    // the active profile (credits / XP / owned weapons) to that account; guests keep the default profile.
    public static class AccountUI
    {
        public static void AddToMenu(RectTransform screen, RectTransform stage)
        {
            var panel = BuildPanel(screen);
            var btn = Button(stage, "ACCOUNT", Btn.Secondary, 16, panel.Open, F.CondBold);
            btn.GetComponent<RectTransform>().At(1f, 1f, -48, -176, 292, 44);
            btn.gameObject.AddComponent<AccountButtonView>().button = btn;
        }

        class AccountButtonView : MonoBehaviour
        {
            public KitButton button;
            float t;
            void Update()
            {
                if ((t -= Time.unscaledDeltaTime) > 0f) return;
                t = 0.4f;
                if (button != null) button.SetLabel(AccountStore.IsSignedIn ? AccountStore.Current.ToUpperInvariant() : "SIGN IN");
            }
        }

        static AccountPanel BuildPanel(RectTransform screen)
        {
            var root = Rect(screen, "AccountPanel").Fill();
            var backdrop = Box(root, "Backdrop", Scrim(0.86f), true);
            backdrop.rectTransform.Fill();
            var panel = root.gameObject.AddComponent<AccountPanel>();
            backdrop.gameObject.AddComponent<UIClick>().onClick = panel.Close;

            var card = Card(root, "Card", Surface, Line, 1f);
            card.rectTransform.At(0.5f, 0.5f, 0, 0, 520, 452);
            var col = VCol(card.transform, "Col", 14);
            col.padding = new RectOffset(40, 40, 30, 26);
            col.GetComponent<RectTransform>().Fill();

            var head = Rect(col.transform, "Header").LE(-1, 52);
            Caption(head, "ACCOUNT", 12, Muted, 0.16f).rectTransform.TL(0, 0, 300, 14);
            panel.title = Label(head, "SIGN IN", F.CondBlack, 34, Fg);
            panel.title.rectTransform.TL(0, 14, 360, 38);
            var close = Button(head, "CLOSE", Btn.Quiet, 16, panel.Close);
            close.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 120, 40);

            Caption(col.transform, "USERNAME", 11, Muted, 0.14f).LE(-1, 13);
            panel.user = Input(col.transform, "callsign", F.Body, 16, 16);
            panel.user.GetComponent<RectTransform>().LE(-1, 44);

            Caption(col.transform, "PASSWORD", 11, Muted, 0.14f).LE(-1, 13);
            panel.pass = Input(col.transform, "password", F.Body, 16, 32);
            panel.pass.GetComponent<RectTransform>().LE(-1, 44);
            panel.pass.contentType = TMP_InputField.ContentType.Password;
            panel.pass.ForceLabelUpdate();

            panel.status = Label(col.transform, "", F.BodyBold, 12, DangerSoft);
            panel.status.LE(-1, 16);

            var btnRow = HRow(col.transform, "Buttons", 10, TextAnchor.MiddleCenter);
            btnRow.LE(-1, 48);
            Button(btnRow.transform, "SIGN IN", Btn.Primary, 18, panel.DoSignIn, F.CondBold).LE(0, 48, 1f);
            Button(btnRow.transform, "CREATE", Btn.Secondary, 18, panel.DoCreate, F.CondBold).LE(0, 48, 1f);

            panel.signOut = Button(col.transform, "SIGN OUT (back to guest)", Btn.Quiet, 15, panel.DoSignOut, F.CondSemi);
            panel.signOut.LE(-1, 42);

            Spacer(col.transform);
            Label(col.transform, "Local account on this computer: your credits, rank and bought weapons are saved to it. Guests use a shared local profile.",
                F.Body, 12, Faint).Wrap().LE(-1, 34);

            root.gameObject.SetActive(false);
            root.SetAsLastSibling();
            return panel;
        }

        class AccountPanel : MonoBehaviour
        {
            public TextMeshProUGUI title, status;
            public TMP_InputField user, pass;
            public KitButton signOut;

            public void Open() { Refresh(); gameObject.SetActive(true); transform.SetAsLastSibling(); }
            public void Close() => gameObject.SetActive(false);

            void Update()
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            }

            void Refresh()
            {
                bool inAcc = AccountStore.IsSignedIn;
                title.text = inAcc ? AccountStore.Current.ToUpperInvariant() : "SIGN IN";
                status.text = "";
                signOut.gameObject.SetActive(inAcc);
            }

            public void DoSignIn()
            {
                string err = AccountStore.SignIn(user.text, pass.text);
                Done(err, "Signed in");
            }

            public void DoCreate()
            {
                string err = AccountStore.Create(user.text, pass.text);
                Done(err, "Account created");
            }

            public void DoSignOut()
            {
                AccountStore.SignOut();
                pass.text = "";
                Refresh();
            }

            void Done(string err, string ok)
            {
                if (err != null) { status.color = DangerSoft; status.text = err; return; }
                pass.text = "";
                status.color = Ready;
                status.text = ok;
                Refresh();
                Close();
            }
        }
    }
}
