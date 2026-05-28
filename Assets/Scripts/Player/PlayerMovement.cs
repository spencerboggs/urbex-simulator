using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

/// <summary>
/// CharacterController movement, sprint stamina, jump, and optional crouch.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    /// <summary>Base movement speed in units per second.</summary>
    public float moveSpeed = 2.5f;
    /// <summary>Sprint speed multiplier when not using backpack-specific values.</summary>
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
    /// <summary>Jump apex height in world units.</summary>
    public float jumpHeight = 1.8f;
    /// <summary>Gravity acceleration applied each frame.</summary>
    public float gravity = -20f;

    [Header("Sprint System")]
    /// <summary>Maximum sprint charge.</summary>
    public float maxSprintCharge = 10f;
    /// <summary>Sprint charge drained per second while sprinting and moving.</summary>
    public float sprintDrainRate = 1.5f;
    /// <summary>Sprint charge restored per second while grounded and not sprinting.</summary>
    public float sprintRegenRate = 1f;
    /// <summary>Minimum charge required before sprint can start again after exhaustion.</summary>
    public float minSprintRequired = 3f;
    /// <summary>Jump height multiplier when exhausted (low stamina).</summary>
    public float noStaminaJumpMultiplier = 0.75f;
    /// <summary>Fraction of max sprint charge removed on each jump.</summary>
    public float jumpStaminaCostPercent = 0.1f;

    /// <summary>Current sprint stamina charge (0 to maxSprintCharge).</summary>
    private float sprintCharge;
    /// <summary>True when charge hit zero and sprint cannot start until minSprintRequired.</summary>
    private bool exhausted = false;

    [Header("Feel")]
    /// <summary>Small downward velocity while grounded to stay on slopes.</summary>
    public float groundedStickForce = -2f;
    /// <summary>Horizontal input scale while airborne (0 = none, 1 = full).</summary>
    public float airControlMultiplier = 0.6f;
    /// <summary>Extra gravity scale while falling.</summary>
    public float fallMultiplier = 1.05f;

    /// <summary>Cached CharacterController on this GameObject.</summary>
    private CharacterController controller;
    /// <summary>World velocity applied each frame (horizontal from input, vertical from gravity/jump).</summary>
    private Vector3 velocity;

    [Header("Crouch")]
    /// <summary>Standing height scale when crouched.</summary>
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

    /// <summary>Standing capsule height captured at Start.</summary>
    private float originalHeight;
    /// <summary>Crouched capsule height derived from originalHeight.</summary>
    private float crouchHeight;
    /// <summary>Whether the player is currently in a crouched pose.</summary>
    private bool isCrouching;

    /// <summary>Caches controller, sprint charge, and crouch height baselines.</summary>
    void Start()
    {
        controller = GetComponent<CharacterController>();
        sprintCharge = maxSprintCharge;
        originalHeight = controller.height;
        crouchHeight = originalHeight * crouchHeightMultiplier;
    }

    /// <summary>Handles movement input, sprint stamina, jump, crouch, gravity, and CharacterController motion.</summary>
    void Update()
    {
        bool isGrounded = controller.isGrounded;

        // Stick to ground on slopes when grounded and not rising.
        if (isGrounded && velocity.y < 0)
            velocity.y = groundedStickForce;

        // WASD input in local space.
        Vector2 moveInput = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;

        Vector3 noMovement = new Vector3(0f, 0f, 0f);

        Vector3 moveDir =
            (transform.right * moveInput.x +
             transform.forward * moveInput.y).normalized;

        // Sprint stamina: exhaust at zero, allow restart after minSprintRequired.
        bool wantsToSprint = Keyboard.current.leftShiftKey.isPressed;

        if (sprintCharge <= 0f)
            exhausted = true;

        if (sprintCharge >= minSprintRequired)
            exhausted = false;

        bool canStartSprint = !exhausted;
        bool isSprinting = wantsToSprint && canStartSprint && !isCrouching;
        float sprintMult = _hasBackpack ? _sprintMultiplierWithBackpack : _sprintMultiplierNoBackpack;
        float currentSpeed = moveSpeed * (isSprinting ? sprintMult : 1f);

        // Drain sprint while moving; regen on ground when not sprinting.
        if (isSprinting && !noMovement.Equals(moveDir))
        {
            sprintCharge -= sprintDrainRate * Time.deltaTime;
        }
        else if (isGrounded)
        {
            // Regenerate only on the ground so falling or climbing does not refill stamina mid-air.
            sprintCharge += sprintRegenRate * Time.deltaTime;
        }

        sprintCharge = Mathf.Clamp(sprintCharge, 0f, maxSprintCharge);

        // Jump: weaker apex when exhausted; otherwise costs a fraction of max charge.
        if (Keyboard.current.spaceKey.isPressed && isGrounded)
        {
            if (exhausted)
            {
                velocity.y = jumpHeight * noStaminaJumpMultiplier * Mathf.Sqrt(-gravity);
            }
            else
            {
                velocity.y = jumpHeight * Mathf.Sqrt(-gravity);
                sprintCharge = Mathf.Max(0f, sprintCharge - maxSprintCharge * jumpStaminaCostPercent);
            }
            
        }

        // Cancel upward velocity when hitting a ceiling.
        if (controller.collisionFlags.HasFlag(CollisionFlags.Above) && velocity.y > 0)
            velocity.y = 0f;

        // Crouch: hold to crouch, release when headroom allows (disabled via _crouchEnabled).
        bool crouchInput = _crouchEnabled && Keyboard.current.leftCtrlKey.isPressed;

        if (!_crouchEnabled)
            isCrouching = false;

        if (isGrounded && crouchInput)
            isCrouching = true;
        else if (!crouchInput && isCrouching && HasStandingClearance())
            isCrouching = false;

        // Lost headroom during stand lerp: stay crouched until clear.
        if (!crouchInput && !isCrouching &&
            controller.height > crouchHeight + 0.02f &&
            controller.height < originalHeight - 0.02f &&
            !HasStandingClearance())
            isCrouching = true;

        float targetHeight = (_crouchEnabled && isCrouching) ? crouchHeight : originalHeight;

        // Lerp capsule height and adjust center/scale for crouch pose.
        float previousHeight = controller.height;

        controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * 15f);
        if (_crouchEnabled && isCrouching)
        {
            controller.center = new Vector3(0f, controller.height / 2f - 0.4f, 0f);
            transform.localScale = new Vector3(1f, crouchHeightMultiplier, 1f);
        }
        else
        {
            transform.localScale = Vector3.one;
            controller.center = new Vector3(0f, 0f, 0f);
        }

        float heightDiff = controller.height - previousHeight;
        controller.center += new Vector3(0f, heightDiff / 2f, 0f);

        // Gravity with extra pull while falling; horizontal move with air control scaling.
        float gravityScale = (velocity.y < 0) ? fallMultiplier : 1f;
        velocity.y += gravity * gravityScale * Time.deltaTime;

        float control = isGrounded ? 1f : airControlMultiplier;
        Vector3 horizontalMove = moveDir * currentSpeed * control;

        velocity.x = horizontalMove.x;
        velocity.z = horizontalMove.z;

        // Combine horizontal and vertical velocity for CharacterController.Move.
        Vector3 finalMove =
            horizontalMove +
            new Vector3(0, velocity.y, 0);

        controller.Move(finalMove * Time.deltaTime);
    }

    /// <summary>
    /// Returns whether a full-height capsule at the current feet position would not overlap blocking geometry.
    /// </summary>
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

        // Reject overlaps that are self, child hierarchy, or floor under feet.
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider c = overlaps[i];
            if (c == null || c == controller)
                continue;
            if (c.transform.IsChildOf(transform) || transform.IsChildOf(c.transform))
                continue;
            // Ignore floor colliders the capsule rests on.
            if (c.bounds.max.y < ignoreBelowY)
                continue;
            return false;
        }

        return true;
    }

    /// <summary>Builds world-space standing capsule sphere centers and radius for overlap checks.</summary>
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

    /// <summary>Computes the world-space bottom of the current CharacterController capsule.</summary>
    private void GetCapsuleBottomWorld(out Vector3 capsuleBottomWorld)
    {
        Vector3 worldCenter = transform.TransformPoint(controller.center);
        float halfExtents = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        capsuleBottomWorld = worldCenter - transform.up * halfExtents;
    }

    /// <summary>Whether the player is currently crouching.</summary>
    public bool getIsCrouching() => isCrouching;

    /// <summary>Current sprint charge.</summary>
    public float GetSprintCharge() => sprintCharge;

    /// <summary>Maximum sprint charge.</summary>
    public float GetMaxSprintCharge() => maxSprintCharge;

    /// <summary>Forward speed along the player's facing axis (horizontal only).</summary>
    public float GetForwardVelocity()
    {
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        return Vector3.Dot(horizontalVel, transform.forward);
    }

    /// <summary>Current vertical velocity.</summary>
    public float GetVerticalVelocity() => velocity.y;

    /// <summary>Resets vertical velocity (used when finishing a climb).</summary>
    public void ResetVerticalVelocity() => velocity.y = 0f;

    /// <summary>Updates backpack state for sprint multiplier selection.</summary>
    public void SetHasBackpack(bool hasBackpack) => _hasBackpack = hasBackpack;

    /// <summary>Removes sprint charge and marks exhausted at zero.</summary>
    public void DrainStamina(float amount)
    {
        if (amount <= 0f)
            return;

        sprintCharge = Mathf.Max(0f, sprintCharge - amount);

        if (sprintCharge <= 0f)
            exhausted = true;
    }
}
