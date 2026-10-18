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

            netWeaponIndex.OnValueChanged += OnWeaponChanged;
            SwitchToWeapon(netWeaponIndex.Value);
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
        void SwitchWeaponRpc(int idx)
        {
            if (idx >= 0 && idx < weapons.Length)
            {
                magAmmos[currentIndex] = ammoInMag;
                reserveAmmos[currentIndex] = ammoReserve;
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
                if (playerCam) playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, 60f, Time.deltaTime * 10f);
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
                HitRpc(hitNetObj != null ? hitNetObj.NetworkObjectId : 0UL, w.damage, hit.point, w.weaponName);
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

        [Rpc(SendTo.Server)]
        void HitRpc(ulong networkObjId, int damage, Vector3 hitPoint, FixedString32Bytes weapon)
        {
            if (networkObjId == 0) return;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjId, out var netObj))
            {
                var health = netObj.GetComponent<Health>();
                if (health != null)
                {
                    health.TakeDamage(damage, OwnerClientId, weapon.ToString());
                    HitConfirmRpc(hitPoint, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
                }
            }
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
