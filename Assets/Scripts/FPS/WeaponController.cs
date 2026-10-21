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
        Vector3 lastTracerStart, lastTracerEnd;  // for replicating the shot to others
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
        // The owner raycasts locally for responsiveness and only reports *what* it hit;
        // the server decides damage (from its own WeaponData) and rejects impossible hits.
        const float FIRE_RATE_TOLERANCE = 0.1f;   // allow 10% faster than fireRate (clock/jitter)
        const float SHOT_BURST_WINDOW = 0.2f;     // seconds of shots that may arrive bunched up by jitter
        const float SWITCH_GRACE = 0.75f;         // old weapon still accepted this long after a switch (RTT)
        const float ORIGIN_TOLERANCE = 3.5f;      // reported eye position vs server's (lagged) view of the shooter
        const float TARGET_TOLERANCE = 3f;        // reported hit point vs server's view of the target's colliders
        const float RANGE_TOLERANCE = 0.5f;
        const float LOS_END_MARGIN = 0.3f;        // ignore geometry this close to either end of a LOS ray

        int localShotSeq;                 // owner: one id per trigger pull (all pellets share it)
        int svLastShot;                   // server: last shot id seen from this shooter
        int svPelletsLeft;                // server: hits still allowed for svLastShot
        float svShotTokens = -1f;         // server: fire-rate token bucket (-1 = full on first use)
        float svTokenTime;
        int svPrevWeapon = -1;            // server: weapon before the last switch, for SWITCH_GRACE
        float svSwitchTime = -100f;
        Health ownHealth;

        static readonly RaycastHit[] s_losHits = new RaycastHit[16];

        static Material s_tracerMat;
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
        }

        void SwitchToWeapon(int idx)
        {
            currentIndex = idx;
            ammoInMag = magAmmos[idx];
            ammoReserve = reserveAmmos[idx];
            isReloading = false;
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
            if (HUDManager.InputBlocked)
            {
                // a menu is open: no shooting/switching, drop out of the scope
                isAiming = false;
                if (playerCam) playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, GameSettings.FieldOfView, Time.deltaTime * 10f);
                return;
            }

            HandleWeaponSwitch();
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

            var kb = Keyboard.current;
            if (kb == null) return;

            for (int i = 0; i < digitKeys.Length && i < weapons.Length; i++)
            {
                int weaponIdx = i < 9 ? i : 9;
                if (kb[digitKeys[i]].wasPressedThisFrame)
                {
                    if (weaponIdx != currentIndex)
                        SwitchWeaponRpc(weaponIdx);
                    return;
                }
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.y.ReadValue();
            if (scroll > 1f)
            {
                int next = (currentIndex + 1) % weapons.Length;
                SwitchWeaponRpc(next);
            }
            else if (scroll < -1f)
            {
                int prev = (currentIndex - 1 + weapons.Length) % weapons.Length;
                SwitchWeaponRpc(prev);
            }
        }

        [Rpc(SendTo.Server)]
        void SwitchWeaponRpc(int idx, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            // Gun Game: the weapon is dictated by the server's kill count, not the client.
            if (MatchNet.Instance != null && MatchNet.Instance.Mode == MatchNet.GameModeType.GunGame &&
                idx != Mathf.Clamp(MatchNet.Instance.GunLevelOf(OwnerClientId), 0, weapons.Length - 1))
                return;
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

            if (fireInput && Time.time >= nextFireTime && ammoInMag > 0)
            {
                nextFireTime = Time.time + 1f / w.fireRate;
                ammoInMag--;
                magAmmos[currentIndex] = ammoInMag;
                NotifyAmmo();

                localShotSeq++;
                for (int p = 0; p < w.pelletsPerShot; p++)
                    FireRaycast(w);

                // Camera recoil (player look kicks up)
                if (fpsController)
                    fpsController.ApplyRecoil(w.recoilUp * 0.3f, w.recoilSide * 0.3f);

                // Weapon model kick (owner-local animation)
                ApplyWeaponKick(w);

                // Networked muzzle flash + tracer so everyone sees the shot
                ShootRpc(lastTracerStart, lastTracerEnd);

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

        void FireRaycast(WeaponData w)
        {
            if (!playerCam) return;

            Vector3 spread = Vector3.zero;
            float spreadAngle = isAiming ? w.spread * 0.3f : w.spread;
            spread.x = Random.Range(-spreadAngle, spreadAngle);
            spread.y = Random.Range(-spreadAngle, spreadAngle);

            Vector3 dir = playerCam.transform.forward;
            dir = Quaternion.Euler(spread) * dir;

            Ray ray = new Ray(playerCam.transform.position, dir);
            Vector3 tracerEnd;

            if (Physics.Raycast(ray, out RaycastHit hit, w.range, hitMask))
            {
                tracerEnd = hit.point;
                var hitNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (hitNetObj != null)
                    HitRpc(hitNetObj.NetworkObjectId, localShotSeq, currentIndex, ray.origin, hit.point);
                SpawnImpact(hit.point, hit.normal);
            }
            else
            {
                tracerEnd = ray.origin + dir * w.range;
            }

            // Tracer starts from the muzzle for a believable look
            Vector3 tracerStart = muzzlePoint ? muzzlePoint.position : ray.origin;
            SpawnTracer(tracerStart, tracerEnd);
            lastTracerStart = tracerStart;
            lastTracerEnd = tracerEnd;
        }

        // ───── Visual effects (owner-local) ─────
        void SpawnTracer(Vector3 start, Vector3 end)
        {
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = TracerMat();
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            lr.startWidth = 0.035f;
            lr.endWidth = 0.012f;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            lr.startColor = lr.endColor = new Color(1f, 0.92f, 0.55f, 1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            StartCoroutine(FadeTracer(go, lr, 0.055f));
        }

        IEnumerator FadeTracer(GameObject go, LineRenderer lr, float dur)
        {
            float t = 0f;
            Color baseCol = lr.startColor;
            while (t < dur && go != null)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(1f - t / dur);
                var c = new Color(baseCol.r, baseCol.g, baseCol.b, a);
                lr.startColor = c;
                lr.endColor = c;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        void SpawnImpact(Vector3 point, Vector3 normal)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);
            go.name = "Impact";
            go.transform.position = point + normal * 0.02f;
            go.transform.localScale = Vector3.one * 0.14f;
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = GlowMat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("ImpactLight");
            lightGo.transform.SetParent(go.transform);
            lightGo.transform.localPosition = Vector3.zero;
            var l = lightGo.AddComponent<Light>();
            l.color = new Color(1f, 0.7f, 0.3f);
            l.range = 2.5f;
            l.intensity = 3f;

            StartCoroutine(FadeImpact(go, 0.22f));
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
                if (light) light.intensity = 3f * k;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        [Rpc(SendTo.Server)]
        void ShootRpc(Vector3 tracerStart, Vector3 tracerEnd)
        {
            ShootEffectsRpc(tracerStart, tracerEnd);
        }

        [Rpc(SendTo.Everyone)]
        void ShootEffectsRpc(Vector3 tracerStart, Vector3 tracerEnd)
        {
            // Remote players see the tracer (the owner already drew its own locally).
            if (!IsOwner) SpawnTracer(tracerStart, tracerEnd);

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
        // point. Damage, weapon name and whether the hit counts at all are decided here.
        [Rpc(SendTo.Server)]
        void HitRpc(ulong networkObjId, int shotSeq, int weaponIdx, Vector3 origin, Vector3 hitPoint,
                    RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;   // no shooting "as" someone else
            if (ownHealth != null && ownHealth.isDead.Value) { RejectHit("shooter is dead"); return; }

            // Weapon: must be the one the server has equipped (or the previous one right after a switch).
            bool current = weaponIdx == netWeaponIndex.Value;
            bool recentPrev = weaponIdx == svPrevWeapon && Time.unscaledTime - svSwitchTime < SWITCH_GRACE;
            if (weaponIdx < 0 || weaponIdx >= weapons.Length || !(current || recentPrev))
            { RejectHit("weapon " + weaponIdx + " not equipped"); return; }
            var w = weapons[weaponIdx];

            // Fire rate: each new shot id costs a token; pellets of the same shot share it.
            if (shotSeq < svLastShot) { RejectHit("stale shot"); return; }
            if (shotSeq > svLastShot)
            {
                float now = Time.unscaledTime;
                float cap = 1f + SHOT_BURST_WINDOW * w.fireRate;
                svShotTokens = svShotTokens < 0f ? cap
                    : Mathf.Min(cap, svShotTokens + (now - svTokenTime) * w.fireRate * (1f + FIRE_RATE_TOLERANCE));
                svTokenTime = now;
                svLastShot = shotSeq;
                if (svShotTokens < 1f) { svPelletsLeft = 0; RejectHit("firing faster than " + w.fireRate + "/s"); return; }
                svShotTokens -= 1f;
                svPelletsLeft = Mathf.Max(1, w.pelletsPerShot);
            }
            if (svPelletsLeft <= 0) { RejectHit("more hits than pellets in shot " + shotSeq); return; }
            svPelletsLeft--;

            // Target: must exist, be damageable, alive and not the shooter.
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjId, out var netObj)) return;
            if (netObj == NetworkObject) return;
            var health = netObj.GetComponent<Health>();
            if (health == null || health.isDead.Value) return;

            // Geometry: plausible origin, in range, near the target, and no wall in between.
            if (!OriginPlausible(origin)) { RejectHit("origin too far from shooter"); return; }
            if (Vector3.Distance(origin, hitPoint) > w.range + RANGE_TOLERANCE) { RejectHit("out of range"); return; }
            if (!NearTarget(netObj, hitPoint)) { RejectHit("hit point not near target"); return; }
            if (WorldBlocks(origin, hitPoint)) { RejectHit("no line of sight"); return; }

            // Team Deathmatch: no friendly fire between teammates.
            if (MatchNet.Instance != null && MatchNet.Instance.BlocksDamage(OwnerClientId, netObj.OwnerClientId))
                return;
            health.TakeDamage(w.damage, OwnerClientId, w.weaponName);
            HitConfirmRpc(hitPoint, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
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
        static Material TracerMat()
        {
            if (s_tracerMat == null)
                s_tracerMat = new Material(Shader.Find("Sprites/Default"));
            return s_tracerMat;
        }

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
