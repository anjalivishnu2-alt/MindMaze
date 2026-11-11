using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

// Shared VR state, read by the game scripts. Everything is false/zero on a normal monitor.
public static class MMVR
{
    public static bool Headset { get { return XRSettings.isDeviceActive; } }
    public static bool Preview;                                   // F9: look at the VR screen on a monitor
    public static bool WorldUI { get { return Headset || Preview; } }

    public static bool Up, Down, Left, Right, Confirm, Back, MapBtn, BreatheHeld, ClickPending, CursorValid, TriggerHeld;
    public static Vector2 Move, Turn, Cursor;
}

// VR rig: head tracking, thumbstick input, controller pointer, and the floating screen that shows
// the whole IMGUI interface (menus, HUD, map) in the world instead of on a flat overlay.
[DefaultExecutionOrder(-200)]
public class MMVRRig : MonoBehaviour
{
    public static MMVRRig I;
    public bool menuMode = true;

    Camera cam;
    Transform handR;
    GameObject quad;
    Material mat;
    LineRenderer line;
    RenderTexture rt, prevActive;
    InputAction moveA, turnA, trigR, gripL, gripR, btnA, btnB, btnX, btnMenu;
    Vector2 prevMove;
    Vector3 smoothFwd = Vector3.forward, targetFwd = Vector3.forward;
    bool prevMenu, placed;

    public static void Boot()
    {
        if (I != null) return;
        var go = new GameObject("MMVRRig");
        I = go.AddComponent<MMVRRig>();
    }

    void Awake()
    {
        I = this;
        cam = Camera.main;

        moveA = Make("move", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
        turnA = Make("turn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}");
        trigR = Make("trigR", InputActionType.Button, "<XRController>{RightHand}/{TriggerButton}");
        gripL = Make("gripL", InputActionType.Button, "<XRController>{LeftHand}/{GripButton}");
        gripR = Make("gripR", InputActionType.Button, "<XRController>{RightHand}/{GripButton}");
        btnA = Make("a", InputActionType.Button, "<XRController>{RightHand}/{PrimaryButton}");
        btnB = Make("b", InputActionType.Button, "<XRController>{RightHand}/{SecondaryButton}");
        btnX = Make("x", InputActionType.Button, "<XRController>{LeftHand}/{PrimaryButton}");
        btnMenu = Make("menu", InputActionType.Button, "<XRController>{LeftHand}/{MenuButton}");

        if (Application.isPlaying && cam != null)
        {
            // head: follows the headset whenever one is active
            var head = cam.GetComponent<TrackedPoseDriver>();
            if (head == null) head = cam.gameObject.AddComponent<TrackedPoseDriver>();
            var hp = new InputAction("hp", InputActionType.Value, "<XRHMD>/centerEyePosition");
            hp.AddBinding("<XRHMD>/devicePosition");
            var hr = new InputAction("hr", InputActionType.Value, "<XRHMD>/centerEyeRotation");
            hr.AddBinding("<XRHMD>/deviceRotation");
            head.positionInput = new InputActionProperty(hp);
            head.rotationInput = new InputActionProperty(hr);
            hp.Enable(); hr.Enable();
            head.enabled = MMVR.Headset;
            headDriver = head;

            // right hand pointer
            var player = cam.transform.parent != null ? cam.transform.parent : transform;
            handR = new GameObject("MMHandR").transform;
            handR.SetParent(player, false);
            var drv = handR.gameObject.AddComponent<TrackedPoseDriver>();
            var pp = new InputAction("pp", InputActionType.Value, "<XRController>{RightHand}/pointerPosition");
            pp.AddBinding("<XRController>{RightHand}/devicePosition");
            var pr = new InputAction("pr", InputActionType.Value, "<XRController>{RightHand}/pointerRotation");
            pr.AddBinding("<XRController>{RightHand}/deviceRotation");
            drv.positionInput = new InputActionProperty(pp);
            drv.rotationInput = new InputActionProperty(pr);
            pp.Enable(); pr.Enable();
        }

        var sh = Resources.Load<Shader>("MMUIQuad");
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "MMVRUI";
        Destroy(quad.GetComponent<Collider>());
        var mr = quad.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mat = new Material(sh);
        mr.sharedMaterial = mat;
        quad.SetActive(false);

        var lgo = new GameObject("MMPointer");
        lgo.transform.SetParent(transform, false);
        line = lgo.AddComponent<LineRenderer>();
        line.positionCount = 2; line.startWidth = 0.006f; line.endWidth = 0.002f; line.useWorldSpace = true;
        var lm = new Material(sh); lm.mainTexture = Texture2D.whiteTexture; lm.SetFloat("_Alpha", 0.7f);
        line.sharedMaterial = lm;
        line.enabled = false;
    }

    TrackedPoseDriver headDriver;

    static InputAction Make(string n, InputActionType t, string path)
    {
        var a = new InputAction(n, t, path);
        a.Enable();
        return a;
    }

    void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (I == this) I = null;
        MMVR.Preview = false;
        MMVR.Up = MMVR.Down = MMVR.Left = MMVR.Right = MMVR.Confirm = MMVR.Back = MMVR.MapBtn = MMVR.BreatheHeld = MMVR.ClickPending = MMVR.CursorValid = false;
        MMVR.Move = MMVR.Turn = Vector2.zero;
    }

    void Update()
    {
        bool xr = MMVR.Headset;
        if (!xr && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) MMVR.Preview = !MMVR.Preview;
        if (headDriver != null && headDriver.enabled != xr) headDriver.enabled = xr;

        Vector2 mv = xr ? moveA.ReadValue<Vector2>() : Vector2.zero;
        Vector2 tn = xr ? turnA.ReadValue<Vector2>() : Vector2.zero;
        MMVR.Move = mv; MMVR.Turn = tn;
        MMVR.Up = xr && mv.y > 0.7f && prevMove.y <= 0.7f;
        MMVR.Down = xr && mv.y < -0.7f && prevMove.y >= -0.7f;
        MMVR.Left = xr && mv.x < -0.7f && prevMove.x >= -0.7f;
        MMVR.Right = xr && mv.x > 0.7f && prevMove.x <= 0.7f;
        prevMove = mv;
        MMVR.Confirm = xr && btnA.WasPressedThisFrame();
        MMVR.Back = xr && (btnB.WasPressedThisFrame() || btnMenu.WasPressedThisFrame());
        MMVR.MapBtn = xr && btnX.WasPressedThisFrame();
        MMVR.BreatheHeld = xr && (gripL.IsPressed() || gripR.IsPressed());
        MMVR.TriggerHeld = (xr && trigR.IsPressed()) || (!xr && MMVR.Preview && Mouse.current != null && Mouse.current.leftButton.isPressed);

        // pointer: right controller in a headset, the mouse in the desktop preview
        bool have = false; Ray ray = new Ray();
        bool click = false;
        if (xr && handR != null && handR.localPosition.sqrMagnitude > 0.0001f)
        { ray = new Ray(handR.position, handR.forward); have = true; click = trigR.WasPressedThisFrame(); }
        else if (!xr && MMVR.Preview && Mouse.current != null && cam != null)
        { ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue()); have = true; click = Mouse.current.leftButton.wasPressedThisFrame; }

        MMVR.CursorValid = false;
        Vector3 end = have ? ray.origin + ray.direction * 3f : Vector3.zero;
        if (have && quad != null && quad.activeSelf && rt != null)
        {
            var pl = new Plane(-quad.transform.forward, quad.transform.position);
            float d;
            if (pl.Raycast(ray, out d))
            {
                Vector3 hit = ray.GetPoint(d);
                Vector3 l = quad.transform.InverseTransformPoint(hit);
                if (Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f)
                {
                    MMVR.Cursor = new Vector2((l.x + 0.5f) * rt.width, (0.5f - l.y) * rt.height);
                    MMVR.CursorValid = true;
                    end = hit;
                }
            }
        }
        if (click && MMVR.CursorValid) MMVR.ClickPending = true;
        line.enabled = xr && have;
        if (line.enabled) { line.SetPosition(0, ray.origin); line.SetPosition(1, end); }
    }

    void LateUpdate()
    {
        bool show = MMVR.WorldUI;
        if (quad.activeSelf != show) quad.SetActive(show);
        if (!show || cam == null) return;
        EnsureRT();

        float h = menuMode ? 1.5f : 1.2f, dist = menuMode ? 2.0f : 1.5f;
        float w = h * rt.width / rt.height;
        Transform c = cam.transform;
        Vector3 fwd = Quaternion.Euler(0f, c.eulerAngles.y, 0f) * Vector3.forward;
        if (!placed || menuMode != prevMenu) { smoothFwd = targetFwd = fwd; placed = true; }
        else if (Vector3.Angle(targetFwd, fwd) > 30f) targetFwd = fwd;
        smoothFwd = Vector3.Slerp(smoothFwd, targetFwd, 1f - Mathf.Exp(-3.5f * Time.unscaledDeltaTime));
        prevMenu = menuMode;

        Vector3 pos = c.position + smoothFwd * dist + Vector3.up * (menuMode ? -0.05f : -0.1f);
        quad.transform.position = pos;
        quad.transform.rotation = Quaternion.LookRotation(pos - c.position, Vector3.up);
        quad.transform.localScale = new Vector3(w, h, 1f);
    }

    void EnsureRT()
    {
        int w = Mathf.Max(256, Screen.width), h = Mathf.Max(144, Screen.height);
        if (rt != null && rt.width == w && rt.height == h) return;
        if (rt != null) { rt.Release(); Destroy(rt); }
        rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "MindMazeUI", antiAliasing = 1 };
        rt.Create();
        mat.SetTexture("_MainTex", rt);
    }

    // called from OnGUI: everything drawn between Begin and End lands on the floating screen
    public void BeginUI()
    {
        EnsureRT();
        prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
    }

    public void EndUI() { RenderTexture.active = prevActive; }

    public void DrawCursor()
    {
        if (!MMVR.CursorValid) return;
        float s = MindMazeUI.S, r = 11f * s;
        var c = MMVR.Cursor;
        MindMazeUI.Tex(new Rect(c.x - r * 1.8f, c.y - r * 1.8f, r * 3.6f, r * 3.6f), MindMazeUI.Glow, MindMazeUI.A(MMStyle.Violet, 0.8f));
        MindMazeUI.Tex(new Rect(c.x - r * 0.6f, c.y - r * 0.6f, r * 1.2f, r * 1.2f), MMStyle.Dot, Color.white);
    }
}
