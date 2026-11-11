using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Walks the player through the maze automatically (like the camera in the film),
// so in VR the viewer only has to look around. It keeps picking new far-away spots
// and follows the corridors to them using a NavMesh. Press T to pause / resume;
// WASD temporarily takes over.
public class AutoTour : MonoBehaviour
{
    public MazeWalker walker;
    public bool autoWalk = true;
    public float speed = 1.3f;
    [Tooltip("Turn the body to face the way you're walking. Keep ON for desktop, OFF in VR (the head decides where to look).")]
    public bool turnBodyWithPath = true;
    public float turnSpeed = 55f;
    public Vector2 legLength = new Vector2(25f, 70f);

    NavMeshPath path;
    int corner;
    float stuckTimer;
    Vector3 lastPos;
    Vector3 smoothedDir;

    void Start()
    {
        if (walker == null) walker = GetComponent<MazeWalker>();
        path = new NavMeshPath();
        lastPos = transform.position;
        NewLeg();
    }

void NewLeg()
    {
        corner = 0;
        NavMeshHit here;
        if (!NavMesh.SamplePosition(transform.position, out here, 4f, NavMesh.AllAreas)) { path.ClearCorners(); return; }
        Vector3 fallback = Vector3.zero; bool haveFallback = false;
        for (int i = 0; i < 80; i++)
        {
            Vector3 dir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
            // prefer going roughly forward so the tour doesn't double back
            if (i < 40 && Vector3.Dot(dir, transform.forward) < -0.2f) continue;
            Vector3 p = here.position + dir * Random.Range(legLength.x, legLength.y);
            NavMeshHit there;
            if (!NavMesh.SamplePosition(p, out there, 6f, NavMesh.AllAreas)) continue;
            var test = new NavMeshPath();
            if (!NavMesh.CalculatePath(here.position, there.position, NavMesh.AllAreas, test) || test.status != NavMeshPathStatus.PathComplete || test.corners.Length < 2) continue;
            if (!haveFallback) { fallback = there.position; haveFallback = true; }
            // only head for spots that are deep inside narrow corridors (walls close on both sides)
            if (!IsCorridor(there.position) || PathOpenness(test) > 0.35f) continue;
            path = test; corner = 1;
            return;
        }
        if (haveFallback && NavMesh.CalculatePath(here.position, fallback, NavMesh.AllAreas, path)) { corner = 1; return; }
        path.ClearCorners();
    }

    static bool IsCorridor(Vector3 p)
    {
        Vector3 o = p + Vector3.up;
        int close = 0;
        if (Physics.Raycast(o, Vector3.forward, 3.5f)) close++;
        if (Physics.Raycast(o, Vector3.back, 3.5f)) close++;
        if (Physics.Raycast(o, Vector3.left, 3.5f)) close++;
        if (Physics.Raycast(o, Vector3.right, 3.5f)) close++;
        return close >= 2;
    }

    // fraction of sample points along the path that are out in the open
    static float PathOpenness(NavMeshPath p)
    {
        int open = 0, total = 0;
        for (int i = 1; i < p.corners.Length; i++)
        {
            Vector3 a = p.corners[i - 1], b = p.corners[i];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 3f));
            for (int s = 0; s < steps; s++) { total++; if (!IsCorridor(Vector3.Lerp(a, b, s / (float)steps))) open++; }
        }
        return total == 0 ? 1f : open / (float)total;
    }

    void Update()
    {
        bool toggle = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) toggle = true;
#else
        if (Input.GetKeyDown(KeyCode.T)) toggle = true;
#endif
        if (toggle) autoWalk = !autoWalk;

        if (walker == null) return;
        if (!autoWalk || walker.userIsSteering)
        {
            walker.autoMove = Vector3.zero;
            if (walker.userIsSteering) { stuckTimer = 0f; lastPos = transform.position; }
            if (walker.userIsSteering && autoWalk) NewLegSoon();
            return;
        }
        if (path == null || path.corners.Length < 2 || corner >= path.corners.Length) { NewLeg(); walker.autoMove = Vector3.zero; return; }

        Vector3 target = path.corners[corner];
        Vector3 to = target - transform.position; to.y = 0f;
        if (to.magnitude < 0.6f)
        {
            corner++;
            if (corner >= path.corners.Length) NewLeg();
            return;
        }
        Vector3 dir = to.normalized;
        smoothedDir = Vector3.Slerp(smoothedDir.sqrMagnitude < 0.01f ? dir : smoothedDir, dir, Time.deltaTime * 2.5f);
        walker.autoMove = smoothedDir * speed;

        if (turnBodyWithPath)
        {
            Quaternion want = Quaternion.LookRotation(smoothedDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
        }

        // if something (a fallen block) blocks the way, pick a new route
        stuckTimer += Time.deltaTime;
        if (stuckTimer > 2f)
        {
            if ((transform.position - lastPos).magnitude < 0.4f) NewLeg();
            stuckTimer = 0f; lastPos = transform.position;
        }
    }

    bool pendingLeg;
    void NewLegSoon() { pendingLeg = true; }
    void LateUpdate()
    {
        if (pendingLeg && walker != null && !walker.userIsSteering) { pendingLeg = false; NewLeg(); }
    }
}
