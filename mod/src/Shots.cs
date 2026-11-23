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
// Paused game (Driver.Paused): the run's timers stand still, eases under way finish. Caps: a two-shot left on for 2 minutes, "between" for 3 s, or a character gone for 1 s ends in a Return; a new world or the title screen
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
    public const double FrameMax = 120, BetweenMax = 3,   // 0.6.1: was 15; a caller that never releases kept the view 15 s (WASD headless run); 3 s still bridges strikes ~1 s apart
                         MissingMax = 1, BlockedMax = 0.25, PreviewFrame = 1.2;

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
    static bool s_vicDown;
    /// <summary>0.6.1: the side of the last shot of this fight (kept so close-ups don't flip left / right) and the pair it was for.</summary>
    static int s_keepSide;
    static (CharacterId, CharacterId) s_sidePair;
    /// <summary>0.6.1 (the user, 2026-11-22): a kill cam moment keeps the player's view: its look point glides onto the target and its zoom
    /// eases in (ShotCore.KillFocus) instead of the cut to a close-up. s_focusT: real seconds since it began (still while paused).</summary>
    static bool s_focus;
    static double s_focusT;
    /// <summary>The kill cam focus drawn now (the check reads it): true while a kill cam moment keeps the player's view.</summary>
    public static bool Focusing => s_focus && Active;

    /// <summary>0.6.0: the depth-of-field switch (get, set), wired to GameHost.WorldOptions.Post.PhotoDepthOfField when Meitou has it (CameraMod);
    /// null = no shot focus. The offline check plugs its own in.</summary>
    public static (Func<bool?> Get, Action<bool?> Set)? Dof;
    /// <summary>0.6.0: marks this frame as a cinematic camera's, so the renderer runs its depth of field (Meitou clears it every frame).</summary>
    public static Action? CinematicView;
    static bool s_dofOn, s_freshFromPlayer;
    static bool? s_dofPrev;
    /// <summary>The f-number drawn last (NaN = no focus this frame), for the check.</summary>
    public static float LastAperture = float.NaN;

    /// <summary>The pair ShotWatch watches (tick thread): attacker and target of the running shot.</summary>
    public static volatile Watching? Watch;
    public sealed record Watching(CharacterId Attacker, CharacterId Victim);

    /// <summary>0.6.0: a called-shot mod has made itself known this game (CameraApi.Announce, or any Frame / Shot request): the SHOTS tab
    /// then shows the "Called-shot shots" switch (from the next time the window is built: a game load), Settings > Mods at once.</summary>
    public static volatile bool CallerSeen;

    public static Mode Now => (Mode)s_mode;

    /// <summary>0.6.1: the Kit holds the camera (the two-shot, a shot, between shots, an impact); false on the way back and when idle.</summary>
    public static bool Holding => s_mode is (int)Mode.Frame or (int)Mode.Shot or (int)Mode.Between or (int)Mode.Impact;
    public static bool Active => s_mode != (int)Mode.None;
    public static string Caller => s_caller;
    /// <summary>The angle on screen now ("OverShoulder", ...; "" = none).</summary>
    public static string Playing => s_playing;

    /// <summary>The offline check's random numbers (a fixed seed makes the picks repeatable).</summary>
    public static void Seed(int seed) => s_rng = new Random(seed);

    #region Requests (host thread, through Driver.Queue)

    /// <summary>Whether <paramref name="caller"/> may take the camera now: the setting is on, and nobody else (but the kill cam shots) holds it.</summary>
    public static bool MayTake(string caller) =>
        SwitchOn(caller) && (!Active || s_caller == caller || s_caller == KillCamCaller || s_mode == (int)Mode.Return);

    /// <summary>0.6.1 (a code review, 2026-11-22): the switch that governs <paramref name="caller"/>: the kill cam's own "Kill cam shots",
    /// everyone else's "Called-shot shots". Before, the kill cam was refused whenever called-shot shots were off.</summary>
    public static bool SwitchOn(string caller) => caller == KillCamCaller ? Driver.Settings.KillCamShots : Driver.Settings.Shots;
    static string SwitchName(string caller) => caller == KillCamCaller ? "kill cam shots" : "called-shot shots";

    /// <summary>Starts or changes a run. <paramref name="mode"/>: Frame, Shot or Impact.</summary>
    public static bool Start(string caller, CharacterId att, CharacterId vic, Mode mode, ShotKind want, double seconds, bool time, bool endAfter, string from)
    {
        if (!MayTake(caller))
        {
            bool on = SwitchOn(caller);
            Counters.Add(on ? "shot.refused.busy" : "shot.refused.off");
            Log.Line(on ? $"shot refused: {caller} ({from}) while {s_caller} has the camera" : $"shot refused: {SwitchName(caller)} are off ({caller}, {from})");
            return false;
        }
        if (att.IsNone || vic.IsNone) { Counters.Add("shot.refused.no_character"); return false; }
        bool fresh = !Active || s_mode == (int)Mode.Return || s_caller != caller;
        if (fresh) { s_havePose = false; s_last = ShotKind.None; s_freshFromPlayer = true; }
        s_caller = caller;
        if (fresh) s_session = Driver.Host is { } h ? Access.Session(h) : null;   // a run started in this world (Apply stops it in another)
        (s_att, s_vic) = (att, vic);
        Watch = new Watching(att, vic);
        s_time = time;
        s_endAfter = endAfter;
        s_missing = 0;
        s_focus = false;
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
        if (Active && s_caller == KillCamCaller && s_mode != (int)Mode.Return && vic == s_vic) { Counters.Add("killcam.same_victim"); return; }
        // 0.6.1: the moment lasts at least the ease in, so the push-in is seen through (SlowMo's slow time has ended by then; that's fine)
        double ease = EaseSeconds();
        if (!Start(KillCamCaller, att, vic, Mode.Impact, ShotKind.ImpactClose, Math.Max(double.IsFinite(seconds) ? seconds : 0, ease), time: false, endAfter: true,
            "PZA's SlowMo kill cam: " + what)) return;
        s_focus = true;
        s_focusT = 0;
        s_pendingPick = false;
        s_playing = "KillFocus";
        Counters.Add("killcam.focus");
        Log.Line($"kill cam focus: your view glides onto the target and eases in {PushPercent():0}% over {ease:0.0} s ({what})");
    }

    static double EaseSeconds() => Math.Clamp(double.IsFinite(Driver.Settings.KillCamEase) ? Driver.Settings.KillCamEase : 1.5, 0.5, 3);
    static double PushPercent() => Math.Clamp(double.IsFinite(Driver.Settings.KillCamPush) ? Driver.Settings.KillCamPush : 25, 0, ShotCore.KillPushMax);

    /// <summary>The kill cam focus at <paramref name="t"/> real seconds: the look point arrives in the first 60% of the ease (the target is
    /// centred first), the push-in takes the whole ease; both smoothstepped, so it starts and lands without a jolt.</summary>
    public static (float Aim, float Push) FocusEase(double t, double ease)
    {
        if (!double.IsFinite(t) || t < 0) t = 0;
        if (!double.IsFinite(ease) || ease <= 0) ease = 1.5;
        return (ShotCore.Smooth((float)Math.Min(1, t / (0.6 * ease))), ShotCore.Smooth((float)Math.Min(1, t / ease)));
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
        s_blendLen = s_focus ? EaseSeconds()   // 0.6.1: the kill cam focus eases back as slowly as it went in
            : Math.Clamp(double.IsFinite(Driver.Settings.ShotReturnSeconds) ? Driver.Settings.ShotReturnSeconds : 0.6, 0, 3);
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
        s_focus = false;
        Watch = null;
        SlowLink.Release(why);
        DofOff(why);
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
        if (!SwitchOn(s_caller) && s_mode != (int)Mode.Return) BeginReturn(SwitchName(s_caller) + " switched off");
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
            s_vicDown = v.Body is { } vb && (vb.Dead || vb.Unconscious || vb.PlayingDead);
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
            case Mode.Between when s_inMode > BetweenMax: BeginReturn($"{BetweenMax:0} s between shots with no word"); break;
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
        else if (s_focus)
        {   // 0.6.1: the player's live view, its look point on the target, its zoom eased in; lifted only where Meitou would lift it anyway
            s_focusT += tdt;
            var (aim, push) = FocusEase(s_focusT, EaseSeconds());
            drawn = ShotCore.Lift(ShotCore.KillFocus(basePose, ShotCore.Subject(s_vicPos, s_vicDown), aim, push, PushPercent()), ground);
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
                var live = ShotCore.PlaceLifted(s_kind, s_side, scene, basePose.Fov, ground);
                string? no = live is { } lp ? ShotCore.Blocked(s_kind, lp, scene, ground, others, clear) : "the bolt landed";
                s_blockedFor = no is null ? 0 : s_blockedFor + step;
                if (live is null) Pick(scene, others, basePose.Fov, ground, clear, "the bolt landed", ShotKind.ImpactClose);
                else if (s_blockedFor > BlockedMax) Pick(scene, others, basePose.Fov, ground, clear, no!, s_kind == ShotKind.Frame ? ShotKind.Frame : ShotKind.None);
            }
            if (s_mode == (int)Mode.Return || s_mode == (int)Mode.None) return;
            var goal = ShotCore.PlaceLifted(s_kind, s_side, scene, basePose.Fov, ground) ?? s_drawn;
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
        Focus(cam, drawn, mode);
        CameraFocus.Point = drawn.Target;
        CameraFocus.Eye = cam.Eye;
        CameraFocus.Distance = Vector3.Distance(drawn.Target, CameraFocus.Eye);
        CameraFocus.FieldOfView = drawn.Fov;
        CameraFocus.Camera = "Shot " + s_playing;
        Counters.Add("shot.drawn." + s_kind);
    }

    /// <summary>0.6.0 shot focus: the focus on the shot's subject (the point it looks at), the f-number from the setting, softening in as the
    /// camera arrives and sharpening on the way back; depth of field switched on for the run and handed back when it ends.</summary>
    static void Focus(WorldCamera cam, Pose drawn, Mode mode)
    {
        LastAperture = float.NaN;
        if (Dof is not { } dof || !Driver.Settings.ShotFocus) { DofOff("shot focus off"); return; }
        float t;
        if (mode == Mode.Return) t = 1 - (float)(s_blendLen <= 0 ? 1 : Math.Min(1, s_blendT / s_blendLen));
        else if (s_focus) t = FocusEase(s_focusT, EaseSeconds()).Aim;   // 0.6.1: the background softens as the view settles on the target
        else if (s_freshFromPlayer && s_blendT < s_blendLen) t = (float)(s_blendT / s_blendLen);
        else { t = 1; s_freshFromPlayer = false; }
        float ap = ShotCore.Aperture(Driver.Settings.ShotAperture, s_kind, t);
        float dist = Vector3.Distance(cam.Eye, drawn.Target);
        if (!float.IsFinite(dist) || dist <= 0) return;
        cam.FocusDistance = dist;
        cam.Aperture = ap;
        LastAperture = ap;
        try { CinematicView?.Invoke(); } catch (Exception e) { Log.Exception("shot focus: cinematic view", e); }
        if (!s_dofOn)
        {
            try { s_dofPrev = dof.Get(); dof.Set(true); s_dofOn = true; }
            catch (Exception e) { Log.Exception("shot focus on", e); return; }
            Counters.Add("shot.focus.on");
            Log.Line($"shot focus: depth of field on (f/{Driver.Settings.ShotAperture:0.0#}); it was {(s_dofPrev is null ? "the game's own switch" : s_dofPrev.Value ? "on" : "off")}");
        }
    }

    static void DofOff(string why)
    {
        if (!s_dofOn) return;
        s_dofOn = false;
        try { Dof?.Set(s_dofPrev); } catch (Exception e) { Log.Exception("shot focus hand-back", e); }
        Counters.Add("shot.focus.off");
        Log.Line($"shot focus: depth of field handed back ({why})");
    }

    static ShotScene Scene(IReadOnlyList<ProjectileSnapshot> bolts, CharacterSnapshot? a)
    {
        Vector3? bolt = null;
        Vector3 dir = default;
        foreach (var b in bolts)
            if (b.Shooter == s_att && !b.Stuck) { bolt = b.Position; dir = b.Direction; }   // the newest in flight
        return new ShotScene(s_attPos, s_vicPos, bolt, dir, a?.RangedDrawn ?? false, s_vicDown);
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
        int keep = s_sidePair == (s_att, s_vic) ? s_keepSide : 0;
        var pick = ShotCore.Choose(w, scene, fov, ground, others, clear, s_rng, avoid: s_last, keepSide: keep);
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
        (s_keepSide, s_sidePair) = (pick.Side, (s_att, s_vic));
        if (pick.Kind != ShotKind.Frame && pick.Kind != ShotKind.Overview) s_last = pick.Kind;
        s_playing = pick.Kind.ToString();
        Counters.Add("shot.pick." + pick.Kind);
        if (pick.Tried > 1) Counters.Add("shot.fallback");
        // 0.6.1: where the camera really is, so a play log shows what the player saw
        var eye = pick.Pose.Eye;
        float g = ground?.Invoke(eye.X, eye.Z) ?? float.NaN;
        string place = $"eye {(float.IsFinite(g) ? (eye.Y - g).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " above the ground" : "over no ground")}, "
            + $"{pick.Pose.Pitch / CamCore.Deg:0} degrees down, {pick.Pose.Distance:0} away{(scene.VictimDown ? ", the target is down" : "")}";
        Log.Line($"shot angle: {pick.Kind} from the {(pick.Side > 0 ? "right" : "left")}; {place} ({why}{(pick.Tried > 1 ? $"; {pick.Tried - 1} blocked first: {pick.Why.TrimEnd(' ', ';')}" : "")})");
    }

    #endregion
}
