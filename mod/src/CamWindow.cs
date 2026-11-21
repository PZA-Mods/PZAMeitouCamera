// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The window on PZA's Meitou UI (a real Kenshi window: caption, close and minimize), the plugin's camera menu (v0.7.0 - v0.21) in three tabs:
//   CAMERAS   one button per camera (the active one shown "> name <"; clicking it again turns the camera off), Off-centre / Dutch angle /
//             0.5x lens on / off, Save angle as Low, and (0.2.0) Tilt-Shift Off / Miniature / HD-2D / Diorama through PZA's Looks
//   SETTINGS  the plugin's settings column: one row per value, - / + / Reset, and Reset all settings
//   HELP      the help text, a scrolling list wrapped in the real font
// and the tiny button while the window is closed (click = camera off / back to the last one, + or a double click = the window, drag, wheel =
// opacity). Every pick is a direct button (no cycling, PZA UI rules); picks never close the window. Places and opacity are saved in the settings.
using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using PZA.MeitouUi;
using L = PZA.MeitouUi.PzaLayout;

namespace PZA.Camera.Meitou;

public static class CamWindow
{
    public const string Id = "pza.camera";
    public static PzaWindow Window = null!;
    public static PzaTinyButton Tiny = null!;
    public static readonly string[] Extras = [nameof(CamSettings.OffCentre), nameof(CamSettings.Dutch), nameof(CamSettings.WideLens)];
    /// <summary>0.3.0: the VIEW tab's on / off buttons.</summary>
    public static readonly string[] ViewToggles = [nameof(CamSettings.ZoomStops), nameof(CamSettings.KeepInView), nameof(CamSettings.CinematicZoomLock)];
    /// <summary>0.5.0: the SHOTS tab's on / off buttons (called-shot shots, the kill cam shots).</summary>
    public static readonly string[] ShotToggles = [nameof(CamSettings.Shots), nameof(CamSettings.KillCamShots), nameof(CamSettings.ShotFocus)];

    /// <summary>0.2.1: the window's height (fraction of the screen) and the lowest its bottom may sit. Meitou opens a PZA window centred, and
    /// the game's bottom HUD (Kenshi_MainPanel.layout: FloorPanel from 0.657, the message box MessagePanel from 0.713, where Meitou shows its
    /// notices over our window) hid 0.2.0's Tilt-Shift row at 1080p (seen in game 2026-11-17). So the window is shorter, opens higher, and a
    /// saved place lower than this is pulled back up (also a place saved by 0.1.0 / 0.2.0).</summary>
    public const double WindowH = 0.58, HudTop = 0.70;
    /// <summary>The highest offset (fraction of the screen, + = down) that keeps the bottom above <see cref="HudTop"/>.</summary>
    public const double MaxPlaceY = HudTop - (0.5 + WindowH / 2);

    static bool s_started;
    static Cam s_lastOn = Cam.Diorama;

    public static string LayoutXml()
    {
        var cams = new List<string>();
        var all = Enum.GetValues<Cam>();
        for (int i = 0; i < all.Length; i++)
            cams.Add(L.Button("Cam_" + all[i], i % 2 == 0 ? 0.03 : 0.515, 0.02 + (i / 2) * 0.095, 0.455, 0.083));
        for (int i = 0; i < Extras.Length; i++)
            cams.Add(L.Button("Tog_" + Extras[i], 0.03 + i * 0.3175, 0.685, 0.305, 0.072));
        cams.Add(L.Button("Capture", 0.03, 0.77, 0.94, 0.072));
        // 0.2.0: Tilt-Shift through PZA's Looks (one direct button per look, the current one shown "> name <")
        cams.Add(L.Text("lbTs", 0.03, 0.852, 0.94, 0.06));
        for (int i = 0; i < LooksLink.Buttons.Length; i++)
            cams.Add(L.Button(LooksLink.Buttons[i].Button, 0.03 + i * 0.2375, 0.918, 0.2275, 0.07));

        var rows = new List<string>();
        for (int i = 0; i < Rows.All.Length; i++)
        {
            string k = Rows.All[i].Key;
            double y = 0.02 + i * 0.085;
            rows.Add(L.Text("Lb_" + k, 0.03, y, 0.47, 0.072));
            rows.Add(L.Button("Minus_" + k, 0.51, y, 0.09, 0.072));
            rows.Add(L.Button("Plus_" + k, 0.61, y, 0.09, 0.072));
            rows.Add(L.Button("Reset_" + k, 0.71, y, 0.26, 0.072));
        }
        rows.Add(L.Button("ResetAll", 0.03, 0.88, 0.94, 0.075));

        // 0.3.0: VIEW - Zoom stops, Keep in view, the settle steps (45-degree lock), the Diorama look
        var view = new List<string>
        {
            L.Button("Tog_ZoomStops", 0.03, 0.03, 0.94, 0.075),
            L.Text("lbZs", 0.03, 0.115, 0.94, 0.065),
            L.Button("ZsNear", 0.03, 0.19, 0.2275, 0.075),
            L.Button("ZsMid", 0.2675, 0.19, 0.2275, 0.075),
            L.Button("ZsFar", 0.505, 0.19, 0.2275, 0.075),
            L.Button("ZsReset", 0.7425, 0.19, 0.2275, 0.075),
            L.Button("Tog_KeepInView", 0.03, 0.30, 0.94, 0.075),
        };
        for (int i = 0; i < Rows.View.Length; i++)
        {
            string k = Rows.View[i].Key;
            double y = 0.41 + i * 0.085;
            view.Add(L.Text("Lb_" + k, 0.03, y, 0.47, 0.072));
            view.Add(L.Button("Minus_" + k, 0.51, y, 0.09, 0.072));
            view.Add(L.Button("Plus_" + k, 0.61, y, 0.09, 0.072));
            view.Add(L.Button("Reset_" + k, 0.71, y, 0.26, 0.072));
        }
        view.Add(L.Button("Look", 0.03, 0.69, 0.455, 0.075));
        view.Add(L.Button("Fish", 0.515, 0.69, 0.455, 0.075));   // 0.4.1: the plugin's Fisheye button
        view.Add(L.Button("Tog_CinematicZoomLock", 0.03, 0.79, 0.94, 0.075));   // 0.4.0

        // 0.5.0: SHOTS - called-shot shots (CameraApi), the kill cam shots, a preview, and the shots' speeds and lengths. 0.6.0 (the user,
        // 2026-11-21): only what the player's mods can use: no tab without PZA's SlowMo or a called-shot mod, the kill cam and time rows only
        // with PZA's SlowMo, the called-shot switch only once such a mod is known. Decided when the window is built (each game load: every mod
        // has loaded by then, and a mod is only added with a restart).
        (ShotsSlowMo, ShotsCaller) = ShotDeps();
        ShotsTab = ShotsSlowMo || ShotsCaller;
        var shots = new List<string>();
        double sy = 0.03;
        if (ShotsCaller) { shots.Add(L.Button("Tog_Shots", 0.03, sy, 0.94, 0.075)); sy += 0.09; }
        if (ShotsSlowMo)
        {
            shots.Add(L.Button("Tog_KillCamShots", 0.03, sy, 0.455, 0.075));
            shots.Add(L.Button("Tog_ShotFocus", 0.515, sy, 0.455, 0.075));
        }
        else shots.Add(L.Button("Tog_ShotFocus", 0.03, sy, 0.94, 0.075));
        sy += 0.09;
        shots.Add(L.Button("ShotPreview", 0.03, sy, 0.94, 0.075));
        sy += 0.1;
        foreach (var r in ShotRows)
        {
            string k = r.Key;
            shots.Add(L.Text("Lb_" + k, 0.03, sy, 0.47, 0.072));
            shots.Add(L.Button("Minus_" + k, 0.51, sy, 0.09, 0.072));
            shots.Add(L.Button("Plus_" + k, 0.61, sy, 0.09, 0.072));
            shots.Add(L.Button("Reset_" + k, 0.71, sy, 0.26, 0.072));
            sy += 0.084;
        }

        var tabs = new List<(string, string[])> { ("TabCams", [.. cams]), ("TabView", [.. view]), ("TabSettings", [.. rows]) };
        if (ShotsTab) tabs.Add(("TabShots", [.. shots]));
        tabs.Add(L.HelpList("TabHelp", "lstHelp"));
        return L.Doc(L.WindowMin("Root", "PZA'S CAMERA", 0.35, (1 - WindowH) / 2, 0.30, WindowH,
            L.Tabs("Tabs", 0.03, 0.02, 0.94, 0.95, [.. tabs])));
    }

    /// <summary>0.6.0: what the SHOTS tab was built with (set by LayoutXml).</summary>
    public static bool ShotsTab, ShotsSlowMo, ShotsCaller;
    /// <summary>The offline check's stand-in for "PZA's SlowMo is here, a called-shot mod is known".</summary>
    public static Func<(bool SlowMo, bool Caller)>? DepsOverride;
    public static (bool SlowMo, bool Caller) ShotDeps() => DepsOverride?.Invoke() ?? (SlowLink.Available, Shots.CallerSeen);

    /// <summary>The time rows need PZA's SlowMo (they are its speeds).</summary>
    static readonly string[] TimeRows = [nameof(CamSettings.ShotPickSpeed), nameof(CamSettings.ShotSpeed), nameof(CamSettings.ShotBetweenSpeed), nameof(CamSettings.ShotImpactSpeed)];

    /// <summary>The SHOTS rows the window shows (as built).</summary>
    public static IEnumerable<Rows.Row> ShotRows => ShotsTab ? Rows.Shots.Where(r => ShotsSlowMo || !TimeRows.Contains(r.Key)) : [];

    public static void Setup()
    {
        Window = new PzaWindow(Id, LayoutXml)
        {
            Minimizable = true,
            Bind = Bind,
            Event = OnEvent,
            LoadPlace = () =>
            {
                double y = Driver.Settings.WindowPlaceY;
                if (y > MaxPlaceY + 1e-3) { y = MaxPlaceY; Counters.Add("window.kept_above_hud"); Log.Line($"window place {Driver.Settings.WindowPlaceY:0.###} would reach the HUD: opened at {y:0.###}"); }
                return new Vector2((float)Driver.Settings.WindowPlaceX, (float)y);
            },
            SavePlace = p =>
            {
                Driver.SetSetting(nameof(CamSettings.WindowPlaceX), Math.Round((double)p.X, 3));
                Driver.SetSetting(nameof(CamSettings.WindowPlaceY), Math.Round((double)p.Y, 3));
                Counters.Add("window.place_saved");
            },
            OpenChanged = open =>
            {
                if (!Driver.Settings.ShowWindow) return;
                Driver.SetSetting(nameof(CamSettings.Tiny), !open);
                Counters.Add(open ? "window.full" : "window.tiny");
            },
        };
        PzaUi.Add(Window);

        Tiny = new PzaTinyButton("camera")
        {
            Caption = () => Strings.TF("mini", Strings.CamName(Driver.Current)),
            Width = 170,   // "Camera: Pixel Isometric" at 1080p
            Visible = () => Driver.Settings.ShowWindow && !Window.IsOpen,
            Click = () => { Driver.Queue(TinyToggle, "tiny button"); Counters.Add("tiny.click"); },
            DoubleClick = () => { Driver.Queue(TinyToggle, "tiny button double click (undo)"); Window.Open(); Counters.Add("tiny.double"); },
            OpenFull = () => { Window.Open(); Counters.Add("tiny.plus"); },
            LoadPlace = () => new Vector2((float)Driver.Settings.TinyPlaceX, (float)Driver.Settings.TinyPlaceY),
            SavePlace = p =>
            {
                Driver.SetSetting(nameof(CamSettings.TinyPlaceX), Math.Round((double)p.X, 3));
                Driver.SetSetting(nameof(CamSettings.TinyPlaceY), Math.Round((double)p.Y, 3));
                Counters.Add("tiny.moved");
            },
            LoadOpacity = () => Driver.Settings.TinyOpacity,
            SaveOpacity = a => { Driver.SetSetting(nameof(CamSettings.TinyOpacity), a); Counters.Add("tiny.opacity"); },
        };
        PzaUi.Add(Tiny);
    }

    /// <summary>The tiny button: the camera off, or back to the last one that was on.</summary>
    public static void TinyToggle()
    {
        if (Driver.Current != Cam.Off) { s_lastOn = Driver.Current; Driver.Pick(Cam.Off, "tiny button"); }
        else Driver.Pick(s_lastOn, "tiny button");
    }

    /// <summary>Shift+K: the window open or hidden (shown again even when the window was switched off in the settings).</summary>
    public static void HotkeyToggle()
    {
        if (!Driver.Settings.ShowWindow) Driver.SetSetting(nameof(CamSettings.ShowWindow), true);
        if (Window.IsOpen) Window.Close(); else Window.Open();
        Counters.Add("window.hotkey");
    }

    static bool ShotToggleShown(string key) => ShotsTab && key switch
    {
        nameof(CamSettings.Shots) => ShotsCaller,
        nameof(CamSettings.KillCamShots) => ShotsSlowMo,
        _ => true,
    };

    public static string Value(string key)
    {
        double v = (double)typeof(CamSettings).GetProperty(key)!.GetValue(Driver.Settings)!;
        if (Rows.Shots.Any(r => r.Key == key)) return v.ToString("0.0#", CultureInfo.InvariantCulture);   // 0.5.0: speeds and seconds
        return key == nameof(CamSettings.WideZoom) ? v.ToString("0.00", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);
    }

    static void Bind(UiBindings b)
    {
        b.Text["Root"] = Strings.T("win.title");
        PzaWindow.TabText(b, "Tabs", 0, Strings.T("tab.cams"));
        PzaWindow.TabText(b, "Tabs", 1, Strings.T("tab.view"));
        PzaWindow.TabText(b, "Tabs", 2, Strings.T("tab.settings"));
        if (ShotsTab) PzaWindow.TabText(b, "Tabs", 3, Strings.T("tab.shots"));
        PzaWindow.TabText(b, "Tabs", ShotsTab ? 4 : 3, Strings.T("tab.help"));
        var names = new List<string>();
        foreach (var c in Enum.GetValues<Cam>())
        {
            string n = "Cam_" + c;
            bool hidden = !Driver.Settings.Isometric && CamCore.Hideable(c);   // 0.4.0: the plugin's Isometric=0
            if (!hidden) names.Add(n);
            bool active = c == Driver.Current || (c == Cam.Off && Driver.Current == Cam.Off);
            b.Text[n] = active ? Strings.TF("active", Strings.CamName(c)) : Strings.CamName(c);
            PzaWindow.Tooltip(b, n, Strings.T(hidden ? "tip.hidden" : "tip." + c));
        }
        foreach (var k in Extras.Concat(ViewToggles).Concat(ShotToggles.Where(ShotToggleShown)))
        {
            string n = "Tog_" + k;
            names.Add(n);
            bool on = (bool)typeof(CamSettings).GetProperty(k)!.GetValue(Driver.Settings)!;
            b.Text[n] = Strings.T($"tog.{k}.{(on ? "on" : "off")}");
            PzaWindow.Tooltip(b, n, Strings.T("tip." + k));
        }
        // Tilt-Shift: read every frame, so a change on the Settings page shows too; disabled with a note without PZA's Looks
        string? ts = LooksLink.Current;
        b.Text["lbTs"] = Strings.T(ts is null ? "ts.missing" : "ts.label");
        PzaWindow.Tooltip(b, "lbTs", Strings.T("tip.ts"));
        foreach (var (button, name) in LooksLink.Buttons)
        {
            string shown = Strings.T("ts." + button[3..]);
            b.Text[button] = ts == name ? Strings.TF("active", shown) : shown;
            PzaWindow.Tooltip(b, button, Strings.T(ts is null ? "ts.missing" : "tip.ts." + button[3..]));
            if (ts is not null) names.Add(button);
        }
        names.Add("Capture");
        b.Text["Capture"] = Strings.T("btn.capture");
        PzaWindow.Tooltip(b, "Capture", Strings.T("tip.capture"));
        // 0.3.0: VIEW
        var sc = Driver.StopsCamera;
        b.Text["lbZs"] = Strings.TF("zs.label", [Strings.CamName(sc), .. Driver.StopsFor(sc).Select(x => x.ToString("0", CultureInfo.InvariantCulture))]);
        PzaWindow.Tooltip(b, "lbZs", Strings.T("tip.ZoomStops"));
        foreach (var (n, key) in new[] { ("ZsNear", "zs.near"), ("ZsMid", "zs.mid"), ("ZsFar", "zs.far"), ("ZsReset", "zs.reset") })
        {
            b.Text[n] = Strings.T(key);
            PzaWindow.Tooltip(b, n, Strings.T("tip." + key));
            names.Add(n);
        }
        b.Text["Look"] = Strings.T(Driver.LookOn ? "look.on" : "look.off");
        PzaWindow.Tooltip(b, "Look", Strings.T(ts is null ? "ts.missing" : "tip.look"));
        if (ts is not null || Driver.LookOn) names.Add("Look");
        if (ShotsTab)
        {
            b.Text["ShotPreview"] = Strings.T("btn.preview");
            PzaWindow.Tooltip(b, "ShotPreview", Strings.T("tip.preview"));
            if (Driver.Settings.Shots) names.Add("ShotPreview");
        }
        string? fish = LooksLink.Fisheye;
        b.Text["Fish"] = Strings.T(fish is not null && fish != "Off" ? "fish.on" : "fish.off");
        PzaWindow.Tooltip(b, "Fish", Strings.T(fish is null ? "fish.missing" : "tip.fish"));
        if (fish is not null) names.Add("Fish");
        foreach (var r in Rows.All.Concat(Rows.View).Concat(ShotRows))
        {
            b.Text["Lb_" + r.Key] = Strings.TF("row." + r.Key, Value(r.Key));
            PzaWindow.Tooltip(b, "Lb_" + r.Key, Strings.T("tiprow." + r.Key));
            b.Text["Minus_" + r.Key] = "-";
            b.Text["Plus_" + r.Key] = "+";
            b.Text["Reset_" + r.Key] = Strings.T("btn.reset");
            PzaWindow.Tooltip(b, "Minus_" + r.Key, Strings.T("tip.minus"));
            PzaWindow.Tooltip(b, "Plus_" + r.Key, Strings.T("tip.plus"));
            PzaWindow.Tooltip(b, "Reset_" + r.Key, Strings.T("tip.reset"));
            names.AddRange(["Minus_" + r.Key, "Plus_" + r.Key, "Reset_" + r.Key]);
        }
        names.Add("ResetAll");
        b.Text["ResetAll"] = Strings.T("btn.resetall");
        PzaWindow.Enable(b, [.. names]);
        Window.Paragraphs(b, "lstHelp", string.Join("\n\n", Enumerable.Range(1, 14).Select(i => Strings.T("help." + i))));
    }

    static void OnEvent(MyGuiEvent e)
    {
        string a = e.Action ?? "";
        Counters.Add("event." + a);
        int u = a.IndexOf('_');
        string head = u < 0 ? a : a[..u], tail = u < 0 ? "" : a[(u + 1)..];
        switch (head)
        {
            case "Cam" when Enum.TryParse<Cam>(tail, out var c): Driver.Queue(() => Driver.Pick(c, "window"), "window " + a); break;
            case "Tog": Driver.Queue(() => Driver.ToggleExtra(tail, "window"), "window " + a); break;
            case "ZsNear": Driver.Queue(() => Driver.SaveStop(0, "window Near"), "window " + a); break;
            case "ZsMid": Driver.Queue(() => Driver.SaveStop(1, "window Mid"), "window " + a); break;
            case "ZsFar": Driver.Queue(() => Driver.SaveStop(2, "window Far"), "window " + a); break;
            case "ZsReset": Driver.Queue(() => Driver.SaveStop(-1, "window Reset stops"), "window " + a); break;
            case "Look": Driver.Queue(() => Driver.ToggleLook("window"), "window " + a); break;
            case "ShotPreview": Driver.Queue(() => Shots.Preview("window"), "window " + a); break;
            case "Fish": Driver.Queue(() => Driver.ToggleFisheye("window"), "window " + a); break;
            case "Ts":
                // on the interface thread, as PZA's Looks asks; its pass reads the value at the next frame
                if (LooksLink.Buttons.FirstOrDefault(x => x.Button == a) is { Name: { } n }) LooksLink.Set(n, "window");
                break;
            case "Capture": Driver.Queue(() => Driver.CaptureLow("window"), "window " + a); break;
            case "Minus": Driver.Queue(() => Driver.StepRow(tail, -1, "window -"), "window " + a); break;
            case "Plus": Driver.Queue(() => Driver.StepRow(tail, 1, "window +"), "window " + a); break;
            case "Reset": Driver.Queue(() => Driver.StepRow(tail, 0, "window Reset"), "window " + a); break;
            case "ResetAll": Driver.Queue(() => Driver.ResetAll("window"), "window " + a); break;
        }
    }

    /// <summary>Once a frame (PzaUi.EachFrame): the window's first opening, the window switched off, the counter line.</summary>
    public static void EachFrame()
    {
        Log.Tick();
        if (!s_started)
        {
            s_started = true;
            if (Driver.Settings.ShowWindow && !Driver.Settings.Tiny) Window.Open();
        }
        if (!Driver.Settings.ShowWindow && Window.IsOpen) Window.Close();
    }
}
