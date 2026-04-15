using UnityEngine;
using UnityEngine.InputSystem;

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

    void Start()
    {
        controller = GetComponent<CharacterController>();
        sprintCharge = maxSprintCharge;
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

        bool isSprinting = wantsToSprint && canStartSprint && isGrounded;

        float currentSpeed = moveSpeed * (isSprinting ? sprintMultiplier : 1f);

        // --------------------
        // CHARGE DRAIN / REGEN
        // --------------------
        if (isSprinting)
        {
            sprintCharge -= sprintDrainRate * Time.deltaTime;
            // Debug log for sprint charge and states
            UnityEngine.Debug.Log(
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
        if (Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
        {
            velocity.y = jumpHeight * Mathf.Sqrt(-gravity);
            UnityEngine.Debug.Log("Jump triggered");
        }

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
}
