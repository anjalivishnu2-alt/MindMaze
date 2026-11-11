using UnityEngine;

// Hand-drawn "speed lines" behind a falling block (like the film), plus a scribbled
// pencil ring on the floor that tightens where the block is about to land.
public class SketchTrail : MonoBehaviour
{
    static Material lineMat;
    public int lineCount = 6;
    public static bool ShowLandingRing = false;   // pencil ring on the floor before impact (off)

    Transform[] anchors;
    Vector3[] baseLocal;
    TrailRenderer[] trails;
    Rigidbody rb;
    LineRenderer ring;
    Vector3 landPoint;
    bool hasLand;
    float ringStart;
    float fallTime = 1.5f;

    public static void SetMaterial(Material m) { if (m != null) lineMat = m; }

    public static Material LineMaterial()
    {
        if (lineMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            lineMat = new Material(sh);
            lineMat.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.95f, 1f));
        }
        return lineMat;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        anchors = new Transform[lineCount];
        baseLocal = new Vector3[lineCount];
        trails = new TrailRenderer[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            var a = new GameObject("sketch").transform;
            a.SetParent(transform, false);
            // spread across the block's top edges / corners
            baseLocal[i] = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
            baseLocal[i][Random.Range(0, 3)] = Random.value < 0.5f ? -0.5f : 0.5f;
            a.localPosition = baseLocal[i];
            var tr = a.gameObject.AddComponent<TrailRenderer>();
            tr.sharedMaterial = LineMaterial();
            tr.time = Random.Range(0.25f, 0.6f);
            tr.minVertexDistance = 0.15f;
            float w = Random.Range(0.006f, 0.018f);
            tr.widthCurve = new AnimationCurve(new Keyframe(0, w), new Keyframe(1, 0));
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            anchors[i] = a;
            trails[i] = tr;
        }

        // predict the landing spot and draw a pencil ring there
        RaycastHit hit;
        if (ShowLandingRing && Physics.Raycast(transform.position, Vector3.down, out hit, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            landPoint = hit.point + Vector3.up * 0.03f; hasLand = true;
            float v = rb ? Mathf.Abs(rb.linearVelocity.y) : 5f;
            float h = transform.position.y - hit.point.y;
            // h = v t + 0.5 g t^2  -> time to land
            float g = Mathf.Abs(Physics.gravity.y);
            fallTime = (-v + Mathf.Sqrt(v * v + 2f * g * h)) / g;
            var rg = new GameObject("landing_ring");
            ring = rg.AddComponent<LineRenderer>();
            ring.sharedMaterial = LineMaterial();
            ring.loop = true; ring.positionCount = 40;
            ring.widthMultiplier = 0.02f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringStart = Time.time;
        }
    }

    void Update()
    {
        bool falling = rb != null && rb.linearVelocity.y < -1.5f;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i] == null) continue;
            // jitter the line origins a little each frame -> sketchy, hand-drawn lines
            anchors[i].localPosition = baseLocal[i] + Random.insideUnitSphere * 0.04f;
            trails[i].emitting = falling;
        }

        if (ring != null)
        {
            float k = Mathf.Clamp01((Time.time - ringStart) / Mathf.Max(0.3f, fallTime));
            float radius = Mathf.Lerp(2.2f, 0.9f, k);
            for (int i = 0; i < ring.positionCount; i++)
            {
                float a = i / (float)ring.positionCount * Mathf.PI * 2f + Time.time * 0.5f;
                float r = radius * (1f + (Mathf.PerlinNoise(i * 0.4f, Time.time * 6f) - 0.5f) * 0.18f);
                ring.SetPosition(i, landPoint + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            ring.widthMultiplier = Mathf.Lerp(0.012f, 0.03f, k);
            if (k >= 1f || !falling && Time.time - ringStart > 0.5f) { Destroy(ring.gameObject); ring = null; }
        }
    }

    void OnDestroy() { if (ring != null) Destroy(ring.gameObject); }
}
