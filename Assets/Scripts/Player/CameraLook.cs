using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour
{
    [Header("References")]
    // The transform that holds the camera, which will be rotated for vertical look (pitch)
    public Transform cameraHolder;

    [Header("Settings")]
    // Mouse sensitivity for looking around
    public float mouseSensitivity = 100f;

    // Current vertical rotation (pitch) of the camera
    float xRotation = 0f;

    void Start()
    {
        // Lock the cursor to the center of the screen and hide it
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // Read mouse movement input
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        // Vertical look (pitch)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Horizontal look (yaw)
        transform.Rotate(Vector3.up * mouseX);
    }
}