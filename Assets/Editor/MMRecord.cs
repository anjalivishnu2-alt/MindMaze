using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

// Records the Game view (1080p, 30 fps, with sound) to an MP4 using Unity Recorder.
// MMCapture (the scripted gameplay video / trailer) calls Begin and End through its hooks.
[InitializeOnLoad]
public static class MMRecord
{
    static RecorderController ctl;
    static RecorderControllerSettings cs;

    static MMRecord() { MMCapture.StartRec = Begin; MMCapture.StopRec = End; }

    public static void Begin(string file)
    {
        End();
        cs = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var m = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        m.name = "MindMaze";
        m.Enabled = true;
        m.EncoderSettings = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4, EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
        m.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
        m.CaptureAudio = true;
        m.OutputFile = file;                      // absolute path, no extension
        cs.AddRecorderSettings(m);
        cs.SetRecordModeToManual();
        cs.FrameRate = 30f;
        cs.FrameRatePlayback = FrameRatePlayback.Constant;
        cs.CapFrameRate = true;
        ctl = new RecorderController(cs);
        ctl.PrepareRecording();
        ctl.StartRecording();
        Debug.Log("[MMRecord] recording -> " + file + ".mp4");
    }

    public static void End()
    {
        if (ctl != null && ctl.IsRecording()) { ctl.StopRecording(); Debug.Log("[MMRecord] stopped"); }
        ctl = null;
    }

    public static bool Recording { get { return ctl != null && ctl.IsRecording(); } }
}
