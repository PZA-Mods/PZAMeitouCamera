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
        if (c == Current && c != Cam.Off) c = Cam.Off;
        var was = Current;
        Current = c;
        if (c == Cam.Cinematic)
        {
            s_cine = CamCore.Playlist(Settings.CinematicHipLevel)[0];
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
        foreach (var r in Rows.All.Concat(Rows.View)) StepRow(r.Key, 0, from);
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
        string? why = Current == Cam.Off ? null : Why(cam, session, applied);
        SetStandDown(why);
        if (Current == Cam.Off || why is not null)
        {
            HandBack(cam);
            s_pitch = float.NaN;
            s_kv = KeepView.State.Home;
            (s_kvShownExtra, s_kvShownZoom) = (0, 1);
            Publish(cam, cam.Target, Current == Cam.Off ? "Off" : "stood down");
            return;
        }
        if (Current == Cam.Cinematic) Cinematic(dt);
        Draw(cam, dt);
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
        float gameFov = s_gameFov;

        if (float.IsNaN(s_pitch)) s_pitch = cam.Pitch;
        s_pitch = CamCore.Tilt(eff, Settings) is { } tilt ? CamCore.Ease(s_pitch, CamCore.Pitch(tilt), Settings.TiltEase, dt) : cam.Pitch;

        float fov = CamCore.Fov(eff, Settings, gameFov, Settings.WideLens);
        float zoom = eff == Cam.HipLevel ? MathF.Min(cam.Distance, (float)Settings.HipZoomMax) : cam.Distance;
        float distance = zoom * CamCore.DistanceFactor(eff, gameFov, fov);
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
    public static void HandBack(WorldCamera cam)
    {
        if (float.IsNaN(s_ourFov)) return;
        if (cam.FieldOfView == s_ourFov && !float.IsNaN(s_gameFov)) cam.FieldOfView = s_gameFov;
        else Counters.Add("handback.lens_changed_elsewhere");
        if (s_ourRoll != 0 && cam.Roll == s_ourRoll) cam.Roll = 0;
        Log.Line($"lens handed back: {cam.FieldOfView / CamCore.Deg:0.#} degrees");
        s_ourFov = float.NaN;
        s_ourRoll = 0;
        Counters.Add("handback");
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
        s_cineLeft -= dt;
        if (s_cineLeft > 0) return;
        s_cine = CamCore.NextInPlaylist(s_cine, Settings.CinematicHipLevel);
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
    }

    /// <summary>Zoom stops (the plugin's v0.15.0): the boom pinned to the camera's current stop; a wheel notch that moves it past the tolerance
    /// steps one stop that way. Not in Cinematic (the plugin's own Cinematic zoom lock is not ported).</summary>
    public static void PinZoom(StrategyCamera strat)
    {
        var eff = Effective;
        if (!Settings.ZoomStops || Current == Cam.Cinematic || !ZoomStops.Has(eff)) return;
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
            case Cam.Turntable when sinceUserTurn >= s.TurntablePause:
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

    public static Row Find(string key) => All.Concat(View).First(r => r.Key == key);
}
