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

    // Internal state
    private CharacterController controller;
    private PlayerMovement movement;
    private InputAction moveAction;

    // Climbing state variables
    private bool isClimbing = false;
    private Vector3 climbTarget;
    private Vector3 climbStartPosition;
    private Vector3 climbMidPoint;

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

        if (!controller.isGrounded && !movement.getIsCrouching() && movement.GetForwardVelocity() > 0.1f)
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
}
