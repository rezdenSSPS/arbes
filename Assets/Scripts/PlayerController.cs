using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person character movement: WASD / arrows / left stick to move,
/// Shift (or L3) to sprint, Space (or A / Cross) to jump.
/// Movement is relative to the camera view.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float rotationSpeed = 12f;

    [Header("Jumping")]
    public float jumpHeight = 1.4f;
    public float gravity = -20f;

    [Header("References")]
    [Tooltip("Camera used to make movement relative to the view. Defaults to Camera.main.")]
    public Transform cameraTransform;

    [Tooltip("If the player falls below this height they respawn at the start.")]
    public float respawnHeight = -20f;

    CharacterController controller;
    Vector3 verticalVelocity;
    Vector3 spawnPosition;
    Quaternion spawnRotation;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    void Update()
    {
        if (Time.timeScale == 0f) return; // paused

        Vector2 input = ReadMoveInput();
        var keyboard = Keyboard.current;
        var gamepad = Gamepad.current;

        bool sprint = (keyboard != null && keyboard.leftShiftKey.isPressed) ||
                      (gamepad != null && gamepad.leftStickButton.isPressed);
        bool jump = (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) ||
                    (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);

        // Direction relative to the camera, flattened onto the ground.
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = cameraTransform.forward; forward.y = 0f; forward.Normalize();
            right = cameraTransform.right;     right.y = 0f;   right.Normalize();
        }

        Vector3 move = forward * input.y + right * input.x;
        if (move.sqrMagnitude > 1f) move.Normalize();

        // Turn to face the direction we are moving.
        if (move.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }

        // Gravity + jumping.
        bool grounded = controller.isGrounded;
        if (grounded && verticalVelocity.y < 0f) verticalVelocity.y = -2f;
        if (grounded && jump) verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        verticalVelocity.y += gravity * Time.deltaTime;

        float speed = sprint ? sprintSpeed : walkSpeed;
        controller.Move((move * speed + verticalVelocity) * Time.deltaTime);

        if (transform.position.y < respawnHeight) Respawn();
    }

    Vector2 ReadMoveInput()
    {
        Vector2 value = Vector2.zero;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) value.x -= 1f;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            if (stick.sqrMagnitude > value.sqrMagnitude) value = stick;
        }
        return Vector2.ClampMagnitude(value, 1f);
    }

    public void Respawn()
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        verticalVelocity = Vector3.zero;
        controller.enabled = true;
    }
}
