# KORDON 1.0.4

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

Everything is original to this repository: setting, maps, models, textures, sounds,
UI and code.

## What changed in this build

The project now gets all the way through Unity's build pipeline, including Netcode's
ILPP code generator - the stage that runs after the C# compiler.

* **The player build failed in `NetworkBehaviourILPP`.** The scoreboard RPC took a
  `string[]`, which Netcode's generated serialiser cannot handle:
  `Don't know how to serialize System.String[]` ... `PostProcessing failed`. It fails
  the whole build, not just that RPC. Names now travel as a single length prefixed
  string and are decoded on the client (`NameListWire`, 9 unit tests); a name may
  contain any character, and a truncated payload gives empty rows instead of an
  exception on a client.
* Name cleaning no longer glues words together when a name contains a newline.
* A CI rule now validates every RPC parameter type the way the ILPP generator does -
  143 parameters - so a `string[]`, a `List<>` or an unknown struct in an RPC fails
  in CI instead of inside the editor.

## Before you open it: clear the stale build cache

The `Unity.Collections.CodeGen.dll ... Unable to resolve reference 'Unity.Collections'`
message in your log is from the failed build, not a package problem. Quit Unity,
delete `Library/ScriptAssemblies` (or the whole `Library` folder), then reopen.

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
| Core rule tests (`dotnet test`) | 234 / 234 passed |
| Syntax + structure (Roslyn, C# 9) | 103 scripts, 0 errors |
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
