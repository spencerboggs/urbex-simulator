using System.Collections.Specialized;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(CharacterController))]
public class ClimbingController : MonoBehaviour
{
    [Header("Detection")]
    public float wallCheckDistance = 0.6f;
    public float upCheckHeight = 1.3f;
    public float lowCheckHeight = 0.5f;
    public float skinWidth = 0.1f;

    [Header("Climb Offsets")]
    public float forwardOffset = -0.8f;
    public float heightOffset = 1.2f;
    public float clearanceHeight = 1.2f;

    [Header("Climb")]
    public float climbSpeed = 4f;

    private CharacterController controller;
    private PlayerMovement movement;
    private InputAction moveAction;

    private bool isClimbing = false;
    private Vector3 climbTarget;
    private Vector3 climbStartPosition;
    private Vector3 climbMidPoint;
    private int climbPhase = 0; // 0 = upward, 1 = forward

    void Start()
    {
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
        if (isClimbing)
        {
            PerformClimb();
            return;
        }

        // Only auto-mantle when not on ground AND pressing forward (W)
        if (!controller.isGrounded && !movement.getIsCrouching())
        {
            DetectLedgeAndAutoClimb();
        }
    }


    void DetectLedgeAndAutoClimb()
    {
        Vector3 forward = transform.forward;

        // Start rays from slightly in front of the character to avoid starting inside walls
        float startOffset = skinWidth;

        // Multiple detection points at different heights, offset forward slightly
        Vector3 lowOrigin = transform.position + Vector3.up * lowCheckHeight + forward * startOffset;
        Vector3 midOrigin = transform.position + Vector3.up * 1.0f + forward * startOffset;
        Vector3 upperOrigin = transform.position + Vector3.up * upCheckHeight + forward * startOffset;

        // ----------------------------
        // WALL IN FRONT CHECK using SphereCast for better reliability
        // ----------------------------
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

        // ----------------------------
        // WALL ABOVE CHECK
        // ----------------------------
        bool wallAbove = Physics.SphereCast(
            upperOrigin,
            sphereRadius,
            forward,
            out RaycastHit aboveHit,
            wallCheckDistance * 0.9f
        );

        // ----------------------------
        // DEBUG OUTPUT
        // ----------------------------
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

        // ----------------------------
        // REQUIREMENTS TO CLIMB
        // ----------------------------
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

        // ----------------------------
        // FIND TOP SURFACE
        // ----------------------------
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

        // ----------------------------
        // FINAL TARGET - Position on top of the ledge
        // ----------------------------
        climbTarget = topHit.point + Vector3.up * 0.2f + forward * forwardOffset;

        // Make sure the target position has clearance
        Vector3 clearanceCheck = climbTarget + Vector3.up;
        if (Physics.CheckSphere(clearanceCheck, 0.4f))
        {
            Debug.Log("No head clearance at target");
            return;
        }

        Debug.DrawLine(wallHit.point, topHit.point, Color.blue);
        Debug.Log($"CLIMB TRIGGERED - Wall height: {wallHeight:F2}m, Surface height: {surfaceHeight:F2}m");

        StartClimb();
    }

    void StartClimb()
    {
        isClimbing = true;
        climbPhase = 0; // Start with upward phase
        climbStartPosition = transform.position;

        // Calculate the upward target - climb HIGHER before moving forward
        Vector3 forward = transform.forward;
        Vector3 wallTop = climbTarget - forward * forwardOffset; // The top edge of the wall

        // Increased upward height by adding extra clearance (was 0.5f, now 0.8f)
        climbMidPoint = new Vector3(climbStartPosition.x, wallTop.y + clearanceHeight, climbStartPosition.z);

        if (movement != null)
            movement.enabled = false;

        controller.enabled = false;

        Debug.Log("AUTO MANTLE STARTED - Phase 0: Upward");
    }

    void PerformClimb()
    {
        if (climbPhase == 0)
        {
            // PHASE 1: Move straight UP to clear the wall
            Vector3 upwardTarget = new Vector3(climbStartPosition.x, climbMidPoint.y, climbStartPosition.z);
            transform.position = Vector3.MoveTowards(
                transform.position,
                upwardTarget,
                climbSpeed * Time.deltaTime
            );

            if (Vector3.Distance(transform.position, upwardTarget) < 0.05f)
            {
                climbPhase = 1;
                Debug.Log("MANTLE PHASE 1 COMPLETE - Phase 2: Forward");
            }
        }
        else if (climbPhase == 1)
        {
            Vector3 target = new Vector3(
                climbTarget.x,
                climbTarget.y + clearanceHeight,
                climbTarget.z
            );

            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                climbSpeed * Time.deltaTime
            );

            if (Vector3.Distance(transform.position, target) < 0.05f)
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

        Debug.Log("MANTLE COMPLETE");
    }

}