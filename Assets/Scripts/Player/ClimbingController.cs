using System.Collections.Specialized;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(CharacterController))]
public class ClimbingController : MonoBehaviour
{
    [Header("Detection")]
    // Distance to check for walls in front of the player
    public float wallCheckDistance = 0.6f;
    // Heights at which to check for walls
    public float upCheckHeight = 1.3f;
    public float lowCheckHeight = 0.5f;
    // Small offset to prevent starting raycasts inside walls
    public float skinWidth = 0.1f;

    [Header("Climb Offsets")]
    // The amount of extra height the player needs to clear the wall
    public float heightOffset = 1.2f;
    // Additional clearance height to ensure the player doesn't get stuck on the edge
    public float clearanceHeight = 1.2f;

    [Header("Climb")]
    // Time it takes to complete the climb
    public float climbSpeed = 4f;

    [Header("Attached Wall Climb (Climbable tag)")]
    [Tooltip("Tag applied to surfaces that should be climbed by attaching to the wall (chain link fences, walls with footholds, etc).")]
    public string climbableTag = "Climbable";

    [Tooltip("How far in front of the player to look for a climbable surface to attach to.")]
    public float wallClingDetectDistance = 0.65f;

    [Tooltip("Sphere radius used when probing for a climbable surface.")]
    public float wallClingCheckRadius = 0.2f;

    [Tooltip("Distance the player's pivot is held away from the wall surface while clinging.")]
    public float wallClingDistance = 0.45f;

    [Tooltip("Vertical movement speed while clinging (W / S).")]
    public float wallClimbVerticalSpeed = 2.0f;

    [Tooltip("Horizontal movement speed while clinging (A / D).")]
    public float wallClimbHorizontalSpeed = 2.5f;

    [Tooltip("Short delay after detaching before the player can re-grip the same wall.")]
    public float wallClingReattachCooldown = 0.25f;

    [Tooltip("Outward push (toward wall normal) applied when jumping off the wall.")]
    public float wallClingJumpOffPush = 0.35f;

    [Tooltip("Maximum tilt from vertical (degrees) a surface can have while still counting as a climbable wall.")]
    public float maxWallTiltFromVertical = 30f;

    [Tooltip("Grace period (in seconds) the player stays attached after the wall ends above them. Lets them keep climbing past the top edge so their feet clear it and they can walk over. Physics still applies — ceilings and other geometry will block them normally.")]
    public float wallClingTopOutDuration = 0.8f;

    // Internal state
    private CharacterController controller;
    private PlayerMovement movement;
    private InputAction moveAction;

    // Mantle state variables
    private bool isClimbing = false;
    private Vector3 climbTarget;
    private Vector3 climbStartPosition;
    private Vector3 climbMidPoint;

    // Wall cling state variables
    private bool isWallClinging = false;
    private Vector3 wallNormal = Vector3.forward;
    private Collider currentClingCollider;
    private float clingCooldownTimer = 0f;
    // Seconds spent off the wall since last successful probe. Always ticks while clinging
    // without a surface in front, so the player can never get stuck in a phantom-cling
    // state in mid-air. Resets to 0 whenever the probe re-acquires a climbable surface
    private float topOutTimer = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();

        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            moveAction = playerInput.actions["Move"];
        }
    }

    void Update()
    {
        if (isClimbing)
        {
            PerformMantle();
            return;
        }

        if (isWallClinging)
        {
            PerformWallCling();
            return;
        }

        if (clingCooldownTimer > 0f)
            clingCooldownTimer -= Time.deltaTime;

        // Priority 1: if there is a Climbable-tagged surface in front, the new
        // attached wall-climb system handles it. Tagged surfaces are intentionally
        // reserved for this system and must never fall through to the mantle.
        // Priority 2: otherwise, defer to the existing mantle climb (untouched).
        if (TryHandleClimbableWall())
        {
            return;
        }
        else if (!controller.isGrounded && !movement.getIsCrouching() && movement.GetForwardVelocity() > 0.1f)
        {
            DetectLedgeAndAutoClimb();
        }
    }

    void DetectLedgeAndAutoClimb()
    {
        // Get the forward direction of the player
        Vector3 forward = transform.forward;

        // Start rays from slightly in front of the character to avoid starting inside walls
        float startOffset = skinWidth;

        // Detection points
        Vector3 lowOrigin = transform.position + Vector3.up * lowCheckHeight + forward * startOffset;
        Vector3 midOrigin = transform.position + Vector3.up * 1.0f + forward * startOffset;
        Vector3 upperOrigin = transform.position + Vector3.up * upCheckHeight + forward * startOffset;

        /* Check if wall is in front */
        float sphereRadius = 0.2f;
        RaycastHit lowHit, midHit;
        bool wallInFrontLow = Physics.SphereCast(lowOrigin, sphereRadius, forward, out lowHit, wallCheckDistance);
        bool wallInFrontMid = Physics.SphereCast(midOrigin, sphereRadius, forward, out midHit, wallCheckDistance);

        // Use the closest wall hit
        RaycastHit wallHit = lowHit;
        bool wallInFront = wallInFrontLow;
        if (wallInFrontMid && (!wallInFrontLow || midHit.distance < lowHit.distance))
        {
            wallHit = midHit;
            wallInFront = true;
        }
        else if (wallInFrontLow)
        {
            wallHit = lowHit;
            wallInFront = true;
        }

        if (!wallInFront) return;

        // Check wall height relative to player
        float wallHeightFromGround = wallHit.point.y - transform.position.y;

        // Don't climb walls that are too low (player can just step up)
        if (wallHeightFromGround < 0.4f)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Wall too low to climb: {wallHeightFromGround:F2}m");
            return;
        }

        // Don't climb walls that are too high (above player's head)
        if (wallHeightFromGround > upCheckHeight + 0.3f)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Wall too high to climb: {wallHeightFromGround:F2}m (max: {upCheckHeight + 0.3f}m)");
            return;
        }

        /* Check for wall above - this is the key part for stepped walls */
        // Raycast forward from above the wall to see if there's a wall further ahead
        Vector3 forwardRayOrigin = wallHit.point + Vector3.up * 1.0f;
        RaycastHit forwardWallHit;
        bool hasForwardWall = Physics.Raycast(forwardRayOrigin, forward, out forwardWallHit, 1.2f);

        // Also check directly above the wall
        Vector3 aboveOrigin = wallHit.point + Vector3.up * 0.5f;
        RaycastHit aboveHit;
        bool hasWallAbove = Physics.Raycast(aboveOrigin, forward, out aboveHit, wallCheckDistance);

        // Determine if we can climb (either no wall above, or there's a stepped wall further ahead)
        bool canClimb = false;
        float stepDepth = 0f;

        if (!hasWallAbove)
        {
            // No wall above at all - easy climb
            canClimb = true;
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log("No wall above - standard climb");
        }
        else if (hasForwardWall && forwardWallHit.distance > wallHit.distance + 0.2f)
        {
            // There's a wall above, but also a wall further ahead (stepped)
            stepDepth = forwardWallHit.distance - wallHit.distance;

            // Check if the stepped wall is too high
            float steppedWallHeight = forwardWallHit.point.y - transform.position.y;
            if (steppedWallHeight > upCheckHeight + 0.3f)
            {
                /* DEBUG OUTPUT (REMOVE LATER) */
                Debug.Log($"Stepped wall too high: {steppedWallHeight:F2}m");
                return;
            }

            canClimb = true;
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Stepped wall detected! Step depth: {stepDepth:F2}m");
        }

        if (!canClimb)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log("Wall continues above - cannot climb");
            return;
        }

        /* Find the top surface */
        // Search from either the original wall or the forward wall
        Vector3 searchOrigin = hasForwardWall && stepDepth > 0.2f ? forwardWallHit.point : wallHit.point;
        searchOrigin += forward * 0.1f; // Small forward offset

        RaycastHit topHit = new RaycastHit();
        bool foundSurface = false;
        float surfaceHeight = 0f;

        // Cast upward to find where the wall ends, then raycast down
        for (float yOffset = 0.5f; yOffset <= 2.0f; yOffset += 0.3f)
        {
            Vector3 rayStart = searchOrigin + Vector3.up * yOffset;

            if (Physics.Raycast(rayStart, Vector3.down, out topHit, 2.0f))
            {
                // Check if this surface is above the original wall
                if (topHit.point.y > wallHit.point.y + 0.2f)
                {
                    surfaceHeight = topHit.point.y;
                    foundSurface = true;
                    /* DEBUG OUTPUT (REMOVE LATER) */
                    Debug.Log($"Found surface at height: {surfaceHeight:F2}m");
                    break;
                }
            }
        }

        if (!foundSurface)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log("No top surface found");
            return;
        }

        // Calculate total height the player needs to climb
        float totalClimbHeight = surfaceHeight - transform.position.y;

        // Check if the climb height is reasonable
        if (totalClimbHeight > 1.5f)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Climb height too high: {totalClimbHeight:F2}m (max: 1.5m)");
            return;
        }

        if (totalClimbHeight < 0.4f)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Climb height too low: {totalClimbHeight:F2}m - should just step up");
            return;
        }

        // Check if the surface is too steep
        float slopeAngle = Vector3.Angle(topHit.normal, Vector3.up);
        if (slopeAngle > 50f)
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Surface too steep: {slopeAngle:F2}�");
            return;
        }

        // Calculate target position
        if (totalClimbHeight < 0.8f)
        {
            // For lower walls (waist/chest height), just do a quick vault
            climbTarget = topHit.point + Vector3.up * 0.1f;
            climbTarget += forward * 0.5f; // Move further forward for vault
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Quick vault - height: {totalClimbHeight:F2}m");
        }
        else
        {
            // For higher walls, do a full climb
            climbTarget = topHit.point + Vector3.up * 0.2f;

            // Add forward offset based on situation
            if (hasForwardWall && stepDepth > 0.2f)
            {
                climbTarget += forward * 0.3f;
            }
            else
            {
                climbTarget += forward * 0.4f;
            }
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log($"Full climb - height: {totalClimbHeight:F2}m");
        }

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.Log($"CLIMB TRIGGERED - Wall height: {wallHeightFromGround:F2}m, Surface height: {surfaceHeight:F2}m, Total climb: {totalClimbHeight:F2}m");
        StartClimb();
    }

    void StartClimb()
    {
        isClimbing = true;
        climbStartPosition = transform.position;

        // Simplify the climb for lower walls
        float totalClimbHeight = climbTarget.y - climbStartPosition.y;

        if (totalClimbHeight < 0.8f)
        {
            // For quick vaults, just go straight to the target
            climbMidPoint = climbTarget;
        }
        else
        {
            // For full climbs, create an arc
            Vector3 forward = transform.forward;
            Vector3 wallTop = climbTarget - forward;
            climbMidPoint = new Vector3(climbStartPosition.x, wallTop.y + clearanceHeight, climbStartPosition.z);
        }

        if (movement != null)
            movement.enabled = false;

        controller.enabled = false;

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.Log($"MANTLE STARTED - Type: {(totalClimbHeight < 0.8f ? "Quick vault" : "Full climb")}");
    }

    void PerformMantle()
    {
        float totalClimbHeight = climbTarget.y - climbStartPosition.y;

        if (totalClimbHeight < 0.8f)
        {
            // Simple linear climb for quick vaults
            transform.position = Vector3.MoveTowards(transform.position, climbTarget, climbSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, climbTarget) < 0.05f)
            {
                FinishClimb();
            }
        }
        else
        {
            // Arced climb for higher walls
            Vector3 upwardTarget = new Vector3(climbStartPosition.x, climbMidPoint.y, climbStartPosition.z);
            transform.position = Vector3.MoveTowards(
                transform.position,
                upwardTarget + clearanceHeight * Vector3.up,
                climbSpeed * Time.deltaTime
            );

            if (Vector3.Distance(transform.position, upwardTarget) < 0.05f)
            {
                FinishClimb();
            }
        }
    }

    void FinishClimb()
    {
        isClimbing = false;
        controller.enabled = true;

        if (movement != null)
            movement.ResetVerticalVelocity();
        movement.enabled = true;

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.Log("MANTLE COMPLETE");
    }

    // Returns true when a Climbable-tagged surface is in front of the player. In that case
    // the mantle should not be considered, even if cling itself didn't start this frame
    bool TryHandleClimbableWall()
    {
        if (!DetectForwardWall(out RaycastHit hit))
            return false;

        if (hit.collider == null || !hit.collider.CompareTag(climbableTag))
            return false;

        if (CanStartWallCling(hit))
        {
            StartWallCling(hit);
        }
        // Tagged climbable surfaces are owned by this system. Suppress the mantle
        // branch even if we didn't attach this frame
        return true;
    }

    bool DetectForwardWall(out RaycastHit hit)
    {
        Vector3 forward = transform.forward;
        float startOffset = skinWidth;

        Vector3 lowOrigin = transform.position + Vector3.up * lowCheckHeight + forward * startOffset;
        Vector3 midOrigin = transform.position + Vector3.up * 1.0f + forward * startOffset;

        bool lowOk = Physics.SphereCast(lowOrigin, wallClingCheckRadius, forward, out RaycastHit lowHit, wallClingDetectDistance);
        bool midOk = Physics.SphereCast(midOrigin, wallClingCheckRadius, forward, out RaycastHit midHit, wallClingDetectDistance);

        if (midOk && (!lowOk || midHit.distance < lowHit.distance))
        {
            hit = midHit;
            return true;
        }
        if (lowOk)
        {
            hit = lowHit;
            return true;
        }

        hit = default;
        return false;
    }

    bool CanStartWallCling(RaycastHit hit)
    {
        if (clingCooldownTimer > 0f) return false;
        if (movement != null && movement.getIsCrouching()) return false;

        // Surface must be roughly vertical so wall-space basis vectors are stable
        float tiltFromVertical = Mathf.Abs(Vector3.Angle(hit.normal, Vector3.up) - 90f);
        if (tiltFromVertical > maxWallTiltFromVertical) return false;

        // Player must be actively moving / pressing toward the wall to grip it
        bool forwardPressed = Keyboard.current != null && Keyboard.current.wKey.isPressed;
        bool forwardVelocity = movement != null && movement.GetForwardVelocity() > 0.1f;
        return forwardPressed || forwardVelocity;
    }

    void StartWallCling(RaycastHit hit)
    {
        isWallClinging = true;
        wallNormal = hit.normal;
        currentClingCollider = hit.collider;
        topOutTimer = 0f;

        // Snap to a consistent horizontal offset from the wall surface; preserve current Y
        // so the player doesn't pop vertically when gripping
        Vector3 contact = hit.point;
        Vector3 snappedPos = new Vector3(
            contact.x + wallNormal.x * wallClingDistance,
            transform.position.y,
            contact.z + wallNormal.z * wallClingDistance);

        controller.enabled = false;
        transform.position = snappedPos;
        controller.enabled = true;

        // Hand off control: pause normal movement and kill vertical velocity so we don't
        // accumulate gravity while attached
        if (movement != null)
        {
            movement.ResetVerticalVelocity();
            movement.enabled = false;
        }

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.Log($"WALL CLING STARTED on {hit.collider.name}");
    }

    void PerformWallCling()
    {
        // Detach on jump (Space)
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            DetachFromWall(jumpOff: true);
            return;
        }

        // Build wall-space basis. wallUp is the world up projected onto the wall plane
        // wallRight is along the wall surface, perpendicular to wallNormal and wallUp
        Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, wallNormal);
        if (wallUp.sqrMagnitude < 0.0001f)
        {
            // Surface degenerated to floor/ceiling - bail
            DetachFromWall(jumpOff: false);
            return;
        }
        wallUp.Normalize();

        Vector3 wallRight = Vector3.Cross(wallUp, wallNormal).normalized;
        // Make A/D match the player's perceived left/right regardless of which way the cross resolves
        if (Vector3.Dot(wallRight, transform.right) < 0f)
            wallRight = -wallRight;

        Vector2 input = ReadMoveInput();
        Vector3 motion = wallUp * (input.y * wallClimbVerticalSpeed)
                       + wallRight * (input.x * wallClimbHorizontalSpeed);

        controller.Move(motion * Time.deltaTime);

        // Re-probe forward to confirm we're still in front of a climbable surface and to
        // track curved walls / adjacent climbable colliders. Also corrects drift along the
        // wall normal so the player stays at a consistent offset
        Vector3 probeOrigin = transform.position + Vector3.up * 1.0f;
        Vector3 probeDir = -wallNormal;
        float probeDistance = wallClingDistance + 0.5f;

        if (Physics.SphereCast(probeOrigin, wallClingCheckRadius * 0.8f, probeDir, out RaycastHit reHit, probeDistance)
            && reHit.collider != null
            && reHit.collider.CompareTag(climbableTag))
        {
            wallNormal = reHit.normal;
            currentClingCollider = reHit.collider;

            // Distance from player pivot to wall surface, measured along the (current) normal
            float currentDistance = Vector3.Dot(transform.position - reHit.point, wallNormal);
            float correction = wallClingDistance - currentDistance;
            if (Mathf.Abs(correction) > 0.001f)
                controller.Move(wallNormal * correction);

            // Back on the wall — refresh the grace timer.
            topOutTimer = 0f;
        }
        else
        {
            // No climbable surface in front. Keep the player attached for a short grace
            // period so they can keep climbing past the top edge and step onto the ledge
            // above. The timer ticks every frame regardless of input - that's what keeps
            // them from ever getting stuck floating in mid-air with cling controls. The
            // actual displacement is still controller.Move, so ceilings and other geometry
            // block them just like normal movement; we never override physics
            topOutTimer += Time.deltaTime;
            if (topOutTimer >= wallClingTopOutDuration)
            {
                DetachFromWall(jumpOff: false);
            }
        }
    }

    void DetachFromWall(bool jumpOff)
    {
        isWallClinging = false;
        currentClingCollider = null;
        clingCooldownTimer = wallClingReattachCooldown;

        if (jumpOff)
        {
            // Brief outward shove so the player visibly pushes off and the cooldown
            // keeps them from immediately re-gripping the same surface
            controller.Move(wallNormal * wallClingJumpOffPush);
        }

        if (movement != null)
        {
            movement.ResetVerticalVelocity();
            movement.enabled = true;
        }

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.Log($"WALL CLING ENDED (jumpOff={jumpOff})");
    }

    Vector2 ReadMoveInput()
    {
        Vector2 v = Vector2.zero;
        if (Keyboard.current == null) return v;
        if (Keyboard.current.wKey.isPressed) v.y += 1f;
        if (Keyboard.current.sKey.isPressed) v.y -= 1f;
        if (Keyboard.current.dKey.isPressed) v.x += 1f;
        if (Keyboard.current.aKey.isPressed) v.x -= 1f;
        return v;
    }
}
