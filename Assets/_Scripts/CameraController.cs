//using UnityEngine;
//using UnityEngine.InputSystem;

//public class CameraController : MonoBehaviour {
//    [Header("Target")]
//    [SerializeField] private Transform target;
//    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

//    [Header("Distance")]
//    [SerializeField] private float distance = 5f;
//    [SerializeField] private float minDistance = 1.5f;
//    [SerializeField] private float maxDistance = 8f;

//    [Header("Rotation")]
//    [SerializeField] private float mouseSensitivity = 0.75f;
//    [SerializeField] private float minPitch = -30f;
//    [SerializeField] private float maxPitch = 70f;
//    [SerializeField] private float rotationSmoothTime = 0.05f;

//    [Header("Collision")]
//    [SerializeField] private LayerMask collisionMask;
//    [SerializeField] private float collisionRadius = 0.2f;

//    private PlayerControls controls;
//    private Vector2 lookInput;

//    private float yaw;
//    private float pitch = 20f;
//    private float currentYaw;
//    private float currentPitch;
//    private float yawVelocity;
//    private float pitchVelocity;

//    private void Awake() {
//        controls = new PlayerControls();
//        controls.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
//        controls.Player.Look.canceled += ctx => lookInput = Vector2.zero;

//        yaw = transform.eulerAngles.y;
//        currentYaw = yaw;
//        currentPitch = pitch;
//    }

//    private void OnEnable() => controls.Enable();
//    private void OnDisable() => controls.Disable();

//    private void Start() {
//        Cursor.lockState = CursorLockMode.Locked;
//    }

//    private void LateUpdate() {
//        if (target == null) return;

//        yaw += lookInput.x * mouseSensitivity;
//        pitch -= lookInput.y * mouseSensitivity;
//        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

//        currentYaw = Mathf.SmoothDampAngle(currentYaw, yaw, ref yawVelocity, rotationSmoothTime);
//        currentPitch = Mathf.SmoothDampAngle(currentPitch, pitch, ref pitchVelocity, rotationSmoothTime);

//        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
//        Vector3 pivot = target.position + targetOffset;

//        float desiredDistance = distance;
//        Vector3 camDirection = -(rotation * Vector3.forward);

//        // проверка столкновений камеры со стенами/объектами, чтобы камера не проваливалась внутрь геометрии
//        if (Physics.SphereCast(pivot, collisionRadius, camDirection, out RaycastHit hit, desiredDistance, collisionMask)) {
//            desiredDistance = Mathf.Clamp(hit.distance, minDistance, maxDistance);
//        }

//        transform.position = pivot - rotation * Vector3.forward * desiredDistance;
//        transform.rotation = rotation;
//    }

//    // вызывать из скролла мыши (Mouse Scroll), если добавите такое действие в Input Actions
//    public void Zoom(float scrollDelta, float zoomSpeed = 2f) {
//        distance = Mathf.Clamp(distance - scrollDelta * zoomSpeed, minDistance, maxDistance);
//        Debug.Log(scrollDelta);
//    }
//} 