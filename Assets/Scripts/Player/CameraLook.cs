using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class CameraLook : MonoBehaviour
{
    [Header("References")]
    // The transform that holds the camera, which will be rotated for vertical look (pitch)
    public Transform cameraHolder;

    [Header("Settings")]
    // Mouse sensitivity for looking around
    public float mouseSensitivity = 0.3f;

    [Header("Eye Position")]
    [Tooltip("Distance below the top of the CharacterController capsule where the camera sits. A small inset keeps the eyes just inside the capsule rather than poking out the top.")]
    public float eyeOffsetFromTop = 0.15f;

    // Current vertical rotation (pitch) of the camera
    float xRotation = 0f;
    ClimbingController _climbing;

    void Start()
    {
        TryGetComponent(out _climbing);
        ApplyEyeHeight();
    }

    void Update()
    {
        if (Mouse.current == null || cameraHolder == null)
            return;

        // Read mouse movement input
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        // Vertical look (pitch) - full range even while wall-clinging
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Horizontal look (yaw)
        transform.Rotate(Vector3.up * mouseX);

        if (_climbing != null && _climbing.IsWallClinging)
            ApplyWallClingYawClamp();
    }

    // While clinging, limit yaw to ±wallClingYawLimit around the direction facing the
    // wall so look direction stays aligned with wall-relative movement (no full 360)
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

    // Position cameraHolder near the top of the standing CharacterController capsule
    // (the "head") so:
    //   - Pitch rotates around the eyes instead of the capsule center, because
    //     cameraHolder.localRotation pivots at cameraHolder's own local origin
    //   - Yaw (applied to the player root) still sweeps the camera around the
    //     player's vertical centerline - but now at eye height, which is what you
    //     want for a first-person feel
    //
    // Run once at Start, not every frame: PlayerMovement scales transform.localScale.y
    // for crouch, which already lowers the camera proportionally. Recomputing here
    // each frame from controller.height would compound on top of that and pull the
    // camera too far down
    private void ApplyEyeHeight()
    {
        if (cameraHolder == null)
            return;

        CharacterController controller = GetComponent<CharacterController>();
        if (controller == null)
            return;

        // CharacterController.center and .height are in this transform's local
        // space. The capsule top sits at center.y + height/2; back off by
        // eyeOffsetFromTop so the eyes are just below the crown.
        float capsuleTopLocalY = controller.center.y + controller.height * 0.5f;
        Vector3 localPos = cameraHolder.localPosition;
        localPos.y = capsuleTopLocalY - eyeOffsetFromTop;
        cameraHolder.localPosition = localPos;
    }
}
