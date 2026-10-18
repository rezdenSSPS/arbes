using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orbiting third-person camera. Mouse or right stick rotates around the target.
/// Pulls in closer when a wall is between the camera and the player.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 6f;
    public float focusHeight = 1.5f;
    public float mouseSensitivity = 0.15f;
    public float stickSensitivity = 150f;
    public float minPitch = -10f;
    public float maxPitch = 65f;
    public bool lockCursor = true;

    float yaw;
    float pitch = 20f;

    void Start()
    {
        if (target != null) yaw = target.eulerAngles.y;
        if (lockCursor) SetCursorLocked(true);
    }

    public static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (Time.timeScale > 0f)
        {
            var mouse = Mouse.current;
            // Clicking the game view re-captures the mouse (e.g. after alt-tab).
            if (lockCursor && mouse != null && Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);

            Vector2 look = Vector2.zero;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                look += mouse.delta.ReadValue() * mouseSensitivity;
            if (Gamepad.current != null)
                look += Gamepad.current.rightStick.ReadValue() * stickSensitivity * Time.unscaledDeltaTime;

            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = target.position + Vector3.up * focusHeight;
        Vector3 direction = rotation * Vector3.back;
        float actualDistance = distance;

        if (Physics.SphereCast(focus, 0.25f, direction, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(target))
        {
            actualDistance = Mathf.Max(0.5f, hit.distance);
        }

        transform.SetPositionAndRotation(focus + direction * actualDistance, rotation);
    }
}
