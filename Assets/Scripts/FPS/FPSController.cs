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
        float standHeight = 2f;
        float crouchHeight = 1.2f;
        float speedMultiplier = 1f; // set by weapon weight

        public bool IsSprinting { get; private set; }

        public override void OnNetworkSpawn()
        {
            cc = GetComponent<CharacterController>();

            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (playerCamera) playerCamera.gameObject.SetActive(true);
            }
            else
            {
                if (playerCamera) playerCamera.gameObject.SetActive(false);
                // disable audio listener on non-owner
                var listener = GetComponentInChildren<AudioListener>();
                if (listener) listener.enabled = false;
            }
        }

        void Update()
        {
            if (!IsOwner) return;
            if (cc == null) return;
            if (playerCamera && !playerCamera.gameObject.activeSelf)
                playerCamera.gameObject.SetActive(true);
            if (Time.timeScale == 0) return;

            HandleLook();
            HandleMovement();

            if (transform.position.y < -30f)
            {
                // respawn
                cc.enabled = false;
                transform.position = new Vector3(0, 2, 0);
                velocity = Vector3.zero;
                cc.enabled = true;
            }
        }

        void HandleLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            float mx = mouse.delta.x.ReadValue() * mouseSensitivity * 0.1f;
            float my = mouse.delta.y.ReadValue() * mouseSensitivity * 0.1f;

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

            float h = 0f;
            float v = 0f;
            if (kb.dKey.isPressed) h += 1f;
            if (kb.aKey.isPressed) h -= 1f;
            if (kb.wKey.isPressed) v += 1f;
            if (kb.sKey.isPressed) v -= 1f;

            // Sprint
            IsSprinting = kb.leftShiftKey.isPressed && v > 0.1f && !isCrouching;

            // Crouch
            if (kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame)
            {
                isCrouching = !isCrouching;
                cc.height = isCrouching ? crouchHeight : standHeight;
                cc.center = new Vector3(0, cc.height / 2f, 0);
                if (cameraHolder)
                    cameraHolder.localPosition = new Vector3(0, cc.height - 0.15f, 0);
            }

            float speed = isCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            speed *= speedMultiplier;

            Vector3 move = transform.right * h + transform.forward * v;
            if (move.sqrMagnitude > 1f) move.Normalize();
            cc.Move(move * speed * Time.deltaTime);

            // Jump
            if (kb.spaceKey.wasPressedThisFrame && grounded && !isCrouching)
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
