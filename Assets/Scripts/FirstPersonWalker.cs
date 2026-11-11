using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Simple first-person controller: WASD / arrows to move, mouse to look,
// Shift to sprint, Space to jump, Esc to free the cursor, click to recapture.
[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalker : MonoBehaviour
{
    public Transform cameraPivot;
    public float walkSpeed = 3.5f;
    public float sprintSpeed = 6.5f;
    public float jumpHeight = 1.0f;
    public float gravity = -20f;
    public float mouseSensitivity = 0.12f;
    public bool showHelp = true;

    CharacterController controller;
    float pitch;
    float verticalVelocity;
    float helpTimer = 8f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
        LockCursor(true);
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Update()
    {
        Vector2 move = Vector2.zero;
        Vector2 look = Vector2.zero;
        bool sprint = false, jump = false, esc = false, click = false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
            sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            jump = kb.spaceKey.wasPressedThisFrame;
            esc = kb.escapeKey.wasPressedThisFrame;
        }
        if (mouse != null)
        {
            look = mouse.delta.ReadValue() * mouseSensitivity;
            click = mouse.leftButton.wasPressedThisFrame;
        }
#else
        move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
        sprint = Input.GetKey(KeyCode.LeftShift);
        jump = Input.GetKeyDown(KeyCode.Space);
        esc = Input.GetKeyDown(KeyCode.Escape);
        click = Input.GetMouseButtonDown(0);
#endif

        if (esc) LockCursor(false);
        if (click && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        if (move.sqrMagnitude > 1f) move.Normalize();
        float speed = sprint ? sprintSpeed : walkSpeed;
        Vector3 horizontal = (transform.right * move.x + transform.forward * move.y) * speed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (jump) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;

        controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

        if (helpTimer > 0f) helpTimer -= Time.deltaTime;
    }

    void OnGUI()
    {
        if (!showHelp) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        style.normal.textColor = Color.white;
        if (helpTimer > 0f || Cursor.lockState != CursorLockMode.Locked)
        {
            string msg = Cursor.lockState == CursorLockMode.Locked
                ? "WASD move  •  Mouse look  •  Shift sprint  •  Space jump  •  Esc free cursor"
                : "Click to continue exploring";
            var size = style.CalcSize(new GUIContent(msg));
            var rect = new Rect((Screen.width - size.x) / 2f - 12, Screen.height - 60, size.x + 24, size.y + 12);
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 12, rect.y + 6, size.x, size.y), msg, style);
        }
    }
}
