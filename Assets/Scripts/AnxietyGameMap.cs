using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Top-down map. Press M (or Tab, or X in VR) to see the whole maze: solid walls, START at one end,
// EXIT at the other, where you are, and how far is left. The clock keeps running and you stand still.
public partial class AnxietyGame
{
    bool mapOpen, mapReady, startSet;
    Bounds mapBounds;
    readonly List<Rect> wallRects = new List<Rect>();     // wall footprints in world x/z
    Vector3 startPoint;
    float totalDist = 1f, leftDist = 1f, nextDist;

    static bool MapPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return CapKey(7) || MMVR.MapBtn || (k != null && (k.mKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame));
#else
        return Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    void MapInput()
    {
        if (!startSet && walker) { startPoint = walker.transform.position; startSet = true; nextDist = 0f; totalDist = Mathf.Max(1f, PathLen(startPoint, goal)); leftDist = totalDist; }
        if (MapPressed())
        {
            mapOpen = !mapOpen;
            if (mapOpen) { EnsureMap(); nextDist = 0f; }
            if (walker) walker.enabled = !mapOpen;
        }
        if (!mapOpen) return;
        if (walker && walker.enabled) walker.enabled = false;
        if (Time.time >= nextDist)
        {
            nextDist = Time.time + 0.4f;
            leftDist = PathLen(walker.transform.position, goal);
        }
    }

    static float PathLen(Vector3 a, Vector3 b)
    {
        var path = new NavMeshPath();
        if (NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.corners.Length > 1)
        {
            float d = 0f;
            for (int i = 1; i < path.corners.Length; i++) d += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return d;
        }
        return Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
    }

    void EnsureMap()
    {
        if (mapReady) return;
        var maze = GameObject.Find("mazetoday30");
        bool first = true;
        wallRects.Clear();
        if (maze)
            foreach (var r in maze.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (b.size.y < 3f) continue;                   // props, not walls
                wallRects.Add(new Rect(b.min.x, b.min.z, b.size.x, b.size.z));
                if (first) { mapBounds = b; first = false; } else mapBounds.Encapsulate(b);
            }
        if (first) mapBounds = new Bounds(Vector3.zero, new Vector3(100, 10, 100));
        // Clip to the playable zone so the map shows only corridors the player can reach
        float zx0 = ZoneX0 - 2f, zx1 = ZoneX1 + 2f, zz0 = ZoneZ0 - 2f, zz1 = ZoneZ1 + 2f;
        Vector3 zoneMin = new Vector3(Mathf.Max(mapBounds.min.x, zx0), mapBounds.min.y, Mathf.Max(mapBounds.min.z, zz0));
        Vector3 zoneMax = new Vector3(Mathf.Min(mapBounds.max.x, zx1), mapBounds.max.y, Mathf.Min(mapBounds.max.z, zz1));
        mapBounds.SetMinMax(zoneMin, zoneMax);
        mapReady = true;
    }

    Vector2 ToMap(Rect p, Vector3 w)
    {
        return new Vector2(p.x + (w.x - mapBounds.min.x) / mapBounds.size.x * p.width,
                           p.y + (1f - (w.z - mapBounds.min.z) / mapBounds.size.z) * p.height);
    }

void DrawMapHint()
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        var dim = MindMazeUI.A(MMStyle.Paper, 0.55f);
        if (!mapOpen) MindMazeUI.Tracked(new Vector2(W - 110f * s, H - 60f * s), MMVR.WorldUI ? "X  MAP" : "M  MAP", MindMazeUI.Cond, 26f * s, 0.15f, dim, 0, 0.6f);
        if (MMVR.WorldUI)
        {
            // clickable with the controller pointer: pause or leave at any moment
            HudButton("PAUSE", new Vector2(120f * s, H - 60f * s), "pause");
            HudButton("LEAVE", new Vector2(290f * s, H - 60f * s), "leave");
        }
        else MindMazeUI.Tracked(new Vector2(130f * s, H - 60f * s), "ESC  PAUSE", MindMazeUI.Cond, 26f * s, 0.15f, dim, 0, 0.6f);
    }

    readonly List<Rect> hudHits = new List<Rect>();
    readonly List<string> hudIds = new List<string>();
    void HudButton(string label, Vector2 c, string id)
    {
        float s = MindMazeUI.S, size = 28f * s;
        float w = MindMazeUI.TrackedWidth(label, MindMazeUI.Cond, size, 0.2f), pad = 22f * s;
        var r = new Rect(c.x - w * 0.5f - pad, c.y - size * 1.0f, w + pad * 2f, size * 2f);
        bool hot = r.Contains(Ptr());
        MindMazeUI.Fill(r, hot ? new Color(0.93f, 0.92f, 0.9f, 0.95f) : new Color(0f, 0f, 0f, 0.55f));
        MindMazeUI.Tracked(c, label, MindMazeUI.Cond, size, 0.2f, hot ? MMStyle.Ink : MindMazeUI.A(MMStyle.Paper, 0.9f), 0, 0.0f);
        hudHits.Add(r); hudIds.Add(id);
    }

void DrawMap()
    {
        if (!mapOpen || !mapReady) return;
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        MindMazeUI.FillScreen(new Color(0, 0, 0, 0.86f));

        // the whole block (map + distance + hint) is centred between the clock and the panic bar
        float aspect = mapBounds.size.x / mapBounds.size.z;
        float info = 150f * s, areaTop = 200f * s, areaH = H - 360f * s;
        float maxPh = areaH - info, maxPw = W * 0.84f;
        float ph = Mathf.Min(maxPh, maxPw / aspect), pw = ph * aspect;
        float top = areaTop + (areaH - (ph + info)) * 0.5f;
        var p = new Rect(W * 0.5f - pw * 0.5f, top, pw, ph);

        MindMazeUI.Fill(p, new Color(1f, 1f, 1f, 0.05f));
        float k0 = p.width / mapBounds.size.x, minT = 2.5f * s;
        var wallCol = new Color(0.93f, 0.92f, 0.9f, 0.9f);
        for (int i = 0; i < wallRects.Count; i++)
        {
            var w = wallRects[i];
            float x = p.x + (w.x - mapBounds.min.x) * k0, wpx = Mathf.Max(minT, w.width * k0);
            float y = p.y + (1f - (w.yMax - mapBounds.min.z) / mapBounds.size.z) * p.height, hpx = Mathf.Max(minT, w.height * k0);
            MindMazeUI.Fill(new Rect(x, y, wpx, hpx), wallCol);
        }

        // START and EXIT sit at the two ends
        Vector2 st = ToMap(p, startPoint), ex = ToMap(p, goal);
        float rs = 15f * s;
        MindMazeUI.Tex(new Rect(st.x - rs, st.y - rs, rs * 2f, rs * 2f), MindMazeUI.Ring, MMStyle.Paper);
        MapLabel(new Vector2(st.x, st.y - 44f * s), "START", MMStyle.Paper, 28f * s);

        float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
        float rg = (36f + 14f * pulse) * s;
        MindMazeUI.Tex(new Rect(ex.x - rg, ex.y - rg, rg * 2f, rg * 2f), MindMazeUI.Glow, MindMazeUI.A(MMStyle.Violet, 0.85f));
        float r1 = 13f * s;
        MindMazeUI.Tex(new Rect(ex.x - r1, ex.y - r1, r1 * 2f, r1 * 2f), MMStyle.Dot, MMStyle.Violet);
        MapLabel(new Vector2(ex.x, ex.y - 48f * s), "EXIT", MMStyle.Violet, 30f * s);

        // you: a hollow pulsing ring, a small dot and a facing tick. Nothing filled, so no wall is hidden.
        var cam = Camera.main;
        Vector2 me = ToMap(p, walker.transform.position);
        float yaw = cam ? cam.transform.eulerAngles.y : walker.transform.eulerAngles.y;
        var red = new Color(1f, 0.32f, 0.32f, 1f);
        float rr0 = (20f + 6f * pulse) * s;
        MindMazeUI.Tex(new Rect(me.x - rr0, me.y - rr0, rr0 * 2f, rr0 * 2f), MindMazeUI.Ring, MindMazeUI.A(red, 0.55f + 0.45f * pulse));
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(yaw, me);
        MindMazeUI.Fill(new Rect(me.x - 1.5f * s, me.y - 36f * s, 3f * s, 28f * s), red);
        GUI.matrix = m;
        float rm = 6f * s;
        MindMazeUI.Tex(new Rect(me.x - rm, me.y - rm, rm * 2f, rm * 2f), MMStyle.Dot, red);

        // how much is left, centred under the map
        float prog = Mathf.Clamp01(1f - leftDist / totalDist), by = p.yMax;
        MindMazeUI.Tracked(new Vector2(W * 0.5f, by + 50f * s), Mathf.RoundToInt(leftDist) + " M TO EXIT", MindMazeUI.Cond, 46f * s, 0.12f, MMStyle.Paper, 0, 0.7f);
        MMStyle.ThinBar(new Rect(W * 0.5f - 240f * s, by + 88f * s, 480f * s, 7f * s), prog, MMStyle.Violet, 0.95f);
        MindMazeUI.Tracked(new Vector2(W * 0.5f, by + 124f * s), MMVR.WorldUI ? "X  ·  BACK TO MAZE" : "M  ·  BACK TO MAZE", MindMazeUI.BodyMedium, 24f * s, 0.15f, MindMazeUI.A(MMStyle.Muted, 1f), 0, 0.6f);
    }


static void MapLabel(Vector2 c, string text, Color col, float size)
    {
        float w = MindMazeUI.TrackedWidth(text, MindMazeUI.Cond, size, 0.2f), pad = size * 0.5f;
        MindMazeUI.Fill(new Rect(c.x - w * 0.5f - pad, c.y - size * 0.75f, w + pad * 2f, size * 1.5f), new Color(0f, 0f, 0f, 0.9f));
        MindMazeUI.Tracked(c, text, MindMazeUI.Cond, size, 0.2f, col, 0, 0.6f);
    }
}
