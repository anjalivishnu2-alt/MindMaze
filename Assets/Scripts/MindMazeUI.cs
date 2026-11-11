using UnityEngine;

// MIND MAZE - immediate-mode UI kit.
// Fonts live in Assets/Resources/Fonts. Everything is laid out for a 1080p reference
// and scaled by screen height (S), so it stays proportional at any resolution.
public static class MindMazeUI
{
    // ---------- palette ----------
    public static readonly Color Bone   = new Color(0.94f, 0.92f, 0.88f, 1f);
    public static readonly Color Ash    = new Color(0.66f, 0.64f, 0.61f, 1f);
    public static readonly Color Dim    = new Color(0.45f, 0.44f, 0.42f, 1f);
    public static readonly Color Ember  = new Color(0.86f, 0.26f, 0.22f, 1f);
    public static readonly Color Breath = new Color(0.62f, 0.80f, 0.90f, 1f);

    // ---------- fonts ----------
    public static Font Display, DisplayItalic, DisplayLight, Body, BodyMedium, Cond, CondLight, Italic;

    // ---------- textures ----------
    public static Texture2D Vignette, GradV, GradH, Ring, Glow, KeyFill, KeyLine, Arrow, Pixel;

    static GUIStyle text, slice;
    static bool ready;

    public static float S => Screen.height / 1080f;
    public static float W => Screen.width;
    public static float H => Screen.height;

    public static void Init()
    {
        if (ready) return;
        ready = true;
        // Big Shoulders Stencil for titles, Montserrat for everything else
        Display       = Load("BigShouldersStencilDisplay-Black");
        DisplayItalic = Load("Montserrat-SemiBold");
        DisplayLight  = Load("Montserrat-Medium");
        Body          = Load("Montserrat-Medium");
        BodyMedium    = Load("Montserrat-Medium");
        Cond          = Load("Montserrat-SemiBold");
        CondLight     = Load("Montserrat-Medium");
        Italic        = Load("Montserrat-Medium");

        text  = new GUIStyle { clipping = TextClipping.Overflow, richText = false };
        slice = new GUIStyle();

        Pixel    = Texture2D.whiteTexture;
        Vignette = MakeTex(256, 256, (u, v) => { float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 1.414f; return Smooth(0.30f, 1.05f, d); });
        GradV    = MakeTex(4, 256, (u, v) => Smooth(0f, 1f, v));                      // transparent at top, solid at bottom
        GradH    = MakeTex(256, 4, (u, v) => Smooth(0f, 1f, 1f - u));                // solid at left, transparent at right
        Ring     = MakeTex(256, 256, (u, v) => { float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f; return Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.035f); });
        Glow     = MakeTex(128, 128, (u, v) => { float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f; float k = Mathf.Clamp01(1f - d); return k * k; });
        KeyFill  = RoundRect(32, 5f, false);
        KeyLine  = RoundRect(32, 5f, true);
        Arrow    = MakeTex(64, 64, (u, v) =>
        {
            // up-pointing chevron-triangle
            float y = v, x = u;                    // v = 0 at the top
            if (y < 0.22f || y > 0.78f) return 0f;
            float half = (y - 0.22f) / 0.56f * 0.36f;
            float e = Mathf.Min(x - (0.5f - half), (0.5f + half) - x);
            return Mathf.Clamp01(e * 64f + 0.5f) * Mathf.Clamp01((0.78f - y) * 64f + 0.5f);
        });
    }

    static Font Load(string n)
    {
        var f = Resources.Load<Font>("Fonts/" + n);
        if (f == null) Debug.LogWarning("[MindMazeUI] missing font Resources/Fonts/" + n);
        return f;
    }

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    static Texture2D MakeTex(int w, int h, System.Func<float, float, float> alpha)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color(1, 1, 1, alpha((x + 0.5f) / w, 1f - (y + 0.5f) / h));
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Texture2D RoundRect(int size, float r, bool outline)
    {
        return MakeTex(size, size, (u, v) =>
        {
            float x = u * size, y = v * size;
            float qx = Mathf.Max(Mathf.Abs(x - size * 0.5f) - (size * 0.5f - r), 0f);
            float qy = Mathf.Max(Mathf.Abs(y - size * 0.5f) - (size * 0.5f - r), 0f);
            float d = Mathf.Sqrt(qx * qx + qy * qy) - r;            // <0 inside
            float inside = Mathf.Clamp01(0.5f - d);
            if (!outline) return inside;
            float lw = 1.6f;
            return inside * Mathf.Clamp01(d + lw + 0.5f);
        });
    }

    public static bool Repaint => Event.current != null && Event.current.type == EventType.Repaint;

    public static Color A(Color c, float a) { c.a *= Mathf.Clamp01(a); return c; }

    // ---------- primitives ----------
    public static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Pixel); GUI.color = o; }
    public static void Tex(Rect r, Texture t, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t); GUI.color = o; }
    public static void FillScreen(Color c) => Fill(new Rect(0, 0, W, H), c);

    public static void VignetteScreen(Color c, float scale = 1f)
    {
        float pad = (scale - 1f) * 0.5f;
        Tex(new Rect(-W * pad, -H * pad, W * scale, H * scale), Vignette, c);
    }

    public static void Sliced(Rect r, Texture2D t, Color c, int border = 7)
    {
        if (!Repaint) return;
        slice.normal.background = t;
        slice.border = new RectOffset(border, border, border, border);
        var o = GUI.color; GUI.color = c;
        slice.Draw(r, false, false, false, false);
        GUI.color = o;
    }

    // ---------- text ----------
    static void Prep(Font f, float size, TextAnchor a, bool wrap)
    {
        text.font = f;
        text.fontSize = Mathf.Max(10, Mathf.RoundToInt(size));   // never smaller than legible
        text.alignment = a;
        text.wordWrap = wrap;
    }

    public static Vector2 Measure(string s, Font f, float size)
    {
        Prep(f, size, TextAnchor.UpperLeft, false);
        return text.CalcSize(new GUIContent(s));
    }

    public static float Height(string s, Font f, float size, float width)
    {
        Prep(f, size, TextAnchor.UpperLeft, true);
        return text.CalcHeight(new GUIContent(s), width);
    }

    /// Plain label with a soft drop shadow for legibility over the bright walls.
    public static void Label(Rect r, string s, Font f, float size, Color c, TextAnchor a = TextAnchor.MiddleCenter, bool wrap = false, float shadow = 0.75f)
    {
        if (c.a <= 0.003f) return;
        Prep(f, size, a, wrap);
        if (shadow > 0f)
        {
            float o = Mathf.Max(1f, size * 0.05f);
            text.normal.textColor = new Color(0, 0, 0, c.a * shadow * 0.45f);
            GUI.Label(new Rect(r.x + o, r.y + o * 1.4f, r.width, r.height), s, text);
            GUI.Label(new Rect(r.x - o * 0.5f, r.y + o, r.width, r.height), s, text);
        }
        text.normal.textColor = c;
        GUI.Label(r, s, text);
    }

    /// Letter-spaced single-line text. align: -1 left, 0 centre, 1 right of 'anchor'. Returns drawn width.
    public static float Tracked(Vector2 anchor, string s, Font f, float size, float trackingEm, Color c, int align = 0, float shadow = 0.75f)
    {
        float total = TrackedWidth(s, f, size, trackingEm);
        if (c.a <= 0.003f) return total;
        Prep(f, size, TextAnchor.MiddleLeft, false);
        float sp = trackingEm * size;
        float x = align < 0 ? anchor.x : align > 0 ? anchor.x - total : anchor.x - total * 0.5f;
        float h = size * 1.6f;
        foreach (char ch in s)
        {
            string cs = ch.ToString();
            float w = text.CalcSize(new GUIContent(cs)).x;
            Label(new Rect(x, anchor.y - h * 0.5f, w + 4, h), cs, f, size, c, TextAnchor.MiddleLeft, false, shadow);
            Prep(f, size, TextAnchor.MiddleLeft, false);
            x += w + sp;
        }
        return total;
    }

    public static float TrackedWidth(string s, Font f, float size, float trackingEm)
    {
        Prep(f, size, TextAnchor.MiddleLeft, false);
        float total = 0f;
        foreach (char ch in s) total += text.CalcSize(new GUIContent(ch.ToString())).x;
        return total + trackingEm * size * Mathf.Max(0, s.Length - 1);
    }

    /// Fixed-width digits so a ticking clock never wobbles sideways.
    public static void Clock(Vector2 centre, string s, Font f, float size, Color c)
    {
        Prep(f, size, TextAnchor.MiddleCenter, false);
        float cell = text.CalcSize(new GUIContent("0")).x * 1.02f;
        float colon = cell * 0.55f;
        float total = 0f;
        foreach (char ch in s) total += ch == ':' ? colon : cell;
        float x = centre.x - total * 0.5f, h = size * 1.4f;
        foreach (char ch in s)
        {
            float w = ch == ':' ? colon : cell;
            Label(new Rect(x, centre.y - h * 0.5f - (ch == ':' ? size * 0.06f : 0f), w, h), ch.ToString(), f, size, c, TextAnchor.MiddleCenter);
            x += w;
        }
    }

    // ---------- widgets ----------
    /// A thin rule with a small diamond in the middle.
    public static void Divider(Vector2 centre, float width, Color c)
    {
        float s = S;
        float gap = 14f * s;
        float half = width * 0.5f;
        if (half > gap)
        {
            Fill(new Rect(centre.x - half, centre.y - 0.5f * s, half - gap, Mathf.Max(1f, s)), c);
            Fill(new Rect(centre.x + gap, centre.y - 0.5f * s, half - gap, Mathf.Max(1f, s)), c);
        }
        float d = 7f * s;
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, centre);
        Fill(new Rect(centre.x - d * 0.5f, centre.y - d * 0.5f, d, d), c);
        GUI.matrix = m;
    }

    public enum Glyph { None, Up, Down, Left, Right }

    /// A keyboard keycap. Returns its width.
    public static float KeyCap(Vector2 centre, string label, float h, float alpha, Glyph glyph = Glyph.None, bool lit = false)
    {
        float s = h / 44f;
        float fs = 19f * s;
        float w = glyph != Glyph.None ? h : Mathf.Max(h, Measure(label, Cond, fs).x + 26f * s);
        var r = new Rect(centre.x - w * 0.5f, centre.y - h * 0.5f, w, h);
        // a slight "depth" lip under the cap
        Sliced(new Rect(r.x, r.y + 3f * s, r.width, r.height), KeyFill, new Color(0, 0, 0, 0.55f * alpha));
        Sliced(r, KeyFill, new Color(0.05f, 0.05f, 0.06f, (lit ? 0.85f : 0.72f) * alpha));
        if (lit) Tex(new Rect(r.x - h * 0.5f, r.y - h * 0.6f, r.width + h, r.height + h * 1.2f), Glow, A(Bone, 0.10f * alpha));
        Sliced(r, KeyLine, A(Bone, (lit ? 1f : 0.7f) * alpha));
        if (glyph != Glyph.None)
        {
            float g = h * 0.42f;
            float ang = glyph == Glyph.Up ? 0f : glyph == Glyph.Right ? 90f : glyph == Glyph.Down ? 180f : 270f;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, centre);
            Tex(new Rect(centre.x - g * 0.5f, centre.y - g * 0.5f, g, g), Arrow, A(Bone, 0.95f * alpha));
            GUI.matrix = m;
        }
        else
        {
            Label(new Rect(r.x, r.y - 1f * s, r.width, r.height), label, Cond, fs, A(Bone, 0.95f * alpha), TextAnchor.MiddleCenter, false, 0f);
        }
        return w;
    }

    /// "PRESS [KEY] TO DO X" on one line, centred.
    public static void Prompt(Vector2 centre, string before, string key, string after, float size, float alpha)
    {
        float s = S;
        float track = 0f;
        float capH = size * 2.0f;
        float gap = 12f * s;
        float wb = TrackedWidth(before, BodyMedium, size, track);
        float wa = TrackedWidth(after, BodyMedium, size, track);
        float wk = Mathf.Max(capH, Measure(key, Cond, 19f * capH / 44f).x + 26f * capH / 44f);
        float total = wb + gap + wk + gap + wa;
        float x = centre.x - total * 0.5f;
        Tracked(new Vector2(x, centre.y), before, BodyMedium, size, track, A(Bone, 0.9f * alpha), -1);
        KeyCap(new Vector2(x + wb + gap + wk * 0.5f, centre.y), key, capH, alpha, Glyph.None, true);
        Tracked(new Vector2(x + wb + gap + wk + gap, centre.y), after, BodyMedium, size, track, A(Bone, 0.9f * alpha), -1);
    }

    /// Same prompt, starting at x (left aligned).
    public static void PromptLeft(Vector2 left, string before, string key, string after, float size, float alpha)
    {
        float capH = size * 2.0f;
        float wb = TrackedWidth(before, BodyMedium, size, 0f);
        float wa = TrackedWidth(after, BodyMedium, size, 0f);
        float wk = Mathf.Max(capH, Measure(key, Cond, 19f * capH / 44f).x + 26f * capH / 44f);
        float gap = 12f * S;
        float total = wb + gap + wk + gap + wa;
        Prompt(new Vector2(left.x + total * 0.5f, left.y), before, key, after, size, alpha);
    }

    /// Horizontal progress bar (0..1) with a faint track.
    public static void Bar(Rect r, float t, Color c, float alpha)
    {
        Fill(r, new Color(1, 1, 1, 0.12f * alpha));
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), A(c, alpha));
    }
}
