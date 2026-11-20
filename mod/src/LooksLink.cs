// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The Tilt-Shift buttons' door to PZA's Looks for Meitou (0.2.1+): its public static class PZA.Looks.Meitou.LooksApi, found by type name, so
// neither mod references the other (the user, 2026-11-17: "Yes, add the button"). Members used: const int Version (>= 1), string[]
// TiltShiftNames ("Off", "PZA Miniature", "PZA HD-2D", "PZA Diorama"), string TiltShift, bool SetTiltShift(string). A change goes through
// PZA's Looks' own settings group, so it is saved and shows on Settings > Mods. Without PZA's Looks (or an older one) the row shows disabled
// with a note; never an error.
using System.Diagnostics;
using System.Reflection;

namespace PZA.Camera.Meitou;

public static class LooksLink
{
    public const string TypeName = "PZA.Looks.Meitou.LooksApi";
    public const int WantVersion = 1;

    /// <summary>The window's buttons, in order, and the PZA's Looks names they set.</summary>
    public static readonly (string Button, string Name)[] Buttons =
        [("Ts_Off", "Off"), ("Ts_Miniature", "PZA Miniature"), ("Ts_HD2D", "PZA HD-2D"), ("Ts_Diorama", "PZA Diorama")];

    static Type? s_api;
    static PropertyInfo? s_current, s_names, s_fish;
    static MethodInfo? s_set, s_fishSet;
    /// <summary>0.4.1: PZA's Looks 0.3.0's LooksApi version 2 adds the fisheye (FisheyeNames, Fisheye, SetFisheye).</summary>
    public const int FisheyeVersion = 2;
    public const string FisheyeOn = "PZA Fisheye";
    static long s_lastLook;
    static bool s_logged;

    /// <summary>PZA's Looks is loaded and new enough. Looks again at most every 2 s while it is missing.</summary>
    public static bool Available
    {
        get
        {
            if (s_api is not null) return true;
            long now = Stopwatch.GetTimestamp();
            if (s_lastLook != 0 && Stopwatch.GetElapsedTime(s_lastLook, now).TotalSeconds < 2) return false;
            s_lastLook = now;
            Find();
            return s_api is not null;
        }
    }

    /// <summary>Forgets what was found (the offline check, between "missing" and "loaded").</summary>
    public static void Reset() { s_api = null; s_current = s_names = s_fish = null; s_set = s_fishSet = null; s_lastLook = 0; s_logged = false; }

    static void Find()
    {
        Type? t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(TypeName); } catch { t = null; }
            if (t is not null) break;
        }
        if (t is null) return;
        int version = t.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int v ? v : 0;
        var current = t.GetProperty("TiltShift", BindingFlags.Public | BindingFlags.Static);
        var names = t.GetProperty("TiltShiftNames", BindingFlags.Public | BindingFlags.Static);
        var set = t.GetMethod("SetTiltShift", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        bool ok = version >= WantVersion && current?.PropertyType == typeof(string) && names?.PropertyType == typeof(string[]) && set?.ReturnType == typeof(bool);
        if (!s_logged)
        {
            s_logged = true;
            Log.Line(ok ? $"PZA's Looks found (LooksApi version {version}): the Tilt-Shift buttons work"
                        : $"PZA's Looks found but its LooksApi is version {version} or lacks a member: the Tilt-Shift buttons stay disabled");
        }
        if (!ok) return;
        (s_api, s_current, s_names, s_set) = (t, current, names, set);
        if (version >= FisheyeVersion)
        {
            var fish = t.GetProperty("Fisheye", BindingFlags.Public | BindingFlags.Static);
            var fishSet = t.GetMethod("SetFisheye", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
            if (fish?.PropertyType == typeof(string) && fishSet?.ReturnType == typeof(bool)) (s_fish, s_fishSet) = (fish, fishSet);
            Log.Line(s_fish is null ? "PZA's Looks' LooksApi 2 lacks Fisheye / SetFisheye: the Fisheye button stays disabled" : "PZA's Looks has the fisheye: the Fisheye button works");
        }
    }

    /// <summary>The Tilt-Shift now ("Off", "PZA Miniature", ...), or null without PZA's Looks.</summary>
    public static string? Current
    {
        get
        {
            if (!Available) return null;
            try { return s_current!.GetValue(null) as string; }
            catch (Exception e) { Log.Exception("LooksApi.TiltShift", e); return null; }
        }
    }

    /// <summary>PZA's Looks' own names (to check the buttons match them).</summary>
    public static string[] Names => Available && s_names!.GetValue(null) is string[] n ? n : [];

    /// <summary>The fisheye now ("Off", "PZA Fisheye", ...), or null without PZA's Looks 0.3.0 (LooksApi 2).</summary>
    public static string? Fisheye
    {
        get
        {
            if (!Available || s_fish is null) return null;
            try { return s_fish.GetValue(null) as string; }
            catch (Exception e) { Log.Exception("LooksApi.Fisheye", e); return null; }
        }
    }

    /// <summary>Sets the fisheye by name ("Off" turns it off); false without it or for an unknown name.</summary>
    public static bool SetFisheye(string name, string from)
    {
        if (!Available || s_fishSet is null) { Counters.Add("fisheye.no_looks"); return false; }
        try
        {
            bool ok = (bool)s_fishSet.Invoke(null, [name])!;
            Counters.Add(ok ? "fisheye.set" : "fisheye.unknown_name");
            Log.Line(ok ? $"Fisheye {name} ({from})" : $"PZA's Looks does not know Fisheye '{name}' ({from})");
            return ok;
        }
        catch (Exception e) { Log.Exception("LooksApi.SetFisheye", e); return false; }
    }

    /// <summary>Sets the Tilt-Shift by name; false without PZA's Looks or for a name it does not know.</summary>
    public static bool Set(string name, string from)
    {
        if (!Available) { Counters.Add("tiltshift.no_looks"); return false; }
        try
        {
            bool ok = (bool)s_set!.Invoke(null, [name])!;
            Counters.Add(ok ? "tiltshift.set" : "tiltshift.unknown_name");
            Log.Line(ok ? $"Tilt-Shift {name} ({from})" : $"PZA's Looks does not know Tilt-Shift '{name}' ({from})");
            return ok;
        }
        catch (Exception e) { Log.Exception("LooksApi.SetTiltShift", e); return false; }
    }
}
