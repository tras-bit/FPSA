# KORDON 1.0.5

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

The screens are readable now. The settings screen was the worst of them, but the
same mistake - elements positioned by hand from mixed anchors - was on the connect,
kit and tutorial screens too.

* **Settings rebuilt on a stacked layout.** Captions no longer sit on top of slider
  tracks, the quality readout no longer collides with the subtitles button, and the
  last control rows are no longer hidden behind the footer.
* **Controls look like controls.** Buttons have a raised surface with hover and
  pressed tints; sliders have a track, a fill and a handle. Before, every control was
  the same colour as the panel and the slider fill was resized to zero width, so the
  sliders showed no value at all.
* **All 16 rebindable rows fit**, each a button with the action left and the key
  right, instead of one label padded with spaces.
* **Connect screen** got an aligned server block (address field, port, EU-first
  region stepper) and buttons that report what actually happened.
* **Kit and tutorial screens** rebuilt the same way; the 14 step tutorial list is
  now two columns and no longer runs off the panel.

`docs/art/ui-settings-layout.png` in this repository shows the new settings layout,
drawn from the same numbers the game uses.

## This release contains the game source, not an `.exe`

Being straight about it: the project was authored in a Linux container without a
Unity Editor, without a Windows machine and without a Unity licence, so no player
binary could be produced here. What is here is the complete project - open it in
Unity 6 and the game runs; the same menu builds the `.exe`.

## Look at it in three steps

1. Unity Hub -> **Add project from disk** -> this folder -> open with **6000.0.32f1**.
2. Menu **Kordon -> Open boot scene (press Play)**.
3. Press **Play**.

Windows build: **Kordon -> Build -> Windows 64-bit player** -> `Builds/Windows/Kordon.exe`.

## This release contains the game source, not an `.exe`

Being straight about it: the project was authored in a Linux container without a
Unity Editor, without a Windows machine and without a Unity licence, so no player
binary could be produced here. What is here is the complete project - open it in
Unity 6 and the game runs; the same menu builds the `.exe`.

## Look at it in three steps

1. Unity Hub -> **Add project from disk** -> this folder -> open with **6000.0.32f1**.
   First import resolves Netcode for GameObjects 2.2.0 and imports the art (a few
   minutes, once).
2. Menu **Kordon -> Open boot scene (press Play)**.
3. Press **Play**. Content, UI and the world are built from code at startup, so
   there is nothing to wire in the scene view.

To get the Windows build: **Kordon -> Build -> Windows 64-bit player** ->
`Builds/Windows/Kordon.exe`.
Linux dedicated server: **Kordon -> Build -> Linux 64-bit player**.

## Playing a match

* **Solo practice** - main menu, AI opponents, clearly labelled as practice.
* **Free-for-All** - one machine hosts (PLAY - FREE-FOR-ALL -> HOST), everyone else
  joins with the host address and port (27015 by default).
* **Co-op** - CO-OP OPERATION (2-4) -> HOST, then 1-3 operators join.
* **Dedicated server** - `Kordon.exe -kordon-server -serverAddress 0.0.0.0
  -serverPort 27015 -map ffa_zavod7`.

## What is in the box

| | |
|---|---|
| Code | 105 scripts, ~22 300 lines: engine-free rules core, game layer, netcode layer |
| Rules tested | 225 unit tests, all passing in CI |
| Maps | Zavod-7 (88 x 68 m, 3 routes) and Silent Kiln (70 x 60 m co-op) |
| Art | 60 modular kit models, 10 weapons/grenades (22 484 tris), 51 textures, 19 materials, 2 hand written shaders |
| Audio | 79 synthesised WAV files |
| Art sources | `ArtSource/Blender` - `.blend` files, `.fbx` exports in the project |
| Docs | README, concept, rules, magazine system, networking, art, verification |

## Verification actually run for this tag

| Check | Result |
|---|---|
| Core rule tests (`dotnet test`) | 243 / 243 passed |
| Syntax + structure (Roslyn, C# 9) | 105 scripts, 0 errors |
| Unity-side type check against API stubs | 0 errors, 0 warnings |
| Content / policy audit (packages, shaders, textures, FBX materials) | PASS |
| Unity Editor build | **not run** - no editor in the build environment |
| Two-machine network test | **not run** - same reason |

What could not be executed here, and why, is in `docs/06_VERIFICATION.md`.

## Known limitations

* No prebuilt binary (see above).
* The package layer and the art contracts have now been checked, but the game has
  still never been played. Balance numbers were tuned on paper and in unit tests.
* A first Play can surface editor-only fixes (an import setting, a missing `.meta`)
  that no static check can see. Two such rounds are already fixed in 1.0.1/1.0.2.
