using UnityEngine;

// Writes a thought along the long faces of a falling block (like words scrawled on a
// Jenga piece). The text follows the block as it tumbles and stays after it lands.
// Text objects are not parented to the block (its non-uniform scale would skew them).
public class BlockWords : MonoBehaviour
{
    class Face { public TextMesh tm; public Vector3 localPos; public Quaternion localRot; }
    Face[] faces;

    void Start()
    {
        var tw = ThoughtWriter.Instance;
        if (tw == null || tw.TextFont == null) { enabled = false; return; }
        string line = WhisperSystem.Lines[Random.Range(0, WhisperSystem.Lines.Length)];
        string line2 = Random.value < 0.5f ? line : WhisperSystem.Lines[Random.Range(0, WhisperSystem.Lines.Length)];

        // long faces of the block: top / bottom (+-Y) and the two thin sides (+-X); text runs along Z
        faces = new[]
        {
            Make(line,  new Vector3(0,  0.5f, 0), Quaternion.LookRotation(Vector3.down, Vector3.forward) * Quaternion.Euler(0, 0, 90), 0.75f, tw),
            Make(line2, new Vector3(0, -0.5f, 0), Quaternion.LookRotation(Vector3.up,   Vector3.forward) * Quaternion.Euler(0, 0, 90), 0.75f, tw),
            Make(line,  new Vector3( 0.5f, 0, 0), Quaternion.LookRotation(Vector3.left,  Vector3.up) * Quaternion.Euler(0, 0, 0), 0.75f, tw),
            Make(line2, new Vector3(-0.5f, 0, 0), Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(0, 0, 0), 0.75f, tw),
        };
        LateUpdate();
    }

    Face Make(string text, Vector3 localPos, Quaternion localRot, float fill, ThoughtWriter tw)
    {
        var go = new GameObject("block words");
        var tm = go.AddComponent<TextMesh>();
        tm.font = tw.TextFont; tm.text = text; tm.fontSize = 64;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.95f, 0.95f, 0.95f, 0.9f);
        tm.characterSize = 0.05f;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = tw.TextMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // fit the line to ~75% of the block's length, and not taller than its face
        go.transform.rotation = Quaternion.identity;
        float w = Mathf.Max(0.01f, mr.bounds.size.x), h = Mathf.Max(0.01f, mr.bounds.size.y);
        float length = transform.lossyScale.z * fill;
        float faceH = (Mathf.Abs(localPos.y) > 0.1f ? transform.lossyScale.x : transform.lossyScale.y) * 0.8f;
        tm.characterSize *= Mathf.Min(length / w, faceH / h);
        return new Face { tm = tm, localPos = localPos, localRot = localRot };
    }

void LateUpdate()
    {
        if (faces == null) return;
        var cam = Camera.main;
        foreach (var f in faces)
        {
            if (f.tm == null) continue;
            Vector3 n = transform.TransformDirection(f.localPos.normalized).normalized;
            Vector3 p = transform.TransformPoint(f.localPos) + n * 0.012f;
            f.tm.transform.position = p;
            var r = f.tm.GetComponent<MeshRenderer>();
            if (cam == null) continue;
            // only the faces turned towards you show their words
            bool facing = Vector3.Dot(n, cam.transform.position - p) > 0f;
            r.enabled = facing;
            if (!facing) continue;
            // lay the text flat on the face, running along the block, readable left-to-right from here
            Vector3 along = transform.forward;
            if (Vector3.Dot(along, cam.transform.right) < 0f) along = -along;
            Vector3 fwd = -n;
            f.tm.transform.rotation = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, along));
        }
    }

    void OnDestroy()
    {
        if (faces == null) return;
        foreach (var f in faces) if (f.tm != null) Destroy(f.tm.gameObject);
    }
}
