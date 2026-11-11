using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// JENGA - an anxiety simulator.
// Reach the DEADLINE (the pale beam of light) before the clock runs out.
// The clock, falling blocks, whispers and the maze push anxiety up; anxiety slows you,
// narrows your vision and can tip into a panic attack. Holding E / right mouse lets you
// stop and breathe: it calms you, but the clock keeps running.
// Just as you get close, the deadline slips away once - you tried so hard, and it moved.
public partial class AnxietyGame : MonoBehaviour
{
    public enum State { Intro, Playing, Won, Lost }
    public State state = State.Intro;

    [Header("References")]
    public MazeWalker walker;
    public AnxietyDirector director;
    public FallingBlocks blocks;
    public WhisperSystem whispers;
    public Material beamMaterial;

    [Header("Rules")]
    [Tooltip("0 = work it out from the maze (tight but fair).")]
    public float timeLimit = 0f;
    public float hitPenalty = 8f;
    public int goalSlips = 1;
    public float slipDistance = 14f;
    public float reachDistance = 2.2f;
    public float difficulty = 1f;   // >1 = less time
    [Tooltip("The whole run is one long panic attack.")]
    public bool fullPanic = false;
    [Tooltip("Write intrusive thoughts on walls / falling blocks instead of on the screen.")]
    public bool thoughtsOnWalls = true;
    public ThoughtWriter writer;
    [HideInInspector] public bool forceBreath;   // used by GameplayRecorder to hold a breath
    [Tooltip("Average speed assumed when working out the time limit (m/s).")]
    public float assumedSpeed = 1.9f;

    float timeLeft, totalTime;
    Vector3 startPos, goalA, goalB, goal;
    int slipsLeft;
    Transform beacon;
    Light beaconLight;
    float beaconFlicker;
    AudioSource beaconHum;
    float breathingCalm;
    bool breathing;
    float runTime;
    float endFade;
    float penaltyFlash;
    string penaltyText = "";
    float banner; string bannerText = "";
    float introTime;
    const float IntroReady = 8f;

    class Thought { public string text; public Vector2 pos; public float born, life; public int size; }
    readonly List<Thought> thoughts = new List<Thought>();

    void Start()
    {
        if (walker == null) walker = FindFirstObjectByType<MazeWalker>();
        if (director == null) director = FindFirstObjectByType<AnxietyDirector>();
        if (blocks == null) blocks = FindFirstObjectByType<FallingBlocks>();
        if (whispers == null) whispers = FindFirstObjectByType<WhisperSystem>();
        if (whispers) whispers.onWhisper += ShowThought;
        FallingBlocks.OnPlayerHit += OnHit;

        startPos = walker.transform.position;
        difficulty *= 1f + 0.3f * (level - 1); PlanGoals(); SetupTimeRange();
        slipsLeft = goalSlips;
        goal = goalA;
        BuildBeacon();

        walker.showHelp = false;
        walker.frozen = true;
        if (blocks) blocks.spawning = false;
        if (whispers) whispers.active = false;
        director.externalDread = 0f; autoBegin = skipIntro; skipIntro = false; vrBase = director.vrComfort || UnityEngine.XR.XRSettings.isDeviceActive; MMVRRig.Boot(); Time.timeScale = 1f; AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void OnDestroy()
    {
        FallingBlocks.OnPlayerHit -= OnHit;
        if (whispers) whispers.onWhisper -= ShowThought;
    }

    // ---------- planning the route ----------
    static float PathLength(Vector3 a, Vector3 b)
    {
        var p = new NavMeshPath();
        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, p) || p.status != NavMeshPathStatus.PathComplete) return -1f;
        float L = 0f; for (int i = 1; i < p.corners.Length; i++) L += Vector3.Distance(p.corners[i - 1], p.corners[i]);
        return L;
    }

    // Dense playzone bounds — keeps goals inside the tight centre of the maze
    const float ZoneX0 = -59f, ZoneX1 = 5f, ZoneZ0 = -41f, ZoneZ1 = 5f;
    static bool InCorridor(Vector3 p)
    {
        // Must be inside the dense zone
        if (p.x < ZoneX0 || p.x > ZoneX1 || p.z < ZoneZ0 || p.z > ZoneZ1) return false;
        Vector3 o = p + Vector3.up; int c = 0;
        if (Physics.Raycast(o, Vector3.forward, 3.5f)) c++;
        if (Physics.Raycast(o, Vector3.back, 3.5f)) c++;
        if (Physics.Raycast(o, Vector3.left, 3.5f)) c++;
        if (Physics.Raycast(o, Vector3.right, 3.5f)) c++;
        return c >= 2;
    }

    void PlanGoals()
    {
        NavMeshHit h;
        if (NavMesh.SamplePosition(startPos, out h, 5f, NavMesh.AllAreas)) startPos = h.position;
        // fixed layout: START at one end of the maze, EXIT at the other (markers placed in the scene)
        var mStart = GameObject.Find("MM_Start"); var mExit = GameObject.Find("MM_Exit");
        if (mStart && mExit)
        {
            if (NavMesh.SamplePosition(mStart.transform.position, out h, 5f, NavMesh.AllAreas)) startPos = h.position;
            goalA = NavMesh.SamplePosition(mExit.transform.position, out h, 5f, NavMesh.AllAreas) ? h.position : mExit.transform.position;
            goalB = goalA; goalSlips = 0;
            float fixedLen = PathLength(startPos, goalA);
            if (timeLimit <= 0f)
            {
                float t0 = Mathf.Max(fixedLen, 60f) / assumedSpeed * 1.5f + 20f;
                timeLimit = Mathf.Clamp(t0 / Mathf.Max(0.5f, difficulty), 90f, 360f);
            }
            totalTime = timeLeft = timeLimit;
            return;
        }
        var tri = NavMesh.CalculateTriangulation();
        float bestA = -1f; goalA = startPos;
        float lenA = 0f, lenB = 0f;
        // goal A: far-ish through the corridors, but not absurdly far
        for (int i = 0; i < 250; i++)
        {
            Vector3 p = tri.vertices[Random.Range(0, tri.vertices.Length)];
            if (!InCorridor(p)) continue;
            float L = PathLength(startPos, p);
            if (L < 0f) continue;
            float score = -Mathf.Abs(L - 110f);           // aim for ~110 m of walking
            if (score > bestA) { bestA = score; goalA = p; lenA = L; }
        }
        // goal B: where the deadline slips to - further on, 40-70 m from A
        float bestB = -1e9f; goalB = goalA;
        for (int i = 0; i < 250; i++)
        {
            Vector3 p = tri.vertices[Random.Range(0, tri.vertices.Length)];
            if (!InCorridor(p)) continue;
            float L = PathLength(goalA, p);
            if (L < 0f) continue;
            float score = -Mathf.Abs(L - 55f) + Vector3.Distance(p, startPos) * 0.2f;
            if (score > bestB) { bestB = score; goalB = p; lenB = L; }
        }
        if (timeLimit <= 0f)
        {
            // walking ~2.4 m/s when anxious, people get lost (x1.7), plus some slack
            float total = (lenA + (goalSlips > 0 ? lenB : 0f)) / assumedSpeed * 1.6f + 25f;
            timeLimit = Mathf.Clamp(total / Mathf.Max(0.5f, difficulty), 90f, 360f);
        }
        totalTime = timeLeft = timeLimit;
    }

    void BuildBeacon()
    {
        var root = new GameObject("DEADLINE");
        beacon = root.transform;
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(beacon, false);
        beam.transform.localScale = new Vector3(0.25f, 45f, 0.25f);
        beam.transform.localPosition = new Vector3(0, 45f, 0);
        var r = beam.GetComponent<Renderer>();
        if (beamMaterial) r.sharedMaterial = beamMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lg = new GameObject("glow"); lg.transform.SetParent(beacon, false); lg.transform.localPosition = Vector3.up * 2f;
        beaconLight = lg.AddComponent<Light>(); beaconLight.type = LightType.Point; beaconLight.range = 10f; beaconLight.intensity = 6f;
        beacon.position = goal;
        // Audio beacon: soft hum that gets louder as player approaches the exit
        var ha = new GameObject("hum"); ha.transform.SetParent(beacon, false);
        beaconHum = ha.AddComponent<AudioSource>();
        beaconHum.loop = true; beaconHum.spatialBlend = 0f;
        beaconHum.volume = 0f;
        ha.AddComponent<BeaconHumSynth>();
        beaconHum.Play();
    }

    // ---------- events ----------
    void OnHit()
    {
        if (state != State.Playing) return;
        timeLeft -= hitPenalty;
        hits++;
        penaltyFlash = 1.5f; penaltyText = "-" + hitPenalty.ToString("0") + "s";
        if (director) director.AddShock(0.3f, 0.55f);
        if (whispers) whispers.Burst(2);
    }

    void ShowThought(int idx)
    {
        if (state != State.Playing || idx < 0 || idx >= WhisperSystem.Lines.Length) return;
        // fewer words than voices: only some whispers get written down
        float a = director ? director.anxiety : 0.5f;
        if (Random.value > Mathf.Lerp(0.5f, 0.9f, a)) return;
        if (thoughtsOnWalls)
        {
            if (writer == null) writer = FindFirstObjectByType<ThoughtWriter>();
            if (writer != null) { writer.Write(WhisperSystem.Lines[idx]); return; }
        }
        // keep thoughts in the periphery, never right in the middle
        Vector2 p;
        do { p = new Vector2(Random.Range(0.08f, 0.92f), Random.Range(0.12f, 0.88f)); }
        while (Mathf.Abs(p.x - 0.5f) < 0.18f && Mathf.Abs(p.y - 0.5f) < 0.18f);
        thoughts.Add(new Thought { text = WhisperSystem.Lines[idx], pos = p, born = Time.time, life = Random.Range(2f, 3.5f), size = Random.Range(18, 34) });
    }

    // ---------- loop ----------
bool AnyStartInput()
    {
#if ENABLE_INPUT_SYSTEM
        return (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);
#endif
    }
bool BreathHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return MMVR.BreatheHeld || (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) || (Mouse.current != null && Mouse.current.rightButton.isPressed);
#else
        return Input.GetKey(KeyCode.Space) || Input.GetMouseButton(1);
#endif
    }
bool RestartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.R);
#endif
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (MMVR.ClickPending) { MMVR.ClickPending = false; HandleClick(MMVR.Cursor); }
        if (dragTime && MMVR.WorldUI) { if (MMVR.TriggerHeld && MMVR.CursorValid) TimeFromMouse(MMVR.Cursor.x); else dragTime = false; }
        if (director) director.vrComfort = vrBase || MMSettings.Seated;
        AudioListener.volume = MMSettings.Volume / 100f;
        if (EscPressed()) OnEsc();
        switch (state)
        {
            case State.Intro:
                introTime += dt; if (autoBegin) { autoBegin = false; if (quickStart) { quickStart = false; Begin(); } else { cur = Pg.Briefing; sel = 0; introTime = MenuAt + 1f; } }
                walker.inputEnabled = false;
                walker.pitchOffset = -24f + Mathf.Sin(introTime * 0.25f) * 6f;   // looking up at the towering walls
                if (introTime < MenuAt) { if (AnyStartInput()) introTime = MenuAt + 0.2f; } else Nav();
                break;
            case State.Playing:
                if (paused) Nav(); else { MapInput(); Tick(dt); }
                break;
            default:
                endFade = Mathf.MoveTowards(endFade, 1f, dt * 0.5f);
                if (endFade > 0.6f) { Nav(); if (RetryPressed() && cur == Pg.None) Activate("retry"); }
                break;
        }

        // beacon: slow pulse, flickers out when it slips
        if (beacon)
        {
            beaconFlicker = Mathf.MoveTowards(beaconFlicker, 0f, dt);
            bool on = beaconFlicker <= 0f || Random.value > 0.6f;
            foreach (var rr in beacon.GetComponentsInChildren<Renderer>()) rr.enabled = on;
            if (beaconLight) beaconLight.intensity = on ? 5f + Mathf.Sin(Time.time * 2f) * 1.5f : 0f;
            if (beaconHum && walker)
            {
                float dist = Vector3.Distance(new Vector3(walker.transform.position.x, 0, walker.transform.position.z),
                                              new Vector3(beacon.position.x, 0, beacon.position.z));
                beaconHum.volume = Mathf.Lerp(0f, 0.18f, Mathf.Clamp01(1f - dist / 25f));
            }
        }
        for (int i = thoughts.Count - 1; i >= 0; i--) if (Time.time - thoughts[i].born > thoughts[i].life) thoughts.RemoveAt(i);
        penaltyFlash = Mathf.MoveTowards(penaltyFlash, 0f, dt);
        banner = Mathf.MoveTowards(banner, 0f, dt);
    }

    void Begin()
    {
        state = State.Playing; cur = Pg.None; sel = 0;
        walker.frozen = false;
        walker.enabled = true;
        walker.inputEnabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        if (blocks) blocks.spawning = true;
        if (whispers) whispers.active = true;
        director.alwaysPanic = fullPanic;
        Banner("Find the way out.");
    }

    void Banner(string s) { bannerText = s; banner = 3f; }

    void Tick(float dt)
    {
        runTime += dt;
        timeLeft -= dt;
        walker.pitchOffset = Mathf.MoveTowards(walker.pitchOffset, 0f, dt * 15f);   // lower your gaze as the run begins

        // time pressure drives the background dread
        float pressure = 1f - Mathf.Clamp01(timeLeft / totalTime);
        director.externalDread = fullPanic ? Mathf.Lerp(0.85f, 1f, pressure) : Mathf.Lerp(0.2f, 0.85f, pressure) + (timeLeft < 20f ? 0.1f : 0f);

        // breathing: stop, slow down, the world quiets a little - but the clock keeps going
        breathing = BreathHeld() || forceBreath;
        breathingCalm = Mathf.MoveTowards(breathingCalm, breathing ? 1f : 0f, dt * (breathing ? 0.35f : 0.8f));
        director.calm = breathingCalm;
        walker.frozen = breathing;

        float dist = Vector3.Distance(Flat(walker.transform.position), Flat(goal));
        if (slipsLeft > 0 && dist < slipDistance)
        {
            // so close... and it moves.
            slipsLeft--;
            goal = goalB;
            beacon.position = goal;
            beaconFlicker = 1.2f;
            director.AddShock(0.7f, 0.35f);
            if (whispers) whispers.Burst(3);
            Banner("It moved.");
        }
        else if (slipsLeft == 0 && dist < reachDistance)
        {
            End(State.Won);
            return;
        }

        if (timeLeft <= 0f) { timeLeft = 0f; End(State.Lost); }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    void End(State s)
    {
        state = s;
        walker.frozen = true;
        if (blocks) blocks.spawning = false;
        if (whispers) whispers.active = s == State.Lost; // the voices don't stop if you lose
        director.calm = s == State.Won ? 1f : 0f;
        director.alwaysPanic = s == State.Lost && fullPanic;
        director.externalDread = s == State.Won ? 0f : 0.9f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        endFade = 0f; sel = 0; cur = Pg.None; paused = false; Time.timeScale = 1f; AudioListener.pause = false;
    }

    // ---------- HUD ----------
    // Drawn with MindMazeUI (Inter, in Assets/Resources/Fonts). Authored for 1080p and scaled.
    int hits;
    float heartPhase, heartBeat;   // heartbeat for the pulse readout
    float breathVis;               // fades the breathing ring in/out

    static string MMSS(float t) { int x = Mathf.Max(0, Mathf.CeilToInt(t)); return (x / 60).ToString("00") + ":" + (x % 60).ToString("00"); }
    static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

    void LateUpdate()
    {
        float a = director ? director.anxiety : 0f;
        float bpm = Mathf.Lerp(66f, 150f, a);
        heartPhase += Time.deltaTime * bpm / 60f;
        if (heartPhase >= 1f) { heartPhase -= 1f; heartBeat = 1f; }
        heartBeat = Mathf.MoveTowards(heartBeat, 0f, Time.deltaTime * 4f);
        breathVis = Mathf.MoveTowards(breathVis, state == State.Playing && breathing ? 1f : 0f, Time.deltaTime * 3f);
    }

}
