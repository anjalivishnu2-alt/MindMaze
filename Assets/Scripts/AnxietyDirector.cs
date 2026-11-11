using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Drives the whole "anxiety" mood: a level that rises over time, spikes on shocks
// (falling blocks) and when the corridor is tight, and feeds back into post-processing,
// camera motion, fog, light flicker, texture speed and procedural heartbeat / breathing audio.
public class AnxietyDirector : MonoBehaviour
{
    public static AnxietyDirector Instance;

    [Header("References")]
    public MazeWalker walker;
    public Camera cam;
    public Volume volume;
    public Light playerLight;
    public WallTextureAnimator textures;

    [Header("Anxiety")]
    [Tooltip("Seconds for the background dread to build to its maximum.")]
    public float buildUpSeconds = 150f;
    [Range(0, 1)] public float minimumAnxiety = 0.2f;
    [Range(0, 1)] public float anxiety;          // read-only view
    public float shockDecay = 0.25f;

    [Header("Look")]
    public float baseFov = 78f;
    public float fogBase = 0.035f;
    public float fogMax = 0.11f;
    public float exposure = 0.6f;
    public float lightMax = 4f;
    [Range(0, 1)] public float baseVignette = 0.22f;
    public float fogFar = 15f;
    public float fogNear = 7f;

    [Header("VR")]
    [Tooltip("Turns off camera roll, shake, bob and FOV changes, which cause motion sickness in a headset. The eye/vignette, blinks, colour and wall effects stay.")]
    public bool vrComfort = false;

    [Header("Moving shadows from above")]
    public Light sun;
    public Texture2D[] shadowFrames;
    public float shadowFps = 10f;

    [Header("Game hooks")]
    [Tooltip("If 0..1, the game drives the background dread (e.g. from the timer). -1 = build up over time.")]
    public float externalDread = -1f;
    [Range(0, 1)] public float calm;      // set while the player is breathing slowly
    public bool panicking;                // a panic attack is happening
    public float panicDuration = 6f;
    [Tooltip("Full panic-attack mode: the attack never really stops. Only slow breathing gives brief relief.")]
    public bool alwaysPanic = false;
    [Range(0, 1)] public float panicSpeed = 0.25f;   // movement multiplier at the height of an attack
    [Range(0, 1)] public float panicVision = 0.38f;  // how much the attack darkens / narrows vision
    float panicTimer, highTimer, panicAmt;

    float blinkCooldown = 4f, blinkTimer, blinkLen;
    bool blinking;

    float shock;
    float tightness;
    float t;
    float shakeTrauma;
    float glitchTimer;
    float glitchCooldown = 6f;
    float nextBeat;
    float breathPhase;
    float ringing;

    Vignette vignette;
    LensDistortion lens;
    ChromaticAberration chroma;
    FilmGrain grain;
    ColorAdjustments color;
    PaniniProjection panini;

    AudioSource heartSrc, breathSrc, droneSrc, ringSrc;
    AudioClip beatClip;

    void Awake() { Instance = this; }

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (walker == null) walker = FindFirstObjectByType<MazeWalker>();
        if (textures == null) textures = FindFirstObjectByType<WallTextureAnimator>();
        if (volume != null)
        {
            var p = volume.profile; // runtime instance
            p.TryGet(out vignette); p.TryGet(out lens); p.TryGet(out chroma);
            p.TryGet(out grain); p.TryGet(out color); p.TryGet(out panini);
        }
        BuildAudio();
    }

    /// Call when something frightening happens near the player (0..1).
    public void AddShock(float amount, float shake)
    {
        shock = Mathf.Clamp01(shock + amount);
        shakeTrauma = Mathf.Clamp01(shakeTrauma + shake);
        if (amount > 0.5f) { ringing = 1f; glitchTimer = 0.25f; }
    }

void Update()
    {
        float dt = Time.deltaTime;
        t += dt;

        // --- anxiety level ---
        shock = Mathf.MoveTowards(shock, 0f, shockDecay * dt);
        if (Time.frameCount % 6 == 0) tightness = Mathf.Lerp(tightness, MeasureTightness(), 0.5f);
        float dread = externalDread >= 0f ? externalDread : Mathf.Lerp(minimumAnxiety, 0.75f, Mathf.Clamp01(t / buildUpSeconds));
        float target = Mathf.Clamp01(dread + shock * 0.6f + tightness * 0.25f - calm * 0.65f);
        anxiety = Mathf.Lerp(anxiety, target, dt * 1.5f);

        // --- breathing (drives the suffocation squeeze) ---
        float breathRate = Mathf.Lerp(0.22f, 0.75f, anxiety) * (1f - calm * 0.55f);
        breathPhase += dt * breathRate * Mathf.PI * 2f;
        float breath = (Mathf.Sin(breathPhase) + 1f) * 0.5f;
        float gasp = Mathf.Pow(breath, 3f);

        // --- random glitches ---
        glitchCooldown -= dt;
        if (glitchCooldown <= 0f)
        {
            glitchTimer = Random.Range(0.06f, 0.2f + anxiety * 0.3f);
            glitchCooldown = Random.Range(2f, 9f) * Mathf.Lerp(1.4f, 0.4f, anxiety);
        }
        bool glitch = glitchTimer > 0f;
        if (glitch) glitchTimer -= dt;

        // --- eye blinks (sometimes long, heavy ones when panicking) ---
        blinkCooldown -= dt;
        if (blinkCooldown <= 0f && !blinking)
        {
            blinking = true; blinkTimer = 0f;
            blinkLen = Random.Range(0.18f, 0.3f) + (Random.value < anxiety * 0.4f ? Random.Range(0.5f, 1.3f) : 0f);
            blinkCooldown = Random.Range(3f, 8f) * Mathf.Lerp(1.3f, 0.5f, anxiety);
        }
        float lid = 0f;
        if (blinking)
        {
            blinkTimer += dt;
            float x = blinkTimer / blinkLen;
            lid = Mathf.Sin(Mathf.Clamp01(x) * Mathf.PI);
            if (x >= 1f) blinking = false;
        }

        // --- panic attack: anxiety pinned at the top for too long ---
        if (anxiety > 0.9f) highTimer += dt; else highTimer = Mathf.Max(0f, highTimer - dt);
        if (alwaysPanic)
        {
            bool relief = calm > 0.7f;
            if (!panicking && !relief) { panicking = true; ringing = Mathf.Max(ringing, 0.6f); }
            if (panicking && relief) panicking = false;
            panicTimer = panicDuration;
        }
        else if (!panicking && highTimer > 2.5f) { panicking = true; panicTimer = panicDuration; ringing = 1f; }
        if (panicking && !alwaysPanic) { panicTimer -= dt * (1f + calm * 3f); if (panicTimer <= 0f) { panicking = false; highTimer = 0f; } }
        panicAmt = Mathf.MoveTowards(panicAmt, panicking ? 1f : 0f, dt * 1.2f);
        lid = Mathf.Max(lid, panicAmt * panicVision);

        
// --- post-processing ---
        float distortScale = vrComfort ? 0.3f : 1f;
        if (vignette != null)
        {
            vignette.intensity.Override(Mathf.Clamp01(baseVignette + anxiety * 0.2f + gasp * (0.06f + anxiety * 0.16f) + shock * 0.15f + lid * 0.65f));
            vignette.smoothness.Override(Mathf.Clamp01(Mathf.Lerp(0.3f, 0.75f, anxiety) + lid * 0.25f));
        }
        if (vignette != null) vignette.intensity.Override(Mathf.Clamp01(vignette.intensity.value * MMSettings.Vig));
        if (lens != null)
        {
            float wobble = Mathf.Sin(t * 0.7f) * 0.12f + Mathf.PerlinNoise(t * 0.5f, 3.1f) * 0.2f - 0.1f;
            float squeeze = -gasp * (0.12f + anxiety * 0.25f);
            float l = (wobble * anxiety + squeeze + (glitch ? Random.Range(-0.6f, 0.4f) : 0f)) * distortScale;
            lens.intensity.Override(Mathf.Clamp(l, -0.85f, 0.6f));
            lens.scale.Override(1f);
        }
        if (chroma != null) chroma.intensity.Override(Mathf.Clamp01((0.4f + anxiety * 0.4f + (glitch ? 0.6f : 0f) + shock * 0.3f) * MMSettings.BlurMul));
        if (grain != null) grain.intensity.Override(Mathf.Clamp01(0.2f + anxiety * 0.35f));
        if (color != null)
        {
            float dip = glitch ? Random.Range(-1.2f, 0.5f) : 0f;
            color.postExposure.Override(exposure - gasp * anxiety * 0.3f + dip - lid * 6f);
            color.contrast.Override(25f + anxiety * 20f);
            color.saturation.Override(-100f);
        }
        if (panini != null) panini.distance.Override(vrComfort ? 0f : Mathf.Clamp01(0.1f + anxiety * 0.35f + Mathf.Sin(t * 0.33f) * 0.1f));

        // --- camera (skipped in VR comfort mode) ---
        if (cam != null && !vrComfort)
        {
            float fov = baseFov + Mathf.Sin(breathPhase) * (2f + anxiety * 5f) - shock * 8f;
            if (glitch) fov += Random.Range(-15f, 15f);
            cam.fieldOfView = fov;
        }
        if (walker != null)
        {
            shakeTrauma = Mathf.MoveTowards(shakeTrauma, 0f, dt * 0.8f);
            if (vrComfort)
            {
                walker.shakeEuler = Vector3.zero; walker.extraRoll = 0f; walker.bobOffset = Vector3.zero;
            }
            else
            {
                float tr = shakeTrauma * shakeTrauma + anxiety * 0.015f;
                walker.shakeEuler = new Vector3(
                    (Mathf.PerlinNoise(t * 25f, 0f) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(t * 25f, 7f) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(t * 25f, 13f) - 0.5f) * 2f) * tr * 12f;
                walker.extraRoll = (Mathf.PerlinNoise(t * 0.08f, 42f) - 0.5f) * 2f * (4f + anxiety * 10f) + (glitch ? Random.Range(-8f, 8f) : 0f);
                float bobAmt = walker.currentSpeed > 0.1f ? 0.03f + anxiety * 0.025f : 0.006f;
                float bobFreq = walker.currentSpeed > 0.1f ? walker.currentSpeed * 2.2f : 1f;
                walker.bobOffset = new Vector3(Mathf.Sin(t * bobFreq * 0.5f) * bobAmt * 0.6f, Mathf.Abs(Mathf.Sin(t * bobFreq)) * bobAmt - gasp * 0.03f, 0f);
            }
            walker.shakeEuler *= MMSettings.ShakeMul; walker.extraRoll *= MMSettings.ShakeMul; walker.bobOffset *= MMSettings.ShakeMul;
            walker.speedMultiplier = Mathf.Lerp(1f, 0.8f, anxiety) * (shock > 0.6f ? 0.6f : 1f) * Mathf.Lerp(1f, panicSpeed, panicAmt);
        }

        // --- walls breathe in on you and ripple (Maze/AnxietyWall shader) ---
        float squeezeIn = gasp * (0.04f + anxiety * 0.3f) + shock * 0.25f;
        Shader.SetGlobalFloat("_MazeBreath", squeezeIn);
        Shader.SetGlobalFloat("_MazeWarp", 0.03f + anxiety * 0.15f + (glitch ? 0.4f : 0f));

        // --- environment: grey haze closes in as anxiety rises ---
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 2f;
        RenderSettings.fogEndDistance = Mathf.Lerp(fogFar, fogNear, anxiety) - gasp * 6f;
        if (sun != null && shadowFrames != null && shadowFrames.Length > 0)
        {
            float sp = shadowFps * Mathf.Lerp(0.7f, 1.8f, anxiety);
            sun.cookie = shadowFrames[Mathf.FloorToInt(t * sp) % shadowFrames.Length];
        }
        if (playerLight != null)
        {
            float flick = Mathf.PerlinNoise(t * 9f, 1f);
            bool drop = Random.value < anxiety * 0.03f;
            playerLight.intensity = drop ? 0.2f : Mathf.Lerp(lightMax, lightMax * 0.6f, anxiety) * (0.8f + flick * 0.4f);
        }
        if (textures != null) textures.speedMultiplier = Mathf.Lerp(0.8f, 2.2f, anxiety) * (glitch ? 3f : 1f);

        // --- audio ---
        float bpm = Mathf.Lerp(62f, 155f, anxiety);
        if (Time.time >= nextBeat)
        {
            heartSrc.PlayOneShot(beatClip, Mathf.Lerp(0.35f, 1f, anxiety));
            nextBeat = Time.time + 60f / bpm;
        }
        breathSrc.pitch = Mathf.Lerp(0.85f, 1.5f, anxiety);
        breathSrc.volume = Mathf.Lerp(0.12f, 0.45f, anxiety) * (0.4f + 0.6f * breath) * (1f - lid * 0.7f);
        droneSrc.volume = Mathf.Lerp(0.15f, 0.5f, anxiety);
        droneSrc.pitch = 1f + Mathf.Sin(t * 0.21f) * 0.03f;
        ringing = Mathf.MoveTowards(ringing, 0f, dt * 0.25f);
        ringSrc.volume = ringing * 0.12f;
    }

    float MeasureTightness()
    {
        if (walker == null) return 0f;
        Vector3 o = walker.transform.position + Vector3.up * 1f;
        float sum = 0f; int n = 8;
        for (int i = 0; i < n; i++)
        {
            Vector3 d = Quaternion.Euler(0, i * 360f / n, 0) * Vector3.forward;
            sum += Physics.Raycast(o, d, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 6f;
        }
        float avg = sum / n;
        return Mathf.Clamp01(1f - (avg - 1f) / 4f);
    }

    // ---------- procedural audio ----------
    void BuildAudio()
    {
        const int sr = 44100;
        heartSrc = MakeSource(null, false, 1f);
        beatClip = MakeClip("Heartbeat", sr, 0.6f, (i, tt) =>
        {
            float a = Thump(tt, 0f, 55f) + 0.7f * Thump(tt, 0.18f, 48f);
            return a;
        });

        var breathClip = MakeClip("Breath", sr, 4f, (i, tt) =>
        {
            float env = Mathf.Pow(Mathf.Sin(tt / 4f * Mathf.PI * 2f) * 0.5f + 0.5f, 2f);
            return (Random.value * 2f - 1f) * env * 0.5f;
        }, true);
        breathSrc = MakeSource(breathClip, true, 0.2f);

        var droneClip = MakeClip("Drone", sr, 8f, (i, tt) =>
        {
            return 0.35f * Mathf.Sin(2f * Mathf.PI * 41.2f * tt) + 0.25f * Mathf.Sin(2f * Mathf.PI * 43.7f * tt)
                 + 0.12f * Mathf.Sin(2f * Mathf.PI * 82.5f * tt) + (Random.value * 2f - 1f) * 0.05f;
        });
        droneSrc = MakeSource(droneClip, true, 0.2f);

        var ringClip = MakeClip("Ringing", sr, 2f, (i, tt) => 0.4f * Mathf.Sin(2f * Mathf.PI * 4000f * tt));
        ringSrc = MakeSource(ringClip, true, 0f);
    }

    static float Thump(float tt, float start, float freq)
    {
        float x = tt - start;
        if (x < 0f) return 0f;
        float env = Mathf.Exp(-x * 18f) * Mathf.Clamp01(x * 400f);
        return Mathf.Sin(2f * Mathf.PI * freq * x * (1f + Mathf.Exp(-x * 30f))) * env;
    }

    AudioSource MakeSource(AudioClip clip, bool loop, float vol)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip; s.loop = loop; s.volume = vol; s.spatialBlend = 0f; s.playOnAwake = false;
        if (clip != null) s.Play();
        return s;
    }

    static AudioClip MakeClip(string name, int sr, float seconds, System.Func<int, float, float> gen, bool lowpass = false)
    {
        int n = Mathf.CeilToInt(sr * seconds);
        var data = new float[n];
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float v = gen(i, i / (float)sr);
            if (lowpass) { prev += (v - prev) * 0.08f; v = prev * 3f; }
            data[i] = Mathf.Clamp(v, -1f, 1f);
        }
        var c = AudioClip.Create(name, n, 1, sr, false);
        c.SetData(data, 0);
        return c;
    }
}
