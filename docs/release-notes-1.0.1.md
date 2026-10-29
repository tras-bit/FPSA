# KORDON 1.0.1

Fix release. 1.0.0 was green in CI but would not compile when opened in a real
Unity 6000.0.32f1 editor - the first time the package layer was resolved by an
actual editor instead of by static checks. This build fixes that.

## What changed

* **`Packages/manifest.json` now enables the built-in Physics 2D package.** Netcode
  for GameObjects 2.2.0 needs it (`NetworkRigidbodyBase` uses `Rigidbody2D` and
  `FixedJoint2D`); without it a fresh clone reported
  `CS1069: The type name 'Rigidbody2D' could not be found ... forwarded to assembly
  'UnityEngine.Physics2DModule'` and did not compile at all.
* A CI rule now fails the build if a required built-in package is missing from the
  manifest, or if the pinned package versions drift, so this cannot come back
  silently. Verified by removing the module and watching the check fail.
* README gained "If the first open reports errors": the Physics 2D error, and the
  stale `Library/ScriptAssemblies` a failed package resolve leaves behind
  (`Unity.Collections.CodeGen ... Unable to resolve reference 'Unity.Collections'`).

## Opening it

1. Quit Unity if it is open on this project.
2. Delete `Library/ScriptAssemblies` (or the whole `Library` folder) - that clears
   the leftover `Unity.Collections.CodeGen` assembly error.
3. Reopen in **6000.0.32f1**, let the packages resolve, menu
   **Kordon -> Open boot scene (press Play)**, then **Play**.

---

## This release contains the game source, not an `.exe`

Being straight about it: the project was built in a Linux container without a Unity
Editor, without a Windows machine and without a Unity licence, so no player binary
could be produced here. What is here is the complete project - open it in Unity 6
and the game runs; the same menu builds the `.exe`.

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

* **Solo practice** - main menu -> practice: AI opponents, clearly labelled as
  practice, never presented as online players.
* **Free-for-All** - one machine hosts (main menu -> PLAY - FREE-FOR-ALL -> HOST),
  everyone else joins with the host address and port (27015 by default).
* **Co-op** - main menu -> CO-OP OPERATION (2-4) -> HOST, then 1-3 operators join.
* **Dedicated server** - `Kordon.exe -kordon-server -serverAddress 0.0.0.0
  -serverPort 27015 -map ffa_zavod7`; clients join it exactly like a hosted game.

Controls, HUD rules and the magazine system are in the README.

## What is in the box

| | |
|---|---|
| Code | 101 scripts, 22 174 lines: engine-free rules core, game layer, netcode layer |
| Rules tested | 219 unit tests, all passing in CI |
| Maps | Zavod-7 (88 x 68 m, 3 routes) and Silent Kiln (70 x 60 m co-op) |
| Art | 60 modular kit models, 10 weapons/grenades (22 484 tris), 51 textures, 19 materials |
| Audio | 79 synthesised WAV files |
| Shaders | 2 hand written shaders (bodycam post pass, grenade FX), loaded from `Resources` so they cannot be stripped from a player build |
| Art sources | `ArtSource/Blender` - `.blend` files, `.fbx` exports in the project |
| Docs | README, concept, rules, magazine system, networking, art, verification |

## Verification actually run for this tag

| Check | Result |
|---|---|
| Core rule tests (`dotnet test`) | 219 / 219 passed |
| Syntax + structure (Roslyn, C# 9) | 101 scripts, 0 errors |
| Unity-side type check against API stubs | 0 errors |
| Content / policy audit | PASS (now also checks the package manifest) |
| Unity Editor build | **not run** - no editor in the build environment |
| Two-machine network test | **not run** - same reason |

The honest summary of what could not be executed here, and why, is in
`docs/06_VERIFICATION.md`. Nothing in this repository is a stub: no TODO, no
placeholder feature and no dead menu item was left behind.

## Known limitations

* No prebuilt binary (see above).
* The game has never been launched in a Unity Editor - the first Play may surface
  small editor-only fixes (import settings, a missing `.meta`) that are invisible to
  static checks.
* Balance numbers (recoil, spread, AI reaction) were tuned on paper and in unit
  tests, not by playing.
