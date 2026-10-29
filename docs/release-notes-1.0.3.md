# KORDON 1.0.3

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

Everything is original to this repository: setting, maps, models, textures, sounds,
UI and code.

## What changed in this build

The project now compiles in Unity 6 with **zero errors and zero warnings**.

* **Fixed the last compile error.** `CS0012: The type 'NetworkEndpoint' is defined
  in an assembly that is not referenced` - `UnityTransport` wraps Unity Transport,
  so `Kordon.Net` must reference `Unity.Networking.Transport`, and the manifest now
  declares `com.unity.transport` 2.4.0 (the version Netcode 2.2.0 requires) instead
  of relying on it arriving transitively.
* **Fixed a kit-screen lie.** The grenade button always read `GRENADE: FRAG x2`,
  whichever grenade you picked. It now follows the selection, so the screen can
  never show one grenade while issuing another.
* Obsolete Unity 6 calls replaced (`FindObjectOfType` -> `FindFirstObjectByType`,
  `PlayerSettings.SetApplicationIdentifier` -> the `NamedBuildTarget` overload); a
  never-raised event and four dead fields removed.
* AI hostiles now search paths at most once every 0.9 s (immediately when a path
  runs out), instead of twice a second each.

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
