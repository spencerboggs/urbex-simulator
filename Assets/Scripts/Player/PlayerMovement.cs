using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;
    public float sprintMultiplier = 2.5f;
    public float jumpHeight = 1.5f;
    public float gravity = -10f;

    [Header("Sprint System")]
    public float maxSprintCharge = 10f;
    public float sprintDrainRate = 1.5f;
    public float sprintRegenRate = 0.5f;
    public float minSprintRequired = 3f;

    private float sprintCharge;

    private bool exhausted = false;

    [Header("Feel")]
    public float groundedStickForce = -2f;
    public float airControlMultiplier = 0.6f;
    public float fallMultiplier = 1.05f;

    private CharacterController controller;
    private Vector3 velocity;

    [Header("Crouch")]
    public float crouchHeightMultiplier = 0.5f;

    private float originalHeight;
    private float crouchHeight;
    private bool isCrouching;


    void Start()
    {
        controller = GetComponent<CharacterController>();
        sprintCharge = maxSprintCharge;
        originalHeight = controller.height;
        crouchHeight = originalHeight * crouchHeightMultiplier;
    }

    void Update()
    {
        bool isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
            velocity.y = groundedStickForce;

        // --------------------
        // INPUT
        // --------------------
        Vector2 moveInput = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;

        Vector3 noMovement = new Vector3(0f, 0f, 0f);

        Vector3 moveDir =
            (transform.right * moveInput.x +
             transform.forward * moveInput.y).normalized;

        // --------------------
        // SPRINT STATE MACHINE (FIXED)
        // --------------------
        bool wantsToSprint = Keyboard.current.leftShiftKey.isPressed;

        // Exhaustion logic
        if (sprintCharge <= 0f)
            exhausted = true;

        if (sprintCharge >= minSprintRequired)
            exhausted = false;

        bool canStartSprint = !exhausted;

        bool isSprinting = wantsToSprint && canStartSprint;

        float currentSpeed = moveSpeed * (isSprinting ? sprintMultiplier : 1f);

        // --------------------
        // CHARGE DRAIN / REGEN
        // --------------------
        if (isSprinting && !noMovement.Equals(moveDir))
        {
            sprintCharge -= sprintDrainRate * Time.deltaTime;
            // Debug log for sprint charge and states
            Debug.Log(
                $"[SPRINT] Charge={sprintCharge:F2} | Exhausted={exhausted} | Sprinting={isSprinting}"
            );
        }
        else
        {
            sprintCharge += sprintRegenRate * Time.deltaTime;
        }

        sprintCharge = Mathf.Clamp(sprintCharge, 0f, maxSprintCharge);

        // --------------------
        // JUMP
        // --------------------
        if (Keyboard.current.spaceKey.isPressed && isGrounded)
        {
            velocity.y = jumpHeight * Mathf.Sqrt(-gravity);
            Debug.Log("Jump triggered");
        }

        // --------------------
        // CROUCH (FIXED ALIGNMENT)
        // --------------------
        bool crouchInput = Keyboard.current.leftCtrlKey.isPressed;

        if (isGrounded && crouchInput)
        {
            isCrouching = true;
            Debug.Log("Crouch initiated");
        }
        
        if (!crouchInput && isCrouching)
        {
            Debug.Log("Attempting to stand up from crouch");
            Debug.Log($"Can stand up: {CanStandUp()}");
            if (CanStandUp())
                isCrouching = false;
        }

        float targetHeight = isCrouching ? crouchHeight : originalHeight;

        // compute height delta BEFORE applying
        float previousHeight = controller.height;

        controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * 15f);
        if (isCrouching) {
            // When crouching, we want to lower the center to keep the bottom of the capsule fixed
            controller.center = new Vector3(0f, controller.height / 2f - 0.4f, 0f);
            // Scale the player down visually (optional, can be removed if not desired)
            transform.localScale = new Vector3(1f, crouchHeightMultiplier, 1f);
        }
        else
        {
            // When standing up, reset the scale and adjust the center to keep the bottom fixed
            transform.localScale = Vector3.one;
            // Adjust center to keep bottom fixed when standing up
            controller.center = new Vector3(0f, 0f, 0f);
        }

        // IMPORTANT: keep bottom of capsule fixed to ground
        float heightDiff = controller.height - previousHeight;
        controller.center += new Vector3(0f, heightDiff / 2f, 0f);

        // --------------------
        // GRAVITY
        // --------------------
        float gravityScale = (velocity.y < 0) ? fallMultiplier : 1f;
        velocity.y += gravity * gravityScale * Time.deltaTime;

        // --------------------
        // MOVE
        // --------------------
        float control = isGrounded ? 1f : airControlMultiplier;

        Vector3 horizontalMove = moveDir * currentSpeed * control;

        Vector3 finalMove =
            horizontalMove +
            new Vector3(0, velocity.y, 0);

        controller.Move(finalMove * Time.deltaTime);
    }

    private bool CanStandUp()
    {
        // Just check if there's enough space above the player's head to stand up
        float headClearance = originalHeight - crouchHeight;
        Vector3 rayOrigin = transform.position + Vector3.up * crouchHeight;
        return !Physics.SphereCast(rayOrigin, controller.radius, Vector3.up, out _, headClearance);
    }

    public bool getIsCrouching()
    {
        return isCrouching;
    }

    public void ResetVerticalVelocity()
    {
        velocity.y = 0f;
    }
}
