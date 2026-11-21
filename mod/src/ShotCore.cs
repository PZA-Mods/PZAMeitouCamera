// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.5.0 called-shot shots: the pure rules (no game types), so the offline check and the fuzz harness test them with hand-worked numbers.
//   Pose     one drawn camera in Meitou's conventions (CamCore's header): the point looked at, yaw, pitch (+ = looking down), boom, lens, roll.
//            FromEye turns "an eye here, looking at that point" into one; Lerp blends two (yaw the short way round).
//   Place    where each shot kind puts the eye for an attacker and a target (feet positions; a figure is 19.5 units tall).
//   Choose   the angle picker: the wanted kind first, then the others, each from both sides, then a high overview; the first whose eye is
//            above the ground, outside every character and sees the subjects (the ground sampled along each sight line, plus the game's
//            own wall test passed in) wins. None at all: the player's camera stays.
//   Speed    the time asked of PZA's SlowMo per phase (picking, a shot, between shots, a kill / knockout / severed limb).
using System.Numerics;

namespace PZA.Camera.Meitou;

/// <summary>The shots. Frame = the two-shot while the player picks; Overview = the high fallback when every other angle is blocked.</summary>
public enum ShotKind { None, Frame, OverShoulder, SideLow, ImpactClose, BoltFollow, Overview }

/// <summary>The time phases a shot asks PZA's SlowMo for.</summary>
public enum ShotPhase { Pick, Shot, Between, Impact }

/// <summary>A drawn camera: the eye sits at Target + (cos p sin y, sin p, cos p cos y) x Distance. Angles in radians.</summary>
public readonly record struct Pose(Vector3 Target, float Yaw, float Pitch, float Distance, float Fov, float Roll)
{
    public Vector3 Eye => Target + new Vector3(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw)) * Distance;

    public bool Finite => float.IsFinite(Target.X + Target.Y + Target.Z + Yaw + Pitch + Distance + Fov + Roll) && Distance > 0 && Fov > 0;
}

/// <summary>What a shot is about: feet positions, the attacker's facing, a bolt in flight (ranged), the other characters near (feet).</summary>
public readonly record struct ShotScene(Vector3 Attacker, Vector3 Victim, Vector3? Bolt = null, Vector3 BoltDir = default, bool Ranged = false);

/// <summary>The picker's answer. Kind None = nothing clear: leave the player's camera alone.</summary>
public readonly record struct ShotPick(ShotKind Kind, int Side, Pose Pose, int Tried, string Why);

public static class ShotCore
{
    /// <summary>A Kenshi human (ZoomStops.CharH) and the room kept round a body's axis.</summary>
    public const float H = 19.5f, BodyRadius = 4.5f;
    /// <summary>The eye stays this far above the ground; the sight lines need this much air over it.</summary>
    public const float EyeClearance = 2f, SightMargin = 0.5f;
    public const float MinDistance = 6, MaxDistance = 600;
    /// <summary>The shots' lenses (degrees); 0 = the player's lens.</summary>
    public const float ShoulderLens = 45, SideLens = 55, CloseLens = 40, BoltLens = 50;

    public static Vector3 Chest(Vector3 feet) => feet + new Vector3(0, 0.7f * H, 0);
    public static Vector3 Head(Vector3 feet) => feet + new Vector3(0, 0.9f * H, 0);

    /// <summary>"An eye here, looking at that point". Pitch kept in -10..85 degrees and the boom in MinDistance..MaxDistance, so the eye may
    /// move a little (the checks use <see cref="Pose.Eye"/>, never the asked eye).</summary>
    public static Pose FromEye(Vector3 target, Vector3 eye, float fov, float roll = 0)
    {
        var v = eye - target;
        float d = v.Length();
        if (!float.IsFinite(d) || d < 1e-4f) { v = new Vector3(0, 0.5f, 1); d = MinDistance; }
        float yaw = MathF.Atan2(v.X, v.Z);
        float pitch = MathF.Asin(Math.Clamp(v.Y / v.Length(), -1, 1));
        pitch = Math.Clamp(pitch, CamCore.MinPitchDeg * CamCore.Deg, CamCore.MaxPitchDeg * CamCore.Deg);
        return new Pose(target, CamCore.Wrap(yaw), pitch, Math.Clamp(d, MinDistance, MaxDistance), fov, roll);
    }

    /// <summary>The ground line from the attacker to the target (unit; straight ahead +Z when they stand on one spot), its right and length.</summary>
    public static (Vector3 U, Vector3 Right, float L) Axis(Vector3 a, Vector3 v)
    {
        var d = new Vector3(v.X - a.X, 0, v.Z - a.Z);
        float l = d.Length();
        var u = l > 1e-3f && float.IsFinite(l) ? d / l : new Vector3(0, 0, 1);
        if (!float.IsFinite(l)) l = 0;
        // looking along u, the right is (-u.z, 0, u.x) (Meitou: looking along (-sin y, -cos y), the right is (cos y, 0, -sin y))
        return (u, new Vector3(-u.Z, 0, u.X), l);
    }

    /// <summary>The pose of one shot kind from one side (+1 / -1), or null when it cannot be made (a bolt shot with no bolt in flight).
    /// <paramref name="gameFov"/> radians: the player's lens, used by the two-shot and the overview.</summary>
    public static Pose? Place(ShotKind kind, int side, in ShotScene s, float gameFov)
    {
        float sd = side < 0 ? -1 : 1;
        if (!(gameFov > 0.1f && gameFov < 2.9f)) gameFov = 50 * CamCore.Deg;
        var (u, right, l) = Axis(s.Attacker, s.Victim);
        l = Math.Min(l, 400);   // two characters 400 apart is already a long shot
        var mid = (s.Attacker + s.Victim) / 2;
        var up = Vector3.UnitY;
        switch (kind)
        {
            case ShotKind.Frame:
            {   // behind and above the attacker, off to one side, both in the picture
                var t = mid + up * (0.55f * H);
                var eye = s.Attacker - u * (28 + 0.35f * l) + right * sd * (16 + 0.25f * l) + up * (0.9f * H + 0.25f * l);
                return Fit(t, eye, gameFov, l);
            }
            case ShotKind.OverShoulder:
                return FromEye(Chest(s.Victim), s.Attacker - u * 11 + right * sd * 6 + up * (0.95f * H), ShoulderLens * CamCore.Deg);
            case ShotKind.SideLow:
            {   // side-on, low and wide
                var t = mid + up * (0.45f * H);
                return Fit(t, mid + right * sd * (0.85f * l + 26) + up * (0.22f * H), SideLens * CamCore.Deg, l);
            }
            case ShotKind.ImpactClose:
            {   // close on the target, from the attacker's side, turned 40 degrees off the line
                var t = Chest(s.Victim) + up * (0.1f * H);
                float a = sd * 40 * CamCore.Deg;
                var back = -u;
                var dir = new Vector3(back.X * MathF.Cos(a) - back.Z * MathF.Sin(a), 0, back.X * MathF.Sin(a) + back.Z * MathF.Cos(a));
                return FromEye(t, t + dir * 24 + up * 3, CloseLens * CamCore.Deg);
            }
            case ShotKind.BoltFollow:
            {
                if (s.Bolt is not { } b || !float.IsFinite(b.X + b.Y + b.Z)) return null;
                var f = s.BoltDir;
                if (!(f.LengthSquared() > 1e-6f) || !float.IsFinite(f.X + f.Y + f.Z)) f = u;
                f = Vector3.Normalize(f);
                var r = Vector3.Normalize(new Vector3(-f.Z, 0, f.X) is var rr && rr.LengthSquared() > 1e-6f ? rr : right);
                return FromEye(b + f * 30, b - f * 16 + r * sd * 3 + up * 3, BoltLens * CamCore.Deg);
            }
            case ShotKind.Overview:
            {   // high above, 30 degrees off the line behind the attacker: the shot almost nothing hides
                var t = mid + up * (0.5f * H);
                float a = sd * 30 * CamCore.Deg;
                var back = -u;
                var dir = new Vector3(back.X * MathF.Cos(a) - back.Z * MathF.Sin(a), 0, back.X * MathF.Sin(a) + back.Z * MathF.Cos(a));
                float p = 55 * CamCore.Deg, dist = Math.Max(90, (0.5f * l + 20) / MathF.Tan(gameFov / 2));
                return FromEye(t, t + (dir * MathF.Cos(p) + up * MathF.Sin(p)) * dist, gameFov);
            }
        }
        return null;
    }

    /// <summary>The eye pushed back along its own line until both subjects (<paramref name="l"/> apart, plus a body's room) fit the lens.</summary>
    static Pose Fit(Vector3 target, Vector3 eye, float fov, float l)
    {
        var p = FromEye(target, eye, fov);
        float need = (0.5f * l + 12) / MathF.Tan(Math.Clamp(fov, 0.2f, 2.8f) / 2);
        return p.Distance >= need ? p : p with { Distance = Math.Clamp(need, MinDistance, MaxDistance) };
    }

    /// <summary>True when the ground rises over the line from <paramref name="eye"/> to <paramref name="at"/> (KeepView's sampler, a smaller
    /// margin and no gap at the subject's end: a sight line ends at a chest, well above the feet).</summary>
    public static bool GroundBlocks(Vector3 eye, Vector3 at, Func<float, float, float>? ground) =>
        KeepView.Blocked(at, eye, ground, new KeepView.Params(Margin: SightMargin, StartFrac: 0.02, Samples: 24), out _);

    /// <summary>Why this eye can't be used, or null. <paramref name="others"/>: every character near, feet (the subjects included).
    /// <paramref name="clear"/>: the game's own test of a line (walls, rocks; null = none).</summary>
    public static string? Blocked(ShotKind kind, Pose p, in ShotScene s, Func<float, float, float>? ground, IReadOnlyList<Vector3> others,
        Func<Vector3, Vector3, bool>? clear)
    {
        if (!p.Finite) return "not a number";
        var eye = p.Eye;
        if (ground is not null)
        {
            float g = ground(eye.X, eye.Z);
            if (float.IsFinite(g) && eye.Y < g + EyeClearance) return "under the ground";
        }
        foreach (var c in others)
        {
            if (!float.IsFinite(c.X + c.Y + c.Z)) continue;
            float dx = eye.X - c.X, dz = eye.Z - c.Z;
            if (dx * dx + dz * dz < BodyRadius * BodyRadius && eye.Y > c.Y - 1 && eye.Y < c.Y + H + 1) return "inside a character";
        }
        bool needsAttacker = kind is not (ShotKind.ImpactClose or ShotKind.BoltFollow);
        if (!Sees(eye, Chest(s.Victim), ground, clear)) return "the target is hidden";
        if (needsAttacker && !Sees(eye, Chest(s.Attacker), ground, clear)) return "the attacker is hidden";
        if (kind == ShotKind.BoltFollow && s.Bolt is { } b && !Sees(eye, b, ground, clear)) return "the bolt is hidden";
        return null;
    }

    static bool Sees(Vector3 eye, Vector3 at, Func<float, float, float>? ground, Func<Vector3, Vector3, bool>? clear)
    {
        if (GroundBlocks(eye, at, ground)) return false;
        if (clear is null) return true;
        try { return clear(eye, at); }
        catch { return false; }   // a test that throws never lets a camera into a wall
    }

    /// <summary>The kinds tried, in order: the wanted one first (Auto = None: a random order, the last shot's kind last, a bolt in flight first
    /// for a ranged shot), then the rest; Frame tries only itself. The Overview always comes last.</summary>
    public static ShotKind[] Order(ShotKind want, in ShotScene s, ShotKind avoid, Random rng)
    {
        if (want == ShotKind.Frame) return [ShotKind.Frame, ShotKind.Overview];
        var pool = new List<ShotKind> { ShotKind.OverShoulder, ShotKind.SideLow, ShotKind.ImpactClose };
        for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        if (avoid != ShotKind.None && pool.Remove(avoid)) pool.Add(avoid);
        if (s.Ranged && s.Bolt is not null) pool.Insert(0, ShotKind.BoltFollow);
        if (want is not (ShotKind.None or ShotKind.Overview) && want != ShotKind.Frame)
        {
            pool.Remove(want);
            pool.Insert(0, want);
        }
        pool.Add(ShotKind.Overview);
        return [.. pool];
    }

    /// <summary>The angle picker. Every kind of <see cref="Order"/> from both sides (a random side first); the first clear one wins.</summary>
    public static ShotPick Choose(ShotKind want, in ShotScene s, float gameFov, Func<float, float, float>? ground, IReadOnlyList<Vector3> others,
        Func<Vector3, Vector3, bool>? clear, Random rng, ShotKind avoid = ShotKind.None)
    {
        int tried = 0;
        string why = "";
        int first = rng.Next(2) == 0 ? 1 : -1;
        foreach (var k in Order(want, s, avoid, rng))
            foreach (int side in new[] { first, -first })
            {
                if (Place(k, side, s, gameFov) is not { } p) continue;
                tried++;
                var no = Blocked(k, p, s, ground, others, clear);
                if (no is null) return new ShotPick(k, side, p, tried, why);
                if (why.Length < 400) why += $"{k}{(side > 0 ? "+" : "-")}: {no}; ";
            }
        return new ShotPick(ShotKind.None, 0, default, tried, why);
    }

    /// <summary>A blend of two poses, t in 0..1 (eased by the caller): yaw the short way round, the boom geometric (a zoom that feels even).</summary>
    public static Pose Lerp(Pose a, Pose b, float t)
    {
        if (!float.IsFinite(t)) t = 1;
        t = Math.Clamp(t, 0, 1);
        if (!a.Finite) return b with { Yaw = CamCore.Wrap(b.Yaw) };   // fuzz 2026-11-19: an unwrapped yaw came back as it was
        if (!b.Finite) return a with { Yaw = CamCore.Wrap(a.Yaw) };
        float da = MathF.Max(a.Distance, 1e-3f), db = MathF.Max(b.Distance, 1e-3f);
        return new Pose(Vector3.Lerp(a.Target, b.Target, t), CamCore.Wrap(a.Yaw + CamCore.Wrap(b.Yaw - a.Yaw) * t), a.Pitch + (b.Pitch - a.Pitch) * t,
            da * MathF.Pow(db / da, t), a.Fov + (b.Fov - a.Fov) * t, a.Roll + (b.Roll - a.Roll) * t);
    }

    /// <summary>Smoothstep in 0..1 (NaN = 1: done).</summary>
    public static float Smooth(float t)
    {
        if (!float.IsFinite(t)) return 1;
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The speed asked of PZA's SlowMo in each phase, from the settings, kept in its hold range (0.05 - 1). Slowest on an impact,
    /// then picking, a shot, and fastest between shots.</summary>
    public static double Speed(ShotPhase phase, CamSettings s)
    {
        double v = phase switch
        {
            ShotPhase.Pick => s.ShotPickSpeed,
            ShotPhase.Shot => s.ShotSpeed,
            ShotPhase.Between => s.ShotBetweenSpeed,
            _ => s.ShotImpactSpeed,
        };
        return Math.Clamp(double.IsFinite(v) ? v : 0.3, 0.05, 1);
    }

    /// <summary>The sharpest f-number used while a shot eases in or out (nearly everything in focus), and the softest allowed.</summary>
    public const float SharpAperture = 22, SoftestAperture = 0.7f;

    /// <summary>0.6.0 shot focus: the f-number drawn. <paramref name="setting"/> is the player's (Shot focus f-number); a two-shot or an overview
    /// keeps both characters about sharp (at least f/4); <paramref name="t"/> 0..1 is how far the camera is into the shot (0 = still on the
    /// player's camera: sharp, 1 = the shot), so the background softens as the camera arrives and sharpens as it leaves. Always finite, in
    /// 0.7 .. 22.</summary>
    public static float Aperture(double setting, ShotKind kind, float t)
    {
        double a = double.IsFinite(setting) ? Math.Clamp(setting, SoftestAperture, SharpAperture) : 1.4;
        if (kind is ShotKind.Frame or ShotKind.Overview) a = Math.Max(a, 4);
        float k = Smooth(t);
        return (float)(SharpAperture + (a - SharpAperture) * k);   // a in 0.7 .. 22 and k in 0 .. 1 keep it there
    }

    /// <summary>A shot's length in real seconds, kept in 0.2 .. 10 (NaN or 0 = the setting's).</summary>
    public static double Seconds(double asked, double setting)
    {
        double v = double.IsFinite(asked) && asked > 0 ? asked : setting;
        return Math.Clamp(double.IsFinite(v) ? v : 1.5, 0.2, 10);
    }

    /// <summary>A kind's name from a caller ("OverShoulder", "side", ...); unknown or "Auto" = None (the picker chooses).</summary>
    public static ShotKind Parse(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return ShotKind.None;
        string k = kind.Trim().Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
        return k switch
        {
            "overshoulder" or "shoulder" => ShotKind.OverShoulder,
            "sidelow" or "side" => ShotKind.SideLow,
            "impactclose" or "impact" or "close" or "closeup" => ShotKind.ImpactClose,
            "boltfollow" or "bolt" => ShotKind.BoltFollow,
            "overview" => ShotKind.Overview,
            "frame" or "twoshot" => ShotKind.Frame,
            _ => ShotKind.None,
        };
    }
}
