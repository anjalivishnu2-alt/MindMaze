using UnityEngine;
using System.Collections.Generic;

// Drops Jenga-style blocks from above around the player at random. Some land right on
// top of you, some crash just beside you. Impacts shake the camera and spike anxiety.
public class FallingBlocks : MonoBehaviour
{
    public Transform player;
    public Material[] blockMaterials;
    public Material sketchMaterial;
    [Tooltip("Blocks only fall while this is on (the game turns it on when a run starts).")]
    public bool spawning = true;
    public static System.Action OnPlayerHit;
    public Vector3 blockSize = new Vector3(0.9f, 0.32f, 2.7f);
    public Vector2 intervalCalm = new Vector2(5f, 10f);
    public Vector2 intervalPanic = new Vector2(1.0f, 3.0f);
    public float dropHeight = 22f;
    public int maxBlocks = 35;
    public float blockLifetime = 18f;
    [Range(0, 1)] public float directHitChance = 0.15f;

    readonly List<Block> live = new List<Block>();
    float timer = 4f;
    AudioClip impactClip;
    Collider playerCol;

    class Block { public GameObject go; public float born; }

    void Start()
    {
        if (player == null) { var w = FindFirstObjectByType<MazeWalker>(); if (w) player = w.transform; }
        if (player) playerCol = player.GetComponent<Collider>();
        impactClip = MakeImpact();
        SketchTrail.SetMaterial(sketchMaterial);
    }

    void Update()
    {
        if (player == null) return;
        float a = AnxietyDirector.Instance ? AnxietyDirector.Instance.anxiety : 0.3f;
        if (spawning) timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Spawn(a);
            Vector2 range = Vector2.Lerp(intervalCalm, intervalPanic, a);
            timer = Random.Range(range.x, range.y);
            if (Random.value < a * 0.35f) timer *= 0.25f; // sometimes they come in bursts
        }

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var b = live[i];
            if (b.go == null) { live.RemoveAt(i); continue; }
            float age = Time.time - b.born;
            if (age > blockLifetime)
            {
                // sink into the floor and disappear
                var rb = b.go.GetComponent<Rigidbody>();
                if (rb && !rb.isKinematic) { rb.isKinematic = true; foreach (var c in b.go.GetComponents<Collider>()) c.enabled = false; }
                b.go.transform.position += Vector3.down * Time.deltaTime * 0.6f;
                if (age > blockLifetime + 4f) { Destroy(b.go); live.RemoveAt(i); }
            }
        }
        while (live.Count > maxBlocks) { if (live[0].go) Destroy(live[0].go); live.RemoveAt(0); }
    }

    void Spawn(float a)
    {
        Vector3 basePos = player.position;
        Vector3 fwd = player.forward; fwd.y = 0; fwd.Normalize();
        Vector3 offset;
        float r = Random.value;
        if (r < directHitChance + a * 0.1f) offset = Random.insideUnitSphere * 0.6f;                       // on you
        else if (r < 0.6f) offset = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(1.2f, 3f); // near miss
        else offset = fwd * Random.Range(3f, 9f) + Random.insideUnitSphere * 2f;                            // ahead, blocking the way
        offset.y = 0;
        // lead the target if the player is moving
        var walker = player.GetComponent<MazeWalker>();
        if (walker) offset += fwd * walker.currentSpeed * 0.8f;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "FallingBlock";
        go.transform.position = basePos + offset + Vector3.up * (dropHeight + Random.Range(-3f, 6f));
        go.transform.rotation = Random.rotation;
        float s = Random.Range(0.8f, 1.5f);
        go.transform.localScale = blockSize * s;
        if (blockMaterials != null && blockMaterials.Length > 0)
            go.GetComponent<Renderer>().sharedMaterial = blockMaterials[Random.Range(0, blockMaterials.Length)];
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 20f * s;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.angularVelocity = Random.insideUnitSphere * 4f;
        rb.linearVelocity = Vector3.down * Random.Range(4f, 10f);
        var hit = go.AddComponent<BlockImpact>();
        hit.owner = this;
        // blocks pass through you (no getting trapped under one); a hit is judged by distance
        if (playerCol != null) foreach (var col in go.GetComponents<Collider>()) Physics.IgnoreCollision(col, playerCol);
        go.AddComponent<SketchTrail>();
        go.AddComponent<BlockWords>();   // every falling block carries a thought
        live.Add(new Block { go = go, born = Time.time });
    }

    public void OnImpact(BlockImpact b, Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (speed < 2f) return;
        float dist = Vector3.Distance(b.transform.position, player.position);
        bool hitPlayer = dist < 1.8f && !b.counted;
        if (dist < 1.8f) b.counted = true;
        if (hitPlayer)
        {
            // let it pass through you instead of sitting on your head
            // (already ignoring the player's collider)
            AnxietyDirector.Instance?.AddShock(1f, 1f);
            OnPlayerHit?.Invoke();
        }
        else
        {
            float near = Mathf.Clamp01(1f - dist / 10f);
            AnxietyDirector.Instance?.AddShock(near * 0.7f, near * Mathf.Clamp01(speed / 15f));
        }
        float vol = Mathf.Clamp01(speed / 15f);
        AudioSource.PlayClipAtPoint(impactClip, b.transform.position, vol);
    }

    static AudioClip MakeImpact()
    {
        const int sr = 44100; float sec = 0.9f;
        int n = (int)(sr * sec);
        var d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float boom = Mathf.Sin(2f * Mathf.PI * 60f * t * (1f + 2f * Mathf.Exp(-t * 25f))) * Mathf.Exp(-t * 7f);
            lp += ((Random.value * 2f - 1f) - lp) * 0.25f;
            float crack = lp * Mathf.Exp(-t * 30f) * 1.4f;
            float clack = Mathf.Sin(2f * Mathf.PI * 420f * t) * Mathf.Exp(-t * 45f) * 0.5f;
            d[i] = Mathf.Clamp(boom + crack + clack, -1f, 1f);
        }
        var clip = AudioClip.Create("BlockImpact", n, 1, sr, false);
        clip.SetData(d, 0);
        return clip;
    }
}

public class BlockImpact : MonoBehaviour
{
    public FallingBlocks owner;
    public bool counted;
    float lastHit = -1f;
    void OnCollisionEnter(Collision c)
    {
        if (Time.time - lastHit < 0.25f) return;
        lastHit = Time.time;
        if (owner) owner.OnImpact(this, c);
    }
}
