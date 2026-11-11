using UnityEngine;
using System.Collections.Generic;

// Writes intrusive thoughts onto the world: scrawled on a nearby wall you're looking
// towards, or on a falling block. They tremble, fade in, linger and fade out.
// (World-space text also works in VR, unlike screen overlays.)
public class ThoughtWriter : MonoBehaviour
{
    public Camera cam;
    public static ThoughtWriter Instance;
    public int maxAtOnce = 4;
    public float life = 5f;
    public float maxDistance = 12f;
    [Tooltip("Maze/WorldText material so writing is hidden behind nearer walls.")]
    public Material textMaterial;
    Material runtimeMat;
    [Range(0, 1)] public float blockChance = 0.5f;

    class Note { public TextMesh tm; public float born, life, baseSize; public Vector3 basePos; public Transform t; }
    readonly List<Note> notes = new List<Note>();
    Font font;

    void Start()
    {
        Instance = this;
        if (cam == null) cam = Camera.main;
        maxAtOnce = Mathf.Min(maxAtOnce, 2);
        font = Resources.Load<Font>("Fonts/Montserrat-Medium");
        if (font == null) font = Resources.Load<Font>("Fonts/Inter-MediumItalic");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (textMaterial != null && font != null) { runtimeMat = new Material(textMaterial); runtimeMat.mainTexture = font.material.mainTexture; }
    }

    public Font TextFont { get { return font; } }
    public Material TextMaterial()
    {
        if (runtimeMat != null && font != null) runtimeMat.mainTexture = font.material.mainTexture;
        return runtimeMat != null ? runtimeMat : (font != null ? font.material : null);
    }

    
public bool Write(string text)
    {
        if (cam == null || font == null || notes.Count >= maxAtOnce) return false;
        Vector3 pos, normal; Transform parent = null;
        if (!FindSurface(out pos, out normal, out parent)) return false;

        var go = new GameObject("thought: " + text);
        go.transform.position = pos + normal * (parent != null ? 0.03f : 0.22f); // stand off the breathing walls
        Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? cam.transform.forward : Vector3.up;
        go.transform.rotation = Quaternion.LookRotation(-normal, up) * Quaternion.Euler(0, 0, Random.Range(-2f, 2f));
        if (parent != null) go.transform.SetParent(parent, true);

        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        float dist = Vector3.Distance(cam.transform.position, pos);
        tm.characterSize = Mathf.Clamp(dist * 0.0055f, 0.012f, 0.04f) * Random.Range(0.95f, 1.05f);
        if (parent != null) tm.characterSize *= 0.6f;     // words on falling blocks stay inside the block
        tm.color = new Color(1, 1, 1, 0);
        var mr = go.GetComponent<MeshRenderer>();
        if (runtimeMat != null) runtimeMat.mainTexture = font.material.mainTexture; // font atlas can be rebuilt
        mr.sharedMaterial = runtimeMat != null ? runtimeMat : font.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        notes.Add(new Note { tm = tm, born = Time.time, life = life * Random.Range(0.8f, 1.2f), baseSize = tm.characterSize, basePos = go.transform.localPosition, t = go.transform });
        return true;
    }

    bool FindSurface(out Vector3 pos, out Vector3 normal, out Transform parent)
    {
        pos = normal = Vector3.zero; parent = null;
        Vector3 o = cam.transform.position;
        int mask = ~(1 << 2);

        // sometimes: on a falling block that's in view
        if (Random.value < blockChance)
        {
            foreach (var b in FindObjectsByType<BlockImpact>(FindObjectsSortMode.None))
            {
                Vector3 to = b.transform.position - o;
                if (to.magnitude > maxDistance * 1.5f || Vector3.Angle(cam.transform.forward, to) > 40f) continue;
                RaycastHit bh;
                if (Physics.Raycast(o, to.normalized, out bh, maxDistance * 1.5f, mask, QueryTriggerInteraction.Ignore) && bh.transform == b.transform)
                { pos = bh.point; normal = bh.normal; parent = b.transform; return true; }
            }
        }
        // otherwise: a wall in the edge of your view (not dead centre)
        for (int i = 0; i < 16; i++)
        {
            Vector2 c = Random.insideUnitCircle;
            if (c.magnitude < 0.35f) continue;
            Vector3 dir = Quaternion.AngleAxis(c.x * 38f, cam.transform.up) * Quaternion.AngleAxis(c.y * 22f, cam.transform.right) * cam.transform.forward;
            RaycastHit h;
            if (!Physics.Raycast(o, dir, out h, maxDistance, mask, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(h.normal.y) > 0.5f) continue;          // walls only
            if (h.distance < 1.2f) continue;
            if (!WideEnough(h.point, h.normal, mask)) continue;   // skip thin wall ends
            pos = h.point; normal = h.normal;
            if (h.rigidbody != null) parent = h.transform;
            return true;
        }
        return false;
    }

    // the wall face must continue ~1.2 m either side of the spot, so the words aren't cut off
    bool WideEnough(Vector3 p, Vector3 n, int mask)
    {
        Vector3 side = Vector3.Cross(Vector3.up, n).normalized;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 probe = p + n * 0.3f + side * (1.2f * s);
            RaycastHit h;
            if (!Physics.Raycast(probe, -n, out h, 0.6f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Dot(h.normal, n) < 0.95f) return false;
        }
        return true;
    }

    
void Update()
    {
        float a = AnxietyDirector.Instance ? AnxietyDirector.Instance.anxiety : 0.5f;
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var n = notes[i];
            if (n.tm == null) { notes.RemoveAt(i); continue; }
            float k = (Time.time - n.born) / n.life;
            if (k >= 1f) { Destroy(n.tm.gameObject); notes.RemoveAt(i); continue; }
            float alpha = Mathf.Sin(k * Mathf.PI);
            alpha = Mathf.Pow(alpha, 0.6f) * Mathf.Lerp(0.45f, 0.8f, a);
            n.tm.color = new Color(0.95f, 0.95f, 0.95f, alpha);
            // trembling letters
            n.t.localPosition = n.basePos + (Vector3)(Random.insideUnitCircle * 0.003f * (0.5f + a));
            n.tm.characterSize = n.baseSize * (1f + k * 0.04f);
        }
    }
}
