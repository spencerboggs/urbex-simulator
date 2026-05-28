using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person look: pitch on the camera holder, yaw on the player root.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CameraLook : MonoBehaviour
{
    [Header("References")]
    /// <summary>Transform rotated for vertical look (pitch).</summary>
    public Transform cameraHolder;

    [Header("Settings")]
    /// <summary>Mouse sensitivity for look input.</summary>
    public float mouseSensitivity = 0.3f;

    [Header("Eye Position")]
    [Tooltip("Distance below the top of the CharacterController capsule where the camera sits. A small inset keeps the eyes just inside the capsule rather than poking out the top.")]
    public float eyeOffsetFromTop = 0.15f;

    /// <summary>Accumulated pitch in degrees, clamped for vertical look limits.</summary>
    float xRotation = 0f;
    /// <summary>Optional climb controller used to clamp yaw while wall clinging.</summary>
    ClimbingController _climbing;

    /// <summary>Caches climb reference and sets initial eye height on the camera holder.</summary>
    void Start()
    {
        TryGetComponent(out _climbing);
        ApplyEyeHeight();
    }

    /// <summary>Applies mouse look as pitch on the holder and yaw on the player root.</summary>
    void Update()
    {
        if (Mouse.current == null || cameraHolder == null)
            return;

        // Read mouse delta and split into horizontal and vertical look.
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        // Pitch on camera holder; yaw on player root.
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);

        // Wall cling limits how far the body can turn away from the surface.
        if (_climbing != null && _climbing.IsWallClinging)
            ApplyWallClingYawClamp();
    }

    /// <summary>Clamps body yaw to a wall-relative arc while clinging.</summary>
    // While clinging, limit yaw to wall-relative movement instead of full 360.
    private void ApplyWallClingYawClamp()
    {
        float center = _climbing.WallClingYawCenter;
        float limit = _climbing.WallClingYawLimit;
        float delta = Mathf.DeltaAngle(center, transform.eulerAngles.y);
        delta = Mathf.Clamp(delta, -limit, limit);

        Vector3 euler = transform.eulerAngles;
        euler.y = center + delta;
        transform.eulerAngles = euler;
    }

    /// <summary>Positions the camera holder at eye height once at start.</summary>
    // Position cameraHolder at eye height once at start. Crouch scales the root via PlayerMovement;
    // recomputing each frame from controller.height would compound and pull the camera too low.
    private void ApplyEyeHeight()
    {
        if (cameraHolder == null)
            return;

        CharacterController controller = GetComponent<CharacterController>();
        if (controller == null)
            return;

        float capsuleTopLocalY = controller.center.y + controller.height * 0.5f;
        Vector3 localPos = cameraHolder.localPosition;
        localPos.y = capsuleTopLocalY - eyeOffsetFromTop;
        cameraHolder.localPosition = localPos;
    }
}
