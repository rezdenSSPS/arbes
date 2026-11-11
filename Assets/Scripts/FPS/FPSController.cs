using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace AirsoftFPS
{
    [RequireComponent(typeof(CharacterController))]
    public class FPSController : NetworkBehaviour
    {
        [Header("Movement")]
        public float walkSpeed = 5.5f;
        public float sprintSpeed = 9f;
        public float crouchSpeed = 2.5f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;

        [Header("Look")]
        public float mouseSensitivity = 2f;
        public float maxLookUp = 85f;
        public float maxLookDown = -85f;

        [Header("References")]
        public Transform cameraHolder;
        public Camera playerCamera;

        [Header("Stamina")]
        public float sprintSeconds = 6f;         // sprint time from full stamina
        public float staminaRegenSeconds = 8f;   // empty -> full while not sprinting
        public float minStaminaToSprint = 0.15f; // after exhaustion, this much must regen before sprinting again

        [Header("Lean")]
        public float leanOffset = 0.5f;          // metres the camera shifts sideways
        public float leanRoll = 11f;             // camera roll, degrees
        public float leanSpeed = 9f;

        [Header("Feel")]
        public float groundAccel = 50f;          // m/s² towards the wanted speed on the ground
        public float groundDecel = 60f;          // m/s² when slowing down / stopping
        public float airAccel = 4f;              // m/s²: a little steering in the air, never extra speed
        public float sprintFovKick = 8f;         // degrees added to the FOV at full sprint

        CharacterController cc;
        Vector3 velocity;
        float xRotation;
        bool isCrouching;
        bool wasDowned;   // Medic: downed players kneel in place
        float standHeight = 2f;
        float crouchHeight = 1.2f;
        float speedMultiplier = 1f; // set by weapon weight
        float stamina = 1f;
        bool staminaExhausted;
        float lean;       // -1..1, smoothed
        Vector3 horiz;    // horizontal velocity (momentum: a jump keeps the take-off speed, doesn't add to it)
        float takeoffSpeed;
        bool airborne, sprintAtTakeoff;
        float airTime, landSlow = 1f;
        float bobPhase, bobWeight, bobSprint, landDip, fallSpeed;
        float fovKick;
        Health health;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching => isCrouching;
        public float Stamina => stamina;          // 0..1, for the HUD
        public float LeanAmount => lean;          // -1..1
        public float FovKick => fovKick;          // degrees on top of the Settings FOV (sprinting)
        public float GroundSpeed { get; private set; }   // m/s actually moved on the ground (0 in the air)
        public bool Grounded { get; private set; }

        public override void OnNetworkSpawn()
        {
            cc = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            // NGO instantiates a remotely spawned player at the prefab origin and moves the transform only
            // afterwards. With Physics.autoSyncTransforms off the CharacterController would still sit at
            // (0,0,0) and the first Move() would snap the player there: re-create it at the spawn pose.
            if (IsOwner && cc != null && cc.enabled) { cc.enabled = false; cc.enabled = true; }
            // the small "stick to the ground" move (velocity.y = -2) falls under minMoveDistance at very high
            // frame rates and the player stops being grounded (no jumping): don't skip tiny moves
            if (cc != null) cc.minMoveDistance = 0f;

            // Customisable low-poly character (visual only)
            if (GetComponent<PlayerAvatar>() == null) gameObject.AddComponent<PlayerAvatar>();

            if (IsOwner)
            {
                if (GetComponent<GoggleFog>() == null) gameObject.AddComponent<GoggleFog>();   // local lens fog (F wipes)
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (playerCamera)
                {
                    AirsoftAudio.BeforeListenerOn();   // the camera carries the local AudioListener
                    playerCamera.gameObject.SetActive(true);
                    // post-processing (bloom / tonemapping from the map's global Volume)
                    var urp = playerCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (urp == null) urp = playerCamera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    urp.renderPostProcessing = GameSettings.PostProcessing;
                    urp.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;
                    playerCamera.farClipPlane = 400f;
                }
            }
            else
            {
                if (playerCamera) playerCamera.gameObject.SetActive(false);
                // disable audio listener on non-owner
                var listener = GetComponentInChildren<AudioListener>();
                if (listener) listener.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            // The local camera's AudioListener goes away with the avatar (back to the lobby, or the session
            // ended): hand over to the audio fallback listener right now.
            if (!IsOwner) return;
            var listener = GetComponentInChildren<AudioListener>(true);
            if (listener) listener.enabled = false;
            AirsoftAudio.AfterListenerOff();
        }

        void Update()
        {
            // a dead body doesn't block anyone (round modes keep players down for a while)
            bool dead = health != null && health.IsSpawned && health.isDead.Value;
            if (cc != null && cc.detectCollisions == dead) cc.detectCollisions = !dead;

            if (!IsOwner) return;
            if (cc == null) return;
            if (playerCamera && !playerCamera.gameObject.activeSelf)
            {
                AirsoftAudio.BeforeListenerOn();
                playerCamera.gameObject.SetActive(true);
            }
            if (Time.timeScale == 0) return;

            // Pause / results menus own the mouse; the match (and gravity) keeps running.
            if (!HUDManager.InputBlocked) HandleLook();
            HandleMovement();
            HandleLean();

            if (transform.position.y < -30f)
            {
                // Fell through the map: put the player back on a real spawn point for their team instead of
                // the old fixed (0, 2, 0) (which could be off the map, in the open, or on top of enemies).
                // GetSpawnFor is static and reads only the scene + MatchNet, so it's safe to call on the owner.
                var pose = PlayerSpawner.GetSpawnFor(OwnerClientId);
                cc.enabled = false;
                transform.SetPositionAndRotation(pose.position, pose.rotation);
                velocity = Vector3.zero;
                horiz = Vector3.zero;
                cc.enabled = true;
            }
        }

        void HandleLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var wc = GetComponent<WeaponController>();
            float sens = mouseSensitivity * GameSettings.Sensitivity
                         * (wc != null && wc.IsAiming ? GameSettings.ScopedSensitivity : 1f);
            float mx = mouse.delta.x.ReadValue() * sens * 0.1f;
            float my = mouse.delta.y.ReadValue() * sens * 0.1f;
            if (GameSettings.InvertY) my = -my;

            xRotation -= my;
            xRotation = Mathf.Clamp(xRotation, maxLookDown, maxLookUp);

            if (cameraHolder)
                cameraHolder.localRotation = Quaternion.Euler(xRotation, 0, 0);
            transform.Rotate(Vector3.up * mx);
        }

        void HandleMovement()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float dt = Time.deltaTime;

            bool grounded = cc.isGrounded;
            if (grounded && velocity.y < 0)
                velocity.y = -2f;

            bool downed = ObjectiveMode.IsDowned(OwnerClientId);   // Medic: down in place until revived / bled out
            bool input = !HUDManager.InputBlocked && !downed
                         && !MatchNet.IntroFrozen                                         // match-start flyover / 3-2-1
                         && !(health != null && health.IsSpawned && health.isDead.Value);   // dead: no walking around
            float h = 0f;
            float v = 0f;
            if (input && kb.dKey.isPressed) h += 1f;
            if (input && kb.aKey.isPressed) h -= 1f;
            if (input && kb.wKey.isPressed) v += 1f;
            if (input && kb.sKey.isPressed) v -= 1f;

            // Landing: a jump costs a moment of speed (no bunny-hopping faster than running) and dips the camera
            if (grounded && airborne)
            {
                airborne = false;
                if (airTime > 0.25f) landSlow = 0.6f;
                landDip = -Mathf.Min(0.14f, 0.012f * fallSpeed);
            }
            if (!grounded && !airborne)
            {
                airborne = true;
                airTime = 0f;
                takeoffSpeed = horiz.magnitude;
                sprintAtTakeoff = IsSprinting;
            }
            if (airborne) { airTime += dt; fallSpeed = Mathf.Max(0f, -velocity.y); }
            landSlow = Mathf.MoveTowards(landSlow, 1f, dt * 1.3f);

            // Sprint (gated by stamina: drains while sprinting, regens otherwise; after it empties you
            // can't sprint again until it has recovered a little). It can't start in the air.
            bool wantSprint = input && kb.leftShiftKey.isPressed && v > 0.1f && !isCrouching
                              && !ObjectiveMode.CannotSprint(OwnerClientId);   // e.g. carrying the flag (CTF)
            if (staminaExhausted && stamina < minStaminaToSprint) wantSprint = false;
            if (airborne && !sprintAtTakeoff) wantSprint = false;
            IsSprinting = wantSprint && stamina > 0f;
            if (IsSprinting && !airborne)
            {
                stamina -= dt / Mathf.Max(0.1f, sprintSeconds);
                if (stamina <= 0f) { stamina = 0f; staminaExhausted = true; }
            }
            else if (!IsSprinting)
            {
                stamina = Mathf.Min(1f, stamina + dt / Mathf.Max(0.1f, staminaRegenSeconds));
                if (stamina >= minStaminaToSprint) staminaExhausted = false;
            }

            // Crouch: toggle on press, or hold (Settings > Controls)
            bool crouchKey = kb.cKey.isPressed || kb.leftCtrlKey.isPressed;
            bool wantCrouch = isCrouching;
            if (GameSettings.ToggleCrouch)
            {
                if (input && (kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame)) wantCrouch = !isCrouching;
            }
            else wantCrouch = input && crouchKey;
            if (downed) wantCrouch = true;              // kneeling: camera low
            else if (wasDowned) wantCrouch = false;     // stand up again when revived
            wasDowned = downed;
            if (wantCrouch != isCrouching)
            {
                isCrouching = wantCrouch;
                cc.height = isCrouching ? crouchHeight : standHeight;
                cc.center = new Vector3(0, cc.height / 2f, 0);
                if (cameraHolder)
                    cameraHolder.localPosition = new Vector3(0, cc.height - 0.15f, 0);
            }

            float speed = isCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            speed *= speedMultiplier * ObjectiveMode.SpeedFactor(OwnerClientId) * landSlow;   // e.g. frozen before a round, slower VIP
            if (MatchNet.IntroFrozen) speed = 0f;

            Vector3 wish = transform.right * h + transform.forward * v;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= speed;

            if (!airborne)
            {
                // quick but not instant: a few frames to get going, a short slide to a stop
                float acc = wish.sqrMagnitude > horiz.sqrMagnitude ? groundAccel : groundDecel;
                horiz = Vector3.MoveTowards(horiz, wish, acc * dt);
            }
            else
            {
                // in the air: keep the take-off momentum, steer a little, never speed up past the take-off speed
                horiz = Vector3.MoveTowards(horiz, wish, airAccel * dt);
                float cap = Mathf.Max(takeoffSpeed, 1.5f);
                if (horiz.magnitude > cap) horiz = horiz.normalized * cap;
            }

            // Jump
            if (input && kb.spaceKey.wasPressedThisFrame && grounded && !isCrouching)
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            velocity.y += gravity * dt;
            Vector3 before = transform.position;
            cc.Move((horiz + Vector3.up * velocity.y) * dt);

            // walls and obstacles eat momentum: keep only what we actually moved (sliding along a wall stays)
            if (dt > 0f)
            {
                Vector3 moved = transform.position - before; moved.y = 0f;
                Vector3 real = moved / dt;
                if (real.sqrMagnitude < horiz.sqrMagnitude) horiz = real;
            }

            Grounded = cc.isGrounded;
            GroundSpeed = Grounded ? horiz.magnitude : 0f;

            // Sprint feel: the view widens a little while actually running fast
            float kickTarget = IsSprinting && Grounded
                ? sprintFovKick * Mathf.Clamp01(Mathf.InverseLerp(walkSpeed, sprintSpeed * 0.9f, horiz.magnitude / Mathf.Max(0.3f, speedMultiplier)))
                : 0f;
            if (!GameSettings.ViewBob) kickTarget *= 0.5f;
            fovKick = Mathf.Lerp(fovKick, kickTarget, 1f - Mathf.Exp(-(kickTarget > fovKick ? 6f : 4f) * dt));
        }

        // Peek left (Q) / right (E): shift the camera sideways with a little roll, shortened if a wall is
        // in the way. Owner-only and camera-only (the body collider doesn't move). E doubles as the objective
        // interact key; the overlap is harmless (interacting happens standing still at an objective).
        void HandleLean()
        {
            if (cameraHolder == null) return;
            var kb = Keyboard.current;
            float target = 0f;
            bool canLean = kb != null && !HUDManager.InputBlocked
                           && !(health != null && health.IsSpawned && health.isDead.Value)
                           && !ObjectiveMode.IsDowned(OwnerClientId);
            if (canLean)
            {
                if (kb.qKey.isPressed) target -= 1f;
                if (kb.eKey.isPressed) target += 1f;
            }
            if (target != 0f)
            {
                Vector3 head = transform.position + Vector3.up * (cc.height - 0.15f);
                Vector3 dir = transform.right * Mathf.Sign(target);
                if (Physics.Raycast(head, dir, out RaycastHit hit, leanOffset + 0.3f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<Health>() == null)   // walls block the lean, not people
                    target *= Mathf.Clamp01((hit.distance - 0.3f) / leanOffset);
            }
            float dt = Time.deltaTime;
            lean = Mathf.MoveTowards(lean, target, leanSpeed * dt);

            // Head bob: one dip per step, a sway from foot to foot every two steps, a touch of roll.
            // Driven by the speed actually moved on the ground, so it eases out when you stop or hit a wall.
            float sp = GroundSpeed;
            float sprintT = Mathf.Clamp01(Mathf.InverseLerp(walkSpeed * 0.9f, sprintSpeed * 0.9f, sp));
            bobSprint = Mathf.Lerp(bobSprint, sprintT, 1f - Mathf.Exp(-6f * dt));
            float wantBob = GameSettings.ViewBob ? Mathf.Clamp01(sp / (walkSpeed * 0.7f)) : 0f;
            bobWeight = Mathf.Lerp(bobWeight, wantBob, 1f - Mathf.Exp(-8f * dt));
            float stepsPerSec = isCrouching ? 1.5f : Mathf.Lerp(1.85f, 2.75f, bobSprint);
            if (bobWeight > 0.01f) bobPhase += dt * Mathf.PI * stepsPerSec;
            else bobPhase = 0f;
            float crouchMul = isCrouching ? 0.55f : 1f;
            float w = bobWeight * crouchMul;
            float by = Mathf.Lerp(0.032f, 0.06f, bobSprint) * w * (Mathf.Cos(2f * bobPhase) - 1f) * 0.5f;   // 0 .. -A
            float bx = Mathf.Lerp(0.018f, 0.032f, bobSprint) * w * Mathf.Sin(bobPhase);
            float roll = Mathf.Lerp(0.45f, 1.0f, bobSprint) * w * Mathf.Sin(bobPhase);
            float pitch = Mathf.Lerp(0.25f, 0.6f, bobSprint) * w * (1f - Mathf.Cos(2f * bobPhase)) * 0.5f;
            landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-9f * dt));

            cameraHolder.localPosition = new Vector3(lean * leanOffset + bx, cc.height - 0.15f + by + landDip, 0f);
            cameraHolder.localRotation = Quaternion.Euler(xRotation + pitch, 0f, -lean * leanRoll + roll);
        }

        public void SetSpeedMultiplier(float mult) => speedMultiplier = mult;

        public void ApplyRecoil(float up, float side)
        {
            xRotation -= up;
            transform.Rotate(Vector3.up * Random.Range(-side, side));
        }
    }
}
