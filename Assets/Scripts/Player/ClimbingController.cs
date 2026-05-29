using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mantle climbs over ledges and attached wall-climbing on Climbable-tagged surfaces.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ClimbingController : MonoBehaviour
{
    [Header("Detection")]
    public float wallCheckDistance = 0.6f;
    public float upCheckHeight = 1.3f;
    public float lowCheckHeight = 0.5f;
    public float skinWidth = 0.1f;

    [Header("Climb Offsets")]
    public float heightOffset = 1.2f;
    public float clearanceHeight = 1.2f;

    [Header("Climb")]
    public float climbSpeed = 4f;

    [Header("Attached Wall Climb (Climbable tag)")]
    [Tooltip("Tag applied to surfaces that should be climbed by attaching to the wall (chain link fences, walls with footholds, etc).")]
    public string climbableTag = "Climbable";

    [Tooltip("How far in front of the player to look for a climbable surface to attach to.")]
    public float wallClingDetectDistance = 0.65f;

    [Tooltip("Minimum horizontal alignment (0-1) between view direction and the wall face required to start clinging. Higher values require looking more directly at the wall.")]
    [Range(0f, 1f)]
    public float wallClingStartFacingThreshold = 0.35f;

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

    /// <summary>
    /// Waist, chest, and head probe heights for multi-sample wall contact while clinging.
    /// </summary>
    static readonly float[] WallProbeHeights = { 0.45f, 1.0f, 1.45f };

    private CharacterController controller;
    private PlayerMovement movement;
    private InputAction moveAction;

    /// <summary>
    /// True while the player is lerping through a ledge mantle (not wall-cling).
    /// </summary>
    private bool isClimbing = false;
    /// <summary>
    /// World position the mantle lerp ends at (on top of the ledge with forward offset).
    /// </summary>
    private Vector3 climbTarget;
    /// <summary>
    /// Player position when the current mantle began; used for rise-then-forward paths.
    /// </summary>
    private Vector3 climbStartPosition;
    /// <summary>
    /// Intermediate waypoint for tall mantles: rise to lip height before moving onto the ledge.
    /// </summary>
    private Vector3 climbMidPoint;

    /// <summary>
    /// True while attached to a Climbable-tagged surface with movement locked to the wall plane.
    /// </summary>
    private bool isWallClinging = false;
    /// <summary>
    /// Outward-facing normal of the wall face used for offset and look constraints.
    /// </summary>
    private Vector3 wallNormal = Vector3.forward;
    /// <summary>
    /// Collider currently providing cling contact; used to detect face changes at corners.
    /// </summary>
    private Collider currentClingCollider;
    /// <summary>
    /// Countdown after detach before the same wall can be gripped again.
    /// </summary>
    private float clingCooldownTimer = 0f;
    /// <summary>
    /// Seconds spent hauling over the top lip with reduced or no forward wall contact.
    /// </summary>
    private float topOutTimer = 0f;

    /// <summary>
    /// Seconds without wall contact while clinging. Resets when a climbable surface is re-acquired.
    /// </summary>
    private float offSurfaceTimer = 0f;
    /// <summary>
    /// Blended attach normal; eases corner transfers instead of snapping each frame.
    /// </summary>
    private Vector3 smoothedWallNormal = Vector3.forward;
    /// <summary>
    /// World yaw reference for limiting look rotation while wall-clinging.
    /// </summary>
    private float wallClingYawCenter;

    /// <summary>
    /// How the player is transitioning between wall faces at a corner during cling.
    /// </summary>
    enum CornerWrapKind
    {
        None,
        Outside,
        Inside
    }

    public bool IsWallClinging => isWallClinging;
    public float WallClingYawCenter => wallClingYawCenter;
    public float WallClingYawLimit => wallClingYawLimit;

    /// <summary>
    /// Caches CharacterController, PlayerMovement, and the Move input action.
    /// </summary>
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

    /// <summary>
    /// Drives mantle, wall-cling, or ledge detection depending on current climb state.
    /// </summary>
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

        // Climbable-tagged surfaces use wall-cling; otherwise attempt mantle when airborne.
        if (TryHandleClimbableWall())
        {
            return;
        }
        else if (!controller.isGrounded && !movement.getIsCrouching() && movement.GetForwardVelocity() > 0.1f)
        {
            DetectLedgeAndAutoClimb();
        }
    }

    /// <summary>
    /// Probes for a mantle ledge in front of the player and starts a climb when valid.
    /// </summary>
    void DetectLedgeAndAutoClimb()
    {
        Vector3 forward = transform.forward;
        float startOffset = skinWidth;

        // Spherecast at low and mid heights to find the nearest wall segment in front.
        Vector3 lowOrigin = transform.position + Vector3.up * lowCheckHeight + forward * startOffset;
        Vector3 midOrigin = transform.position + Vector3.up * 1.0f + forward * startOffset;
        Vector3 upperOrigin = transform.position + Vector3.up * upCheckHeight + forward * startOffset;

        float sphereRadius = 0.2f;
        RaycastHit lowHit, midHit;
        bool wallInFrontLow = Physics.SphereCast(lowOrigin, sphereRadius, forward, out lowHit, wallCheckDistance);
        bool wallInFrontMid = Physics.SphereCast(midOrigin, sphereRadius, forward, out midHit, wallCheckDistance);

        // Prefer the nearer of low and mid hits as the primary wall sample.
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

        // Reject walls that are too low or too high relative to the player.
        float wallHeightFromGround = wallHit.point.y - transform.position.y;

        if (wallHeightFromGround < 0.4f)
            return;

        if (wallHeightFromGround > upCheckHeight + 0.3f)
            return;

        // Stepped walls: probe above the lip, then forward for a farther wall that can be climbed.
        Vector3 forwardRayOrigin = wallHit.point + Vector3.up * 1.0f;
        RaycastHit forwardWallHit;
        bool hasForwardWall = Physics.Raycast(forwardRayOrigin, forward, out forwardWallHit, 1.2f);

        Vector3 aboveOrigin = wallHit.point + Vector3.up * 0.5f;
        RaycastHit aboveHit;
        bool hasWallAbove = Physics.Raycast(aboveOrigin, forward, out aboveHit, wallCheckDistance);

        // Decide between a simple lip climb and a stepped wall with a farther face.
        bool canClimb = false;
        float stepDepth = 0f;

        if (!hasWallAbove)
        {
            canClimb = true;
        }
        else if (hasForwardWall && forwardWallHit.distance > wallHit.distance + 0.2f)
        {
            stepDepth = forwardWallHit.distance - wallHit.distance;

            float steppedWallHeight = forwardWallHit.point.y - transform.position.y;
            if (steppedWallHeight > upCheckHeight + 0.3f)
                return;

            canClimb = true;
        }

        if (!canClimb)
            return;

        Vector3 searchOrigin = hasForwardWall && stepDepth > 0.2f ? forwardWallHit.point : wallHit.point;
        searchOrigin += forward * 0.1f;

        // Scan upward from the wall to find the standable surface above the lip.
        RaycastHit topHit = new RaycastHit();
        bool foundSurface = false;
        float surfaceHeight = 0f;

        for (float yOffset = 0.5f; yOffset <= 2.0f; yOffset += 0.3f)
        {
            Vector3 rayStart = searchOrigin + Vector3.up * yOffset;

            if (Physics.Raycast(rayStart, Vector3.down, out topHit, 2.0f))
            {
                if (topHit.point.y > wallHit.point.y + 0.2f)
                {
                    surfaceHeight = topHit.point.y;
                    foundSurface = true;
                    break;
                }
            }
        }

        if (!foundSurface)
            return;

        float totalClimbHeight = surfaceHeight - transform.position.y;

        if (totalClimbHeight > 1.5f)
            return;

        if (totalClimbHeight < 0.4f)
            return;

        float slopeAngle = Vector3.Angle(topHit.normal, Vector3.up);
        if (slopeAngle > 50f)
            return;

        // Build mantle destination from climb height and whether a step was detected.
        if (totalClimbHeight < 0.8f)
        {
            climbTarget = topHit.point + Vector3.up * 0.1f;
            climbTarget += forward * 0.5f;
        }
        else
        {
            climbTarget = topHit.point + Vector3.up * 0.2f;

            if (hasForwardWall && stepDepth > 0.2f)
            {
                climbTarget += forward * 0.3f;
            }
            else
            {
                climbTarget += forward * 0.4f;
            }
        }

        StartClimb();
    }

    /// <summary>
    /// Locks movement and computes the mantle path from climb height (single step vs. rise-then-forward).
    /// </summary>
    void StartClimb()
    {
        isClimbing = true;
        climbStartPosition = transform.position;

        float totalClimbHeight = climbTarget.y - climbStartPosition.y;

        // Short climbs go straight to target; tall climbs rise to mid height before moving forward.
        if (totalClimbHeight < 0.8f)
        {
            climbMidPoint = climbTarget;
        }
        else
        {
            Vector3 forward = transform.forward;
            Vector3 wallTop = climbTarget - forward;
            climbMidPoint = new Vector3(climbStartPosition.x, wallTop.y + clearanceHeight, climbStartPosition.z);
        }

        if (movement != null)
            movement.enabled = false;

        controller.enabled = false;
    }

    /// <summary>
    /// Lerps the player toward the mantle target each frame until the climb finishes.
    /// </summary>
    void PerformMantle()
    {
        float totalClimbHeight = climbTarget.y - climbStartPosition.y;

        // Short mantles: move directly to the ledge stand position.
        if (totalClimbHeight < 0.8f)
        {
            transform.position = Vector3.MoveTowards(transform.position, climbTarget, climbSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, climbTarget) < 0.05f)
            {
                FinishClimb();
            }
        }
        else
        {
            // Tall mantles: rise to mid height with clearance before finishing on the ledge.
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

    /// <summary>
    /// Restores CharacterController and PlayerMovement after a mantle completes.
    /// </summary>
    void FinishClimb()
    {
        isClimbing = false;
        controller.enabled = true;

        if (movement != null)
            movement.ResetVerticalVelocity();
        movement.enabled = true;
    }

    /// <summary>
    /// Returns true when a Climbable-tagged surface is in front, reserving it from mantle logic even if cling did not start.
    /// </summary>
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

        // Grounded players skip cling so they can run up to fences without sticking.
        if (controller.isGrounded)
            return false;

        return true;
    }

    /// <summary>
    /// Spherecasts forward at low and mid height and returns the nearer Climbable hit.
    /// </summary>
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

    /// <summary>
    /// At concave corners, searches horizontal directions for the nearest climbable surface facing the player.
    /// </summary>
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

            if (!IsClimbableWallInFront(candidate))
                continue;

            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                hit = candidate;
            }
        }

        return bestDistance < float.MaxValue;
    }

    /// <summary>
    /// True when airborne, off cooldown, upright enough, facing the wall, and pressing or moving forward into it.
    /// </summary>
    bool CanStartWallCling(RaycastHit hit)
    {
        if (controller.isGrounded) return false;
        if (clingCooldownTimer > 0f) return false;
        if (movement != null && movement.getIsCrouching()) return false;

        float tiltFromVertical = Mathf.Abs(Vector3.Angle(hit.normal, Vector3.up) - 90f);
        if (tiltFromVertical > maxWallTiltFromVertical) return false;

        if (!IsClimbableWallInFront(hit))
            return false;

        bool forwardPressed = Keyboard.current != null && Keyboard.current.wKey.isPressed;
        bool forwardVelocity = movement != null && movement.GetForwardVelocity() > 0.1f;
        return forwardPressed || forwardVelocity;
    }

    /// <summary>
    /// True when the climbable face lies in front of the player's horizontal view within <see cref="wallClingStartFacingThreshold"/>.
    /// </summary>
    bool IsClimbableWallInFront(RaycastHit hit)
    {
        Vector3 viewForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Vector3 towardWall = Vector3.ProjectOnPlane(-hit.normal, Vector3.up);

        if (viewForward.sqrMagnitude < 0.0001f || towardWall.sqrMagnitude < 0.0001f)
            return false;

        viewForward.Normalize();
        towardWall.Normalize();
        return Vector3.Dot(viewForward, towardWall) >= wallClingStartFacingThreshold;
    }

    /// <summary>
    /// Attaches the player to a climbable wall, snapping offset and pausing normal movement.
    /// </summary>
    void StartWallCling(RaycastHit hit)
    {
        isWallClinging = true;
        wallNormal = hit.normal;
        smoothedWallNormal = hit.normal;
        currentClingCollider = hit.collider;
        topOutTimer = 0f;
        offSurfaceTimer = 0f;

        RecenterWallClingYaw(smooth: false);

        Vector3 contact = hit.point;
        Vector3 snappedPos = new Vector3(
            contact.x + wallNormal.x * wallClingDistance,
            transform.position.y,
            contact.z + wallNormal.z * wallClingDistance);

        controller.enabled = false;
        transform.position = snappedPos;
        controller.enabled = true;

        if (movement != null)
        {
            movement.ResetVerticalVelocity();
            movement.enabled = false;
        }
    }

    /// <summary>
    /// Updates wall cling movement, surface attachment, and top-out detach logic.
    /// </summary>
    void PerformWallCling()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            DetachFromWall(jumpOff: true);
            return;
        }

        // Build wall-aligned up/right axes and read WASD input for cling motion.
        Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, wallNormal);
        if (wallUp.sqrMagnitude < 0.0001f)
        {
            DetachFromWall(jumpOff: false);
            return;
        }
        wallUp.Normalize();

        Vector3 wallRight = Vector3.Cross(wallUp, wallNormal).normalized;
        if (Vector3.Dot(wallRight, transform.right) < 0f)
            wallRight = -wallRight;

        Vector2 input = ReadMoveInput();
        ApplyWallClimbStaminaDrain(input);

        Vector3 motion = wallUp * (input.y * wallClimbVerticalSpeed)
                       + wallRight * (input.x * wallClimbHorizontalSpeed);

        Vector3 normalBeforeMove = smoothedWallNormal;
        bool movingUp = input.y > 0.15f;
        bool nearWallTop = IsNearWallTop(wallUp, wallRight);

        // Multi-probe surface solve and corner-wrap classification for this frame.
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

        // Top-out grace: keep moving up briefly past the lip before detaching.
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

        // Scale horizontal/vertical cling speed by contact grip (full during top-out).
        float grip = toppingOut
            ? 1f
            : hasSurface
                ? 1f
                : Mathf.Max(0f, 1f - offSurfaceTimer / wallClingAirDetachDelay);
        controller.Move(motion * grip * Time.deltaTime);

        if (toppingOut && wallClingTopOutUpBoost > 0f)
            controller.Move(wallUp * wallClingTopOutUpBoost * Time.deltaTime);

        // Re-probe after motion to re-attach when drifting sideways off the face in open air.
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

    /// <summary>
    /// True when lower probes still see the wall but upper probes do not (player is at the top lip).
    /// </summary>
    bool IsNearWallTop(Vector3 wallUp, Vector3 wallRight)
    {
        // Top lip: lower probes still hit the wall but the highest probe does not.
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

    /// <summary>
    /// True when a climbable hit at the given height scores as valid forward wall contact.
    /// </summary>
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

    /// <summary>
    /// Multi-probe surface solver using chest, waist, and head samples plus corner-wrap probes.
    /// </summary>
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

        // Phase 1: forward fan at waist, chest, and head along the current smoothed normal.
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

        // Phase 2: when strafing (or approaching an inside corner), probe outside and inside wrap paths.
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

        // Boost contact strength when multiple forward probes agree on the same face.
        contactStrength = bestScore;
        if (forwardValidProbes >= 2)
            contactStrength = Mathf.Max(contactStrength, 0.68f);
        else if (forwardValidProbes >= 1)
            contactStrength = Mathf.Max(contactStrength, 0.48f);

        // Infer outside vs. inside wrap from normal change when strafing around a corner.
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

    /// <summary>
    /// Picks the concave adjacent face when its score beats forward contact or the angle warrants a transfer.
    /// </summary>
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

    /// <summary>
    /// Fans probes around an outside (convex) corner and samples ahead for the next climbable face.
    /// </summary>
    void ProbeOutsideCornerWrap(Vector3 wallUp, Vector3 wallRight, Vector3 moveAlongWall, ref float bestScore, ref RaycastHit bestHit)
    {
        // Slerp from current normal toward the strafe direction across a multi-step fan.
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

        // Ahead sample past the corner edge for a direct hit on the new outside face.
        Vector3 aheadOrigin = transform.position
                              + moveAlongWall * wallClingCornerProbeAhead
                              + Vector3.up * 1.0f;
        Vector3 towardWall = (-smoothedWallNormal - moveAlongWall * 0.4f).normalized;

        TryCornerAheadProbes(aheadOrigin, towardWall, wallUp, wallRight, insideCorner: false, ref bestScore, ref bestHit);
    }

    /// <summary>
    /// Probes perpendicular faces, inward fans, and bisector rays to wrap concave (inside) corners.
    /// </summary>
    void ProbeInsideCornerWrap(
        Vector3 wallUp,
        Vector3 wallRight,
        Vector3 moveAlongWall,
        ref float bestScore,
        ref RaycastHit bestHit)
    {
        // Perpendicular casts at increasing depth to find the adjacent inside face.
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

        // Inward fan from current normal toward the corner bisector for concave wrap hits.
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

        // Ahead and bisector samples deep in the concave pocket for a stable inside-face grip.
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

    /// <summary>
    /// True when the hit is a climbable face on the far side of a concave corner relative to strafe direction.
    /// </summary>
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

    /// <summary>
    /// Scores an inside-corner face by angle from current normal, cling distance, and facing toward the player.
    /// </summary>
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

        // Weight angle, cling distance, and facing for inside-corner face selection.
        float angleScore = Mathf.Clamp01((angleFromCurrent - 30f) / 60f);
        float distScore = 1f - distError / 0.95f;
        float facingScore = Mathf.Clamp01(facing / 0.25f);
        score = angleScore * 0.45f + distScore * 0.35f + facingScore * 0.2f;
        return score > 0.1f;
    }

    /// <summary>
    /// Multi-height probes from a point past the corner toward the next climbable face.
    /// </summary>
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

    /// <summary>
    /// Resolves strafe direction from A/D, or an auto-detected approach vector for inside corners while climbing up.
    /// </summary>
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

    /// <summary>
    /// When moving up without strafe input, finds the nearest perpendicular inside face to approach.
    /// </summary>
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

    /// <summary>
    /// Labels a face change as inside or outside based on whether the new normal faces into the strafe direction.
    /// </summary>
    static CornerWrapKind ClassifyCornerWrap(Vector3 previousNormal, Vector3 newNormal, Vector3 moveAlongWall)
    {
        if (Vector3.Dot(newNormal, moveAlongWall) < -0.12f)
            return CornerWrapKind.Inside;

        return CornerWrapKind.Outside;
    }

    /// <summary>
    /// Spherecasts toward the wall and accepts only hits on colliders tagged as climbable.
    /// </summary>
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

    /// <summary>
    /// SphereCastAll along a direction and returns the best-scoring valid inside-corner adjacent hit.
    /// </summary>
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

    /// <summary>
    /// True when the surface normal is within maxWallTiltFromVertical of a vertical wall.
    /// </summary>
    bool IsValidClimbableWall(Vector3 normal)
    {
        float tiltFromVertical = Mathf.Abs(Vector3.Angle(normal, Vector3.up) - 90f);
        return tiltFromVertical <= maxWallTiltFromVertical;
    }

    /// <summary>
    /// Scores a wall hit by lateral offset along the face, cling distance error, and facing toward the chest.
    /// </summary>
    bool ScoreWallHit(RaycastHit hit, Vector3 wallUp, Vector3 wallRight, out float score, float lateralLimitScale = 1f)
    {
        score = 0f;
        if (!IsValidClimbableWall(hit.normal))
            return false;

        Vector3 chest = transform.position + Vector3.up * 1.0f;

        // Reject hits too far sideways, too deep/shallow, or facing away from the player.
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

        // Combined lateral, distance, and facing score for forward wall contact.
        float lateralScore = 1f - lateral / lateralLimit;
        float distScore = 1f - distError / 0.38f;
        float facingScore = Mathf.Clamp01(facing / 0.35f);
        score = lateralScore * 0.35f + distScore * 0.4f + facingScore * 0.25f;
        return score > 0.12f;
    }

    /// <summary>
    /// True when nothing blocks the path from chest height to the target cling offset on the new face.
    /// </summary>
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
            // Concave wrap may pass through the other climbable face at the corner.
            if (block.collider != null && block.collider.CompareTag(climbableTag))
                return true;

            return false;
        }

        if (block.collider != null && block.collider.CompareTag(climbableTag))
            return true;

        return false;
    }

    /// <summary>
    /// Blends wall normal, applies corner arc nudges, and pulls the player to the correct cling offset.
    /// </summary>
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

        // Blend attach normal faster but capped while rounding a corner.
        float blendT = 1f - Mathf.Exp(-wallNormalBlendSpeed * Time.deltaTime);
        if (roundingCorner)
            blendT = Mathf.Min(blendT * (insideTransition ? 1.6f : 1.35f), insideTransition ? 0.28f : 0.22f);

        smoothedWallNormal = Vector3.Slerp(smoothedWallNormal, hit.normal, blendT).normalized;
        wallNormal = smoothedWallNormal;

        // Subtle arc motion along outside tangents or into inside corners while strafing.
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

        // Pull toward cling distance along the face normal (reduced while topping out upward).
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

    /// <summary>
    /// Updates wallClingYawCenter (and snap rotation when not smooth) to face away from the wall.
    /// </summary>
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

    /// <summary>
    /// Accumulates offSurfaceTimer and detaches when air grace expires without re-acquiring a face.
    /// </summary>
    void HandleWallSurfaceLost(Vector2 input)
    {
        offSurfaceTimer += Time.deltaTime;

        if (offSurfaceTimer >= wallClingAirDetachDelay)
            DetachFromWall(jumpOff: false);
    }

    /// <summary>
    /// Drains sprint stamina while moving on the wall and detaches when stamina is empty.
    /// </summary>
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

    /// <summary>
    /// Releases wall cling, restores movement, and optionally applies a jump-off push.
    /// </summary>
    void DetachFromWall(bool jumpOff)
    {
        isWallClinging = false;
        currentClingCollider = null;
        offSurfaceTimer = 0f;
        topOutTimer = 0f;
        clingCooldownTimer = wallClingReattachCooldown;

        if (jumpOff)
            controller.Move(smoothedWallNormal * wallClingJumpOffPush);

        if (movement != null)
        {
            movement.ResetVerticalVelocity();
            movement.enabled = true;
        }
    }

    /// <summary>
    /// Reads WASD as a -1..1 move vector for wall-cling input (keyboard fallback).
    /// </summary>
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
