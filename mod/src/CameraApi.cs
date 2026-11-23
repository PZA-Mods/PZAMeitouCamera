// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.5.0: the door for other mods (contract version 1): cinematic called-shot shots. Neither mod references the other; a caller finds this
// type by name and calls it through reflection, as PZA's Looks' LooksApi and PZA's SlowMo's SlowMoApi:
//   var api = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PZA.Camera.Meitou.CameraApi")).FirstOrDefault(t => t is not null);
// Characters are Meitou's own CharacterId (Meitou.Simulation), boxed by the reflection call. Any thread: the calls are queued and act at the
// next drawn frame; the answers say whether the request was taken (the setting is on, the mod runs, nobody else holds the camera).
// Time: with time = true the camera asks PZA's SlowMo for the shots' speeds (holder "pza.camera.shot"); the slowest holder wins, so a caller
// that hands time to the camera releases its own hold while the shots play. Without PZA's SlowMo, time is left alone.
using Meitou.Simulation;

namespace PZA.Camera.Meitou;

public static class CameraApi
{
    /// <summary>Bumped when a member changes meaning; a caller checks it is at least the version it was written for.</summary>
    public const int Version = 1;

    /// <summary>The shot kinds a caller may ask for ("Auto" = the camera picks one at random, never a blocked one).</summary>
    public static readonly string[] Kinds = ["Auto", "OverShoulder", "SideLow", "ImpactClose", "BoltFollow"];

    /// <summary>The mod runs and "Called-shot shots" is on: the calls below can be taken.</summary>
    public static bool Available => Driver.Ready && Driver.Settings.Shots;

    /// <summary>A run is on (the two-shot, a shot, between shots, an impact or the way back).</summary>
    public static bool Busy => Shots.Active;

    /// <summary>0.6.1 (an addition to v1): the Kit holds the camera now (the two-shot, a shot, between shots, an impact). False on the way back
    /// (the Kit eases onto whatever is drawn each frame) and when idle. A view mod (PZA's WASD &amp; Controller Support) stands aside while this
    /// is true and puts its view back the moment it turns false; the way back then lands on that view.</summary>
    public static bool Holding => Shots.Holding;

    /// <summary>Read only (2026-11-20, for a controller's right-stick flick): the settle step of the camera in effect now, degrees; 0 when it
    /// does not settle (camera off or stood down, or a camera without settle steps). Isometric / Pixel Isometric: their settle step; Diorama /
    /// Diorama Low: theirs (0 = free turning); Side view: its 90-degree steps. Cinematic gives the camera it plays now.</summary>
    public static double SettleStepDegrees => Settle().Step;

    /// <summary>Read only: that camera's settle offset, degrees (the steps sit at offset + n x step). 0 when it does not settle.</summary>
    public static double SettleOffsetDegrees => Settle().Offset;

    static (double Step, double Offset) Settle()
    {
        if (Driver.Current == Cam.Off || Driver.StandDown is not null) return (0, 0);
        var s = Driver.Settings;
        (double step, double off) = Driver.Effective switch
        {
            Cam.Isometric or Cam.PixelIsometric => (s.IsoYawStep, s.IsoYawOffset),
            Cam.Diorama or Cam.DioramaLow => (s.YawStep, 0),
            Cam.SideView => (s.SideYawStep, Driver.SideOffsetDeg),
            _ => (0, 0),
        };
        return double.IsFinite(step) && step > 0 ? (step, double.IsFinite(off) ? off : 0) : (0, 0);
    }

    /// <summary>The angle on screen now ("Frame", "OverShoulder", ...; "" = the player's own camera).</summary>
    public static string Playing => Shots.Active ? Shots.Playing : "";

    /// <summary>While the player picks: the camera eases to a two-shot of <paramref name="attacker"/> and <paramref name="target"/>; time crawls
    /// (with <paramref name="time"/>). Again with another target: the two-shot moves. Lasts until Shot, Release or 2 minutes.</summary>
    public static bool Frame(string caller, CharacterId attacker, CharacterId target, bool time = true) =>
        Ask(caller, () => Shots.Start(caller, attacker, target, Shots.Mode.Frame, ShotKind.Frame, 0, time, endAfter: false, "CameraApi.Frame"));

    /// <summary>A strike or shot plays out: a cut to one angle for <paramref name="seconds"/> of real time (0 = the setting, 1.5 s), then back
    /// to the two-shot (time faster) until the next Shot or Release. <paramref name="kind"/>: one of <see cref="Kinds"/>; a blocked one falls
    /// back to the next. A kill, knockout or severed limb by the attacker on the target cuts to the close-up by itself.</summary>
    public static bool Shot(string caller, CharacterId attacker, CharacterId target, string kind = "Auto", double seconds = 0, bool time = true) =>
        Ask(caller, () => Shots.Start(caller, attacker, target, Shots.Mode.Shot, ShotCore.Parse(kind), seconds, time, endAfter: false, "CameraApi.Shot " + kind));

    /// <summary>The caller's own word that the blow it just played was a kill, knockout or limb (<paramref name="what"/>: "kill", "knockout",
    /// "limb"): the close-up, the slowest time. Usually not needed (the camera sees the hit itself).</summary>
    public static void Impact(string caller, string what = "kill")
    {
        if (!Available) return;
        Driver.Queue(() => Shots.Impact(caller, what ?? "kill", "CameraApi.Impact"), "CameraApi.Impact");
    }

    /// <summary>The end: the camera eases back to exactly the player's camera, and the shots' time hold ends. Only the caller that holds
    /// the camera releases it.</summary>
    public static void Release(string caller) => Driver.Queue(() => Shots.Release(caller ?? "?", "CameraApi.Release by " + caller), "CameraApi.Release");

    /// <summary>0.6.0 (still contract version 1: an addition): a called-shot mod says it is there, best once at load (its IMod.Initialize or
    /// the first frame). The "Called-shot shots" switch is shown only to players who have such a mod; without this call it appears after the
    /// mod's first Frame or Shot. Changes nothing else.</summary>
    public static void Announce(string caller)
    {
        if (string.IsNullOrWhiteSpace(caller)) return;
        if (!Shots.CallerSeen) Log.Line($"a called-shot mod is here ({caller}): the SHOTS tab shows its switch");
        Shots.CallerSeen = true;
        Counters.Add("api.announce");
    }

    static bool Ask(string caller, Func<bool> start)
    {
        if (string.IsNullOrWhiteSpace(caller)) return false;
        if (!Shots.CallerSeen) { Shots.CallerSeen = true; Log.Line($"a called-shot mod is here ({caller}, first request)"); }
        if (!Available) { Counters.Add("api.refused"); return false; }
        if (!Shots.MayTake(caller)) { Counters.Add("api.busy"); return false; }
        Driver.Queue(() => start(), "CameraApi from " + caller);
        Counters.Add("api.taken");
        return true;
    }
}
