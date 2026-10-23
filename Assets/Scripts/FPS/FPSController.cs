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

        CharacterController cc;
        Vector3 velocity;
        float xRotation;
        bool isCrouching;
        bool wasDowned;   // Medic: downed players kneel in place
        float standHeight = 2f;
        float crouchHeight = 1.2f;
        float speedMultiplier = 1f; // set by weapon weight
        Health health;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching => isCrouching;

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

            if (transform.position.y < -30f)
            {
                // Fell through the map: put the player back on a real spawn point for their team instead of
                // the old fixed (0, 2, 0) (which could be off the map, in the open, or on top of enemies).
                // GetSpawnFor is static and reads only the scene + MatchNet, so it's safe to call on the owner.
                var pose = PlayerSpawner.GetSpawnFor(OwnerClientId);
                cc.enabled = false;
                transform.SetPositionAndRotation(pose.position, pose.rotation);
                velocity = Vector3.zero;
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

            bool grounded = cc.isGrounded;
            if (grounded && velocity.y < 0)
                velocity.y = -2f;

            bool downed = ObjectiveMode.IsDowned(OwnerClientId);   // Medic: down in place until revived / bled out
            bool input = !HUDManager.InputBlocked && !downed
                         && !(health != null && health.IsSpawned && health.isDead.Value);   // dead: no walking around
            float h = 0f;
            float v = 0f;
            if (input && kb.dKey.isPressed) h += 1f;
            if (input && kb.aKey.isPressed) h -= 1f;
            if (input && kb.wKey.isPressed) v += 1f;
            if (input && kb.sKey.isPressed) v -= 1f;

            // Sprint
            IsSprinting = input && kb.leftShiftKey.isPressed && v > 0.1f && !isCrouching
                          && !ObjectiveMode.CannotSprint(OwnerClientId);   // e.g. carrying the flag (CTF)

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
            speed *= speedMultiplier * ObjectiveMode.SpeedFactor(OwnerClientId);   // e.g. frozen before a round, slower VIP

            Vector3 move = transform.right * h + transform.forward * v;
            if (move.sqrMagnitude > 1f) move.Normalize();
            cc.Move(move * speed * Time.deltaTime);

            // Jump
            if (input && kb.spaceKey.wasPressedThisFrame && grounded && !isCrouching)
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            velocity.y += gravity * Time.deltaTime;
            cc.Move(velocity * Time.deltaTime);
        }

        public void SetSpeedMultiplier(float mult) => speedMultiplier = mult;

        public void ApplyRecoil(float up, float side)
        {
            xRotation -= up;
            transform.Rotate(Vector3.up * Random.Range(-side, side));
        }
    }
}
