using UnityEngine;

// Procedural hum synth for the exit beacon.
// Generates two detuned sine waves (a low fundamental + a fifth) and a soft tremolo.
// Attach to the same GameObject as the AudioSource; AnxietyGame drives the volume externally.
[RequireComponent(typeof(AudioSource))]
public class BeaconHumSynth : MonoBehaviour
{
    [Range(40f, 220f)] public float baseFreq = 80f;    // Hz — below speech, felt more than heard
    [Range(0f, 1f)]    public float detune = 0.015f;   // slight chorus between voice 0 and voice 1
    [Range(0.1f, 8f)]  public float tremoloRate = 2.1f;
    [Range(0f, 0.4f)]  public float tremoloDepth = 0.22f;

    double phase0, phase1;
    int sampleRate;

    void Awake()  { sampleRate = AudioSettings.outputSampleRate; }

    void OnAudioFilterRead(float[] data, int channels)
    {
        double dt = 1.0 / sampleRate;
        double f0 = baseFreq, f1 = baseFreq * (1.0 + detune);
        double t = AudioSettings.dspTime;

        for (int i = 0; i < data.Length; i += channels)
        {
            float tremolo = 1f - tremoloDepth * (0.5f + 0.5f * Mathf.Sin((float)(t * tremoloRate * System.Math.PI * 2)));
            float v0 = (float)System.Math.Sin(phase0 * System.Math.PI * 2) * 0.6f;
            float v1 = (float)System.Math.Sin(phase1 * System.Math.PI * 2) * 0.4f;
            float vFifth = (float)System.Math.Sin((phase0 * 1.5) * System.Math.PI * 2) * 0.18f;
            float sample = (v0 + v1 + vFifth) * tremolo;

            for (int c = 0; c < channels; c++) data[i + c] += sample;

            phase0 = System.Math.IEEERemainder(phase0 + f0 * dt, 1.0);
            phase1 = System.Math.IEEERemainder(phase1 + f1 * dt, 1.0);
            t += dt;
        }
    }
}
