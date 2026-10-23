using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // In-match UI, built in code with UIKit: HUD (health, ammo, weapon slots,
    // crosshair, hit marker, kill feed, per-mode top widget, minimap), scoreboard (Tab),
    // killcam + death screen, pause (Esc) and the end-of-match results.
    public class HUDManager : MonoBehaviour
    {
        // Old scene-wired HUD. Kept so existing scenes still load; hidden at runtime.
        [Header("Legacy HUD (hidden at runtime)")]
        public Slider healthSlider;
        public Image healthFill;
        public TextMeshProUGUI healthText;
        public TextMeshProUGUI ammoText;
        public TextMeshProUGUI weaponNameText;
        public Image[] weaponSlots;
        public TextMeshProUGUI[] weaponSlotLabels;
        public RectTransform crosshairGroup;
        public Image[] crosshairLines;
        public CanvasGroup hitMarkerGroup;
        public CanvasGroup deathOverlay;
        public TextMeshProUGUI deathText;
        public TextMeshProUGUI waveText;
        public TextMeshProUGUI joinCodeText;
        public GameObject pausePanel;

        // True while a menu (pause / results / settings) owns the mouse and keyboard.
        public static bool InputBlocked { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => InputBlocked = false;

        Health playerHealth;
        WeaponController weaponCtrl;
        bool wired;
        bool isPaused;
        bool killFeedSub;
        float hitMarkerTimer;
        float deathTime = -1f;
        string lastKiller = "", lastKillerWeapon = "";
        int lastKillerTeam;
        float resultsStart = -1f;
        float fpsSmoothed;
        float enemyCountTimer;

        struct KillLine { public string killer, weapon, victim; public int killerTeam, victimTeam; public bool me; public float expire; }
        readonly List<KillLine> killLines = new List<KillLine>();
        bool killFeedDirty;

        // ───────── built UI ─────────
        Canvas canvas;
        GameObject hudRoot, scoreboard, deathScreen, killcamScreen, pauseScreen, resultsScreen;
        GameObject hitScreen;   // Milsim HIT overlay
        SettingsScreen settings;
        KillCam killCam;

        // HUD widgets
        GameObject codeChip;
        TextMeshProUGUI codeValue, codeHint, fpsText;
        GameObject tdmWidget, ffaWidget, ggWidget, coopWidget;
        TextMeshProUGUI tdmRed, tdmBlue, tdmTarget, tdmYou;
        TextMeshProUGUI tdmTargetCaption;   // "FIRST TO" (Medic: "TICKETS")
        TextMeshProUGUI ffaYou, ffaLeader, ffaLeaderScore, ffaTarget;
        TextMeshProUGUI ggNext;
        readonly List<Image> ggSteps = new List<Image>();
        TextMeshProUGUI coopWave, coopLeftNum;
        GameObject clockChip;
        Image clockFrame;
        TextMeshProUGUI clockCaption, clockText;
        readonly List<FeedRow> feedRows = new List<FeedRow>();
        RectTransform crosshair;
        Image[] crossArms;
        int crossSize = -1; int crossColor = -1;
        GameObject hitMarker;
        Image vignette;
        TextMeshProUGUI hpValue, hpUnit;
        Image hpFill;
        readonly List<SlotView> slotViews = new List<SlotView>();
        TextMeshProUGUI ammoWeapon, ammoMag, ammoReserve;
        GameObject reloadHint;
        TextMeshProUGUI reloadText;

        // scoreboard
        TextMeshProUGUI sbTitle, sbGoal, sbFooterLeft, sbFooterRight;
        GameObject sbTeams, sbList;
        readonly TeamView[] sbTeamViews = new TeamView[2];
        TextMeshProUGUI sbHeadProgress, sbHeadA, sbHeadB;
        readonly List<ListRow> sbRows = new List<ListRow>();
        float sbTimer;

        // Milsim HIT overlay
        TextMeshProUGUI hitBy, hitInstr, hitDist, hitCount;
        RectTransform hitArrow;
        GameObject hitWalk, hitRespawn;
        Image hitFill;

        // death
        Image deathSwatch;
        TextMeshProUGUI deathBy, deathHp, deathSeconds;
        Image deathFill;
        TextMeshProUGUI deathRespawnIn, deathRespawnNote;   // round modes rewrite these ("Out until the next round")

        // killcam overlay
        Image kcSwatch, kcFill;
        TextMeshProUGUI kcBy, kcHp, kcSeconds;
        GameObject kcHpBlock;

        // pause
        TextMeshProUGUI pauseMode, pauseScore, pauseMeta;

        // results
        Image resBanner;
        TextMeshProUGUI resMode, resWinner, resSub, resCountdown, resWaiting;
        Image resFill;
        KitButton resBack;
        readonly List<PodiumView> podium = new List<PodiumView>();
        readonly List<(TextMeshProUGUI v, TextMeshProUGUI k)> resStats = new();

        class FeedRow { public GameObject go; public Image bg, frame; public TextMeshProUGUI killer, weapon, victim; }
        class SlotView { public Image bg, frame; public TextMeshProUGUI key, name; }
        class TeamView { public Image header; public TextMeshProUGUI name, score, headK, headD, headO; public readonly List<(GameObject go, Image bg, TextMeshProUGUI n, TextMeshProUGUI k, TextMeshProUGUI d)> rows = new(); public readonly List<TextMeshProUGUI> obj = new(); }
        class ListRow { public GameObject go; public Image bg, bar; public TextMeshProUGUI rank, name, progress, a, b; public GameObject barBack; }
        class PodiumView { public RectTransform rt; public Image bg, frame; public TextMeshProUGUI rank, name, stat; }

        // ════════════════════════ wiring ════════════════════════
        public void Setup(Health health, WeaponController wc)
        {
            if (wired) return;
            playerHealth = health;
            weaponCtrl = wc;

            if (playerHealth != null)
            {
                playerHealth.OnDeath += ShowDeath;
                playerHealth.OnRespawn += HideDeath;
            }
            if (weaponCtrl != null)
            {
                weaponCtrl.OnHitMarker += ShowHitMarker;
                weaponCtrl.RefreshHUD();
            }
            wired = (playerHealth != null && weaponCtrl != null);
        }

        // Self-wire to the local player each frame until connected, so the HUD
        // never depends on an in-scene manager spawning at the right moment.
        void TryAutoWire()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return;
            var po = nm.LocalClient.PlayerObject;
            if (po == null) return;
            var h = po.GetComponent<Health>();
            var wc = po.GetComponent<WeaponController>();
            if (h != null && wc != null)
                Setup(h, wc);
        }

        void Start()
        {
            // Hide the old scene-built HUD (this component sits on the old canvas).
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            InputBlocked = false;
            BuildUI();
        }

        void ShowHitMarker(Vector3 point) => hitMarkerTimer = 0.2f;

        void ShowDeath()
        {
            deathTime = Time.unscaledTime;
        }

        void HideDeath() => deathTime = -1f;

        void HandleKill(string killer, string victim, string weapon)
        {
            var mnet = MatchNet.Instance;
            var nm = NetworkManager.Singleton;
            string me = mnet != null && nm != null ? mnet.NameFor(nm.LocalClientId) : "";
            var line = new KillLine
            {
                killer = killer, victim = victim, weapon = weapon ?? "",
                killerTeam = TeamByName(killer), victimTeam = TeamByName(victim),
                me = !string.IsNullOrEmpty(me) && (killer == me || victim == me),
                expire = Time.unscaledTime + 5f
            };
            killLines.Add(line);
            if (killLines.Count > 5) killLines.RemoveAt(0);
            killFeedDirty = true;

            if (!string.IsNullOrEmpty(me) && victim == me)
            {
                lastKiller = killer;
                lastKillerWeapon = weapon ?? "";
                lastKillerTeam = line.killerTeam;
            }
        }

        static int TeamByName(string name)
        {
            var mnet = MatchNet.Instance;
            if (mnet == null || string.IsNullOrEmpty(name) || !mnet.TeamMode) return 0;
            foreach (var e in mnet.Roster) if (e.name == name) return e.team;
            return 0;
        }

        // ════════════════════════ frame update ════════════════════════
        void Update()
        {
            if (!wired) TryAutoWire();
            if (!killFeedSub && MatchNet.Instance != null)
            {
                MatchNet.Instance.OnKillFeed += HandleKill;
                killFeedSub = true;
            }

            // Host left / connection dropped during the match: back to the menu (NetworkUI only handles
            // this in the lobby scene).
            var nmL = NetworkManager.Singleton;
            if (nmL != null && (nmL.IsConnectedClient || nmL.IsServer)) { sawConnected = true; wasHost = nmL.IsServer; }
            else if (sawConnected && !leaving && (nmL == null || (!nmL.IsListening && !nmL.ShutdownInProgress)))
            {
                NetworkUI.PendingMenuHint = wasHost ? "The match session ended." : "The host left the match.";
                QuitToMenu();
                return;
            }

            var mnet = MatchNet.Instance;
            // ReturnToLobby resets the phase and despawns the avatars (this scene's only cameras) before the
            // lobby has loaded: keep the opaque results screen up until this scene unloads.
            if (resultsStart >= 0f && mnet != null && mnet.CurrentPhase == MatchNet.Phase.Lobby) return;
            bool results = mnet != null && mnet.CurrentPhase == MatchNet.Phase.Results;
            if (results && resultsStart < 0f) { resultsStart = Time.unscaledTime; if (isPaused) TogglePause(); }
            if (!results) resultsStart = -1f;

            // Esc: settings closes itself; otherwise toggle pause (not on the results screen)
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !results
                && !settings.IsOpen && SettingsScreen.LastEscCloseFrame != Time.frameCount)
                TogglePause();

            bool dead = playerHealth != null ? playerHealth.isDead.Value : deathTime >= 0f;
            if (dead && deathTime < 0f) deathTime = Time.unscaledTime;
            if (!dead) deathTime = -1f;
            bool showScores = kb != null && kb.tabKey.isPressed && mnet != null && !results && !isPaused;
            bool killcam = dead && killCam != null && killCam.Playing;
            bool hit = playerHealth != null && playerHealth.IsOut;
            // Medic: a downed / out-for-good player gets the mode's own overlay instead of the Milsim walk-back
            bool heldOut = hit && NetworkManager.Singleton != null && ObjectiveMode.HoldsHit(NetworkManager.Singleton.LocalClientId);

            // (no HUD before the own avatar, and with it the only camera, has spawned)
            hudRoot.SetActive(wired && !results && !isPaused && !killcam);
            resultsScreen.SetActive(results);
            pauseScreen.SetActive(isPaused && !results);
            deathScreen.SetActive(dead && !killcam && !results && !isPaused && !showScores
                                  && !ObjectiveMode.LocalSpectating);   // round modes: watching a teammate
            killcamScreen.SetActive(killcam && !results && !isPaused && !showScores);
            scoreboard.SetActive(showScores);
            hitScreen.SetActive(hit && !heldOut && !results && !isPaused && !showScores);

            InputBlocked = isPaused || results || settings.IsOpen;
            bool wantCursor = InputBlocked;
            if (wantCursor && Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (!wantCursor && wired && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

            if (!results) UpdateHud(mnet, dead || hit);
            if (showScores) { sbTimer -= Time.unscaledDeltaTime; if (sbTimer <= 0f) { sbTimer = 0.2f; UpdateScoreboard(mnet); } }
            else sbTimer = 0f;
            if (dead && !results) UpdateDeath(killcam);
            if (hit && !heldOut && !results) UpdateHit(mnet);
            if (isPaused) UpdatePause(mnet);
            if (results) UpdateResults(mnet);
        }

        void UpdateHud(MatchNet mnet, bool dead)
        {
            var mode = mnet != null ? mnet.Mode : MatchNet.GameModeType.Coop;
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : 0;

            // join code chip
            string jc = NetworkUI.JoinCode;
            codeChip.SetActive(!string.IsNullOrEmpty(jc));
            codeValue.text = jc;

            // fps
            fpsText.gameObject.SetActive(GameSettings.ShowFps);
            if (GameSettings.ShowFps)
            {
                float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                fpsSmoothed = Mathf.Lerp(fpsSmoothed <= 0 ? 1f / dt : fpsSmoothed, 1f / dt, 0.1f);
                fpsText.text = Mathf.RoundToInt(fpsSmoothed) + " FPS";
            }

            // top widget per mode
            tdmWidget.SetActive(MatchNet.IsTeamMode(mode));
            ffaWidget.SetActive(mode == MatchNet.GameModeType.FreeForAll);
            ggWidget.SetActive(mode == MatchNet.GameModeType.GunGame);
            coopWidget.SetActive(mode == MatchNet.GameModeType.Coop);

            // match clock (last 30 s in orange, last 10 s in danger red)
            float timeLeft = mnet != null ? mnet.TimeLeft : -1f;
            clockChip.SetActive(timeLeft >= 0f);
            if (timeLeft >= 0f)
            {
                clockText.text = MatchNet.FormatClock(timeLeft);
                bool late = timeLeft <= 30f;
                Color warn = ClockColor(timeLeft);
                clockText.color = warn;
                clockCaption.color = late ? warn : Muted;
                clockFrame.enabled = late;
                clockFrame.color = warn;
            }

            if (mnet != null)
            {
                if (MatchNet.IsTeamMode(mode))
                {
                    tdmRed.text = mnet.TeamScore(1).ToString();
                    tdmBlue.text = mnet.TeamScore(2).ToString();
                    bool cap = ObjectiveMode.TryScoreCaption(out string capText, out string capValue);   // Medic: tickets
                    tdmTargetCaption.text = cap ? capText : "FIRST TO";
                    tdmTarget.text = cap ? capValue : mnet.TeamTarget.ToString();
                    int team = mnet.TeamOf(me);
                    tdmYou.text = team == 0 ? "" : $"YOU ARE <color={Html(TeamColor(team))}>{(team == 1 ? "RED" : "BLUE")}</color>";
                    tdmYou.transform.parent.gameObject.SetActive(team != 0);
                }
                else if (mode == MatchNet.GameModeType.FreeForAll)
                {
                    int myKills = 0, best = -1; string leader = "";
                    foreach (var e in mnet.Roster)
                    {
                        if (e.clientId == me) myKills = e.kills;
                        if (e.kills > best) { best = e.kills; leader = e.name; }
                    }
                    ffaYou.text = myKills.ToString();
                    ffaLeader.text = "LEADER  " + leader;
                    ffaLeaderScore.text = Mathf.Max(0, best).ToString();
                    ffaTarget.text = "/ " + MatchNet.FFA_KILL_TARGET;
                }
                else if (mode == MatchNet.GameModeType.GunGame)
                {
                    int lvl = mnet.GunLevelOf(me);
                    for (int i = 0; i < ggSteps.Count; i++)
                        ggSteps[i].color = i < lvl ? Fg : i == lvl ? Accent : Line;
                    string next = "final weapon";
                    if (weaponCtrl != null && weaponCtrl.weapons != null && lvl + 1 < weaponCtrl.weapons.Length)
                        next = weaponCtrl.weapons[lvl + 1].weaponName;
                    ggNext.text = $"next: <color={Html(Fg)}>{next}</color>";
                }
                else
                {
                    coopWave.text = "WAVE " + (MatchNet.Instance != null ? MatchNet.Instance.CoopWave : 1);
                    enemyCountTimer -= Time.unscaledDeltaTime;
                    if (enemyCountTimer <= 0f)
                    {
                        enemyCountTimer = 0.5f;
                        int alive = 0;
                        foreach (var ai in FindObjectsByType<EnemyAI>())
                        {
                            var h = ai.GetComponent<Health>();
                            if (h == null || !h.isDead.Value) alive++;
                        }
                        coopLeftNum.text = alive.ToString();
                    }
                }
            }

            // kill feed
            for (int i = killLines.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= killLines[i].expire) { killLines.RemoveAt(i); killFeedDirty = true; }
            if (killFeedDirty) RefreshKillFeed(mode);

            // crosshair + hit marker
            if (crossSize != GameSettings.CrosshairSize || crossColor != GameSettings.CrosshairColor)
            {
                crossSize = GameSettings.CrosshairSize; crossColor = GameSettings.CrosshairColor;
                SettingsScreen.LayoutCrosshair(crossArms, crossSize, GameSettings.CrosshairTint);
            }
            bool aimingScope = weaponCtrl != null && weaponCtrl.IsAiming;
            crosshair.gameObject.SetActive(!dead && !aimingScope);
            if (hitMarkerTimer > 0) hitMarkerTimer -= Time.unscaledDeltaTime;
            hitMarker.SetActive(hitMarkerTimer > 0 && GameSettings.ShowHitMarker && !dead);

            // health
            int hp = playerHealth != null ? playerHealth.currentHealth.Value : 100;
            int max = playerHealth != null ? Mathf.Max(1, playerHealth.maxHealth) : 100;
            bool low = hp > 0 && hp <= max * 0.25f;
            hpValue.text = hp.ToString();
            hpValue.color = hpUnit.color = low ? Hex("#FFD2CC") : Fg;
            hpFill.color = low ? Danger : Fg;
            SetBar(hpFill, hp / (float)max);
            if (MatchNet.IsHitRuleMode(mode))   // Milsim, Medic
            {
                // no HP in Milsim: you're either in the game or HIT
                bool out_ = playerHealth != null && playerHealth.IsOut;
                hpValue.text = out_ ? "HIT" : "IN";
                hpUnit.text = "";
                hpValue.color = out_ ? Danger : Fg;
                hpFill.color = out_ ? Danger : Fg;
                SetBar(hpFill, out_ ? 0f : 1f);
                low = false;
            }
            else hpUnit.text = "HP";
            vignette.gameObject.SetActive(low && !dead);
            if (low) vignette.color = A(Hex("#C8281E"), 0.4f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f));

            // weapons + ammo
            if (weaponCtrl != null && weaponCtrl.AmmoReady)
            {
                int active = weaponCtrl.CurrentSlot - 1;
                bool gg = mode == MatchNet.GameModeType.GunGame;
                var ws = weaponCtrl.weapons;
                for (int i = 0; i < slotViews.Count; i++)
                {
                    var sv = slotViews[i];
                    bool on = i == active;
                    bool locked = gg && !on;
                    string shortName = ws != null && i < ws.Length ? (string.IsNullOrEmpty(ws[i].shortName) ? ws[i].weaponName : ws[i].shortName) : "";
                    sv.name.text = locked ? "—" : shortName;
                    sv.bg.color = on ? Accent : locked ? Scrim(0.35f) : Scrim(0.72f);
                    sv.key.color = sv.name.color = on ? Ground : locked ? Faint : Fg;
                    sv.frame.enabled = !on;
                }
                var w = weaponCtrl.CurrentWeapon;
                int mag = weaponCtrl.CurrentMag;
                ammoWeapon.text = $"[{(active == 9 ? 0 : active + 1)}] {weaponCtrl.CurrentWeaponName}";
                ammoMag.text = mag.ToString();
                ammoReserve.text = "/ " + weaponCtrl.CurrentReserve;
                bool lowAmmo = w != null && mag <= Mathf.Max(1, w.magSize / 4);
                ammoMag.color = lowAmmo ? Accent : Fg;
                bool reloading = weaponCtrl.IsReloading;
                reloadHint.SetActive(lowAmmo || reloading);
                reloadText.text = reloading ? "RELOADING…" : "RELOAD";
            }
        }

        void RefreshKillFeed(MatchNet.GameModeType mode)
        {
            killFeedDirty = false;
            for (int i = 0; i < feedRows.Count; i++)
            {
                var row = feedRows[i];
                int li = killLines.Count - 1 - i;   // newest on top
                if (li < 0) { row.go.SetActive(false); continue; }
                var k = killLines[li];
                row.go.SetActive(true);
                bool envDeath = string.IsNullOrEmpty(k.killer);
                row.killer.gameObject.SetActive(!envDeath);
                bool milsim = MatchNet.IsHitRuleMode(mode);   // Milsim, Medic
                row.weapon.gameObject.SetActive(!envDeath && (milsim || !string.IsNullOrEmpty(k.weapon)));
                row.killer.text = k.killer;
                row.killer.color = TeamColor(k.killerTeam);
                // Milsim: "X hit Y" (weapon small after it)
                row.weapon.text = !milsim ? k.weapon : string.IsNullOrEmpty(k.weapon) ? "hit"
                    : $"hit <size=80%><color={Html(Muted)}>{k.weapon}</color></size>";
                row.victim.text = envDeath ? k.victim + (milsim ? " is hit" : " was eliminated") : k.victim;
                row.victim.color = envDeath ? DangerSoft : (k.victimTeam != 0 ? TeamColor(k.victimTeam) : Fg);
                row.bg.color = k.me ? A(Accent, 0.22f) : Scrim(0.72f);
                row.frame.enabled = k.me;
            }
        }

        void UpdateScoreboard(MatchNet mnet)
        {
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : 0;
            var mode = mnet.Mode;
            sbTitle.text = ModeName(mode).ToUpperInvariant();
            float timeLeft = mnet.TimeLeft;
            sbGoal.text = Goal(mode) + (timeLeft >= 0f
                ? $"  ·  <color={Html(ClockColor(timeLeft))}>{MatchNet.FormatClock(timeLeft)}</color> left" : "");
            string where = !string.IsNullOrEmpty(NetworkUI.JoinCode) ? "ONLINE · " + NetworkUI.JoinCode : "LAN";
            sbFooterLeft.text = MapCatalog.CurrentDisplayName + " arena · " + where;
            sbFooterRight.text = mnet.Roster.Count + " / 8 players";

            var list = new List<MatchNet.Entry>(mnet.Roster);
            bool tdm = MatchNet.IsTeamMode(mode);
            bool milsimSb = MatchNet.IsHitRuleMode(mode);   // Milsim, Medic: hits instead of kills
            sbTeams.SetActive(tdm);
            sbList.SetActive(!tdm);
            if (tdm)
            {
                list.Sort((a, b) => b.kills.CompareTo(a.kills));
                for (int t = 0; t < 2; t++)
                {
                    var tv = sbTeamViews[t];
                    tv.score.text = mnet.TeamScore(t + 1).ToString();
                    tv.headK.text = milsimSb ? "HITS" : "KILLS";
                    tv.headD.text = milsimSb ? "OUT" : "DEATHS";
                    bool objSb = ObjectiveMode.HasObjective(mode);   // CTF / Domination: objective column
                    tv.headO.text = objSb ? ObjectiveMode.PointsLabelFor(mode) : "";
                    int r = 0;
                    foreach (var e in list)
                    {
                        if (e.team != t + 1 || r >= tv.rows.Count) continue;
                        var row = tv.rows[r++];
                        row.go.SetActive(true);
                        row.n.text = (e.clientId == me ? e.name + " (you)" : e.name) + ObjectiveMode.TagFor(e.clientId);   // Medic: +MEDIC / DOWN
                        row.k.text = e.kills.ToString();
                        row.d.text = e.deaths.ToString();
                        row.bg.color = e.clientId == me ? A(Accent, 0.16f) : Color.clear;
                        tv.obj[r - 1].text = objSb ? ObjectiveMode.PointsOf(e.clientId).ToString() : "";
                        row.n.rectTransform.offsetMax = new Vector2(objSb ? -194 : -134, 0);
                    }
                    for (; r < tv.rows.Count; r++) tv.rows[r].go.SetActive(false);
                }
                return;
            }

            bool coop = mode == MatchNet.GameModeType.Coop;
            if (coop) list.Sort((a, b) => a.deaths.CompareTo(b.deaths));
            else list.Sort((a, b) => b.kills.CompareTo(a.kills));
            sbHeadProgress.text = mode == MatchNet.GameModeType.GunGame ? "CURRENT WEAPON" : coop ? "" : "PROGRESS";
            sbHeadA.text = coop ? "" : "KILLS";
            sbHeadB.text = coop ? "DOWNS" : "DEATHS";
            for (int i = 0; i < sbRows.Count; i++)
            {
                var row = sbRows[i];
                if (i >= list.Count) { row.go.SetActive(false); continue; }
                var e = list[i];
                bool you = e.clientId == me;
                row.go.SetActive(true);
                row.rank.text = (i + 1).ToString();
                row.rank.color = i == 0 ? Accent : Muted;
                row.name.text = you ? e.name + " (you)" : e.name;
                row.bg.color = you ? A(Accent, 0.16f) : Color.clear;
                row.barBack.SetActive(!coop);
                row.progress.gameObject.SetActive(!coop);
                row.bar.color = you ? Accent : Fg;
                if (mode == MatchNet.GameModeType.GunGame)
                {
                    int lvl = Mathf.Min(e.kills, MatchNet.GUNGAME_WEAPONS - 1);
                    SetBar(row.bar, e.kills / (float)MatchNet.GUNGAME_WEAPONS);
                    row.progress.text = WeaponNameAt(lvl);
                }
                else if (!coop)
                {
                    SetBar(row.bar, e.kills / (float)MatchNet.FFA_KILL_TARGET);
                    row.progress.text = e.kills + " / " + MatchNet.FFA_KILL_TARGET;
                }
                row.a.text = coop ? "" : e.kills.ToString();
                row.b.text = e.deaths.ToString();
            }
        }

        string WeaponNameAt(int i)
        {
            var ws = weaponCtrl != null ? weaponCtrl.weapons : null;
            if (ws != null && i >= 0 && i < ws.Length) return ws[i].weaponName;
            return "Weapon " + (i + 1);
        }

        static Color ClockColor(float timeLeft) => timeLeft <= 10f ? Danger : timeLeft <= 30f ? Accent : Fg;

        static string Goal(MatchNet.GameModeType m) => m switch
        {
            MatchNet.GameModeType.TeamDeathmatch => "first team to " + MatchNet.TDM_KILL_TARGET,
            MatchNet.GameModeType.FreeForAll => "first to " + MatchNet.FFA_KILL_TARGET + " kills",
            MatchNet.GameModeType.GunGame => "finish all " + MatchNet.GUNGAME_WEAPONS + " weapons",
            MatchNet.GameModeType.Milsim => "first team to " + MatchNet.MILSIM_HIT_TARGET + " hits",
            _ when ObjectiveMode.HasObjective(m)   // CTF, Domination, Bomb, VIP escort
                => "first team to " + ObjectiveMode.TargetFor(m) + ObjectiveMode.UnitFor(m),
            MatchNet.GameModeType.Medic => "medics revive · out of tickets and players = lose",
            _ => "survive the waves together"
        };

        void UpdateDeath(bool killcam)
        {
            float delay = playerHealth != null ? playerHealth.respawnDelay : 3f;
            // round modes (Bomb, VIP escort) decide the respawn: a longer delay, or out until the next round
            bool held = false;
            if (ObjectiveMode.LocalRespawnRule(out float objDelay, out string heldText))
            {
                if (objDelay < 0f) held = true; else delay = objDelay;
                deathRespawnIn.text = held ? heldText : "Respawning in";
                deathRespawnNote.text = held ? "No respawns during the round." : "Full health on respawn.";
            }
            else { deathRespawnIn.text = "Respawning in"; deathRespawnNote.text = "Full health on respawn."; }
            float left = held ? 0f : Mathf.Max(0f, delay - (Time.unscaledTime - deathTime));
            deathSeconds.gameObject.SetActive(!held);
            deathFill.transform.parent.gameObject.SetActive(!held);
            deathSeconds.text = Mathf.CeilToInt(left).ToString();
            SetBar(deathFill, 1f - left / Mathf.Max(0.01f, delay));

            // Who: the server's report of this death (sent only to us) if it arrived, else the kill feed.
            var mnet = MatchNet.Instance;
            var info = mnet != null ? mnet.LastDeath : default;
            bool fresh = info.time > 0f && info.time >= deathTime - 1f && info.kind != MatchNet.KillerKind.None;
            string killer = fresh ? info.killerName : lastKiller;
            string weapon = fresh ? info.weapon : lastKillerWeapon;
            int team = fresh ? info.killerTeam : lastKillerTeam;
            bool known = !string.IsNullOrEmpty(killer);
            Color c = team != 0 ? TeamColor(team) : Red;
            string by = known
                ? $"<color={Html(c)}>{killer}</color>" +
                  (string.IsNullOrEmpty(weapon) ? "" : $" <size=20><color={Html(Accent)}>· {weapon}</color></size>")
                : "Enemy fire";
            bool hasHp = fresh && info.killerHp >= 0 && info.killerMaxHp > 0;
            string hp = hasHp ? $"<color={Html(Fg)}>{info.killerHp}</color> / {info.killerMaxHp} HP left" : "";

            deathSwatch.color = known ? c : DangerLine;
            deathBy.text = by;
            deathHp.text = hp;
            deathHp.gameObject.SetActive(hasHp);

            if (!killcam) return;
            kcSwatch.color = known ? c : DangerLine;
            kcBy.text = by;
            kcHpBlock.SetActive(hasHp);
            if (hasHp) kcHp.text = $"{info.killerHp}<size=16><color={Html(Muted)}> / {info.killerMaxHp}</color></size>";
            kcSeconds.text = held ? deathRespawnIn.text : "Respawning in " + Mathf.CeilToInt(left);
            SetBar(kcFill, killCam.Progress);
        }

        void UpdatePause(MatchNet mnet)
        {
            if (mnet == null) return;
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : 0;
            pauseMode.text = ModeName(mnet.Mode).ToUpperInvariant();
            switch (mnet.Mode)
            {
                case MatchNet.GameModeType.TeamDeathmatch:
                case MatchNet.GameModeType.Milsim:
                case MatchNet.GameModeType.CaptureTheFlag:
                case MatchNet.GameModeType.Domination:
                case MatchNet.GameModeType.Medic:
                case MatchNet.GameModeType.Bomb:
                case MatchNet.GameModeType.VipEscort:
                    pauseScore.text = $"<color={Html(Red)}>{mnet.TeamScore(1)}</color> <color={Html(Faint)}>:</color> <color={Html(Blue)}>{mnet.TeamScore(2)}</color>";
                    break;
                case MatchNet.GameModeType.GunGame:
                    pauseScore.text = $"{Mathf.Min(mnet.GunLevelOf(me) + 1, MatchNet.GUNGAME_WEAPONS)} / {MatchNet.GUNGAME_WEAPONS}";
                    break;
                case MatchNet.GameModeType.FreeForAll:
                    int k = 0; foreach (var e in mnet.Roster) if (e.clientId == me) k = e.kills;
                    pauseScore.text = $"{k} / {MatchNet.FFA_KILL_TARGET}";
                    break;
                default:
                    pauseScore.text = "WAVE " + (MatchNet.Instance != null ? MatchNet.Instance.CoopWave : 1);
                    break;
            }
            string code = NetworkUI.JoinCode;
            float timeLeft = mnet.TimeLeft;
            pauseMeta.text = $"{MapCatalog.CurrentDisplayName} arena          {mnet.Roster.Count} / 8 players" +
                (string.IsNullOrEmpty(code) ? "          LAN game" : $"          Code <color={Html(Accent)}>{code}</color>") +
                (timeLeft >= 0f ? $"          <color={Html(ClockColor(timeLeft))}>{MatchNet.FormatClock(timeLeft)}</color> left" : "");
        }

        void UpdateResults(MatchNet mnet)
        {
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : 0;
            var mode = mnet.Mode;
            bool tdm = MatchNet.IsTeamMode(mode);
            bool milsim = MatchNet.IsHitRuleMode(mode);   // Milsim, Medic
            bool onTime = mnet.EndedOnTime;
            string objLine = ObjectiveMode.ResultLine(mnet.WinnerTeam, onTime, mnet.IsDraw);   // Medic: tickets
            resMode.text = ModeName(mode).ToUpperInvariant() + (onTime ? " · TIME'S UP" : " · MATCH OVER");
            if (mnet.IsDraw)
            {
                resWinner.text = "DRAW";
                resBanner.color = Muted;
                resSub.text = objLine ?? (tdm ? $"{mnet.TeamScore(1)} : {mnet.TeamScore(2)} · time's up, scores level"
                                  : "time's up · tied for first place");
            }
            else if (tdm && mnet.WinnerTeam != 0)
            {
                resWinner.text = mnet.WinnerTeam == 1 ? "RED TEAM WINS" : "BLUE TEAM WINS";
                resBanner.color = TeamColor(mnet.WinnerTeam);
                resSub.text = objLine ?? ($"{mnet.TeamScore(1)} : {mnet.TeamScore(2)} · " +
                              (onTime ? "higher score when time ran out" : $"first team to {mnet.TeamTarget}" + (milsim ? " hits" : ObjectiveMode.UnitFor(mode))));
            }
            else
            {
                string w = mnet.WinnerClientId == ulong.MaxValue ? "NOBODY" : mnet.NameFor(mnet.WinnerClientId).ToUpperInvariant();
                resWinner.text = mnet.WinnerClientId == me ? "YOU WIN" : w + " WINS";
                resBanner.color = Accent;
                bool gg = mode == MatchNet.GameModeType.GunGame;
                resSub.text = onTime ? (gg ? "time's up · furthest weapon" : "time's up · most kills")
                            : gg ? "finished all " + MatchNet.GUNGAME_WEAPONS + " weapons"
                                 : "first to " + MatchNet.FFA_KILL_TARGET + " kills";
            }

            var list = new List<MatchNet.Entry>(mnet.Roster);
            list.Sort((a, b) => b.kills != a.kills ? b.kills.CompareTo(a.kills) : a.deaths.CompareTo(b.deaths));
            for (int i = 0; i < podium.Count; i++)
            {
                var p = podium[i];
                p.rt.gameObject.SetActive(i < list.Count);
                if (i >= list.Count) continue;
                var e = list[i];
                bool you = e.clientId == me;
                p.name.text = e.name;
                p.name.color = tdm ? TeamColor(e.team) : Fg;
                p.stat.text = mode == MatchNet.GameModeType.GunGame
                    ? $"{WeaponNameAt(Mathf.Min(e.kills, MatchNet.GUNGAME_WEAPONS - 1))} · {Mathf.Min(e.kills, MatchNet.GUNGAME_WEAPONS)} / {MatchNet.GUNGAME_WEAPONS}"
                    : ObjectiveMode.HasObjective(mode) ? $"{ObjectiveMode.PointsOf(e.clientId)} {ObjectiveMode.PointsLabelFor(mode).ToLowerInvariant()} · {e.kills} {(milsim ? "hits" : "kills")}"
                    : milsim ? $"{e.kills} hits · {e.deaths} out"
                    : $"{e.kills} kills · {e.deaths} deaths";
                p.bg.color = you ? Surface : Raised;
                p.frame.color = you ? Accent : Line;
                p.frame.pixelsPerUnitMultiplier = 4f / (you ? 1.5f : 1f);
            }

            int kills = 0, deaths = 0, place = list.Count;
            for (int i = 0; i < list.Count; i++)
                if (list[i].clientId == me) { kills = list[i].kills; deaths = list[i].deaths; place = i + 1; }
            if (tdm)
            {
                SetStat(0, kills.ToString(), milsim ? "HITS" : "KILLS");
                SetStat(1, deaths.ToString(), milsim ? "TIMES HIT" : "DEATHS");
                if (milsim && ObjectiveMode.HasObjective(mode))   // Medic: revives
                    SetStat(2, ObjectiveMode.PointsOf(me).ToString(), ObjectiveMode.PointsLabelFor(mode));
                else SetStat(2, (deaths == 0 ? kills : kills / (float)deaths).ToString("0.0"), milsim ? "H/O" : "K/D");
            }
            else
            {
                if (mode == MatchNet.GameModeType.GunGame) SetStat(0, Mathf.Min(kills + 1, MatchNet.GUNGAME_WEAPONS).ToString(), "WEAPON");
                else SetStat(0, kills.ToString(), "KILLS");
                SetStat(1, deaths.ToString(), "DEATHS");
                SetStat(2, Ordinal(place), "PLACE");
            }

            float left = Mathf.Max(0f, MatchNet.RESULTS_SECONDS - (Time.unscaledTime - resultsStart));
            resCountdown.text = Mathf.CeilToInt(left) + " s";
            SetBar(resFill, 1f - left / MatchNet.RESULTS_SECONDS);
            bool host = mnet.IsHost;
            resBack.gameObject.SetActive(host);
            resWaiting.gameObject.SetActive(!host);
        }

        void SetStat(int i, string v, string k) { resStats[i].v.text = v; resStats[i].k.text = k; }

        static string Ordinal(int n)
        {
            if (n % 100 is 11 or 12 or 13) return n + "th";
            return n + (n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
        }

        // ════════════════════════ pause / quit ════════════════════════
        // The match keeps running while paused (it's multiplayer); only local input stops.
        public void TogglePause()
        {
            isPaused = !isPaused;
            if (!isPaused && settings != null && settings.IsOpen) settings.Close();
        }

        public void ResumeGame() { if (isPaused) TogglePause(); }

        bool sawConnected, wasHost, leaving;

        public void QuitToMenu()
        {
            if (leaving) return;
            leaving = true;
            NetworkUI.JoinCode = "";
            Time.timeScale = 1;
            InputBlocked = false;
            NetworkManager.Singleton?.Shutdown();
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
            InputBlocked = false;
            if (playerHealth != null)
            {
                playerHealth.OnDeath -= ShowDeath;
                playerHealth.OnRespawn -= HideDeath;
            }
            if (weaponCtrl != null) weaponCtrl.OnHitMarker -= ShowHitMarker;
            if (killFeedSub && MatchNet.Instance != null)
                MatchNet.Instance.OnKillFeed -= HandleKill;
            if (canvas != null) Destroy(canvas.gameObject);
        }

        // ════════════════════════ UI construction ════════════════════════
        void BuildUI()
        {
            canvas = UIKit.Canvas("AirsoftHUDCanvas", 10);
            var root = canvas.transform;
            hudRoot = Rect(root, "HUD").Fill().gameObject;
            BuildHud(hudRoot.transform);
            deathScreen = BuildDeath(root);
            killcamScreen = BuildKillcam(root);
            hitScreen = BuildHitOverlay(root);
            hitScreen.SetActive(false);
            scoreboard = BuildScoreboard(root);
            pauseScreen = BuildPause(root);
            resultsScreen = BuildResults(root);
            settings = SettingsScreen.Create(root, null);
            deathScreen.SetActive(false); killcamScreen.SetActive(false);
            scoreboard.SetActive(false); pauseScreen.SetActive(false); resultsScreen.SetActive(false);
            killCam = gameObject.AddComponent<KillCam>();
        }

        static RectTransform ScrimRow(Transform parent, string name, float spacing, RectOffset pad, float alpha = 0.72f)
        {
            var row = HRow(parent, name, spacing, TextAnchor.MiddleCenter);
            row.padding = pad;
            row.gameObject.AddComponent<Image>().color = Scrim(alpha);
            var fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)row.transform;
        }

        void BuildHud(Transform root)
        {
            vignette = Box(root, "LowHealth", A(Hex("#C8281E"), 0.45f));
            vignette.sprite = VignetteSprite;
            vignette.rectTransform.Fill();
            vignette.gameObject.SetActive(false);

            // top-left: join code + fps
            var tl = VCol(root, "TopLeft", 6, TextAnchor.UpperLeft, false);
            tl.GetComponent<RectTransform>().At(0f, 1f, 24, -20, 400, 260);
            var chip = ScrimRow(tl.transform, "Code", 10, new RectOffset(10, 10, 6, 6));
            Label(chip, "CODE", F.BodyBold, 12, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            codeValue = Label(chip, "", F.MonoBold, 12, Accent, TextAlignmentOptions.MidlineLeft, 0.16f);
            codeHint = Label(chip, "click to copy", F.Body, 12, Muted);
            chip.GetComponent<Image>().raycastTarget = true;
            chip.gameObject.AddComponent<UIClick>().onClick = () =>
            {
                if (string.IsNullOrEmpty(NetworkUI.JoinCode)) return;
                GUIUtility.systemCopyBuffer = NetworkUI.JoinCode;
                codeHint.text = "copied!";
                CancelInvoke(nameof(ResetCopyHint));
                Invoke(nameof(ResetCopyHint), 1f);
            };
            codeChip = chip.gameObject;
            fpsText = Label(tl.transform, "", F.MonoBold, 12, Ready);
            fpsText.LE(-1, 16);
            Minimap.Create(tl.transform);   // under the code chip, clear of kill feed / health / ammo

            // top-center widget
            var top = VCol(root, "TopCenter", 6, TextAnchor.UpperCenter, false);
            top.GetComponent<RectTransform>().At(0.5f, 1f, 0, -18, 600, 140);

            tdmWidget = VCol(top.transform, "TDM", 6, TextAnchor.UpperCenter, false).gameObject;
            var tdmRow = HRow(tdmWidget.transform, "Score", 0);
            tdmRow.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            var tdmFit = tdmRow.gameObject.AddComponent<ContentSizeFitter>();
            tdmFit.horizontalFit = tdmFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var red = HRow(tdmRow.transform, "Red", 10); red.padding = new RectOffset(16, 16, 6, 6);
            red.gameObject.AddComponent<Image>().color = Red;
            Label(red.transform, "RED", F.BodyBold, 11, Ground, TextAlignmentOptions.MidlineLeft, 0.14f);
            tdmRed = Label(red.transform, "0", F.CondBlack, 34, Ground);
            var mid = VCol(tdmRow.transform, "Target", 0, TextAnchor.MiddleCenter, false); mid.padding = new RectOffset(16, 16, 4, 4);
            tdmTargetCaption = Label(mid.transform, "FIRST TO", F.BodyBold, 10, Muted, TextAlignmentOptions.Center, 0.14f);
            tdmTarget = Label(mid.transform, "40", F.CondBold, 20, Fg, TextAlignmentOptions.Center);
            var blue = HRow(tdmRow.transform, "Blue", 10); blue.padding = new RectOffset(16, 16, 6, 6);
            blue.gameObject.AddComponent<Image>().color = Blue;
            tdmBlue = Label(blue.transform, "0", F.CondBlack, 34, Ground);
            Label(blue.transform, "BLUE", F.BodyBold, 11, Ground, TextAlignmentOptions.MidlineLeft, 0.14f);
            var youChip = ScrimRow(tdmWidget.transform, "You", 0, new RectOffset(8, 8, 3, 3));
            tdmYou = Label(youChip, "", F.BodySemi, 12, Fg, TextAlignmentOptions.Center, 0.1f);
            // objective modes (CTF flags, Domination A/B/C): widget under the score bar + world markers
            ObjectiveHUD.Create(gameObject, top.transform, tdmWidget.transform.GetSiblingIndex() + 1, root);

            var ffa = ScrimRow(top.transform, "FFA", 18, new RectOffset(18, 18, 8, 8), 0.78f);
            ffaWidget = ffa.gameObject;
            Label(ffa, "FREE-FOR-ALL", F.CondBold, 18, Fg, TextAlignmentOptions.MidlineLeft, 0.08f);
            Box(ffa, "Sep", LineStrong).LE(1, 26);
            var youGrp = HRow(ffa, "You", 8);
            Label(youGrp.transform, "YOU", F.Body, 13, Muted);
            ffaYou = Label(youGrp.transform, "0", F.CondBlack, 28, Accent);
            var leadGrp = HRow(ffa, "Leader", 8);
            ffaLeader = Label(leadGrp.transform, "LEADER", F.Body, 13, Muted);
            ffaLeaderScore = Label(leadGrp.transform, "0", F.CondBlack, 28, Fg);
            ffaTarget = Label(ffa, "/ 15", F.Body, 13, Muted);

            var gg = VCol(top.transform, "GunGame", 6, TextAnchor.UpperLeft, true);
            gg.padding = new RectOffset(16, 16, 8, 10);
            gg.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            gg.LE(360, -1);
            var ggFit = gg.gameObject.AddComponent<ContentSizeFitter>(); ggFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ggWidget = gg.gameObject;
            var ggHead = Rect(gg.transform, "Head").LE(-1, 22);
            Label(ggHead, "GUN GAME", F.CondBold, 18, Fg, TextAlignmentOptions.MidlineLeft, 0.08f).rectTransform.Fill();
            ggNext = Label(ggHead, "", F.Body, 13, Muted, TextAlignmentOptions.MidlineRight);
            ggNext.rectTransform.Fill();
            var steps = HRow(gg.transform, "Steps", 3, TextAnchor.MiddleLeft, true);
            steps.LE(-1, 6);
            for (int i = 0; i < MatchNet.GUNGAME_WEAPONS; i++) ggSteps.Add(Box(steps.transform, "Step", Line).LE(-1, 6, 1));

            var coop = ScrimRow(top.transform, "Coop", 16, new RectOffset(18, 18, 8, 8), 0.78f);
            coopWidget = coop.gameObject;
            coopWave = Label(coop, "WAVE 1", F.CondBlack, 30, Fg);
            Box(coop, "Sep", LineStrong).LE(1, 26);
            var left = HRow(coop, "Left", 6, TextAnchor.LowerLeft);
            coopLeftNum = Label(left.transform, "0", F.CondBold, 22, Accent, TextAlignmentOptions.BottomLeft);
            Label(left.transform, "enemies left", F.Body, 14, Fg, TextAlignmentOptions.BottomLeft).margin = new Vector4(0, 0, 0, 3);

            // match clock under the mode widget (hidden when there is no time limit)
            var clock = ScrimRow(top.transform, "Clock", 8, new RectOffset(12, 12, 4, 4), 0.78f);
            clockChip = clock.gameObject;
            clockFrame = Frame(clock, Accent, 1f);
            clockFrame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            clockCaption = Label(clock, "TIME", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            clockText = Label(clock, "00:00", F.MonoBold, 18, Fg);
            clockFrame.transform.SetAsLastSibling();
            clockFrame.enabled = false;
            clockChip.SetActive(false);

            // top-right kill feed
            var feed = VCol(root, "KillFeed", 4, TextAnchor.UpperRight, false);
            feed.GetComponent<RectTransform>().At(1f, 1f, -24, -20, 520, 200);
            for (int i = 0; i < 5; i++)
            {
                var r = new FeedRow();
                var row = ScrimRow(feed.transform, "Kill", 10, new RectOffset(11, 11, 5, 5));
                r.go = row.gameObject;
                r.bg = row.GetComponent<Image>();
                r.frame = Frame(row, Accent, 1f);
                r.frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                r.killer = Label(row, "", F.BodySemi, 14, Fg);
                r.weapon = Label(row, "", F.CondSemi, 13, Accent, TextAlignmentOptions.MidlineLeft, 0.06f);
                r.victim = Label(row, "", F.BodySemi, 14, Fg);
                r.frame.transform.SetAsLastSibling();
                r.go.SetActive(false);
                feedRows.Add(r);
            }

            // crosshair + hit marker
            crosshair = Rect(root, "Crosshair").At(0.5f, 0.5f, 0, 0, 80, 80);
            crossArms = SettingsScreen.CrosshairArms(crosshair);
            var hm = Rect(root, "HitMarker").At(0.5f, 0.5f, 0, 0, 56, 56);
            foreach (var (x, y, rot) in new[] { (-14f, 14f, -45f), (14f, 14f, 45f), (-14f, -14f, 45f), (14f, -14f, -45f) })
            {
                var bar = Box(hm, "Bar", Accent).rectTransform;
                bar.At(0.5f, 0.5f, x, y, 14, 2);
                bar.localRotation = Quaternion.Euler(0, 0, rot);
            }
            hitMarker = hm.gameObject;
            hitMarker.SetActive(false);

            // bottom-left health
            var hpBox = VCol(root, "Health", 8, TextAnchor.LowerLeft, true);
            hpBox.padding = new RectOffset(16, 16, 12, 12);
            hpBox.gameObject.AddComponent<Image>().color = Scrim();
            hpBox.GetComponent<RectTransform>().At(0f, 0f, 24, 24, 280, 92);
            var hpRow = HRow(hpBox.transform, "Value", 10, TextAnchor.LowerLeft);
            hpRow.LE(-1, 52);
            hpValue = Label(hpRow.transform, "100", F.CondBlack, 54, Fg, TextAlignmentOptions.BottomLeft);
            hpValue.lineSpacing = -30;
            hpUnit = Label(hpRow.transform, "HP", F.BodyBold, 12, Fg, TextAlignmentOptions.BottomLeft, 0.14f);
            hpUnit.margin = new Vector4(0, 0, 0, 8);
            hpFill = Bar(hpBox.transform, "Bar", Line, Fg);
            hpFill.transform.parent.GetComponent<Image>().LE(-1, 8);

            // bottom-center weapon slots
            var slotsRow = HRow(root, "Slots", 4, TextAnchor.LowerCenter);
            slotsRow.GetComponent<RectTransform>().At(0.5f, 0f, 0, 24, 10 * 62, 50);
            for (int i = 0; i < 10; i++)
            {
                var sv = new SlotView();
                sv.bg = Box(slotsRow.transform, "Slot" + i, Scrim());
                sv.bg.LE(58, 50);
                sv.frame = Frame(sv.bg.transform, A(Line, 0.9f), 1f);
                sv.key = Label(sv.bg.transform, i == 9 ? "0" : (i + 1).ToString(), F.MonoBold, 10, Fg, TextAlignmentOptions.TopLeft);
                sv.key.rectTransform.Fill(6, 5, 6, 5);
                sv.name = Label(sv.bg.transform, "", F.CondBold, 13, Fg, TextAlignmentOptions.BottomLeft);
                sv.name.rectTransform.Fill(6, 5, 4, 5);
                sv.name.overflowMode = TextOverflowModes.Truncate;
                slotViews.Add(sv);
            }

            // bottom-right ammo
            var ammo = VCol(root, "Ammo", 6, TextAnchor.LowerRight, false);
            ammo.padding = new RectOffset(16, 16, 12, 12);
            ammo.gameObject.AddComponent<Image>().color = Scrim();
            var ammoFit = ammo.gameObject.AddComponent<ContentSizeFitter>();
            ammoFit.horizontalFit = ammoFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ammoRt = ammo.GetComponent<RectTransform>();
            ammoRt.anchorMin = ammoRt.anchorMax = ammoRt.pivot = new Vector2(1f, 0f);
            ammoRt.anchoredPosition = new Vector2(-24, 24);
            Rect(ammo.transform, "MinWidth").LE(168, 0);
            ammoWeapon = Label(ammo.transform, "", F.CondSemi, 16, Muted, TextAlignmentOptions.MidlineRight, 0.06f);
            var magRow = HRow(ammo.transform, "Mag", 6, TextAnchor.LowerRight);
            ammoMag = Label(magRow.transform, "0", F.CondBlack, 54, Fg, TextAlignmentOptions.BottomRight);
            ammoReserve = Label(magRow.transform, "/ 0", F.CondBlack, 26, Muted, TextAlignmentOptions.BottomRight);
            ammoReserve.margin = new Vector4(0, 0, 0, 6);
            var rh = HRow(ammo.transform, "Reload", 6, TextAnchor.MiddleRight);
            KeyCap(rh.transform, "R", 11, Accent, Accent);
            reloadText = Label(rh.transform, "RELOAD", F.BodyBold, 12, Accent, TextAlignmentOptions.MidlineRight, 0.12f);
            reloadHint = rh.gameObject;
        }

        void ResetCopyHint() { if (codeHint) codeHint.text = "click to copy"; }

        GameObject BuildDeath(Transform root)
        {
            var screen = Rect(root, "DeathScreen").Fill();
            Box(screen, "Tint", A(Hex("#3C0C08"), 0.55f)).rectTransform.Fill();
            var v = Box(screen, "Vignette", A(Ground, 0.8f)); v.sprite = VignetteSprite; v.rectTransform.Fill();

            var col = VCol(screen, "Column", 28, TextAnchor.MiddleCenter, false);
            col.GetComponent<RectTransform>().At(0.5f, 0.5f, 0, 0, 900, 460);
            var head = VCol(col.transform, "Head", 10, TextAnchor.MiddleCenter, false);
            Label(head.transform, "YOU WERE HIT", F.BodyBold, 12, Hex("#FFB3AA"), TextAlignmentOptions.Center, 0.24f);
            var big = Label(head.transform, "ELIMINATED", F.CondBlack, 120, Fg, TextAlignmentOptions.Center, 0.02f);
            big.LE(-1, 108);

            var card = HRow(col.transform, "By", 18, TextAnchor.MiddleLeft);
            card.padding = new RectOffset(22, 22, 14, 14);
            card.gameObject.AddComponent<Image>().color = Scrim(0.82f);
            Frame(card.transform, Line, 1f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            deathSwatch = Box(card.transform, "Swatch", Red);
            deathSwatch.LE(44, 44);
            var byCol = VCol(card.transform, "Text", 2, TextAnchor.MiddleLeft, false);
            Label(byCol.transform, "ELIMINATED BY", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            deathBy = Label(byCol.transform, "", F.CondBold, 28, Fg);
            deathHp = Label(byCol.transform, "", F.Body, 14, Muted);

            var resp = VCol(col.transform, "Respawn", 12, TextAnchor.MiddleCenter, true);
            resp.LE(360, -1);
            var rr = HRow(resp.transform, "Row", 10, TextAnchor.LowerCenter);
            deathRespawnIn = Label(rr.transform, "Respawning in", F.Body, 15, TextSoft, TextAlignmentOptions.BottomLeft);
            deathRespawnIn.margin = new Vector4(0, 0, 0, 6);
            deathSeconds = Label(rr.transform, "3", F.CondBlack, 44, Accent, TextAlignmentOptions.BottomLeft);
            deathFill = Bar(resp.transform, "Bar", Line, Accent);
            deathFill.transform.parent.GetComponent<Image>().LE(-1, 6);
            deathRespawnNote = Label(resp.transform, "Full health on respawn.", F.Body, 13, Muted, TextAlignmentOptions.Center);

            var tab = HRow(screen, "TabHint", 8, TextAnchor.MiddleRight);
            tab.GetComponent<RectTransform>().At(1f, 0f, -24, 24, 300, 24);
            KeyCap(tab.transform, "TAB", 11);
            Label(tab.transform, "scoreboard", F.Body, 13, TextSoft);
            return screen.gameObject;
        }

        // Milsim HIT overlay: the HUD stays up (you can still walk), this sits on top of it.
        GameObject BuildHitOverlay(Transform root)
        {
            var screen = Rect(root, "HitOverlay").Fill();
            var v = Box(screen, "Vignette", A(Hex("#C8281E"), 0.38f)); v.sprite = VignetteSprite; v.rectTransform.Fill();
            v.raycastTarget = false;

            var col = VCol(screen, "Column", 8, TextAnchor.UpperCenter, false);
            col.GetComponent<RectTransform>().At(0.5f, 1f, 0, -170, 760, 300);
            Label(col.transform, "YOU'RE OUT", F.BodyBold, 12, Hex("#FFB3AA"), TextAlignmentOptions.Center, 0.24f);
            Label(col.transform, "HIT!", F.CondBlack, 96, Fg, TextAlignmentOptions.Center, 0.04f).LE(-1, 88);
            hitBy = Label(col.transform, "", F.BodySemi, 16, TextSoft, TextAlignmentOptions.Center);

            var card = VCol(col.transform, "Card", 8, TextAnchor.MiddleCenter, false);
            card.padding = new RectOffset(22, 22, 12, 12);
            card.gameObject.AddComponent<Image>().color = Scrim(0.82f);
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            hitInstr = Label(card.transform, "", F.Body, 15, Fg, TextAlignmentOptions.Center);

            // walking back: arrow towards the zone + distance
            var walk = HRow(card.transform, "Walk", 10, TextAnchor.MiddleCenter);
            hitWalk = walk.gameObject;
            hitArrow = Rect(walk.transform, "Arrow").LE(28, 28).GetComponent<RectTransform>();
            Box(hitArrow, "Stem", Accent).rectTransform.At(0.5f, 0.5f, 0, -3, 3, 16);
            var l = Box(hitArrow, "L", Accent).rectTransform; l.At(0.5f, 0.5f, -4, 4, 12, 3); l.localRotation = Quaternion.Euler(0, 0, 45);
            var r = Box(hitArrow, "R", Accent).rectTransform; r.At(0.5f, 0.5f, 4, 4, 12, 3); r.localRotation = Quaternion.Euler(0, 0, -45);
            hitDist = Label(walk.transform, "", F.CondBold, 24, Accent, TextAlignmentOptions.MidlineLeft);

            // in the zone: countdown
            var resp = VCol(card.transform, "Respawn", 6, TextAnchor.MiddleCenter, true);
            resp.LE(320, -1);
            hitRespawn = resp.gameObject;
            hitCount = Label(resp.transform, "", F.CondBold, 22, Ready, TextAlignmentOptions.Center);
            hitFill = Bar(resp.transform, "Bar", Line, Ready);
            hitFill.transform.parent.GetComponent<Image>().LE(-1, 6);
            return screen.gameObject;
        }

        void UpdateHit(MatchNet mnet)
        {
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : 0;
            int team = mnet != null ? mnet.TeamOf(me) : 0;
            string teamName = team == 1 ? "RED" : team == 2 ? "BLUE" : "YOUR";
            string tc = Html(team != 0 ? TeamColor(team) : Accent);

            hitBy.text = string.IsNullOrEmpty(lastKiller) ? "" :
                $"by <color={Html(lastKillerTeam != 0 ? TeamColor(lastKillerTeam) : Fg)}>{lastKiller}</color>" +
                (string.IsNullOrEmpty(lastKillerWeapon) ? "" : $" <color={Html(Muted)}>· {lastKillerWeapon}</color>");

            Vector3 pos = playerHealth.transform.position;
            float outside = MilsimZone.DistanceOutside(team, pos, out Vector3 zone);
            double at = playerHealth.hitRespawnAt.Value;
            bool counting = at > 0 && nm != null;
            hitWalk.SetActive(!counting && outside > 0f);
            hitRespawn.SetActive(counting);

            if (counting)
            {
                float left = Mathf.Max(0f, (float)(at - nm.ServerTime.Time));
                float total = Mathf.Max(0.01f, playerHealth.hitRespawnTime);
                hitInstr.text = $"In the <color={tc}>{teamName} BASE</color>. Stay here.";
                hitCount.text = "BACK IN " + Mathf.CeilToInt(left);
                SetBar(hitFill, 1f - left / total);
                return;
            }

            hitInstr.text = $"Raise your hand, don't shoot. Walk back to the <color={tc}>{teamName} BASE</color> to respawn.";
            if (outside > 0f)
            {
                hitDist.text = Mathf.CeilToInt(outside) + " m";
                var cam = Camera.main;
                Vector3 fwd = cam != null ? cam.transform.forward : playerHealth.transform.forward;
                fwd.y = 0;
                Vector3 to = zone - pos; to.y = 0;
                float ang = fwd.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(fwd, to, Vector3.up) : 0f;
                hitArrow.localRotation = Quaternion.Euler(0, 0, -ang);
            }
        }

        // Cinematic bands while the killcam watches the killer (the full death screen follows).
        GameObject BuildKillcam(Transform root)
        {
            var screen = Rect(root, "Killcam").Fill();
            var top = Band(screen, "Top", true, 52);
            var tag = HRow(top, "Tag", 10, TextAnchor.MiddleLeft);
            tag.GetComponent<RectTransform>().At(0f, 0.5f, 24, 0, 500, 30);
            var badge = HRow(tag.transform, "Badge", 0, TextAnchor.MiddleCenter);
            badge.padding = new RectOffset(10, 10, 3, 3);
            badge.gameObject.AddComponent<Image>().color = Accent;
            Label(badge.transform, "KILLCAM", F.CondBold, 15, Ground, TextAlignmentOptions.Center, 0.12f);
            Label(tag.transform, "watching who got you", F.Body, 14, TextSoft);
            kcSeconds = Label(top, "", F.BodySemi, 14, TextSoft, TextAlignmentOptions.MidlineRight);
            kcSeconds.rectTransform.At(1f, 0.5f, -24, 0, 300, 30);
            kcFill = Bar(top, "Progress", A(Line, 0.6f), Accent);
            var bar = kcFill.transform.parent.GetComponent<RectTransform>();
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1f, 0f); bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0, 3); bar.anchoredPosition = Vector2.zero;

            var bottom = Band(screen, "Bottom", false, 96);
            var card = HRow(bottom, "By", 18, TextAnchor.MiddleCenter);
            card.GetComponent<RectTransform>().Fill();
            kcSwatch = Box(card.transform, "Swatch", Red);
            kcSwatch.LE(36, 36);
            var byCol = VCol(card.transform, "Text", 2, TextAnchor.MiddleLeft, false);
            Label(byCol.transform, "ELIMINATED BY", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            kcBy = Label(byCol.transform, "", F.CondBold, 28, Fg);
            var hpGroup = HRow(card.transform, "HpGroup", 18, TextAnchor.MiddleLeft);
            kcHpBlock = hpGroup.gameObject;
            Box(hpGroup.transform, "Sep", LineStrong).LE(1, 40);
            var hpCol = VCol(hpGroup.transform, "Hp", 2, TextAnchor.MiddleLeft, false);
            Label(hpCol.transform, "THEIR HP", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            kcHp = Label(hpCol.transform, "", F.CondBlack, 28, Fg);
            return screen.gameObject;
        }

        static RectTransform Band(Transform parent, string name, bool top, float height)
        {
            var rt = Box(parent, name, A(Ground, 0.86f)).rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        GameObject BuildScoreboard(Transform root)
        {
            var screen = Rect(root, "Scoreboard").Fill();
            Box(screen, "Dim", Scrim(0.62f)).rectTransform.Fill();
            var panel = VCol(screen, "Panel", 18, TextAnchor.UpperLeft, true);
            panel.padding = new RectOffset(32, 32, 28, 28);
            panel.gameObject.AddComponent<Image>().color = Scrim(0.94f);
            var prt = panel.GetComponent<RectTransform>();
            prt.At(0.5f, 0.5f, 0, 0, 980, 400);
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Frame(panel.transform, Line, 1f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var head = Rect(panel.transform, "Head").LE(-1, 36);
            var titleRow = HRow(head, "Title", 14, TextAnchor.LowerLeft);
            titleRow.GetComponent<RectTransform>().Fill();
            sbTitle = Label(titleRow.transform, "", F.CondBlack, 34, Fg, TextAlignmentOptions.BottomLeft);
            sbGoal = Label(titleRow.transform, "", F.Body, 14, Muted, TextAlignmentOptions.BottomLeft);
            sbGoal.margin = new Vector4(0, 0, 0, 5);
            var hold = HRow(head, "Hold", 8, TextAnchor.MiddleRight);
            hold.GetComponent<RectTransform>().Fill();
            KeyCap(hold.transform, "TAB", 11);
            Label(hold.transform, "hold to show", F.Body, 13, Muted);

            // team layout (TDM)
            var teams = HRow(panel.transform, "Teams", 20, TextAnchor.UpperLeft, true);
            sbTeams = teams.gameObject;
            for (int t = 0; t < 2; t++)
            {
                var tv = new TeamView();
                var tc = VCol(teams.transform, t == 0 ? "Red" : "Blue", 0);
                tc.LE(-1, -1, 1);
                var th = Box(tc.transform, "Header", t == 0 ? Red : Blue);
                th.LE(-1, 52);
                Label(th.transform, t == 0 ? "RED" : "BLUE", F.BodyBold, 13, Ground, TextAlignmentOptions.MidlineLeft, 0.16f).rectTransform.Fill(14, 0, 14, 0);
                tv.score = Label(th.transform, "0", F.CondBlack, 32, Ground, TextAlignmentOptions.MidlineRight);
                tv.score.rectTransform.Fill(14, 0, 14, 0);
                var th2 = ColumnHeads(tc.transform, 34, ("PLAYER", -1f, false), ("", 60f, true), ("KILLS", 60f, true), ("DEATHS", 60f, true));
                tv.headO = th2[1]; tv.headK = th2[2]; tv.headD = th2[3];
                for (int r = 0; r < 8; r++)
                {
                    var row = Box(tc.transform, "Row", Color.clear);
                    row.LE(-1, 44);
                    Box(row.transform, "Sep", Raised).rectTransform.At(0.5f, 0f, 0, 0, 2000, 1);
                    var n = Label(row.transform, "", F.BodySemi, 15, Fg); n.rectTransform.Fill(14, 0, 134, 0);
                    var k = Label(row.transform, "", F.CondBold, 20, Fg, TextAlignmentOptions.MidlineRight); k.rectTransform.Fill(0, 0, 74, 0);
                    var d = Label(row.transform, "", F.CondSemi, 20, Muted, TextAlignmentOptions.MidlineRight); d.rectTransform.Fill(0, 0, 14, 0);
                    var o = Label(row.transform, "", F.CondBold, 20, Accent, TextAlignmentOptions.MidlineRight); o.rectTransform.Fill(0, 0, 134, 0);
                    tv.rows.Add((row.gameObject, row, n, k, d));
                    tv.obj.Add(o);
                }
                sbTeamViews[t] = tv;
            }

            // list layout (FFA / Gun Game / Co-op)
            var list = VCol(panel.transform, "List", 0);
            sbList = list.gameObject;
            var heads = ColumnHeads(list.transform, 34, ("#", 48f, false), ("PLAYER", -1f, false), ("PROGRESS", 280f, false), ("KILLS", 90f, true), ("DEATHS", 90f, true));
            sbHeadProgress = heads[2]; sbHeadA = heads[3]; sbHeadB = heads[4];
            for (int r = 0; r < 8; r++)
            {
                var lr = new ListRow();
                var bg = Box(list.transform, "Row", Color.clear);
                bg.LE(-1, 48);
                lr.go = bg.gameObject; lr.bg = bg;
                Box(bg.transform, "Sep", Raised).rectTransform.At(0.5f, 0f, 0, 0, 2000, 1);
                var row = HRow(bg.transform, "Cells", 0);
                row.padding = new RectOffset(14, 14, 0, 0);
                row.GetComponent<RectTransform>().Fill();
                lr.rank = Label(row.transform, "", F.CondBold, 20, Muted); lr.rank.LE(48, 48);
                lr.name = Label(row.transform, "", F.BodySemi, 15, Fg); lr.name.LE(-1, 48, 1);
                var prog = HRow(row.transform, "Progress", 10); prog.padding = new RectOffset(0, 24, 0, 0); prog.LE(280, 48);
                lr.bar = Bar(prog.transform, "Bar", Line, Fg);
                lr.barBack = lr.bar.transform.parent.gameObject;
                lr.barBack.GetComponent<Image>().LE(-1, 6, 1);
                lr.progress = Label(prog.transform, "", F.Body, 12, Muted); lr.progress.LE(96, 20);
                lr.a = Label(row.transform, "", F.CondBold, 20, Fg, TextAlignmentOptions.MidlineRight); lr.a.LE(90, 48);
                lr.b = Label(row.transform, "", F.CondSemi, 20, Muted, TextAlignmentOptions.MidlineRight); lr.b.LE(90, 48);
                sbRows.Add(lr);
            }

            var foot = Rect(panel.transform, "Footer").LE(-1, 32);
            Box(foot, "Line", Line).rectTransform.At(0.5f, 1f, 0, 0, 916, 1);
            sbFooterLeft = Label(foot, "", F.Body, 13, Muted, TextAlignmentOptions.BottomLeft); sbFooterLeft.rectTransform.Fill();
            sbFooterRight = Label(foot, "", F.Body, 13, Muted, TextAlignmentOptions.BottomRight); sbFooterRight.rectTransform.Fill();
            return screen.gameObject;
        }

        static TextMeshProUGUI[] ColumnHeads(Transform parent, float h, params (string text, float width, bool right)[] cols)
        {
            var bg = Rect(parent, "Heads").LE(-1, h);
            Box(bg, "Line", Line).rectTransform.At(0.5f, 0f, 0, 0, 2000, 1);
            var row = HRow(bg, "Cells", 0);
            row.padding = new RectOffset(14, 14, 0, 0);
            row.GetComponent<RectTransform>().Fill();
            var outs = new TextMeshProUGUI[cols.Length];
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                outs[i] = Label(row.transform, c.text, F.BodyBold, 11, Muted,
                    c.right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, 0.14f);
                if (c.width < 0) outs[i].LE(-1, h, 1); else outs[i].LE(c.width, h);
            }
            return outs;
        }

        GameObject BuildPause(Transform root)
        {
            var screen = Rect(root, "PauseScreen").Fill();
            Box(screen, "Dim", Scrim(0.7f), true).rectTransform.Fill();
            var stage = Rect(screen, "Stage").At(0.5f, 0.5f, 0, 0, 1280, 720);
            var panel = Box(stage, "Panel", Ground);
            panel.rectTransform.At(0f, 0.5f, -1000, 0, 1480, 4000);
            Box(panel.transform, "Edge", Line).rectTransform.At(1f, 0.5f, 0, 0, 1, 4000);

            var col = VCol(stage, "Menu", 14);
            col.padding = new RectOffset(64, 64, 64, 64);
            col.GetComponent<RectTransform>().TL(0, 0, 480, 720);
            var title = HRow(col.transform, "Title", 10);
            title.LE(-1, 48);
            Box(title.transform, "Bar", Accent).LE(9, 26);
            Label(title.transform, "PAUSED", F.CondBlack, 44, Fg);

            var note = HRow(col.transform, "Note", 10, TextAnchor.UpperLeft);
            note.padding = new RectOffset(14, 14, 12, 12);
            note.gameObject.AddComponent<Image>().color = Raised;
            Frame(note.transform, Line, 1f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var info = Rect(note.transform, "Icon").LE(18, 18);
            var ring = Box(info, "Ring", Accent); ring.sprite = RingSprite; ring.rectTransform.Fill();
            Label(info, "!", F.BodyBold, 12, Accent, TextAlignmentOptions.Center).rectTransform.Fill();
            Label(note.transform, "The match keeps running. Other players can still hit you.", F.Body, 14, TextSoft).Wrap(8).LE(-1, -1, 1);
            Spacer(col.transform, -1, 0, 0).LE(-1, 0);

            var resume = Button(col.transform, "RESUME", Btn.Primary, 24, ResumeGame, F.CondBlack, 14);
            resume.LE(-1, 60);
            resume.label.alignment = TextAlignmentOptions.MidlineLeft;
            resume.label.rectTransform.Fill(24, 0, 24, 0);
            var esc = KeyCap(resume.transform, "ESC", 12, Ground, Ground);
            esc.anchorMin = esc.anchorMax = esc.pivot = new Vector2(1f, 0.5f);
            esc.anchoredPosition = new Vector2(-24, 0);

            var set = Button(col.transform, "SETTINGS", Btn.Quiet, 20, () => settings.Open());
            set.LE(-1, 52); set.label.alignment = TextAlignmentOptions.MidlineLeft; set.label.rectTransform.Fill(22, 0, 22, 0);
            var leave = Button(col.transform, "LEAVE MATCH", Btn.Quiet, 20, QuitToMenu);
            leave.LE(-1, 52); leave.label.alignment = TextAlignmentOptions.MidlineLeft; leave.label.rectTransform.Fill(22, 0, 22, 0);
            Spacer(col.transform);
            var quit = Button(col.transform, "QUIT TO DESKTOP", Btn.Danger, 18, QuitGame);
            quit.LE(-1, 48); quit.label.alignment = TextAlignmentOptions.MidlineLeft; quit.label.rectTransform.Fill(22, 0, 22, 0);

            // right: match + controls
            var right = VCol(stage, "Info", 24);
            right.padding = new RectOffset(64, 64, 64, 64);
            right.GetComponent<RectTransform>().TL(480, 0, 800, 720);
            var match = VCol(right.transform, "Match", 14);
            match.padding = new RectOffset(24, 24, 22, 22);
            match.gameObject.AddComponent<Image>().color = Scrim(0.86f);
            Frame(match.transform, Line, 1f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Caption(match.transform, "CURRENT MATCH", 11, Muted, 0.16f).LE(-1, 14);
            var mrow = Rect(match.transform, "Row").LE(-1, 36);
            pauseMode = Label(mrow, "", F.CondBold, 30, Fg); pauseMode.rectTransform.Fill();
            pauseScore = Label(mrow, "", F.CondBlack, 30, Fg, TextAlignmentOptions.MidlineRight); pauseScore.rectTransform.Fill();
            pauseMeta = Label(match.transform, "", F.Body, 14, TextSoft);
            pauseMeta.LE(-1, 18);

            var ctr = VCol(right.transform, "Controls", 10);
            ctr.padding = new RectOffset(24, 24, 22, 22);
            ctr.gameObject.AddComponent<Image>().color = Scrim(0.86f);
            Frame(ctr.transform, Line, 1f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Caption(ctr.transform, "CONTROLS", 11, Muted, 0.16f).LE(-1, 14);
            var grid = Rect(ctr.transform, "Grid").LE(-1, (Binds.Length + 1) / 2 * 30);
            for (int i = 0; i < Binds.Length; i++)
            {
                int c = i % 2, r = i / 2;
                var cell = Rect(grid, "Bind").TL(c * 320, r * 30, 288, 24);
                Label(cell, Binds[i].action, F.Body, 14, TextSoft).rectTransform.Fill(0, 0, 100, 0);
                var key = KeyCap(cell, Binds[i].key, 11);
                key.anchorMin = key.anchorMax = key.pivot = new Vector2(1f, 0.5f);
                key.anchoredPosition = Vector2.zero;
            }
            return screen.gameObject;
        }

        GameObject BuildResults(Transform root)
        {
            var screen = Rect(root, "ResultsScreen").Fill();
            Box(screen, "Ground", Ground, true).rectTransform.Fill();
            var stage = Rect(screen, "Stage").At(0.5f, 0.5f, 0, 0, 1280, 720);

            resBanner = Box(stage, "Banner", Accent);
            resBanner.rectTransform.At(0.5f, 1f, 0, 400, 4000, 630);
            var bcol = VCol(stage, "BannerText", 6, TextAnchor.MiddleCenter, false);
            bcol.GetComponent<RectTransform>().TL(0, 0, 1280, 230);
            resMode = Label(bcol.transform, "", F.BodyBold, 13, Ground, TextAlignmentOptions.Center, 0.24f);
            resWinner = Label(bcol.transform, "", F.CondBlack, 104, Ground, TextAlignmentOptions.Center, 0.01f);
            resWinner.LE(-1, 96);
            resSub = Label(bcol.transform, "", F.BodySemi, 16, Ground, TextAlignmentOptions.Center);

            // top players
            Caption(stage, "TOP PLAYERS", 12, Muted, 0.16f).rectTransform.TL(64, 266, 400, 16);
            float[] heights = { 210, 170, 140 };
            for (int i = 0; i < 3; i++)
            {
                var p = new PodiumView();
                p.bg = Box(stage, "Podium" + (i + 1), Raised);
                p.rt = p.bg.rectTransform;
                float w = (796 - 24) / 3f;
                // bottoms aligned: shorter blocks start lower
                p.rt.TL(64 + i * (w + 12), 296 + heights[0] - heights[i], w, heights[i]);
                p.frame = Frame(p.bg.transform, Line, 1f);
                var pc = VCol(p.bg.transform, "Text", 10, TextAnchor.LowerLeft);
                pc.padding = new RectOffset(18, 18, 18, 18);
                pc.GetComponent<RectTransform>().Fill();
                p.rank = Label(pc.transform, "#" + (i + 1), F.CondBlack, 40, i == 0 ? Accent : Muted);
                p.name = Label(pc.transform, "", F.CondBold, 26, Fg);
                p.name.overflowMode = TextOverflowModes.Ellipsis;
                p.stat = Label(pc.transform, "", F.Body, 14, Muted);
                podium.Add(p);
            }

            // your match
            var right = VCol(stage, "You", 14);
            right.GetComponent<RectTransform>().TL(876, 266, 340, 422);
            Caption(right.transform, "YOUR MATCH", 12, Muted, 0.16f).LE(-1, 16);
            var stats = HRow(right.transform, "Stats", 1, TextAnchor.MiddleLeft, true, true);
            stats.padding = new RectOffset(1, 1, 1, 1);
            stats.gameObject.AddComponent<Image>().color = Line;
            stats.LE(-1, 80);
            for (int i = 0; i < 3; i++)
            {
                var cell = VCol(stats.transform, "Stat", 4, TextAnchor.MiddleLeft);
                cell.padding = new RectOffset(14, 14, 14, 14);
                cell.gameObject.AddComponent<Image>().color = Surface;
                var v = Label(cell.transform, "0", F.CondBlack, 34, Fg); v.LE(-1, 34);
                var k = Label(cell.transform, "", F.BodyBold, 11, Muted, TextAlignmentOptions.MidlineLeft, 0.14f); k.LE(-1, 14);
                resStats.Add((v, k));
            }
            ProfileUI.AddToResults(right.transform);   // XP gained + level progress
            Spacer(right.transform);
            var cdRow = Rect(right.transform, "Countdown").LE(-1, 20);
            Label(cdRow, "Returning to lobby", F.Body, 14, TextSoft).rectTransform.Fill();
            resCountdown = Label(cdRow, "", F.MonoBold, 14, Accent, TextAlignmentOptions.MidlineRight);
            resCountdown.rectTransform.Fill();
            resFill = Bar(right.transform, "Bar", Line, Accent);
            resFill.transform.parent.GetComponent<Image>().LE(-1, 4);
            resBack = Button(right.transform, "BACK TO LOBBY NOW", Btn.Primary, 20, () => MatchNet.Instance?.HostReturnToLobbyNow(), F.CondBlack, 12);
            resBack.LE(-1, 52);
            resWaiting = Label(right.transform, "The host takes everyone back to the lobby.", F.Body, 13, Muted, TextAlignmentOptions.Center);
            resWaiting.LE(-1, 52);
            return screen.gameObject;
        }
    }
}
