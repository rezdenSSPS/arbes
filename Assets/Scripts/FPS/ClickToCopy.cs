using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

namespace AirsoftFPS
{
    // Put on a TMP text showing the join code: click it to copy the code to the
    // clipboard, with a brief "COPIED!" flash. Works when the cursor is free
    // (the lobby/waiting room, or while paused in-game).
    [RequireComponent(typeof(TMP_Text))]
    public class ClickToCopy : MonoBehaviour, IPointerClickHandler
    {
        TMP_Text label;
        bool flashing;

        void Awake()
        {
            label = GetComponent<TMP_Text>();
            label.raycastTarget = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(NetworkUI.JoinCode)) return;
            GUIUtility.systemCopyBuffer = NetworkUI.JoinCode;
            if (!flashing) StartCoroutine(Flash());
        }

        IEnumerator Flash()
        {
            flashing = true;
            string original = label.text;
            Color oc = label.color;
            label.text = "COPIED!";
            label.color = new Color(0.45f, 1f, 0.55f);
            yield return new WaitForSecondsRealtime(0.9f);
            label.color = oc;
            label.text = original;
            flashing = false;
        }
    }
}
