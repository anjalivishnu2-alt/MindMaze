using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// First-person controller: WASD / arrows move, mouse look, Shift run, Space jump,
// Esc frees the cursor, click recaptures. Exposes hooks (speedMultiplier, extraRoll,
// shakeEuler, bobOffset) so the anxiety effects can disturb the camera.
[RequireComponent(typeof(CharacterController))]
public class MazeWalker : MonoBehaviour
{
    public Transform cameraPivot;
    public float eyeHeight = 1.4f;
    public float walkSpeed = 3.0f;
    public float sprintSpeed = 5.5f;
    public float jumpHeight = 1.4f;
    public float gravity = -20f;
    public float mouseSensitivity = 0.12f;
    public float turnSpeed = 95f;           // left / right arrow turning (deg/s)
    [HideInInspector] public float pitchOffset;      // used by the intro camera
    [HideInInspector] public bool inputEnabled = true;
    public bool showHelp = true;

    [HideInInspector] public float speedMultiplier = 1f;
    [HideInInspector] public float extraRoll;
    [HideInInspector] public Vector3 shakeEuler;
    [HideInInspector] public Vector3 bobOffset;
    [HideInInspector] public float currentSpeed;
    [HideInInspector] public Vector3 autoMove;      // set by AutoTour (world space, m/s)
    [HideInInspector] public bool userIsSteering;
    [HideInInspector] public bool frozen;           // e.g. while breathing or after the run ends

    CharacterController controller;
    float pitch;
    float verticalVelocity;
    float prevTurn;
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
        float turn = 0f;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null)
        {
            // arrow keys: up/down walk, left/right turn. Space is used for breathing (see AnxietyGame).
            if (kb.upArrowKey.isPressed) move.y += 1;
            if (kb.downArrowKey.isPressed) move.y -= 1;
            if (kb.rightArrowKey.isPressed) turn += 1;
            if (kb.leftArrowKey.isPressed) turn -= 1;
            sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            jump = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            esc = kb.escapeKey.wasPressedThisFrame;
        }
        if (mouse != null)
        {
            look = mouse.delta.ReadValue() * mouseSensitivity;
            click = mouse.leftButton.wasPressedThisFrame;
        }
#else
        move = new Vector2(0f, (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
        turn = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
        sprint = Input.GetKey(KeyCode.LeftShift);
        jump = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        esc = Input.GetKeyDown(KeyCode.Escape);
        click = Input.GetMouseButtonDown(0);
#endif

        if (MMVR.Headset) { move = MMVR.Move; turn = MMVR.Turn.x; look = Vector2.zero; sprint = false; }
        if (esc) LockCursor(false);
        if (click && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
        if (inputEnabled)
        {
            if (MMSettings.SnapTurn) { if (Mathf.Abs(turn) > 0.5f && Mathf.Abs(prevTurn) <= 0.5f) transform.Rotate(0f, Mathf.Sign(turn) * 30f, 0f); }
            else transform.Rotate(0f, turn * turnSpeed * Time.deltaTime, 0f);
        }
        prevTurn = turn;
        if (!inputEnabled) { move = Vector2.zero; look = Vector2.zero; }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -65f, 25f);
        }
        if (cameraPivot != null && !MMVR.Headset)
        {
            cameraPivot.localRotation = Quaternion.Euler(pitch + pitchOffset + shakeEuler.x, shakeEuler.y, extraRoll + shakeEuler.z);
            cameraPivot.localPosition = new Vector3(0f, eyeHeight, 0f) + bobOffset;
        }

        if (move.sqrMagnitude > 1f) move.Normalize();
        float speed = (sprint ? sprintSpeed : walkSpeed) * speedMultiplier;
        Vector3 hr = transform.right, hf = transform.forward;
        if (MMVR.Headset && cameraPivot != null) { var q = Quaternion.Euler(0f, cameraPivot.eulerAngles.y, 0f); hr = q * Vector3.right; hf = q * Vector3.forward; }
        Vector3 horizontal = (hr * move.x + hf * move.y) * speed;
        userIsSteering = move.sqrMagnitude > 0.01f;
        if (!userIsSteering) horizontal = autoMove * speedMultiplier;
        if (frozen) { horizontal = Vector3.zero; jump = false; }
        currentSpeed = horizontal.magnitude;

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
        style.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        if (helpTimer > 0f || Cursor.lockState != CursorLockMode.Locked)
        {
            string msg = Cursor.lockState == CursorLockMode.Locked
                ? "↑ ↓ walk  •  ← → turn  •  Enter jump  •  hold Space to breathe"
                : "Click to continue";
            var size = style.CalcSize(new GUIContent(msg));
            var rect = new Rect((Screen.width - size.x) / 2f - 12, Screen.height - 60, size.x + 24, size.y + 12);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 12, rect.y + 6, size.x, size.y), msg, style);
        }
    }
}
