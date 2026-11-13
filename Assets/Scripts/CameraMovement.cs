using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;       // the CameraTarget object
    [SerializeField] private Vector3 targetOffset = Vector3.zero;

    [Header("Orbit")]
    [SerializeField] private float distance = 4f;
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;
    [SerializeField] private float rotationSmoothing = 0.05f;

    [Header("Collision")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.25f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursor = true;

    private float yaw;
    private float pitch = 15f;
    private float smoothYaw, smoothPitch;
    private float yawVel, pitchVel;

    private void Start()
    {
        yaw = smoothYaw = transform.eulerAngles.y;
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        // Click in the Game view to capture the mouse, Escape to release it
        if (Mouse.current.leftButton.wasPressedThisFrame && lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Always read the mouse (no lock check)
        Vector2 delta = Mouse.current.delta.ReadValue();
        yaw += delta.x * sensitivity;
        pitch -= delta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawVel, rotationSmoothing);
        smoothPitch = Mathf.SmoothDampAngle(smoothPitch, pitch, ref pitchVel, rotationSmoothing);

        Quaternion rot = Quaternion.Euler(smoothPitch, smoothYaw, 0f);
        Vector3 focus = target.position + targetOffset;

        // Pull the camera in if something is between it and the character
        float dist = distance;
        Vector3 dir = rot * Vector3.back;
        if (Physics.SphereCast(focus, collisionRadius, dir, out RaycastHit hit,
                               distance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(hit.distance, 0.5f);
        }

        transform.rotation = rot;
        transform.position = focus + dir * dist;
    }
}