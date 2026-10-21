using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace AirsoftFPS
{
    public class HUDManager : MonoBehaviour
    {
        [Header("Health")]
        public Slider healthSlider;
        public Image healthFill;
        public TextMeshProUGUI healthText;

        [Header("Ammo")]
        public TextMeshProUGUI ammoText;
        public TextMeshProUGUI weaponNameText;

        [Header("Weapon Selector")]
        public Image[] weaponSlots;          // 10 slot backgrounds
        public TextMeshProUGUI[] weaponSlotLabels;

        [Header("Crosshair")]
        public RectTransform crosshairGroup;
        public Image[] crosshairLines;

        [Header("Hit Marker")]
        public CanvasGroup hitMarkerGroup;

        [Header("Death Screen")]
        public CanvasGroup deathOverlay;
        public TextMeshProUGUI deathText;

        [Header("Wave")]
        public TextMeshProUGUI waveText;

        [Header("Online Join Code")]
        public TextMeshProUGUI joinCodeText;

        [Header("Pause")]
        public GameObject pausePanel;

        float hitMarkerTimer;
        bool isPaused;
        bool wired;
        string _lastJoinCode;

        // Runtime-built mode UI (scoreboard + result banner), cloned from waveText
        TextMeshProUGUI scoreboardText;
        TextMeshProUGUI resultText;
        TextMeshProUGUI killFeedText;
        bool modeUIBuilt;
        bool killFeedSub;
        struct KillLine { public string text; public float expire; }
        readonly System.Collections.Generic.List<KillLine> killLines = new System.Collections.Generic.List<KillLine>();

        Health playerHealth;
        WeaponController weaponCtrl;

        public void Setup(Health health, WeaponController wc)
        {
            if (wired) return;
            playerHealth = health;
            weaponCtrl = wc;

            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged += UpdateHealth;
                playerHealth.OnDeath += ShowDeath;
                playerHealth.OnRespawn += HideDeath;
                UpdateHealth(playerHealth.currentHealth.Value, playerHealth.maxHealth);
            }

            if (weaponCtrl != null)
            {
                weaponCtrl.OnAmmoChanged += UpdateAmmo;
                weaponCtrl.OnHitMarker += ShowHitMarker;
                weaponCtrl.RefreshHUD();
            }

            wired = (playerHealth != null && weaponCtrl != null);
        }

        // Self-wire to the local player each frame until connected, so the HUD
        // never depends on an in-scene manager spawning at the right moment.
        void TryAutoWire()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return;
            var po = nm.LocalClient.PlayerObject;
            if (po == null) return;
            var h = po.GetComponent<Health>();
            var wc = po.GetComponent<WeaponController>();
            if (h != null && wc != null)
                Setup(h, wc);
        }

        void UpdateHealth(int current, int max)
        {
            if (healthSlider)
            {
                healthSlider.maxValue = max;
                healthSlider.value = current;
            }
            if (healthFill)
            {
                float pct = max > 0 ? (float)current / max : 0;
                healthFill.color = Color.Lerp(new Color(0.8f, 0.1f, 0.1f), new Color(0.1f, 0.8f, 0.2f), pct);
            }
            if (healthText)
                healthText.text = current.ToString();
        }

        void UpdateAmmo(string weaponName, int mag, int reserve, int slot)
        {
            if (string.IsNullOrEmpty(weaponName)) return;
            if (ammoText)
                ammoText.text = $"{mag} / {reserve}";
            if (weaponNameText)
                weaponNameText.text = $"[{slot}] {weaponName}";

            HighlightSlot(slot - 1);
        }

        static readonly Color SlotActive = new Color(1f, 0.55f, 0.15f, 0.95f);
        static readonly Color SlotIdle = new Color(0.12f, 0.12f, 0.15f, 0.6f);
        static readonly Color LabelActive = Color.white;
        static readonly Color LabelIdle = new Color(0.65f, 0.65f, 0.65f, 0.9f);

        void HighlightSlot(int activeIndex)
        {
            if (weaponSlots != null)
            {
                for (int i = 0; i < weaponSlots.Length; i++)
                {
                    if (weaponSlots[i] == null) continue;
                    weaponSlots[i].color = (i == activeIndex) ? SlotActive : SlotIdle;
                }
            }
            if (weaponSlotLabels != null)
            {
                for (int i = 0; i < weaponSlotLabels.Length; i++)
                {
                    if (weaponSlotLabels[i] == null) continue;
                    weaponSlotLabels[i].color = (i == activeIndex) ? LabelActive : LabelIdle;
                }
            }
        }

        void ShowHitMarker(Vector3 point)
        {
            hitMarkerTimer = 0.2f;
        }

        void ShowDeath()
        {
            if (deathOverlay) deathOverlay.alpha = 1f;
            if (deathText) deathText.text = "ELIMINATED\nRespawning...";
        }

        bool slotNamesSet;

        // Slot labels come from the weapon list (so the HUD always matches the guns).
        void SetSlotNames()
        {
            slotNamesSet = true;
            if (weaponSlots == null || weaponCtrl.weapons == null) return;
            for (int i = 0; i < weaponSlots.Length && i < weaponCtrl.weapons.Length; i++)
            {
                if (weaponSlots[i] == null) continue;
                var t = weaponSlots[i].transform.Find("Name");
                var tmp = t ? t.GetComponent<TextMeshProUGUI>() : null;
                if (tmp && !string.IsNullOrEmpty(weaponCtrl.weapons[i].shortName))
                    tmp.text = weaponCtrl.weapons[i].shortName;
            }
        }

        void HideDeath()
        {
            if (deathOverlay) deathOverlay.alpha = 0f;
        }

        void Update()
        {
            if (!wired) TryAutoWire();

            // Pull live ammo every frame (robust to spawn-order timing in builds).
            if (weaponCtrl != null && weaponCtrl.AmmoReady && !slotNamesSet)
                SetSlotNames();
            if (weaponCtrl != null && weaponCtrl.AmmoReady)
                UpdateAmmo(weaponCtrl.CurrentWeaponName, weaponCtrl.CurrentMag,
                           weaponCtrl.CurrentReserve, weaponCtrl.CurrentSlot);

            // Keep HP on screen even if the first health event was missed on spawn.
            if (playerHealth != null)
                UpdateHealth(playerHealth.currentHealth.Value, playerHealth.maxHealth);

            // Subscribe to the kill feed once MatchNet exists.
            if (!killFeedSub && MatchNet.Instance != null)
            {
                MatchNet.Instance.OnKillFeed += HandleKill;
                killFeedSub = true;
            }

            // Hit marker fade
            if (hitMarkerTimer > 0)
            {
                hitMarkerTimer -= Time.unscaledDeltaTime;
                if (hitMarkerGroup) hitMarkerGroup.alpha = hitMarkerTimer > 0 ? 1f : 0f;
            }

            // Pause
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                TogglePause();

            UpdateModeUI();

            if (joinCodeText)
            {
                string jc = NetworkUI.JoinCode;
                if (jc != _lastJoinCode)
                {
                    _lastJoinCode = jc;
                    joinCodeText.text = string.IsNullOrEmpty(jc) ? "" : $"ONLINE CODE   {jc}   (click to copy)";
                }
            }
        }

        void BuildModeUI()
        {
            if (modeUIBuilt) return;
            modeUIBuilt = true;
            var src = waveText != null ? waveText : ammoText;
            if (src == null) return;

            var sgo = Instantiate(src.gameObject, src.transform.parent);
            sgo.name = "ScoreboardText";
            scoreboardText = sgo.GetComponent<TextMeshProUGUI>();
            if (scoreboardText != null)
            {
                scoreboardText.fontSize = Mathf.Max(22f, src.fontSize);
                scoreboardText.alignment = TextAlignmentOptions.Top;
                scoreboardText.color = Color.white;
                scoreboardText.richText = true;
                scoreboardText.raycastTarget = false;
                var rt = scoreboardText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(700f, 500f);
                scoreboardText.gameObject.SetActive(false);
            }

            var rgo = Instantiate(src.gameObject, src.transform.parent);
            rgo.name = "ResultText";
            resultText = rgo.GetComponent<TextMeshProUGUI>();
            if (resultText != null)
            {
                resultText.fontSize = Mathf.Max(40f, src.fontSize * 1.6f);
                resultText.alignment = TextAlignmentOptions.Center;
                resultText.color = new Color(1f, 0.55f, 0.15f);
                resultText.richText = true;
                resultText.raycastTarget = false;
                var rt = resultText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 120f);
                rt.sizeDelta = new Vector2(900f, 300f);
                resultText.gameObject.SetActive(false);
            }

            var kgo = Instantiate(src.gameObject, src.transform.parent);
            kgo.name = "KillFeedText";
            killFeedText = kgo.GetComponent<TextMeshProUGUI>();
            if (killFeedText != null)
            {
                killFeedText.fontSize = Mathf.Max(20f, src.fontSize * 0.8f);
                killFeedText.alignment = TextAlignmentOptions.TopRight;
                killFeedText.color = Color.white;
                killFeedText.richText = true;
                killFeedText.raycastTarget = false;
                killFeedText.enableAutoSizing = false;
                var rt = killFeedText.rectTransform;
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-24f, -24f);
                rt.sizeDelta = new Vector2(620f, 240f);
                killFeedText.text = "";
            }
        }

        void HandleKill(string killer, string victim, string weapon)
        {
            string line;
            if (string.IsNullOrEmpty(killer))
                line = $"<color=#FF6B6B>{victim}</color>  was eliminated";
            else
            {
                string w = string.IsNullOrEmpty(weapon) ? "" : $"<color=#E0922F>{weapon}</color>   ";
                line = $"<color=#EDEDED>{killer}</color>   {w}<color=#FF6B6B>{victim}</color>";
            }
            killLines.Add(new KillLine { text = line, expire = Time.unscaledTime + 5f });
            if (killLines.Count > 5) killLines.RemoveAt(0);
        }

        void UpdateModeUI()
        {
            BuildModeUI();
            var mnet = MatchNet.Instance;

            var mode = mnet != null ? mnet.Mode : MatchNet.GameModeType.Coop;

            // Top banner per mode.
            if (waveText != null)
            {
                if (mode == MatchNet.GameModeType.FreeForAll) waveText.text = "FREE-FOR-ALL";
                else if (mode == MatchNet.GameModeType.TeamDeathmatch)
                    waveText.text = $"TEAM DEATHMATCH    <color=#FF6B6B>{mnet.TeamScore(1)}</color> : <color=#6BA8FF>{mnet.TeamScore(2)}</color>";
                else if (mode == MatchNet.GameModeType.GunGame) waveText.text = "GUN GAME";
                else
                {
                    var spawner = FindAnyObjectByType<EnemySpawner>();
                    if (spawner != null) waveText.text = $"WAVE {spawner.CurrentWave}";
                }
            }

            // Scoreboard while holding Tab.
            var kb = Keyboard.current;
            bool showScores = kb != null && kb.tabKey.isPressed && mnet != null;
            if (scoreboardText != null)
            {
                if (showScores)
                {
                    var list = new System.Collections.Generic.List<MatchNet.Entry>(mnet.Roster);
                    var sb = new System.Text.StringBuilder();
                    if (mode == MatchNet.GameModeType.TeamDeathmatch)
                    {
                        sb.AppendLine($"<b>TEAM DEATHMATCH</b>   (first team to {MatchNet.TDM_KILL_TARGET})");
                        sb.AppendLine($"<color=#FF6B6B>RED  {mnet.TeamScore(1)}</color>      <color=#6BA8FF>BLUE  {mnet.TeamScore(2)}</color>");
                        sb.AppendLine("");
                        list.Sort((a, b) => a.team != b.team ? a.team.CompareTo(b.team) : b.kills.CompareTo(a.kills));
                        foreach (var e in list)
                            sb.AppendLine($"<color={(e.team == 1 ? "#FF6B6B" : "#6BA8FF")}>{e.name}</color>      {e.kills} / {e.deaths}");
                    }
                    else if (mode == MatchNet.GameModeType.GunGame)
                    {
                        sb.AppendLine($"<b>GUN GAME</b>   (finish all {MatchNet.GUNGAME_WEAPONS} weapons)");
                        sb.AppendLine("");
                        list.Sort((a, b) => b.kills.CompareTo(a.kills));
                        foreach (var e in list)
                            sb.AppendLine($"{e.name}      weapon {System.Math.Min(e.kills + 1, MatchNet.GUNGAME_WEAPONS)} / {MatchNet.GUNGAME_WEAPONS}");
                    }
                    else if (mode == MatchNet.GameModeType.FreeForAll)
                    {
                        sb.AppendLine($"<b>SCOREBOARD</b>   (first to {MatchNet.FFA_KILL_TARGET})");
                        sb.AppendLine("");
                        list.Sort((a, b) => b.kills.CompareTo(a.kills));
                        foreach (var e in list)
                            sb.AppendLine($"{e.name}      {e.kills} kills / {e.deaths} deaths");
                    }
                    else
                    {
                        sb.AppendLine("<b>PLAYERS</b>");
                        sb.AppendLine("");
                        foreach (var e in list)
                            sb.AppendLine($"{e.name}      {e.deaths} downs");
                    }
                    scoreboardText.text = sb.ToString();
                    if (!scoreboardText.gameObject.activeSelf) scoreboardText.gameObject.SetActive(true);
                }
                else if (scoreboardText.gameObject.activeSelf)
                    scoreboardText.gameObject.SetActive(false);
            }

            // Winner banner when the match ends.
            if (resultText != null && mnet != null)
            {
                bool over = mnet.CurrentPhase == MatchNet.Phase.Results;
                if (over)
                {
                    string title;
                    if (mnet.WinnerTeam == 1) title = "<color=#FF6B6B>RED TEAM WINS</color>";
                    else if (mnet.WinnerTeam == 2) title = "<color=#6BA8FF>BLUE TEAM WINS</color>";
                    else title = (mnet.WinnerClientId == ulong.MaxValue ? "Nobody" : mnet.NameFor(mnet.WinnerClientId)) + " WINS";
                    resultText.text = $"{title}\n<size=50%>returning to lobby...</size>";
                    if (!resultText.gameObject.activeSelf) resultText.gameObject.SetActive(true);
                }
                else if (resultText.gameObject.activeSelf)
                    resultText.gameObject.SetActive(false);
            }

            // Kill feed (top-right), newest on top, entries fade after 5s.
            if (killFeedText != null)
            {
                for (int i = killLines.Count - 1; i >= 0; i--)
                    if (Time.unscaledTime >= killLines[i].expire) killLines.RemoveAt(i);
                var sb = new System.Text.StringBuilder();
                for (int i = killLines.Count - 1; i >= 0; i--) sb.AppendLine(killLines[i].text);
                killFeedText.text = sb.ToString();
            }
        }

        public void TogglePause()
        {
            isPaused = !isPaused;
            if (pausePanel) pausePanel.SetActive(isPaused);
            Time.timeScale = isPaused ? 0 : 1;
            Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isPaused;
        }

        public void ResumeGame() => TogglePause();

        public void QuitToMenu()
        {
            NetworkUI.JoinCode = "";
            Time.timeScale = 1;
            Unity.Netcode.NetworkManager.Singleton?.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("AirsoftLobby");
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged -= UpdateHealth;
                playerHealth.OnDeath -= ShowDeath;
                playerHealth.OnRespawn -= HideDeath;
            }
            if (weaponCtrl != null)
            {
                weaponCtrl.OnAmmoChanged -= UpdateAmmo;
                weaponCtrl.OnHitMarker -= ShowHitMarker;
            }
            if (killFeedSub && MatchNet.Instance != null)
                MatchNet.Instance.OnKillFeed -= HandleKill;
        }
    }
}
