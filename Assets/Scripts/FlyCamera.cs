using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

// Simple fly camera: hold right mouse to look, WASD to move, Q/E down/up, Shift to go faster.
public class FlyCamera : MonoBehaviour
{
    public float speed = 1.5f;
    public float fastMultiplier = 3f;
    public float lookSensitivity = 3f;
    float yaw, pitch;

    void Start()
    {
        var e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        Vector3 d = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) d += transform.forward;
        if (Input.GetKey(KeyCode.S)) d -= transform.forward;
        if (Input.GetKey(KeyCode.D)) d += transform.right;
        if (Input.GetKey(KeyCode.A)) d -= transform.right;
        if (Input.GetKey(KeyCode.E)) d += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) d -= Vector3.up;
        float s = speed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
        transform.position += d.normalized * s * Time.deltaTime;
#elif ENABLE_INPUT_SYSTEM
        // Same controls through the Input System (used when the project's active input handling is the new system).
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || kb == null) return;
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue() * 0.1f; // matches the legacy "Mouse X/Y" axis scale
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        Vector3 d = Vector3.zero;
        if (kb.wKey.isPressed) d += transform.forward;
        if (kb.sKey.isPressed) d -= transform.forward;
        if (kb.dKey.isPressed) d += transform.right;
        if (kb.aKey.isPressed) d -= transform.right;
        if (kb.eKey.isPressed) d += Vector3.up;
        if (kb.qKey.isPressed) d -= Vector3.up;
        float s = speed * (kb.leftShiftKey.isPressed ? fastMultiplier : 1f);
        transform.position += d.normalized * s * Time.deltaTime;
#endif
    }
}
