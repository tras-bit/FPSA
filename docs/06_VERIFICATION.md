# KORDON — verification report

Date: 2026-10-29. This file reports what was actually executed, and what could
not be executed in this environment, without softening either.

## What runs in CI on every push

| Gate | Tool | Last result |
|---|---|---|
| Core rules unit tests | `dotnet test Tests/Kordon.Core.Tests` (289 tests) | **289 passed, 0 failed** |
| Project structure / syntax | `Tests/Kordon.SyntaxCheck` (Roslyn, C# 9) | **111 scripts, 0 errors** |
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

## 1.0.6 map and weapon verification (executed here)

The screenshot that opened this round showed a player on a sliver of ground with
the rest of the world missing, then flying off the map. Two independent defects
were found and both are fixed; this is what was executed to prove it.

* **Authored scene audit.** `Tools/blender/audit_map.py` opens the real
  `ArtSource/Blender/<map>.blend` used for the FBX export and ray casts a 2 m grid
  downward over the playable bounds: **1496 samples on Zavod-7, 1050 on Silent
  Kiln, 0 holes** on both. Every spawn has the ground shell within 0.6 m below it
  and 1.85 m of clear headroom above it. Reports:
  `docs/reports/map-audit-ffa_zavod7.md`, `docs/reports/map-audit-coop_silent_kiln.md`.
* **Review renders.** The same run renders `docs/art/ffa_zavod7-iso.png`,
  `-top.png` and `docs/art/coop_silent_kiln-iso.png`, `-top.png` from the authored
  scene (CPU Cycles, in this environment). These are images of the map geometry,
  not drawings of a plan.
* **Why the player fell.** The ground shell was authored `blocksMovement: false`
  and `MapBuilder` only added colliders for shells that blocked movement, so the
  terrain had no collider in Unity. The audit proves the geometry was there; the
  missing collider was purely the assembly code. `MapBuilder` now treats a shell as
  solid when it is walkable or blocks movement, logs an error for a walkable shell
  without a collider, builds boundary walls from the layout bounds and records a
  kill floor, and the controller returns a body that reaches the kill floor to a
  spawn (with a warning) instead of letting it fall forever.
* **Weapon detail.** All seven weapons and three grenades were rebuilt and exported
  from Blender: 22 484 -> **68 636 triangles**, with `sightHeightM` and length
  unchanged (verified by diffing `weapon_manifest.json`), so the aim pose and the
  ballistics metadata are not disturbed. FBX files are re-imported and their
  bounding boxes compared with the authored ones by the build script.
* **HUD geometry.** The bottom-right column (weapon panel, primary magazines,
  sidearm magazines, grenade line, magazine inspection) is computed by
  `Kordon.Core.Ui.HudLayout` and unit tested against six screen sizes from
  1280x720 to 3440x1440: no row overlaps, the column stays out of the centred
  interaction hint, and the whole column fits in the bottom 235 px.
* **Map data contract.** `Tests/Kordon.Core.Tests/MapContentTests.cs` (14 tests)
  asserts exactly one walkable ground shell per map, that it is `ground`, that every
  spawn is inside the bounds at floor height with a zone, that every kit placement
  resolves to a module the kit manifest exports and carries a tag, and the 20-slot
  four-zone / fifty-elimination free-for-all layout.

What this does **not** prove: that the collider fix feels right in a live game.
That needs the Unity editor and a Windows build, neither of which exists here. The
geometry audit and the compile check cover the failure that was actually observed,
but a first person play test remains the missing verification.

## 1.0.7 HUD verification (executed here)

The 1.0.6 HUD put the magazines in the lower right but drew them as flat bars in a
cramped box.  This round replaces the block with a loadout card plus magazine
silhouettes; the geometry and the icon rasteriser are what was verified.

* **Geometry is unit tested, not eyeballed.** `Kordon.Core.Ui.HudLayout` owns the
  edge inset, column width, the four row heights and the gaps, and
  `HudLayoutTests` (15 tests) checks six screen sizes from 1280x720 to 3440x1440:
  no row overlaps another, the block never reaches the centred interaction hint,
  subtitles sit above the block, six primary magazines plus their caption fit the
  row, and the magazine icons are taller than they are wide (a horizontal bar was
  exactly the 1.0.6 mistake).
* **Icons are generated, not loaded.** `HudIcons` rasterises a rounded nine-sliced
  panel, a curved magazine (tapered body, floor plate, feed lips, three ribs, a
  bright round when chambered) and a disc into `Texture2D`s cached by colour, so the
  pocket rule still comes from `UiBuilder.MagazineColor` and no image files ship.
  This cannot be rendered here - there is no Unity - so what is verified is that it
  compiles against the Unity API stubs with 0 errors and 0 warnings, and that
  `Tools/blender/draw_hud.py` draws the same silhouettes from the same numbers for
  the reference image.
* **The reference image is labelled as a reference.** `docs/art/ui-hud-layout.png`
  is a layout mock, not a screenshot; the caption inside the image says so.

What this does not prove: how the HUD looks in game. Font rendering, sprite
filtering and the real 1280x720 output need the Unity editor.

## 1.0.8 ammunition privacy and weapon rebuild (executed here)

The user's rule, in their words: rounds must not be displayed without taking the
magazine out.  The previous HUD printed a pouch count next to every weapon and drew
the whole pouch permanently, so this was a rule the game stated and then broke.

* **The rule is code, not a convention.** `Kordon.Core.Ui.HudAmmoPolicy` answers
  three questions - is the pouch visible, is the exact count visible, how many
  magazine icons may be drawn - and `HudAmmoPolicyTests` (5 tests) pins all three,
  including the case that matters: an inspect flag without a magazine in hand, or a
  round count without an inspection, must leak nothing.
* **The HUD cannot bypass it.** `HudScreen` hides the magazine rows, their captions,
  the pouch summary on the loadout card and the inspect readout through that policy.
  `Tools/verify/policy_check.py` fails the build if the HUD stops routing the exact
  count or the pouch through it, or if `HudAmmoPolicy` stops gating on
  `MayShowExactCount`.  Verified by watching the check fail on the previous commit.
* **The weapons are renders of the real geometry.** `Tools/blender/render_weapons.py`
  renders every weapon from the same `build_weapon()` the FBX export uses;
  `docs/art/weapons-lineup.png` and `docs/art/weapons-detail.png` are those renders.
  The build re-imports each FBX and compares its bounding box with the authored one,
  which is what proves the exported asset is the model that was rendered.

What this does not prove: how any of it looks and feels in the game.  The renders
show geometry from a fixed camera, not a viewmodel in motion; the HUD mock shows
layout, not a live canvas.  A first-person play test in the Unity editor is still
the verification this environment cannot perform.

## 1.0.9 weapon animation and 1.0.10 art rewrite (executed here)

The previous viewmodel smoothed a single position toward a single target. That is
why the weapon slid between poses, the magazine teleported and the muzzle never
settled. The behaviour now lives in `Kordon.Core.Weapons.ViewmodelAnimator`, an
engine free class, and is covered by 16 tests (273 -> 289 in the suite):

| Behaviour | What the test pins |
|---|---|
| Hip pose | rests at the same hand position, `State == Ready` |
| Sights | `AimBlend > 0.95` after two seconds and the sights land on the camera axis within 2 cm |
| Aim release | blend returns below 0.1, so the weapon never stays glued to the eye |
| State ownership | a reload owns the weapon; sprint only takes over when nothing else is happening |
| Recoil | kicks back and up on the first frame, settles inside 1 cm and 1.5 degrees in one second |
| Recoil bounds | 600 frames of maximum impulse under long frames never produce NaN or runaway travel |
| Magazine out | visible while dropping, hidden after 0.24 s |
| Magazine in | rises within two frames and seats at exactly zero offset, rotation included |
| Inspect | magazine lifts above the well, turns more than 30 degrees, and the animator reports it out (the HUD contract) |
| Charging handle | racks to 1.0 and runs home below 0.02 |
| Sway | a 220 deg/s turn moves the weapon more than 0.4 degrees and it returns to centre |
| Bob | 2.5 cm of motion at a run, damped below 60 percent of that while aiming |
| Landing | dips more than 8 mm and climbs more than a degree |
| Determinism | two animators on identical input produce identical poses |
| Frame safety | zero, negative and 0.1 s frames stay finite and inside the hip envelope |

What this does not prove: how it feels under a mouse. The tests fix the numbers a
human would otherwise have to judge from a video - settling times, overshoot,
whether the magazine leaves the well before the hand moves - but a first person
play test in the editor is still the verification this environment cannot perform.

Art, generated here and verified by re-import:

* seven weapons rebuilt from one layout table each: every station (bore, receiver
  top and bottom, grip, magazine well, trigger guard, stock root) is derived from
  the receiver, connected parts overlap by 3-5 mm, and no part is positioned by
  hand. Bored and stepped barrels, muzzle devices with ports cut through the tube,
  receivers with panel frames, slotted screw heads, controls and a cut ejection
  port with the bolt carrier visible, handguards with per side M-LOK pockets,
  solid chassis stocks with a rounded thumbhole and a hinge, collapsible stocks on
  an exposed buffer tube, lofted raked grips with finger grooves and stipple
  panels, curved magazines with transverse ribs and a lipped floor plate, a hooded
  AK front sight, and hollow optics with dark coated lenses (red dot / prism /
  three turret scope); 261 640 triangles including grenades;
* every mesh beveled, shaded smooth with split normals at 34 degrees and UV
  unwrapped, so the generated textures have somewhere to land;
* five weapon surfaces (`gun_metal`, `gun_bare`, `gun_wood`, `gun_olive`,
  `gun_polymer`) with their own texture recipes, 24 surfaces and 66 texture sets;
* `docs/art/weapons-lineup.png` and `docs/art/weapons-detail.png` rendered from the
  exported geometry wearing those textures (Cycles, CPU).

A defect this round found and fixed: regenerating the texture set silently dropped
`textureType: 1` from every normal map `.meta`, which would have imported all 60
normal maps as plain colour. The generator now writes it, and the policy check that
found it stays in place.

## 1.0.11 hands and bodies (executed here)

The first person weapon had no hands and the other players were capsules.  Both are
now authored geometry, and both are checked the way the rest of the art is:

* **weapon FBX carry two extra nodes.**  Each of the seven weapon files exports
  `HandRight` and `HandLeft` next to the weapon and the magazine; the viewmodel finds
  them by name and animates the support hand.  The exporter re-imports every file and
  compares the bounding box, so a hand that lands in the wrong frame fails the build
  (`weapons: 10 files, 375 388 triangles`).
* **the support hand follows the magazine.** `ViewmodelPose.SupportHandBlend` is core
  state with two tests: it stays below 0.001 with no reload, passes 0.3 within six
  frames of `MagazineOut`, settles above 0.8, and returns below 0.08 once the reload
  completes.  296 tests in the suite, the five newest of which cover the body gait
  (`BodyGaitTests.cs`): standing still, cadence against speed over stride, alternating
  legs inside their joints, a crouch that folds the knees, and a fall that ends on the
  ground.
* **bodies are authored, not primitives.** `Tools/blender/build_operators.py` writes
  `operator.fbx` and `militia.fbx` (28 840 triangles each, 1.73 m to the top of the
  helmet) plus
  `StreamingAssets/Data/Art/operator_manifest.json`.  `RemoteOperatorView` and the
  co-op hostile spawner load them from Resources and only fall back to a capsule when
  the file is missing; the fallback is logged and never silent.
* **hit volume matches the rules.** Both bodies carry a 1.74 m capsule proxy - the
  authored body to the top of its helmet - so the server's `ZoneFromHit` (head above
  1.5 m, thorax above 0.85 m) resolves against the silhouette a player sees, and a
  round that passes over a helmet misses instead of counting as a headshot.
* `docs/art/operators.png` is the body sheet, rendered from the exported geometry
  wearing the same generated textures the game loads.
* **the exported body is a hierarchy, not one mesh.** Nine parts sit on `HipL`,
  `KneeL`, `ShoulderL`, `ElbowL` (and the right hand side) under one root, which is
  what lets `OperatorBodyRig` walk the body and drop it when it dies. Re-importing the
  FBX and turning `HipL` by -28 degrees moves the shin 0.39 m forward, which is how the
  joint centres were checked; the build also refuses a body whose proportions are not
  an operator (1.70-1.86 m tall, 0.52-0.72 m across, under 0.78 m deep, nothing below
  the ground plane), which is the check that caught the volumes that were authored
  lying down.

What this does not prove: the bodies have no skeleton, so there is no walk, aim,
reload or death animation, and a death resolves as damage rather than a ragdoll.  The
hands are anatomy built from sweeps, not sculpted character art - they read correctly
at viewmodel distance and they hold the weapon they are exported with, which is the
problem they were built to solve.

## What was verified by generation (executed here)

* Kit: 60 modules, 203k triangles, FBX exported, both `.blend` files saved.
* Maps: Zavod-7 (732 kit instances, 190 nav nodes / 738 links, 20 spawns) and
  Silent Kiln (480 instances, 188 nodes / 1270 links, 6 spawns), both validated
  for connectivity by the nav generator.
* Textures: 24 surfaces -> 66 texture sets (albedo, normal, metallic/smoothness) and
  24 Unity materials, all generated from seeded noise.
* Weapons and thrown equipment: 7 first person weapon models + 3 grenades,
  261 640 triangles (1.0.10 modeller rewrite; 200 434 in 1.0.9, 85 736 in 1.0.8,
  68 636 after the 1.0.6 detail pass, 22 484 before it), every mesh UV unwrapped and shaded with
  split normals, FBX re-imported and bounding boxes verified; 79 synthesised
  WAV clips in total (4 new grenade sounds: fragmentation blast, distant blast,
  smoke hiss, flashbang).

* Dedicated server: the launch path is real code (`DedicatedServerBootstrap` →
  `DedicatedServerHost`), the command line parser is engine free and covered by six
  unit tests, and the server build target is part of the editor build script. The
  server itself was never executed here (no Unity).

* Shaders: both shaders sit under `Resources` and are loaded through
  `ShaderLibrary`; a policy check (run in CI) fails if a shader moves out of
  `Resources`, if a `Shader.Find` literal names a missing shader, or if a
  `ShaderLibrary` resource path stops resolving. Without this, `Shader.Find` would
  return null in a player build and the bodycam pass would vanish silently.

* First real editor open (2026-10-29, Unity 6000.0.32f1) surfaced a defect the
  static checks could not see: the manifest did not enable the built-in
  **Physics 2D** package, so Netcode for GameObjects failed to compile
  (`CS1069: Rigidbody2D ... UnityEngine.Physics2DModule`). Fixed by declaring
  `com.unity.modules.physics2d`, and `Tools/verify/policy_check.py` now fails if a
  required built-in package is missing or if the three package versions drift.
  This is the honest cost of building without an editor: package-level errors only
  appear when the project is actually opened.

* Art contracts (1.0.2): a `KRD_`-prefixed FBX material resolves to an authored
  `M_*.mat`, and every texture `.meta` carries the import settings the art needs.
  Both were real defects found while preparing the release - the whole map would
  have rendered with untextured imported materials, and all 17 normal maps were
  imported as plain colour. Both rules were verified by breaking them on purpose.
* Material naming is unit tested (`SurfaceNamingTests`, 6 tests): the normalisation
  from `KRD_steel_rust (Instance)` to `steel_rust` is the part that fails silently,
  so it is the part with tests.

* Assembly boundaries (1.0.3): the editor reported
  `CS0012 NetworkEndpoint ... Unity.Networking.Transport` for `KordonNetwork.cs`.
  The static checks compile every script into one assembly, so a missing asmdef
  reference is invisible to them. A policy rule now maps package namespaces (by
  `using` or fully qualified use) to the reference the owning asmdef must carry;
  verified by removing the reference and watching the check fail.
* Editor warnings are now part of the target: the Unity-side type check reports
  **0 errors, 0 warnings** (CS0618 obsolete API, CS0067 unused event, CS0414/CS0169
  dead fields all cleared).

* RPC serialisation (1.0.4): the build failed in Netcode's ILPP code generator with
  "Don't know how to serialize System.String[]" - a stage after the C# compiler that
  neither the compiler nor the static checks exercised. Fixed by sending the
  scoreboard names as one length prefixed string (`NameListWire`, 9 tests), and a
  policy rule now checks all 143 RPC parameters against the types the generator
  accepts. Verified by reintroducing the offending signature.

* Screen layout (1.0.5): the stacks are engine free and tested
  (`UiLayoutTests`, 8 tests) - rows never overlap, overflow is reported, and the
  real settings screen budget (18 rows on the left, 19 on the right) fits with
  under 90 px to spare. The old hand-positioned layout is encoded as a test that
  fails, which is what it did on screen: 16 rows at 44 px did not fit.
  `docs/art/ui-settings-layout.png` draws the result from the same numbers.

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
| Automated checks pass | **yes** — 289 tests, 0 syntax errors, 0 compile errors, policy PASS |
| Runnable build + instructions, or an exact limitation | **exact limitation documented above** (no Unity/Windows in this environment) |

## Next step for a human with Unity

1. Open the project with 6000.0.32f1 (packages resolve from the official index).
2. `Kordon → Open boot scene (press Play)`.
3. If the editor reports an import issue, the file and line will be in the
   Console; nothing in the runtime code depends on hand authored scene or prefab
   YAML, so there is no scene graph to repair.
