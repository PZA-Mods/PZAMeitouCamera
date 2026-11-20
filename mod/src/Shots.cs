// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.5.0 called-shot shots, the director (host thread, once a frame from Driver.OnFrame, after the player's own camera was drawn):
//   Frame    while the player picks a target: the camera eases to a two-shot of the attacker and the target.
//   Shot     a queued strike or shot plays: a cut to one angle (over the shoulder, side-on low and wide, a close-up on the target, the bolt
//            in flight), picked at random by ShotCore.Choose, never a blocked one; re-picked when a character walks into the line.
//   Between  after a shot: back to the two-shot, time faster, until the next shot or the release.
//   Impact   a kill, a knockout or a severed limb by the shot's attacker on its target (seen by ShotWatch, or told by the caller): a cut to the
//            close-up and the slowest time.
//   Return   the release: the camera eases back to the player's camera as it is drawn THIS frame, so it lands exactly on the player's camera
//            and settings (the shots never touch the rig, a setting or the player's own camera layer; the game redraws those every frame).
// Time is asked of PZA's SlowMo's hold door (SlowLink, holder "pza.camera.shot"), only when the caller handed time to the camera; never set here.
// Paused game (Driver.Paused): the run's timers stand still, eases under way finish. Caps: a two-shot left on for 2 minutes, "between" for 15 s, or a character gone for 1 s ends in a Return; a new world or the title screen
// stops at once. One caller at a time (the kill cam shots give way to a caller).
using System.Numerics;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using Meitou.Simulation;
using PZA.MeitouUi;

namespace PZA.Camera.Meitou;

public static class Shots
{
    public enum Mode { None, Frame, Shot, Between, Impact, Return }

    public const string KillCamCaller = "pza.camera.killcam", PreviewCaller = "pza.camera.preview";
    public const double FrameMax = 120, BetweenMax = 15, MissingMax = 1, BlockedMax = 0.25, PreviewFrame = 1.2;

    static volatile int s_mode;
    static volatile string s_caller = "", s_playing = "";
    static CharacterId s_att = CharacterId.None, s_vic = CharacterId.None;
    static bool s_time, s_endAfter, s_preview, s_havePose;
    static ShotKind s_kind, s_last;
    static int s_side;
    static double s_left, s_inMode, s_missing, s_recheck, s_blockedFor, s_blendT, s_blendLen;
    static Pose s_from, s_drawn;
    static Random s_rng = new();
    static WorldSession? s_session;
    static Vector3 s_attPos, s_vicPos;

    /// <summary>The pair ShotWatch watches (tick thread): attacker and target of the running shot.</summary>
    public static volatile Watching? Watch;
    public sealed record Watching(CharacterId Attacker, CharacterId Victim);

    public static Mode Now => (Mode)s_mode;
    public static bool Active => s_mode != (int)Mode.None;
    public static string Caller => s_caller;
    /// <summary>The angle on screen now ("OverShoulder", ...; "" = none).</summary>
    public static string Playing => s_playing;

    /// <summary>The offline check's random numbers (a fixed seed makes the picks repeatable).</summary>
    public static void Seed(int seed) => s_rng = new Random(seed);

    #region Requests (host thread, through Driver.Queue)

    /// <summary>Whether <paramref name="caller"/> may take the camera now: the setting is on, and nobody else (but the kill cam shots) holds it.</summary>
    public static bool MayTake(string caller) =>
        Driver.Settings.Shots && (!Active || s_caller == caller || s_caller == KillCamCaller || s_mode == (int)Mode.Return);

    /// <summary>Starts or changes a run. <paramref name="mode"/>: Frame, Shot or Impact.</summary>
    public static bool Start(string caller, CharacterId att, CharacterId vic, Mode mode, ShotKind want, double seconds, bool time, bool endAfter, string from)
    {
        if (!MayTake(caller))
        {
            Counters.Add(Driver.Settings.Shots ? "shot.refused.busy" : "shot.refused.off");
            Log.Line(Driver.Settings.Shots ? $"shot refused: {caller} ({from}) while {s_caller} has the camera" : $"shot refused: called-shot shots are off ({caller}, {from})");
            return false;
        }
        if (att.IsNone || vic.IsNone) { Counters.Add("shot.refused.no_character"); return false; }
        bool fresh = !Active || s_mode == (int)Mode.Return || s_caller != caller;
        if (fresh) { s_havePose = false; s_last = ShotKind.None; }
        s_caller = caller;
        if (fresh) s_session = Driver.Host is { } h ? Access.Session(h) : null;   // a run started in this world (Apply stops it in another)
        (s_att, s_vic) = (att, vic);
        Watch = new Watching(att, vic);
        s_time = time;
        s_endAfter = endAfter;
        s_missing = 0;
        Enter(mode, want, seconds, from);
        return true;
    }

    /// <summary>A kill, knockout or severed limb on the running shot (the caller's word or ShotWatch's): the close-up, slowest time.</summary>
    public static void Impact(string caller, string what, string from)
    {
        if (!Active || s_mode == (int)Mode.Return || (caller != s_caller && caller != KillCamCaller)) { Counters.Add("shot.impact.ignored"); return; }
        Counters.Add("shot.impact." + what);
        Log.Line($"shot impact: {what} ({from})");
        Enter(Mode.Impact, ShotKind.ImpactClose, Driver.Settings.ShotImpactSeconds, from);
    }

    /// <summary>The release: back to the player's camera (eased). Only the caller holding the camera (or "" = anyone: the window, a new world).</summary>
    public static void Release(string caller, string why)
    {
        if (!Active || s_mode == (int)Mode.Return) return;
        if (caller != "" && caller != s_caller) { Counters.Add("shot.release.not_owner"); return; }
        BeginReturn(why);
    }

    /// <summary>The window's Preview button and the F10 row: the two-shot, one random angle, back. The followed (or first selected) character
    /// is the attacker, the nearest other character the target.</summary>
    public static string Preview(string from)
    {
        if (Driver.Host is not { } h || Access.Session(h) is not { } session) return "no game";
        CharacterId? me = PzaUi.Player is { } p ? Access.Follow(p) : null;
        if (me is null && PzaUi.SelectedCharacters() is { Count: > 0 } sel) me = sel[0];
        var chars = session.CurrentSnapshot.Characters;
        var a = me is { } id ? chars.FirstOrDefault(c => c.Id == id) : null;
        if (a is null) { Log.Line($"preview: select a character first ({from})"); return "select a character first"; }
        CharacterSnapshot? best = null;
        float bd = 300 * 300;
        foreach (var c in chars)
        {
            if (c.Id == a.Id) continue;
            float d = Vector3.DistanceSquared(c.Position, a.Position);
            if (d < bd) { bd = d; best = c; }
        }
        if (best is null) { Log.Line($"preview: nobody within 300 of {a.Name} ({from})"); return "nobody near"; }
        if (!Start(PreviewCaller, a.Id, best.Id, Mode.Frame, ShotKind.Frame, 0, time: true, endAfter: false, from)) return "refused";
        s_preview = true;
        Log.Line($"preview: {a.Name} -> {best.Name} ({MathF.Sqrt(bd):0} apart; {from})");
        return $"{a.Name} -> {best.Name}";
    }

    /// <summary>Kill cam shots (setting, off by default): PZA's SlowMo's kill cam slows time for a kill, knockout or severed limb; the camera
    /// cuts to the close-up for that moment and comes back. Never over a caller's run (its own Impact covers that).</summary>
    public static void KillCamMoment(CharacterId att, CharacterId vic, string what, double seconds)
    {
        if (Active && s_caller != KillCamCaller && s_mode != (int)Mode.Return)
        {
            if (att == s_att && vic == s_vic) Impact(s_caller, what, "seen by the camera");
            return;
        }
        if (!Driver.Settings.KillCamShots) return;
        Start(KillCamCaller, att, vic, Mode.Impact, ShotKind.ImpactClose, seconds, time: false, endAfter: true, "PZA's SlowMo kill cam: " + what);
    }

    static void Enter(Mode mode, ShotKind want, double seconds, string from)
    {
        var was = (Mode)s_mode;
        s_mode = (int)mode;
        s_inMode = 0;
        s_blockedFor = 0;
        s_recheck = 0;
        s_left = mode switch
        {
            Mode.Shot => ShotCore.Seconds(seconds, Driver.Settings.ShotSeconds),
            Mode.Impact => Math.Clamp(double.IsFinite(seconds) && seconds > 0 ? seconds : Driver.Settings.ShotImpactSeconds, 0.25, 5),
            _ => 0,
        };
        s_kind = mode == Mode.Frame || mode == Mode.Between ? ShotKind.Frame : want;
        s_pendingPick = true;
        // the two-shot eases in; a shot, an impact cut at once (the cut is the point)
        s_blendLen = mode is Mode.Frame or Mode.Between ? (was is Mode.None or Mode.Return ? 0.6 : 0.45) : 0;
        s_blendT = 0;
        s_from = s_drawn;
        Phase(mode switch { Mode.Frame => ShotPhase.Pick, Mode.Shot => ShotPhase.Shot, Mode.Between => ShotPhase.Between, _ => ShotPhase.Impact }, from);
        Counters.Add("shot.mode." + mode);
        Log.Line($"shot: {mode}{(mode is Mode.Shot or Mode.Impact ? $" {(want == ShotKind.None ? "any angle" : want.ToString())} for {s_left:0.##} s" : "")} ({s_caller}; {from})");
    }

    static bool s_pendingPick;

    static void Phase(ShotPhase phase, string why)
    {
        if (!s_time || !Driver.Settings.ShotTime) { SlowLink.Release("the caller keeps time"); return; }
        SlowLink.Hold(ShotCore.Speed(phase, Driver.Settings), phase + ", " + why);
    }

    static void BeginReturn(string why)
    {
        s_mode = (int)Mode.Return;
        s_from = s_drawn;
        s_blendT = 0;
        s_blendLen = Math.Clamp(double.IsFinite(Driver.Settings.ShotReturnSeconds) ? Driver.Settings.ShotReturnSeconds : 0.6, 0, 3);
        s_preview = false;
        Watch = null;
        SlowLink.Release(why);
        Counters.Add("shot.return");
        Log.Line($"shot: back to the player's camera ({why})");
        if (!s_havePose) Stop("nothing was drawn yet");
    }

    /// <summary>Ends at once (no easing): a new world, the title screen, the feature switched off.</summary>
    public static void Stop(string why)
    {
        if (s_mode == (int)Mode.None) return;
        s_mode = (int)Mode.None;
        s_caller = "";
        s_playing = "";
        s_preview = false;
        s_havePose = false;
        Watch = null;
        SlowLink.Release(why);
        Counters.Add("shot.end");
        Log.Line($"shot: ended ({why})");
    }

    #endregion

    #region Each frame

    /// <summary>Once a frame, after the player's own camera was drawn (or handed back): draws the shot over it. <paramref name="applied"/>:
    /// the game copied its camera this frame (false while the cinema or a scene holds the camera: then nothing is written).</summary>
    public static void Apply(WorldCamera cam, WorldSession session, float dt, bool applied, IReadOnlyList<CharacterSnapshot> chars,
        IReadOnlyList<ProjectileSnapshot> bolts, Func<float, float, float>? ground, Func<Vector3, Vector3, bool>? clear, bool paused = false)
    {
        // while the game is paused the run's clocks stand still (a shot holds its angle, nothing times out, the preview waits); an ease that is
        // under way still finishes (a release lands on the player's camera, a two-shot asked for during a paused pick arrives)
        float tdt = paused ? 0 : dt;
        if (paused && s_mode != (int)Mode.None) Counters.Add("shot.paused");
        if (s_mode == (int)Mode.None) { s_session = session; return; }
        if (!ReferenceEquals(session, s_session) && s_session is not null) { s_session = session; Stop("a new world"); return; }
        s_session = session;
        if (!Driver.Settings.Shots && s_mode != (int)Mode.Return) BeginReturn("called-shot shots switched off");
        s_inMode += tdt;

        CharacterSnapshot? a = null, v = null;
        foreach (var c in chars)
        {
            if (c.Id == s_att) a = c;
            if (c.Id == s_vic) v = c;
        }
        if (a is null || v is null)
        {
            s_missing += tdt;
            if (s_missing > MissingMax && s_mode != (int)Mode.Return) BeginReturn("a character is gone");
        }
        else
        {
            s_missing = 0;
            (s_attPos, s_vicPos) = (a.Position, v.Position);
        }

        var basePose = new Pose(cam.Target, cam.Yaw, cam.Pitch, cam.Distance, cam.FieldOfView, cam.Roll);
        var mode = (Mode)s_mode;
        switch (mode)
        {
            case Mode.Frame when s_preview && s_inMode >= PreviewFrame:
                Enter(Mode.Shot, ShotKind.None, Driver.Settings.ShotSeconds, "preview");
                s_endAfter = true;
                break;
            case Mode.Frame when s_inMode > FrameMax: BeginReturn("the two-shot was left on for 2 minutes"); break;
            case Mode.Between when s_inMode > BetweenMax: BeginReturn("15 s between shots with no word"); break;
            case Mode.Shot or Mode.Impact:
                s_left -= tdt;
                if (s_left <= 0)
                {
                    if (s_endAfter) BeginReturn(mode == Mode.Impact ? "the moment is over" : "the shot is over");
                    else Enter(Mode.Between, ShotKind.Frame, 0, "the shot is over");
                }
                break;
        }
        mode = (Mode)s_mode;
        if (mode == Mode.None) return;

        Pose drawn;
        if (mode == Mode.Return)
        {
            s_blendT += dt;
            drawn = s_blendLen <= 0 ? basePose : ShotCore.Lerp(s_from, basePose, ShotCore.Smooth((float)(s_blendT / s_blendLen)));
            if (s_blendT >= s_blendLen)
            {
                Stop("back on the player's camera");
                return;   // nothing written this frame: exactly the player's camera
            }
        }
        else
        {
            var scene = Scene(bolts, a);
            var others = Others(chars, scene);
            if (s_pendingPick) Pick(scene, others, basePose.Fov, ground, clear, "start");
            else if ((s_recheck += dt) >= 0.1)
            {   // 10 times a second: is the line still clear? A bolt that landed hands over to the target
                double step = s_recheck;
                s_recheck = 0;
                var live = ShotCore.Place(s_kind, s_side, scene, basePose.Fov);
                string? no = live is { } lp ? ShotCore.Blocked(s_kind, lp, scene, ground, others, clear) : "the bolt landed";
                s_blockedFor = no is null ? 0 : s_blockedFor + step;
                if (live is null) Pick(scene, others, basePose.Fov, ground, clear, "the bolt landed", ShotKind.ImpactClose);
                else if (s_blockedFor > BlockedMax) Pick(scene, others, basePose.Fov, ground, clear, no!, s_kind == ShotKind.Frame ? ShotKind.Frame : ShotKind.None);
            }
            if (s_mode == (int)Mode.Return || s_mode == (int)Mode.None) return;
            var goal = ShotCore.Place(s_kind, s_side, scene, basePose.Fov) ?? s_drawn;
            if (s_blendT < s_blendLen)
            {
                s_blendT += dt;
                drawn = ShotCore.Lerp(s_havePose ? s_from : basePose, goal, ShotCore.Smooth((float)(s_blendT / s_blendLen)));
            }
            else drawn = goal;
        }
        if (!drawn.Finite) { Counters.Add("shot.not_finite"); return; }
        s_drawn = drawn;
        s_havePose = true;
        if (!applied) { Counters.Add("shot.held_by_scene"); return; }
        cam.Target = drawn.Target;
        cam.Yaw = drawn.Yaw;
        cam.Pitch = drawn.Pitch;
        cam.Distance = drawn.Distance;
        cam.FieldOfView = drawn.Fov;
        cam.Roll = drawn.Roll;
        Driver.NoteShotLens(basePose.Fov, drawn.Fov);
        CameraFocus.Point = drawn.Target;
        CameraFocus.Eye = cam.Eye;
        CameraFocus.Distance = Vector3.Distance(drawn.Target, CameraFocus.Eye);
        CameraFocus.FieldOfView = drawn.Fov;
        CameraFocus.Camera = "Shot " + s_playing;
        Counters.Add("shot.drawn." + s_kind);
    }

    static ShotScene Scene(IReadOnlyList<ProjectileSnapshot> bolts, CharacterSnapshot? a)
    {
        Vector3? bolt = null;
        Vector3 dir = default;
        foreach (var b in bolts)
            if (b.Shooter == s_att && !b.Stuck) { bolt = b.Position; dir = b.Direction; }   // the newest in flight
        return new ShotScene(s_attPos, s_vicPos, bolt, dir, a?.RangedDrawn ?? false);
    }

    static List<Vector3> Others(IReadOnlyList<CharacterSnapshot> chars, ShotScene s)
    {
        var mid = (s.Attacker + s.Victim) / 2;
        float r = Vector3.Distance(s.Attacker, s.Victim) / 2 + 150;
        var list = new List<Vector3>();
        foreach (var c in chars)
        {
            if (Vector3.DistanceSquared(c.Position, mid) > r * r) continue;
            list.Add(c.Position);
            if (list.Count >= 64) break;
        }
        return list;
    }

    static void Pick(ShotScene scene, List<Vector3> others, float fov, Func<float, float, float>? ground, Func<Vector3, Vector3, bool>? clear, string why,
        ShotKind? want = null)
    {
        s_pendingPick = false;
        s_blockedFor = 0;
        var w = want ?? s_kind;
        var pick = ShotCore.Choose(w, scene, fov, ground, others, clear, s_rng, avoid: s_last);
        if (pick.Kind == ShotKind.None)
        {
            Counters.Add("shot.all_blocked");
            Log.Line($"shot: every angle is blocked ({pick.Why.TrimEnd(' ', ';')}): the player's camera stays");
            BeginReturn("every angle is blocked");
            return;
        }
        bool cut = s_havePose && s_kind != ShotKind.Frame && w != ShotKind.Frame;
        if (cut) { s_blendT = s_blendLen = 0; }
        s_kind = pick.Kind;
        s_side = pick.Side;
        if (pick.Kind != ShotKind.Frame && pick.Kind != ShotKind.Overview) s_last = pick.Kind;
        s_playing = pick.Kind.ToString();
        Counters.Add("shot.pick." + pick.Kind);
        if (pick.Tried > 1) Counters.Add("shot.fallback");
        Log.Line($"shot angle: {pick.Kind} from the {(pick.Side > 0 ? "right" : "left")} ({why}{(pick.Tried > 1 ? $"; {pick.Tried - 1} blocked first: {pick.Why.TrimEnd(' ', ';')}" : "")})");
    }

    #endregion
}
