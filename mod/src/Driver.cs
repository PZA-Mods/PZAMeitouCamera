// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The behaviour, as the RE_Kenshi plugin's, in two layers (route: the port notes):
//   rig   (StrategyCamera.Update postfix, each control tick): only the YAW. The strategy camera's heading is what WASD / arrow panning and the
//         follow camera use, so the turning cameras (Turntable, Chase, Hip level, Profile) and the settling ones (Isometric, Side view) turn
//         the rig itself, through its public Rotate. The rig's tilt, boom and pivot height stay the game's.
//   drawn (GameLoop.PlaceSceneCameras postfix, each frame): the TILT, the LENS, the long lens's distance, Off-centre and the Dutch roll, written
//         on the drawn WorldCamera after the game copied the rig to it (GameHost.ApplyCamera) and after the follow camera moved its target;
//         Meitou's KeepEyeAboveGround then keeps the eye above the ground and UpdateView builds the picking matrix from what we drew.
//         GameHost.ApplyCamera writes the rig's yaw, tilt, boom (and the roll 0) again every frame, so nothing piles up; the lens is the one thing
//         it never writes: it is remembered and handed back exactly (only when it is still ours).
// It stands down (logged once per reason) while: the title screen shows; the cinema or a scene holds the camera (no ApplyCamera this frame);
// Meitou's free camera flies; PZA's WASD Control has a view on; or anything else (photo mode, the squad tour, another mod) turned the drawn
// camera after ApplyCamera (the plugin's v0.17.1 rule: never write against another camera).
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Meitou.Game.Host;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Host.Input;
using Meitou.Game.Rendering.Frame;
using Meitou.Modding;
using Meitou.Simulation;
using PZA.MeitouUi;

namespace PZA.Camera.Meitou;

/// <summary>The camera's focus point, published every frame for a later PZA Tilt-Shift on Meitou (on Kenshi, PZA Camera sent the pivot to
/// PZA Tilt-Shift through PZA_SetSetting, plugin v0.17.3 / v0.18.1). Another mod reads these fields by name (type
/// "PZA.Camera.Meitou.CameraFocus" in assembly PZACamera.Meitou); no reference to this mod is needed.</summary>
public static class CameraFocus
{
    /// <summary>The point the camera looks at, world units: the followed character's tracked point (or the strategy pivot), Off-centre's shift
    /// left out, so a sharp zone stays on the character.</summary>
    public static Vector3 Point;
    /// <summary>The drawn eye, world units.</summary>
    public static Vector3 Eye;
    /// <summary>|Point - Eye|: the focus distance.</summary>
    public static float Distance;
    /// <summary>The drawn vertical lens, radians.</summary>
    public static float FieldOfView;
    /// <summary>The camera in use ("Off", "Diorama", ...; Cinematic gives the camera it plays now).</summary>
    public static string Camera = "Off";
    /// <summary>Counts the frames published (0 = never: the mod is off).</summary>
    public static long Frame;
}

public static class Driver
{
    /// <summary>The camera picked (Cinematic stays Cinematic; <see cref="Effective"/> is the one it plays).</summary>
    public static Cam Current = Cam.Off;
    public static CamSettings Settings = new();
    public static ModSettingsGroup? Group;
    /// <summary>The game host seen last (GameHost.ApplyCamera postfix).</summary>
    public static object? Host;
    /// <summary>0.5.0 (the user, 2026-11-20: "The camera needs to pause when the player pauses... looking at YOU Cinematic mode"): the game is
    /// paused (Meitou's SimulationClock.IsPaused: the pause key, speed 0, a load hold), read once a frame. While it is, Cinematic's switch timer and
    /// Turntable's spin stand still and a called shot's timers stop; the player's own turning and zooming, and eases already under way, go on.</summary>
    public static bool Paused;
    /// <summary>0.5.0: the mod resolved the game and patched it (CameraApi answers only then).</summary>
    public static bool Ready;
    /// <summary>Why it stands down now; null = it draws.</summary>
    public static string? StandDown { get; private set; }

    static readonly ConcurrentQueue<(Action Do, string What)> s_pending = new();
    static bool s_applied, s_started;
    static float s_appYaw, s_appPitch, s_appDist;
    static float s_pitch = float.NaN, s_gameFov = float.NaN, s_ourFov = float.NaN, s_ourRoll;
    static long s_lastFrame;
    static Cam s_cine = Cam.Diorama;
    static double s_cineLeft;
    static readonly Stopwatch s_clock = Stopwatch.StartNew();
    static double s_userTurn = -100;
    static CharacterId s_charId = CharacterId.None;
    static Vector3 s_charPos;
    static float s_charSpeed;
    static (double Old, double At)? s_lowUndo;
    static float s_charYaw = float.NaN;
    // 0.3.0: zoom stops (the pinned stop per camera, -1 = not pinned yet), Keep in view, Side view's line-up, the Diorama look
    static readonly int[] s_zsIdx = Enumerable.Repeat(-1, Enum.GetValues<Cam>().Length).ToArray();
    static KeepView.State s_kv = KeepView.State.Home;
    static double s_kvShownExtra, s_kvShownZoom = 1, s_kvClock;
    static bool s_kvSaid;
    static bool s_lookOn;
    static string s_lookPrevTs = "Off";

    /// <summary>The ground (or sea, or a building floor) height under a point: the session rig's GroundAt, set each frame (the offline check sets
    /// its own).</summary>
    public static Func<float, float, float>? Ground;
    /// <summary>Side view's settle offset (degrees) from SideAlign: its steps start from the followed character's side.</summary>
    public static double SideOffsetDeg;
    /// <summary>The Diorama look is on (Diorama camera + PZA's Looks' Diorama tilt-shift; the plugin's v0.18.0 button).</summary>
    public static bool LookOn => s_lookOn;

    /// <summary>The camera drawn: Cinematic's current one, else the one picked.</summary>
    public static Cam Effective => Current == Cam.Cinematic ? s_cine : Current;

    /// <summary>The window's buttons, the hotkeys and the debug row queue here; the next frame runs them (on the host thread).</summary>
    public static void Queue(Action a, string what) => s_pending.Enqueue((a, what));

    /// <summary>How many actions wait, and dropping them (the offline check).</summary>
    public static int PendingCount => s_pending.Count;
    public static void ClearPending() { while (s_pending.TryDequeue(out _)) { } }

    /// <summary>Runs what is queued (public for the offline check, which has no frame loop).</summary>
    public static int RunPending()
    {
        int n = 0;
        while (s_pending.TryDequeue(out var p))
        {
            n++;
            try { p.Do(); }
            catch (Exception e) { Log.Exception(p.What, e); }
        }
        return n;
    }

    #region Actions

    /// <summary>Switches to a camera; the active one again turns the camera off (the plugin's menu).</summary>
    public static void Pick(Cam c, string from)
    {
        if (!Settings.Isometric && CamCore.Hideable(c))
        {
            Counters.Add("pick.hidden");
            Log.Line($"camera {c} is hidden (Settings > Mods > Long-lens cameras is off) ({from})");
            return;
        }
        if (c == Current && c != Cam.Off) c = Cam.Off;
        var was = Current;
        Current = c;
        if (c == Cam.Cinematic)
        {
            s_cine = CamCore.Playlist(Settings.CinematicHipLevel, Settings.Isometric)[0];
            s_cineLeft = Settings.CinematicSeconds;
        }
        Array.Fill(s_zsIdx, -1);
        if (c == Cam.SideView) LineUpSide();
        if (s_lookOn && c != Cam.Diorama) EndLook("another camera picked");
        Counters.Add("pick." + c);
        Log.Line($"camera {c} ({from}; was {was})");
        Notice(c == Cam.Off ? Strings.T("msg.off") : c == Cam.Cinematic ? Strings.TF("msg.cine", Strings.CamName(s_cine)) : Strings.TF("msg.camera", Strings.CamName(c)));
    }

    /// <summary>Off-centre, Dutch angle, 0.5x lens: on / off. Turning one on with the camera Off switches to Free (the plugin's 0.5x lens rule),
    /// since Off never touches the camera.</summary>
    public static void ToggleExtra(string key, string from)
    {
        var p = typeof(CamSettings).GetProperty(key)!;
        bool on = !(bool)p.GetValue(Settings)!;
        SetSetting(key, on);
        Counters.Add($"extra.{key}.{(on ? "on" : "off")}");
        Log.Line($"{key} {(on ? "on" : "off")} ({from})");
        if (key == nameof(CamSettings.ZoomStops)) Array.Fill(s_zsIdx, -1);
        if (on && Current == Cam.Off && CamWindow.Extras.Contains(key)) Pick(Cam.Free, from + ", for " + key);
    }

    /// <summary>Side view lines up with the followed character's side (the plugin's SideAlign, v0.15.0): its 90-degree steps start there.</summary>
    static void LineUpSide()
    {
        double step = Settings.SideYawStep;
        if (!Settings.SideAlign || !(step > 0) || float.IsNaN(s_charYaw) || Host is not { } h || Access.Session(h) is not { } s)
        {
            SideOffsetDeg = 0;
            Log.Line("Side view: world steps (not following anyone, or Side view lines up is off)");
            return;
        }
        double side = CamCore.ProfileYaw(s_charYaw, s.Camera.Strategy.Yaw, 0) / CamCore.Deg;
        SideOffsetDeg = side - Math.Floor(side / step) * step;
        Counters.Add("side.lined_up");
        Log.Line($"Side view: lined up with the followed character's side ({side:0} degrees; steps of {step:0} from there)");
    }

    /// <summary>The Diorama look (the plugin's v0.18.0 button): Diorama + PZA's Looks' Diorama tilt-shift; off puts the old tilt-shift back.
    /// The plugin's toy colour has no PZA's Looks counterpart yet.</summary>
    public static void ToggleLook(string from)
    {
        if (s_lookOn) { EndLook(from); if (Current == Cam.Diorama) Pick(Cam.Off, from); return; }
        if (LooksLink.Current is not { } ts) { Counters.Add("look.no_looks"); Log.Line("Diorama look: needs PZA's Looks (" + from + ")"); return; }
        s_lookPrevTs = ts;
        if (!LooksLink.Set("PZA Diorama", from + ", Diorama look")) return;
        s_lookOn = true;
        if (Current != Cam.Diorama) Pick(Cam.Diorama, from + ", Diorama look");
        Counters.Add("look.on");
        Log.Line($"Diorama look on ({from}; tilt-shift was {s_lookPrevTs})");
    }

    static bool s_fishPrevWide;

    /// <summary>0.4.1, the plugin's Fisheye button (v0.14.0): PZA's Looks' fisheye on together with the 0.5x lens (its lens is the chain's own,
    /// so the 0.5x lens widens it as on Kenshi); with the camera Off that picks Free. Off: the fisheye off and the 0.5x lens back as it was.</summary>
    public static void ToggleFisheye(string from)
    {
        if (LooksLink.Fisheye is not { } now) { Counters.Add("fisheye.no_looks"); Log.Line("Fisheye: needs PZA's Looks 0.3.0 or newer (" + from + ")"); return; }
        if (now == "Off")
        {
            if (!LooksLink.SetFisheye(LooksLink.FisheyeOn, from)) return;
            s_fishPrevWide = Settings.WideLens;
            if (!Settings.WideLens) ToggleExtra(nameof(CamSettings.WideLens), from + ", Fisheye");
            else if (Current == Cam.Off) Pick(Cam.Free, from + ", Fisheye");
            Counters.Add("fisheye.on");
            return;
        }
        LooksLink.SetFisheye("Off", from);
        if (!s_fishPrevWide && Settings.WideLens) ToggleExtra(nameof(CamSettings.WideLens), from + ", Fisheye off");
        Counters.Add("fisheye.off");
    }

    static void EndLook(string why)
    {
        s_lookOn = false;
        LooksLink.Set(s_lookPrevTs, "Diorama look off: " + why);
        Counters.Add("look.off");
        Log.Line($"Diorama look off ({why}; tilt-shift back to {s_lookPrevTs})");
    }

    /// <summary>The camera whose stops the window shows and sets (Off and Cinematic use Free's).</summary>
    public static Cam StopsCamera => ZoomStops.Has(Effective) ? Effective : Cam.Free;

    /// <summary>A camera's stops: saved ones (Zs&lt;Camera&gt;0..2, 0 = not set) over the researched defaults, sorted.</summary>
    public static double[] StopsFor(Cam c)
    {
        double tilt = CamCore.Tilt(c, Settings) is { } t && t > 0 ? t : Settings.Tilt;
        double fov = float.IsNaN(s_gameFov) ? 50 : s_gameFov / CamCore.Deg;
        var d = ZoomStops.Defaults(c, tilt, fov);
        for (int i = 0; i < ZoomStops.N; i++)
            if (typeof(CamSettings).GetProperty($"Zs{c}{i}")?.GetValue(Settings) is double v && v > 0) d[i] = ZoomStops.ClampD(v);
        Array.Sort(d);
        return d;
    }

    /// <summary>Near / Mid / Far: the zoom seen now becomes this camera's stop k (the plugin's buttons); Reset (k = -1) goes back to the defaults.</summary>
    public static void SaveStop(int k, string from)
    {
        var c = StopsCamera;
        if (k < 0)
        {
            for (int i = 0; i < ZoomStops.N; i++) SetSetting($"Zs{c}{i}", 0.0);
            Array.Fill(s_zsIdx, -1);
            Counters.Add("stops.reset");
            Log.Line($"zoom stops {c}: back to the researched {string.Join(" / ", StopsFor(c).Select(x => x.ToString("0")))} ({from})");
            return;
        }
        if (Host is not { } h || Access.Session(h) is not { } s) { Counters.Add("stops.no_camera"); return; }
        var (stops, at) = ZoomStops.SetStop(StopsFor(c), k, s.Camera.Strategy.Distance);
        for (int i = 0; i < ZoomStops.N; i++) SetSetting($"Zs{c}{i}", stops[i]);
        s_zsIdx[(int)c] = at;
        Counters.Add("stops.saved");
        Log.Line($"zoom stops {c}: {string.Join(" / ", stops.Select(x => x.ToString("0")))} (stop {k} set to {s.Camera.Strategy.Distance:0}, {from})");
    }

    /// <summary>A settings row's - / + / Reset (the window's SETTINGS tab).</summary>
    public static void StepRow(string key, int dir, string from)
    {
        var row = Rows.Find(key);
        var p = typeof(CamSettings).GetProperty(key)!;
        double v = dir == 0 ? (double)p.GetValue(new CamSettings())! : CamCore.StepValue((double)p.GetValue(Settings)!, row.Step, row.Min, row.Max, dir);
        SetSetting(key, v);
        Counters.Add("row." + key);
        Log.Line($"{key} = {v.ToString(System.Globalization.CultureInfo.InvariantCulture)} ({from})");
        if (key == nameof(CamSettings.ShiftLeftPct) && !Settings.OffCentre) SetSetting(nameof(CamSettings.OffCentre), true);   // the plugin's rule
    }

    public static void ResetAll(string from)
    {
        foreach (var r in Rows.All.Concat(Rows.View).Concat(Rows.Shots)) StepRow(r.Key, 0, from);
        Log.Line("every settings row back to its default (" + from + ")");
    }

    /// <summary>Shift+J: the tilt seen now becomes Diorama Low's; again within 5 s puts the old one back (the plugin's undo).</summary>
    public static void CaptureLow(string from)
    {
        double now = s_clock.Elapsed.TotalSeconds;
        if (s_lowUndo is { } u && now - u.At < 5)
        {
            SetSetting(nameof(CamSettings.LowTilt), u.Old);
            s_lowUndo = null;
            Counters.Add("low.undo");
            Log.Line($"Low angle put back to {u.Old:0} ({from})");
            Notice(Strings.TF("msg.low.undo", u.Old.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
            return;
        }
        float pitch = !float.IsNaN(s_pitch) ? s_pitch : Host is { } h && Access.Session(h) is { } s ? s.Camera.Strategy.Pitch : float.NaN;
        if (float.IsNaN(pitch)) { Counters.Add("low.no_camera"); return; }
        double deg = Math.Round(Math.Clamp(pitch / CamCore.Deg, CamCore.MinPitchDeg, CamCore.MaxPitchDeg));
        s_lowUndo = (Settings.LowTilt, now);
        SetSetting(nameof(CamSettings.LowTilt), deg);
        Counters.Add("low.saved");
        Log.Line($"Low angle saved: {deg:0} degrees ({from}; was {s_lowUndo.Value.Old:0})");
        Notice(Strings.TF("msg.low.saved", deg.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
    }

    static void Notice(string text)
    {
        if (!Settings.Popups) return;
        if (PzaUi.Player is not { } player || !Access.HasNotice) { Counters.Add("notice.unavailable"); return; }
        try { Access.Notice(player, text); Counters.Add("notice.shown"); }
        catch (Exception e) { Log.Exception("notice", e); }
    }

    /// <summary>Changes a setting through its group, so the settings page shows it and Meitou keeps it.</summary>
    public static void SetSetting(string key, object value) => PzaSettings.Set(Group, Settings, key, value);

    #endregion

    #region Keys (InputBindings.Resolve postfix)

    /// <summary>Shift + K opens / hides the window (the plugin's menu key), Shift + J saves the Low angle (the plugin's capture key). Plain K and J
    /// stay Meitou's Put down and Rescue (GameHotkeys prefix); Ctrl / Alt never ours; nothing while a text box has the keyboard.</summary>
    public static void OnResolve(TickInput input)
    {
        if (PzaUi.Typing) return;
        bool shift = input.IsDown(Key.LeftShift) || input.IsDown(Key.RightShift);
        bool ctrlAlt = input.IsDown(Key.LeftControl) || input.IsDown(Key.RightControl) || input.IsDown(Key.LeftAlt) || input.IsDown(Key.RightAlt);
        if (!shift || ctrlAlt) return;
        if (input.WasPressed(Key.K)) { Counters.Add("key.shift_k"); Queue(() => CamWindow.HotkeyToggle(), "Shift+K"); }
        if (input.WasPressed(Key.J)) { Counters.Add("key.shift_j"); Queue(() => CaptureLow("Shift+J"), "Shift+J"); }
    }

    /// <summary>The game's hotkey handler must not also run Put down (K) or Rescue (J) for our Shift chords.</summary>
    public static bool OwnsKey(Key key, bool shift) => shift && key is Key.K or Key.J;

    #endregion

    #region Drawn camera (GameHost.ApplyCamera postfix, GameLoop.PlaceSceneCameras postfix)

    /// <summary>The game copied the rig to the drawn camera: remember what it wrote (the stand-down check compares against it).</summary>
    public static void OnApplied(object host, CameraState s)
    {
        Host = host;
        s_applied = true;
        (s_appYaw, s_appPitch, s_appDist) = (s.Yaw, s.Pitch, s.Distance);
    }

    /// <summary>Once a frame, after the follow camera and the other mods placed the drawn camera.</summary>
    public static void OnFrame()
    {
        long ts = Stopwatch.GetTimestamp();
        float dt = s_lastFrame == 0 ? 0 : (float)Math.Min(0.25, Stopwatch.GetElapsedTime(s_lastFrame, ts).TotalSeconds);
        s_lastFrame = ts;
        RunPending();
        bool applied = s_applied;
        s_applied = false;
        if (Host is not { } host || Access.Camera(host) is not { } cam || Access.Session(host) is not { } session) return;

        if (!s_started && PzaUi.InGame && session.World.Player.Exists)
        {
            s_started = true;
            if (Settings.StartOn && Current == Cam.Off) Pick(Cam.Diorama, "Camera on at load");
        }

        Ground = session.Camera.GroundAt;
        ShotWatch.HostFrame(s_clock.Elapsed.TotalSeconds);
        SetPaused(session.Simulation.IsPaused);
        NpcRange = Access.NpcRange(host);
        string? why = Current == Cam.Off ? null : Why(cam, session, applied);
        SetStandDown(why);
        if (Current == Cam.Off || why is not null)
        {
            HandBack(cam, quiet: Shots.Active);
            s_pitch = float.NaN;
            s_kv = KeepView.State.Home;
            (s_kvShownExtra, s_kvShownZoom) = (0, 1);
            Publish(cam, cam.Target, Current == Cam.Off ? "Off" : "stood down");
        }
        else
        {
            if (Current == Cam.Cinematic) Cinematic(dt);
            Draw(cam, dt);
        }
        // 0.5.0: a called shot draws over the player's camera (also while it is Off, or stood down for WASD Control's view: the shot was
        // asked for, and it eases back onto whatever that camera draws). Never on the title screen or in Meitou's free camera.
        if (!Shots.Active) return;
        if (!PzaUi.InGame) Shots.Stop("the title screen");
        else if (session.Camera.IsFree) Shots.Stop("Meitou's free camera");
        else Shots.Apply(cam, session, dt, applied, session.CurrentSnapshot.Characters, session.CurrentSnapshot.Projectiles, session.Camera.GroundAt, Sight(session), Paused);
    }

    /// <summary>The pause, with a line when it starts or ends while something of ours moves on its own (Cinematic, Turntable, a shot).</summary>
    public static void SetPaused(bool paused)
    {
        if (paused == Paused) return;
        Paused = paused;
        if (Effective == Cam.Turntable || Current == Cam.Cinematic || Shots.Active)
        {
            Counters.Add(paused ? "pause.hold" : "pause.resume");
            Log.Line(paused ? "game paused: Cinematic, Turntable and the shots' timers hold" : "game resumed: Cinematic, Turntable and the shots' timers go on");
        }
    }

    /// <summary>0.5.0: the game's own test of a line (the navmesh's walls, rocks and buildings: World.Walkability.LineOfSight), for the shots'
    /// angle picker; the ground is sampled by the picker itself. Null when the world has none.</summary>
    static Func<Vector3, Vector3, bool>? Sight(WorldSession session)
    {
        try
        {
            var walk = session.World.Walkability;
            return walk is null ? null : (a, b) => walk.LineOfSight(a, b);
        }
        catch (Exception e) { Log.Exception("World.Walkability", e); return null; }
    }

    /// <summary>0.5.0: a shot wrote its lens over the player's. The player's lens is remembered as the game's, and ours is the shot's, so the
    /// next frame neither takes the shot's lens for the game's nor leaves it on after the shot (HandBack puts it back).</summary>
    public static void NoteShotLens(float baseFov, float fov)
    {
        if (float.IsNaN(s_ourFov) || float.IsNaN(s_gameFov)) s_gameFov = baseFov;
        s_ourFov = fov;
    }

    /// <summary>Why it must not draw this frame, or null.</summary>
    static string? Why(WorldCamera cam, WorldSession session, bool applied)
    {
        if (!PzaUi.InGame) return "the title screen";
        if (!applied) return "the cinema or a scene holds the camera";
        if (session.Camera.IsFree) return "Meitou's free camera flies";
        if (Access.WasdViewOn()) return "PZA's WASD Control has a view on";
        if (CamCore.Turned(cam.Yaw, cam.Pitch, cam.Distance, s_appYaw, s_appPitch, s_appDist))
            return "another camera turned the view (photo mode, a squad flight or another mod)";
        return null;
    }

    static void SetStandDown(string? why)
    {
        if (why == StandDown) return;
        if (why is not null) { Counters.Add("stand_down"); Log.Line("stands down: " + why); }
        else if (StandDown is not null) Log.Line("draws again (" + StandDown + " is over)");
        StandDown = why;
    }

    /// <summary>Writes this frame's camera: the tilt eased to the camera's, the lens, the long lens's distance, Off-centre, Dutch.</summary>
    public static void Draw(WorldCamera cam, float dt)
    {
        var eff = Effective;
        // the game's lens: whatever is there unless it is still the one we wrote last frame (the game's own change is kept)
        if (float.IsNaN(s_ourFov) || cam.FieldOfView != s_ourFov) s_gameFov = cam.FieldOfView;
        float baseFov = BaseFov;   // 0.4.0: the plugin's Fov (a lens for the cameras without their own), else the game's

        if (float.IsNaN(s_pitch)) s_pitch = cam.Pitch;
        if (CamCore.Tilt(eff, Settings) is { } tilt)
        {   // 0.4.0: TiltRange (the plugin's, Diorama and Diorama Low): the player's own tilt kept within the camera's tilt +- range
            float want = CamCore.Pitch(tilt);
            if (eff is Cam.Diorama or Cam.DioramaLow) want = CamCore.TiltWithin(cam.Pitch, want, Settings.TiltRange);
            s_pitch = CamCore.Ease(s_pitch, want, Settings.TiltEase, dt);
        }
        else s_pitch = cam.Pitch;

        float fov = CamCore.Fov(eff, Settings, baseFov, Settings.WideLens);
        float zoom = eff == Cam.HipLevel ? MathF.Min(cam.Distance, (float)Settings.HipZoomMax) : cam.Distance;
        fov = GuardLens(eff, ref zoom, fov, baseFov);
        float distance = zoom * CamCore.DistanceFactor(eff, baseFov, fov);
        var point = cam.Target;
        var target = Settings.OffCentre ? point + CamCore.OffCentre(cam.Yaw, zoom, Settings.ShiftLeftPct) : point;

        var (extra, zoomK) = KeepInView(point, cam.Yaw, s_pitch, distance, dt);
        float pitch = (float)Math.Min(s_pitch + extra, CamCore.MaxPitchDeg * CamCore.Deg);

        cam.Target = target;
        cam.Pitch = pitch;
        cam.Distance = (float)(distance * zoomK);
        cam.FieldOfView = fov;
        s_ourFov = fov;
        s_ourRoll = Settings.Dutch ? (float)Math.Clamp(Settings.DutchDeg, -45, 45) * CamCore.Deg : 0;
        cam.Roll = s_ourRoll;
        Counters.Add("drawn." + eff);
        Publish(cam, point, eff.ToString());
    }

    /// <summary>The character draw distance (GameHost.NpcRange), read each frame; null = unknown (the guard stays off). The offline check sets it.</summary>
    public static float? NpcRange;
    static int s_breathing = -1;
    static long s_capCount;

    /// <summary>The lens the cameras without their own use: the plugin's Fov setting when set, else the game's (50 before the first frame).</summary>
    public static float BaseFov => Settings.Fov >= 1 ? (float)Math.Clamp(Settings.Fov, 10, 120) * CamCore.Deg : float.IsNaN(s_gameFov) ? 50 * CamCore.Deg : s_gameFov;

    /// <summary>0.4.0, the plugin's NpcRangeGuard (v0.15.0) and LensBreathe (v0.21.0): a long-lens camera's eye kept within 97% of the character
    /// draw distance. Breathe on: past it the eye stays there and the lens widens, so the zoom keeps working; off: the zoom stops there. Returns
    /// the lens to draw; <paramref name="zoom"/> is capped. Falsifier in the log: while widened, the planned eye distance equals the limit.</summary>
    public static float GuardLens(Cam eff, ref float zoom, float fov, float f0)
    {
        if (!CamCore.LongLens(eff) || !Settings.NpcRangeGuard || NpcRange is not { } r || !(r > 100)) return fov;
        double limit = CamCore.NpcRangeShare * r;
        float lens = fov;
        if (Settings.LensBreathe)
        {
            fov = CamCore.BreatheLens(zoom, lens, f0, limit);
            int on = fov > lens + 1e-4f ? 1 : 0;
            if (on != s_breathing)
            {
                if (on == 1) Log.Line($"long lens widened on {eff}: zoom {zoom:0} would put the camera at {CamCore.LensDistance(zoom, lens, f0):0}, past the character draw distance {r:0}; camera held at {limit:0}, lens {lens / CamCore.Deg:0.#} -> {fov / CamCore.Deg:0.#} degrees (planned distance {CamCore.LensDistance(zoom, fov, f0):0})");
                else if (s_breathing == 1) Log.Line($"long lens back to {lens / CamCore.Deg:0.#} degrees on {eff} (zoom {zoom:0}, the camera inside the draw distance)");
                s_breathing = on;
            }
            if (on == 1) Counters.Add("lens.widened");
        }
        double cap = CamCore.ZoomCap(limit, fov, f0);
        if (cap > 1 && zoom > cap)
        {
            if (s_capCount++ % 600 == 0) Log.Line($"zoom {zoom:0} capped to {cap:0} on {eff}: the game draws characters only within {r:0} of the camera");
            Counters.Add("lens.zoom_capped");
            zoom = (float)cap;
        }
        return fov;
    }

    /// <summary>Keep in view (the plugin's v0.17.0 / v0.17.2): decides 15 times a second against the ground (<see cref="Ground"/>), and the shown
    /// tilt and zoom ease toward the decision every frame (no 3-degree steps). Returns the extra tilt (radians, + down) and the distance factor.</summary>
    public static (double Extra, double ZoomK) KeepInView(Vector3 point, float yaw, float pitch, float distance, float dt)
    {
        var p = new KeepView.Params(MaxExtra: Math.Clamp(Settings.KeepInViewMaxTilt, 5, 60) * CamCore.Deg);
        if (!Settings.KeepInView || Ground is null) KeepView.Release(ref s_kv, dt, p);
        else if ((s_kvClock += dt) >= 1.0 / 15)
        {
            double step = s_kvClock;
            s_kvClock = 0;
            var eye = KeepView.Eye(point, yaw, pitch + s_kv.Extra, distance * s_kv.ZoomK);
            bool blocked = KeepView.Blocked(point, eye, Ground, p, out double worst);
            var back = s_kv;   // one step back toward the player's own camera: zoom first, then tilt
            if (back.ZoomK < 1) back.ZoomK = Math.Min(1, back.ZoomK + p.ZoomRate * 0.5 * step);
            else back.Extra = Math.Max(0, back.Extra - p.ReturnRate * step);
            bool backClear = !KeepView.Blocked(point, KeepView.Eye(point, yaw, pitch + back.Extra, distance * back.ZoomK), Ground, p, out _);
            KeepView.Step(ref s_kv, blocked, backClear, step, p, pitch);
            if (blocked && !s_kvSaid) { s_kvSaid = true; Counters.Add("keepview.blocked"); Log.Line($"keep in view: the ground hides the character ({worst:0.0} units over the line): tilting up, then coming in"); }
            if (!s_kv.Engaged && s_kvSaid) { s_kvSaid = false; Counters.Add("keepview.clear"); Log.Line("keep in view: clear again, back to the camera's own view"); }
            if (s_kv.Engaged) Counters.Add("keepview.working");
        }
        double k = Math.Min(1, 6 * Math.Max(0, dt));
        s_kvShownExtra += (s_kv.Extra - s_kvShownExtra) * k;
        s_kvShownZoom += (s_kv.ZoomK - s_kvShownZoom) * k;
        return (s_kvShownExtra, s_kvShownZoom);
    }

    /// <summary>Puts the game's lens and roll back when they are still ours (once, after a stand-down or Off).</summary>
    public static void HandBack(WorldCamera cam, bool quiet = false)
    {
        if (float.IsNaN(s_ourFov)) return;
        if (cam.FieldOfView == s_ourFov && !float.IsNaN(s_gameFov)) cam.FieldOfView = s_gameFov;
        else Counters.Add("handback.lens_changed_elsewhere");
        if (s_ourRoll != 0 && cam.Roll == s_ourRoll) cam.Roll = 0;
        if (!quiet) Log.Line($"lens handed back: {cam.FieldOfView / CamCore.Deg:0.#} degrees");
        s_ourFov = float.NaN;
        s_ourRoll = 0;
        Counters.Add(quiet ? "handback.under_shot" : "handback");
    }

    static void Publish(WorldCamera cam, Vector3 point, string camera)
    {
        CameraFocus.Point = point;
        CameraFocus.Eye = cam.Eye;
        CameraFocus.Distance = Vector3.Distance(point, CameraFocus.Eye);
        CameraFocus.FieldOfView = cam.FieldOfView;
        CameraFocus.Camera = camera;
        CameraFocus.Frame++;
    }

    static void Cinematic(float dt)
    {
        if (Paused) { Counters.Add("cinematic.paused"); return; }   // the camera playing now holds until the game runs again
        s_cineLeft -= dt;
        if (s_cineLeft > 0) return;
        s_cine = CamCore.NextInPlaylist(s_cine, Settings.CinematicHipLevel, Settings.Isometric);
        s_cineLeft = Math.Max(1, Settings.CinematicSeconds);
        Counters.Add("cinematic.next");
        Log.Line("Cinematic: " + s_cine);
        if (Settings.CinematicPopups) Notice(Strings.TF("msg.cine", Strings.CamName(s_cine)));
    }

    #endregion

    #region Rig yaw (StrategyCamera.Update postfix)

    /// <summary>One strategy camera tick: the turning and settling cameras turn the rig. Only the game session's rig.</summary>
    public static void OnStrategyUpdate(StrategyCamera strat, float dt, ActionState a)
    {
        if (Host is not { } host || Access.Session(host) is not { } session || !ReferenceEquals(session.Camera.Strategy, strat)) return;
        double now = s_clock.Elapsed.TotalSeconds;
        if (a.Held(InputAction.RotateLeft) || a.Held(InputAction.RotateRight) || (a.Held(InputAction.Orbit) && a.MouseDelta != Vector2.Zero))
            s_userTurn = now;
        var c = Followed(session, dt);
        s_charYaw = c?.Yaw ?? float.NaN;
        if (Current == Cam.Off || StandDown is not null || session.Camera.IsFree) return;

        float turn = YawTurn(Effective, strat.Yaw, dt, now - s_userTurn, c?.Yaw, s_charSpeed);
        if (MathF.Abs(turn) >= 1e-6f)
        {
            strat.Rotate(turn);
            Counters.Add("turn." + Effective);
        }
        PinZoom(strat);
        BandZoom(strat);
    }

    /// <summary>0.4.0: the plugin's ZoomMin / ZoomMax while a PZA camera is on, and NpcRangeGuard without LensBreathe (the zoom itself capped at the
    /// draw distance, as the plugin did). On the rig, so the wheel feels the end.</summary>
    public static void BandZoom(StrategyCamera strat)
    {
        if (Current == Cam.Off) return;
        float d = strat.Distance, want = d;
        if (Settings.ZoomMin > 0 && want < Settings.ZoomMin) want = (float)Settings.ZoomMin;
        if (Settings.ZoomMax > 0 && want > Settings.ZoomMax) want = (float)Settings.ZoomMax;
        var eff = Effective;
        if (CamCore.LongLens(eff) && Settings.NpcRangeGuard && !Settings.LensBreathe && NpcRange is { } r && r > 100)
        {
            float f0 = BaseFov;
            double cap = CamCore.ZoomCap(CamCore.NpcRangeShare * r, CamCore.Fov(eff, Settings, f0, false), f0);
            if (cap > 1 && want > cap) want = (float)cap;
        }
        if (MathF.Abs(want - d) <= 1e-3f) return;
        strat.Place(strat.PivotXz, strat.Yaw, strat.Pitch, want);
        Counters.Add("zoom.band");
    }

    /// <summary>Zoom stops (the plugin's v0.15.0): the boom pinned to the camera's current stop; a wheel notch that moves it past the tolerance
    /// steps one stop that way. In Cinematic, the plugin's Cinematic zoom lock (v0.16.0, 0.4.0 here): each camera sits at its Mid stop and the
    /// wheel can't change it; without the lock the zoom is free there.</summary>
    public static void PinZoom(StrategyCamera strat)
    {
        var eff = Effective;
        if (Current == Cam.Cinematic)
        {
            if (!Settings.CinematicZoomLock || !ZoomStops.Has(eff)) return;
            float mid = (float)StopsFor(eff)[1];
            if (MathF.Abs(strat.Distance - mid) <= 1e-3f) return;
            strat.Place(strat.PivotXz, strat.Yaw, strat.Pitch, mid);
            Counters.Add("cinematic.zoom_locked");
            return;
        }
        if (!Settings.ZoomStops || !ZoomStops.Has(eff)) return;
        var stops = StopsFor(eff);
        int was = s_zsIdx[(int)eff], idx = ZoomStops.Step(stops, was, strat.Distance);
        s_zsIdx[(int)eff] = idx;
        if (idx != was && was >= 0) { Counters.Add("stops.step"); Log.Line($"zoom stop {eff}: {(idx > was ? "out" : "in")} to {(new[] { "near", "mid", "far" })[idx]} ({stops[idx]:0})"); }
        if (MathF.Abs(strat.Distance - (float)stops[idx]) > 1e-3f)
        {
            strat.Place(strat.PivotXz, strat.Yaw, strat.Pitch, (float)stops[idx]);
            Counters.Add("stops.pinned");
        }
    }

    /// <summary>The rig's turn this tick for a camera (radians, positive = left). <paramref name="charYaw"/> null = no character followed.
    /// Public for the offline check.</summary>
    public static float YawTurn(Cam eff, float yaw, float dt, double sinceUserTurn, float? charYaw, float charSpeed)
    {
        var s = Settings;
        switch (eff)
        {
            case Cam.Isometric or Cam.PixelIsometric when sinceUserTurn >= s.YawSettle:
                return CamCore.EaseTurn(yaw, CamCore.Settle(yaw, s.IsoYawStep, s.IsoYawOffset), 6, dt);
            case Cam.Diorama or Cam.DioramaLow when s.YawStep > 0 && sinceUserTurn >= s.YawSettle:
                return CamCore.EaseTurn(yaw, CamCore.Settle(yaw, s.YawStep, 0), 6, dt);
            case Cam.SideView when sinceUserTurn >= s.YawSettle:
                return CamCore.EaseTurn(yaw, CamCore.Settle(yaw, s.SideYawStep, SideOffsetDeg), 6, dt);
            case Cam.Turntable when sinceUserTurn >= s.TurntablePause && !Paused:
                return (float)((Current == Cam.Cinematic ? s.CinematicTurntableSpeed : s.TurntableSpeed) * CamCore.Deg * dt);
            case Cam.Chase when charYaw is { } cy && charSpeed >= s.ChaseMinSpeed && sinceUserTurn >= s.ChasePause:
                return CamCore.EaseTurn(yaw, CamCore.ChaseYaw(cy, s.ChaseFront), s.ChaseEase, dt);
            case Cam.HipLevel when charYaw is { } hy && sinceUserTurn >= s.ChasePause:
                return CamCore.EaseTurn(yaw, CamCore.HipYaw(hy, s.HipAngle), s.HipEase, dt);
            case Cam.Profile when charYaw is { } py && sinceUserTurn >= s.ChasePause:
                return CamCore.EaseTurn(yaw, CamCore.ProfileYaw(py, yaw, (int)Math.Round(s.ProfileSide)), s.ProfileEase, dt);
        }
        return 0;
    }

    /// <summary>The character the camera follows (F), else the first selected; its speed from the last tick (units per second).</summary>
    static CharacterSnapshot? Followed(WorldSession session, float dt)
    {
        CharacterId? id = PzaUi.Player is { } p ? Access.Follow(p) : null;
        if (id is null && PzaUi.SelectedCharacters() is { Count: > 0 } sel) id = sel[0];
        if (id is not { } want || session.CurrentSnapshot.Characters.FirstOrDefault(x => x.Id == want) is not { } c)
        {
            if (CamCore.FollowsCharacter(Effective)) Counters.Add("no_character");
            s_charId = CharacterId.None;
            s_charSpeed = 0;
            return null;
        }
        if (c.Id == s_charId && dt > 0) s_charSpeed = Vector3.Distance(c.Position with { Y = 0 }, s_charPos with { Y = 0 }) / dt;
        else s_charSpeed = 0;
        (s_charId, s_charPos) = (c.Id, c.Position);
        return c;
    }

    #endregion

    /// <summary>The F10 debug row: the next camera (the plugin's CycleHotkey).</summary>
    public static string DebugNext()
    {
        var next = (Cam)(((int)Current + 1) % Enum.GetValues<Cam>().Length);
        Queue(() => Pick(next, "F10"), "F10");
        return "Camera: " + next;
    }
}

/// <summary>The SETTINGS tab's rows: the plugin's settings column (v0.9.0), in its order.</summary>
public static class Rows
{
    public readonly record struct Row(string Key, double Step, double Min, double Max);

    public static readonly Row[] All =
    [
        new(nameof(CamSettings.ShiftLeftPct), 5, -40, 40),
        new(nameof(CamSettings.Tilt), 5, 10, 85),
        new(nameof(CamSettings.LowTilt), 5, -10, 85),
        new(nameof(CamSettings.IsoFov), 1, 4, 40),
        new(nameof(CamSettings.HipAngle), 15, -180, 180),
        new(nameof(CamSettings.TurntableSpeed), 3, -90, 90),
        new(nameof(CamSettings.CinematicSeconds), 1, 2, 60),
        new(nameof(CamSettings.DutchDeg), 1, -30, 30),
        new(nameof(CamSettings.WideZoom), 0.05, 0.25, 1),
        new(nameof(CamSettings.ProfileSide), 1, -1, 1),
    ];

    /// <summary>0.3.0: the VIEW tab's rows (the settle steps that make the camera end on 45-degree steps, and Keep in view's tilt).</summary>
    public static readonly Row[] View =
    [
        new(nameof(CamSettings.YawStep), 15, 0, 180),
        new(nameof(CamSettings.IsoYawStep), 15, 0, 180),
        new(nameof(CamSettings.KeepInViewMaxTilt), 5, 5, 60),
    ];

    /// <summary>0.5.0: the SHOTS tab's rows (the time each phase asks of PZA's SlowMo, and the lengths).</summary>
    public static readonly Row[] Shots =
    [
        new(nameof(CamSettings.ShotPickSpeed), 0.05, 0.05, 1),
        new(nameof(CamSettings.ShotSpeed), 0.05, 0.05, 1),
        new(nameof(CamSettings.ShotBetweenSpeed), 0.05, 0.05, 1),
        new(nameof(CamSettings.ShotImpactSpeed), 0.05, 0.05, 1),
        new(nameof(CamSettings.ShotSeconds), 0.1, 0.2, 10),
        new(nameof(CamSettings.ShotImpactSeconds), 0.25, 0.25, 5),
        new(nameof(CamSettings.ShotReturnSeconds), 0.1, 0, 3),
    ];

    public static Row Find(string key) => All.Concat(View).Concat(Shots).First(r => r.Key == key);
}
