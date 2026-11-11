using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// MIND MAZE screens, built from the Figma design:
// stencil titles, centred caps menus with a brush stroke on the selected item, minimal HUD.
// Screens: Menu, Help, Care, Settings, Briefing (before a run), Pause, Breathe, Time's up / You made it out.
public partial class AnxietyGame
{
    public enum Pg { None, Menu, Help, Care, Settings, Briefing, Pause, Breathe }

    static bool quickStart;         // retry drops straight into the maze
    static bool skipIntro;          // retry / next level go straight to the briefing
    static bool careSeen;           // "Before you enter" is shown once per session
    static int level = 1;
    bool autoBegin, vrBase, paused;
    const float MenuAt = 5f;        // the typed opening line plays first, then the menu fades in

    Pg cur = Pg.Menu, settingsBack = Pg.Menu;
    int sel;
    readonly List<Rect> menuHits = new List<Rect>();
    readonly List<string> hitIds = new List<string>();
    readonly List<Rect> rowRects = new List<Rect>();
    readonly List<Rect> sliderRects = new List<Rect>();
    int dragSlider = -1;
    Vector2 lastMouse;

    // ---------- input ----------
    static bool KeyPressed(int which) { return KeyRaw(which) || VRKey(which) || CapKey(which); }
    static bool VRKey(int which)
    {
        switch (which) { case 0: return MMVR.Up; case 1: return MMVR.Down; case 2: return MMVR.Left; case 3: return MMVR.Right; case 4: return MMVR.Confirm; case 6: return MMVR.Back; }
        return false;
    }
    static Vector2 Ptr() { return MMVR.WorldUI ? MMVR.Cursor : Event.current.mousePosition; }
    static bool KeyRaw(int which)
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return false;
        switch (which)
        {
            case 0: return k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame;
            case 1: return k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame;
            case 2: return k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame;
            case 3: return k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
            case 4: return k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame;
            case 5: return k.rKey.wasPressedThisFrame;
            case 6: return k.escapeKey.wasPressedThisFrame;
        }
        return false;
#else
        switch (which)
        {
            case 0: return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
            case 1: return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
            case 2: return Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
            case 3: return Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
            case 4: return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
            case 5: return Input.GetKeyDown(KeyCode.R);
            case 6: return Input.GetKeyDown(KeyCode.Escape);
        }
        return false;
#endif
    }
    static bool PressedUp() { return KeyPressed(0); }
    static bool PressedDown() { return KeyPressed(1); }
    static bool PressedConfirm() { return KeyPressed(4); }
    static bool RetryPressed() { return KeyPressed(5); }
    static bool EscPressed() { return KeyPressed(6); }

    void Nav()
    {
        if (cur == Pg.Settings) SettingsInput(); else MenuInput();
    }

    void MenuInput()
    {
        if (cur == Pg.Briefing) { if (KeyPressed(2)) AdjustTime(-5f); if (KeyPressed(3)) AdjustTime(5f); }
        int n = hitIds.Count;
        if (n == 0) return;
        if (sel >= n) sel = 0;
        if (PressedDown()) sel = (sel + 1) % n;
        if (PressedUp()) sel = (sel - 1 + n) % n;
        if (PressedConfirm()) Activate(hitIds[sel]);
    }

    void OnEsc()
    {
        if (state == State.Playing)
        {
            if (!paused) PauseGame();
            else if (cur == Pg.Pause) ResumeGame();
            else { cur = Pg.Pause; sel = 0; }
        }
        else if (state == State.Intro && introTime >= MenuAt)
        {
            if (cur == Pg.Settings) { cur = settingsBack; sel = 0; }
            else if (cur == Pg.Help || cur == Pg.Care || cur == Pg.Briefing) { cur = Pg.Menu; sel = 0; }
        }
    }

    void PauseGame()
    {
        paused = true; cur = Pg.Pause; sel = 0;
        Time.timeScale = 0f; AudioListener.pause = true;
        walker.enabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void ResumeGame()
    {
        paused = false; cur = Pg.None; sel = 0;
        Time.timeScale = 1f; AudioListener.pause = false;
        walker.enabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void HandleClick(Vector2 p)
    {
        if (state == State.Playing && !paused)
        {
            for (int i = 0; i < hudHits.Count; i++) if (hudHits[i].Contains(p)) { Activate(hudIds[i]); return; }
            return;
        }
        if (cur == Pg.Briefing && timeRect.Contains(p)) { dragTime = true; TimeFromMouse(p.x); return; }
        if (cur == Pg.Settings)
        {
            for (int i = 0; i < sliderRects.Count; i++)
                if (sliderRects[i].width > 0f && sliderRects[i].Contains(p)) { dragSlider = i; sel = i; SliderFromMouse(i, p.x); return; }
            for (int i = 0; i < rowRects.Count; i++)
                if (rowRects[i].Contains(p)) { sel = i; if (i < 3) FlipToggle(i); return; }
        }
        for (int i = 0; i < menuHits.Count; i++)
            if (menuHits[i].Contains(p)) { Activate(hitIds[i]); return; }
    }

    void RestartScene(bool skip)
    {
        skipIntro = skip;
        Time.timeScale = 1f; AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void Activate(string id)
    {
        switch (id)
        {
            case "play": cur = Pg.Briefing; sel = 0; break;
            case "help": cur = Pg.Help; sel = 0; break;
            case "settings": settingsBack = cur; cur = Pg.Settings; sel = 0; break;
            case "back": cur = (cur == Pg.Settings) ? settingsBack : Pg.Menu; sel = 0; break;
            case "ready": careSeen = true; cur = Pg.Briefing; sel = 0; break;
            case "start": Begin(); break;
            case "resume": ResumeGame(); break;
            case "calm": cur = Pg.Breathe; sel = 0; break;
            case "continue": if (paused) ResumeGame(); else { cur = Pg.None; sel = 0; } break;
            case "breathe": cur = Pg.Breathe; sel = 0; break;
            case "retry": quickStart = true; RestartScene(true); break;
            case "next": level++; RestartScene(true); break;
            case "pause": if (state == State.Playing && !paused) PauseGame(); break;
            case "leave":
            case "menu": level = 1; RestartScene(false); break;
            case "exit":
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    // ---------- settings ----------
    static readonly string[] SettingNames = { "Seated mode", "Vignette", "Snap turn", "Camera shake", "Tunnel vision", "Distortion", "Volume" };

    static bool GetToggle(int i) { return i == 0 ? MMSettings.Seated : i == 1 ? MMSettings.Vignette : MMSettings.SnapTurn; }
    static void FlipToggle(int i)
    {
        if (i == 0) MMSettings.Seated = !MMSettings.Seated;
        else if (i == 1) MMSettings.Vignette = !MMSettings.Vignette;
        else MMSettings.SnapTurn = !MMSettings.SnapTurn;
        PlayerPrefs.Save();
    }
    static int GetSlider(int i) { return i == 3 ? MMSettings.Shake : i == 4 ? MMSettings.Tunnel : i == 5 ? MMSettings.Blur : MMSettings.Volume; }
    static void SetSlider(int i, int v)
    {
        if (i == 3) MMSettings.Shake = v; else if (i == 4) MMSettings.Tunnel = v; else if (i == 5) MMSettings.Blur = v; else MMSettings.Volume = v;
        PlayerPrefs.Save();
    }

    void SliderFromMouse(int i, float mx)
    {
        var r = sliderRects[i];
        SetSlider(i, Mathf.RoundToInt(Mathf.Clamp01((mx - r.x) / r.width) * 100f));
    }

    void SettingsInput()
    {
        const int rows = 7;                     // 0..6 settings, 7 = Back
        if (PressedDown()) sel = (sel + 1) % (rows + 1);
        if (PressedUp()) sel = (sel - 1 + rows + 1) % (rows + 1);
        if (sel >= rows) { if (PressedConfirm()) { cur = settingsBack; sel = 0; } return; }
        if (sel < 3) { if (KeyPressed(2) || KeyPressed(3) || PressedConfirm()) FlipToggle(sel); }
        else
        {
            if (KeyPressed(2)) SetSlider(sel, GetSlider(sel) - 10);
            if (KeyPressed(3)) SetSlider(sel, GetSlider(sel) + 10);
        }
    }

    // ---------- entry ----------
    void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0) HandleClick(e.mousePosition);
        else if (e.type == EventType.MouseDrag && dragSlider >= 0) SliderFromMouse(dragSlider, e.mousePosition.x);
        else if (e.type == EventType.MouseDrag && dragTime) TimeFromMouse(e.mousePosition.x);
        else if (e.type == EventType.MouseUp) { dragSlider = -1; dragTime = false; }
        if (e.type != EventType.Repaint) return;
        if (MMVR.WorldUI)
        {
            var rig = MMVRRig.I;
            if (rig == null) return;
            rig.menuMode = state != State.Playing || paused || mapOpen;
            rig.BeginUI(); DrawAllScreens(); rig.DrawCursor(); rig.EndUI();
            return;
        }
        DrawAllScreens();
    }

    void DrawAllScreens()
    {
        MindMazeUI.Init();
        GUI.depth = -10;
        menuHits.Clear(); hitIds.Clear(); hudHits.Clear(); hudIds.Clear();
        if (CapHideUI) return;
        if (BlockoutMode.Active) { DrawBlockoutHud(); return; }
        if (state == State.Intro) DrawIntro();
        else if (state == State.Playing) { if (paused) DrawPause(); else { DrawMap(); DrawHud(); DrawMapHint(); } }
        else DrawEnd();
    }

    // ---------- menu list ----------
    void MenuList(string[] labels, string[] ids, float y0, float alpha, int selOffset = 0)
    {
        float W = MindMazeUI.W, s = MindMazeUI.S, step = 86f * s;
        Vector2 m = Ptr();
        bool moved = (m - lastMouse).sqrMagnitude > 0.5f;
        for (int i = 0; i < labels.Length; i++)
        {
            float w = MindMazeUI.TrackedWidth(labels[i].ToUpperInvariant(), MindMazeUI.BodyMedium, 40f * s, 0.1f);
            var r = new Rect(W * 0.5f - w * 0.5f - 40f * s, y0 + i * step - 40f * s, w + 80f * s, 80f * s);
            if (moved && r.Contains(m)) sel = i + selOffset;
            menuHits.Add(r); hitIds.Add(ids[i]);
        }
        lastMouse = m;
        if (selOffset == 0 && sel >= labels.Length) sel = 0;
        for (int i = 0; i < labels.Length; i++)
            MMStyle.MenuItem(new Vector2(W * 0.5f, y0 + i * step), labels[i], sel == i + selOffset, alpha);
    }

    void Backdrop(float a)
    {
        float W = MindMazeUI.W, H = MindMazeUI.H;
        MindMazeUI.Tex(new Rect(W * 0.12f, -H * 0.15f, W * 0.76f, H * 1.3f), MindMazeUI.Glow, new Color(0, 0, 0, 0.7f * a));
    }

    // ---------- title / menus before a run ----------
    void DrawIntro()
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S, t = introTime;

        // fade up from black into a dim view of the towering walls
        MindMazeUI.FillScreen(new Color(0, 0, 0, Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((t - 0.3f) / 2.5f))));
        MindMazeUI.VignetteScreen(new Color(0, 0, 0, 0.9f), 1.15f);

        // "You are entering a maze of thoughts..." typed out, then dissolves
        string line = "You are entering a maze of thoughts…";
        int chars = Mathf.Clamp(Mathf.FloorToInt((t - 0.8f) * 14f), 0, line.Length);
        float aLine = t < 3.8f ? 1f : Mathf.Clamp01(1f - (t - 3.8f) / 0.9f);
        if (chars > 0 && aLine > 0f)
        {
            float fs = 34f * s;
            string shown = line.Substring(0, chars);
            float full = MindMazeUI.Measure(line, MindMazeUI.Italic, fs).x;
            MindMazeUI.Label(new Rect(W * 0.5f - full * 0.5f, H * 0.5f - fs, full + 40, fs * 2f), shown, MindMazeUI.Italic, fs, MindMazeUI.A(MMStyle.Paper, 0.9f * aLine), TextAnchor.MiddleLeft);
        }

        float a = Mathf.Clamp01((t - MenuAt) / 1.2f);
        if (a <= 0f) return;
        Backdrop(a);

        switch (cur)
        {
            case Pg.Help: DrawHelp(a); break;
            case Pg.Care: DrawCare(a); break;
            case Pg.Settings: DrawSettings(a); break;
            case Pg.Briefing: DrawBriefing(a); break;
            default:
                {
                    float ts = 170f * s, y1 = 215f * s;
                    MMStyle.Stencil(new Vector2(W * 0.5f, y1), "Mind", ts, a);
                    MMStyle.Stencil(new Vector2(W * 0.5f, y1 + ts * 0.9f), "Maze", ts, a);
                    MenuList(new[] { "Play", "Help & Guide", "Settings", "Exit" }, new[] { "play", "help", "settings", "exit" }, 580f * s, a);
                    break;
                }
        }
    }

    void DrawHelp(float a)
    {
        float W = MindMazeUI.W, s = MindMazeUI.S;
        MMStyle.Stencil(new Vector2(W * 0.5f, 170f * s), "Help & Guide", 120f * s, a);
        string[] names = { "Walk", "Turn", "Look around", "Breathe", "Map", "Pause" };
        string[] vals = { "Up and down arrows", "Left and right arrows", "Mouse", "Hold Space", "M or Tab", "Esc" };
        for (int i = 0; i < names.Length; i++)
        {
            float y = 300f * s + i * 90f * s;
            MindMazeUI.Tracked(new Vector2(W * 0.5f, y), names[i].ToUpperInvariant(), MindMazeUI.Cond, 40f * s, 0.1f, MindMazeUI.A(MMStyle.Paper, a), 0, 0.6f);
            MindMazeUI.Tracked(new Vector2(W * 0.5f, y + 44f * s), vals[i].ToUpperInvariant(), MindMazeUI.BodyMedium, 30f * s, 0.1f, MindMazeUI.A(MMStyle.Muted, a), 0, 0.5f);
        }
        MenuList(new[] { "Back" }, new[] { "back" }, 930f * s, a);
    }

    void DrawCare(float a)
    {
        float W = MindMazeUI.W, s = MindMazeUI.S;
        MMStyle.Stencil(new Vector2(W * 0.5f, 300f * s), "Before you enter", 130f * s, a);
        MMStyle.Line(new Vector2(W * 0.5f, 430f * s), "This will feel like panic. Press Esc to pause or leave at any time.", 28f * s, MMStyle.Paper, 0.9f * a, MindMazeUI.BodyMedium);
        MenuList(new[] { "I'm ready", "Settings" }, new[] { "ready", "settings" }, 640f * s, a);
    }

    // "Set your time" — the one screen between menu and maze.
    // Stripped down: just the time slider, the clock readout, and START.
    void DrawBriefing(float a)
    {
        float W = MindMazeUI.W, s = MindMazeUI.S;

        // header
        MMStyle.Stencil(new Vector2(W * 0.5f, 220f * s), "Set your time", 130f * s, a);

        // big clock readout
        MindMazeUI.Clock(new Vector2(W * 0.5f, 390f * s), MMSS(totalTime), MindMazeUI.Cond, 160f * s, MindMazeUI.A(MMStyle.Paper, a));

        // drag slider
        float tw = 520f * s, ty = 490f * s, x = W * 0.5f - tw * 0.5f;
        timeRect = new Rect(x, ty - 26f * s, tw, 52f * s);
        float v = Mathf.InverseLerp(timeMin, timeMax, totalTime);
        MindMazeUI.Fill(new Rect(x, ty - 2f * s, tw, 4f * s), new Color(1, 1, 1, 0.3f * a));
        MindMazeUI.Fill(new Rect(x, ty - 2f * s, tw * v, 4f * s), MindMazeUI.A(MMStyle.Paper, a));
        MindMazeUI.Tex(new Rect(x + tw * v - 12f * s, ty - 12f * s, 24f * s, 24f * s), MMStyle.Dot, MindMazeUI.A(MMStyle.Paper, a));
        MindMazeUI.Tracked(new Vector2(x, ty + 42f * s), MMSS(timeMin), MindMazeUI.Cond, 24f * s, 0.12f, MindMazeUI.A(MMStyle.Muted, a), -1, 0.6f);
        MindMazeUI.Tracked(new Vector2(x + tw, ty + 42f * s), MMSS(timeMax), MindMazeUI.Cond, 24f * s, 0.12f, MindMazeUI.A(MMStyle.Muted, a), 1, 0.6f);
        MMStyle.Line(new Vector2(W * 0.5f, ty + 86f * s), "← drag or use arrow keys →", 24f * s, MMStyle.Muted, 0.7f * a, MindMazeUI.BodyMedium);

        // just START — no settings clutter
        MenuList(new[] { "Start" }, new[] { "start" }, 680f * s, a);
    }

    void DrawSettings(float a)
    {
        float W = MindMazeUI.W, s = MindMazeUI.S;
        MMStyle.Stencil(new Vector2(W * 0.5f, 140f * s), "Settings", 120f * s, a);
        rowRects.Clear(); sliderRects.Clear();
        Vector2 m = Ptr();
        bool moved = (m - lastMouse).sqrMagnitude > 0.5f;
        float x0 = W * 0.5f - 476f * s, vx = x0 + 570f * s;
        for (int i = 0; i < SettingNames.Length; i++)
        {
            float y = 300f * s + i * 84f * s;
            var row = new Rect(x0, y - 40f * s, 952f * s, 80f * s);
            rowRects.Add(row);
            if (moved && row.Contains(m)) sel = i;
            MMStyle.MenuItem(new Vector2(x0 + 260f * s, y), SettingNames[i], sel == i, a);
            if (i < 3)
            {
                bool on = GetToggle(i);
                MindMazeUI.Tracked(new Vector2(vx, y), on ? "ON" : "OFF", MindMazeUI.Cond, 40f * s, 0.1f, MindMazeUI.A(on ? MMStyle.Paper : MMStyle.Muted, a), -1, 0.6f);
                sliderRects.Add(Rect.zero);
            }
            else
            {
                float v = GetSlider(i) / 100f;
                var tr = new Rect(vx, y - 2f * s, 382f * s, 4f * s);
                MindMazeUI.Fill(tr, new Color(1, 1, 1, 0.3f * a));
                MindMazeUI.Fill(new Rect(tr.x, tr.y, tr.width * v, tr.height), MindMazeUI.A(MMStyle.Paper, a));
                MindMazeUI.Tex(new Rect(tr.x + tr.width * v - 11f * s, y - 11f * s, 22f * s, 22f * s), MMStyle.Dot, MindMazeUI.A(MMStyle.Paper, a));
                sliderRects.Add(new Rect(vx, y - 24f * s, 382f * s, 48f * s));
            }
        }
        MenuList(new[] { "Back" }, new[] { "back" }, 930f * s, a, 7);
    }

    // ---------- pause + breathe ----------
    void DrawPause()
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        MindMazeUI.FillScreen(new Color(0, 0, 0, 0.6f));
        MindMazeUI.VignetteScreen(new Color(0, 0, 0, 0.9f), 1.15f);
        Backdrop(1f);
        if (cur == Pg.Settings) { DrawSettings(1f); return; }
        if (cur == Pg.Breathe) { DrawBreathe(1f); return; }
        MMStyle.Stencil(new Vector2(W * 0.5f, 190f * s), "Paused", 150f * s, 1f);
        MenuList(new[] { "Resume", "Restart", "Calm exit", "Settings", "Leave game" }, new[] { "resume", "retry", "calm", "settings", "menu" }, 380f * s, 1f);
    }

    void DrawBreathe(float a)
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        float p = Mathf.Repeat(Time.unscaledTime, 8f) / 8f;          // 4 s in, 4 s out
        bool inhale = p < 0.5f;
        float q = inhale ? p * 2f : (p - 0.5f) * 2f;
        float k = inhale ? Ease(q) : 1f - Ease(q);
        int n = 4 - Mathf.Min(3, (int)(q * 4f));
        Vector2 c = new Vector2(W * 0.5f, H * 0.42f);
        float outer = 300f * s, orb = Mathf.Lerp(150f, 195f, k) * s;
        MindMazeUI.Tex(new Rect(c.x - orb * 1.7f, c.y - orb * 1.7f, orb * 3.4f, orb * 3.4f), MindMazeUI.Glow, MindMazeUI.A(MMStyle.Violet, 0.45f * a));
        MindMazeUI.Tex(new Rect(c.x - outer / 0.9f, c.y - outer / 0.9f, outer / 0.9f * 2f, outer / 0.9f * 2f), MindMazeUI.Ring, MindMazeUI.A(MMStyle.Paper, 0.6f * a));
        MindMazeUI.Tex(new Rect(c.x - orb / 0.9f, c.y - orb / 0.9f, orb / 0.9f * 2f, orb / 0.9f * 2f), MindMazeUI.Ring, MindMazeUI.A(MMStyle.Paper, 0.9f * a));
        MindMazeUI.Tracked(new Vector2(c.x, c.y - 70f * s), inhale ? "BREATHE IN" : "BREATHE OUT", MindMazeUI.Cond, 30f * s, 0.1f, MindMazeUI.A(MMStyle.Paper, a), 0, 0.6f);
        MMStyle.Stencil(new Vector2(c.x, c.y + 35f * s), n.ToString(), 190f * s, a);
        MenuList(new[] { "Continue", "Menu" }, new[] { "continue", "menu" }, 880f * s, a);
    }

    // ---------- in-game HUD ----------
    void DrawHud()
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        float a = director ? director.anxiety : 0f;
        var paper = MMStyle.Paper; var violet = MMStyle.Violet;
        bool low = timeLeft < 30f;
        float lowPulse = low ? Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 0f;

        // edge pressure when time is short or panic hits
        bool panic = director && director.panicking && !breathing;
        float edge = (low ? 0.10f + 0.12f * lowPulse : 0f) + (panic ? 0.18f + 0.12f * Mathf.Abs(Mathf.Sin(Time.time * 9f)) : 0f);
        if (edge > 0f) MindMazeUI.VignetteScreen(MindMazeUI.A(violet, edge), 1.25f);

        // clock, top centre, shaking with anxiety; turns violet when time is short
        Vector2 jit = Random.insideUnitCircle * a * a * 4f * s * MMSettings.ShakeMul;
        float cx = W * 0.5f + jit.x, top = jit.y;
        MindMazeUI.Tex(new Rect(cx - 260f * s, top - 100f * s, 520f * s, 280f * s), MindMazeUI.Glow, new Color(0, 0, 0, 0.5f));
        Color clockCol = low ? Color.Lerp(paper, violet, 0.35f + 0.65f * lowPulse) : paper;
        MindMazeUI.Clock(new Vector2(cx, top + 110f * s), MMSS(timeLeft), MindMazeUI.Cond, (low ? 150f : 128f) * s, clockCol);

        if (penaltyFlash > 0f)
        {
            float k = penaltyFlash / 1.5f;
            MindMazeUI.Label(new Rect(cx - 200f * s, top + 190f * s + (k - 1f) * 30f * s, 400f * s, 100f * s), penaltyText.Replace("-", "−") + " HIT", MindMazeUI.Cond, 70f * s, MindMazeUI.A(violet, k));
        }

        // prompt under the clock (story beats)
        if (banner > 0f)
        {
            float k = banner > 2.4f ? (3f - banner) / 0.6f : Mathf.Clamp01(banner / 0.8f);
            float by = H * 0.24f;
            MindMazeUI.Tex(new Rect(W * 0.5f - 460f * s, by - 90f * s, 920f * s, 180f * s), MindMazeUI.Glow, new Color(0, 0, 0, 0.55f * k));
            MMStyle.Line(new Vector2(W * 0.5f, by), bannerText, 46f * s, violet, k);
        }

        // intrusive thoughts at the edge of vision (when not written on walls)
        foreach (var th in thoughts)
        {
            float k = (Time.time - th.born) / th.life;
            float alpha = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * Mathf.Lerp(0.35f, 0.85f, a);
            Vector2 j = Random.insideUnitCircle * (1f + a * 2f) * s;
            MindMazeUI.Label(new Rect(th.pos.x * W - 400f * s + j.x, th.pos.y * H - 40f * s + j.y, 800f * s, 80f * s), th.text.ToUpperInvariant(), MindMazeUI.Italic, th.size * 0.95f * s, MindMazeUI.A(paper, alpha));
        }

        // panic meter: one thin bar, bottom centre
        float pb = a > 0.7f ? 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(Time.time * 7f)) : 1f;
        MindMazeUI.Tracked(new Vector2(W * 0.5f, H - 120f * s), "PANIC", MindMazeUI.Cond, 34f * s, 0.2f, MindMazeUI.A(a > 0.7f ? violet : paper, pb), 0, 0.6f);
        MMStyle.ThinBar(new Rect(W * 0.5f - 400f * s, H - 84f * s, 800f * s, 18f * s), a, violet, pb);

        // breathing ring
        if (breathVis > 0f)
        {
            float p = Mathf.Repeat(Time.time, 8f) / 8f;               // 4 s in, 4 s out
            bool inhale = p < 0.5f;
            float k = inhale ? Ease(p * 2f) : 1f - Ease((p - 0.5f) * 2f);
            float rr = Mathf.Lerp(60f, 100f, k) * s;
            Vector2 c = new Vector2(W * 0.5f, H * 0.62f);
            MindMazeUI.Tex(new Rect(c.x - rr * 1.9f, c.y - rr * 1.9f, rr * 3.8f, rr * 3.8f), MindMazeUI.Glow, MindMazeUI.A(violet, 0.22f * breathVis));
            MindMazeUI.Tex(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), MindMazeUI.Ring, MindMazeUI.A(paper, 0.85f * breathVis));
            MindMazeUI.Tracked(c, inhale ? "BREATHE IN" : "BREATHE OUT", MindMazeUI.BodyMedium, 28f * s, 0.1f, MindMazeUI.A(paper, 0.95f * breathVis), 0, 0.6f);
            MMStyle.ThinBar(new Rect(c.x - 70f * s, c.y + 150f * s, 140f * s, 4f * s), breathingCalm, violet, breathVis);
        }
        else if (a > 0.5f)
        {
            float k = Mathf.Clamp01((a - 0.5f) * 4f) * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 4f)));
            MindMazeUI.Tracked(new Vector2(W * 0.5f, mapOpen ? H - 36f * s : H * 0.7f), "HOLD SPACE TO BREATHE", MindMazeUI.BodyMedium, 44f * s, 0.1f, MindMazeUI.A(paper, 0.95f * k), 0, 0.6f);
        }

        if (panic)
        {
            Vector2 j = Random.insideUnitCircle * 3f * s;
            float k = 0.45f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 9f));
            MindMazeUI.Tracked(new Vector2(W * 0.5f + j.x, H * 0.6f + j.y), "CAN'T BREATHE", MindMazeUI.Cond, 44f * s, 0.1f, MindMazeUI.A(Color.Lerp(paper, violet, 0.6f), k), 0, 0.6f);
        }
    }

    // ---------- end screens ----------
    void DrawEnd()
    {
        float W = MindMazeUI.W, H = MindMazeUI.H, s = MindMazeUI.S;
        bool won = state == State.Won;

        MindMazeUI.FillScreen(new Color(0, 0, 0, endFade * 0.85f));
        MindMazeUI.VignetteScreen(new Color(0, 0, 0, endFade), 1.1f);
        float f = Mathf.Clamp01(endFade * 1.5f);

        if (cur == Pg.Breathe) { Backdrop(1f); DrawBreathe(1f); return; }

        MMStyle.Stencil(new Vector2(W * 0.5f, 260f * s), won ? "You made it out." : "Time's up", Mathf.Min(150f * s, W * 0.1f), f);
        string msg = won ? "Failure isn't the end of the game. It's the next block." : "For many, this is every day.";
        MMStyle.Line(new Vector2(W * 0.5f, 400f * s), msg, (won ? 30f : 38f) * s, won ? MMStyle.Paper : MMStyle.Violet, f);

        if (endFade > 0.6f)
        {
            float a = Mathf.Clamp01((endFade - 0.6f) * 4f);
            if (won) MenuList(new[] { "Next level", "Replay", "Main menu" }, new[] { "next", "retry", "menu" }, 560f * s, a);
            else MenuList(new[] { "Try again", "Take a breath", "Main menu" }, new[] { "retry", "breathe", "menu" }, 560f * s, a);
        }
    }

    // ---------- greybox placeholder UI (process videos) ----------
    GUIStyle dbg, dbgBig;
    void DrawBlockoutHud()
    {
        float s = MindMazeUI.S, W = MindMazeUI.W, H = MindMazeUI.H;
        if (dbg == null) { dbg = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft }; dbgBig = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter }; }
        dbg.fontSize = Mathf.RoundToInt(18 * s); dbg.padding = new RectOffset(Mathf.RoundToInt(12 * s), 0, Mathf.RoundToInt(10 * s), 0);
        dbgBig.fontSize = Mathf.RoundToInt(34 * s);
        float a = director ? director.anxiety : 0f;
        string info = "BLOCKOUT  build 0.1\n" +
                      "state: " + state + "\n" +
                      "time left: " + timeLeft.ToString("0.0") + " s\n" +
                      "anxiety: " + a.ToString("0.00") + (director && director.panicking ? "  (PANIC)" : "") + "\n" +
                      "breathing: " + (breathing ? "yes" : "no") + "   hits: " + hits + "\n" +
                      "goal dist: " + (walker ? Vector3.Distance(Flat(walker.transform.position), Flat(goal)).ToString("0.0") : "-") + " m";
        GUI.Box(new Rect(20 * s, 20 * s, 380 * s, 190 * s), info, dbg);
        if (state == State.Intro)
            GUI.Box(new Rect(W * 0.5f - 360 * s, H * 0.4f, 720 * s, 140 * s), "MIND MAZE  [title placeholder]\npress SPACE to start", dbgBig);
        else if (state == State.Playing && banner > 0f)
            GUI.Box(new Rect(W * 0.5f - 300 * s, H * 0.3f, 600 * s, 80 * s), "[banner] " + bannerText, dbgBig);
        else if (state != State.Playing)
            GUI.Box(new Rect(W * 0.5f - 360 * s, H * 0.4f, 720 * s, 140 * s), (state == State.Won ? "WIN" : "LOSE") + "  [end screen placeholder]\npress SPACE to restart", dbgBig);
    }


float timeMin = 75f, timeMax = 210f; Rect timeRect; bool dragTime;
    static float TimeFrac { get { return PlayerPrefs.GetFloat("mm_time_frac", 0.5f); } set { PlayerPrefs.SetFloat("mm_time_frac", Mathf.Clamp01(value)); PlayerPrefs.Save(); } }
    static float Round5(float v) { return Mathf.Round(v / 5f) * 5f; }
    // Fixed per-level time windows — intentionally tight, anxiety is the point.
    // Level 1: 1:30–2:30  |  Level 2: 1:15–2:00  |  Level 3+: 1:00–1:30
    void SetupTimeRange()
    {
        timeMin = level == 1 ? 90f : level == 2 ? 75f : 60f;
        timeMax = level == 1 ? 150f : level == 2 ? 120f : 90f;
        ApplyTime();
    }
        void ApplyTime() { totalTime = timeLeft = Mathf.Clamp(Round5(Mathf.Lerp(timeMin, timeMax, TimeFrac)), timeMin, timeMax); }
    void AdjustTime(float seconds) { TimeFrac = Mathf.InverseLerp(timeMin, timeMax, Mathf.Clamp(totalTime + seconds, timeMin, timeMax)); ApplyTime(); }
    void TimeFromMouse(float mx) { TimeFrac = Mathf.Clamp01((mx - timeRect.x) / timeRect.width); ApplyTime(); }
}
