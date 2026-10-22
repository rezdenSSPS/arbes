using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AirsoftFPS
{
    // Hover / click sounds for UIKit widgets that aren't KitButtons (check rows, sliders). Reacts to the
    // pointer only, so settings refreshes that set values from code stay silent.
    public class UISound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        public string click = "ui_toggle";
        public bool hover = true;

        Selectable sel;
        Slider slider;
        bool held;
        float lastTick;

        void Awake()
        {
            sel = GetComponent<Selectable>();
            slider = GetComponent<Slider>();
            if (slider != null) slider.onValueChanged.AddListener(_ => Drag());
        }

        bool Interactable => sel == null || sel.IsInteractable();

        void Drag()
        {
            if (!held || Time.unscaledTime - lastTick < 0.06f) return;
            lastTick = Time.unscaledTime;
            AirsoftAudio.Play2D("ui_slider", 0.45f);
        }

        public void OnPointerEnter(PointerEventData e) { if (hover && Interactable) AirsoftAudio.UiHover(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (slider == null && Interactable && !string.IsNullOrEmpty(click)) AirsoftAudio.Play2D(click, 0.55f, AirsoftAudio.Bus.Ui, 0f, 0.03f);
        }

        public void OnPointerDown(PointerEventData e)
        {
            held = true;
            if (slider != null && Interactable) { lastTick = Time.unscaledTime; AirsoftAudio.Play2D("ui_slider", 0.45f); }
        }

        public void OnPointerUp(PointerEventData e) => held = false;
    }
}
