using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Collections;
using System.Collections;

namespace AirsoftFPS
{
    public class WeaponController : NetworkBehaviour
    {
        [Header("Setup")]
        public Transform weaponHolder;
        public Transform muzzlePoint;
        public LayerMask hitMask = ~0;

        [Header("Weapons")]
        public WeaponData[] weapons;

        NetworkVariable<int> netWeaponIndex = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        int currentIndex;
        int ammoInMag;
        int ammoReserve;
        float nextFireTime;
        bool isReloading;
        float reloadEndTime;
        int reloadSfx;           // reload sounds played so far (0 = mag out, 1 = mag in, 2 = rack)
        int reloadCutWeapon = -1;   // weapon whose reload was cut short by dying (-1 = none): reloaded after respawn
        float lastDeadTime;
        bool isAiming;

        FPSController fpsController;
        Camera playerCam;

        // Per-weapon ammo tracking
        int[] magAmmos;
        int[] reserveAmmos;

        // HUD callback
        public System.Action<string, int, int, int> OnAmmoChanged;
        public System.Action<Vector3> OnHitMarker;

        // ───── Animation / effects state ─────
        Vector3 weaponRestPos;
        Quaternion weaponRestRot;
        Vector3 recoilPos;       // current positional kick
        Vector3 recoilRot;       // current rotational kick (euler degrees)
        float switchLower;       // 1 = fully lowered (just switched), lerps to 0
        float reloadLerp;        // smoothed reload dip
        float bobTimer;
        bool haveRest;

        // Muzzle flash
        Light muzzleLight;
        GameObject muzzleFlashObj;
        float muzzleFlashTimer;

        // Low-poly models (owner view): muzzle position per weapon slot, in slot space
        Vector3[] modelMuzzle;
        bool[] hasModel;

        // ───── Server-authoritative hit validation ─────
        // The owner flies its BBs locally for responsiveness and only reports *what* they hit;
        // the server decides damage (from its own WeaponData) and rejects impossible hits.
        // Every trigger pull is announced first (ShootRpc: origin, direction, weapon, seed), so the
        // server can re-fly the same BBs and check a reported hit against their real path and timing.
        const float FIRE_RATE_TOLERANCE = 0.1f;   // allow 10% faster than fireRate (clock/jitter)
        const float SHOT_BURST_WINDOW = 0.2f;     // seconds of shots that may arrive bunched up by jitter
        const float SWITCH_GRACE = 0.75f;         // old weapon still accepted this long after a switch (RTT)
        const float ORIGIN_TOLERANCE = 3.5f;      // reported eye position vs server's (lagged) view of the shooter
        const float TARGET_TOLERANCE = 3f;        // reported hit point vs server's view of the target's colliders
        const float RANGE_TOLERANCE = 0.5f;
        const float LOS_END_MARGIN = 0.3f;        // ignore geometry this close to either end of a LOS ray
        const float PATH_TOLERANCE = 0.6f;        // hit point vs the server's re-flown BB path
        const float TOF_TOLERANCE = 0.25f;        // a hit may arrive this much sooner than the BB's flight time (jitter)
        const float LATE_TOLERANCE = 1.5f;        // ... and this much after the longest possible flight (lag spikes)
        const int SHOT_HISTORY = 64;              // shots remembered for validation (> fireRate * MAX_FLIGHT_TIME)

        struct ShotRecord
        {
            public int seq;          // 0 = empty slot
            public bool valid;       // passed the fire-time checks
            public int weapon;
            public Vector3 origin, dir;
            public uint seed;
            public bool aimed;
            public float time;       // server receive time (Time.unscaledTime)
            public int usedPellets;  // bitmask of pellets that already scored
        }

        int localShotSeq;                 // owner: one id per trigger pull (all pellets share it)
        int svLastShot;                   // server: last shot id seen from this shooter
        readonly ShotRecord[] svShots = new ShotRecord[SHOT_HISTORY];
        float svShotTokens = -1f;         // server: fire-rate token bucket (-1 = full on first use)
        float svTokenTime;
        int svPrevWeapon = -1;            // server: weapon before the last switch, for SWITCH_GRACE
        float svSwitchTime = -100f;
        Health ownHealth;

        static readonly RaycastHit[] s_losHits = new RaycastHit[16];

        static Material s_glowMat;

        public WeaponData CurrentWeapon => weapons != null && currentIndex < weapons.Length ? weapons[currentIndex] : null;
        public bool IsReloading => isReloading;
        public bool IsAiming => isAiming;

        // Live ammo state so the HUD can poll each frame (robust against spawn-order
        // timing in builds, where a one-shot read can land before OnNetworkSpawn).
        public bool AmmoReady => weapons != null && currentIndex >= 0 && currentIndex < weapons.Length && magAmmos != null;
        public int CurrentMag => ammoInMag;
        public int CurrentReserve => ammoReserve;
        public int CurrentSlot => currentIndex + 1;
        public string CurrentWeaponName => AmmoReady ? weapons[currentIndex].weaponName : "";

        static readonly Key[] digitKeys = new Key[]
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0
        };

        public override void OnNetworkSpawn()
        {
            if (weapons == null || weapons.Length == 0)
                weapons = WeaponData.GetAllWeapons();

            magAmmos = new int[weapons.Length];
            reserveAmmos = new int[weapons.Length];
            for (int i = 0; i < weapons.Length; i++)
            {
                magAmmos[i] = weapons[i].magSize;
                reserveAmmos[i] = weapons[i].maxReserve;
            }

            fpsController = GetComponent<FPSController>();
            playerCam = GetComponentInChildren<Camera>();
            ownHealth = GetComponent<Health>();

            // Capture weapon rest pose for recoil animation
            if (weaponHolder)
            {
                weaponRestPos = weaponHolder.localPosition;
                weaponRestRot = weaponHolder.localRotation;
                haveRest = true;
            }

            // Cache muzzle light + build a visible flash
            if (muzzlePoint)
            {
                muzzleLight = muzzlePoint.GetComponent<Light>();
                if (muzzleLight == null) muzzleLight = muzzlePoint.GetComponentInChildren<Light>(true);
                BuildMuzzleFlash();
            }

            if (IsOwner) ApplyWeaponModels();

            netWeaponIndex.OnValueChanged += OnWeaponChanged;
            SwitchToWeapon(netWeaponIndex.Value);
        }

        // Swap the primitive placeholder guns for the low-poly models from Resources/AirsoftGuns.
        void ApplyWeaponModels()
        {
            if (weaponHolder == null) return;
            modelMuzzle = new Vector3[weapons.Length];
            hasModel = new bool[weapons.Length];
            for (int i = 0; i < weapons.Length && i < weaponHolder.childCount; i++)
            {
                var slot = weaponHolder.GetChild(i);
                if (muzzleFlashObj != null && slot == muzzleFlashObj.transform) continue;
                AirsoftGuns.Gun g = null;
                try { g = AirsoftGuns.Build(weapons[i].model, weapons[i].modelLength, slot); }
                catch (System.Exception e) { Debug.LogWarning("[Airsoft] gun model " + weapons[i].model + ": " + e.Message); }
                if (g == null) continue;

                // hide the old placeholder parts
                foreach (var r in slot.GetComponentsInChildren<Renderer>(true))
                    if (!r.transform.IsChildOf(g.root.transform)) r.enabled = false;

                foreach (var r in g.root.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // the slot origin is the hand position: nudge the grip slightly back/down
                g.root.transform.localPosition = new Vector3(0f, -0.02f, 0.05f);
                modelMuzzle[i] = g.root.transform.localPosition + g.muzzleLocal;
                hasModel[i] = true;
            }
        }

        void BuildMuzzleFlash()
        {
            muzzleFlashObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = muzzleFlashObj.GetComponent<Collider>();
            if (col) Destroy(col);
            muzzleFlashObj.name = "MuzzleFlashVisual";
            muzzleFlashObj.transform.SetParent(muzzlePoint);
            muzzleFlashObj.transform.localPosition = Vector3.zero;
            muzzleFlashObj.transform.localScale = new Vector3(0.14f, 0.14f, 0.28f);
            var rend = muzzleFlashObj.GetComponent<Renderer>();
            rend.sharedMaterial = GlowMat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            muzzleFlashObj.SetActive(false);
        }

        void OnWeaponChanged(int prev, int next)
        {
            SwitchToWeapon(next);
            if (IsOwner) AirsoftAudio.Play2D("switch", 0.5f, AirsoftAudio.Bus.Sfx, 0.04f);
            else AirsoftAudio.PlayAt("switch", transform.position + Vector3.up * 1.3f, 0.4f, 1.5f, 12f);
        }

        void SwitchToWeapon(int idx)
        {
            currentIndex = idx;
            ammoInMag = magAmmos[idx];
            ammoReserve = reserveAmmos[idx];
            isReloading = false;
            reloadCutWeapon = -1;
            isAiming = false;
            nextFireTime = Time.time + 0.25f;
            switchLower = 1f; // animate weapon raising

            if (fpsController)
                fpsController.SetSpeedMultiplier(weapons[idx].moveSpeedMult);

            if (weaponHolder)
            {
                for (int i = 0; i < weaponHolder.childCount; i++)
                {
                    var child = weaponHolder.GetChild(i);
                    if (muzzleFlashObj != null && child == muzzleFlashObj.transform) continue;
                    child.gameObject.SetActive(i == idx);
                }
            }

            NotifyAmmo();
        }

        void Update()
        {
            // Muzzle flash runs on ALL clients so remote players see your shots
            if (muzzleFlashTimer > 0f)
            {
                muzzleFlashTimer -= Time.deltaTime;
                if (muzzleFlashTimer <= 0f)
                {
                    if (muzzleFlashObj) muzzleFlashObj.SetActive(false);
                    if (muzzleLight) muzzleLight.enabled = false;
                }
            }

            if (!IsOwner) return;
            if (Time.timeScale == 0) return;
            bool dead = ownHealth != null && ownHealth.IsSpawned && ownHealth.isDead.Value;
            if (HUDManager.InputBlocked || dead || (ownHealth != null && ownHealth.IsOut))
            {
                // a menu is open, Milsim HIT, or dead (death screen / spectating): no shooting/switching,
                // drop out of the scope
                isAiming = false;
                if (playerCam) playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, GameSettings.FieldOfView, Time.deltaTime * 10f);
                // dead: drop a reload in progress (restarted on respawn); otherwise let it finish
                if (dead)
                {
                    if (isReloading) reloadCutWeapon = currentIndex;
                    isReloading = false;
                    lastDeadTime = Time.time;
                }
                else if (isReloading) HandleReload();
                return;
            }

            HandleWeaponSwitch();
            // A moment after respawning (forced weapon changes and the round-start refill come first), finish the
            // reload that dying cut short, if that weapon is still in hand.
            if (reloadCutWeapon >= 0 && Time.time - lastDeadTime >= 0.3f)
            {
                if (reloadCutWeapon == currentIndex && !isReloading &&
                    ammoInMag < weapons[currentIndex].magSize && ammoReserve > 0) StartReload();
                reloadCutWeapon = -1;
            }
            HandleReload();
            HandleAim();
            HandleFire();
        }

        // ───── Weapon view animation (owner only) ─────
        void LateUpdate()
        {
            if (!IsOwner || !haveRest || weaponHolder == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (muzzlePoint && hasModel != null && currentIndex < hasModel.Length && hasModel[currentIndex]
                && currentIndex < weaponHolder.childCount)
                muzzlePoint.position = weaponHolder.GetChild(currentIndex).TransformPoint(modelMuzzle[currentIndex]);

            // Recoil recovery
            recoilPos = Vector3.Lerp(recoilPos, Vector3.zero, dt * 11f);
            recoilRot = Vector3.Lerp(recoilRot, Vector3.zero, dt * 9f);

            // Raise weapon after switching
            switchLower = Mathf.Lerp(switchLower, 0f, dt * 8f);

            // Reload dip
            reloadLerp = Mathf.Lerp(reloadLerp, isReloading ? 1f : 0f, dt * 7f);

            // Weapon bob while moving
            Vector3 bob = Vector3.zero;
            var kb = Keyboard.current;
            bool moving = kb != null && (kb.wKey.isPressed || kb.aKey.isPressed ||
                                         kb.sKey.isPressed || kb.dKey.isPressed);
            if (moving)
            {
                bool sprint = fpsController != null && fpsController.IsSprinting;
                float bobSpeed = sprint ? 13f : 8.5f;
                float bobAmt = sprint ? 0.016f : 0.009f;
                bobTimer += dt * bobSpeed;
                bob.x = Mathf.Cos(bobTimer) * bobAmt;
                bob.y = Mathf.Abs(Mathf.Sin(bobTimer)) * bobAmt;
            }
            else
            {
                bobTimer = 0f;
                bob.y = Mathf.Sin(Time.time * 1.4f) * 0.0025f; // idle sway
            }

            // Lowered offset from switch + reload
            float lower = Mathf.Clamp01(switchLower + reloadLerp);
            Vector3 lowerOffset = new Vector3(0f, -0.14f, -0.05f) * lower;
            Quaternion reloadTilt = Quaternion.Euler(reloadLerp * 30f, reloadLerp * 10f, 0f);

            weaponHolder.localPosition = weaponRestPos + recoilPos + lowerOffset + bob;
            weaponHolder.localRotation = weaponRestRot * Quaternion.Euler(recoilRot) * reloadTilt;
        }

        void HandleWeaponSwitch()
        {
            // Gun Game: your weapon is locked to your progression level; no manual switching.
            if (MatchNet.Instance != null && MatchNet.Instance.Mode == MatchNet.GameModeType.GunGame)
            {
                int lvl = Mathf.Clamp(MatchNet.Instance.GunLevelOf(OwnerClientId), 0, weapons.Length - 1);
                if (lvl != currentIndex) SwitchWeaponRpc(lvl);
                return;
            }

            // objective modes (VIP escort: sidearms only): off-limits weapons are skipped
            if (!ObjectiveMode.WeaponAllowed(OwnerClientId, weapons[currentIndex]))
            {
                int ok = NextAllowedWeapon(currentIndex, 1);
                if (ok != currentIndex) SwitchWeaponRpc(ok);
                return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            for (int i = 0; i < digitKeys.Length && i < weapons.Length; i++)
            {
                int weaponIdx = i < 9 ? i : 9;
                if (kb[digitKeys[i]].wasPressedThisFrame)
                {
                    if (weaponIdx != currentIndex && ObjectiveMode.WeaponAllowed(OwnerClientId, weapons[weaponIdx]))
                        SwitchWeaponRpc(weaponIdx);
                    return;
                }
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.y.ReadValue();
            if (scroll > 1f)
            {
                int next = NextAllowedWeapon((currentIndex + 1) % weapons.Length, 1);
                if (next != currentIndex) SwitchWeaponRpc(next);
            }
            else if (scroll < -1f)
            {
                int prev = NextAllowedWeapon((currentIndex - 1 + weapons.Length) % weapons.Length, -1);
                if (prev != currentIndex) SwitchWeaponRpc(prev);
            }
        }

        // First weapon from `from` (stepping by dir) this player may use right now (ObjectiveMode.WeaponAllowed).
        int NextAllowedWeapon(int from, int dir)
        {
            for (int k = 0; k < weapons.Length; k++)
            {
                int i = ((from + dir * k) % weapons.Length + weapons.Length) % weapons.Length;
                if (ObjectiveMode.WeaponAllowed(OwnerClientId, weapons[i])) return i;
            }
            return currentIndex;
        }

        // Owner: full magazines and reserves again (round modes, when a new round starts).
        public void RefillAmmo()
        {
            if (weapons == null || magAmmos == null) return;
            for (int i = 0; i < weapons.Length; i++)
            {
                magAmmos[i] = weapons[i].magSize;
                reserveAmmos[i] = weapons[i].maxReserve;
            }
            ammoInMag = magAmmos[currentIndex];
            ammoReserve = reserveAmmos[currentIndex];
            isReloading = false;
            reloadCutWeapon = -1;
            NotifyAmmo();
        }

        [Rpc(SendTo.Server)]
        void SwitchWeaponRpc(int idx, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            // Gun Game: the weapon is dictated by the server's kill count, not the client.
            if (MatchNet.Instance != null && MatchNet.Instance.Mode == MatchNet.GameModeType.GunGame &&
                idx != Mathf.Clamp(MatchNet.Instance.GunLevelOf(OwnerClientId), 0, weapons.Length - 1))
                return;
            if (idx >= 0 && idx < weapons.Length && !ObjectiveMode.WeaponAllowed(OwnerClientId, weapons[idx])) return;
            if (idx >= 0 && idx < weapons.Length)
            {
                magAmmos[currentIndex] = ammoInMag;
                reserveAmmos[currentIndex] = ammoReserve;
                if (idx != netWeaponIndex.Value)
                {
                    svPrevWeapon = netWeaponIndex.Value;
                    svSwitchTime = Time.unscaledTime;
                }
                netWeaponIndex.Value = idx;
            }
        }

        void HandleReload()
        {
            if (isReloading)
            {
                // mag in a bit past halfway, then (gas / spring guns) the slide or bolt
                var rw = weapons[currentIndex];
                float done = 1f - (reloadEndTime - Time.time) / Mathf.Max(0.01f, rw.reloadTime);
                if (reloadSfx == 0 && done >= 0.55f) { reloadSfx = 1; AirsoftAudio.Play2D("reload_in", 0.6f, AirsoftAudio.Bus.Sfx, 0.04f); }
                if (reloadSfx == 1 && done >= 0.85f && AirsoftAudio.ShotKind(rw) != "aeg")
                { reloadSfx = 2; AirsoftAudio.Play2D("reload_rack", 0.55f, AirsoftAudio.Bus.Sfx, 0.04f); }
                if (Time.time >= reloadEndTime)
                {
                    int need = weapons[currentIndex].magSize - ammoInMag;
                    int take = Mathf.Min(need, ammoReserve);
                    ammoInMag += take;
                    ammoReserve -= take;
                    magAmmos[currentIndex] = ammoInMag;
                    reserveAmmos[currentIndex] = ammoReserve;
                    isReloading = false;
                    NotifyAmmo();
                }
                return;
            }

            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame &&
                ammoInMag < weapons[currentIndex].magSize && ammoReserve > 0)
                StartReload();
        }

        void StartReload()
        {
            isReloading = true;
            reloadEndTime = Time.time + weapons[currentIndex].reloadTime;
            reloadSfx = 0;
            AirsoftAudio.Play2D("reload_out", 0.55f, AirsoftAudio.Bus.Sfx, 0.04f);
        }

        void HandleAim()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (weapons[currentIndex].aimFOV > 0 && mouse.rightButton.isPressed && !isReloading)
            {
                isAiming = true;
                if (playerCam) playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, weapons[currentIndex].aimFOV, Time.deltaTime * 10f);
            }
            else
            {
                isAiming = false;
                if (playerCam) playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, GameSettings.FieldOfView, Time.deltaTime * 10f);
            }
        }

        void HandleFire()
        {
            if (isReloading) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            var w = weapons[currentIndex];
            bool fireInput = w.isAutomatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame;
            if (mouse.leftButton.wasPressedThisFrame && ammoInMag <= 0 && Time.time >= nextFireTime)
            {
                AirsoftAudio.Play2D("dryfire", 0.6f, AirsoftAudio.Bus.Sfx, 0.05f, 0.1f);   // empty: click
                if (ammoReserve > 0) { StartReload(); return; }                              // and reload
            }

            if (fireInput && Time.time >= nextFireTime && ammoInMag > 0)
            {
                nextFireTime = Time.time + 1f / w.fireRate;
                ammoInMag--;
                magAmmos[currentIndex] = ammoInMag;
                NotifyAmmo();

                FireBBs(w);

                // Camera recoil (player look kicks up)
                if (fpsController)
                    fpsController.ApplyRecoil(w.recoilUp * 0.3f, w.recoilSide * 0.3f);

                // Weapon model kick (owner-local animation)
                ApplyWeaponKick(w);

                if (ammoInMag <= 0 && ammoReserve > 0)
                    StartReload();
            }
        }

        void ApplyWeaponKick(WeaponData w)
        {
            float kickBack = Mathf.Lerp(0.03f, 0.08f, Mathf.Clamp01(w.recoilUp / 8f));
            recoilPos += new Vector3(Random.Range(-0.008f, 0.008f), 0.012f, -kickBack);
            recoilRot += new Vector3(-w.recoilUp * 1.1f, Random.Range(-w.recoilSide, w.recoilSide), Random.Range(-1f, 1f));

            // Clamp so rapid fire doesn't bury the gun
            recoilPos.z = Mathf.Max(recoilPos.z, -0.13f);
            recoilPos.y = Mathf.Min(recoilPos.y, 0.05f);
            recoilRot.x = Mathf.Max(recoilRot.x, -22f);
        }

        // One trigger pull: announce it to the server first (so it can validate the hits that follow
        // and show the BBs to everyone else), then fly the BBs locally.
        void FireBBs(WeaponData w)
        {
            if (!playerCam) return;

            localShotSeq++;
            int shot = localShotSeq;
            int weaponIdx = currentIndex;
            Vector3 origin = playerCam.transform.position;
            Vector3 dir = playerCam.transform.forward;
            uint seed = (uint)Random.Range(int.MinValue, int.MaxValue);
            bool aimed = isAiming;

            ShootRpc(shot, weaponIdx, origin, dir, seed, aimed);
            AirsoftAudio.Shot(w, origin, true);
            SpawnBBs(w, origin, dir, seed, aimed, hit =>
            {
                if (this == null) return;   // player despawned while the BB was in the air
                SpawnImpact(hit.point, hit.normal);
                var hitNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (hitNetObj != null && hitNetObj.GetComponent<Health>() != null && IsSpawned)
                    HitRpc(hitNetObj.NetworkObjectId, shot, weaponIdx, origin, hit.point);
            });
        }

        // Launches every pellet of a shot. `onHit` == null -> visual-only copy (remote clients).
        void SpawnBBs(WeaponData w, Vector3 origin, Vector3 dir, uint seed, bool aimed,
                      BBProjectile.HitHandler onHit)
        {
            // The BB is drawn leaving the gun and slides onto the true eye-line path over a few metres.
            Vector3 muzzle = muzzlePoint && (muzzlePoint.position - origin).sqrMagnitude < 2.25f
                ? muzzlePoint.position : origin;
            int pellets = Mathf.Max(1, w.pelletsPerShot);
            if (onHit == null) onHit = hit => { if (this != null) SpawnImpact(hit.point, hit.normal); };
            for (int p = 0; p < pellets; p++)
                BBProjectile.Fire(w, origin, BBBallistics.PelletDirection(w, dir, seed, p, aimed), muzzle,
                                  hitMask, transform, onHit);
        }

        // ───── Visual effects ─────
        void SpawnImpact(Vector3 point, Vector3 normal)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col) DestroyImmediate(col);   // now, not at frame end: same-frame raycasts (dropped flag/bomb) hit it
            go.name = "Impact";
            go.transform.position = point + normal * 0.02f;
            go.transform.localScale = Vector3.one * 0.08f;
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = GlowMat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("ImpactLight");
            lightGo.transform.SetParent(go.transform);
            lightGo.transform.localPosition = Vector3.zero;
            var l = lightGo.AddComponent<Light>();
            l.color = new Color(1f, 0.7f, 0.3f);
            l.range = 1.5f;
            l.intensity = 2f;

            StartCoroutine(FadeImpact(go, 0.18f));
        }

        IEnumerator FadeImpact(GameObject go, float dur)
        {
            Vector3 startScale = go.transform.localScale;
            var light = go.GetComponentInChildren<Light>();
            float t = 0f;
            while (t < dur && go != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(1f - t / dur);
                go.transform.localScale = startScale * k;
                if (light) light.intensity = 2f * k;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // Fire-time checks happen here, once per trigger pull: sender, alive, weapon, fire rate and
        // origin. The shot is remembered so HitRpc can re-fly its BBs, then relayed to everyone.
        [Rpc(SendTo.Server)]
        void ShootRpc(int shotSeq, int weaponIdx, Vector3 origin, Vector3 dir, uint seed, bool aimed,
                      RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;   // no shooting "as" someone else
            if (shotSeq <= svLastShot) { RejectShot(shotSeq, "stale shot"); return; }
            svLastShot = shotSeq;

            ref var rec = ref svShots[shotSeq & (SHOT_HISTORY - 1)];
            rec = new ShotRecord { seq = shotSeq, valid = false };

            if (ownHealth != null && ownHealth.isDead.Value) { RejectShot(shotSeq, "shooter is dead"); return; }
            if (ownHealth != null && ownHealth.IsOut) { RejectShot(shotSeq, "shooter is HIT (Milsim)"); return; }

            // Weapon: must be the one the server has equipped (or the previous one right after a switch).
            bool current = weaponIdx == netWeaponIndex.Value;
            bool recentPrev = weaponIdx == svPrevWeapon && Time.unscaledTime - svSwitchTime < SWITCH_GRACE;
            if (weaponIdx < 0 || weaponIdx >= weapons.Length || !(current || recentPrev))
            { RejectShot(shotSeq, "weapon " + weaponIdx + " not equipped"); return; }
            var w = weapons[weaponIdx];
            if (!ObjectiveMode.WeaponAllowed(OwnerClientId, w)) { RejectShot(shotSeq, "weapon not allowed in this role"); return; }

            // Fire rate: each trigger pull costs a token; pellets of the same shot share it.
            float now = Time.unscaledTime;
            float cap = 1f + SHOT_BURST_WINDOW * w.fireRate;
            svShotTokens = svShotTokens < 0f ? cap
                : Mathf.Min(cap, svShotTokens + (now - svTokenTime) * w.fireRate * (1f + FIRE_RATE_TOLERANCE));
            svTokenTime = now;
            if (svShotTokens < 1f) { RejectShot(shotSeq, "firing faster than " + w.fireRate + "/s"); return; }
            svShotTokens -= 1f;

            if (!IsFinite(origin) || !IsFinite(dir) || dir.sqrMagnitude < 0.25f || dir.sqrMagnitude > 4f)
            { RejectShot(shotSeq, "bad origin/direction"); return; }
            if (!OriginPlausible(origin)) { RejectShot(shotSeq, "origin too far from shooter"); return; }

            rec.valid = true;
            rec.weapon = weaponIdx;
            rec.origin = origin;
            rec.dir = dir.normalized;
            rec.seed = seed;
            rec.aimed = aimed;
            rec.time = now;
            rec.usedPellets = 0;

            if (ownHealth != null) ownHealth.EndSpawnProtection();   // firing ends spawn protection
            ShootEffectsRpc(weaponIdx, origin, rec.dir, seed, aimed);
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        [Rpc(SendTo.Everyone)]
        void ShootEffectsRpc(int weaponIdx, Vector3 origin, Vector3 dir, uint seed, bool aimed)
        {
            // Remote players fly a visual-only copy of the same BBs (the owner already has its own).
            if (!IsOwner && weapons != null && weaponIdx >= 0 && weaponIdx < weapons.Length)
            {
                SpawnBBs(weapons[weaponIdx], origin, dir, seed, aimed, null);
                AirsoftAudio.Shot(weapons[weaponIdx], origin, false);
            }

            // Visible muzzle flash on every client (disabled by the timer in Update)
            muzzleFlashTimer = 0.05f;
            if (muzzleFlashObj)
            {
                muzzleFlashObj.transform.localScale = new Vector3(
                    Random.Range(0.10f, 0.18f), Random.Range(0.10f, 0.18f), Random.Range(0.22f, 0.34f));
                muzzleFlashObj.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                muzzleFlashObj.SetActive(true);
            }
            if (muzzleLight)
            {
                muzzleLight.intensity = 3.5f;
                muzzleLight.enabled = true;
            }
        }

        // The client only says which object it hit, with which shot/weapon, from where and at what
        // point. Damage, weapon name and whether the hit counts at all are decided here, against
        // the shot announced by ShootRpc.
        [Rpc(SendTo.Server)]
        void HitRpc(ulong networkObjId, int shotSeq, int weaponIdx, Vector3 origin, Vector3 hitPoint,
                    RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;   // no shooting "as" someone else

            // Shot: announced earlier (RPCs from one client arrive in order), passed the fire-time
            // checks (alive, weapon, fire rate, origin) and still remembered. A BB that left the gun
            // while its shooter was alive still counts if it lands after the shooter died.
            ref var rec = ref svShots[shotSeq & (SHOT_HISTORY - 1)];
            if (shotSeq <= 0 || rec.seq != shotSeq) { RejectHit("unknown or stale shot " + shotSeq); return; }
            if (!rec.valid) { RejectHit("shot " + shotSeq + " was rejected when fired"); return; }
            if (weaponIdx != rec.weapon) { RejectHit("weapon " + weaponIdx + " is not the one shot " + shotSeq + " was fired with"); return; }
            if ((origin - rec.origin).sqrMagnitude > 0.01f) { RejectHit("origin differs from the fired shot"); return; }
            var w = weapons[rec.weapon];
            float flight = Time.unscaledTime - rec.time;
            if (flight > BBBallistics.MAX_FLIGHT_TIME + LATE_TOLERANCE) { RejectHit("hit arrived after the BB was spent"); return; }

            // Target: must exist, be damageable, alive and not the shooter.
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjId, out var netObj)) return;
            if (netObj == NetworkObject) return;
            var health = netObj.GetComponent<Health>();
            if (health == null || health.isDead.Value || health.IsOut) return;

            // Geometry: in range, near the target, on a BB path with no wall before the hit.
            if (Vector3.Distance(rec.origin, hitPoint) > w.range + RANGE_TOLERANCE) { RejectHit("out of range"); return; }
            if (!NearTarget(netObj, hitPoint)) { RejectHit("hit point not near target"); return; }

            // Re-fly each pellet of the shot that hasn't scored yet; one of them must pass through the
            // hit point before any static geometry (this is the line-of-sight check, along the arc).
            int pellets = Mathf.Clamp(w.pelletsPerShot, 1, 31);
            int match = -1;
            float tof = 0f, bestMiss = float.MaxValue;
            bool blocked = false;
            for (int p = 0; p < pellets; p++)
            {
                if ((rec.usedPellets & (1 << p)) != 0) continue;
                Vector3 pd = BBBallistics.PelletDirection(w, rec.dir, rec.seed, p, rec.aimed);
                var r = BBBallistics.TracePath(w, rec.origin, pd, hitPoint, hitMask, PATH_TOLERANCE, LOS_END_MARGIN,
                                               out float t, out float miss);
                if (r == BBBallistics.PathResult.Hit && miss < bestMiss) { match = p; tof = t; bestMiss = miss; }
                else if (r == BBBallistics.PathResult.Blocked) blocked = true;
            }
            if (match < 0)
            {
                if (rec.usedPellets == (1 << pellets) - 1) RejectHit("more hits than pellets in shot " + shotSeq);
                else RejectHit(blocked ? "no line of sight along the BB path" : "hit point not on any BB path");
                return;
            }

            // Time of flight: the BB needs `tof` seconds to get there; a hit can't be reported sooner.
            if (flight < tof - TOF_TOLERANCE)
            { RejectHit($"hit after {flight:0.00}s, BB needs {tof:0.00}s"); return; }
            rec.usedPellets |= 1 << match;

            // Team modes: no friendly fire between teammates.
            if (MatchNet.Instance != null && MatchNet.Instance.BlocksDamage(OwnerClientId, health.MatchId))
                return;
            health.TakeDamage(w.damage, OwnerClientId, w.weaponName);
            HitConfirmRpc(hitPoint, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        void RejectShot(int shotSeq, string reason)
        {
            if (Debug.isDebugBuild)
                Debug.Log($"[Airsoft] Shot {shotSeq} from client {OwnerClientId} rejected: {reason}");
        }

        // The reported eye position must be close to where the server sees this player
        // (it lags behind the owner), and must not be on the other side of a wall from it.
        bool OriginPlausible(Vector3 origin)
        {
            Vector3 feet = transform.position;
            float h = Mathf.Clamp(Vector3.Dot(origin - feet, Vector3.up), -0.5f, 2.2f);
            Vector3 serverEye = feet + Vector3.up * h;
            if (Vector3.Distance(origin, serverEye) > ORIGIN_TOLERANCE) return false;
            return !WorldBlocks(serverEye, origin);
        }

        // Hit point within TARGET_TOLERANCE of the target's colliders (covers interpolation lag).
        static bool NearTarget(NetworkObject target, Vector3 point)
        {
            bool any = false;
            Bounds b = default;
            foreach (var c in target.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) b = new Bounds(target.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
            b.Expand(TARGET_TOLERANCE * 2f);
            return b.Contains(point);
        }

        // True if static world geometry sits between a and b. Anything networked (players,
        // enemies) is ignored: it moves, and the client's raycast already stopped at it.
        bool WorldBlocks(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist <= LOS_END_MARGIN * 2f) return false;
            int n = Physics.RaycastNonAlloc(a, d / dist, s_losHits, dist, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = s_losHits[i];
                if (hit.distance < LOS_END_MARGIN || hit.distance > dist - LOS_END_MARGIN) continue;
                if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
                return true;
            }
            return false;
        }

        void RejectHit(string reason)
        {
            if (Debug.isDebugBuild)
                Debug.Log($"[Airsoft] Hit from client {OwnerClientId} rejected: {reason}");
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void HitConfirmRpc(Vector3 point, RpcParams rpcParams = default)
        {
            AirsoftAudio.Play2D("hitmarker", 0.7f, AirsoftAudio.Bus.Sfx, 0.02f, 0.03f);
            OnHitMarker?.Invoke(point);
        }

        void NotifyAmmo()
        {
            if (weapons != null && currentIndex < weapons.Length)
                OnAmmoChanged?.Invoke(weapons[currentIndex].weaponName, ammoInMag, ammoReserve, currentIndex + 1);
        }

        // Lets the HUD pull the current ammo/weapon the moment it wires up
        public void RefreshHUD() => NotifyAmmo();

        // ───── Shared runtime materials ─────
        static Material GlowMat()
        {
            if (s_glowMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");   // always in builds
                if (sh == null) sh = Shader.Find("Unlit/Color");
                var c = new Color(1f, 0.82f, 0.4f);
                s_glowMat = new Material(sh);
                s_glowMat.color = c;
                if (s_glowMat.HasProperty("_BaseColor")) s_glowMat.SetColor("_BaseColor", c);
            }
            return s_glowMat;
        }
    }
}
