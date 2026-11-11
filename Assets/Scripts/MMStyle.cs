using UnityEngine;

// MIND MAZE - screen style from the Figma design:
// grit stencil titles, wide-tracked caps menu items, rough brush stroke on the selected one.
public static class MMStyle
{
    public static readonly Color Violet = new Color(0.69f, 0.37f, 0.84f, 1f);
    public static readonly Color Ink    = new Color(0.06f, 0.06f, 0.07f, 1f);
    public static readonly Color Paper  = MindMazeUI.Bone;
    public static readonly Color Muted  = new Color(0.62f, 0.61f, 0.60f, 1f);

    static Texture2D brush, grit;
    public static Texture2D BrushTex { get { if (brush == null) brush = MakeBrush(); return brush; } }
    public static Texture2D GritTex  { get { if (grit == null)  grit  = MakeGrit();  return grit; } }

    static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u + 12345u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;
        }
    }

    static Texture2D Make(int w, int h, System.Func<float, float, float> alpha)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color(1, 1, 1, alpha((x + 0.5f) / w, 1f - (y + 0.5f) / h));   // v = 0 at the top
        t.SetPixels32(px); t.Apply();
        return t;
    }

    // dry-brush stroke: bristle teeth on the top/bottom edges, streaky ragged ends
    static Texture2D MakeBrush()
    {
        const int w = 1024, h = 128;
        return Make(w, h, (u, v) =>
        {
            int cx = (int)(u * w / 3f);
            float tT = Hash(cx, 1) < 0.35f ? Hash(cx, 2) * 0.11f : Hash(cx, 2) * 0.03f;
            float tB = Hash(cx, 3) < 0.35f ? Hash(cx, 4) * 0.11f : Hash(cx, 4) * 0.03f;
            float yT = 0.08f + tT, yB = 0.92f - tB;
            int r = (int)(v * h / 5f);
            float eL = Hash(r, 5) < 0.5f ? Hash(r, 6) * 0.06f : Hash(r, 6) * 0.012f;
            float eR = Hash(r, 7) < 0.5f ? Hash(r, 8) * 0.06f : Hash(r, 8) * 0.012f;
            float xL = 0.012f + eL, xR = 1f - 0.012f - eR;
            return Mathf.Clamp01((v - yT) * h) * Mathf.Clamp01((yB - v) * h) * Mathf.Clamp01((u - xL) * w) * Mathf.Clamp01((xR - u) * w);
        });
    }

    // worn-paint speckle + scratches, drawn dark over the stencil letters
    static Texture2D MakeGrit()
    {
        const int w = 512, h = 128;
        return Make(w, h, (u, v) =>
        {
            int x = (int)(u * w), y = (int)(v * h);
            float a = 0f;
            if (Hash(x, y) < 0.014f) a = 1f;
            if (Hash(x / 2, y / 2 + 777) < 0.006f) a = 1f;
            if (Hash(y, 13) < 0.03f && Hash(x / 20, y + 99) < 0.35f) a = 1f;
            return a;
        });
    }

    static Texture2D dot;
    public static Texture2D Dot { get { if (dot == null) dot = Make(64, 64, (u, v) => Mathf.Clamp01((0.48f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f))) * 40f)); return dot; } }

    /// Stencil title, centred on 'centre', with the grit overlay. Returns drawn width.
    public static float Stencil(Vector2 centre, string s, float size, float alpha)
    {
        s = s.ToUpperInvariant();
        Color c = MindMazeUI.A(Paper, alpha);
        float w = MindMazeUI.Tracked(centre, s, MindMazeUI.Display, size, 0.03f, c, 0, 0f);
        if (c.a > 0.003f)
            MindMazeUI.Tex(new Rect(centre.x - w * 0.5f, centre.y - size * 0.42f, w, size * 0.84f), GritTex, new Color(0.04f, 0.04f, 0.05f, 0.9f * c.a));
        return w;
    }

    /// Wide-tracked caps line (violet by default), centred.
    public static void Line(Vector2 centre, string s, float size, Color c, float alpha, Font f = null)
    {
        MindMazeUI.Tracked(centre, s.ToUpperInvariant(), f != null ? f : MindMazeUI.Cond, size, 0.08f, MindMazeUI.A(c, alpha), 0, 0.5f);
    }

    public static void BrushRect(Rect item, Color c)
    {
        float s = MindMazeUI.S;
        MindMazeUI.Tex(new Rect(item.x - 20f * s, item.y - 8f * s, item.width + 40f * s, item.height + 16f * s), BrushTex, c);
    }

    /// One menu row. Selected = brush stroke with dark text; otherwise plain text.
    public static Rect MenuItem(Vector2 centre, string label, bool selected, float alpha)
    {
        float s = MindMazeUI.S, fs = 40f * s;
        string t = label.ToUpperInvariant();
        float w = MindMazeUI.TrackedWidth(t, MindMazeUI.BodyMedium, fs, 0.1f);
        var r = new Rect(centre.x - w * 0.5f - 40f * s, centre.y - 40f * s, w + 80f * s, 80f * s);
        if (selected) BrushRect(r, MindMazeUI.A(Paper, alpha));
        MindMazeUI.Tracked(centre, t, MindMazeUI.BodyMedium, fs, 0.1f, MindMazeUI.A(selected ? Ink : Paper, alpha), 0, selected ? 0f : 0.6f);
        return r;
    }

    /// Thin bar (panic meter): faint track + violet fill.
    public static void ThinBar(Rect r, float t, Color fill, float alpha)
    {
        MindMazeUI.Fill(r, new Color(1, 1, 1, 0.18f * alpha));
        MindMazeUI.Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), MindMazeUI.A(fill, alpha));
    }
}
