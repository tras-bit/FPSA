# KORDON 1.0.2

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

Everything is original to this repository: setting, maps, models, textures, sounds,
UI and code.

## What changed in this build

1.0.1 fixed the package layer. This build fixes two art-pipeline defects that only
appear when you actually look at the running game:

* **Models now use the authored materials.** The Blender kit exports its materials
  as `KRD_steel_rust`; the game looked up `M_KRD_steel_rust`, found nothing, and
  silently kept the untextured import. Everything - the whole map, every weapon -
  would have rendered flat grey with no error in the console. The name is now
  normalised in one tested place, so `KRD_steel_rust` resolves to `M_steel_rust`.
* **Normal maps are imported as normal maps.** All 17 had no `textureType` in their
  `.meta`, so Unity treated them as plain colour and the surface lighting was
  wrong.
* Both are now content checks in CI, each verified by breaking it on purpose.

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
| Code | 102 scripts, ~22 300 lines: engine-free rules core, game layer, netcode layer |
| Rules tested | 225 unit tests, all passing in CI |
| Maps | Zavod-7 (88 x 68 m, 3 routes) and Silent Kiln (70 x 60 m co-op) |
| Art | 60 modular kit models, 10 weapons/grenades (22 484 tris), 51 textures, 19 materials, 2 hand written shaders |
| Audio | 79 synthesised WAV files |
| Art sources | `ArtSource/Blender` - `.blend` files, `.fbx` exports in the project |
| Docs | README, concept, rules, magazine system, networking, art, verification |

## Verification actually run for this tag

| Check | Result |
|---|---|
| Core rule tests (`dotnet test`) | 225 / 225 passed |
| Syntax + structure (Roslyn, C# 9) | 102 scripts, 0 errors |
| Unity-side type check against API stubs | 0 errors |
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
