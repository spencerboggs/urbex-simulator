using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    // Base movement speed in units per second
    public float moveSpeed = 2.5f;
    // Sprint speed multiplier
    public float sprintMultiplier = 2.5f;

    [Header("Backpack (affects sprint)")]
    [SerializeField]
    private bool _hasBackpack;

    [Tooltip("Sprint multiplier when not wearing a backpack.")]
    [SerializeField]
    private float _sprintMultiplierNoBackpack = 2.7f;

    [Tooltip("Sprint multiplier when wearing a backpack.")]
    [SerializeField]
    private float _sprintMultiplierWithBackpack = 2.3f;
    // Height the player can jump
    public float jumpHeight = 1.8f;
    // Gravity force applied to the player
    public float gravity = -20f;

    [Header("Sprint System")]
    // Maximum sprint charge available
    public float maxSprintCharge = 10f;
    // Rate at which sprint charge depletes when sprinting
    public float sprintDrainRate = 1.5f;
    // Rate at which sprint charge regenerates when not sprinting
    public float sprintRegenRate = 1f;
    // Minimum sprint charge required to start sprinting
    public float minSprintRequired = 3f;
    // No stamina jump multiplier (percentage of normal jump height when stamina is depleted)
    public float noStaminaJumpMultiplier = 0.75f;
    // Stamina decay jump multiplier (how much percent stamina is lost when jumping)
    public float jumpStaminaCostPercent = 0.1f;

    // Current sprint charge level
    private float sprintCharge;
    // Whether the player is currently exhausted (unable to sprint)
    private bool exhausted = false;

    [Header("Feel")]
    // Small downward force to keep the player grounded when walking down slopes
    public float groundedStickForce = -2f;
    // Multiplier for horizontal control while in the air (0 = no control, 1 = full control)
    public float airControlMultiplier = 0.6f;
    // Multiplier for gravity when the player is falling (makes jumps feel snappier)
    public float fallMultiplier = 1.05f;

    // Internal state
    private CharacterController controller;
    private Vector3 velocity;

    [Header("Crouch")]
    // Multiplier for crouch height (scale factor)
    public float crouchHeightMultiplier = 0.5f;

    [SerializeField]
    [Tooltip("Temporary kill switch until crouching is reworked to avoid scaling the whole player.")]
    private bool _crouchEnabled = false;

    [Tooltip("Layers counted as blocking when checking headroom to stand up.")]
    [SerializeField]
    private LayerMask _crouchObstructionMask = ~0;

    [Tooltip("Tiny extra gap so standing checks do not graze ceilings due to float error.")]
    [SerializeField]
    private float _standClearanceSkin = 0.02f;

    [Tooltip("Colliders with world-space top below (feet + this) are ignored so the ground does not block standing checks.")]
    [SerializeField]
    private float _standCheckIgnoreBelowFeet = 0.15f;

    // Original height of the character controller capsule
    private float originalHeight;
    // Current height of the character controller capsule when crouching
    private float crouchHeight;
    // Whether the player is currently crouching
    private bool isCrouching;

    /* HUD Elements */
    // public PlayerHUD hud;

    void Start()
    {
        // Get reference to the CharacterController component
        controller = GetComponent<CharacterController>();
        // Initialize sprint charge to max at the start
        sprintCharge = maxSprintCharge;
        // Store the original height of the character controller for crouching calculations
        originalHeight = controller.height;
        // Calculate the crouch height based on the original height and the crouch multiplier
        crouchHeight = originalHeight * crouchHeightMultiplier;
    }

    void Update()
    {
        // Check if the player is grounded
        bool isGrounded = controller.isGrounded;

        // If grounded and "falling", apply a small downward force to keep the player "stuck" to the ground
        if (isGrounded && velocity.y < 0)
            velocity.y = groundedStickForce;

        /* Input handling */
        Vector2 moveInput = Vector2.zero;

        // Read movement input from WASD keys
        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;

        // Set control vector to zero if no input to prevent unintended movement
        Vector3 noMovement = new Vector3(0f, 0f, 0f);

        // Normalize movement input to prevent faster diagonal movement
        Vector3 moveDir =
            (transform.right * moveInput.x +
             transform.forward * moveInput.y).normalized;

        /* Sprint handling */
        // Check if the player is trying to sprint (holding Left Shift)
        bool wantsToSprint = Keyboard.current.leftShiftKey.isPressed;

        // Exhaustion logic
        if (sprintCharge <= 0f)
            exhausted = true;

        if (sprintCharge >= minSprintRequired)
            exhausted = false;

        // Player can sprint if they want to sprint and are not exhausted
        bool canStartSprint = !exhausted;
        // Final sprinting state
        bool isSprinting = wantsToSprint && canStartSprint && !isCrouching;
        // Apply sprint multiplier to movement speed if sprinting
        float sprintMult = _hasBackpack ? _sprintMultiplierWithBackpack : _sprintMultiplierNoBackpack;
        float currentSpeed = moveSpeed * (isSprinting ? sprintMult : 1f);

        // Charge and drain logic
        if (isSprinting && !noMovement.Equals(moveDir))
        {
            // Drain sprint charge when sprinting and moving
            sprintCharge -= sprintDrainRate * Time.deltaTime;
        }
        else
        {
            // Regenerate sprint charge when not sprinting
            sprintCharge += sprintRegenRate * Time.deltaTime;
        }

        // Clamp sprint charge to valid range
        sprintCharge = Mathf.Clamp(sprintCharge, 0f, maxSprintCharge);

        /* Jump handling */
        // Check if the player is trying to jump (pressing Space) and is grounded
        if (Keyboard.current.spaceKey.isPressed && isGrounded)
        {
            if (exhausted)
            {
                // Jump 0.75 height if sprint charge is low to allow for some mobility even when exhausted
                velocity.y = jumpHeight * noStaminaJumpMultiplier * Mathf.Sqrt(-gravity);
            }
            else
            {
                // Calculate the initial jump velocity
                velocity.y = jumpHeight * Mathf.Sqrt(-gravity);
                // Subtract a small amount from the stamina to prevent infinite jumping
                sprintCharge = Mathf.Max(0f, sprintCharge - maxSprintCharge * jumpStaminaCostPercent);
            }
            
        }

        // If the player hits their head on something while jumping, reset vertical velocity
        if (controller.collisionFlags.HasFlag(CollisionFlags.Above) && velocity.y > 0)
            velocity.y = 0f;

        /* Crouch handling */
        // Check if the player is trying to crouch (holding Left Ctrl)
        bool crouchInput = _crouchEnabled && Keyboard.current.leftCtrlKey.isPressed;

        if (!_crouchEnabled)
            isCrouching = false;

        if (isGrounded && crouchInput)
            isCrouching = true;
        else if (!crouchInput && isCrouching && HasStandingClearance())
            isCrouching = false;

        // Mid-stand lerp lost headroom (moving ceiling or bad sample): snap intent back to crouched until clear
        if (!crouchInput && !isCrouching &&
            controller.height > crouchHeight + 0.02f &&
            controller.height < originalHeight - 0.02f &&
            !HasStandingClearance())
            isCrouching = true;

        // Apply crouch/stand changes to the character controller height and center
        float targetHeight = (_crouchEnabled && isCrouching) ? crouchHeight : originalHeight;

        // compute height delta BEFORE applying
        float previousHeight = controller.height;

        controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * 15f);
        if (_crouchEnabled && isCrouching)
        {
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

        // Keep bottom of capsule fixed to ground
        float heightDiff = controller.height - previousHeight;
        controller.center += new Vector3(0f, heightDiff / 2f, 0f);

        /* Gravity application */
        float gravityScale = (velocity.y < 0) ? fallMultiplier : 1f;
        velocity.y += gravity * gravityScale * Time.deltaTime;

        /* Movement application */
        // Apply air control multiplier if not grounded
        float control = isGrounded ? 1f : airControlMultiplier;
        // Calculate horizontal movement based on input, current speed, and control multiplier
        Vector3 horizontalMove = moveDir * currentSpeed * control;

        // Store horizontal velocity for external access
        velocity.x = horizontalMove.x;
        velocity.z = horizontalMove.z;

        // Combine horizontal movement with vertical velocity for final movement vector
        Vector3 finalMove =
            horizontalMove +
            new Vector3(0, velocity.y, 0);

        // Move the character controller based on the final movement vector
        controller.Move(finalMove * Time.deltaTime);

        /* HUD Updates */
        // hud.SetStamina(sprintCharge / maxSprintCharge);
    }

    // True if a full-height capsule at the current feet position would not overlap geometry (excluding this controller)
    private bool HasStandingClearance()
    {
        GetCapsuleBottomWorld(out Vector3 feetWorld);
        GetStandingCapsuleWorld(out Vector3 bottomSphereCenter, out Vector3 topSphereCenter, out float checkRadius);

        const QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
        Collider[] overlaps = Physics.OverlapCapsule(
            bottomSphereCenter,
            topSphereCenter,
            checkRadius,
            _crouchObstructionMask,
            triggers);

        if (overlaps == null || overlaps.Length == 0)
            return true;

        float ignoreBelowY = feetWorld.y + _standCheckIgnoreBelowFeet;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider c = overlaps[i];
            if (c == null || c == controller)
                continue;
            if (c.transform.IsChildOf(transform) || transform.IsChildOf(c.transform))
                continue;
            // Floor / ground the capsule rests on; do not treat as overhead obstruction
            if (c.bounds.max.y < ignoreBelowY)
                continue;
            return false;
        }

        return true;
    }

    // Inner line for Physics.CheckCapsule / OverlapCapsule: hemisphere centers for a vertical capsule of given height and radius, feet fixed at current capsule bottom
    private void GetStandingCapsuleWorld(out Vector3 bottomSphereCenter, out Vector3 topSphereCenter, out float radius)
    {
        radius = controller.radius - _standClearanceSkin;
        if (radius <= 0.01f)
            radius = controller.radius * 0.98f;

        float standHeight = Mathf.Max(originalHeight - _standClearanceSkin * 2f, radius * 2f + 0.01f);
        Vector3 up = transform.up;

        GetCapsuleBottomWorld(out Vector3 capsuleBottomWorld);

        bottomSphereCenter = capsuleBottomWorld + up * radius;
        topSphereCenter = capsuleBottomWorld + up * (standHeight - radius);
    }

    private void GetCapsuleBottomWorld(out Vector3 capsuleBottomWorld)
    {
        Vector3 worldCenter = transform.TransformPoint(controller.center);
        float halfExtents = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        capsuleBottomWorld = worldCenter - transform.up * halfExtents;
    }

    // Get method for other scripts to check if the player is currently crouching
    public bool getIsCrouching() => isCrouching;

    // Get method to retrieve the player's current sprint charge
    public float GetSprintCharge() => sprintCharge;

    // Get method to retrieve the player's maximum sprint charge
    public float GetMaxSprintCharge() => maxSprintCharge;

    // Get method to retrieve the player's current forward velocity
    public float GetForwardVelocity()
    {
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        return Vector3.Dot(horizontalVel, transform.forward);
    }

    // Get method to retrieve the player's current vertical velocity
    public float GetVerticalVelocity() => velocity.y;

    // Set method to reset vertical velocity (used by climbing controller when finishing a climb)
    public void ResetVerticalVelocity() => velocity.y = 0f;

    public void SetHasBackpack(bool hasBackpack) => _hasBackpack = hasBackpack;
}
