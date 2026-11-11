using UnityEngine;

// Hooks used by MMCapture (scripted gameplay video / trailer). They let the capture press
// the same buttons a player would, without depending on the editor window having focus.
public partial class AnxietyGame
{
    public static bool CapHideUI;                 // trailer: draw none of the game's screens
    static int capKeyWhich = -1, capKeyFrame = -1;

    /// Press a button on the next frame. 0 up, 1 down, 2 left, 3 right, 4 confirm, 5 retry, 6 esc, 7 map.
    public static void CapPress(int which) { capKeyWhich = which; capKeyFrame = Time.frameCount + 1; }
    static bool CapKey(int which) { return capKeyWhich == which && capKeyFrame == Time.frameCount; }

    public static int CapLevel { get { return level; } set { level = Mathf.Max(1, value); } }
    public float CapTimeLeft { get { return timeLeft; } set { timeLeft = value; } }
    public float CapTotalTime { get { return totalTime; } }
    public Vector3 CapGoal { get { return goal; } }
    public bool CapMapOpen { get { return mapOpen; } }
    public bool CapPaused { get { return paused; } }
    public Pg CapPage { get { return cur; } }
    public int CapSel { get { return sel; } }
    public float CapIntroTime { get { return introTime; } }
}
