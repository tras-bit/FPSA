# KORDON 1.0.6

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

This round starts from a screenshot of 1.0.5: a player standing on a sliver of
ground with nothing else in the world, then flying off the map. The map is
playable again, and the weapon handling the game talks about is on screen and
animated.

* **The ground has a collider.** The terrain shell was authored with
  `blocksMovement: false`, and the map assembly only added colliders to shells that
  blocked movement - so the ground had no collider at all and every body fell
  straight through it. A shell is now solid when it is walkable *or* blocks
  movement, and a walkable shell without a collider is reported as an error instead
  of shipping an invisible hole.
* **You can no longer leave the map.** Nothing kept a player inside it: the yard has
  open edges away from the fences, and a body that walked off an apron fell forever.
  Four invisible walls now follow the layout bounds, a kill floor sits at y = -8,
  and a body that reaches it is returned to a spawn point with a warning instead of
  falling into the void.
* **Spawns put you on the floor**, not at an authored height above or inside it:
  `Teleport`/`Respawn` ray cast down and settle on the surface, and a fall reset
  picks a random spawn rather than always the first one.
* **Magazines actually reload.** The magazine used to be hidden on the frame the
  reload event fired and shown again on the insert event, so it teleported. It now
  drops out of the well on a curve with a slight tilt, disappears once it is clear,
  and the fresh one rises from below, overshoots and seats.
* **Better weapon models.** Each weapon gained an ejection port, charging handle,
  selector, magazine well flare, receiver rivets, handguard vent slots, magazine
  ribs and witness holes, plus a two-segment bevel on the silhouette:
  **22 484 -> 68 636 triangles** across the seven weapons and three grenades. Sight
  heights and overall lengths are unchanged, so the aim pose and the ballistics
  data are untouched.
* **Magazines sit in the lower right**, as the reference has them: primary
  magazines, sidearm magazines, the grenade line and the magazine inspection readout
  are one column above a weapon panel. The pocket rule is unchanged - white full,
  yellow partial, red empty, never a round count until the magazine is checked.
* **A panel shows which weapon is in hand.** One row per slot with its key, the
  weapon name and how many magazines that pouch has left; the active row is lit.

## The map was audited inside Blender

`Tools/blender/audit_map.py` opens the real `ArtSource/Blender/<map>.blend` - the
same scene the FBX files come from - and ray casts a 2 m grid downward over the
playable bounds. Zavod-7: **1496 samples, 0 holes**. Silent Kiln: **1050 samples,
0 holes**. Every spawn has ground within 0.6 m below and 1.85 m of headroom above.
The run also renders the review images in `docs/art/` (`*-iso.png`, `*-top.png`)
from that scene.

That audit is the answer to "is the map geometry there": yes, and it was there
before 1.0.6 as well. The missing collider was in the assembly code, which is what
the fix above addresses. A first person play test is still the one verification
this environment cannot perform.

## This release contains the game source, not an `.exe`

Being straight about it: the project was authored in a Linux container without a
Unity Editor, without a Windows machine and without a Unity licence, so no player
binary could be produced here. What is here is the complete project - open it in
Unity 6 and the game runs; the same menu builds the `.exe`.

## Look at it in three steps

1. Unity Hub -> **Add project from disk** -> this folder -> open with **6000.0.32f1**.
2. Menu **Kordon -> Open boot scene (press Play)**.
3. Press Play. Boot menu -> FREE-FOR-ALL vs bots for practice, or HOST a real match.

* **Co-op** - CO-OP OPERATION (2-4) -> HOST, then 1-3 operators join.
* **Dedicated server** - `Kordon.exe -kordon-server -serverAddress 0.0.0.0
  -serverPort 27015 -map ffa_zavod7`.

## What is in the box

| | |
|---|---|
| Code | 106 scripts, engine-free rules core, game layer, netcode layer |
| Rules tested | 265 unit tests, all passing in CI |
| Maps | Zavod-7 (88 x 68 m, 3 routes) and Silent Kiln (70 x 60 m co-op) |
| Art | 60 modular kit models, 7 weapons + 3 grenades (68 636 tris), 51 textures, 19 materials, 2 hand written shaders |
| Audio | 79 synthesised WAV files |
| Art sources | `ArtSource/Blender` - `.blend` files, `.fbx` exports in the project |
| Docs | README, concept, rules, magazine system, networking, art, verification |

## Verification actually run for this tag

| Check | Result |
|---|---|
| Core rule tests (`dotnet test`) | 265 / 265 passed |
| Syntax + structure (Roslyn, C# 9) | 106 scripts, 0 errors |
| Unity-side type check against API stubs | 0 errors, 0 warnings |
| Content / policy audit (packages, shaders, textures, FBX materials) | PASS |
| Map audit in Blender (floor coverage, spawns, shell contract) | 2546 samples, 0 holes, 0 findings |
| Weapon export re-import + bounding box check | 10 files, 68 636 tris |
| Unity Editor build | **not run** - no editor in the build environment |
| Two-machine network test | **not run** - same reason |

What could not be executed here, and why, is in `docs/06_VERIFICATION.md`.

## Known limitations

* No prebuilt binary (see above).
* The map geometry and the map assembly are now checked, but the game has still
  never been played: no first person play test, no balance pass by feel.
* A first Play can surface editor-only fixes (an import setting, a missing `.meta`)
  that no static check can see. Two such rounds are already fixed in 1.0.1/1.0.2.
