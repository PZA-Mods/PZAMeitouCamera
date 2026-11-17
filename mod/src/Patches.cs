// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The Harmony patches. Each body is wrapped: a throw is logged (at most once a second) and never reaches the game. All run on the host thread
// (input resolve, control tick, frame, key events), one after another, so the Driver's plain statics are safe.
using HarmonyLib;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Host.Input;

namespace PZA.Camera.Meitou;

public static class Patches
{
    /// <summary>Applies all of them or none; returns one line per patch.</summary>
    public static List<string> Apply(Harmony h)
    {
        var lines = new List<string>();
        try
        {
            h.Patch(Access.ApplyCamera, postfix: Hook(nameof(ApplyCameraPostfix)));
            lines.Add("patched GameHost.ApplyCamera postfix (what the game drew from the rig)");
            h.Patch(Access.PlaceSceneCameras, postfix: Hook(nameof(PlaceSceneCamerasPostfix)));
            lines.Add("patched GameLoop.PlaceSceneCameras postfix (the drawn camera: tilt, lens, distance, off-centre, roll)");
            h.Patch(Access.StrategyUpdate, postfix: Hook(nameof(StrategyUpdatePostfix)));
            lines.Add("patched StrategyCamera.Update postfix (the rig's yaw: turning and settling cameras)");
            h.Patch(Access.Resolve, postfix: Hook(nameof(ResolvePostfix)));
            lines.Add("patched InputBindings.Resolve postfix (Shift+K, Shift+J)");
            h.Patch(Access.HotkeyKey, prefix: Hook(nameof(HotkeyPrefix)));
            lines.Add("patched GameHotkeys.Key prefix (Shift+K / Shift+J no longer also put down / rescue)");
        }
        catch
        {
            h.UnpatchAll(h.Id);
            throw;
        }
        return lines;
    }

    static HarmonyMethod Hook(string name) => new(AccessTools.Method(typeof(Patches), name));

    static void ApplyCameraPostfix(object __instance, CameraState s)
    {
        try { Driver.OnApplied(__instance, s); }
        catch (Exception e) { Log.Exception("ApplyCamera postfix", e); }
    }

    static void PlaceSceneCamerasPostfix()
    {
        try { Driver.OnFrame(); }
        catch (Exception e) { Log.Exception("PlaceSceneCameras postfix", e); }
    }

    static void StrategyUpdatePostfix(StrategyCamera __instance, float dt, ActionState a)
    {
        try { Driver.OnStrategyUpdate(__instance, dt, a); }
        catch (Exception e) { Log.Exception("StrategyCamera.Update postfix", e); }
    }

    static void ResolvePostfix(TickInput input)
    {
        try { Driver.OnResolve(input); }
        catch (Exception e) { Log.Exception("Resolve postfix", e); }
    }

    /// <summary>false = skip the game's handler: only for K / J with Shift held (plain K / J stay Meitou's Put down / Rescue).</summary>
    static bool HotkeyPrefix(Key key, bool shift)
    {
        try
        {
            if (!Driver.OwnsKey(key, shift)) return true;
            Counters.Add("hotkey.blocked_shift_" + key);
            return false;
        }
        catch (Exception e)
        {
            Log.Exception("GameHotkeys prefix", e);
            return true;
        }
    }
}
