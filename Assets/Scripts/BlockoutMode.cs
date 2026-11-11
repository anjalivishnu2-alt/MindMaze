using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Greybox / blockout look for process videos. Add in Play mode: strips post-processing,
// fog, animated wall textures and world text, and gives every mesh a flat grey material
// (floor darker, walls lighter, the goal beacon and falling blocks in blockout orange).
// Nothing is saved: materials are only swapped at runtime.
public class BlockoutMode : MonoBehaviour
{
    public static bool Active;

    Material wall, floor, accent, block;
    readonly HashSet<Renderer> done = new HashSet<Renderer>();
    float nextScan;

    void Awake()
    {
        Active = true;
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        wall   = Make(lit, new Color(0.70f, 0.70f, 0.70f));
        floor  = Make(lit, new Color(0.42f, 0.42f, 0.43f));
        accent = Make(lit, new Color(1.00f, 0.55f, 0.15f));
        block  = Make(lit, new Color(0.55f, 0.57f, 0.60f));

        foreach (var v in FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)) v.enabled = false;
        foreach (var w in FindObjectsByType<WallTextureAnimator>(FindObjectsSortMode.None)) w.enabled = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.56f);
        var cam = Camera.main;
        if (cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.80f, 0.82f);
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data) data.renderPostProcessing = false;
        }
        Scan();
    }

    static Material Make(Shader sh, Color c)
    {
        var m = new Material(sh);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    void Scan()
    {
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || done.Contains(r)) continue;
            done.Add(r);
            if (r.GetComponent<TextMesh>() != null) { r.gameObject.SetActive(false); continue; }   // no written thoughts yet
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) { r.enabled = false; continue; }
            Material m = wall;
            string n = r.name.ToLowerInvariant();
            if (r.transform.root.name == "DEADLINE") m = accent;
            else if (r.GetComponent<BlockImpact>() != null || r.GetComponentInParent<BlockImpact>() != null) m = block;
            else if (n.Contains("floor") || r.bounds.size.y < 0.3f) m = floor;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
        }
    }

    void LateUpdate()
    {
        RenderSettings.fog = false;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.cookie = null;   // no animated shadow patterns
        if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.25f; Scan(); }
    }

    void OnDestroy() { Active = false; }
}
