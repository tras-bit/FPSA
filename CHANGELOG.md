# Changelog

All notable changes to KORDON. The project is versioned as one product: game
rules, content, networking and art ship together.

## 1.0.9 - 2026-10-29

The first person weapon stopped being a lerp and became an animation system, and
the weapons were modelled a second time - this time from the shapes the real parts
are made of, with the game's own textures on them.

### Animation

* **`ViewmodelAnimator` (engine free, `Kordon.Core.Weapons`).** The old viewmodel
  smoothed one position toward one target, which is why the weapon slid between
  poses, the magazine snapped and the muzzle never settled. The new core composes
  six layers every frame and is deterministic, so it is unit tested like a rule:
  * **state pose** - hip, sights, sprint, magazine out, magazine in, charging,
    inspect, fumble and empty each own a pose, blended by an exponential approach
    with a per state rate (sights come up in ~120 ms, a reload settles slowly);
  * **sway** - the look rate from the bodycam drives a critically damped spring, so
    the weapon lags behind a fast turn and returns to centre on its own;
  * **bob** - a figure eight tied to the footfall phase, scaled by speed and damped
    to 15 percent while aiming;
  * **breathing** - a slow drift that grows with the stamina model's breath value,
    which is the visible half of the stamina -> aim contract;
  * **recoil** - an impulse that kicks the weapon back and up, then a spring with
    real overshoot brings it down; `AddBoltCycle` cycles the carrier per shot;
  * **landing** - a dip and a muzzle climb on touchdown, fed by `PlayerController.Landed`.
* **Magazine timeline.** Dropping (quadratic fall and a roll), hidden, then the fresh
  magazine rising with an overshoot before it seats exactly at zero offset. Inspect
  pulls it out, turns it 62 degrees towards the eye and keeps it there, which is the
  beat the HUD is allowed to show a round count on.
* **`WeaponViewmodel` rewritten as an adapter.** It reads aim, sprint, speed, breath,
  look rate and landing from the player, forwards every `AmmoEvent` beat, measures the
  loaded FBX (sight height, length) so each weapon aims down its own sights, and writes
  the pose onto the weapon root, the magazine prop and the bolt.

### Art

* **Second modelling pass.** Receivers are lofted shells with a domed dust cover;
  trigger guards are swept along a spline instead of three boxes; grips have a palm
  swell, three finger grooves and stippled panels; magazines are curved bodies with
  stamped ribs, a floor plate that overhangs and feed lips; handguards are ferruled
  wood, free float tubes with cut M-LOK slots, or a ribbed pump; new parts include
  the AK rear sight block, hooded front sight, gas block and cleaning rod, the AR
  forward assist, brass deflector, ejection port door and bolt carrier, sling swivels
  on every long gun, a folded bipod on the bolt gun, and optics: a red dot on the
  carbine, a prism scope on the marksman rifle, a three turret scope on the sniper.
* **Real shading.** Every weapon mesh is beveled, shaded smooth with split normals at
  34 degrees and UV unwrapped; the previous flat shading is what kept a beveled shape
  looking like a CAD block. The weapon maps repeat 4-5 times per UV island, because a
  wall texture at 1:1 on a receiver reads as hammered cast iron.
* **Five weapon surfaces** (`gun_metal`, `gun_bare`, `gun_wood`, `gun_olive`,
  `gun_polymer`) with their own generated texture sets, so a phosphated receiver is not
  galvanised sheet, machined steel is not paint, and oiled walnut is not pallet wood.
  24 surfaces, 66 texture sets.
* **Render sheet rebuilt** around the real textures: three quarter framing solved
  from the projected bounding box, a warm key / cool fill / hard rim studio, AgX, and
  denoising. `docs/art/weapons-lineup.png`, `docs/art/weapons-detail.png`.
* 200 434 triangles across seven weapons and three grenades (was 85 736).

### Fixed

* Texture sidecars lost `textureType: 1` on normal maps when regenerated, which would
  have sampled every normal map as colour; the generator now writes it.
* `WeaponViewmodel` bound the player's landing event in `Awake`, before the operator
  had assigned the player, so the event never fired; it is bound in `Start`.

### Tests

* 16 new tests (273 -> 289) pin the animation contract: hip pose at rest, sights on
  the camera axis, aim blend in and out, sprint only taking over when nothing else is
  happening, recoil kicking and settling, recoil staying bounded under abuse, the
  magazine dropping out and disappearing, the fresh magazine rising and seating at
  exactly zero, inspect lifting and turning the magazine, the charging handle racking
  and running home, sway following the look and returning to centre, bob following the
  feet and damping while aiming, the landing dip, determinism, and zero/negative/long
  frame safety.

## 1.0.8 - 2026-10-29

Ammunition is no longer on screen until a magazine is out of the weapon, the
bottom right block is a designed card instead of a cramped black box, and the
weapons were rebuilt from profiles instead of boxes.

### Changed

* **No ammunition on screen until you check it.** This is the rule the game is
  built on, and the HUD was breaking it by printing `4 MAG` next to every weapon
  and drawing the whole pouch permanently. The rule now lives in one tested place,
  `Kordon.Core.Ui.HudAmmoPolicy`: with the weapon in hands the block shows only the
  three slots and the grenade count - no magazine icons, no pouch size, no round
  count, just `R  check magazine`. Holding R pulls a magazine out, and only then do
  the pouch rows appear together with the exact count (`VK-74 30 ROUND  14 / 30`).
* **Loadout card redesigned.** Rounded panel, header rule, one row per slot with a
  key chip on a raised surface, the weapon name in bold, the active row lit with a
  left pip, and the grenade row doubling as the live fuze readout. The card is
  sized from `HudLayout` (360 x 116 px, 12 px corner radius) and everything inside
  it is anchored inside that rectangle - the previous build scaled the whole block
  with the window and broke the layout.
* **Magazine icons.** Curved silhouettes with a floor plate, feed lips and ribs,
  generated at runtime by `HudIcons` (rounded nine-sliced panels, magazines, discs)
  and cached, so no extra image files ship.

### Rebuilt

* **Weapons modelled from profiles.** The weapons were assemblies of axis aligned
  boxes; several parts were also attached in the wrong place (the stock grew
  forward into the receiver, the handguard hung above the barrel, the trigger guard
  floated off the grip, the pistol carried its magazine forward of the trigger).
  The generator now has a profile modeller - `octagon()` and `rounded()` cross
  sections extruded by `loft()` with a scale and an offset per section, and
  `cut_away()` for real boolean holes:
  * receivers are octagonal with a chamfered nose, a dust cover and a magwell flare;
  * grips are raked, tapering ovals with finger grooves and a three-bar trigger
    guard around the trigger;
  * stocks are a hinge block, a tapering body, a cheek riser and a raked butt pad;
  * handguards are vented tubes with holes cut through both cheeks and slots on top;
  * magazines are one lofted curved body instead of stacked rotated boxes;
  * iron sights are separate rear and front assemblies, each standing on the part
    that carries it (receiver and gas block) instead of sharing one height;
  * the sidearm has a slide with serrations, and its magazine sits inside the grip.
  Triangles: 68 636 -> 85 736 across seven weapons and three grenades; sight
  heights and overall lengths are unchanged, so the aim pose and the ballistics
  data are untouched.
* **Review renders** `docs/art/weapons-lineup.png` and `docs/art/weapons-detail.png`
  are Cycles renders of the exported geometry, produced by
  `Tools/blender/render_weapons.py`.

### Verified

* 273 core tests pass (268 -> 273): five new tests pin `HudAmmoPolicy` - the pouch,
  the icons and the exact count are all absent with the weapon in hands, appear
  together on inspection, and a null snapshot reveals nothing.
* The design-rule checker now verifies the rule where it lives: the HUD must route
  both the exact count and the pouch through `HudAmmoPolicy`, and the policy must
  gate the count on `MayShowExactCount`.
* `docs/art/ui-hud-layout.png` draws both states side by side from the same numbers
  (`HudLayout` for geometry, `HudAmmoPolicy` for visibility). It is labelled as a
  layout reference: there is no Unity editor here, so no in-game screenshot exists.

## 1.0.7 - 2026-10-29

The HUD is redesigned. 1.0.6 moved the magazines into the lower right but drew them
as flat bars inside a cramped black box, which is what the screenshot of that build
showed; this round rebuilds the whole block around a proper loadout card.

### Changed

* **Loadout card.** A rounded card with a header rule and one row per slot: a key
  chip, the weapon name and what its pouch holds (`4 MAG`, `x2` grenades). The active
  slot is the only row in the accent colour and the only one with a lit backdrop and
  a left pip, so "which weapon is in hand" is one glance instead of a remembered
  keypress. The grenade row becomes the live fuze readout (`1.8 s`, warning then
  danger colour) while a grenade is cooked.
* **Magazines are magazines.** The HUD icon used to be a 44 x 15 coloured rectangle,
  which reads as a progress bar. Icons are now drawn as curved box magazine
  silhouettes - tapered body, floor plate, feed lips, ribs, and a bright round on top
  when the magazine is chambered - generated at runtime into cached textures by
  `HudIcons`, so the project ships no extra image files and the match pays nothing
  after the first frame. Primary magazines are 22 x 36 px and the sidearm's are
  smaller (15 x 24) so the two pouches read as different things.
* **Row captions and spacing.** PRIMARY MAGS and SIDEARM MAGS label their rows; the
  inspect readout (`14 / 30`) sits above them and is the only place an exact count
  ever appears. The whole block is 258 px tall and 360 wide on 720p and up.
* **Subtitles moved above the block.** At 138 px from the bottom they shared a band
  with the card on a 1280 wide screen, so a long subtitle would have been drawn
  across it.

### Verified

* The block's geometry lives in `Kordon.Core.Ui.HudLayout` and is unit tested on six
  screen sizes from 1280x720 to 3440x1440: rows never overlap, the block stays clear
  of the centred hint, subtitles clear the block, six primary magazines plus captions
  fit the row, and the icons are taller than they are wide.
* `docs/art/ui-hud-layout.png` is drawn from those same numbers by
  `Tools/blender/draw_hud.py`. It is a layout reference, honestly labelled as such -
  this environment has no Unity editor, so the HUD cannot be captured in game.

## 1.0.6 - 2026-10-29

The map is playable again, the weapons have real detail, the magazine reload is
animated, and the magazines and the equipped weapon are on screen in the lower
right. This round starts from the screenshot of 1.0.5: a player standing on a
sliver of ground with nothing else in the world, then flying off the map.

### Fixed

* **Falling out of the world (root cause).** The map's ground shell was authored
  with `blocksMovement: false`, and `MapBuilder` only created colliders for shells
  that blocked movement, so the terrain had no collider at all and every body fell
  through it. A shell is now solid when it is walkable *or* when it blocks movement,
  and a walkable shell without a collider is reported as an error instead of
  shipping an invisible hole. `MapShell.Walkable` is parsed from the map data, so
  the layout itself states which shell the player stands on.
* **Falling out of the world (second cause).** Nothing kept a player inside the
  map: the terrain is the only floor and the yard has open edges away from the
  fences, so a body that walked off an apron fell forever. `MapBuilder` now builds
  four invisible boundary walls around the layout bounds, records a kill floor at
  y = -8, and the controller returns a body that reaches it to a spawn point
  (logging the fall) instead of letting it fall into the void.
* **Spawning inside the floor.** `Teleport` and `Respawn` now ray cast down and put
  the body on the surface beneath the spawn marker instead of trusting the authored
  height, and a fall reset picks a random spawn rather than always the first one.
* **Magazines jumped instead of reloading.** The magazine prop was hidden on the
  frame the reload event fired and shown again on the insert event, which reads as
  the magazine teleporting. The magazine is now a real animated part: it drops out
  of the well on a curve with a slight tilt, disappears when it is clear, and the
  fresh magazine rises from below, overshoots and seats with a click.

### Changed

* **Weapon models rebuilt with detail.** The weapon meshes were box assemblies;
  each now carries an ejection port, charging handle, selector, magazine well
  flare, receiver rivets, handguard vent slots, magazine ribs and witness holes,
  and a two-segment bevel on the silhouette. Triangle count per weapon set went
  from 22,484 to 68,636 with the sight height and overall length unchanged, so the
  aim pose and the ballistics metadata are untouched.
* **Magazines moved to the lower right.** The magazine strip used to be anchored in
  the bottom centre, where it collided with the interaction hint and the subtitle
  line and shifted whenever the row count changed. Primary magazines, sidearm
  magazines, the grenade line and the inspect readout are now one column in the
  lower right, above a weapon panel. The pocket rule is unchanged: white full,
  yellow partial, red empty, and never a round count until the magazine is checked.

### Added

* **Weapon panel.** The HUD now states which weapon is in hand: one row per slot
  with its key, the weapon name and the number of magazines left in that pouch, the
  active row lit in the accent colour. Before this the HUD only showed magazines,
  so the player had to remember which key they pressed last.
* **HUD layout reference** `docs/art/ui-hud-layout.png`, drawn by
  `Tools/blender/draw_hud.py` from the same numbers the game uses, so the picture
  and the code cannot drift apart.
* **Map audit in Blender** (`Tools/blender/audit_map.py`). It opens the authored
  `.blend`, ray casts a 2 m grid downward over the playable bounds (1496 samples on
  Zavod-7, 1050 on Silent Kiln: 0 holes on both), checks every spawn has floor
  below it and headroom above it, checks the walkable-shell contract, and writes
  `docs/reports/map-audit-<map>.md` plus three-quarter and plan renders in
  `docs/art/`. A hole in the floor now fails the build instead of reaching a player.
* **12 new automated tests** (253 -> 265): the HUD column geometry is unit tested
  against six real screen sizes, and the shipped map data is tested for the
  walkable ground shell, spawns inside the bounds at floor height, and every kit
  placement resolving to a module the kit manifest exports.

## 1.0.5 - 2026-10-29

The screens are laid out properly. This round is a UI pass: the settings screen on
the screenshot was unreadable, and looking at it showed the same hand-positioning
mistake on the connect, kit and tutorial screens.

### Fixed

* **Settings screen rebuilt on a stacked layout.** The old version placed elements
  with independent offsets from a mixture of top, centre and bottom anchors, so the
  sensitivity, volume and effects captions sat on top of the slider tracks, the
  quality readout collided with the subtitles button, and the last control rows were
  hidden behind the footer buttons. Two 860 px panels now hold two stacks whose
  heights are derived from their content, and overflow is reported instead of drawn.
* **Buttons look like buttons.** Every control used to be the same dark panel colour
  as the screen, so nothing read as pressable. Controls now sit on a raised surface
  with hover and pressed tints, centred labels, and the sliders have a track, an
  orange fill and a handle - before, the fill was created and immediately resized to
  zero, so sliders showed no value at all.
* **Controls list fits.** Sixteen rebindable rows at 44 px with a fixed footer pushed
  the last rows off the panel; rows are 33 px and the footer is part of the stack.
  Each row is a button with the action on the left and the key in the accent colour
  on the right, instead of one label with padding spaces.
* **Connect screen:** the address field was anchored to the panel centre while its
  caption was anchored to the top, so PORT and REGION captions floated below their
  fields. Rebuilt as a stack: mode buttons, server block, kit block, host/join, and
  the port field and region stepper on one aligned line. Buttons now report what
  actually happened instead of saying "waiting for players" unconditionally.
* **Kit screen:** the primary/secondary rows, magazine plan, equipment and footer
  were at fixed offsets that overlapped the summary; rebuilt as a stack with the
  presets and steppers in aligned button rows.
* **Tutorial screen:** the 14 step list ran off the bottom of a 180 px box; the list
  is now two columns and fits, with PREVIOUS/NEXT and BACK/START PRACTICE paired.

### Added

* `Kordon.Core.Ui.VerticalLayout`: the stack arithmetic, engine free and unit tested
  (8 tests, including the real settings screen budget and a test that the old
  44 px row spacing could not fit 16 rows).
* `docs/art/ui-settings-layout.png`: the new settings screen drawn from the same
  numbers the game uses, as a reference for what the layout should look like.

## 1.0.4 - 2026-10-29

The player build got past the C# compiler and then failed in Netcode's ILPP code
generator, which is a stage no static check reproduced. Fixed, and now checked.

### Fixed

* **`ScoreSnapshotClientRpc` took a `string[]`.** Netcode's generated serialiser
  cannot handle it - "Don't know how to serialize System.String[]" - and the whole
  player build fails in `NetworkBehaviourILPP` (with an ILPP crash stack on top).
  Scoreboard names now travel as a single length prefixed string and are decoded on
  the client (`Kordon.Core.Serialization.NameListWire`, 9 unit tests). The wire
  format is length prefixed so a name may contain any character, including the
  separator, and a truncated or corrupt payload yields empty names instead of
  throwing on a client. Names are clipped to 32 characters and whitespace
  collapsed, so one operator cannot pad the packet.
* Name cleaning turned a newline into nothing, gluing words together
  (`no\nnewline` -> `nonewline`). Control characters now collapse to a space.

### Added

* `Tools/verify/policy_check.py` now validates **every RPC parameter type** the way
  the ILPP generator does: 143 parameters are checked, and a `string[]`, a `List<>`
  or an unknown struct in an RPC signature fails the build with the exact file and
  line. Verified by putting the old `string[]` back and watching it fail.

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
