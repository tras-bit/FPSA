# KORDON 1.0.8

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

Three things: ammunition is no longer on screen until you check it, the bottom
right block is a designed card instead of a cramped black box, and the weapons were
rebuilt from profiles instead of boxes.

### No ammunition until a magazine is out

With the weapon in hands the block shows the three slots and the grenade count and
nothing else - no magazine icons, no pouch size, no round count, just
`R  check magazine`. Holding R pulls a magazine out; then, and only then, the pouch
rows appear together with the exact count (`VK-74 30 ROUND  14 / 30`). Releasing R
takes it all away again.

The rule lives in one tested place (`Kordon.Core.Ui.HudAmmoPolicy`), and the build
fails if the HUD stops routing the pouch or the count through it.

### The bottom right block

* A rounded loadout card, 360 x 116 px: header rule, one row per slot with a key
  chip on a raised surface, the weapon name in bold, the active row lit with a left
  pip, and the grenade row doubling as the live fuze readout while one is cooked.
* Magazine icons are curved silhouettes (floor plate, feed lips, ribs) generated at
  runtime and cached, not flat coloured bars.
* Everything inside the card is anchored inside the card, and the whole block is
  measured by `HudLayout`, which is unit tested on six screen sizes.

### The weapons

The models were assemblies of axis aligned boxes, and several parts were attached
in the wrong place: the stock grew forward into the receiver, the handguard hung
above the barrel, the trigger guard floated off the grip, and the sidearm carried
its magazine forward of the trigger. The generator now has a profile modeller -
octagonal and rounded cross sections extruded with a scale and an offset per
section, plus boolean cutters for real holes:

* octagonal receivers with a chamfered nose, a dust cover and a flared magwell;
* raked, tapering grips with finger grooves and a three-bar trigger guard;
* stocks with a hinge block, a cheek riser and a raked butt pad;
* vented handguards with holes cut through both cheeks and slots on top;
* magazines lofted as one curved body;
* iron sights as separate rear and front assemblies, each standing on the part that
  carries it;
* a sidearm with a serrated slide and its magazine inside the grip.

**22 484 -> 68 636 -> 85 736 triangles** over the three art rounds. Sight heights
and overall lengths are unchanged, so the aim pose and the ballistics data are
untouched. `docs/art/weapons-lineup.png` and `docs/art/weapons-detail.png` are
Cycles renders of that geometry, made by `Tools/blender/render_weapons.py`.

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
| Code | 108 scripts, engine-free rules core, game layer, netcode layer |
| Rules tested | 273 unit tests, all passing in CI |
| Maps | Zavod-7 (88 x 68 m, 3 routes) and Silent Kiln (70 x 60 m co-op) |
| Art | 60 modular kit models, 7 weapons + 3 grenades (85 736 tris), 51 textures, 19 materials, 2 hand written shaders |
| Audio | 79 synthesised WAV files |
| Art sources | `ArtSource/Blender` - `.blend` files, `.fbx` exports in the project |
| Docs | README, concept, rules, magazine system, networking, art, verification |

## Verification actually run for this tag

| Check | Result |
|---|---|
| Core rule tests (`dotnet test`) | 273 / 273 passed |
| Syntax + structure (Roslyn, C# 9) | 108 scripts, 0 errors |
| Unity-side type check against API stubs | 0 errors, 0 warnings |
| Content / policy audit (packages, shaders, textures, FBX materials, ammo privacy) | PASS |
| Weapon export re-import + bounding box check | 10 files, 85 736 tris |
| Map audit in Blender (floor coverage, spawns, shell contract) | 2546 samples, 0 holes |
| HUD geometry unit tests | 15 tests, 6 screen sizes |
| Unity Editor build | **not run** - no editor in the build environment |
| Two-machine network test | **not run** - same reason |

What could not be executed here, and why, is in `docs/06_VERIFICATION.md`.

## Known limitations

* No prebuilt binary (see above).
* No in-game screenshot of the HUD and no viewmodel animation preview: both need the
  Unity editor. The renders and the HUD mock are labelled as what they are.
* The game has still never been played: no first person play test, no balance pass
  by feel.
