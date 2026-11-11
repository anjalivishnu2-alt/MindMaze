using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

// Records a scripted gameplay capture of MIND MAZE as a JPG frame sequence
// (<project>/Recordings/frames). Game time is locked to the capture rate, so the video
// is smooth however slow the capture is. Add to any object in Play mode to start.
//   0s   title sequence plays as normal
//   11s  the run begins; the auto-tour walks the maze
//   31s  holds a breath for a few seconds
//   38s  the clock is cut short so the deadline runs out and the end screen shows
public class GameplayRecorder : MonoBehaviour
{
    public int fps = 30;
    public string folder = "frames";
    public bool walkMaze = true;   // switch the AutoTour on so the player actually travels
    public float beginAt = 11f, breatheAt = 31f, breatheFor = 6f, squeezeAt = 38f, squeezeTo = 20f, endHold = 9f;
    public static bool Done;

    AnxietyGame game;
    string dir;
    int frame;
    float T => frame / (float)fps;

    void Start()
    {
        Done = false;
        game = FindFirstObjectByType<AnxietyGame>();
        dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings", folder);
        if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
        Directory.CreateDirectory(dir);
        Time.captureFramerate = fps;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        bool begun = false, breathed = false, released = false, squeezed = false;
        var tour = FindFirstObjectByType<AutoTour>();
        Vector3 lastPos = tour ? tour.transform.position : Vector3.zero; float lastCheck = 0f;
        float endedAt = -1f;
        var timeLeft = typeof(AnxietyGame).GetField("timeLeft", BindingFlags.NonPublic | BindingFlags.Instance);
        while (true)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, "frame_" + frame.ToString("00000") + ".jpg"), tex.EncodeToJPG(92));
            Destroy(tex);
            frame++;

            if (!begun && T >= beginAt && game.state == AnxietyGame.State.Intro) { game.SendMessage("Begin"); begun = true; if (tour && walkMaze) tour.autoWalk = true; }
            // keep the auto-tour moving: if the player hasn't gone anywhere for 2 s, pick a new route
            if (begun && tour && T - lastCheck >= 2f && game.state == AnxietyGame.State.Playing && !game.forceBreath)
            {
                if (Vector3.Distance(tour.transform.position, lastPos) < 1.0f) tour.SendMessage("NewLeg", SendMessageOptions.DontRequireReceiver);
                lastPos = tour.transform.position; lastCheck = T;
            }
            if (!breathed && T >= breatheAt) { game.forceBreath = true; breathed = true; }
            if (!released && T >= breatheAt + breatheFor) { game.forceBreath = false; released = true; }
            if (!squeezed && T >= squeezeAt && game.state == AnxietyGame.State.Playing)
            {
                float left = (float)timeLeft.GetValue(game);
                if (left > squeezeTo) timeLeft.SetValue(game, squeezeTo);
                squeezed = true;
            }
            if (endedAt < 0f && (game.state == AnxietyGame.State.Lost || game.state == AnxietyGame.State.Won)) endedAt = T;
            if ((endedAt >= 0f && T - endedAt >= endHold) || T > 120f) break;
        }
        Time.captureFramerate = 0;
        game.forceBreath = false;
        Done = true;
        Debug.Log("[GameplayRecorder] DONE " + frame + " frames -> " + dir);
    }

    void OnDisable() { Time.captureFramerate = 0; }
}
