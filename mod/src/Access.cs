// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The game's non-public types and members this mod reaches, looked up ONCE by name (MeitouClient 0.20.1, tag v0.20.1; the camera files are
// unchanged up to the 0.20.2 nightlies). A missing one turns the mod off with a line naming it, never a crash later.
// Public API used as is: WorldSession.Camera (CameraRig: IsFree, Strategy), StrategyCamera.Update / Yaw / Rotate, WorldCamera (Target, Yaw,
// Pitch, Distance, FieldOfView, Roll), CameraState, WorldSession.CurrentSnapshot (CharacterSnapshot.Id / Position / Yaw), InputBindings.Resolve.
using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Host.Input;
using Meitou.Game.Rendering.Frame;

namespace PZA.Camera.Meitou;

public static class Access
{
    /// <summary>Meitou.Game.GameHost (internal).</summary>
    public static Type GameHost = null!;
    /// <summary>GameHost.ApplyCamera(CameraState) (internal): the rig's camera copied to the drawn one, once a frame (GameLoop.MoveCamera; not while
    /// the cinema holds the camera).</summary>
    public static MethodInfo ApplyCamera = null!;
    /// <summary>GameLoop.PlaceSceneCameras() (private): once a frame after MoveCamera: the follow camera's target, WASD Control's views (its
    /// SandboxScenario.FollowCamera postfix), the title's camera. Its postfix runs before KeepEyeAboveGround and UpdateView (picking).</summary>
    public static MethodInfo PlaceSceneCameras = null!;
    /// <summary>StrategyCamera.Update(float, ActionState) (public): the strategy camera's tick, before the rig's leash and the state it keeps.</summary>
    public static MethodInfo StrategyUpdate = null!;
    /// <summary>InputBindings.Resolve(TickInput) (public): the control tick's keys.</summary>
    public static MethodInfo Resolve = null!;
    /// <summary>Meitou.Game.Ui.GameHotkeys.Key(Key, bool shift) (internal): K there is Put down and J Rescue, with or without Shift; the prefix
    /// skips it for Shift+K and Shift+J only.</summary>
    public static MethodInfo HotkeyKey = null!;

    public static AccessTools.FieldRef<object, WorldSession> Session = null!;
    public static AccessTools.FieldRef<object, WorldCamera> Camera = null!;
    public static AccessTools.FieldRef<object, object> Player = null!;
    /// <summary>PlayerInterface.Follow { get; } (CharacterId?): the character the camera follows (F).</summary>
    static PropertyInfo s_follow = null!;
    static MethodInfo? s_notice;

    public static List<string> ResolveAll()
    {
        var missing = new List<string>();
        var game = typeof(WorldSession).Assembly;
        GameHost = game.GetType("Meitou.Game.GameHost")!;
        var loop = game.GetType("Meitou.Game.Host.GameLoop");
        var player = game.GetType("Meitou.Game.PlayerInterface");
        var hotkeys = game.GetType("Meitou.Game.Ui.GameHotkeys");
        if (GameHost is null) missing.Add("type Meitou.Game.GameHost");
        if (loop is null) missing.Add("type Meitou.Game.Host.GameLoop");
        if (player is null) missing.Add("type Meitou.Game.PlayerInterface");
        if (hotkeys is null) missing.Add("type Meitou.Game.Ui.GameHotkeys");
        if (missing.Count > 0) return missing;

        ApplyCamera = Need(AccessTools.Method(GameHost, "ApplyCamera", [typeof(CameraState)]), "GameHost.ApplyCamera(CameraState)", missing);
        PlaceSceneCameras = Need(AccessTools.Method(loop, "PlaceSceneCameras", Type.EmptyTypes), "GameLoop.PlaceSceneCameras()", missing);
        StrategyUpdate = Need(AccessTools.Method(typeof(StrategyCamera), "Update", [typeof(float), typeof(ActionState)]), "StrategyCamera.Update(float, ActionState)", missing);
        Resolve = Need(AccessTools.Method(typeof(InputBindings), "Resolve", [typeof(TickInput)]), "InputBindings.Resolve(TickInput)", missing);
        HotkeyKey = Need(AccessTools.Method(hotkeys!, "Key", [typeof(Key), typeof(bool)]), "GameHotkeys.Key(Key, bool)", missing);

        Session = Field<WorldSession>(GameHost!, "Session", missing);
        Camera = Field<WorldCamera>(GameHost!, "Camera", missing);
        var pf = AccessTools.Field(GameHost, "Player");
        if (pf is null || pf.FieldType != player) missing.Add("field GameHost.Player");
        else Player = AccessTools.FieldRefAccess<object, object>(pf);

        s_follow = AccessTools.Property(player, "Follow")!;
        if (s_follow is null || s_follow.PropertyType != typeof(global::Meitou.Simulation.CharacterId?)) missing.Add("property PlayerInterface.Follow");
        // 0.6.1 (the 0.22.2 nightly of 2026-11-22 added a fifth parameter, float? height = null): StrategyCamera.Place is found by its shape,
        // old or new, never bound at compile time (an optional parameter is filled in by the compiler, so a 0.20.1 build calls the 4-argument
        // one and fails on a Meitou that only has 5). A miss only turns the zoom stops, the zoom band and the Cinematic zoom lock off.
        var v2 = typeof(System.Numerics.Vector2);
        s_place4 = AccessTools.Method(typeof(StrategyCamera), "Place", [v2, typeof(float), typeof(float), typeof(float)]);
        s_place5 = s_place4 is null ? AccessTools.Method(typeof(StrategyCamera), "Place", [v2, typeof(float), typeof(float), typeof(float), typeof(float?)]) : null;
        if (s_place4 is null && s_place5 is null) Log.Line("StrategyCamera.Place not found: zoom stops, the zoom band and the Cinematic zoom lock stay off");
        // optional: the message line (as PZA's SlowMo and WASD Control); a miss only turns the messages off
        s_notice = AccessTools.Method(player, "Notice", [typeof(string), typeof(bool), typeof(string)]);

        // the public members this mod relies on (a later Meitou could rename them)
        foreach (var (t, name) in new[] { (typeof(StrategyCamera), "Rotate"), (typeof(StrategyCamera), "get_Yaw"), (typeof(CameraRig), "get_IsFree"),
                     (typeof(CameraRig), "get_Strategy"), (typeof(WorldCamera), "set_FieldOfView"), (typeof(WorldCamera), "set_Roll") })
            if (t.GetMethod(name) is null) missing.Add($"method {t.Name}.{name}");
        return missing;
    }

    static System.Reflection.MethodInfo? s_place4, s_place5;

    /// <summary>The strategy camera placed at a boom length (its pivot, heading and tilt kept): Meitou's StrategyCamera.Place, whichever shape
    /// this Meitou has. False when it has neither (logged once at start).</summary>
    public static bool PlaceStrategy(StrategyCamera strat, System.Numerics.Vector2 pivot, float yaw, float pitch, float distance)
    {
        try
        {
            if (s_place4 is not null) { s_place4.Invoke(strat, [pivot, yaw, pitch, distance]); return true; }
            if (s_place5 is not null) { s_place5.Invoke(strat, [pivot, yaw, pitch, distance, null]); return true; }
        }
        catch (Exception e) { Log.Exception("StrategyCamera.Place", e); }
        return false;
    }

    /// <summary>Which StrategyCamera.Place this Meitou has (the check): 4, 5 or 0 (none).</summary>
    public static int PlaceShape => s_place4 is not null ? 4 : s_place5 is not null ? 5 : 0;

    static AccessTools.FieldRef<object, T> Field<T>(Type t, string name, List<string> missing)
    {
        var f = AccessTools.Field(t, name);
        if (f is null || f.FieldType != typeof(T)) { missing.Add($"field {t.Name}.{name}"); return null!; }
        return AccessTools.FieldRefAccess<T>(t, name);
    }

    static MethodInfo Need(MethodInfo? m, string name, List<string> missing)
    {
        if (m is null) missing.Add("method " + name);
        return m!;
    }

    public static global::Meitou.Simulation.CharacterId? Follow(object player) => (global::Meitou.Simulation.CharacterId?)s_follow.GetValue(player);

    public static bool HasNotice => s_notice is not null;

    /// <summary>0.4.0: GameHost.NpcRange (private float): the character draw distance, the renderer's slider or settings.cfg's npc range (Meitou
    /// 0.20.1 - 0.22: characters are drawn only within it of the camera's eye). Optional: without it the long-lens guard stays off (logged).</summary>
    static PropertyInfo? s_npcRange;
    static bool s_npcLooked;
    public static float? NpcRange(object host)
    {
        if (!s_npcLooked)
        {
            s_npcLooked = true;
            s_npcRange = AccessTools.Property(GameHost, "NpcRange");
            if (s_npcRange?.PropertyType != typeof(float)) s_npcRange = null;
            Log.Line(s_npcRange is null ? "GameHost.NpcRange not found: the long-lens cameras are not kept inside the character draw distance"
                                        : "GameHost.NpcRange found: the long-lens cameras keep the characters drawn");
        }
        try { return s_npcRange?.GetValue(host) is float r && float.IsFinite(r) ? r : null; }
        catch (Exception e) { Log.Exception("NpcRange", e); return null; }
    }
    public static void Notice(object player, string text) => s_notice!.Invoke(player, [text, false, null]);

    #region Depth of field and Photo Mode (optional, 0.6.0)

    // GameHost.WorldOptions (internal) .Post (PostOptions) .PhotoDepthOfField (bool?): the switch Photo Mode turns depth of field on with; the
    // world frame copies the drawn camera's FocusDistance / Aperture into the pass every frame (GameHost.ApplyCamera sets both back to null each
    // frame, so a shot only sets them while it draws). PhotoMode (internal): Enter() saves the drawn lens in _fieldOfView and Leave() puts it back.
    // Meitou 0.20.1 - 0.22.1 have all of them; a miss only turns that feature off, with a line.
    static FieldInfo? s_worldOptions, s_post, s_photoDof, s_photoFov, s_cinematicView;
    /// <summary>PhotoMode.Enter() (internal), or null.</summary>
    public static MethodInfo? PhotoEnter;

    public static List<string> ResolveOptional()
    {
        var lines = new List<string>();
        var game = typeof(WorldSession).Assembly;
        s_worldOptions = AccessTools.Field(GameHost, "WorldOptions");
        s_post = s_worldOptions is null ? null : AccessTools.Field(s_worldOptions.FieldType, "Post");
        s_photoDof = s_post is null || s_post.FieldType.IsValueType ? null : AccessTools.Field(s_post.FieldType, "PhotoDepthOfField");
        if (s_photoDof is not null && s_photoDof.FieldType != typeof(bool?)) s_photoDof = null;
        // the renderer runs its camera effects (depth of field) only for the cinematic cameras: PostOptions.CinematicView, set by GameLoop.MoveCamera
        // each frame before PlaceSceneCameras (headless 2026-11-20: without it the shots' focus drew nothing, Photo Mode's did)
        s_cinematicView = s_post is null ? null : AccessTools.Field(s_post.FieldType, "CinematicView");
        if (s_cinematicView is null || s_cinematicView.FieldType != typeof(bool)) s_photoDof = null;
        lines.Add(s_photoDof is null ? "shot focus off: GameHost.WorldOptions.Post.PhotoDepthOfField not found" : "shot focus: the depth-of-field switch found");
        var photo = game.GetType("Meitou.Game.Host.Photo.PhotoMode");
        PhotoEnter = photo is null ? null : AccessTools.Method(photo, "Enter", Type.EmptyTypes);
        s_photoFov = photo is null ? null : AccessTools.Field(photo, "_fieldOfView");
        if (s_photoFov is not null && s_photoFov.FieldType != typeof(float)) s_photoFov = null;
        if (PhotoEnter is null || PhotoEnter.ReturnType != typeof(bool) || s_photoFov is null) PhotoEnter = null;
        lines.Add(PhotoEnter is null ? "Photo Mode handover off: PhotoMode.Enter / _fieldOfView not found" : "Photo Mode handover: PhotoMode.Enter found");
        return lines;
    }

    public static bool HasDof => s_photoDof is not null;

    static object? PostOf(object host) => s_post!.GetValue(s_worldOptions!.GetValue(host));

    public static bool? PhotoDof(object host) => PostOf(host) is { } p ? (bool?)s_photoDof!.GetValue(p) : null;

    public static void SetPhotoDof(object host, bool? value)
    {
        if (PostOf(host) is { } p) s_photoDof!.SetValue(p, value);
    }

    /// <summary>This frame is a cinematic camera's (the shot's): the renderer may run its camera effects. Meitou sets the flag again next frame.</summary>
    public static void SetCinematicView(object host)
    {
        if (PostOf(host) is { } p) s_cinematicView!.SetValue(p, true);
    }

    /// <summary>The lens Photo Mode puts back when it is left.</summary>
    public static void SetPhotoReturnLens(object photo, float fov) => s_photoFov?.SetValue(photo, fov);
    public static float? PhotoReturnLens(object photo) => s_photoFov?.GetValue(photo) as float?;

    #endregion

    #region PZA's WASD Control (optional, read only)

    static bool s_wasdApiLooked;
    static MethodInfo? s_wasdNormal;

    /// <summary>0.6.1 (agreed with PZA's WASD &amp; Controller Support, 2026-11-21: the latest player choice wins): a Kit camera picked while a
    /// WASD view is on asks WASD for its normal view through its WasdApi.SetNormalView(string caller), found by name. Without it the Kit stays
    /// stood down for the view, as before, and says so once.</summary>
    public static void AskWasdNormalView(string from)
    {
        if (!s_wasdApiLooked)
        {
            s_wasdApiLooked = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? t = null;
                try { t = asm.GetType("PZA.WASDControl.Meitou.WasdApi"); } catch { }
                if (t?.GetMethod("SetNormalView", BindingFlags.Public | BindingFlags.Static, [typeof(string)]) is { } m) { s_wasdNormal = m; break; }
            }
            Log.Line(s_wasdNormal is null ? "PZA's WASD Control has no WasdApi.SetNormalView: its view stays on until you turn it off" : "PZA's WASD Control's WasdApi.SetNormalView found");
        }
        if (s_wasdNormal is null) return;
        try { s_wasdNormal.Invoke(null, ["pza.camera"]); Counters.Add("wasd.normal_view_asked"); Log.Line($"asked PZA's WASD Control for its normal view ({from})"); }
        catch (Exception e) { Log.Exception("WasdApi.SetNormalView", e); }
    }

    static bool s_wasdLooked;
    static FieldInfo? s_wasdOn, s_wasdView;
    static PropertyInfo? s_wasdActive;   // 0.6.1: WasdApi v1's ViewActive (WASD 995572e), preferred over its internals

    /// <summary>PZA's WASD Control for Meitou has one of its own views on (over the shoulder, first person): it places the drawn camera itself
    /// (its SandboxScenario.FollowCamera postfix), so this mod stands down. Read by name from its assembly when it is loaded; false without it.</summary>
    public static bool WasdViewOn()
    {
        if (!s_wasdLooked)
        {
            s_wasdLooked = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetType("PZA.WASDControl.Meitou.Driver") is not { } d) continue;
                s_wasdActive = asm.GetType("PZA.WASDControl.Meitou.WasdApi")?.GetProperty("ViewActive", BindingFlags.Public | BindingFlags.Static);
                if (s_wasdActive?.PropertyType != typeof(bool)) s_wasdActive = null;
                s_wasdOn = d.GetField("On", BindingFlags.Public | BindingFlags.Static);
                s_wasdView = asm.GetType("PZA.WASDControl.Meitou.Views")?.GetField("Current", BindingFlags.Public | BindingFlags.Static);
                Log.Line($"PZA's WASD Control found: its views {(s_wasdActive is not null ? "are watched (WasdApi.ViewActive)" : s_wasdOn is not null && s_wasdView is not null ? "are watched" : "cannot be read (stand-down by the camera check only)")}");
            }
        }
        if (s_wasdActive is not null)
        {
            try { return s_wasdActive.GetValue(null) is true; } catch { s_wasdActive = null; }
        }
        if (s_wasdOn?.GetValue(null) is not true || s_wasdView?.GetValue(null) is not { } v) return false;
        return v.ToString() != "Normal";
    }

    #endregion
}
