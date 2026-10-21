using UnityEngine;
using UnityEngine.UI;

namespace AirsoftFPS
{
    public class WorldHealthBar : MonoBehaviour
    {
        public Slider slider;
        public Image fillImage;
        public Transform target;
        public Vector3 offset = new Vector3(0, 2.3f, 0);

        Health health;
        Camera mainCam;
        bool graphicsShown = true;

        void Start()
        {
            health = target ? target.GetComponent<Health>() : GetComponentInParent<Health>();

            // The prefab's fillImage points at the background; the real fill graphic
            // is the Image on the slider's Fill Rect. Use that so coloring is visible.
            if (slider != null && slider.fillRect != null)
            {
                var realFill = slider.fillRect.GetComponent<Image>();
                if (realFill != null) fillImage = realFill;
            }

            if (health != null)
            {
                health.OnHealthChanged += UpdateBar;
                UpdateBar(health.currentHealth.Value, health.maxHealth);
            }
        }

        void UpdateBar(int current, int max)
        {
            if (slider)
            {
                slider.maxValue = max;
                slider.value = current;
            }

            if (fillImage)
            {
                float pct = max > 0 ? (float)current / max : 0;
                fillImage.color = Color.Lerp(Color.red, Color.green, pct);
            }

            // Hide when dead
            gameObject.SetActive(current > 0);
        }

        void LateUpdate()
        {
            // Refresh from live health every frame (robust to networked event timing).
            if (health != null)
            {
                int cur = health.currentHealth.Value;
                int max = health.maxHealth;
                if (slider) { slider.maxValue = max; slider.value = cur; }
                if (fillImage)
                    fillImage.color = Color.Lerp(Color.red, Color.green, max > 0 ? (float)cur / max : 0f);
            }

            // Settings > Gameplay > Show enemy health bars
            bool show = GameSettings.ShowHealthBars;
            if (show != graphicsShown)
            {
                graphicsShown = show;
                foreach (var g in GetComponentsInChildren<Graphic>(true)) g.enabled = show;
            }

            if (mainCam == null) mainCam = Camera.main;
            if (mainCam == null) return;

            if (target)
                transform.position = target.position + offset;

            transform.LookAt(transform.position + mainCam.transform.forward);
        }

        void OnDestroy()
        {
            if (health != null)
                health.OnHealthChanged -= UpdateBar;
        }
    }
}
