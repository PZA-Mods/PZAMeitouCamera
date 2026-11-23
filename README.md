# PZA's Director's Kit for Meitou (beta 0.6.1)

**PZA's Director's Kit** (formerly PZA's Camera): thirteen ready-made camera looks for Meitou, one click each: Diorama, Isometric, Top-down, Side view, Hip level, Turntable, Chase, Cinematic
and more, with Off-centre, Dutch angle, a 0.5x lens and Fisheye on top. Click the active camera again and you are back to Meitou's own. A port of
PZA's Camera for RE_Kenshi.

**Download:** the mod zip and its source zip are on the [Releases page](https://github.com/PZA-Mods/PZAMeitouCamera/releases). Building from source: [BUILD.md](BUILD.md).

## Hotkeys

| Key | What it does |
|---|---|
| **Shift+K** | Opens or hides the PZA's Camera window |
| **Shift+J** | Saves the tilt you see as Diorama Low's angle; press again within 5 s to put the old one back |

Plain K (Put down) and J (Rescue) stay Meitou's. Nothing fires while you type in a text box. F10 also has "PZA's Camera: next camera",
"PZA's Camera: window" and "PZA's Camera: preview a called shot".

## The window
Starts as a tiny **Camera: Off** button (top right): click = camera off / back to the last one, **+** or a double click = the window, drag
it, mouse wheel for its opacity. Tabs:
- **CAMERAS**: Off, Free, Diorama, Diorama Low, Isometric, Pixel Isometric, Top-down, Side view, Profile, Hip level, Turntable, Chase,
  Cinematic; Off-centre, Dutch angle, 0.5x lens, **Fisheye**; **Tilt-Shift** Off / Miniature / HD-2D / Diorama (Fisheye and Tilt-Shift
  need PZA's Post-FX for Meitou).
- **VIEW**: zoom stops (near / mid / far), Keep in view, the 45-degree settle of Diorama and Isometric, the Diorama look, Fisheye, the
  Cinematic zoom lock.
- **SETTINGS**: every value with - / + / Reset.
- **SHOTS** (0.5.0): **Kill cam shots** (off by default): when PZA's SlowMo's kill cam slows time for a kill, knockout or severed limb,
  your own camera (its angle and zoom) glides onto the target and eases in gently, then eases back (0.6.1: **Kill cam push-in**,
  0-80%, default 25%, and **Kill cam ease**, 0.5-3 s, default 1.5 s; one per knockout, a kill after it once, at least 3 s apart). **Called-shot shots**: a called-shot mod can ask for
  cinematic angles (a two-shot while you pick, then over the shoulder, side-on and low, close on the target or behind a bolt, and back
  to your camera); a blocked angle is skipped, so the camera never ends up in a wall, a rock or a character. **Preview a shot** tries
  it on your selected character; - / + / Reset for the shots' speeds and lengths. **Focus pull** (0.6.0): the shots focus on their
  subject and the rest goes soft, with Meitou's own depth of field. The tab shows only what your mods can use: none of it without
  PZA's SlowMo or a called-shot mod.
- **HELP**.
While the game is paused, Cinematic and Turntable hold still and a shot holds its angle (0.5.0).
**Photo Mode** (0.6.0): press P during a shot and Meitou's Photo Mode opens from that very view; leaving Photo Mode gives you your own
lens back (before, it could leave you with a camera's lens, such as Isometric's).
Everything is also in **Settings > Mods > PZA's Camera** and is kept after a restart.

## Requires
**Meitou 0.20.1 and newer.** Checked on Meitou 0.20.1, 0.21.0 and 0.22.1; there is no upper version cap any more, so a newer Meitou no longer refuses it. The Fisheye and Tilt-Shift buttons need **PZA's Post-FX for Meitou** (formerly
PZA's Looks; 0.3.0 or newer for Fisheye); the shots' slow time and the kill cam shots need **PZA's SlowMo for Meitou**. Without them those
parts are greyed out or leave time alone.

## Install
- **Workshop:** subscribe to PZA's Director's Kit, then tick **PZADirectorsKit** in Meitou's mod list. The Workshop item also carries the
  RE_Kenshi version for the original game; each game loads only its own file.
- **By hand (GitHub):** unpack `PZACamera.Meitou-0.6.1-beta.zip` into Kenshi's folder (`Kenshi\mods\PZADirectorsKit\`), then tick
  **PZADirectorsKit** in Meitou's mod list. Coming from 0.3.x: remove the old copy first (the files
  `PZACamera.Meitou.dll` and `meitou.json` in `mods\PZACamera\`) so the camera is not loaded twice.
- No items or world data; load order doesn't matter. Safe to remove at any time.

## This is a beta
**This build has passed its offline checks on Meitou 0.20.1, 0.21.0 and 0.22.1 (every camera, the window clicked through, the text fitting in four
languages), but it has not been played in game yet.** Your first impressions are exactly what we need.

Known limits:
- It steps aside while Meitou's own cinema, free camera or photo mode holds the camera, and while PZA's WASD Control has a view on.
- Not ported yet from the RE_Kenshi version: the Cinematic caption and the Diorama look's toy colours.
- Texts in English, Russian, Chinese and Japanese (machine translations, not yet checked by native speakers).

## Planned
Ideas, no dates:
- The parts of the RE_Kenshi version not ported yet: the Cinematic caption and the Diorama look's toy colours.
- Working with Meitou's developers so the camera can hook in cleanly, and perhaps some of these views become built-in camera options.

## Reporting a bug
Which camera, what you saw (a screenshot helps), how to make it happen again, your Meitou version and mod list, and Meitou's `log.txt` (in
your Meitou folder) copied right after that session. Our lines start with `pza.camera`. Post it in its thread on the Meitou Discord or in the Workshop page's Bug reports discussion.

## Licence and source
GNU GPL version 3 or later; the source is attached to the release (`-source.zip`). Made by **PZA**. Unofficial fan-made mod; not affiliated
with Lo-Fi Games.
