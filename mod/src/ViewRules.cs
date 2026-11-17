// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.3.0: Zoom stops and Keep in view, pure maths with no game types (the offline check tests them with hand-worked numbers), the RE_Kenshi
// plugin's src/ZoomStops.h (v0.15.0 / v0.16.0) and src/KeepView.h (v0.17.0 / v0.17.2) in Meitou's conventions.
using System.Numerics;

namespace PZA.Camera.Meitou;

/// <summary>Zoom stops: three boom distances per camera that the wheel steps between. While on, the boom is pinned to the current stop; when the
/// game's wheel zoom moves it away by more than a small tolerance, the stop moves ONE step that way and the boom is pinned there (the notch is
/// used up). Defaults: how tall the character stands on screen (share of the screen height), from classic games and film shot sizes, turned into
/// a boom with the camera's tilt and the game's lens (the plugin's research, 2026-11-01). Meitou's boom: 10 .. 2000.</summary>
public static class ZoomStops
{
    public const int N = 3;
    public const double MinD = 10, MaxD = 2000;
    /// <summary>Kenshi human: 19.5 units tall, ~4 deep (male_skeleton.mesh bounds, the plugin's research).</summary>
    public const double CharH = 19.5, CharW = 4;

    public static double ClampD(double v) => double.IsNaN(v) ? MinD : Math.Clamp(v, MinD, MaxD);

    /// <summary>The cameras that have stops (all but Off and Cinematic, as the plugin).</summary>
    public static bool Has(Cam c) => c is not (Cam.Off or Cam.Cinematic);

    /// <summary>The researched near / mid / far shares per camera (the plugin's ZsShares).</summary>
    public static double[] Shares(Cam c) => c switch
    {
        Cam.Diorama or Cam.DioramaLow => [0.14, 0.09, 0.05],
        Cam.Isometric or Cam.PixelIsometric => [0.18, 0.13, 0.09],
        Cam.TopDown => [0.16, 0.11, 0.07],
        Cam.SideView or Cam.Profile => [0.30, 0.20, 0.12],
        Cam.HipLevel => [0.75, 0.45, 0.30],
        Cam.Turntable => [0.50, 0.33, 0.20],
        Cam.Chase => [0.40, 0.28, 0.18],
        _ => [0.33, 0.18, 0.09],
    };

    /// <summary>The projected height of a figure at a tilt: H cos t + W sin t (seen from above it shows its top, not its height).</summary>
    public static double Extent(double tiltDeg)
    {
        double t = Math.Abs(tiltDeg) * Math.PI / 180;
        return CharH * Math.Cos(t) + CharW * Math.Sin(t);
    }

    public static double ZoomForShare(double share, double tiltDeg, double fovDeg)
    {
        if (!(share >= 0.01)) share = 0.01;
        if (share > 0.95) share = 0.95;
        if (!(fovDeg >= 10 && fovDeg <= 120)) fovDeg = 50;
        return ClampD(Extent(tiltDeg) / (2 * share * Math.Tan(fovDeg * Math.PI / 360)));
    }

    public static double ShareAtZoom(double zoom, double tiltDeg, double fovDeg)
    {
        if (!(zoom >= MinD)) zoom = MinD;
        if (!(fovDeg >= 10 && fovDeg <= 120)) fovDeg = 50;
        return Extent(tiltDeg) / (2 * zoom * Math.Tan(fovDeg * Math.PI / 360));
    }

    /// <summary>The researched stops for a camera at a tilt and lens, near (small boom) to far.</summary>
    public static double[] Defaults(Cam c, double tiltDeg, double fovDeg)
    {
        var d = Shares(c).Select(sh => Math.Round(ZoomForShare(sh, tiltDeg, fovDeg))).ToArray();
        Array.Sort(d);
        return d;
    }

    public static int Nearest(double[] s, double dist)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < s.Length; i++)
        {
            double e = Math.Abs(dist - s[i]);
            if (e < bd) { bd = e; best = i; }
        }
        return best;
    }

    public static double Tolerance(double stop) => 0.02 * stop + 0.05;

    /// <summary>The boom was pinned at stop <paramref name="idx"/>; the game moved it to <paramref name="dist"/>: the next index (one step toward
    /// the move, held at the ends). -1 = not pinned yet: the nearest.</summary>
    public static int Step(double[] s, int idx, double dist)
    {
        if (idx < 0 || idx >= s.Length) return Nearest(s, dist);
        if (double.IsNaN(dist)) return idx;
        double t = s[idx], tol = Tolerance(t);
        if (dist > t + tol) return Math.Min(idx + 1, s.Length - 1);
        if (dist < t - tol) return Math.Max(idx - 1, 0);
        return idx;
    }

    /// <summary>Stop k (0 near, 1 mid, 2 far) set to a distance, the three kept sorted; returns the sorted stops and where the new one went.</summary>
    public static (double[] Stops, int At) SetStop(double[] s, int k, double dist)
    {
        var r = (double[])s.Clone();
        double v = Math.Round(ClampD(dist));
        r[Math.Clamp(k, 0, N - 1)] = v;
        Array.Sort(r);
        return (r, Array.IndexOf(r, v));
    }
}

/// <summary>Keep in view: the game keeps only the eye above the ground, never the line from the eye to the character. When the ground hides the
/// character, the view tilts up (by <c>MaxExtra</c> at most, never past <c>MaxTilt</c> in all), then the camera comes in (to <c>MinZoomK</c> of
/// its distance); clear for <c>HoldTime</c>, it comes back slowly, zoom first, then tilt, and only while one step back would also be clear (no
/// bobbing at a ridge). Meitou conventions: pitch + = looking down; the eye sits at point + (cos p sin y, sin p, cos p cos y) x distance.</summary>
public static class KeepView
{
    public sealed record Params(double Margin = 2, double StartFrac = 0.12, int Samples = 20, double MaxExtra = 35 * Math.PI / 180,
        double MaxTilt = 1.48, double TiltRate = 0.6, double ReturnRate = 0.35, double HoldTime = 0.8, double MinZoomK = 0.45, double ZoomRate = 0.8);

    public struct State
    {
        public double Extra, ZoomK, ClearFor;
        public bool Engaged;
        public static State Home => new() { ZoomK = 1 };
    }

    public static Vector3 Eye(Vector3 point, double yaw, double pitch, double dist) =>
        point + new Vector3((float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch), (float)(Math.Cos(pitch) * Math.Cos(yaw))) * (float)dist;

    /// <summary>True when the ground rises above the line point -> eye (plus the margin), skipping the stretch next to the character;
    /// <paramref name="worst"/> = how far at the worst sample (units).</summary>
    public static bool Blocked(Vector3 point, Vector3 eye, Func<float, float, float>? ground, Params p, out double worst)
    {
        worst = 0;
        if (ground is null || !float.IsFinite(point.X + point.Y + point.Z + eye.X + eye.Y + eye.Z)) return false;
        int n = Math.Clamp(p.Samples, 4, 64);
        double s0 = Math.Clamp(double.IsNaN(p.StartFrac) ? 0 : p.StartFrac, 0, 0.9);
        double m = Math.Clamp(double.IsNaN(p.Margin) ? 0 : p.Margin, 0, 50);   // the plugin's fuzz: a junk margin blocked forever
        double w = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            double t = s0 + (1 - s0) * (i + 1) / (n + 1);   // never exactly the eye or the point
            var q = point + (eye - point) * (float)t;
            double g = ground(q.X, q.Z);
            if (!double.IsFinite(g)) continue;
            double over = g + m - q.Y;
            if (double.IsFinite(over) && over > w) w = over;
        }
        worst = w == double.MinValue ? 0 : w;
        return w > 0;
    }

    /// <summary>One decision. blockedNow: the view with the current extra / zoom; backClear: still clear one step back. baseTilt: the camera's own
    /// tilt (radians, + down). Never NaN, always inside its limits.</summary>
    public static void Step(ref State s, bool blockedNow, bool backClear, double dt, Params p, double baseTilt)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, 0.25) : 0;
        double room = Math.Max(0, p.MaxTilt - baseTilt), cap = Math.Min(p.MaxExtra, room);
        if (blockedNow)
        {
            s.ClearFor = 0;
            s.Engaged = true;
            if (s.Extra < cap - 1e-4) s.Extra = Math.Min(cap, s.Extra + p.TiltRate * dt);
            else s.ZoomK = Math.Max(p.MinZoomK, s.ZoomK - p.ZoomRate * dt);
        }
        else
        {
            s.ClearFor = Math.Min(1e6, s.ClearFor + dt);
            if (s.ClearFor >= p.HoldTime && backClear)
            {
                if (s.ZoomK < 1) s.ZoomK = Math.Min(1, s.ZoomK + p.ZoomRate * 0.5 * dt);
                else if (s.Extra > 0) s.Extra = Math.Max(0, s.Extra - p.ReturnRate * dt);
            }
        }
        if (s.Extra > cap) s.Extra = cap;   // the base tilt may have changed (another camera picked)
        if (!(s.Extra >= 0)) s.Extra = 0;
        if (!(s.ZoomK >= p.MinZoomK)) s.ZoomK = p.MinZoomK;
        if (s.ZoomK > 1) s.ZoomK = 1;
        if (s.Extra <= 0 && s.ZoomK >= 1 && !blockedNow) s.Engaged = false;
    }

    /// <summary>Switched off: ease back home with no checks.</summary>
    public static void Release(ref State s, double dt, Params p)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, 0.25) : 0;
        s.ZoomK = Math.Min(1, s.ZoomK + p.ZoomRate * dt);
        s.Extra = Math.Max(0, s.Extra - p.ReturnRate * 2 * dt);
        s.ClearFor = 0;
        if (s.Extra <= 0 && s.ZoomK >= 1) s.Engaged = false;
    }
}
