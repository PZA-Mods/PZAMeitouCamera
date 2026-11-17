// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The Settings > Mods group (MeitouClient docs/modding.md "Settings"): the plugin's PZACamera.ini keys, same names and defaults. The window's
// buttons and rows change them through the same group (Driver.SetSetting), so the page shows them and Meitou saves them. The page's labels
// are English for now (attributes need constants); the window and messages are translated (lang\meitou.<code>.txt).
using Meitou.Modding;

namespace PZA.Camera.Meitou;

public sealed class CamSettings
{
    [Setting("Camera on at load", Hint = "On: Diorama is on when a game loads. Off: only after you pick a camera (Shift+K opens the window).")]
    public bool StartOn { get; set; }

    [Setting("On-screen messages", Hint = "A message on the message line when the camera changes.")]
    public bool Popups { get; set; } = true;

    // ---- Diorama
    [Setting("Diorama tilt", Hint = "Degrees below the horizon the Diorama camera holds (the HD-2D tabletop look).", Min = 10, Max = 85, Step = 1)]
    public double Tilt { get; set; } = 50;

    [Setting("Diorama lens", Hint = "Diorama's own lens in degrees (a little wider than the game's 50 for strong tabletop perspective). 0 = the game's.", Min = 0, Max = 90, Step = 1)]
    public double DioramaFov { get; set; } = 55;

    [Setting("Tilt ease", Hint = "How fast the camera eases to a camera's tilt (per second; 0 = at once).", Min = 0, Max = 20, Step = 0.5)]
    public double TiltEase { get; set; } = 4;

    [Setting("Diorama Low angle", Hint = "Diorama Low's tilt. Shift+J (or the window's Save angle as Low) saves the angle you see now; again within 5 s puts the old one back.", Min = -10, Max = 85, Step = 1)]
    public double LowTilt { get; set; } = 25;

    // ---- Off-centre, 0.5x lens, Dutch angle (on top of any camera)
    [Setting("Off-centre", Hint = "The camera slides sideways so your character sits off-centre, on top of any camera.")]
    public bool OffCentre { get; set; }

    [Setting("Off-centre amount", Hint = "Percent of the camera's distance. + = the character right of centre, - = left.", Min = -40, Max = 40, Step = 1)]
    public double ShiftLeftPct { get; set; } = 15;

    [Setting("0.5x lens", Hint = "An ultra-wide lens like a phone's 0.5x on top of any normal-lens camera (the long-lens cameras keep their own).")]
    public bool WideLens { get; set; }

    [Setting("0.5x lens strength", Hint = "0.5 shows twice the width, 0.25 four times; straight lines stay straight.", Min = 0.25, Max = 1, Step = 0.05)]
    public double WideZoom { get; set; } = 0.5;

    [Setting("Dutch angle", Hint = "Rolls the camera a few degrees on top of any camera.")]
    public bool Dutch { get; set; }

    [Setting("Dutch angle degrees", Hint = "How far it rolls (negative = the other way).", Min = -30, Max = 30, Step = 1)]
    public double DutchDeg { get; set; } = 8;

    // ---- Isometric, Pixel Isometric
    [Setting("Isometric tilt", Hint = "Degrees below the horizon.", Min = 10, Max = 85, Step = 1)]
    public double IsoTilt { get; set; } = 35;

    [Setting("Isometric lens", Hint = "Degrees: smaller = flatter and more isometric; the camera sits farther back so the zoom frames the same. 10-20 is the sweet spot.", Min = 4, Max = 40, Step = 1)]
    public double IsoFov { get; set; } = 15;

    [Setting("Isometric settle step", Hint = "After you stop turning, the view settles on offset + n x step degrees (the four diagonals). 0 = free turning.", Min = 0, Max = 180, Step = 15)]
    public double IsoYawStep { get; set; } = 90;

    [Setting("Isometric settle offset", Hint = "See above.", Min = 0, Max = 90, Step = 15)]
    public double IsoYawOffset { get; set; } = 45;

    [Setting("Settle wait", Hint = "Seconds after you stop turning before Isometric and Side view settle.", Min = 0, Max = 5, Step = 0.1)]
    public double YawSettle { get; set; } = 0.4;

    [Setting("Pixel Isometric tilt", Hint = "30 degrees is the angle classic pixel-art games use (ground tiles come out 2:1).", Min = 10, Max = 85, Step = 1)]
    public double PixelIsoTilt { get; set; } = 30;

    [Setting("Pixel Isometric lens", Hint = "Degrees.", Min = 4, Max = 40, Step = 1)]
    public double PixelIsoFov { get; set; } = 15;

    // ---- Top-down
    [Setting("Top-down tilt", Hint = "Almost straight down (85 at most: straight down has no forward).", Min = 45, Max = 85, Step = 1)]
    public double TopTilt { get; set; } = 80;

    // ---- Side view, Profile
    [Setting("Side view tilt", Hint = "Low and side-on, like a 2.5D game.", Min = -10, Max = 45, Step = 1)]
    public double SideTilt { get; set; } = 8;

    [Setting("Side view lens", Hint = "Degrees (flat).", Min = 4, Max = 40, Step = 1)]
    public double SideFov { get; set; } = 12;

    [Setting("Side view settle step", Hint = "Side view settles on these steps after you stop turning (0 = free turning).", Min = 0, Max = 180, Step = 15)]
    public double SideYawStep { get; set; } = 90;

    [Setting("Profile side", Hint = "0 = stays on whichever side it is; 1 or -1 = always that side of the character.", Min = -1, Max = 1, Step = 1)]
    public double ProfileSide { get; set; }

    [Setting("Profile swing speed", Hint = "How fast Profile swings round as the character turns.", Min = 0.1, Max = 10, Step = 0.1)]
    public double ProfileEase { get; set; } = 1.5;

    // ---- Hip level
    [Setting("Hip level tilt", Hint = "Degrees below level (0 = level).", Min = -10, Max = 30, Step = 1)]
    public double HipTilt { get; set; } = 2;

    [Setting("Hip level angle", Hint = "Where round the character: 0 = the front, 30 = three-quarter, 90 = side, 180 = behind.", Min = -180, Max = 180, Step = 15)]
    public double HipAngle { get; set; }

    [Setting("Hip level swing speed", Hint = "How fast it swings round to the character's front.", Min = 0.1, Max = 10, Step = 0.1)]
    public double HipEase { get; set; } = 2;

    [Setting("Hip level zoom cap", Hint = "The farthest Hip level zooms out (units; about 10 a metre).", Min = 20, Max = 400, Step = 5)]
    public double HipZoomMax { get; set; } = 60;

    // ---- Turntable
    [Setting("Turntable tilt", Hint = "Degrees below the horizon.", Min = -10, Max = 85, Step = 1)]
    public double TurntableTilt { get; set; } = 12;

    [Setting("Turntable speed", Hint = "Degrees per second (negative = the other way).", Min = -90, Max = 90, Step = 1)]
    public double TurntableSpeed { get; set; } = 12;

    [Setting("Turntable pause", Hint = "Seconds it waits after you turn the camera yourself.", Min = 0, Max = 10, Step = 0.5)]
    public double TurntablePause { get; set; } = 2;

    // ---- Chase
    [Setting("Chase tilt", Hint = "Degrees below the horizon.", Min = -10, Max = 85, Step = 1)]
    public double ChaseTilt { get; set; } = 15;

    [Setting("Chase swing speed", Hint = "How fast it swings round behind the character.", Min = 0.1, Max = 10, Step = 0.1)]
    public double ChaseEase { get; set; } = 1.5;

    [Setting("Chase minimum speed", Hint = "The character must move at least this fast (units per second) before the camera turns.", Min = 0, Max = 100, Step = 1)]
    public double ChaseMinSpeed { get; set; } = 8;

    [Setting("Chase pause", Hint = "Seconds it waits after you turn the camera yourself.", Min = 0, Max = 10, Step = 0.5)]
    public double ChasePause { get; set; } = 2;

    [Setting("Chase from the front", Hint = "Swing round in front of the character instead (see the face; capes blow away from you).")]
    public bool ChaseFront { get; set; }

    // ---- Cinematic
    [Setting("Cinematic seconds", Hint = "Seconds per camera while Cinematic plays every other camera in turn.", Min = 2, Max = 60, Step = 1)]
    public double CinematicSeconds { get; set; } = 6;

    [Setting("Cinematic turntable speed", Hint = "Turntable's speed inside Cinematic (degrees per second).", Min = -90, Max = 90, Step = 1)]
    public double CinematicTurntableSpeed { get; set; } = 30;

    [Setting("Cinematic messages", Hint = "A message at every camera change while Cinematic plays (off: only when it starts).")]
    public bool CinematicPopups { get; set; }

    [Setting("Cinematic includes Hip level", Hint = "Left out by default: it is the close cape clip shot.")]
    public bool CinematicHipLevel { get; set; }

    // ---- 0.3.0: the plugin's YawStep (Diorama and Diorama Low settle on steps too) and SideAlign
    [Setting("Diorama settle step", Hint = "After you stop turning, Diorama and Diorama Low settle on n x this many degrees (45 = every diagonal and side). 0 = free turning.", Min = 0, Max = 180, Step = 15)]
    public double YawStep { get; set; }

    [Setting("Side view lines up", Hint = "When Side view is picked, its steps start from the followed character's side (off = world steps).")]
    public bool SideAlign { get; set; } = true;

    // ---- 0.3.0: Zoom stops (the plugin's v0.15.0 / v0.16.0)
    [Setting("Zoom stops", Hint = "The mouse wheel steps between three distances per camera (near, mid, far) instead of zooming freely. Set your own with the window's Near / Mid / Far.")]
    public bool ZoomStops { get; set; }
    [Setting("Free near stop", Hint = "The near zoom stop of Free (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsFree0 { get; set; }
    [Setting("Free mid stop", Hint = "The mid zoom stop of Free (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsFree1 { get; set; }
    [Setting("Free far stop", Hint = "The far zoom stop of Free (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsFree2 { get; set; }
    [Setting("Diorama near stop", Hint = "The near zoom stop of Diorama (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsDiorama0 { get; set; }
    [Setting("Diorama mid stop", Hint = "The mid zoom stop of Diorama (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsDiorama1 { get; set; }
    [Setting("Diorama far stop", Hint = "The far zoom stop of Diorama (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsDiorama2 { get; set; }
    [Setting("Diorama Low near stop", Hint = "The near zoom stop of Diorama Low (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsDioramaLow0 { get; set; }
    [Setting("Diorama Low mid stop", Hint = "The mid zoom stop of Diorama Low (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsDioramaLow1 { get; set; }
    [Setting("Diorama Low far stop", Hint = "The far zoom stop of Diorama Low (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsDioramaLow2 { get; set; }
    [Setting("Isometric near stop", Hint = "The near zoom stop of Isometric (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsIsometric0 { get; set; }
    [Setting("Isometric mid stop", Hint = "The mid zoom stop of Isometric (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsIsometric1 { get; set; }
    [Setting("Isometric far stop", Hint = "The far zoom stop of Isometric (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsIsometric2 { get; set; }
    [Setting("Pixel Isometric near stop", Hint = "The near zoom stop of Pixel Isometric (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsPixelIsometric0 { get; set; }
    [Setting("Pixel Isometric mid stop", Hint = "The mid zoom stop of Pixel Isometric (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsPixelIsometric1 { get; set; }
    [Setting("Pixel Isometric far stop", Hint = "The far zoom stop of Pixel Isometric (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsPixelIsometric2 { get; set; }
    [Setting("Top-down near stop", Hint = "The near zoom stop of Top-down (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsTopDown0 { get; set; }
    [Setting("Top-down mid stop", Hint = "The mid zoom stop of Top-down (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsTopDown1 { get; set; }
    [Setting("Top-down far stop", Hint = "The far zoom stop of Top-down (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsTopDown2 { get; set; }
    [Setting("Side view near stop", Hint = "The near zoom stop of Side view (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsSideView0 { get; set; }
    [Setting("Side view mid stop", Hint = "The mid zoom stop of Side view (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsSideView1 { get; set; }
    [Setting("Side view far stop", Hint = "The far zoom stop of Side view (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsSideView2 { get; set; }
    [Setting("Profile near stop", Hint = "The near zoom stop of Profile (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsProfile0 { get; set; }
    [Setting("Profile mid stop", Hint = "The mid zoom stop of Profile (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsProfile1 { get; set; }
    [Setting("Profile far stop", Hint = "The far zoom stop of Profile (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsProfile2 { get; set; }
    [Setting("Hip level near stop", Hint = "The near zoom stop of Hip level (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsHipLevel0 { get; set; }
    [Setting("Hip level mid stop", Hint = "The mid zoom stop of Hip level (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsHipLevel1 { get; set; }
    [Setting("Hip level far stop", Hint = "The far zoom stop of Hip level (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsHipLevel2 { get; set; }
    [Setting("Turntable near stop", Hint = "The near zoom stop of Turntable (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsTurntable0 { get; set; }
    [Setting("Turntable mid stop", Hint = "The mid zoom stop of Turntable (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsTurntable1 { get; set; }
    [Setting("Turntable far stop", Hint = "The far zoom stop of Turntable (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsTurntable2 { get; set; }
    [Setting("Chase near stop", Hint = "The near zoom stop of Chase (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsChase0 { get; set; }
    [Setting("Chase mid stop", Hint = "The mid zoom stop of Chase (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsChase1 { get; set; }
    [Setting("Chase far stop", Hint = "The far zoom stop of Chase (boom distance). 0 = the researched default.", Min = 0, Max = 2000, Step = 1, VisibleWhen = nameof(ZoomStops))]
    public double ZsChase2 { get; set; }
    // ---- 0.3.0: Keep in view (the plugin's v0.17.0)
    [Setting("Keep in view", Hint = "When the ground hides your character, the view tilts up, then the camera comes in; it eases back once the view is clear.")]
    public bool KeepInView { get; set; }

    [Setting("Keep in view: most extra tilt", Hint = "Degrees Keep in view may tilt the view up before the camera comes in.", Min = 5, Max = 60, Step = 1, VisibleWhen = nameof(KeepInView))]
    public double KeepInViewMaxTilt { get; set; } = 35;

    // ---- the window (PZA's Meitou UI)
    [Setting("Show the window", Hint = "The PZA's Camera window. Off: Shift+K opens it again.")]
    public bool ShowWindow { get; set; } = true;

    [Setting("Tiny window", Hint = "The window is minimized to its tiny button (the window's -, a double click on its title and the tiny button's + switch this too).", VisibleWhen = nameof(ShowWindow))]
    public bool Tiny { get; set; } = true;

    [Setting("Window place across", Hint = "Where you left the window (a fraction of the screen from its first place). Drag the window by its title to move it.", Min = -1, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow))]
    public double WindowPlaceX { get; set; }

    [Setting("Window place down", Hint = "As above, down.", Min = -1, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow))]
    public double WindowPlaceY { get; set; } = CamWindow.MaxPlaceY - 0.005;   // 0.2.1: opens above the game's bottom HUD (CamWindow.HudTop)

    [Setting("Tiny button across", Hint = "Where the tiny button sits, from the left edge (1 = right edge). Drag the tiny button to move it.", Min = 0, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow))]
    public double TinyPlaceX { get; set; } = 1;

    [Setting("Tiny button down", Hint = "Where the tiny button sits, from the top (0 = top).", Min = 0, Max = 1, Step = 0.001, VisibleWhen = nameof(ShowWindow))]
    public double TinyPlaceY { get; set; } = 0.14;   // below PZA's SlowMo's (0.06) and WASD Control's tiny buttons, top right

    [Setting("Tiny button opacity", Hint = "0.35 (faint) to 1 (solid). The mouse wheel over the tiny button changes it.", Min = 0.35, Max = 1, Step = 0.05, VisibleWhen = nameof(ShowWindow))]
    public double TinyOpacity { get; set; } = 1;

    [Setting("Language", Hint = "auto = the game's language. Needs a restart of the game.", Options = ["auto", "en", "ru", "zh", "ja"])]
    public string Language { get; set; } = "auto";
}
