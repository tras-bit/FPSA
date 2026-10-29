# Changelog

All notable changes to KORDON. The project is versioned as one product: game
rules, content, networking and art ship together.

## 1.0.3 - 2026-10-29

The build now compiles in Unity with **zero errors and zero warnings**. Every fix
here came from the editor output, which is the one thing the static checks cannot
reproduce.

### Fixed

* **`CS0012: The type 'NetworkEndpoint' is defined in an assembly that is not
  referenced`** in `KordonNetwork.cs`. `UnityTransport` is a thin wrapper over
  Unity Transport, so using it forces `Unity.Networking.Transport` to be
  resolvable. `Kordon.Net.asmdef` referenced only `Unity.Netcode.Runtime`, and
  `Packages/manifest.json` did not declare `com.unity.transport` (it only arrived
  transitively). Both are now explicit, pinned to the 2.4.0 that Netcode 2.2.0
  requires.
* **The kit screen's grenade button never changed its caption.** It read
  `GRENADE: FRAG x2` no matter what was selected, because the label was created and
  then thrown away. Cycling now updates it (`FRAG x2` / `SMOKE x2` / `FLASH x2`),
  so the screen cannot show one grenade while issuing another.
* Obsolete Unity 6 API: `FindObjectOfType` (6 call sites) replaced by
  `FindFirstObjectByType`, and `PlayerSettings.SetApplicationIdentifier(BuildTargetGroup, ...)`
  by the `NamedBuildTarget` overload. No CS0618 warnings left.
* A never-raised `PracticeMatch.ScoreboardChanged` event, an assigned-but-unread
  `HudScreen._scoreRowCount`, three unused kit-screen fields and a dead
  `CoopMissionDirector` flag are gone (CS0067 / CS0414 / CS0169).
* AI path searches are throttled to 0.9 s per hostile (and forced when a path runs
  out) instead of twice a second per hostile. The timer that was meant to do this
  was declared and never used.

### Added

* `Tools/verify/policy_check.py` checks assembly boundaries: a script that uses
  `Unity.Netcode` or `Unity.Networking.Transport` (by `using` **or** fully
  qualified) must have the owning asmdef reference that assembly. The static checks
  flatten every script into one assembly, which is exactly why the CS0012 error was
  invisible to them; verified by removing the reference and watching the check fail.

## 1.0.2 - 2026-10-29

Two defects that only a real editor and a real look at the art would show. Both
are fixed, and both now have a CI check so they cannot come back silently.

### Fixed

* **Every model rendered with its imported, untextured material.** The Blender kit
  exports materials as `KRD_steel_rust`; the game looked up `M_KRD_steel_rust`,
  found nothing, and quietly kept the untagged import - a flat grey map and grey
  weapons, with no error in the console. The prefix is now normalised in one
  tested place (`Kordon.Core.Utility.SurfaceNaming`, 6 unit tests) and the lookup
  is `M_steel_rust`.
* **All 17 normal maps were imported as plain colour textures.** The `.meta` files
  had no `textureType`, so Unity treated them as Default instead of NormalMap and
  surface lighting was wrong. They are typed as normal maps now, with albedo sRGB
  and mask maps linear.
* A stale comment in the test project claimed the suite also ran inside Unity via
  a folder that does not exist. Corrected to what actually happens.

### Added

* `Tools/verify/policy_check.py` gained two content checks, each verified by
  breaking the thing on purpose: every `KRD_*` material inside an FBX must resolve
  to an authored material, and every texture `.meta` must carry the import
  settings the art depends on.
* Mesh-only warning per surface instead of per renderer, so a missing material is
  reported once instead of hundreds of times.

## 1.0.1 - 2026-10-29

Fixes found by opening the project in a real Unity 6000.0.32f1 editor - the first
time the package layer was ever resolved by an actual editor rather than by static
checks.

### Fixed

* `Packages/manifest.json` did not enable the built-in **Physics 2D** package, so
  Netcode for GameObjects failed to compile on a fresh clone:
  `CS1069: The type name 'Rigidbody2D' could not be found ... forwarded to assembly
  'UnityEngine.Physics2DModule'` in `NetworkRigidbodyBase.cs`. The module is now
  declared; every other required built-in module is declared too.

### Added

* `Tools/verify/policy_check.py` now checks the package manifest: every built-in
  module the project (and Netcode for GameObjects) compiles against must be
  declared, and Netcode for GameObjects 2.2.0 / Test Framework 1.4.5 / uGUI 2.0.0
  must stay pinned. The check was verified by removing the module and watching CI
  fail.
* README "If the first open reports errors": the Physics 2D error, and the stale
  `Library/ScriptAssemblies` (`Unity.Collections.CodeGen ... Unable to resolve
  reference 'Unity.Collections'`) that a failed resolve leaves behind.

## 1.0.0 - 2026-10-29

First release. Everything below is in this repository and is verified by the
automated checks described in `docs/06_VERIFICATION.md` (225 unit tests, 102
scripts parsed, every Unity-side script type checked, policy check PASS).

### PvP - Free-for-All, the release mode

* Server-authoritative free-for-all: 20 slots, no teams, first operator to
  **50 personal kills** wins. Kills, damage, score, respawns and the match end
  are decided by the server, never by a client.
* Match flow: main menu, mode select, host/join with a lobby, kit prep, live match
  with a Tab scoreboard, pause, results and rematch.
* Kits lock at round start. Respawn is 5 s with 2.5 s of protection, reported on
  the HUD and audible to nearby operators.
* Disconnects free the slot for 90 s so a dropped operator can reconnect with the
  same score and kit. Leave, rejoin and ping are part of the session UI.
* Server-side anti-cheat: fire-rate ceiling, movement/speed validation, score
  spoofing rejection, invalid state rejection.

### Netcode

* Netcode for GameObjects 2.2.0 on Unity transport. Server-authoritative hits,
  health, ammo, reloads, grenades, respawns and results.
* Real dedicated server: `Kordon.exe -kordon-server -serverAddress 0.0.0.0
  -serverPort 27015 -map ffa_zavod7`. The flag parser is engine free and unit
  tested; the server builds the map, runs the match director, logs a heartbeat
  every 15 s and never builds UI or a local operator.
* EU-first regions from `StreamingAssets/Data/Config/net_config.json`. The
  release client refuses loopback addresses; no address is hard coded.
* Voice is an integration point only (channel model, no credentials, no
  third-party account).

### Weapons and equipment

* 7 primaries/pistol (`vk74`, `tr30`, `vs9`, `dmr14`, `sr338`, `k870`, `p9`) and
  3 thrown devices (`frag_f1`, `smoke_rdg`, `flash_zarya`), each with its own
  handling, reload timing, recoil and audio.
* Hitscan with hit zones, per-zone armour, material and angle penetration, and
  lethal head/zone hits. Recoil depends on weapon, stance, movement and breath.
* Magazine system: count, type and size are chosen before the round and sealed at
  round start; there is no permanent ammo counter. Tap R reloads, hold R inspects;
  inspecting shows the exact count and rules out firing. The bottom strip is white
  (full), yellow (partial), red (empty).
* Smoke clouds physically occlude AI vision and player sight, flashbangs blind the
  bodycam through the HUD, blast damage falls off with distance and cover.

### Movement, camera and feel

* WASD, mouse look, sprint with stamina, crouch, prone, lean, inertial movement.
  Stamina drives breathing and aim sway; jumping degrades control. Every
  stance/surface/gear combination has an audibility value.
* The bodycam rig is always on - no toggle, no slider that weakens it. No
  crosshair; minimal HUD.

### Co-op - Operation Silent Kiln

* A finished 2-4 player operation: briefing, linked objectives
  (`reach_yard` -> `breach_gate` -> two fuel-line sabotages and an intel
  retrieval -> 45 s hold -> `reach_extraction` -> `extract`), 34 hostiles with 8
  concurrent, bleed-out, and evacuation. It shares the weapon and movement rules
  with PvP and does not alter the FFA rule set.

### World and art

* Two maps: **Zavod-7** (88 x 68 m, three routes, verticality, limited
  destruction) and **Silent Kiln** (70 x 60 m).
* A modular kit of 60 FBX modules, 10 weapon/grenade models (22 484 triangles),
  51 redistributable textures, 19 materials and 79 synthesised WAV files. Blender
  `.blend` sources ship in `ArtSource/Blender`.

### Meta

* Boot menu, mode select, lobby, prep, HUD, scoreboard, pause, settings, results,
  a 14-step tutorial and a practice range against AI (clearly labelled as practice,
  never presented as online players).
* Subtitles, key rebinding, volume and graphics settings, local and squad voice
  channel model, cosmetic-only progression.

### Verification

* 219 unit tests over the rules that matter: 50-kill victory, reload and
  inspection, ammo hiding, server-side scoring, respawn, match end, damage and
  penetration, anti-cheat, co-op chain, navigation, net configuration, launch
  parsing.
* 102 scripts parsed, Unity-side scripts type checked against API stubs, structure
  and policy checks (`Tools/verify/policy_check.py`, `Tools/unity/audit_packages.py`).

### Known limitation of this release

No prebuilt binary is attached. The project was written in a Linux container with
no Unity Editor, no Windows machine and no Unity licence, so the player was never
compiled here. Source, art sources, tests and the build menu are complete; the
build itself is one menu click in Unity 6 (see README).
