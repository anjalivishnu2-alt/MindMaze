using UnityEngine;

// Whispered intrusive thoughts around the player's head. A murmuring crowd loop sits
// underneath; single whispers come from random directions close to the ears, more often
// as anxiety rises and quieter while the player is breathing slowly.
public class WhisperSystem : MonoBehaviour
{
    // Must match the order of whisper_00 .. whisper_27
    public static readonly string[] Lines = {
        "you're late", "everyone is waiting", "you can't do this", "hurry", "why is it so hard",
        "you're going to fail", "they're all watching", "not good enough", "what if you don't make it",
        "it's too much", "you're wasting time", "faster", "you'll never get out", "what's wrong with you",
        "everyone else can do it", "you're running out of time", "it's all falling apart", "don't stop",
        "you're alone", "try harder", "you always mess it up", "they'll be disappointed", "no time", "why can't you just",
        "breathe", "it's closing in", "you should have started earlier", "what are they thinking of you"
    };

    public AudioClip[] whispers;
    public AudioClip crowdLoop;
    public Transform listener;
    public bool active = true;
    [Range(0, 1)] public float volume = 1f;
    public System.Action<int> onWhisper;   // index into Lines, for on-screen thoughts

    AudioSource crowd;
    AudioSource[] pool;
    int next;
    float timer = 4f;
    float t;

    void Start()
    {
        if (listener == null && Camera.main) listener = Camera.main.transform;
        crowd = gameObject.AddComponent<AudioSource>();
        crowd.clip = crowdLoop; crowd.loop = true; crowd.volume = 0f; crowd.spatialBlend = 0f;
        if (crowdLoop) crowd.Play();
        pool = new AudioSource[6];
        for (int i = 0; i < pool.Length; i++)
        {
            var g = new GameObject("whisper_src_" + i);
            g.transform.SetParent(transform, false);
            var s = g.AddComponent<AudioSource>();
            s.spatialBlend = 1f; s.minDistance = 0.6f; s.maxDistance = 12f;
            s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0f; s.spread = 40f; s.playOnAwake = false;
            pool[i] = s;
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        var d = AnxietyDirector.Instance;
        float a = d ? d.anxiety : 0.4f;
        float calm = d ? d.calm : 0f;
        bool panic = d && d.panicking;

        float targetCrowd = active ? Mathf.Lerp(0.04f, 0.4f, a) * (1f - calm * 0.75f) * (panic ? 1.6f : 1f) * volume : 0f;
        crowd.volume = Mathf.MoveTowards(crowd.volume, targetCrowd, Time.deltaTime * 0.3f);
        crowd.pitch = 1f + Mathf.Sin(t * 0.13f) * 0.04f;

        if (!active || whispers == null || whispers.Length == 0 || listener == null) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Say(Random.Range(0, whispers.Length), a, calm);
            if (Random.value < a * 0.35f || panic) Say(Random.Range(0, whispers.Length), a, calm); // overlapping voices
            timer = Random.Range(4f, 9f) * Mathf.Lerp(1.4f, 0.22f, a) * (1f + calm * 2.5f) * (panic ? 0.4f : 1f);
        }
    }

    public void Say(int idx, float a = 0.6f, float calm = 0f)
    {
        if (whispers == null || idx < 0 || idx >= whispers.Length || whispers[idx] == null) return;
        var s = pool[next++ % pool.Length];
        Vector3 dir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
        s.transform.position = listener.position + dir * Random.Range(0.5f, 2.5f) + Vector3.up * Random.Range(-0.3f, 0.4f);
        s.clip = whispers[idx];
        s.pitch = Random.Range(0.88f, 1.08f);
        s.volume = Mathf.Lerp(0.45f, 1f, a) * (1f - calm * 0.6f) * volume;
        s.Play();
        onWhisper?.Invoke(idx);
    }

    public void Burst(int count)
    {
        var d = AnxietyDirector.Instance;
        for (int i = 0; i < count; i++) Say(Random.Range(0, whispers.Length), d ? d.anxiety : 0.8f, 0f);
    }
}
