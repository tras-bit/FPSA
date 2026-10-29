# KORDON — verification report

Date: 2026-10-29. This file reports what was actually executed, and what could
not be executed in this environment, without softening either.

## What runs in CI on every push

| Gate | Tool | Last result |
|---|---|---|
| Core rules unit tests | `dotnet test Tests/Kordon.Core.Tests` (219 tests) | **219 passed, 0 failed** |
| Project structure / syntax | `Tests/Kordon.SyntaxCheck` (Roslyn, C# 9) | **100 scripts, 0 errors** |
| Unity side type check | `Tests/Kordon.Game.CompileCheck` (Roslyn + hand written UnityEngine/UnityEditor/NGO stubs) | **0 errors** |
| Design rule verification | `Tools/verify/policy_check.py` | **PASS** (8 structural rules) |
| Reports | committed by the `publish` job | `docs/reports/*.md` |

The Core suite covers the rules that decide whether the game is the game: the
50-kill victory and the 49-kill non-victory, 20 slots and no teams, server side
score and kill registration against live victims only, respawn timing and spawn
protection, kill rate limiting, disconnect grace and reconnect, rematch reset,
magazine reload/inspect/tap-vs-hold, HUD snapshots never exposing a total round
count, damage and penetration, recoil and spread responses, movement and stamina,
the anti-cheat validators, the co-op mission chain and the net config policy.
Thrown equipment is covered by its own suite: deterministic flight integration and
bounce energy loss, cooked-fuze clamping, quadratic fragmentation falloff, cover
blocking fragments but not suppression, armour mitigation, flash cone and duration,
smoke occlusion geometry (segment vs sphere, expiry), the kit reissuing exactly the
grenades it was built with, and a self-inflicted grenade death being uncredited but
still starting the normal respawn timer.

The design rule checks are structural, not aspirational: no crosshair anywhere in
the game code, exact ammunition only through `MayShowExactCount`, no loopback
literal outside the Core address policy, networked FFA explicitly sets
`AllowBots = false`, no `TODO`/`FIXME`/`HACK` in shipped code, every script
folder compiled by the type check, every content id referenced by code present in
`StreamingAssets/Data/Definitions`, and a real editor build script.

## What was verified by generation (executed here)

* Kit: 60 modules, 203k triangles, FBX exported, both `.blend` files saved.
* Maps: Zavod-7 (732 kit instances, 190 nav nodes / 738 links, 20 spawns) and
  Silent Kiln (480 instances, 188 nodes / 1270 links, 6 spawns), both validated
  for connectivity by the nav generator.
* Textures: 17 surface sets + hazard + 2 emissives = 51 PNGs, 19 materials.
* Weapons and thrown equipment: 7 first person weapon models + 3 grenades,
  22 484 triangles, FBX re-imported and bounding boxes verified; 79 synthesised
  WAV clips in total (4 new grenade sounds: fragmentation blast, distant blast,
  smoke hiss, flashbang).

* Dedicated server: the launch path is real code (`DedicatedServerBootstrap` →
  `DedicatedServerHost`), the command line parser is engine free and covered by six
  unit tests, and the server build target is part of the editor build script. The
  server itself was never executed here (no Unity).

## What could NOT be executed here, and why

This sandbox is a Debian container with **no Unity Editor, no Windows machine, no
licence** and no access to Unity's package CDN from the sandbox shell. Therefore:

1. **No player build was produced here.** The build path exists and is
   reproducible (`Kordon → Build → Windows 64-bit player`, see README) but it
   cannot be run without Unity 6000.0.32f1 on a machine with a licence. No binary
   is claimed.
2. **The project was not opened in Unity here.** To compensate, every Unity side
   script is compiled by Roslyn in CI against stubs with real Unity 6000.0 / NGO
   2.2.0 signatures, and a Roslyn structural gate checks assembly coverage,
   language level, type duplication and editor/runtime separation. That catches
   compile errors and structural import failures; it cannot catch an asset import
   setting or a runtime null reference.
3. **The maps were not rendered.** Blender ran headless (bpy wheel) without a GL
   context, so the art previews are top-down layout plans drawn with Pillow, not
   screenshots.
4. **No live two-machine network test.** The authoritative logic is unit tested in
   Core; the NGO glue is type checked but not executed.

## Honest status of the done criteria

| Criterion | Status |
|---|---|
| Project opens with no compile errors | **structurally verified** (Roslyn, 0 errors, both gates); not opened in an editor here |
| Blender runs, `.blend` opens, FBX import | **Blender ran** (bpy 4.5.15) and the `.blend`/FBX were produced; the Unity import itself is unverified here |
| Start a match, pick a loadout, move, aim, fire, inspect, reload | implemented and covered by Core tests; runtime behaviour unverified in an editor |
| Real networked FFA, 20 slots, server score, 50-kill win | implemented (NGO 2.2.0, server authority); rules unit tested; transport unverified here |
| Co-op mission completable to evacuation | implemented; mission chain unit tested; AI and networking unverified here |
| Menus, settings, scoreboard, match end | implemented in code; compile checked |
| Automated checks pass | **yes** — 219 tests, 0 syntax errors, 0 compile errors, policy PASS |
| Runnable build + instructions, or an exact limitation | **exact limitation documented above** (no Unity/Windows in this environment) |

## Next step for a human with Unity

1. Open the project with 6000.0.32f1 (packages resolve from the official index).
2. `Kordon → Open boot scene (press Play)`.
3. If the editor reports an import issue, the file and line will be in the
   Console; nothing in the runtime code depends on hand authored scene or prefab
   YAML, so there is no scene graph to repair.
