# PZA's Camera for Meitou (beta 0.3.1)

Thirteen ready-made camera looks for Meitou, one click each: Diorama, Isometric, Top-down, Side view, Hip level, Turntable, Chase, Cinematic
and more, with Off-centre, Dutch angle and a 0.5x lens on top. Click the active camera again and you are back to Meitou's own. A port of
PZA's Camera for RE_Kenshi.

**Download:** the mod zip and its source zip are on the [Releases page](https://github.com/PZA-Mods/PZAMeitouCamera/releases). Building from source: [BUILD.md](BUILD.md).

## Hotkeys

| Key | What it does |
|---|---|
| **Shift+K** | Opens or hides the PZA's Camera window |
| **Shift+J** | Saves the tilt you see as Diorama Low's angle; press again within 5 s to put the old one back |

Plain K (Put down) and J (Rescue) stay Meitou's. Nothing fires while you type in a text box. F10 also has "PZA's Camera: next camera" and
"PZA's Camera: window".

## The window
Starts as a tiny **Camera: Off** button (top right): click = camera off / back to the last one, **+** or a double click = the window, drag
it, mouse wheel for its opacity. Tabs:
- **CAMERAS**: Off, Free, Diorama, Diorama Low, Isometric, Pixel Isometric, Top-down, Side view, Profile, Hip level, Turntable, Chase,
  Cinematic; Off-centre, Dutch angle, 0.5x lens; **Tilt-Shift** Off / Miniature / HD-2D / Diorama (needs PZA's Looks for Meitou).
- **VIEW**: zoom stops (near / mid / far), Keep in view, the 45-degree settle of Diorama and Isometric, the Diorama look.
- **SETTINGS**: every value with - / + / Reset. **HELP**.
Everything is also in **Settings > Mods > PZA's Camera** and is kept after a restart.

## Requires
**Meitou 0.20.1 and newer.** Checked on Meitou 0.20.1, 0.21.0 and 0.22.1; there is no upper version cap any more, so a newer Meitou no longer refuses it. The Tilt-Shift buttons need **PZA's Looks for Meitou** (also
in this beta); without it they are greyed out.

## Install
- Unpack `PZACamera.Meitou-0.3.1-beta.zip` into Kenshi's folder (`Kenshi\mods\PZACamera\`), then tick **PZACamera**
  in Meitou's mod list. If you also use PZA's Camera for RE_Kenshi, it is the same folder on purpose: each game loads only its own file.
- No items or world data; load order doesn't matter. Safe to remove at any time.

## This is a beta
**This build has passed its offline checks on Meitou 0.20.1, 0.21.0 and 0.22.1 (every camera, the window clicked through, the text fitting in four
languages), but it has not been played in game yet.** Your first impressions are exactly what we need.

Known limits:
- It steps aside while Meitou's own cinema, free camera or photo mode holds the camera, and while PZA's WASD Control has a view on.
- Not ported yet from the RE_Kenshi version: Fisheye, the Cinematic zoom lock and its caption, the Diorama look's toy colours.
- Texts in English, Russian, Chinese and Japanese (machine translations, not yet checked by native speakers).

## Planned
Ideas, no dates:
- The parts of the RE_Kenshi version not ported yet: **Fisheye**, the **Cinematic zoom lock** with its caption, the Diorama look's toy colours.
- Working with Meitou's developers so the camera can hook in cleanly, and perhaps some of these views become built-in camera options.

## Reporting a bug
Which camera, what you saw (a screenshot helps), how to make it happen again, your Meitou version and mod list, and Meitou's `log.txt` (in
your Meitou folder) copied right after that session. Our lines start with `pza.camera`. Post it in its thread on the Meitou Discord or in the Workshop page's Bug reports discussion.

## Licence and source
GNU GPL version 3 or later; the source is attached to the release (`-source.zip`). Made by **PZA**. Unofficial fan-made mod; not affiliated
with Lo-Fi Games.
