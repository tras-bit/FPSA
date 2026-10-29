# KORDON 1.0.7

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

The HUD is redesigned. 1.0.6 moved the magazines into the lower right but drew them
as flat bars inside a cramped black box; this round rebuilds the whole block.

* **Loadout card.** A rounded card with a header rule and one row per slot: a key
  chip, the weapon name, and what its pouch holds (`4 MAG`, `x2` grenades). The
  active slot is the only row in the accent colour, the only one with a lit backdrop
  and the only one with a left pip - "which weapon is in hand" is one glance instead
  of a remembered keypress.
* **The grenade row is the fuze readout.** While a grenade is cooked the row shows
  the burning fuze in seconds, warning colour first and danger colour at the end.
* **Magazines that look like magazines.** The icon is no longer a 44 x 15 rectangle.
  It is a curved box magazine silhouette - tapered body, floor plate, feed lips,
  three ribs, and a bright round on top when the magazine is chambered - rasterised
  once into a cached texture by `HudIcons`, so the project ships no extra image files
  and the match pays nothing after the first frame. Primary magazines are 22 x 36 px,
  the sidearm's are 15 x 24 px, so the two pouches read as different things.
* **Captions and a proper inspect line.** PRIMARY MAGS and SIDEARM MAGS label their
  rows; the inspect readout (`14 / 30`) sits above them and is still the only place
  an exact round count ever appears. White full, yellow partial, red empty is
  unchanged.
* **Subtitles moved above the block** instead of sharing a band with the card, where
  a long subtitle would have been drawn across it.
* **The block is measured, not eyeballed.** `Kordon.Core.Ui.HudLayout` owns every
  number and 15 unit tests check six screen sizes from 1280x720 to 3440x1440: no
  overlapping rows, no collision with the centred hint, subtitles clear, six
  magazines plus captions fit, icons taller than wide.

`docs/art/ui-hud-layout.png` in this repository shows the block. It is a layout
reference drawn from the same numbers the game uses, not an in-game screenshot -
there is no Unity editor in the build environment, so the HUD cannot be captured
there. The image says so on itself.

## This release contains the game source, not an `.exe`

Being straight about it: the project was authored in a Linux container without a
Unity Editor, without a Windows machine and without a Unity licence, so no player
binary could be produced here. What is here is the complete project - open it in
Unity 6 and the game runs; the same menu builds the `.exe`.

## Look at it in three steps

1. Unity Hub -> **Add project from disk** -> this folder -> open with **6000.0.32f1**.
2. Menu **Kordon -> Open boot scene (press Play)**.
3. Press Play. Boot menu -> FREE-FOR-ALL vs bots for practice, or HOST a real match.

* **Co-op** - CO-OPERATION (2-4) -> HOST, then 1-3 operators join.
* **Dedicated server** - `Kordon.exe -kordon-server -serverAddress 0.0.0.0
  -serverPort 27015 -map ffa_zavod7`.

## What is in the box

| | |
|---|---|
| Code | 107 scripts, engine-free rules core, game layer, netcode layer |
| Rules tested | 268 unit tests, all passing in CI |
| Maps | Zavod-7 (88 x 68 m, 3 routes) and Silent Kiln (70 x 60 m co-op) |
| Art | 60 modular kit models, 7 weapons + 3 grenades (68 636 tris), 51 textures, 19 materials, 2 hand written shaders |
| Audio | 79 synthesised WAV files |
| Art sources | `ArtSource/Blender` - `.blend` files, `.fbx` exports in the project |
| Docs | README, concept, rules, magazine system, networking, art, verification |

## Verification actually run for this tag

| Check | Result |
|---|---|
| Core rule tests (`dotnet test`) | 268 / 268 passed |
| Syntax + structure (Roslyn, C# 9) | 107 scripts, 0 errors |
| Unity-side type check against API stubs | 0 errors, 0 warnings |
| Content / policy audit (packages, shaders, textures, FBX materials) | PASS |
| Map audit in Blender (floor coverage, spawns, shell contract) | 2546 samples, 0 holes, 0 findings |
| HUD geometry unit tests | 15 tests, 6 screen sizes |
| Unity Editor build | **not run** - no editor in the build environment |
| Two-machine network test | **not run** - same reason |

What could not be executed here, and why, is in `docs/06_VERIFICATION.md`.

## Known limitations

* No prebuilt binary (see above).
* The HUD layout is measured and unit tested, but no screenshot of it in game
  exists: font rendering and sprite filtering need the Unity editor.
* The game has still never been played: no first person play test, no balance pass
  by feel. 1.0.6 fixed the two bugs that made the map unplayable on the screenshot
  (missing ground collider, no world bounds) and audited the map geometry in
  Blender, but that is not the same thing as playing it.
