// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The rules, pure maths with no game types, so the offline check can test them with numbers worked out by hand. Same rules as the RE_Kenshi
// plugin (PZACamera src/PZACamera.cpp, src/LensMath.h), in Meitou's camera conventions (Meitou.Game.Host.Cameras.CameraState):
//   yaw   radians; the eye sits at target + (sin yaw, ., cos yaw) * distance, so the view looks along (-sin yaw, -cos yaw); positive turns left.
//   pitch radians of the eye above the horizon seen from the target: a view 30 degrees down is +30 degrees (the plugin's "degrees below the horizon").
//   A character faces (sin y, cos y) of its own yaw (Meitou AiSystem: Yaw = Atan2(dx, dz)): a camera IN FRONT has yaw = y, BEHIND has y + pi.
namespace PZA.Camera.Meitou;

/// <summary>The cameras, in the menu's order (the plugin's).</summary>
public enum Cam { Off, Free, Diorama, DioramaLow, Isometric, PixelIsometric, TopDown, SideView, Profile, HipLevel, Turntable, Chase, Cinematic }

public static class CamCore
{
    public const float Deg = MathF.PI / 180;

    /// <summary>The highest drawn pitch: straight down has no "forward" (the plugin's TopTilt cap).</summary>
    public const float MaxPitchDeg = 85, MinPitchDeg = -10;

    /// <summary>The long-lens cameras: far back with a narrow lens. They own the lens (no 0.5x lens on top).</summary>
    public static bool LongLens(Cam c) => c is Cam.Isometric or Cam.PixelIsometric or Cam.SideView or Cam.Profile;

    /// <summary>Cameras that turn the view by themselves (they need the character the camera follows, or none for Turntable).</summary>
    public static bool FollowsCharacter(Cam c) => c is Cam.Profile or Cam.HipLevel or Cam.Chase;

    /// <summary>The tilt a camera holds, degrees below the horizon; null = the player's own tilt (Off, Free).</summary>
    public static double? Tilt(Cam c, CamSettings s) => c switch
    {
        Cam.Diorama => s.Tilt,
        Cam.DioramaLow => s.LowTilt,
        Cam.Isometric => s.IsoTilt,
        Cam.PixelIsometric => s.PixelIsoTilt,
        Cam.TopDown => s.TopTilt,
        Cam.SideView or Cam.Profile => s.SideTilt,
        Cam.HipLevel => s.HipTilt,
        Cam.Turntable => s.TurntableTilt,
        Cam.Chase => s.ChaseTilt,
        _ => null,
    };

    /// <summary>A camera's own lens in degrees; null = the game's lens (with the 0.5x lens on top when switched on).</summary>
    public static double? LensDeg(Cam c, CamSettings s) => c switch
    {
        Cam.Isometric => s.IsoFov,
        Cam.PixelIsometric => s.PixelIsoFov,
        Cam.SideView or Cam.Profile => s.SideFov,
        Cam.Diorama or Cam.DioramaLow => s.DioramaFov > 0 ? s.DioramaFov : null,
        _ => null,
    };

    /// <summary>The drawn pitch for a tilt in degrees, kept in [-10, 85] (radians).</summary>
    public static float Pitch(double tiltDeg) => (float)Math.Clamp(double.IsFinite(tiltDeg) ? tiltDeg : 30, MinPitchDeg, MaxPitchDeg) * Deg;

    /// <summary>The 0.5x lens: the vertical lens that shows 1 / zoom times the width with straight lines kept straight (the plugin's
    /// lensmath::WideFov): tan(new / 2) = tan(old / 2) / zoom. zoom is kept in [0.2, 1]; the result below 170 degrees.</summary>
    public static float WideFov(float fov, double zoom)
    {
        double z = Math.Clamp(double.IsFinite(zoom) ? zoom : 0.5, 0.2, 1);
        double f = 2 * Math.Atan(Math.Tan(fov / 2.0) / z);
        return (float)Math.Min(f, 170 * Math.PI / 180);
    }

    /// <summary>The vertical lens drawn (radians), from the game's lens.</summary>
    public static float Fov(Cam c, CamSettings s, float gameFov, bool wide)
    {
        if (LensDeg(c, s) is { } lens) return (float)Math.Clamp(lens, 1, 120) * Deg;
        return wide && c != Cam.Off ? WideFov(gameFov, s.WideZoom) : gameFov;
    }

    /// <summary>How much farther a long-lens camera sits so the zoom frames the same: tan(game / 2) / tan(lens / 2); 1 for the others.</summary>
    public static float DistanceFactor(Cam c, float gameFov, float fov) =>
        LongLens(c) && fov > 0 && fov < gameFov ? MathF.Tan(gameFov / 2) / MathF.Tan(fov / 2) : 1;

    /// <summary>An angle into (-pi, pi].</summary>
    public static float Wrap(float a)
    {
        if (!float.IsFinite(a)) return 0;
        a %= 2 * MathF.PI;
        if (a > MathF.PI) a -= 2 * MathF.PI;
        if (a <= -MathF.PI) a += 2 * MathF.PI;
        return a;
    }

    /// <summary>The turn that eases <paramref name="from"/> towards <paramref name="to"/> along the shorter way: rate per second (0 or less =
    /// at once).</summary>
    public static float EaseTurn(float from, float to, double rate, float dt)
    {
        float gap = Wrap(to - from);
        if (rate <= 0) return gap;
        return gap * (float)Math.Min(1, rate * Math.Max(0, dt));
    }

    /// <summary>A value easing towards a goal (the pitch): rate per second (0 or less = at once).</summary>
    public static float Ease(float from, float to, double rate, float dt) =>
        rate <= 0 || !float.IsFinite(from) ? to : from + (to - from) * (float)Math.Min(1, rate * Math.Max(0, dt));

    /// <summary>The nearest of offset + n x step (degrees) to a yaw (radians); step 0 = the yaw itself.</summary>
    public static float Settle(float yaw, double stepDeg, double offsetDeg)
    {
        if (!(stepDeg > 0)) return yaw;
        double step = stepDeg * Math.PI / 180, off = offsetDeg * Math.PI / 180;
        double n = Math.Round((yaw - off) / step);
        return Wrap((float)(off + n * step));
    }

    /// <summary>Chase: the camera behind a character facing <paramref name="charYaw"/> (in front with <paramref name="front"/>).</summary>
    public static float ChaseYaw(float charYaw, bool front) => Wrap(charYaw + (front ? 0 : MathF.PI));

    /// <summary>Hip level: <paramref name="angleDeg"/> round from the character's front (0 = straight at the front, 180 = from behind).</summary>
    public static float HipYaw(float charYaw, double angleDeg) => Wrap(charYaw + (float)(angleDeg * Math.PI / 180));

    /// <summary>Profile: at the character's side. side 1 = +90 degrees round from its front, -1 = -90 degrees, 0 = whichever side the camera
    /// is nearer now (so a character who turns round walks the other way across the screen, as the plugin's ProfileSide=0).</summary>
    public static float ProfileYaw(float charYaw, float camYaw, int side)
    {
        float a = Wrap(charYaw + MathF.PI / 2), b = Wrap(charYaw - MathF.PI / 2);
        if (side > 0) return a;
        if (side < 0) return b;
        return MathF.Abs(Wrap(a - camYaw)) <= MathF.Abs(Wrap(b - camYaw)) ? a : b;
    }

    /// <summary>Off-centre: the sideways shift of the point the camera looks at, world units: + = the camera slides left, so the character sits
    /// right of the centre (the plugin's ShiftLeftPct). Scaled by the zoom's distance, not the long lens's: the character moves the same share
    /// of the screen on every camera.</summary>
    public static System.Numerics.Vector3 OffCentre(float yaw, float zoomDistance, double pct)
    {
        // the camera's right on the ground (Meitou FollowFocus): it looks along (-sin yaw, 0, -cos yaw)
        var right = new System.Numerics.Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        return right * (float)(-Math.Clamp(pct, -50, 50) / 100.0 * zoomDistance);
    }

    /// <summary>Cinematic's playlist: every camera but Off, Free, Cinematic itself and (unless asked) Hip level.</summary>
    public static Cam[] Playlist(bool hipLevel) =>
        [.. Enum.GetValues<Cam>().Where(c => c is not (Cam.Off or Cam.Free or Cam.Cinematic) && (hipLevel || c != Cam.HipLevel))];

    /// <summary>The camera after <paramref name="c"/> in Cinematic's playlist.</summary>
    public static Cam NextInPlaylist(Cam c, bool hipLevel)
    {
        var list = Playlist(hipLevel);
        int i = Array.IndexOf(list, c);
        return list[(i + 1) % list.Length];
    }

    /// <summary>The drawn camera's yaw, tilt or boom is no longer what the game copied from the rig: something else placed it (the stand-down
    /// rule; the follow camera moves only the target, which is allowed).</summary>
    public static bool Turned(float yaw, float pitch, float distance, float appliedYaw, float appliedPitch, float appliedDistance) =>
        MathF.Abs(Wrap(yaw - appliedYaw)) > 1e-4f || MathF.Abs(pitch - appliedPitch) > 1e-4f || MathF.Abs(distance - appliedDistance) > 1e-3f;

    /// <summary>The step a settings row takes, kept in its range and snapped to the step (no float drift: 0.1 + 0.2 shows 0.3).</summary>
    public static double StepValue(double value, double step, double min, double max, int dir)
    {
        double v = (double.IsFinite(value) ? value : min) + step * Math.Sign(dir);
        v = Math.Round(v / step) * step;
        return Math.Round(Math.Clamp(v, min, max), 4);
    }
}
