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

    [Tooltip("Stamina drained per second while clinging and moving (W/A/S/D).")]
    [Min(0f)]
    public float wallClimbStaminaDrainRate = 1.25f;

    [Tooltip("Short delay after detaching before the player can re-grip the same wall.")]
    public float wallClingReattachCooldown = 0.25f;

    [Tooltip("Outward push (toward wall normal) applied when jumping off the wall.")]
    public float wallClingJumpOffPush = 0.35f;

    [Tooltip("Maximum tilt from vertical (degrees) a surface can have while still counting as a climbable wall.")]
    public float maxWallTiltFromVertical = 30f;

    [Tooltip("Grace period (in seconds) while climbing up past the top lip before detaching. Movement stays at full speed during this window.")]
    public float wallClingTopOutDuration = 0.8f;

    [Tooltip("Extra upward speed applied while hauling over the top edge of a wall.")]
    [Min(0f)]
    public float wallClingTopOutUpBoost = 1.25f;

    [Tooltip("How far the player can turn left or right (degrees) while wall-clinging. Pitch is unrestricted.")]
    [Range(1f, 180f)]
    public float wallClingYawLimit = 90f;

    [Tooltip("Max sideways distance (m) a wall probe hit can be from the player's center column. Stops clinging when you've moved past the edge.")]
    [Min(0.1f)]
    public float wallClingMaxLateralHitOffset = 0.42f;

    [Tooltip("How far ahead (along A/D) to probe when trying to wrap around an outside corner.")]
    [Min(0.05f)]
    public float wallClingCornerProbeAhead = 0.35f;

    [Tooltip("Extra inset toward a concave (inside) corner when sampling the adjacent face.")]
    [Min(0f)]
    public float wallClingInsideCornerInset = 0.22f;

    [Tooltip("How far ahead to search for the perpendicular wall at a concave corner.")]
    [Min(0.1f)]
    public float wallClingInsideFaceProbeReach = 0.55f;

    [Header("Wall Cling Feel")]
    [Tooltip("How quickly the attached normal blends when rounding a corner or transferring to an adjacent face.")]
    [Min(1f)]
    public float wallNormalBlendSpeed = 9f;

    [Tooltip("How quickly the player is pulled back to the correct offset from the wall (prevents snapping).")]
    [Min(1f)]
    public float wallOffsetPullSpeed = 14f;

    [Tooltip("Max distance pulled toward/away from the wall per frame.")]
    [Min(0.01f)]
    public float wallMaxOffsetPullPerFrame = 0.12f;

    [Tooltip("How quickly the yaw reference recenters when the wall face changes.")]
    [Min(1f)]
    public float wallYawRecenterSpeed = 6f;

    [Tooltip("Subtle movement along the corner edge while wrapping around outside corners.")]
    [Min(0f)]
    public float wallCornerArcSpeed = 1.6f;

    [Tooltip("Subtle movement into concave corners while transferring to the adjacent inside face.")]
    [Min(0f)]
    public float wallInsideCornerArcSpeed = 1.4f;

    [Tooltip("Seconds without a solid surface before detaching (unless climbing up over the top).")]
    [Min(0.02f)]
    public float wallClingAirDetachDelay = 0.14f;

    [Tooltip("Minimum multi-probe contact strength (0-1) required to stay attached.")]
    [Range(0.2f, 1f)]
    public float wallClingMinContactStrength = 0.42f;

    [Tooltip("Total angular spread (degrees) for corner-wrap probe fan.")]
    [Range(20f, 120f)]
    public float wallCornerProbeFanDegrees = 72f;

    [Tooltip("Number of directions in the corner-wrap probe fan.")]
    [Range(3, 12)]
    public int wallCornerProbeSteps = 7;

    static readonly float[] WallProbeHeights = { 0.45f, 1.0f, 1.45f };

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
    private float offSurfaceTimer = 0f;
    private Vector3 smoothedWallNormal = Vector3.forward;
    private float wallClingYawCenter;

    enum CornerWrapKind
    {
        None,
        Outside,
        Inside
    }

    public bool IsWallClinging => isWallClinging;
    public float WallClingYawCenter => wallClingYawCenter;
    public float WallClingYawLimit => wallClingYawLimit;

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
        if (!DetectForwardWall(out RaycastHit hit) && !DetectNearestClimbableWall(out hit))
            return false;

        if (hit.collider == null || !hit.collider.CompareTag(climbableTag))
            return false;

        if (CanStartWallCling(hit))
        {
            StartWallCling(hit);
            return true;
        }

        // Grounded players should not engage the wall-climb system - lets them run up to
        // fences without sticking. Airborne players still claim climbables for cling/mantle routing
        if (controller.isGrounded)
            return false;

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

    // At concave corners the player often does not face the wall directly; check all
    // horizontal directions for the nearest climbable surface facing the player.
    bool DetectNearestClimbableWall(out RaycastHit hit)
    {
        hit = default;
        float bestDistance = float.MaxValue;
        Vector3 chest = transform.position + Vector3.up * 1.0f;

        Vector3[] directions =
        {
            transform.forward,
            -transform.forward,
            transform.right,
            -transform.right
        };

        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 dir = directions[i];
            if (dir.sqrMagnitude < 0.0001f)
                continue;

            dir.Normalize();
            if (!Physics.SphereCast(chest, wallClingCheckRadius, dir, out RaycastHit candidate, wallClingDetectDistance + 0.15f))
                continue;

            if (candidate.collider == null || !candidate.collider.CompareTag(climbableTag))
                continue;

            if (!IsValidClimbableWall(candidate.normal))
                continue;

            float facing = Vector3.Dot(chest - candidate.point, candidate.normal);
            if (facing < 0.05f)
                continue;

            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                hit = candidate;
            }
        }

        return bestDistance < float.MaxValue;
    }

    bool CanStartWallCling(RaycastHit hit)
    {
        if (controller.isGrounded) return false;
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
        smoothedWallNormal = hit.normal;
        currentClingCollider = hit.collider;
        topOutTimer = 0f;
        offSurfaceTimer = 0f;

        RecenterWallClingYaw(smooth: false);

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
        ApplyWallClimbStaminaDrain(input);

        Vector3 motion = wallUp * (input.y * wallClimbVerticalSpeed)
                       + wallRight * (input.x * wallClimbHorizontalSpeed);

        Vector3 normalBeforeMove = smoothedWallNormal;
        bool movingUp = input.y > 0.15f;
        bool nearWallTop = IsNearWallTop(wallUp, wallRight);

        bool hasSurface = SolveWallSurface(wallUp, wallRight, input, out RaycastHit surfaceHit, out float contactStrength, out CornerWrapKind cornerWrap);

        bool toppingOut = movingUp && (nearWallTop || !hasSurface);

        if (hasSurface)
        {
            ApplySmoothWallAttachment(
                surfaceHit,
                contactStrength,
                cornerWrap,
                normalBeforeMove,
                wallUp,
                wallRight,
                input,
                limitWallPullForTopOut: toppingOut);
        }

        if (toppingOut)
        {
            topOutTimer += Time.deltaTime;
            offSurfaceTimer = 0f;

            if (topOutTimer >= wallClingTopOutDuration)
                DetachFromWall(jumpOff: false);
        }
        else if (hasSurface)
        {
            topOutTimer = 0f;
            offSurfaceTimer = 0f;
        }
        else
        {
            HandleWallSurfaceLost(input);
        }

        float grip = toppingOut
            ? 1f
            : hasSurface
                ? 1f
                : Mathf.Max(0f, 1f - offSurfaceTimer / wallClingAirDetachDelay);
        controller.Move(motion * grip * Time.deltaTime);

        if (toppingOut && wallClingTopOutUpBoost > 0f)
            controller.Move(wallUp * wallClingTopOutUpBoost * Time.deltaTime);

        // After moving, confirm we are still on a surface (stops sideways drift in open air)
        if (!hasSurface
            && SolveWallSurface(wallUp, wallRight, input, out surfaceHit, out contactStrength, out cornerWrap))
        {
            bool stillTopping = movingUp && (IsNearWallTop(wallUp, wallRight) || !hasSurface);
            ApplySmoothWallAttachment(
                surfaceHit,
                contactStrength,
                cornerWrap,
                normalBeforeMove,
                wallUp,
                wallRight,
                input,
                limitWallPullForTopOut: stillTopping);

            if (!stillTopping)
            {
                offSurfaceTimer = 0f;
                topOutTimer = 0f;
            }
        }
    }

    // True when lower body probes still see the wall but upper probes do not - player is at the top lip
    bool IsNearWallTop(Vector3 wallUp, Vector3 wallRight)
    {
        bool upperContact = HasForwardWallContact(WallProbeHeights[WallProbeHeights.Length - 1], wallUp, wallRight);
        if (upperContact)
            return false;

        for (int i = 0; i < WallProbeHeights.Length - 1; i++)
        {
            if (HasForwardWallContact(WallProbeHeights[i], wallUp, wallRight))
                return true;
        }

        return false;
    }

    bool HasForwardWallContact(float probeHeight, Vector3 wallUp, Vector3 wallRight)
    {
        Vector3 origin = transform.position + Vector3.up * probeHeight;
        if (!TryClimbableProbe(origin, -smoothedWallNormal, out RaycastHit hit))
            return false;

        if (!IsValidClimbableWall(hit.normal))
            return false;

        Vector3 chest = transform.position + Vector3.up * 1.0f;
        if (Vector3.Dot(chest - hit.point, hit.normal) < 0.05f)
            return false;

        return ScoreWallHit(hit, wallUp, wallRight, out _, lateralLimitScale: 1.35f);
    }

    // Multi-probe surface solver: chest/waist/head samples, corner fan, and lateral
    // ahead casts. Picks the best valid hit and returns an aggregate contact strength
    bool SolveWallSurface(
        Vector3 wallUp,
        Vector3 wallRight,
        Vector2 input,
        out RaycastHit bestHit,
        out float contactStrength,
        out CornerWrapKind cornerWrap)
    {
        bestHit = default;
        contactStrength = 0f;
        cornerWrap = CornerWrapKind.None;

        float bestScore = -1f;
        int forwardValidProbes = 0;

        Vector3 primaryDir = -smoothedWallNormal;

        for (int i = 0; i < WallProbeHeights.Length; i++)
        {
            float height = WallProbeHeights[i];
            Vector3 origin = transform.position + Vector3.up * height;

            if (TryClimbableProbe(origin, primaryDir, out RaycastHit hit)
                && ScoreWallHit(hit, wallUp, wallRight, out float score))
            {
                forwardValidProbes++;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestHit = hit;
                }
            }
        }

        float sideInput = input.x;
        float forwardBestScore = bestScore;

        if (TryGetCornerMoveDirection(wallRight, input, out Vector3 moveAlongWall))
        {
            ProbeOutsideCornerWrap(wallUp, wallRight, moveAlongWall, ref bestScore, ref bestHit);

            float insideScore = -1f;
            RaycastHit insideHit = default;
            ProbeInsideCornerWrap(wallUp, wallRight, moveAlongWall, ref insideScore, ref insideHit);

            if (insideScore > 0f
                && ShouldPreferInsideCornerFace(
                    insideHit,
                    moveAlongWall,
                    smoothedWallNormal,
                    insideScore,
                    forwardBestScore))
            {
                bestScore = insideScore;
                bestHit = insideHit;
                cornerWrap = CornerWrapKind.Inside;
            }
        }

        if (bestScore < 0f)
            return false;

        contactStrength = bestScore;
        if (forwardValidProbes >= 2)
            contactStrength = Mathf.Max(contactStrength, 0.68f);
        else if (forwardValidProbes >= 1)
            contactStrength = Mathf.Max(contactStrength, 0.48f);

        if (cornerWrap == CornerWrapKind.None)
        {
            bool faceChanged = Vector3.Angle(smoothedWallNormal, bestHit.normal) > 5f
                               || bestHit.collider != currentClingCollider;
            if (faceChanged && Mathf.Abs(sideInput) > 0.08f)
            {
                Vector3 classifyMoveDir = wallRight * Mathf.Sign(sideInput);
                cornerWrap = ClassifyCornerWrap(smoothedWallNormal, bestHit.normal, classifyMoveDir);
            }
        }

        return contactStrength >= wallClingMinContactStrength;
    }

    static bool ShouldPreferInsideCornerFace(
        RaycastHit insideHit,
        Vector3 moveAlongWall,
        Vector3 currentNormal,
        float insideScore,
        float forwardScore)
    {
        if (forwardScore < 0f)
            return true;

        float angle = Vector3.Angle(currentNormal, insideHit.normal);

        if (Vector3.Dot(insideHit.normal, moveAlongWall) >= 0.05f)
            return false;

        if (angle < 35f)
            return false;

        if (angle >= 50f)
            return true;

        return insideScore >= forwardScore * 0.72f;
    }

    void ProbeOutsideCornerWrap(Vector3 wallUp, Vector3 wallRight, Vector3 moveAlongWall, ref float bestScore, ref RaycastHit bestHit)
    {
        Vector3 primaryDir = -smoothedWallNormal;
        Vector3 bentTarget = (-smoothedWallNormal + moveAlongWall * 0.95f).normalized;
        int steps = Mathf.Max(wallCornerProbeSteps, 3);

        for (int step = 0; step <= steps; step++)
        {
            float t = step / (float)steps;
            Vector3 fanDir = Vector3.Slerp(primaryDir, bentTarget, t).normalized;
            float ahead = wallClingCornerProbeAhead * t;

            for (int h = 0; h < WallProbeHeights.Length; h++)
            {
                float height = WallProbeHeights[h];
                Vector3 origin = transform.position
                                 + Vector3.up * height
                                 + moveAlongWall * ahead;

                if (!TryClimbableProbe(origin, fanDir, out RaycastHit fanHit))
                    continue;

                if (!ScoreWallHit(fanHit, wallUp, wallRight, out float fanScore))
                    continue;

                if (step > 0 && !IsWrapPathClear(fanHit, insideCorner: false))
                    continue;

                if (fanScore > bestScore)
                {
                    bestScore = fanScore;
                    bestHit = fanHit;
                }
            }
        }

        Vector3 aheadOrigin = transform.position
                              + moveAlongWall * wallClingCornerProbeAhead
                              + Vector3.up * 1.0f;
        Vector3 towardWall = (-smoothedWallNormal - moveAlongWall * 0.4f).normalized;

        TryCornerAheadProbes(aheadOrigin, towardWall, wallUp, wallRight, insideCorner: false, ref bestScore, ref bestHit);
    }

    void ProbeInsideCornerWrap(
        Vector3 wallUp,
        Vector3 wallRight,
        Vector3 moveAlongWall,
        ref float bestScore,
        ref RaycastHit bestHit)
    {
        // Primary inside-corner cast: straight toward the perpendicular wall the player is walking into
        float[] aheadSamples = { 0.08f, 0.2f, 0.35f, 0.5f };
        for (int a = 0; a < aheadSamples.Length; a++)
        {
            float ahead = aheadSamples[a] * wallClingInsideFaceProbeReach;
            for (int h = 0; h < WallProbeHeights.Length; h++)
            {
                float height = WallProbeHeights[h];
                Vector3 origin = transform.position + Vector3.up * height + moveAlongWall * ahead;
                Vector3 towardFace = moveAlongWall.normalized;

                if (!TryBestInsideProbeHit(origin, towardFace, moveAlongWall, out RaycastHit perpHit, out float perpScore))
                    continue;

                if (perpScore > bestScore)
                {
                    bestScore = perpScore;
                    bestHit = perpHit;
                }
            }
        }

        Vector3 primaryDir = -smoothedWallNormal;
        Vector3 bentTarget = (-smoothedWallNormal - moveAlongWall * 0.95f).normalized;
        int steps = Mathf.Max(wallCornerProbeSteps, 3);

        for (int step = 0; step <= steps; step++)
        {
            float t = step / (float)steps;
            Vector3 fanDir = Vector3.Slerp(primaryDir, bentTarget, t).normalized;
            float ahead = wallClingCornerProbeAhead * t;

            for (int h = 0; h < WallProbeHeights.Length; h++)
            {
                float height = WallProbeHeights[h];
                Vector3 origin = transform.position
                                 + Vector3.up * height
                                 + moveAlongWall * ahead;

                if (!TryClimbableProbe(origin, fanDir, out RaycastHit fanHit))
                    continue;

                if (!IsInsideAdjacentFace(fanHit, moveAlongWall, out float fanScore))
                    continue;

                if (step > 1 && !IsWrapPathClear(fanHit, insideCorner: true))
                    continue;

                if (fanScore > bestScore)
                {
                    bestScore = fanScore;
                    bestHit = fanHit;
                }
            }
        }

        Vector3 aheadOrigin = transform.position
                              + moveAlongWall * wallClingCornerProbeAhead
                              + Vector3.up * 1.0f;
        Vector3 towardWall = (-smoothedWallNormal + moveAlongWall * 0.55f).normalized;

        for (int h = 0; h < WallProbeHeights.Length; h++)
        {
            float height = WallProbeHeights[h];
            Vector3 origin = aheadOrigin + Vector3.up * (height - 1.0f);

            if (!TryClimbableProbe(origin, towardWall, out RaycastHit aheadHit))
                continue;

            if (!IsInsideAdjacentFace(aheadHit, moveAlongWall, out float aheadScore))
                continue;

            if (aheadScore > bestScore)
            {
                bestScore = aheadScore;
                bestHit = aheadHit;
            }
        }

        Vector3 intoCorner = (-smoothedWallNormal - moveAlongWall).normalized;
        for (int h = 0; h < WallProbeHeights.Length; h++)
        {
            float height = WallProbeHeights[h];
            Vector3 bisectorOrigin = transform.position
                                     + Vector3.up * height
                                     + moveAlongWall * wallClingCornerProbeAhead * 0.65f;

            if (!TryClimbableProbe(bisectorOrigin, intoCorner, out RaycastHit bisectorHit))
                continue;

            if (!IsInsideAdjacentFace(bisectorHit, moveAlongWall, out float bisectorScore))
                continue;

            if (bisectorScore > bestScore)
            {
                bestScore = bisectorScore;
                bestHit = bisectorHit;
            }
        }
    }

    bool IsInsideAdjacentFace(RaycastHit hit, Vector3 moveAlongWall, out float score)
    {
        score = 0f;
        if (!IsValidClimbableWall(hit.normal))
            return false;

        float angleFromCurrent = Vector3.Angle(smoothedWallNormal, hit.normal);
        if (angleFromCurrent < 30f)
            return false;

        if (Vector3.Dot(hit.normal, moveAlongWall) >= 0.05f)
            return false;

        return ScoreInsideCornerFaceHit(hit, angleFromCurrent, out score);
    }

    bool ScoreInsideCornerFaceHit(RaycastHit hit, float angleFromCurrent, out float score)
    {
        score = 0f;
        Vector3 chest = transform.position + Vector3.up * 1.0f;
        float facing = Vector3.Dot(chest - hit.point, hit.normal);
        if (facing < 0.02f)
            return false;

        float distAlongNormal = Vector3.Dot(transform.position - hit.point, hit.normal);
        float distError = Mathf.Abs(distAlongNormal - wallClingDistance);
        if (distError > 0.95f)
            return false;

        float angleScore = Mathf.Clamp01((angleFromCurrent - 30f) / 60f);
        float distScore = 1f - distError / 0.95f;
        float facingScore = Mathf.Clamp01(facing / 0.25f);
        score = angleScore * 0.45f + distScore * 0.35f + facingScore * 0.2f;
        return score > 0.1f;
    }

    void TryCornerAheadProbes(
        Vector3 aheadOrigin,
        Vector3 towardWall,
        Vector3 wallUp,
        Vector3 wallRight,
        bool insideCorner,
        ref float bestScore,
        ref RaycastHit bestHit)
    {
        float lateralScale = insideCorner ? 1.45f : 1f;

        for (int h = 0; h < WallProbeHeights.Length; h++)
        {
            float height = WallProbeHeights[h];
            Vector3 origin = aheadOrigin + Vector3.up * (height - 1.0f);

            if (!TryClimbableProbe(origin, towardWall, out RaycastHit aheadHit))
                continue;

            if (!ScoreWallHit(aheadHit, wallUp, wallRight, out float aheadScore, lateralScale))
                continue;

            if (!IsWrapPathClear(aheadHit, insideCorner))
                continue;

            if (aheadScore > bestScore)
            {
                bestScore = aheadScore;
                bestHit = aheadHit;
            }
        }
    }

    bool TryGetCornerMoveDirection(Vector3 wallRight, Vector2 input, out Vector3 moveAlongWall)
    {
        if (Mathf.Abs(input.x) > 0.08f)
        {
            moveAlongWall = wallRight * Mathf.Sign(input.x);
            return true;
        }

        if (input.y > 0.1f && TryFindInsideCornerApproach(wallRight, out moveAlongWall))
            return true;

        moveAlongWall = default;
        return false;
    }

    bool TryFindInsideCornerApproach(Vector3 wallRight, out Vector3 moveAlongWall)
    {
        moveAlongWall = default;
        float nearest = float.MaxValue;
        Vector3 chest = transform.position + Vector3.up * 1.0f;

        for (int sign = -1; sign <= 1; sign += 2)
        {
            Vector3 dir = wallRight * sign;
            Vector3 origin = chest + dir * 0.12f;
            if (!Physics.SphereCast(
                    origin,
                    wallClingCheckRadius * 0.7f,
                    dir,
                    out RaycastHit hit,
                    wallClingInsideFaceProbeReach + 0.35f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            if (hit.collider == null || !hit.collider.CompareTag(climbableTag))
                continue;

            if (!IsValidClimbableWall(hit.normal))
                continue;

            float angle = Vector3.Angle(smoothedWallNormal, hit.normal);
            if (angle < 45f)
                continue;

            if (Vector3.Dot(hit.normal, dir) >= 0.05f)
                continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                moveAlongWall = dir;
            }
        }

        return nearest < float.MaxValue;
    }

    static CornerWrapKind ClassifyCornerWrap(Vector3 previousNormal, Vector3 newNormal, Vector3 moveAlongWall)
    {
        if (Vector3.Dot(newNormal, moveAlongWall) < -0.12f)
            return CornerWrapKind.Inside;

        return CornerWrapKind.Outside;
    }

    bool TryClimbableProbe(Vector3 origin, Vector3 direction, out RaycastHit hit)
    {
        float probeDistance = wallClingDistance + 0.45f;
        if (Physics.SphereCast(
                origin,
                wallClingCheckRadius * 0.75f,
                direction.normalized,
                out hit,
                probeDistance,
                ~0,
                QueryTriggerInteraction.Ignore)
            && hit.collider != null
            && hit.collider.CompareTag(climbableTag))
        {
            return true;
        }

        hit = default;
        return false;
    }

    bool TryBestInsideProbeHit(
        Vector3 origin,
        Vector3 direction,
        Vector3 moveAlongWall,
        out RaycastHit bestHit,
        out float bestScore)
    {
        bestHit = default;
        bestScore = -1f;
        float probeDistance = wallClingDistance + wallClingInsideFaceProbeReach + 0.25f;
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            wallClingCheckRadius * 0.75f,
            direction.normalized,
            probeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit candidate = hits[i];
            if (candidate.collider == null || !candidate.collider.CompareTag(climbableTag))
                continue;

            if (!IsInsideAdjacentFace(candidate, moveAlongWall, out float score))
                continue;

            if (score > bestScore)
            {
                bestScore = score;
                bestHit = candidate;
            }
        }

        return bestScore > 0f;
    }

    bool IsValidClimbableWall(Vector3 normal)
    {
        float tiltFromVertical = Mathf.Abs(Vector3.Angle(normal, Vector3.up) - 90f);
        return tiltFromVertical <= maxWallTiltFromVertical;
    }

    bool ScoreWallHit(RaycastHit hit, Vector3 wallUp, Vector3 wallRight, out float score, float lateralLimitScale = 1f)
    {
        score = 0f;
        if (!IsValidClimbableWall(hit.normal))
            return false;

        Vector3 chest = transform.position + Vector3.up * 1.0f;

        // Surface must face the player (reject grazing hits past an open edge)
        float facing = Vector3.Dot(chest - hit.point, hit.normal);
        if (facing < 0.1f)
            return false;

        Vector3 toHit = hit.point - chest;
        Vector3 alongWall = Vector3.ProjectOnPlane(toHit, wallUp);
        float lateral = Mathf.Abs(Vector3.Dot(alongWall, wallRight));
        float lateralLimit = wallClingMaxLateralHitOffset * lateralLimitScale;
        if (lateral > lateralLimit)
            return false;

        float distAlongNormal = Vector3.Dot(transform.position - hit.point, hit.normal);
        float distError = Mathf.Abs(distAlongNormal - wallClingDistance);
        if (distError > 0.38f)
            return false;

        float lateralScore = 1f - lateral / lateralLimit;
        float distScore = 1f - distError / 0.38f;
        float facingScore = Mathf.Clamp01(facing / 0.35f);
        score = lateralScore * 0.35f + distScore * 0.4f + facingScore * 0.25f;
        return score > 0.12f;
    }

    bool IsWrapPathClear(RaycastHit targetHit, bool insideCorner)
    {
        Vector3 start = transform.position + Vector3.up * 1.0f;
        Vector3 end = targetHit.point + targetHit.normal * wallClingDistance;
        Vector3 delta = end - start;
        float dist = delta.magnitude;
        if (dist < 0.05f)
            return true;

        float radius = wallClingCheckRadius * 0.6f;
        if (!Physics.SphereCast(start, radius, delta.normalized, out RaycastHit block, dist, ~0, QueryTriggerInteraction.Ignore))
            return true;

        if (block.collider == targetHit.collider)
            return true;

        if (insideCorner)
        {
            // Concave wrap passes through the other climbable face at the corner
            if (block.collider != null && block.collider.CompareTag(climbableTag))
                return true;

            return false;
        }

        if (block.collider != null && block.collider.CompareTag(climbableTag))
            return true;

        return false;
    }

    void ApplySmoothWallAttachment(
        RaycastHit hit,
        float contactStrength,
        CornerWrapKind cornerWrap,
        Vector3 normalBeforeMove,
        Vector3 wallUp,
        Vector3 wallRight,
        Vector2 input,
        bool limitWallPullForTopOut = false)
    {
        currentClingCollider = hit.collider;

        bool roundingCorner = cornerWrap != CornerWrapKind.None;

        bool insideTransition = cornerWrap == CornerWrapKind.Inside;

        float blendT = 1f - Mathf.Exp(-wallNormalBlendSpeed * Time.deltaTime);
        if (roundingCorner)
            blendT = Mathf.Min(blendT * (insideTransition ? 1.6f : 1.35f), insideTransition ? 0.28f : 0.22f);

        smoothedWallNormal = Vector3.Slerp(smoothedWallNormal, hit.normal, blendT).normalized;
        wallNormal = smoothedWallNormal;

        if (roundingCorner && Mathf.Abs(input.x) > 0.08f)
        {
            float arcWeight = Mathf.Clamp01(Vector3.Angle(normalBeforeMove, hit.normal) / 90f);
            float sideSign = Mathf.Sign(input.x);
            Vector3 moveAlongWall = wallRight * sideSign;

            if (insideTransition && wallInsideCornerArcSpeed > 0f)
            {
                Vector3 intoCorner = Vector3.ProjectOnPlane(-normalBeforeMove - hit.normal, wallUp);
                if (intoCorner.sqrMagnitude > 0.0001f)
                {
                    intoCorner.Normalize();
                    controller.Move(intoCorner * wallInsideCornerArcSpeed * arcWeight * Time.deltaTime);
                }

                controller.Move(moveAlongWall * wallInsideCornerArcSpeed * 0.55f * arcWeight * Time.deltaTime);
            }
            else if (cornerWrap == CornerWrapKind.Outside && wallCornerArcSpeed > 0f)
            {
                Vector3 cornerTangent = Vector3.Cross(wallUp, smoothedWallNormal).normalized * sideSign;
                controller.Move(cornerTangent * wallCornerArcSpeed * arcWeight * Time.deltaTime);
            }
        }

        Vector3 pullNormal = insideTransition ? hit.normal : smoothedWallNormal;
        float maxPull = insideTransition ? wallMaxOffsetPullPerFrame * 2.2f : wallMaxOffsetPullPerFrame;
        float pullSpeed = insideTransition ? wallOffsetPullSpeed * 1.35f : wallOffsetPullSpeed;

        float distAlongNormal = Vector3.Dot(transform.position - hit.point, pullNormal);
        float offsetError = wallClingDistance - distAlongNormal;
        if (limitWallPullForTopOut && input.y > 0.08f)
        {
            maxPull = 0f;
        }
        else if (limitWallPullForTopOut)
        {
            maxPull *= 0.2f;
        }

        float pull = Mathf.Clamp(
            offsetError * pullSpeed * Time.deltaTime * Mathf.Lerp(0.5f, 1f, contactStrength),
            -maxPull,
            maxPull);
        if (Mathf.Abs(pull) > 0.0001f)
            controller.Move(pullNormal * pull);

        RecenterWallClingYaw(smooth: true);
    }

    void RecenterWallClingYaw(bool smooth)
    {
        Vector3 faceWall = Vector3.ProjectOnPlane(-smoothedWallNormal, Vector3.up);
        if (faceWall.sqrMagnitude < 0.0001f)
            return;

        faceWall.Normalize();
        float targetYaw = Mathf.Atan2(faceWall.x, faceWall.z) * Mathf.Rad2Deg;

        if (smooth)
        {
            float t = 1f - Mathf.Exp(-wallYawRecenterSpeed * Time.deltaTime);
            wallClingYawCenter = Mathf.LerpAngle(wallClingYawCenter, targetYaw, t);
        }
        else
        {
            wallClingYawCenter = targetYaw;
            transform.rotation = Quaternion.LookRotation(faceWall);
        }
    }

    void HandleWallSurfaceLost(Vector2 input)
    {
        offSurfaceTimer += Time.deltaTime;

        if (offSurfaceTimer >= wallClingAirDetachDelay)
            DetachFromWall(jumpOff: false);
    }

    void ApplyWallClimbStaminaDrain(Vector2 input)
    {
        if (movement == null || wallClimbStaminaDrainRate <= 0f)
            return;

        if (input.sqrMagnitude < 0.0064f)
            return;

        movement.DrainStamina(wallClimbStaminaDrainRate * Time.deltaTime);

        if (movement.GetSprintCharge() <= 0f)
            DetachFromWall(jumpOff: false);
    }

    void DetachFromWall(bool jumpOff)
    {
        isWallClinging = false;
        currentClingCollider = null;
        offSurfaceTimer = 0f;
        topOutTimer = 0f;
        clingCooldownTimer = wallClingReattachCooldown;

        if (jumpOff)
        {
            // Brief outward shove so the player visibly pushes off and the cooldown
            // keeps them from immediately re-gripping the same surface
            controller.Move(smoothedWallNormal * wallClingJumpOffPush);
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
