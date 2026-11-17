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
        // optional: the message line (as PZA's SlowMo and WASD Control); a miss only turns the messages off
        s_notice = AccessTools.Method(player, "Notice", [typeof(string), typeof(bool), typeof(string)]);

        // the public members this mod relies on (a later Meitou could rename them)
        foreach (var (t, name) in new[] { (typeof(StrategyCamera), "Rotate"), (typeof(StrategyCamera), "get_Yaw"), (typeof(CameraRig), "get_IsFree"),
                     (typeof(CameraRig), "get_Strategy"), (typeof(WorldCamera), "set_FieldOfView"), (typeof(WorldCamera), "set_Roll") })
            if (t.GetMethod(name) is null) missing.Add($"method {t.Name}.{name}");
        return missing;
    }

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
    public static void Notice(object player, string text) => s_notice!.Invoke(player, [text, false, null]);

    #region PZA's WASD Control (optional, read only)

    static bool s_wasdLooked;
    static FieldInfo? s_wasdOn, s_wasdView;

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
                s_wasdOn = d.GetField("On", BindingFlags.Public | BindingFlags.Static);
                s_wasdView = asm.GetType("PZA.WASDControl.Meitou.Views")?.GetField("Current", BindingFlags.Public | BindingFlags.Static);
                Log.Line($"PZA's WASD Control found: its views {(s_wasdOn is not null && s_wasdView is not null ? "are watched" : "cannot be read (stand-down by the camera check only)")}");
            }
        }
        if (s_wasdOn?.GetValue(null) is not true || s_wasdView?.GetValue(null) is not { } v) return false;
        return v.ToString() != "Normal";
    }

    #endregion
}
