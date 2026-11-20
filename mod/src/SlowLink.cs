// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.5.0: the shots' door to PZA's SlowMo for Meitou, found by type name (neither mod references the other), as LooksLink:
//   SlowMoApi (0.3.0+, contract version 1): Hold(string holder, double speed) -> bool, Release(string holder). Time is only ever asked of
//             PZA's SlowMo through it, never set here: a pause stays a pause, the player's own speed buttons win, the slowest holder wins.
//   Driver.Settings (read only): the kill cam's switches, so "Kill cam shots" follows PZA's SlowMo's own kill cam (on, which kinds, how long).
// Without PZA's SlowMo (or an older one) the shots still play; time stays whatever the caller or the player set.
using System.Diagnostics;
using System.Reflection;

namespace PZA.Camera.Meitou;

public static class SlowLink
{
    public const string ApiType = "PZA.SlowMotion.Meitou.SlowMoApi", DriverType = "PZA.SlowMotion.Meitou.Driver";
    public const int WantVersion = 1;
    /// <summary>The shots' holder name in PZA's SlowMo's hold door.</summary>
    public const string Holder = "pza.camera.shot";

    static MethodInfo? s_hold, s_release;
    static FieldInfo? s_settings;
    static long s_lastLook;
    static bool s_logged;
    static double s_held = double.NaN;

    /// <summary>PZA's SlowMo's hold door is there. Looks again at most every 2 s while it is missing.</summary>
    public static bool Available
    {
        get
        {
            if (s_hold is not null) return true;
            long now = Stopwatch.GetTimestamp();
            if (s_lastLook != 0 && Stopwatch.GetElapsedTime(s_lastLook, now).TotalSeconds < 2) return false;
            s_lastLook = now;
            Find();
            return s_hold is not null;
        }
    }

    /// <summary>Forgets what was found (the offline check).</summary>
    public static void Reset() { s_hold = s_release = null; s_settings = null; s_lastLook = 0; s_logged = false; s_held = double.NaN; }

    static void Find()
    {
        Type? api = null, driver = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { api ??= asm.GetType(ApiType); driver ??= asm.GetType(DriverType); } catch { }
            if (api is not null) break;
        }
        if (api is null) return;
        int version = api.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int v ? v : 0;
        var hold = api.GetMethod("Hold", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(double)]);
        var release = api.GetMethod("Release", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        bool ok = version >= WantVersion && hold?.ReturnType == typeof(bool) && release is not null;
        if (!s_logged)
        {
            s_logged = true;
            Log.Line(ok ? $"PZA's SlowMo found (SlowMoApi version {version}): the shots slow time through its hold door"
                        : $"PZA's SlowMo found but its SlowMoApi is version {version} or lacks a member: the shots leave time alone");
        }
        if (!ok) return;
        (s_hold, s_release) = (hold, release);
        s_settings = driver?.GetField("Settings", BindingFlags.Public | BindingFlags.Static);
    }

    /// <summary>Asks for <paramref name="speed"/> (only when it changed). False without PZA's SlowMo or when it refused.</summary>
    public static bool Hold(double speed, string why)
    {
        if (s_held == speed) return true;
        if (!Available) { Counters.Add("shot.time.no_slowmo"); return false; }
        try
        {
            bool ok = (bool)s_hold!.Invoke(null, [Holder, speed])!;
            Counters.Add(ok ? "shot.time.hold" : "shot.time.refused");
            Log.Line(ok ? $"shot time: {speed:0.00}x ({why})" : $"shot time: PZA's SlowMo refused {speed:0.00}x ({why})");
            s_held = ok ? speed : double.NaN;
            return ok;
        }
        catch (Exception e) { Log.Exception("SlowMoApi.Hold", e); return false; }
    }

    /// <summary>Ends the shots' hold (nothing when none is held).</summary>
    public static void Release(string why)
    {
        if (double.IsNaN(s_held)) return;
        s_held = double.NaN;
        if (s_release is null) return;
        try { s_release.Invoke(null, [Holder]); Counters.Add("shot.time.release"); Log.Line($"shot time: released ({why})"); }
        catch (Exception e) { Log.Exception("SlowMoApi.Release", e); }
    }

    /// <summary>The speed held now (NaN = none).</summary>
    public static double Held => s_held;

    /// <summary>PZA's SlowMo's kill cam as its settings stand: on, kills / knockouts / limbs / own characters, seconds. Null without PZA's
    /// SlowMo or when its settings can't be read (then "Kill cam shots" does nothing).</summary>
    public static (bool On, bool Kills, bool Knockouts, bool Limbs, bool Own, double Seconds)? KillCam()
    {
        if (!Available || s_settings?.GetValue(null) is not { } st) return null;
        try
        {
            var t = st.GetType();
            bool B(string n, bool d) => t.GetProperty(n)?.GetValue(st) is bool b ? b : d;
            double sec = t.GetProperty("KillCamSeconds")?.GetValue(st) is double d ? d : 1.0;
            if (t.GetProperty("KillCam") is null) return null;
            return (B("KillCam", false), B("KillCamKills", true), B("KillCamKnockouts", true), B("KillCamLimbs", true), B("KillCamOwn", false), sec);
        }
        catch (Exception e) { Log.Exception("PZA's SlowMo kill cam settings", e); return null; }
    }
}
