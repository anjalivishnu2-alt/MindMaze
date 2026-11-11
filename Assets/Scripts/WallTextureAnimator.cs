using UnityEngine;

// Plays image sequences on the wall materials (like an animated texture in Blender)
// and slowly drifts their UVs so the surfaces never sit still.
// Works on runtime copies of the materials, so the material assets stay untouched.
public class WallTextureAnimator : MonoBehaviour
{
    [System.Serializable]
    public class Sequence
    {
        public Material material;
        public Texture2D[] frames;
        public float fps = 12f;
        public Vector2 scrollSpeed;
        public bool pingPong;
        [HideInInspector] public Material runtime;
    }

    public Sequence[] sequences;
    [Tooltip("Plays faster as anxiety rises (set by AnxietyDirector).")]
    public float speedMultiplier = 1f;

    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

    float time;

    void Start()
    {
        // Swap every renderer's shared material for a runtime copy.
        var map = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var s in sequences)
        {
            if (s.material == null) continue;
            s.runtime = new Material(s.material);
            map[s.material] = s.runtime;
        }
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && map.TryGetValue(mats[i], out var rt)) { mats[i] = rt; changed = true; }
            if (changed) r.sharedMaterials = mats;
        }
    }

    void Update()
    {
        time += Time.deltaTime * speedMultiplier;
        foreach (var s in sequences)
        {
            var m = s.runtime;
            if (m == null) continue;
            if (s.frames != null && s.frames.Length > 0)
            {
                int n = s.frames.Length;
                int f = Mathf.FloorToInt(time * s.fps);
                int idx = s.pingPong && n > 1 ? (int)Mathf.PingPong(f, n - 1) : f % n;
                var tex = s.frames[idx];
                m.SetTexture(BaseMap, tex);
                if (m.HasProperty(EmissionMap)) m.SetTexture(EmissionMap, tex);
            }
            if (s.scrollSpeed != Vector2.zero)
            {
                Vector2 off = s.scrollSpeed * time;
                m.SetTextureOffset(BaseMap, off);
                if (m.HasProperty(EmissionMap)) m.SetTextureOffset(EmissionMap, off);
            }
        }
    }
}
