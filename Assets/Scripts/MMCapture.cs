using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Scripted captures for MIND MAZE:
//   Gameplay - a full walkthrough played with the same buttons a player uses (menus, help,
//              settings, time slider, the maze, map, breathing, pause, calm exit, the exit).
//   Trailer  - a cinematic cut with its own camera, title cards and sound design.
// Everything is timed in frames, so the video is identical however slow the capture runs.
// Started in Play mode with MMCapture.Launch(...). Recording itself is done by Assets/Editor/MMRecord.cs.
public class MMCapture : MonoBehaviour
{
    public static System.Action<string> StartRec;
    public static System.Action StopRec;
    public static bool Done;
    public static string Status = "idle";
    public static MMCapture I;
    public static bool Probe;                // save stills along the route (for choosing trailer shots)

    public enum Mode { Gameplay, Trailer }
    public Mode mode;
    public string file;
    public bool record = true;
    public float chosenLefts = 8;            // gameplay: how far the time slider is pulled down
    public const int Fps = 30;

    AnxietyGame game; MazeWalker walker; AnxietyDirector director; FallingBlocks blocks; WhisperSystem whispers; ThoughtWriter writer; Camera cam; CharacterController cc; AutoTour tour;
    int frame, f0;
    float T { get { return (frame - f0) / (float)Fps; } }
    const float Dt = 1f / Fps;

    public static MMCapture Launch(Mode m, string file, bool record)
    {
        if (I != null) Destroy(I.gameObject);
        var go = new GameObject("MMCapture");
        DontDestroyOnLoad(go);
        var c = go.AddComponent<MMCapture>();
        c.mode = m; c.file = file; c.record = record;
        Done = false; Status = "starting";
        return c;
    }

    void Awake() { I = this; }
    void Start() { SaveSettings(); StartCoroutine(mode == Mode.Gameplay ? Gameplay() : Trailer()); }

    void Acquire()
    {
        game = FindFirstObjectByType<AnxietyGame>();
        walker = FindFirstObjectByType<MazeWalker>();
        director = FindFirstObjectByType<AnxietyDirector>();
        blocks = FindFirstObjectByType<FallingBlocks>();
        whispers = FindFirstObjectByType<WhisperSystem>();
        writer = FindFirstObjectByType<ThoughtWriter>();
        cam = Camera.main;
        cc = walker ? walker.GetComponent<CharacterController>() : null;
        tour = walker ? walker.GetComponent<AutoTour>() : null;
        if (tour) tour.enabled = false;      // it zeroes walker.autoMove every frame otherwise
    }

    IEnumerator Reload()
    {
        Time.timeScale = 1f; AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        yield return null; yield return null;
        Acquire();
    }

    void BeginRec()
    {
        if (record && StartRec != null) StartRec(file);
        else { Time.captureFramerate = Fps; QualitySettings.vSyncCount = 0; Application.targetFrameRate = Fps; }
    }

    void EndRec()
    {
        if (record && StopRec != null) StopRec();
        Time.captureFramerate = 0; Application.targetFrameRate = -1;
    }

    IEnumerator Wait(float s) { int end = frame + Mathf.Max(1, Mathf.RoundToInt(s * Fps)); while (frame < end) yield return null; }
    IEnumerator At(float t) { while (T < t) yield return null; }

    // ---------- settings: the demo changes them, then puts the player's own back ----------
    int sShake, sTunnel, sBlur, sVol; bool sSeat, sVig, sSnap, saved; float sFrac;
    void SaveSettings()
    {
        sShake = MMSettings.Shake; sTunnel = MMSettings.Tunnel; sBlur = MMSettings.Blur; sVol = MMSettings.Volume;
        sSeat = MMSettings.Seated; sVig = MMSettings.Vignette; sSnap = MMSettings.SnapTurn; sFrac = PlayerPrefs.GetFloat("mm_time_frac", 0.4f); saved = true;
    }
    void RestoreSettings()
    {
        if (!saved) return;
        MMSettings.Shake = sShake; MMSettings.Tunnel = sTunnel; MMSettings.Blur = sBlur; MMSettings.Volume = sVol;
        MMSettings.Seated = sSeat; MMSettings.Vignette = sVig; MMSettings.SnapTurn = sSnap; PlayerPrefs.SetFloat("mm_time_frac", sFrac); PlayerPrefs.Save();
    }
    void DefaultSettings(int volume)
    {
        MMSettings.Seated = false; MMSettings.Vignette = true; MMSettings.SnapTurn = false;
        MMSettings.Shake = 60; MMSettings.Tunnel = 60; MMSettings.Blur = 40; MMSettings.Volume = volume;
        PlayerPrefs.SetFloat("mm_time_frac", 0.4f); PlayerPrefs.Save();
    }

    void OnDestroy()
    {
        RestoreSettings();
        if (I == this) { AnxietyGame.CapHideUI = false; AnxietyGame.CapLevel = 1; Time.captureFramerate = 0; Application.targetFrameRate = -1; I = null; }
    }

    // =====================================================================================
    //  GAMEPLAY
    // =====================================================================================
    string capTitle, capSub; bool capOn; float capA;
    readonly float[] lit = new float[8];      // 0 up 1 down 2 left 3 right 4 enter 5 space 6 esc 7 map
    bool walking, overlayKeys, endCard;
    int panicHandledUntil;

    void Cap(string title, string sub = null) { if (title == null) { capOn = false; return; } capTitle = title; capSub = sub; capOn = true; capA = 0f; }

    IEnumerator Key(int which, float after)
    {
        AnxietyGame.CapPress(which);
        lit[which] = 1f;
        yield return null; yield return null;
        if (after > 0f) yield return Wait(after);
    }

    bool Playing() { return game && game.state == AnxietyGame.State.Playing; }

    IEnumerator Walk(float secs)
    {
        int n = Mathf.RoundToInt(secs * Fps);
        while (n > 0 && Playing())
        {
            if (director.panicking && frame > panicHandledUntil)
            {
                string a = capTitle, b = capSub; bool was = capOn;
                Cap("Panic attack", "Vision narrows  ·  legs slow  ·  stop and breathe");
                yield return Wait(2.4f);
                yield return Breathe(6.5f);
                panicHandledUntil = frame + 14 * Fps;
                if (was) Cap(a, b); else Cap(null);
            }
            n--; yield return null;
        }
    }

    IEnumerator Breathe(float secs) { if (game) game.forceBreath = true; yield return Wait(secs); if (game) game.forceBreath = false; }

    IEnumerator Gameplay()
    {
        DefaultSettings(70);
        AnxietyGame.CapLevel = 1; AnxietyGame.CapHideUI = false;
        yield return Reload();
        BeginRec();
        yield return null;

        // ---- title, help, settings ----
        Status = "menus";
        yield return Wait(6.6f);
        overlayKeys = true;
        Cap("Main menu", "Arrow keys to choose  ·  Enter to select");
        yield return Wait(2.2f);
        yield return Key(1, 0.8f);
        yield return Key(4, 0.5f);
        Cap("Help & guide", "Every control on one screen");
        yield return Wait(4.8f);
        yield return Key(4, 0.9f);                       // Back
        yield return Key(1, 0.45f); yield return Key(1, 0.7f);
        yield return Key(4, 0.5f);                       // Settings
        Cap("Settings", "Tune the intensity before you go in");
        yield return Wait(1.8f);
        yield return Key(3, 1.0f); yield return Key(3, 0.7f);          // Seated mode on, off
        yield return Key(1, 0.45f); yield return Key(1, 0.45f);        // -> Snap turn
        yield return Key(4, 0.9f); yield return Key(4, 0.6f);          // on, off
        yield return Key(1, 0.5f);                                     // Camera shake
        yield return Key(2, 0.35f); yield return Key(2, 0.7f);         // 60 -> 40
        yield return Key(1, 0.5f);                                     // Tunnel vision
        yield return Key(2, 0.4f); yield return Key(3, 0.7f);
        yield return Key(1, 0.5f);                                     // Distortion
        yield return Key(2, 0.4f); yield return Key(3, 0.7f);
        yield return Key(1, 0.5f);                                     // Volume
        yield return Key(3, 0.35f); yield return Key(3, 0.7f);         // 70 -> 90
        yield return Key(1, 0.7f);                                     // Back
        yield return Key(4, 0.9f);

        // ---- briefing: choose your time ----
        Status = "briefing";
        yield return Key(4, 0.6f);                       // Play
        Cap("Set your time", "Left / Right  ·  less time, more panic");
        yield return Wait(1.8f);
        for (int i = 0; i < 4; i++) yield return Key(3, 0.24f);
        yield return Wait(0.7f);
        for (int i = 0; i < Mathf.RoundToInt(chosenLefts); i++) yield return Key(2, 0.22f);
        yield return Wait(1.6f);
        yield return Key(4, 0.2f);                       // Start

        // ---- the maze ----
        Status = "maze";
        yield return MazeRun();

        // ---- the end ----
        Cap(null);
        walking = false;
        Status = "end " + (game ? game.state.ToString() : "?");
        if (game && game.state == AnxietyGame.State.Won)
        {
            yield return Wait(6.0f);
            Cap("You made it out", "Next level  ·  less time, tighter limits");
            yield return Wait(2.6f);
            yield return Key(4, 0f);                     // Next level: reloads into the level 2 briefing
            yield return Wait(0.4f);
            Acquire();
            Cap("Level 2", "The time range tightens every level");
            yield return Wait(2.4f);
            for (int i = 0; i < 3; i++) yield return Key(3, 0.24f);
            yield return Wait(0.6f);
            for (int i = 0; i < 6; i++) yield return Key(2, 0.22f);
            yield return Wait(2.2f);
        }
        else yield return Wait(8f);
        Cap(null); overlayKeys = false;
        endCard = true; blackTarget = 1f; blackSpeed = 1f / 1.0f;
        AddCue(T + 1.0f, T + 4.4f, 1, "Mind Maze", 0.47f, 190f, MMStyle.Paper, 0.5f, 0.5f);
        AddCue(T + 1.6f, T + 4.4f, 2, "A panic simulation", 0.62f, 34f, MMStyle.Violet, 0.5f, 0.5f);
        yield return Wait(4.6f);
        EndRec();
        yield return null;
        RestoreSettings(); AnxietyGame.CapLevel = 1;
        Status = "done"; Done = true;
    }

    IEnumerator MazeRun()
    {
        Cap("The maze", "Up to walk  ·  Left / Right to turn");
        walking = true;
        yield return Walk(8f); if (!Playing()) yield break;

        Cap("Top view  ·  M", "Start, exit and where you are");
        yield return Key(7, 0f); yield return Wait(5.2f); yield return Key(7, 0.3f);
        Cap("Find the exit", "The clock never stops");
        yield return Walk(11f); if (!Playing()) yield break;

        Cap("Breathe  ·  hold Space", "Panic drops  ·  the clock keeps running");
        yield return Breathe(7f);
        Cap(null);
        yield return Walk(9f); if (!Playing()) yield break;

        yield return Key(6, 0f);                         // Esc: pause
        Cap("Pause  ·  Esc", "Stop or leave at any moment");
        yield return Wait(2.4f);
        yield return Key(1, 0.5f); yield return Key(1, 0.8f);
        yield return Key(4, 0.4f);                       // Calm exit
        Cap("Calm exit", "A guided breath, in and out");
        yield return Wait(8.2f);
        yield return Key(4, 0.4f);                       // Continue
        Cap(null);
        yield return Walk(9f); if (!Playing()) yield break;

        yield return Key(6, 0f);                         // pause again: settings mid-run
        Cap("Settings, mid-run", "Too much? Turn it down without leaving");
        yield return Wait(1.3f);
        yield return Key(1, 0.35f); yield return Key(1, 0.35f); yield return Key(1, 0.6f);
        yield return Key(4, 0.9f);                       // Settings
        yield return Key(1, 0.35f); yield return Key(1, 0.35f); yield return Key(1, 0.6f);   // Camera shake
        yield return Key(2, 0.4f); yield return Key(2, 0.9f);                                // 40 -> 20
        for (int i = 0; i < 4; i++) yield return Key(1, 0.3f);                               // Back
        yield return Key(4, 0.9f);
        yield return Key(4, 0.4f);                       // Resume
        Cap(null);
        yield return Walk(7f); if (!Playing()) yield break;

        Cap("How far is left?", "The map counts down the metres");
        yield return Key(7, 0f); yield return Wait(4.2f); yield return Key(7, 0.3f);
        Cap("Reach the light", "Before the clock runs out");
        int guard = frame + 200 * Fps;
        while (Playing() && frame < guard) yield return Walk(1f);
    }

    // ---- the "player": follows the corridors to the exit, turning and looking like a person ----
    Vector3[] corners; int ci; float repath, yawVel, remaining; Vector3 sdir; Vector3 stuckPos; float stuckT; int stuckN; float sidestep;
    public float walkSpeed = 2.5f;

    void Steer()
    {
        bool can = Playing() && !game.CapPaused && !game.CapMapOpen && !game.forceBreath && walker && walker.enabled;
        float dt = Time.deltaTime;
        if (!can || dt <= 0f) { if (walker) walker.autoMove = Vector3.zero; yawVel = 0f; return; }
        Vector3 pos = walker.transform.position;
        repath -= dt;
        if (corners == null || repath <= 0f)
        {
            var p = new NavMeshPath(); NavMeshHit h;
            Vector3 from = NavMesh.SamplePosition(pos, out h, 3f, NavMesh.AllAreas) ? h.position : pos;
            if (NavMesh.CalculatePath(from, game.CapGoal, NavMesh.AllAreas, p) && p.corners.Length > 1)
            {
                corners = p.corners; ci = 1; remaining = 0f;
                for (int i = 1; i < corners.Length; i++) remaining += Vector3.Distance(corners[i - 1], corners[i]);
            }
            repath = 0.8f;
        }
        if (corners == null) { walker.autoMove = Vector3.zero; return; }
        Vector3 to = corners[ci] - pos; to.y = 0f;
        while (to.magnitude < 0.6f && ci < corners.Length - 1) { ci++; to = corners[ci] - pos; to.y = 0f; }
        Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : walker.transform.forward;
        sdir = sdir.sqrMagnitude < 0.01f ? dir : Vector3.Slerp(sdir, dir, dt * 7f).normalized;

        // look where you're going, with a little wandering of the gaze
        float look = (Mathf.PerlinNoise(Time.time * 0.33f, 3.7f) - 0.5f) * 2f * 13f;
        float yawWant = Mathf.Atan2(sdir.x, sdir.z) * Mathf.Rad2Deg + look;
        float yaw = Mathf.SmoothDampAngle(walker.transform.eulerAngles.y, yawWant, ref yawVel, 0.3f, 170f, dt);
        walker.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        float pitchWant = Mathf.Lerp(-9f, 3f, Mathf.PerlinNoise(Time.time * 0.21f, 9.1f));
        walker.pitchOffset = Mathf.MoveTowards(walker.pitchOffset, pitchWant, dt * 26f);

        // hurry when the clock is nearly out
        float speed = walkSpeed;
        if (game.CapTimeLeft - 5f < remaining / (walkSpeed * 0.8f)) speed = 4.6f;
        float facing = Mathf.Clamp01(Vector3.Dot(walker.transform.forward, sdir));
        Vector3 move = sdir * speed * Mathf.Lerp(0.3f, 1f, facing);
        if (sidestep > 0f) { sidestep -= dt; move += Vector3.Cross(Vector3.up, sdir) * (stuckN % 2 == 0 ? 1.6f : -1.6f); }
        walker.autoMove = move;

        stuckT += dt;
        if (stuckT > 1.5f)
        {
            if ((pos - stuckPos).magnitude < 0.35f) { stuckN++; sidestep = 0.7f; corners = null; }
            else stuckN = 0;
            stuckT = 0f; stuckPos = pos;
        }
    }

    void DrawGameplayOverlay()
    {
        float s = MindMazeUI.S;
        float x = 72f * s;
        if (capA > 0.003f && capTitle != null)
        {
            MindMazeUI.Fill(new Rect(x, 52f * s, 4f * s, capSub != null ? 74f * s : 40f * s), MindMazeUI.A(MMStyle.Violet, capA));
            MindMazeUI.Tracked(new Vector2(x + 22f * s, 70f * s), capTitle.ToUpperInvariant(), MindMazeUI.Cond, 32f * s, 0.12f, MindMazeUI.A(MMStyle.Paper, capA), -1, 0.8f);
            if (capSub != null) MindMazeUI.Tracked(new Vector2(x + 22f * s, 108f * s), capSub.ToUpperInvariant(), MindMazeUI.BodyMedium, 19f * s, 0.1f, MindMazeUI.A(MMStyle.Muted, capA), -1, 0.8f);
        }
        if (!overlayKeys) return;
        // a small input display: what the player is pressing right now
        float h = 34f * s, g = 5f * s, y0 = 176f * s, cx = x + h * 0.5f;
        KeyLight(new Vector2(cx + h + g, y0), "", h, lit[0], MindMazeUI.Glyph.Up);
        KeyLight(new Vector2(cx, y0 + h + g), "", h, lit[2], MindMazeUI.Glyph.Left);
        KeyLight(new Vector2(cx + h + g, y0 + h + g), "", h, lit[1], MindMazeUI.Glyph.Down);
        KeyLight(new Vector2(cx + (h + g) * 2f, y0 + h + g), "", h, lit[3], MindMazeUI.Glyph.Right);
        float kx = x + (h + g) * 3f + 14f * s, ky = y0 + h + g;
        string[] names = { "ENTER", "SPACE", "M", "ESC" }; int[] idx = { 4, 5, 7, 6 };
        for (int i = 0; i < names.Length; i++)
        {
            float w = Mathf.Max(h, MindMazeUI.Measure(names[i], MindMazeUI.Cond, 19f * h / 44f).x + 26f * h / 44f);
            KeyLight(new Vector2(kx + w * 0.5f, ky), names[i], h, lit[idx[i]], MindMazeUI.Glyph.None);
            kx += w + g;
        }
    }

    static void KeyLight(Vector2 c, string label, float h, float l, MindMazeUI.Glyph glyph)
    {
        MindMazeUI.KeyCap(c, label, h, Mathf.Lerp(0.38f, 1f, l), glyph, l > 0.4f);
        if (l > 0.05f) MindMazeUI.Tex(new Rect(c.x - h, c.y - h, h * 2f, h * 2f), MindMazeUI.Glow, MindMazeUI.A(MMStyle.Violet, 0.35f * l));
    }

    // =====================================================================================
    //  TRAILER
    // =====================================================================================
    class Shot
    {
        public float dur = 1f, d0, d1, p0, p1, fov0 = 70f, fov1 = 70f, dread0, dread1, calm0, calm1, roll, rollK = 0.5f, h = 1.4f, bob = 1f, yawOff;
        public bool top; public float h0 = 2f, h1 = 90f;
        public float expo = 0.4f, expo1 = -99f, vigMul = 0.85f, evFloor = -0.45f;
        public int f0, f1; public bool fresh;
    }
    class Cue { public float t0, t1, y, size, fin, fout; public int kind; public string text; public Color col; }
    class Ev { public float t; public System.Action a; }

    Shot shot; float camYaw, camYawVel;
    Vignette vg; ColorAdjustments ca; Light topLight;
    readonly List<Cue> cues = new List<Cue>();
    readonly List<Ev> evs = new List<Ev>(); int evNext;
    Vector3[] pts; float[] cum; float pathLen;
    Transform lead;
    float black, blackTarget, blackSpeed = 1f;
    float master = 1f, masterTarget = 1f, masterSpeed = 1f;
    bool bars, clockOn, breatheOn, markers; float clockVal, breatheT0, markersA; int lastTick = -1;
    AudioSource sfx; AudioClip boom, thud, braam, riser, tick, whoosh;

    void AddCue(float t0, float t1, int kind, string text, float y, float size, Color col, float fin = 0.25f, float fout = 0.35f)
    {
        cues.Add(new Cue { t0 = t0, t1 = t1, kind = kind, text = text, y = y, size = size, col = col, fin = fin, fout = fout });
    }
    void At(float t, System.Action a) { evs.Add(new Ev { t = t, a = a }); }
    void FadeTo(float a, float secs) { blackTarget = a; blackSpeed = 1f / Mathf.Max(0.01f, secs); }
    void Go(Shot s) { s.f0 = frame; s.f1 = frame + Mathf.Max(1, Mathf.RoundToInt(s.dur * Fps)); s.fresh = true; shot = s; }
    void Think(string t) { if (writer) writer.Write(t); }
    void Whisper(int i) { if (whispers) whispers.Say(i, 0.55f, 0f); }
    void Drop(int n) { if (blocks) for (int i = 0; i < n; i++) blocks.SendMessage("Spawn", 1f, SendMessageOptions.DontRequireReceiver); }
    void Sfx(AudioClip c, float v) { if (sfx && c) sfx.PlayOneShot(c, v); }
    void Card(float t, string text, float dur, float vol)
    {
        At(t, () => { black = 1f; blackTarget = 1f; Sfx(boom, vol); });
        AddCue(t, t + dur, 1, text, 0.5f, 132f, MMStyle.Paper, 0.05f, 0.08f);
    }

    void BuildPath()
    {
        var p = new NavMeshPath(); NavMeshHit h;
        Vector3 a = walker.transform.position;
        if (NavMesh.SamplePosition(a, out h, 5f, NavMesh.AllAreas)) a = h.position;
        NavMesh.CalculatePath(a, game.CapGoal, NavMesh.AllAreas, p);
        pts = p.corners;
        if (pts == null || pts.Length < 2) pts = new[] { a, game.CapGoal };
        cum = new float[pts.Length];
        for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        pathLen = cum[pts.Length - 1];
    }

IEnumerator ProbeRun(float L)
    {
        bars = false; black = 0f; blackTarget = 0f;
        string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Recordings", "_probe");
        System.IO.Directory.CreateDirectory(dir);
        Time.captureFramerate = Fps;
        for (float d = 0f; d < L; d += 4f)
        {
            Go(new Shot { dur = 1f, d0 = d, d1 = d + 0.01f, p0 = -6f, p1 = -6f, fov0 = 74f, fov1 = 74f, dread0 = 0.3f, dread1 = 0.3f, bob = 0f, rollK = 0f });
            for (int i = 0; i < 5; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "d_" + Mathf.RoundToInt(d).ToString("000") + ".jpg"), tex.EncodeToJPG(80));
            Destroy(tex);
        }
        Time.captureFramerate = 0;
        Status = "done L=" + L.ToString("0.0"); Done = true;
    }

    Vector3 P(float d)
    {
        d = Mathf.Clamp(d, 0f, pathLen);
        for (int i = 1; i < pts.Length; i++)
            if (d <= cum[i]) { float seg = cum[i] - cum[i - 1]; return Vector3.Lerp(pts[i - 1], pts[i], seg < 0.0001f ? 0f : (d - cum[i - 1]) / seg); }
        return pts[pts.Length - 1];
    }
    Vector3 PS(float d) { return (P(d - 0.8f) + P(d) * 2f + P(d + 0.8f)) * 0.25f; }

    void ApplyShot()
    {
        if (shot == null || !cam || !walker) return;
        float u = Mathf.Clamp01((frame - shot.f0) / (float)Mathf.Max(1, shot.f1 - shot.f0));
        float e = u * u * (3f - 2f * u);
        director.externalDread = Mathf.Lerp(shot.dread0, shot.dread1, u);
        director.calm = Mathf.Lerp(shot.calm0, shot.calm1, u);
        float fovOffset = (cam.fieldOfView - director.baseFov);
        // grade: keep blinks and panic from going fully black on video, lift the exposure a little
        float ev = shot.expo1 > -90f ? Mathf.Lerp(shot.expo, shot.expo1, e) : shot.expo;
        if (ca != null) ca.postExposure.Override(Mathf.Max(ca.postExposure.value, director.exposure + shot.evFloor) + ev);
        if (vg != null) vg.intensity.Override(vg.intensity.value * shot.vigMul);
        if (topLight) { topLight.enabled = shot.top; topLight.intensity = shot.top ? 2.2f * e : 0f; }
        if (shot.top)
        {
            Vector3 b = P(shot.d0);
            Vector3 centre = new Vector3(-31f, 0f, -4f);
            float uu = Mathf.Clamp01(u / 0.72f); e = uu * uu * (3f - 2f * uu);          // arrive early, then hold with a slow push in
            Vector3 xz = Vector3.Lerp(new Vector3(b.x, 0f, b.z), centre, e);
            cam.transform.position = xz + Vector3.up * (Mathf.Lerp(shot.h0, shot.h1, e * e) - 5f * Mathf.Clamp01((u - 0.72f) / 0.28f));
            cam.transform.rotation = Quaternion.Euler(Mathf.Lerp(shot.p0, shot.p1, Mathf.Sqrt(e)), Mathf.LerpAngle(camYaw, 0f, e), 0f);
            cam.fieldOfView = Mathf.Lerp(shot.fov0, shot.fov1, e);
            cam.farClipPlane = 600f;
            RenderSettings.fogStartDistance = Mathf.Lerp(6f, 400f, e); RenderSettings.fogEndDistance = Mathf.Lerp(80f, 900f, e);
            if (lead) lead.position = b;
            return;
        }
        float d = Mathf.Lerp(shot.d0, shot.d1, u);
        Vector3 p = PS(d), ahead = PS(d + 2.6f);
        Vector3 dir = ahead - p; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = walker.transform.forward;
        float yawWant = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + shot.yawOff;
        if (shot.fresh) { camYaw = yawWant; camYawVel = 0f; shot.fresh = false; }
        else camYaw = Mathf.SmoothDampAngle(camYaw, yawWant, ref camYawVel, 0.3f, 400f, Dt);
        walker.transform.position = p;
        walker.transform.rotation = Quaternion.Euler(0f, camYaw, 0f);
        float step = Mathf.Abs(Mathf.Sin(d * Mathf.PI / 0.8f));
        cam.transform.localPosition = new Vector3(Mathf.Sin(d * Mathf.PI / 1.6f) * 0.02f * shot.bob, shot.h + step * 0.035f * shot.bob, 0f);
        Vector3 sh = walker.shakeEuler;
        cam.transform.localRotation = Quaternion.Euler(Mathf.Lerp(shot.p0, shot.p1, e) + sh.x, sh.y, shot.roll + walker.extraRoll * shot.rollK + sh.z);
        cam.fieldOfView = Mathf.Lerp(shot.fov0, shot.fov1, e) + fovOffset;
        cam.farClipPlane = 150f;
        if (lead) { lead.position = PS(d + 9f + (shot.d1 - shot.d0) / Mathf.Max(0.1f, shot.dur) * 1.4f); lead.rotation = walker.transform.rotation; }
    }

IEnumerator Trailer()
    {
        DefaultSettings(100);
        MMSettings.Tunnel = 35;
        AnxietyGame.CapHideUI = true; AnxietyGame.CapLevel = 1;
        yield return Reload();
        AnxietyGame.CapHideUI = true;
        walker.enabled = false; if (cc) cc.enabled = false;
        director.panicVision = 0.05f;
        BuildPath(); BuildSfx();
        // the outer walls are far taller than the maze; for the view from above bring them down to maze height (this run only)
        foreach (string en in new[] { "EdgeWall_N", "EdgeWall_S", "EdgeWall_W", "EdgeWall_E" })
        {
            var eg = GameObject.Find(en); var er = eg ? eg.GetComponent<Renderer>() : null;
            if (!er || er.bounds.size.y < 13f) continue;
            var sc = eg.transform.localScale; sc.y *= 12f / er.bounds.size.y; eg.transform.localScale = sc;
            eg.transform.position += Vector3.up * (6f - eg.transform.position.y);
        }
        lead = new GameObject("MMLead").transform;
        if (director.volume != null) { director.volume.profile.TryGet(out vg); director.volume.profile.TryGet(out ca); }
        var tl = new GameObject("MMTopLight"); tl.transform.rotation = Quaternion.Euler(78f, 20f, 0f);
        topLight = tl.AddComponent<Light>(); topLight.type = LightType.Directional; topLight.shadows = LightShadows.Soft; topLight.enabled = false;
        if (blocks) { blocks.player = lead; blocks.spawning = false; }
        if (whispers) whispers.active = false;
        if (writer) { writer.maxAtOnce = 5; writer.life = 3.2f; }
        bars = true; black = 1f; blackTarget = 1f; master = 1f; masterTarget = 1f;
        float L = pathLen;
        if (Probe) { yield return ProbeRun(L); yield break; }
        Go(new Shot { dur = 3.4f, d0 = 12f, d1 = 12f, p0 = -36f, p1 = -36f, fov0 = 60f, fov1 = 60f, dread0 = 0.05f, dread1 = 0.1f, bob = 0f });
        yield return null;
        BeginRec();
        yield return null;
        f0 = frame;
        Status = "trailer";

        // --- 1. one thought ---
        AddCue(0.7f, 3.3f, 0, "It starts with one thought.", 0.5f, 56f, MMStyle.Paper, 0.2f, 0.5f);
        At(3.4f, () => { Go(new Shot { dur = 5.6f, d0 = 12f, d1 = 17.5f, p0 = -36f, p1 = -3f, fov0 = 60f, fov1 = 66f, dread0 = 0.1f, dread1 = 0.3f, bob = 0.5f, rollK = 0.3f, expo = 0.5f }); FadeTo(0f, 1.8f); });
        At(5.2f, () => { Whisper(0); Think("you're late"); });
        At(7.0f, () => { Whisper(1); Think("everyone is waiting"); });

        // --- 2. then another ---
        Card(9.0f, "Then another.", 0.9f, 1f);
        At(9.9f, () => { black = 0f; blackTarget = 0f; if (whispers) whispers.active = true; Go(new Shot { dur = 5.1f, d0 = 24f, d1 = 38f, p0 = -8f, p1 = 2f, fov0 = 74f, fov1 = 78f, dread0 = 0.45f, dread1 = 0.62f, bob = 1f, rollK = 0.6f, expo = 0.5f }); });
        string[] th = { "you can't do this", "why is it so hard", "they're all watching", "not good enough", "what if you don't make it", "it's too much" };
        for (int i = 0; i < th.Length; i++) { string line = th[i]; int k = i; At(10.1f + i * 0.8f, () => { Think(line); if (k % 2 == 0 && whispers) whispers.Burst(1); }); }

        // --- 3. and another: the clock, the blocks ---
        Card(15.0f, "And another.", 0.8f, 1f);
        At(15.8f, () => { black = 0f; blackTarget = 0f; if (blocks) blocks.spawning = true; clockOn = true; clockVal = 12.6f; Go(new Shot { dur = 6.8f, d0 = 50f, d1 = 73f, p0 = -5f, p1 = 3f, fov0 = 82f, fov1 = 88f, dread0 = 0.68f, dread1 = 0.78f, bob = 1.3f, rollK = 1f, expo = 0.9f, vigMul = 0.7f }); });
        float[] drops = { 15.85f, 16.4f, 17.0f, 17.7f, 18.4f, 19.1f, 19.8f, 20.5f, 21.2f };
        foreach (float dt in drops) At(dt, () => Drop(1));
        AddCue(16.5f, 18.7f, 2, "The clock won't wait.", 0.80f, 42f, MMStyle.Violet);
        AddCue(19.5f, 22.0f, 2, "The blocks keep falling.", 0.80f, 42f, MMStyle.Violet);

        // --- 4. the whole maze from above ---
        At(22.6f, () => { clockOn = false; if (blocks) blocks.spawning = false; Sfx(whoosh, 0.9f); Go(new Shot { top = true, dur = 6.2f, d0 = 73f, h0 = 1.6f, h1 = 86f, p0 = 15f, p1 = 90f, fov0 = 72f, fov1 = 56f, dread0 = 0.5f, dread1 = 0.3f, expo = 0.5f, expo1 = 1.05f, vigMul = 0.4f }); });
        At(25.6f, () => { markers = true; });
        Card(28.8f, "Find the exit.", 1.0f, 1f);

        // --- 5. one thing slows it down ---
        At(28.8f, () => { markers = false; markersA = 0f; Go(new Shot { dur = 8.1f, d0 = 84f, d1 = 86.6f, p0 = -8f, p1 = -20f, fov0 = 74f, fov1 = 64f, dread0 = 0.85f, dread1 = 0.2f, calm0 = 0f, calm1 = 1f, bob = 0.3f, rollK = 0.3f, expo = 0.6f }); });
        At(29.8f, () => { black = 0f; blackTarget = 0f; if (whispers) whispers.active = false; });
        At(30.2f, () => { breatheOn = true; breatheT0 = T; });
        AddCue(30.6f, 33.2f, 2, "One thing slows it down.", 0.80f, 42f, MMStyle.Violet);
        AddCue(33.8f, 36.7f, 2, "The clock keeps running.", 0.80f, 42f, MMStyle.Violet);
        At(36.9f, () => { breatheOn = false; });

        // --- 6. crescendo ---
        float[] ct = { 36.9f, 38.4f, 39.8f, 41.0f, 42.0f, 42.9f, 43.7f, 44.4f, 45.0f, 45.5f, 46.0f, 46.4f, 46.8f, 47.1f, 47.4f };
        float[] cd = { 92f, 30f, 62f, 100f, 44f, 104f, 68f, 20f, 96f, 56f, 36f, 24f, 84f, 116f };
        float[] cp = { -4f, -24f, 2f, -8f, 6f, -16f, 0f, -28f, 4f, -10f, 8f, -20f, 0f, -6f };
        string[] words = { "", "", "", "Hurry", "Faster", "Not good enough", "No time", "You're late", "Try harder", "Everyone is waiting", "It's closing in", "You'll never get out", "Don't stop", "No time" };
        for (int i = 0; i < cd.Length; i++)
        {
            int k = i; float t0 = ct[i], dur = ct[i + 1] - ct[i];
            At(t0, () =>
            {
                if (k == 0) { if (whispers) whispers.active = true; if (blocks) blocks.spawning = true; }
                Go(new Shot { dur = dur, d0 = cd[k], d1 = Mathf.Min(L - 2f, cd[k] + dur * 4.4f), p0 = cp[k], p1 = cp[k] + 3f, fov0 = 86f + (k % 3) * 4f, fov1 = 90f + (k % 3) * 4f, dread0 = 0.8f, dread1 = 0.8f, bob = 1.6f, rollK = 1.2f, roll = (k % 2 == 0 ? 1f : -1f) * (2f + k * 0.5f), expo = 1.0f, vigMul = 0.65f, evFloor = -0.3f });
                if (k > 0) { director.AddShock(0.3f, 0.45f); Sfx(thud, 0.55f + k * 0.03f); Drop(1); if (whispers) whispers.Burst(1); }
            });
            if (words[i].Length > 0) AddCue(t0, t0 + Mathf.Min(0.14f, dur * 0.5f), 4, words[i], 0.5f, 150f, MMStyle.Paper, 0.01f, 0.01f);
        }
        At(41.4f, () => { Sfx(riser, 0.9f); clockOn = true; clockVal = 6f; });
        AddCue(44.4f, 47.4f, 5, "Can't breathe", 0.76f, 50f, MMStyle.Violet, 0.05f, 0.01f);

        // --- 7. silence ---
        At(47.4f, () =>
        {
            black = 1f; blackTarget = 1f; master = 0f; masterTarget = 0f; clockOn = false;
            if (blocks) blocks.spawning = false; if (whispers) whispers.active = false;
            foreach (var b in FindObjectsByType<BlockImpact>(FindObjectsSortMode.None)) Destroy(b.gameObject);
            Go(new Shot { dur = 6f, d0 = L - 13f, d1 = L - 13f, p0 = -8f, p1 = -8f, fov0 = 64f, fov1 = 64f, dread0 = 0f, dread1 = 0f, calm0 = 1f, calm1 = 1f, bob = 0f, rollK = 0f });
        });
        At(48.8f, () => { masterTarget = 0.7f; masterSpeed = 0.5f; });
        AddCue(49.0f, 53.1f, 2, "For many, this is every day.", 0.5f, 52f, MMStyle.Violet, 0.9f, 0.5f);

        // --- 8. title ---
        At(53.6f, () => { master = 1f; masterTarget = 1f; Sfx(braam, 1f); FadeTo(0.58f, 2.2f); Go(new Shot { dur = 10.4f, d0 = L - 13f, d1 = L - 3.5f, p0 = -8f, p1 = -17f, fov0 = 66f, fov1 = 60f, dread0 = 0.25f, dread1 = 0.15f, calm0 = 0.5f, calm1 = 0.8f, bob = 0.4f, rollK = 0.2f, expo = 0.6f }); });
        AddCue(53.6f, 62.8f, 1, "Mind", 0.325f, 205f, MMStyle.Paper, 0.06f, 0.6f);
        AddCue(53.6f, 62.8f, 1, "Maze", 0.495f, 205f, MMStyle.Paper, 0.06f, 0.6f);
        AddCue(55.8f, 62.8f, 2, "A panic simulation", 0.675f, 48f, MMStyle.Violet, 0.6f, 0.6f);
        AddCue(57.4f, 62.8f, 3, "PC  ·  VR", 0.755f, 34f, MMStyle.Paper, 0.6f, 0.6f);
        AddCue(58.8f, 62.8f, 3, "From the film JENGA", 0.825f, 28f, MMStyle.Muted, 0.6f, 0.6f);
        At(62.2f, () => { FadeTo(1f, 1.2f); masterTarget = 0f; masterSpeed = 0.7f; });

        evs.Sort((x, y) => x.t.CompareTo(y.t));
        while (T < 64.2f) yield return null;
        EndRec();
        yield return null;
        AnxietyGame.CapHideUI = false; RestoreSettings(); AudioListener.volume = MMSettings.Volume / 100f;
        Status = "done"; Done = true;
    }

    // ---------- trailer sound design (all synthesised) ----------
    static AudioClip Synth(string name, float secs, System.Func<float, float> f)
    {
        const int sr = 44100; int n = Mathf.CeilToInt(sr * secs);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)sr), -1f, 1f);
        int fade = Mathf.Min(n, 1200);
        for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;
        var c = AudioClip.Create(name, n, 1, sr, false); c.SetData(data, 0); return c;
    }
    static float Tanh(float x) { return (float)System.Math.Tanh(x); }
    static float Saw(double ph) { float v = 0f; for (int k = 1; k <= 8; k++) v += Mathf.Sin((float)(ph * k)) / k; return v; }

    void BuildSfx()
    {
        sfx = gameObject.AddComponent<AudioSource>(); sfx.spatialBlend = 0f; sfx.playOnAwake = false; sfx.ignoreListenerPause = true;
        const float sr = 44100f; const double tau = System.Math.PI * 2.0;
        var rnd = new System.Random(11);
        {
            double ph = 0; float lp = 0f;
            boom = Synth("mm_boom", 2.6f, t =>
            {
                ph += tau * (30f + 62f * Mathf.Exp(-t * 10f)) / sr;
                float n = (float)(rnd.NextDouble() * 2 - 1); lp += (n - lp) * 0.16f;
                return Tanh((Mathf.Sin((float)ph) * Mathf.Exp(-t * 1.7f) + lp * Mathf.Exp(-t * 13f) * 0.9f) * 2.3f) * 0.85f;
            });
        }
        {
            double ph = 0; float lp = 0f;
            thud = Synth("mm_thud", 0.7f, t =>
            {
                ph += tau * (44f + 90f * Mathf.Exp(-t * 16f)) / sr;
                float n = (float)(rnd.NextDouble() * 2 - 1); lp += (n - lp) * 0.3f;
                return Tanh((Mathf.Sin((float)ph) * Mathf.Exp(-t * 6f) + lp * Mathf.Exp(-t * 26f)) * 2.2f) * 0.8f;
            });
        }
        {
            double a = 0, b = 0, c = 0, d = 0, e = 0;
            braam = Synth("mm_braam", 4.2f, t =>
            {
                a += tau * 55.0 / sr; b += tau * 55.45 / sr; c += tau * 82.5 / sr; d += tau * 110.4 / sr; e += tau * 27.5 / sr;
                float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t * 0.85f) * (1f + 0.08f * Mathf.Sin(t * 34f));
                float v = (Saw(a) + Saw(b) + 0.6f * Saw(c) + 0.35f * Saw(d)) * 0.32f + Mathf.Sin((float)e) * 0.6f;
                return Tanh(v * env * 2.6f) * 0.85f;
            });
        }
        {
            double ph = 0; float lp = 0f;
            riser = Synth("mm_riser", 6f, t =>
            {
                float u = t / 6f, g = u * u;
                ph += tau * (70f * Mathf.Pow(18f, u)) / sr;
                float n = (float)(rnd.NextDouble() * 2 - 1); lp += (n - lp) * Mathf.Lerp(0.02f, 0.55f, g);
                return Tanh((lp * 1.5f + Mathf.Sin((float)ph) * 0.3f + Mathf.Sin((float)(ph * 1.5)) * 0.16f) * g * 1.6f) * 0.8f;
            });
        }
        tick = Synth("mm_tick", 0.09f, t => (Mathf.Sin(t * 2f * Mathf.PI * 2300f) * 0.6f + (t < 0.002f ? 0.8f : 0f)) * Mathf.Exp(-t * 75f));
        {
            float lp = 0f;
            whoosh = Synth("mm_whoosh", 1.1f, t =>
            {
                float u = t / 1.1f; float n = (float)(rnd.NextDouble() * 2 - 1);
                lp += (n - lp) * Mathf.Lerp(0.03f, 0.4f, u);
                return lp * Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2.5f) * 2.2f;
            });
        }
    }

    // =====================================================================================
    //  per frame
    // =====================================================================================
    void LateUpdate()
    {
        frame++;
        if (mode == Mode.Gameplay)
        {
            if (walking) Steer();
            if (walker && Playing() && !game.CapPaused && !game.CapMapOpen)
            {
                if (walker.currentSpeed > 0.3f) lit[0] = 1f;
                if (yawVel > 28f) lit[3] = 1f; else if (yawVel < -28f) lit[2] = 1f;
            }
            if (game && game.forceBreath) lit[5] = 1f;
        }
        else
        {
            if (!Done) AnxietyGame.CapHideUI = true;
            while (evNext < evs.Count && evs[evNext].t <= T) { evs[evNext].a(); evNext++; }
            ApplyShot();
            master = Mathf.MoveTowards(master, masterTarget, Dt * masterSpeed);
            AudioListener.volume = master;
            if (clockOn)
            {
                clockVal = Mathf.Max(0f, clockVal - Dt);
                int sec = Mathf.CeilToInt(clockVal);
                if (sec != lastTick) { lastTick = sec; Sfx(tick, 0.8f); }
            }
            markersA = Mathf.MoveTowards(markersA, markers ? 1f : 0f, Dt * 1.5f);
        }
        for (int i = 0; i < lit.Length; i++) lit[i] = Mathf.MoveTowards(lit[i], 0f, Dt / 0.3f);
        capA = Mathf.MoveTowards(capA, capOn ? 1f : 0f, Dt / 0.3f);
        black = Mathf.MoveTowards(black, blackTarget, Dt * blackSpeed);
        if (!Done) Status = mode + " " + T.ToString("0.0") + "s";
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        MindMazeUI.Init();
        GUI.depth = -100;
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S, t = T;
        if (mode == Mode.Gameplay && !endCard) DrawGameplayOverlay();

        if (markersA > 0.003f && cam && pts != null) { Marker(pts[0], "START", MMStyle.Paper, false); Marker(pts[pts.Length - 1], "EXIT", MMStyle.Violet, true); }
        if (breatheOn) DrawBreath(t - breatheT0);
        if (clockOn)
        {
            float pulse = Mathf.Abs(Mathf.Sin(t * 6f));
            Vector2 j = Random.insideUnitCircle * 3f * s;
            MindMazeUI.Tex(new Rect(W * 0.5f - 300f * s, 120f * s, 600f * s, 300f * s), MindMazeUI.Glow, new Color(0, 0, 0, 0.55f));
            MindMazeUI.Clock(new Vector2(W * 0.5f + j.x, 262f * s + j.y), Clk(clockVal), MindMazeUI.Cond, 170f * s, Color.Lerp(MMStyle.Paper, MMStyle.Violet, 0.3f + 0.7f * pulse));
        }
        if (black > 0.003f) MindMazeUI.FillScreen(new Color(0, 0, 0, black));
        foreach (var c in cues) if (t >= c.t0 && t < c.t1) DrawCue(c, t);
        if (bars) { float b = 104f * s; MindMazeUI.Fill(new Rect(0, 0, W, b), Color.black); MindMazeUI.Fill(new Rect(0, H - b, W, b), Color.black); }
    }

    static string Clk(float v) { int x = Mathf.Max(0, Mathf.CeilToInt(v)); return (x / 60).ToString("00") + ":" + (x % 60).ToString("00"); }

    void DrawCue(Cue c, float t)
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        float a = Mathf.Clamp01((t - c.t0) / Mathf.Max(0.001f, c.fin)) * Mathf.Clamp01((c.t1 - t) / Mathf.Max(0.001f, c.fout));
        Vector2 ctr = new Vector2(W * 0.5f, H * c.y);
        switch (c.kind)
        {
            case 0:
                {
                    float fs = c.size * s;
                    int chars = Mathf.Clamp(Mathf.FloorToInt((t - c.t0) * 20f), 0, c.text.Length);
                    if (chars <= 0) break;
                    float full = MindMazeUI.Measure(c.text, MindMazeUI.Italic, fs).x;
                    MindMazeUI.Label(new Rect(W * 0.5f - full * 0.5f, ctr.y - fs, full + 40f, fs * 2f), c.text.Substring(0, chars), MindMazeUI.Italic, fs, MindMazeUI.A(c.col, 0.92f * Mathf.Clamp01((c.t1 - t) / c.fout)), TextAnchor.MiddleLeft);
                    break;
                }
            case 1:
                {
                    float k = Mathf.Clamp01((t - c.t0) / 0.16f); k = 1f - (1f - k) * (1f - k) * (1f - k);
                    float sc = Mathf.Lerp(1.2f, 1f, k) * (1f + 0.04f * (t - c.t0) / Mathf.Max(0.1f, c.t1 - c.t0));
                    MMStyle.Stencil(ctr, c.text, c.size * s * sc, a);
                    break;
                }
            case 2: MMStyle.Line(ctr, c.text, c.size * s, c.col, a); break;
            case 3: MMStyle.Line(ctr, c.text, c.size * s, c.col, a, MindMazeUI.BodyMedium); break;
            case 4:
                {
                    Vector2 j = Random.insideUnitCircle * 10f * s;
                    float size = Mathf.Min(c.size * s, W * 0.86f / Mathf.Max(1, c.text.Length) * 2.1f);
                    MMStyle.Stencil(ctr + j, c.text, size, 1f);
                    break;
                }
            case 5:
                {
                    Vector2 j = Random.insideUnitCircle * 4f * s;
                    float k = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 9f));
                    MindMazeUI.Tracked(ctr + j, c.text.ToUpperInvariant(), MindMazeUI.Cond, c.size * s, 0.12f, MindMazeUI.A(Color.Lerp(MMStyle.Paper, c.col, 0.6f), k * a), 0, 0.6f);
                    break;
                }
        }
    }

    void DrawBreath(float bt)
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        float a = Mathf.Clamp01(bt / 0.8f) * Mathf.Clamp01((6.4f - bt) / 0.5f);
        float p = Mathf.Clamp01(bt / 6.4f);
        bool inhale = p < 0.5f;
        float q = inhale ? p * 2f : (p - 0.5f) * 2f;
        float e = 1f - (1f - q) * (1f - q) * (1f - q);
        float k = inhale ? e : 1f - e;
        float rr = Mathf.Lerp(150f, 235f, k) * s;
        Vector2 c = new Vector2(W * 0.5f, H * 0.5f);
        MindMazeUI.Tex(new Rect(c.x - rr * 2f, c.y - rr * 2f, rr * 4f, rr * 4f), MindMazeUI.Glow, MindMazeUI.A(MMStyle.Violet, 0.3f * a));
        MindMazeUI.Tex(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), MindMazeUI.Ring, MindMazeUI.A(MMStyle.Paper, 0.9f * a));
        MindMazeUI.Tracked(c, inhale ? "BREATHE IN" : "BREATHE OUT", MindMazeUI.Cond, 34f * s, 0.14f, MindMazeUI.A(MMStyle.Paper, a), 0, 0.7f);
    }

    void Marker(Vector3 world, string label, Color col, bool glow)
    {
        float H = MindMazeUI.H, s = MindMazeUI.S;
        Vector3 sp = cam.WorldToScreenPoint(world + Vector3.up * 12f);
        if (sp.z <= 0f) return;
        Vector2 c = new Vector2(sp.x, H - sp.y);
        float r = (glow ? 34f + 7f * Mathf.Sin(T * 4f) : 30f) * s;
        if (glow) MindMazeUI.Tex(new Rect(c.x - r * 3f, c.y - r * 3f, r * 6f, r * 6f), MindMazeUI.Glow, MindMazeUI.A(col, 0.75f * markersA));
        MindMazeUI.Tex(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), MindMazeUI.Ring, MindMazeUI.A(col, markersA));
        float w = MindMazeUI.TrackedWidth(label, MindMazeUI.Cond, 30f * s, 0.16f);
        MindMazeUI.Fill(new Rect(c.x - w * 0.5f - 14f * s, c.y - r - 58f * s, w + 28f * s, 44f * s), new Color(0, 0, 0, 0.7f * markersA));
        MindMazeUI.Tracked(new Vector2(c.x, c.y - r - 36f * s), label, MindMazeUI.Cond, 30f * s, 0.16f, MindMazeUI.A(col, markersA), 0, 0f);
    }
}
