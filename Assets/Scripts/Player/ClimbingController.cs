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
        // Get references to required components
        controller = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();

        // Get the move action from Input System
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            moveAction = playerInput.actions["Move"];
        }
    }

    void Update()
    {
        // If currently climbing, perform the climb movement
        if (isClimbing)
        {
            PerformClimb();
            return;
        }

        // Only auto-mantle when not on ground and not crouching
        if (!controller.isGrounded && !movement.getIsCrouching())
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

        // Multiple detection points at different heights, offset forward slightly
        Vector3 lowOrigin = transform.position + Vector3.up * lowCheckHeight + forward * startOffset;
        Vector3 midOrigin = transform.position + Vector3.up * 1.0f + forward * startOffset;
        Vector3 upperOrigin = transform.position + Vector3.up * upCheckHeight + forward * startOffset;

        
        /* Check if wall is in front */
        float sphereRadius = 0.2f;
        RaycastHit lowHit, midHit;

        bool wallInFrontLow = Physics.SphereCast(
            lowOrigin,
            sphereRadius,
            forward,
            out lowHit,
            wallCheckDistance
        );

        bool wallInFrontMid = Physics.SphereCast(
            midOrigin,
            sphereRadius,
            forward,
            out midHit,
            wallCheckDistance
        );

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

        /* Check if there's a wall above */
        bool wallAbove = Physics.SphereCast(
            upperOrigin,
            sphereRadius,
            forward,
            out RaycastHit aboveHit,
            wallCheckDistance * 0.9f
        );

        /* DEBUG OUTPUT (REMOVE LATER) */
        if (wallInFront)
        {
            Debug.Log($"Wall detected at distance: {wallHit.distance:F2}m, height: {wallHit.point.y:F2}m");
        }
        else
        {
            Debug.Log("No wall detected");
        }

        // Draw sphere casts for visual debugging
        Debug.DrawRay(lowOrigin, forward * wallCheckDistance, wallInFrontLow ? Color.green : Color.red);
        Debug.DrawRay(midOrigin, forward * wallCheckDistance, wallInFrontMid ? Color.yellow : Color.red);
        Debug.DrawRay(upperOrigin, forward * wallCheckDistance, wallAbove ? Color.blue : Color.gray);

        /* Check conditions for mantling */
        // If no wall in front, can't mantle
        if (!wallInFront)
            return;

        // Don't mantle if there's a wall above (too tall)
        if (wallAbove)
        {
            Debug.Log("Wall continues above - too tall to mantle");
            return;
        }

        // Check if the wall is low enough to mantle over
        float wallHeight = wallHit.point.y - transform.position.y;


        /* DEBUG OUTPUT (REMOVE LATER) */
        if (wallHeight > upCheckHeight + 0.5f)
        {
            Debug.Log($"Wall too high: {wallHeight:F2}m");
            return;
        }

        if (wallHeight < 0.3f)
        {
            Debug.Log($"Wall too low: {wallHeight:F2}m");
            return;
        }

        /* Find top of the wall */
        // Create a raycast that starts above the wall hit point and goes down to find the top surface of the wall
        RaycastHit topHit = new RaycastHit();
        bool foundSurface = false;

        // Start scanning from above the wall hit point
        float scanStartY = Mathf.Max(wallHit.point.y + 0.3f, transform.position.y + 0.5f);

        for (float offset = 0.3f; offset <= 1.8f; offset += 0.3f)
        {
            Vector3 scanOrigin = new Vector3(wallHit.point.x, scanStartY + offset, wallHit.point.z) + forward * 0.2f;

            if (Physics.Raycast(scanOrigin, Vector3.down, out topHit, 2.0f))
            {
                // Make sure we found the top of the wall, not something else
                if (Mathf.Abs(topHit.point.x - wallHit.point.x) < 0.5f &&
                    Mathf.Abs(topHit.point.z - wallHit.point.z) < 0.5f)
                {
                    foundSurface = true;
                    Debug.Log($"Found surface at offset {offset}");
                    break;
                }
            }
        }

        if (!foundSurface)
        {
            Debug.Log("No top surface found");
            return;
        }

        // Check if the surface is at a reasonable height to climb onto
        float surfaceHeight = topHit.point.y - transform.position.y;

        /* DEBUG OUTPUT (REMOVE LATER) */
        if (surfaceHeight < 0.4f)
        {
            Debug.Log($"Surface too low: {surfaceHeight:F2}m");
            return;
        }

        if (surfaceHeight > 1.8f)
        {
            Debug.Log($"Surface too high: {surfaceHeight:F2}m");
            return;
        }

        /* Final target calculation */
        climbTarget = topHit.point + Vector3.up * 0.2f + forward;

        // Make sure the target position has clearance
        Vector3 clearanceCheck = climbTarget + Vector3.up;
        if (Physics.CheckSphere(clearanceCheck, 0.4f))
        {
            /* DEBUG OUTPUT (REMOVE LATER) */
            Debug.Log("No head clearance at target");
            return;
        }

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.DrawLine(wallHit.point, topHit.point, Color.blue);
        Debug.Log($"CLIMB TRIGGERED - Wall height: {wallHeight:F2}m, Surface height: {surfaceHeight:F2}m");

        StartClimb();
    }

    void StartClimb()
    {
        isClimbing = true;
        climbStartPosition = transform.position;

        // Calculate the upward target - climb HIGHER before moving forward
        Vector3 forward = transform.forward;
        // The top edge of the wall
        Vector3 wallTop = climbTarget - forward;

        // Increased upward height by adding extra clearance (was 0.5f, now 0.8f)
        climbMidPoint = new Vector3(climbStartPosition.x, wallTop.y + clearanceHeight, climbStartPosition.z);

        if (movement != null)
            movement.enabled = false;

        controller.enabled = false;

        Debug.Log("AUTO MANTLE STARTED - Phase 0: Upward");
    }

    void PerformClimb()
    {
            // Move straight UP to clear the wall
            Vector3 upwardTarget = new Vector3(climbStartPosition.x, climbMidPoint.y, climbStartPosition.z);
            transform.position = Vector3.MoveTowards(
                transform.position,
                // Add clearance height to target position
                upwardTarget + clearanceHeight * Vector3.up,
                climbSpeed * Time.deltaTime
            );

            // Once we reach the upward target, switch to moving forward
            if (Vector3.Distance(transform.position, upwardTarget) < 0.05f)
            {
                FinishClimb();
            }
    }

    void FinishClimb()
    {
        // Set isClimbing to false before enabling the controller
        isClimbing = false;
        controller.enabled = true;

        if (movement != null)
            movement.ResetVerticalVelocity();
            movement.enabled = true;

        /* DEBUG OUTPUT (REMOVE LATER) */
        Debug.Log("MANTLE COMPLETE");
    }

}