using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Base of the round-based objective modes (Bomb, VIP escort). The match is a series of short rounds;
    // each round one team attacks (plants the bomb / escorts the VIP) and the other defends, and the teams
    // swap roles every round. A round: PREP (everyone respawned at their side, frozen for a few seconds)
    // -> LIVE (the round clock runs) -> OVER (result shown, then the next round). Each won round is a team
    // point; the first team to ScoreTarget rounds wins the match (MatchNet ends it, no match time limit).
    //
    // Also here: the interact key (E, held; the server is told while it's down), the progress ring in the
    // HUD, the round widget (round, clock, role, banner), spectating teammates while you're out, and the
    // respawn rules (subclasses decide who stays down).
    public abstract class RoundMode : ObjectiveMode
    {
        public const float PREP_SECONDS = 5f;        // frozen at the start of a round
        public const float OVER_SECONDS = 6f;        // result on screen before the next round
        public const float FIRST_ROUND_DELAY = 2f;   // after the map loaded, before round 1
        public const float USE_HOLD_TIMEOUT = 0.6f;  // server: E counts as held this long after the last "held" message

        public enum Stage : byte { Waiting = 0, Prep = 1, Live = 2, Over = 3 }

        public int Round { get; private set; }
        public Stage RoundStage { get; private set; }
        public double StageEndsAt { get; private set; }   // server time
        public int AttackTeam { get; private set; } = 1;  // attackers / escorts this round
        public int DefendTeam => 3 - AttackTeam;
        public int RoundWinner { get; private set; }
        public string RoundReason { get; private set; } = "";

        protected abstract float RoundSeconds { get; }
        protected abstract string AttackRole { get; }   // "ATTACK", "ESCORT"
        protected abstract string DefendRole { get; }   // "DEFEND", "HUNT"
        // The round clock is not running (e.g. the bomb is planted: the fuse counts instead).
        protected virtual bool ClockPaused => false;

        // Test hooks (editor logic checks): who holds the interact key, who got respawned at round start.
        [NonSerialized] public Func<ulong, bool> testUse;
        [NonSerialized] public readonly List<(ulong id, Pose pose)> testRespawned = new List<(ulong, Pose)>();

        double waitingSince = -1;

        public float StageLeft => Mathf.Max(0f, (float)(StageEndsAt - Now));

        // Editor logic checks: the protected rules from outside.
        public bool TestHoldsRespawn(ulong id) => HoldRespawn(id);
        public float TestMoveFactor(ulong id) => MoveFactor(id);
        public bool TestAllowsWeapon(ulong id, WeaponData w) => AllowsWeapon(id, w);
        public bool TestBlocksSprint(ulong id) => BlocksSprint(id);

        // ───────────────────────── server: round flow ─────────────────────────
        public override void ServerTick(float dt)
        {
            GatherParticipants();
            double now = Now;
            switch (RoundStage)
            {
                case Stage.Waiting:
                    if (waitingSince < 0) waitingSince = now;
                    if (participants.Count > 0 && now - waitingSince >= FIRST_ROUND_DELAY) StartRound(1, now);
                    break;
                case Stage.Prep:
                    RoundTick(dt, now);
                    if (RoundStage == Stage.Prep && now >= StageEndsAt)
                    {
                        RoundStage = Stage.Live;
                        StageEndsAt = now + RoundSeconds;
                        MarkDirty();
                        OnRoundLive(now);
                    }
                    break;
                case Stage.Live:
                    RoundTick(dt, now);
                    if (RoundStage == Stage.Live && !ClockPaused && now >= StageEndsAt) OnRoundTimeout();
                    break;
                case Stage.Over:
                    if (now >= StageEndsAt) StartRound(Round + 1, now);
                    break;
            }
        }

        void StartRound(int n, double now)
        {
            Round = n;
            AttackTeam = n % 2 == 1 ? 1 : 2;   // Red attacks first, then the roles swap every round
            RoundStage = Stage.Prep;
            StageEndsAt = now + PREP_SECONDS;
            RoundWinner = 0;
            RoundReason = "";
            OnRoundStart(now);
            RespawnEveryone();
            MarkDirty();
            Announce($"Round {n}: {TeamName(AttackTeam)} {RoundIntro()}", AttackTeam);
        }

        // Server: the round is decided. The winner gets a team point (which may end the match).
        protected void EndRound(int winner, string reason)
        {
            if (RoundStage != Stage.Live && RoundStage != Stage.Prep) return;
            RoundStage = Stage.Over;
            StageEndsAt = Now + OVER_SECONDS;
            RoundWinner = winner;
            RoundReason = reason ?? "";
            MarkDirty();
            Announce($"{TeamName(winner)} wins round {Round}: {RoundReason}", winner);
            OnRoundEnded(winner);
            AddTeamScore(winner, 1);
        }

        protected abstract void OnRoundStart(double now);       // reset the objective, pick the bomb carrier / VIP
        protected virtual void OnRoundLive(double now) { }
        protected abstract void RoundTick(float dt, double now); // PREP and LIVE (rules usually only when Live)
        protected abstract void OnRoundTimeout();
        protected virtual void OnRoundEnded(int winner) { }
        protected abstract string RoundIntro();                  // "attacks", "escorts Alex"

        protected bool Live => RoundStage == Stage.Live;

        // Results banner: round score and how the deciding round ended.
        protected override string ResultText(int winnerTeam, bool onTime, bool draw) =>
            $"{teamScore[1]} : {teamScore[2]} rounds · first to {ScoreTarget}"
            + (string.IsNullOrEmpty(RoundReason) ? "" : " · last round: " + RoundReason);

        // Server: members of a team and how many of them are still in (alive).
        protected void CountTeam(int team, out int members, out int alive)
        {
            members = alive = 0;
            foreach (var p in participants)
            {
                if (p.team != team) continue;
                members++;
                if (p.active) alive++;
            }
        }

        // Server: is this player holding the interact key (E)?
        protected bool UseHeld(ulong id)
        {
            if (testUse != null) return testUse(id);
            return useUntil.TryGetValue(id, out float t) && Time.unscaledTime < t;
        }

        // ───────────────────────── server: respawns ─────────────────────────
        // Default (Bomb): nobody respawns while a round runs; the next round brings everyone back.
        protected override bool HoldRespawn(ulong clientId) => RoundStage != Stage.Waiting;

        protected override float MoveFactor(ulong clientId) => RoundStage == Stage.Prep ? 0f : 1f;

        // Everyone back at their side, alive, for the new round.
        void RespawnEveryone()
        {
            testRespawned.Clear();
            var perTeam = new int[3];
            roundShuffle = UnityEngine.Random.Range(0, 64);
            foreach (var p in participants)
            {
                int team = p.team >= 1 && p.team <= 2 ? p.team : 0;
                var pose = RoundSpawnPose(p.id, team, perTeam[team]++);
                if (testParticipants != null) { testRespawned.Add((p.id, pose)); continue; }
                var h = HealthOf(p.id);
                if (h != null) h.ServerForceRespawn(pose);
            }
        }

        int roundShuffle;

        // Where a player starts the round: their side's spawn points, one each (index = n-th of the team).
        protected virtual Pose RoundSpawnPose(ulong id, int team, int index)
        {
            var pts = SidePoints(team);
            if (pts.Count == 0) return testParticipants != null ? default : PlayerSpawner.GetSpawnFor(id);
            var t = pts[(index + roundShuffle) % pts.Count];
            int lap = index / pts.Count;   // more players than points: around the point
            Vector3 off = lap == 0 ? Vector3.zero : Quaternion.Euler(0, lap * 90f, 0) * Vector3.forward * 1.5f;
            return new Pose(t.position + off, t.rotation);
        }

        // Spawn points of a side (team 1 / 2); every point if the map has none for that team.
        protected static List<Transform> SidePoints(int team)
        {
            var list = new List<Transform>();
            foreach (var sp in SpawnPoint.All) if (sp != null && (team == 0 || sp.team == team)) list.Add(sp.transform);
            if (list.Count == 0) foreach (var sp in SpawnPoint.All) if (sp != null) list.Add(sp.transform);
            return list;
        }

        protected static Health HealthOf(ulong id)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !nm.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null) return null;
            return cl.PlayerObject.GetComponent<Health>();
        }

        protected static Quaternion FaceTowards(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0;
            return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        // ───────────────────────── sync ─────────────────────────
        protected sealed override void WriteState(ref FastBufferWriter w)
        {
            w.WriteValueSafe(Round);
            w.WriteValueSafe((byte)RoundStage);
            w.WriteValueSafe(StageEndsAt);
            w.WriteValueSafe((byte)AttackTeam);
            w.WriteValueSafe((byte)RoundWinner);
            w.WriteValueSafe(RoundReason ?? "");
            WriteRoundState(ref w);
        }

        protected sealed override void ReadState(ref FastBufferReader r)
        {
            r.ReadValueSafe(out int round);
            r.ReadValueSafe(out byte stage);
            r.ReadValueSafe(out double ends);
            r.ReadValueSafe(out byte attack);
            r.ReadValueSafe(out byte winner);
            r.ReadValueSafe(out string reason);
            Round = round; RoundStage = (Stage)stage; StageEndsAt = ends;
            AttackTeam = attack == 2 ? 2 : 1; RoundWinner = winner; RoundReason = reason;
            ReadRoundState(ref r);
        }

        protected abstract void WriteRoundState(ref FastBufferWriter w);
        protected abstract void ReadRoundState(ref FastBufferReader r);

        // ───────────────────────── interact key (E) ─────────────────────────
        const string MSG_USE = "obj_use";   // client -> server: E held (true, repeated) / released (false)
        static readonly Dictionary<ulong, float> useUntil = new Dictionary<ulong, float>();
        static CustomMessagingManager useHandlerOn;
        bool sentUse;
        float lastUseSend;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRoundStatics() { useUntil.Clear(); useHandlerOn = null; }

        static void EnsureUseHandler(NetworkManager nm)
        {
            var cmm = nm.IsListening && nm.IsServer ? nm.CustomMessagingManager : null;
            if (cmm == null || cmm == useHandlerOn) return;
            cmm.RegisterNamedMessageHandler(MSG_USE, (sender, r) =>
            {
                r.ReadValueSafe(out bool held);
                useUntil[sender] = held ? Time.unscaledTime + USE_HOLD_TIMEOUT : 0f;
            });
            useHandlerOn = cmm;
        }

        void UpdateLocalUse(NetworkManager nm)
        {
            var kb = Keyboard.current;
            var h = LocalHealth();
            bool want = kb != null && kb.eKey.isPressed && !HUDManager.InputBlocked && h != null && !h.isDead.Value
                        && RoundStage != Stage.Waiting;
            if (want == sentUse && !(want && Time.unscaledTime - lastUseSend > 0.2f)) return;
            sentUse = want;
            lastUseSend = Time.unscaledTime;
            if (nm.IsServer) { useUntil[nm.LocalClientId] = want ? Time.unscaledTime + USE_HOLD_TIMEOUT : 0f; return; }
            if (!nm.IsConnectedClient) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(want);
            nm.CustomMessagingManager.SendNamedMessage(MSG_USE, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        // ───────────────────────── every frame, every peer ─────────────────────────
        int refilledRound;

        protected sealed override void VisualUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                EnsureUseHandler(nm);
                UpdateLocalUse(nm);
                // new round: full ammo again
                if (Round != refilledRound && RoundStage == Stage.Prep)
                {
                    refilledRound = Round;
                    var po = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
                    var wc = po != null ? po.GetComponent<WeaponController>() : null;
                    if (wc != null) wc.RefillAmmo();
                }
                UpdateSpectator(nm);
            }
            RoundVisualUpdate();
        }

        protected virtual void RoundVisualUpdate() { }

        protected static Health LocalHealth()
        {
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            return po != null ? po.GetComponent<Health>() : null;
        }

        protected static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        protected static int LocalTeam => MatchNet.Instance != null ? MatchNet.Instance.TeamOf(LocalId) : 0;

        // Local: the local player is out for the rest of the round (watch teammates meanwhile).
        protected virtual bool LocalOutForRound => RoundStage != Stage.Waiting;

        protected override bool RespawnRule(out float delay, out string heldText)
        {
            delay = -1f; heldText = null;
            if (RoundStage == Stage.Waiting) return false;
            heldText = RoundStage == Stage.Over ? "Next round in " + Mathf.CeilToInt(StageLeft) + " s" : "Out until the next round";
            return true;
        }

        // ───────────────────────── spectating ─────────────────────────
        Camera specCam, specPlayerCam;
        ulong specTarget = ulong.MaxValue;
        KillCam killCam;
        Vector3 specPos;

        protected override bool IsLocalSpectating => specCam != null;
        public string SpectatingName => specCam != null ? NameOf(specTarget) : null;

        void UpdateSpectator(NetworkManager nm)
        {
            var h = LocalHealth();
            var mn = MatchNet.Instance;
            bool dead = h != null && h.isDead.Value;
            if (killCam == null) killCam = FindAnyObjectByType<KillCam>();
            bool want = dead && LocalOutForRound && mn != null && mn.CurrentPhase == MatchNet.Phase.InMatch
                        && (killCam == null || !killCam.Playing);
            Transform target = null;
            if (want)
            {
                var mates = LivingTeammates(nm.LocalClientId);
                if (mates.Count == 0) want = false;
                else
                {
                    int i = mates.FindIndex(m => m.MatchId == specTarget);
                    var mouse = Mouse.current;
                    var kb = Keyboard.current;
                    bool next = !HUDManager.InputBlocked && ((mouse != null && mouse.leftButton.wasPressedThisFrame)
                                                            || (kb != null && kb.spaceKey.wasPressedThisFrame));
                    if (i < 0) i = 0; else if (next) i = (i + 1) % mates.Count;
                    if (specTarget != mates[i].MatchId && specCam != null) specPos = Vector3.zero;   // cut, don't glide
                    specTarget = mates[i].MatchId;
                    target = mates[i].transform;
                }
            }
            if (!want) { StopSpectating(); return; }

            if (specCam == null)
            {
                var fps = h.GetComponent<FPSController>();
                specPlayerCam = fps != null ? fps.playerCamera : null;
                var go = new GameObject("SpectatorCam");
                go.transform.SetParent(transform, false);
                specCam = go.AddComponent<Camera>();
                if (specPlayerCam != null) { specCam.CopyFrom(specPlayerCam); specCam.depth = specPlayerCam.depth + 2; specPlayerCam.enabled = false; }
                specCam.fieldOfView = GameSettings.FieldOfView;
                var urp = go.AddComponent<UniversalAdditionalCameraData>();
                urp.renderPostProcessing = GameSettings.PostProcessing;
                urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                specPos = Vector3.zero;
            }

            // over the teammate's shoulder, pulled in if a wall is in the way
            Vector3 head = target.position + Vector3.up * 1.7f;
            Vector3 want3 = head - target.forward * 3.2f + Vector3.up * 0.7f + target.right * 0.6f;
            Vector3 d = want3 - head;
            float dist = d.magnitude;
            if (Physics.SphereCast(head, 0.2f, d / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(target))
                want3 = head + d / dist * Mathf.Max(0.3f, hit.distance - 0.1f);
            specPos = specPos == Vector3.zero ? want3 : Vector3.Lerp(specPos, want3, Time.unscaledDeltaTime * 8f);
            specCam.transform.position = specPos;
            Vector3 look = head + target.forward * 6f - specPos;
            if (look.sqrMagnitude > 0.01f) specCam.transform.rotation = Quaternion.LookRotation(look);
        }

        static List<Health> LivingTeammates(ulong me)
        {
            var list = new List<Health>();
            var mn = MatchNet.Instance;
            int team = mn != null ? mn.TeamOf(me) : 0;
            foreach (var h in FindObjectsByType<Health>())
            {
                if (!h.IsSpawned || !h.IsCombatant || h.isDead.Value || h.MatchId == me) continue;
                if (team != 0 && mn.TeamOf(h.MatchId) != team) continue;
                list.Add(h);
            }
            list.Sort((a, b) => a.MatchId.CompareTo(b.MatchId));
            return list;
        }

        void StopSpectating()
        {
            if (specCam != null) Destroy(specCam.gameObject);
            specCam = null;
            if (specPlayerCam != null) specPlayerCam.enabled = true;
            specPlayerCam = null;
        }

        void OnDisable() => StopSpectating();

        // ───────────────────────── HUD ─────────────────────────
        TextMeshProUGUI roundLabel, clockCaption, clockText, roleTag, statusLine, bannerTitle, bannerSub, specLine;
        Image roleBg, bannerBg;
        GameObject statusChip, banner, specChip;
        // progress ring (centre of the screen, under the crosshair)
        GameObject ring;
        Image ringFill;
        TextMeshProUGUI ringPct, ringText;

        public sealed override void BuildHud(RectTransform widget, RectTransform markers)
        {
            var row = HRow(widget, "Round", 10, TextAnchor.MiddleCenter);
            row.padding = new RectOffset(12, 6, 3, 3);
            row.gameObject.AddComponent<Image>().color = Scrim(0.82f);
            roundLabel = Label(row.transform, "ROUND 1", F.CondBold, 16, Fg, TextAlignmentOptions.MidlineLeft, 0.06f);
            Box(row.transform, "Sep", LineStrong).LE(1, 20);
            var clock = HRow(row.transform, "Clock", 6, TextAnchor.MiddleLeft);
            clockCaption = Label(clock.transform, "", F.BodyBold, 10, Muted, TextAlignmentOptions.MidlineLeft, 0.14f);
            clockText = Label(clock.transform, "0:00", F.MonoBold, 18, Fg);
            var tag = HRow(row.transform, "Role", 0, TextAnchor.MiddleCenter);
            tag.padding = new RectOffset(8, 8, 2, 2);
            roleBg = tag.gameObject.AddComponent<Image>();
            roleTag = Label(tag.transform, "", F.BodyBold, 11, Ground, TextAlignmentOptions.Center, 0.14f);

            var chip = HRow(widget, "Status", 0, TextAnchor.MiddleCenter);
            chip.padding = new RectOffset(10, 10, 3, 3);
            chip.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            statusLine = Label(chip.transform, "", F.BodyBold, 12, Fg, TextAlignmentOptions.Center, 0.1f);
            statusChip = chip.gameObject;

            var sc = HRow(widget, "Spectating", 0, TextAnchor.MiddleCenter);
            sc.padding = new RectOffset(10, 10, 3, 3);
            sc.gameObject.AddComponent<Image>().color = Scrim(0.78f);
            specLine = Label(sc.transform, "", F.BodySemi, 12, TextSoft, TextAlignmentOptions.Center);
            specChip = sc.gameObject;
            specChip.SetActive(false);

            var b = VCol(widget, "Banner", 0, TextAnchor.MiddleCenter, false);
            b.padding = new RectOffset(18, 18, 5, 7);
            bannerBg = b.gameObject.AddComponent<Image>();
            bannerTitle = Label(b.transform, "", F.CondBlack, 24, Ground, TextAlignmentOptions.Center, 0.04f);
            bannerSub = Label(b.transform, "", F.BodySemi, 12, Ground, TextAlignmentOptions.Center);
            banner = b.gameObject;
            banner.SetActive(false);

            var r = Rect(markers, "UseRing").At(0.5f, 0.5f, 0, -96, 220, 110);
            var back = Box(r, "Back", A(Ground, 0.55f));
            back.sprite = RingSprite;
            back.rectTransform.At(0.5f, 1f, 0, 0, 64, 64);
            ringFill = Box(r, "Fill", Accent);
            ringFill.sprite = RingSprite;
            ringFill.type = Image.Type.Filled;
            ringFill.fillMethod = Image.FillMethod.Radial360;
            ringFill.fillOrigin = (int)Image.Origin360.Top;
            ringFill.fillClockwise = true;
            ringFill.rectTransform.At(0.5f, 1f, 0, 0, 64, 64);
            ringPct = Label(r, "", F.CondBlack, 18, Fg, TextAlignmentOptions.Center);
            ringPct.rectTransform.At(0.5f, 1f, 0, -20, 64, 24);
            ringText = Label(r, "", F.BodyBold, 13, Fg, TextAlignmentOptions.Top, 0.12f);
            ringText.rectTransform.At(0.5f, 1f, 0, -70, 300, 24);
            var sh = ringText.gameObject.AddComponent<Shadow>();
            sh.effectColor = A(Ground, 0.9f);
            sh.effectDistance = new Vector2(1, -1);
            ring = r.gameObject;
            ring.SetActive(false);

            BuildModeHud(widget, markers);
            banner.transform.SetAsLastSibling();
        }

        public sealed override void UpdateHud(Camera cam, RectTransform markers)
        {
            int myTeam = LocalTeam;
            bool attack = myTeam == AttackTeam;
            float left = StageLeft;

            roundLabel.text = RoundStage == Stage.Waiting ? "WAITING" : "ROUND " + Round;
            bool fuse = TimerOverride(out float fuseLeft, out string fuseCaption);
            switch (RoundStage)
            {
                case Stage.Waiting: clockCaption.text = ""; clockText.text = "--:--"; clockText.color = Muted; break;
                case Stage.Prep: clockCaption.text = "STARTS IN"; clockText.text = MatchNet.FormatClock(left); clockText.color = Accent; break;
                case Stage.Over: clockCaption.text = "NEXT"; clockText.text = MatchNet.FormatClock(left); clockText.color = Muted; break;
                default:
                    if (fuse)
                    {
                        clockCaption.text = fuseCaption;
                        clockText.text = MatchNet.FormatClock(fuseLeft);
                        clockText.color = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f || fuseLeft > 10f ? Danger : Fg;
                    }
                    else
                    {
                        clockCaption.text = "";
                        clockText.text = MatchNet.FormatClock(left);
                        clockText.color = left <= 10f ? Danger : left <= 30f ? Accent : Fg;
                    }
                    break;
            }
            bool hasRole = myTeam == 1 || myTeam == 2;
            roleBg.transform.gameObject.SetActive(hasRole && RoundStage != Stage.Waiting);
            roleTag.text = attack ? AttackRole : DefendRole;
            roleBg.color = hasRole ? TeamColor(myTeam) : Muted;

            // banner: round start / round result
            if (RoundStage == Stage.Prep)
            {
                banner.SetActive(true);
                bannerBg.color = A(hasRole ? TeamColor(myTeam) : Accent, 0.92f);
                bannerTitle.text = $"ROUND {Round} · YOU {(attack ? AttackRole : DefendRole)}";
                bannerSub.text = RoleBriefing(attack);
            }
            else if (RoundStage == Stage.Over && RoundWinner != 0)
            {
                banner.SetActive(true);
                bool won = RoundWinner == myTeam;
                bannerBg.color = A(TeamColor(RoundWinner), 0.92f);
                bannerTitle.text = hasRole ? (won ? "ROUND WON" : "ROUND LOST") : TeamName(RoundWinner) + " WINS THE ROUND";
                bannerSub.text = $"{TeamName(RoundWinner)} · {RoundReason}";
            }
            else banner.SetActive(false);

            string status = RoundStage == Stage.Live || RoundStage == Stage.Prep ? StatusText(attack) : null;
            statusChip.SetActive(!string.IsNullOrEmpty(status) && RoundStage != Stage.Prep);
            if (!string.IsNullOrEmpty(status)) statusLine.text = status;

            specChip.SetActive(specCam != null);
            if (specCam != null)
                specLine.text = $"SPECTATING <color={Html(TeamColor(myTeam))}>{NameOf(specTarget)}</color>  <color={Html(Muted)}>· LMB next</color>";

            // progress ring
            string verb = null; float progress = 0f; bool doing = false;
            bool show = RoundStage == Stage.Live && LocalAction(out verb, out progress, out doing);
            ring.SetActive(show);
            if (show)
            {
                ringFill.fillAmount = doing ? Mathf.Clamp01(progress) : 0f;
                ringFill.color = Accent;
                ringPct.text = doing ? Mathf.FloorToInt(Mathf.Clamp01(progress) * 100f) + "%" : "E";
                ringText.text = doing ? verb + "…" : $"HOLD <color={Html(Accent)}>E</color> TO {verb}";
            }

            UpdateModeHud(cam, markers, myTeam, attack);
        }

        protected abstract void BuildModeHud(RectTransform widget, RectTransform markers);
        protected abstract void UpdateModeHud(Camera cam, RectTransform markers, int myTeam, bool attack);
        // One line for the round-start banner ("Plant the bomb at A or B").
        protected abstract string RoleBriefing(bool attack);
        // Status chip under the round row, or null.
        protected abstract string StatusText(bool attack);
        // Local player can do (or is doing) the mode's action here: verb ("PLANT"), progress 0..1, doing = holding it.
        protected abstract bool LocalAction(out string verb, out float progress, out bool doing);
        // The clock shows something else than the round time (bomb fuse).
        protected virtual bool TimerOverride(out float seconds, out string caption) { seconds = 0; caption = null; return false; }

        // ───────────────────────── visuals ─────────────────────────
        // A floor disc + a ring of posts marking an area (bomb site, extraction).
        protected Transform BuildZone(string name, Vector3 at, float radius, Color c, int posts = 20)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);
            root.position = at;
            Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.04f, 0), new Vector3(radius * 2f, 0.01f, radius * 2f), Mat(A(c, 0.14f), true), "Area");
            for (int i = 0; i < posts; i++)
            {
                float a = i * Mathf.PI * 2f / posts;
                Prim(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * radius, 0.1f, Mathf.Sin(a) * radius),
                     new Vector3(0.16f, 0.2f, 0.16f), Mat(c), "Edge");
            }
            return root;
        }

        // A big 3D letter on a pole (bomb sites A / B).
        protected static void Letter(Transform parent, string text, Color c, float height)
        {
            var pole = Prim(PrimitiveType.Cylinder, parent, new Vector3(0, height * 0.5f, 0), new Vector3(0.08f, height * 0.5f, 0.08f), Mat(Hex("#D9D6CC")), "Pole");
            var board = Prim(PrimitiveType.Cube, parent, new Vector3(0, height + 0.45f, 0), new Vector3(1.0f, 1.0f, 0.06f), Mat(Hex("#2A2D26")), "Board");
            foreach (float side in new[] { 1f, -1f })
            {
                var go = new GameObject("Letter");
                go.transform.SetParent(board.parent, false);
                go.transform.localPosition = board.localPosition + new Vector3(0, 0, -0.04f * side);
                go.transform.localRotation = side > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                var t = go.AddComponent<TextMeshPro>();
                t.font = UIKit.Font(F.CondBlack, out _);
                t.text = text;
                t.fontSize = 9;
                t.color = c;
                t.alignment = TextAlignmentOptions.Center;
                t.rectTransform.sizeDelta = new Vector2(1f, 1f);
            }
            _ = pole;
        }
    }
}
