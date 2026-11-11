using UnityEngine;

// Player settings (saved). Applied by AnxietyGame / AnxietyDirector / MazeWalker.
public static class MMSettings
{
    public static bool Seated    { get { return PlayerPrefs.GetInt("mm_seated", 0) == 1; }  set { PlayerPrefs.SetInt("mm_seated", value ? 1 : 0); } }
    public static bool Vignette  { get { return PlayerPrefs.GetInt("mm_vignette", 1) == 1; } set { PlayerPrefs.SetInt("mm_vignette", value ? 1 : 0); } }
    public static bool SnapTurn  { get { return PlayerPrefs.GetInt("mm_snap", 0) == 1; }     set { PlayerPrefs.SetInt("mm_snap", value ? 1 : 0); } }
    public static int Shake      { get { return PlayerPrefs.GetInt("mm_shake", 60); }  set { PlayerPrefs.SetInt("mm_shake", Mathf.Clamp(value, 0, 100)); } }
    public static int Tunnel     { get { return PlayerPrefs.GetInt("mm_tunnel", 60); } set { PlayerPrefs.SetInt("mm_tunnel", Mathf.Clamp(value, 0, 100)); } }
    public static int Blur       { get { return PlayerPrefs.GetInt("mm_blur", 40); }   set { PlayerPrefs.SetInt("mm_blur", Mathf.Clamp(value, 0, 100)); } }
    public static int Volume     { get { return PlayerPrefs.GetInt("mm_volume", 70); } set { PlayerPrefs.SetInt("mm_volume", Mathf.Clamp(value, 0, 100)); } }

    // multipliers: the default slider positions give 1.0 (the game as it was)
    public static float ShakeMul => Shake / 60f;
    public static float BlurMul  => Blur / 40f;
    public static float Vig      => Vignette ? 0.2f + Tunnel / 100f * 1.33f : 0f;
}
