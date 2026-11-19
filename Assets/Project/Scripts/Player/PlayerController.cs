using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using Game.Core;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour {
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float rotationSmoothTime = 0.1f;
    [SerializeField] private float acceleration = 10f;

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedGravity = -2f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.1f;
    [SerializeField] private LayerMask groundMask;

    [Header("References")]
    [Tooltip("Обычно сюда перетаскивается transform камеры/её пивота (например, объект с CameraController)")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController controller;
    private PlayerControls controls;
    private Vector2 moveInput;
    private bool sprintHeld;
    private bool jumpPressed;
    private Vector3 velocity;
    private float currentSpeed;
    private float rotationVelocity;
    private bool isGrounded;
    private Vector3 lastMoveDirection = Vector3.forward;
    private GameManager _gameManager;

    [Inject]
    public void Construct(GameManager gameManager) {
        _gameManager = gameManager;
        Debug.Log("PlayerController: GameManager получен, фаза = " + _gameManager.Phase);
    }

    private void Awake() {
        controller = GetComponent<CharacterController>();
        controls = new PlayerControls();
        controls.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Player.Move.canceled += ctx => moveInput = Vector2.zero;
        controls.Player.Sprint.performed += ctx => sprintHeld = true;
        controls.Player.Sprint.canceled += ctx => sprintHeld = false;
        controls.Player.Jump.started += ctx => jumpPressed = true;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Update() {
        CheckGround();
        HandleMovement();
        HandleJumpAndGravity();
    }

    private void CheckGround() {
        isGrounded = groundCheck != null
            ? Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundMask)
            : controller.isGrounded;
    }

    private void HandleMovement() {
        Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);
        bool hasInput = inputDir.magnitude >= 0.01f;

        float targetSpeed = sprintHeld ? sprintSpeed : walkSpeed;

        if (!hasInput) {
            currentSpeed = Mathf.Lerp(currentSpeed, 0f, acceleration * Time.deltaTime);
            if (currentSpeed < 0.01f) {
                currentSpeed = 0f;
                return;
            }
        } else {
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed * inputDir.magnitude, acceleration * Time.deltaTime);
        }
        if (hasInput) {
            float camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            float targetAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + camYaw;

            float smoothedAngle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y, targetAngle, ref rotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
            lastMoveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        }
        controller.Move(lastMoveDirection * currentSpeed * Time.deltaTime);
    }

    private void HandleJumpAndGravity() {
        if (isGrounded) {
            if (velocity.y < 0f)
                velocity.y = groundedGravity;
            if (jumpPressed)
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        jumpPressed = false;
    }
    public float NormalizedSpeed => sprintHeld ? currentSpeed / sprintSpeed : currentSpeed / walkSpeed;
    public bool IsGrounded => isGrounded;
}