# Changelog

All notable changes to KORDON. The project is versioned as one product: game
rules, content, networking and art ship together.

## 1.0.15 - 2026-10-30

The holes in the gloves, and why they were there.

### Fixed

* **The hands were a dozen separate solids that only looked like a hand.** Fingers,
  knuckles, pads, heel and thumb were each a closed mesh that interpenetrated its
  neighbours, and every one of those seams is a crevice with the weapon visible
  through it. From the shooting angle the crevices read as holes punched through the
  glove, which is exactly what they were: there was no glove, only parts.
  The pieces are now voxel-remeshed into a single watertight skin, smoothed and
  decimated to 5 200 faces, so overlaps become one continuous surface and every crease
  becomes a real fillet. The forearm stays a separate part - it has to cross a
  material boundary - but it is buried inside the fused glove.
* A support web between the thumb and the index finger: without that volume the U
  between them is a genuine opening, and the trigger guard showed through it.
* Two `hand_seam` bars 2 mm wide sat on the outside of the skin after fusing and read
  as a wire sticking out between the fingers. Removed: a seam is a groove in leather,
  not a rod laid on it.
* The thumb tip and the four pads around its base all converged on one spot, which
  fused into a single oval bulge - a boxing glove. The pads are trimmed, the palm is
  narrower across the knuckles, and the thumb ends against the index finger.

## 1.0.14 - 2026-10-30

The bodies and the hands, rebuilt where they read as mannequins, plus the review
tools that make the next pass checkable instead of a matter of taste.

### Changed

* **Bodies lean.** `BodyGait` now damps the speed the animation is played at, so a
  body cannot snap from a standstill into a full stride in one frame, and it exposes
  the torso pitch (aim, sprint, crouch) and the lean into a turn. `OperatorBodyRig`
  applies both: the chest is no longer dead vertical while the legs move.
* **The gloved hands are hands.** The four fingers were 10.2 mm tubes on a 17 mm
  pitch, so they interpenetrated and read as one mitten; they are 8.9 mm on a
  18.2 mm pitch with a tapered tip and a different reach on each digit, the palm is
  12% thinner with a hypothenar pad and a stitched wrist crease, and the thumb has a
  joint at its base. Tubes and lumps went from 10 to 14 sides and from 7 to 11
  sections, because these are the closest parts to the camera in the whole game.
* **The operators are people.** Boots: the sole was a 26 mm plate floating 8 mm
  under an upper whose toe began higher still, which read as a skateboard. The upper
  is now authored around one constraint - its underside is inside the sole at every
  section - with an ankle shaft the trouser blouses over. Knee: a joint volume between
  calf and thigh instead of two tubes butting together. Sleeve: a tapered ring instead
  of a box on the wrist. Head: a helmet that comes down over the ears in one continuous
  curve (the old one was a dome on a wider band), a brow ridge, a neck gaiter and a
  chin strap.
* `Tools/blender/render_hands.py` renders `docs/art/hands-detail.png`: the firing hand
  and the support hand, framed from each hand's own bounds at a distance solved from
  the lens, so two iterations can be compared without the shot moving. The pass is
  only honest if it is looked at after every change.

### Fixed

* The operator review render showed a bright plate under each boot: the floor was at
  zero, exactly where the soles are, so the two were coplanar. The floor is 6 mm below.

## 1.0.13 - 2026-10-30

The map, finished where it was actually broken: the spawns.

### Fixed

* **Players spawned inside crates and stared at walls.** The corner shelters were built
  with their walls on the side that faces the map, and their crates, barrels and
  sandbags stood inside the five spawn slots themselves. Nothing in the pipeline looked
  at a spawn horizontally - the map audit only asked whether there was floor under it -
  so the first editor run put the camera into a crate half a metre away.
  The shelters are open to the yard now, their props stand clear of the slots, and the
  map build runs `place_spawns_in_the_open`: every spawn is nudged to the nearest point
  with 1.2 m of elbow room and turned towards the clearest view, then checked - 1.2 m
  of clearance and 6 m of sight, or the build fails. Both maps pass; the tightest slot
  now has 1.28 m of room and 15.5 m of view.
* `check_spawn_clearance` measures that against the real module boxes, in the same pass
  that writes the layout, so a spawn can no longer be "on the floor" and still be
  unusable.

## 1.0.12 - 2026-10-30

The first run in a real Unity editor, and the two things it broke on.

### Fixed

* **The game booted into a dead screen.** `UiBuilder.Font` asked for `Arial.ttf`,
  and Unity 6000 answers that with `ArgumentException: Arial.ttf is no longer a valid
  built in font`. The exception walked out of `GameFlow.Awake` before a single screen
  was built. The builtin is `LegacyRuntime.ttf` now, it is resolved once and cached,
  an OS font is the fallback, and a missing font logs instead of throwing - a font
  rename can never take the whole UI down again.
* **The map was assembled as a mirror, not a rotation.** Blender exports with the
  Unity preset (`axis_forward = -Z`, `axis_up = Y`), so an authored point (x, y, z)
  arrives in Unity as (x, z, -y). `MapSpace` used (x, z, y) and negated the yaw with
  it: positions and rotations were mirrored together, but the module geometry comes
  out of the FBX unmirrored, so every asymmetric module - stairs, doors, conveyors,
  gates, gantries - was turned the wrong way relative to the gap it was built to fill.
  A level built that way reads as collapsed pieces standing in a grid, which is what
  the first editor run showed. The conversion is a rotation now, the yaw keeps its
  sign, and both live in `Kordon.Core.Maps.MapOrientation` under four tests instead of
  in a comment.

## 1.0.11 - 2026-10-29

The player got hands, and every other player got a body.

### Art

* **First person gloves (`Tools/blender/build_hands.py`).** The weapon models used
  to be held by nothing: a rifle floated in front of the camera. Every weapon FBX
  now carries the two gloved hands that hold it, authored in the weapon's own space
  as separate nodes (`HandRight`, `HandLeft`) so the viewmodel can find and animate
  them without an anchor table:
  * a right hand closed on the pistol grip - palm lofted from rounded sections,
    four fingers curling round the front strap onto the far side, thumb over the
    top, knuckle pads, stitched seams, a wrist strap and a jacket cuff;
  * a left hand cradling the fore-end at that weapon's own handguard radius;
  * the sidearm's support hand wraps the shooting hand instead of a fore-end, and
    the pump gun's right hand closes on the stock wrist because it has no pistol
    grip.
  Hands are anatomy, not boxes: every finger is a swept tube through named joints
  with a rounded joint at each knuckle and a rounded cap at the tip.
* **Operator bodies (`Tools/blender/build_operators.py`).** Every other player in a
  match was a Unity capsule and a sphere, and the co-op militia were the same capsule
  tinted rust. There are now two authored bodies - `operator.fbx` and `militia.fbx`,
  1.73 m to the top of the helmet, 28 840 triangles each - with boots, legs, knee
  pads, a belt with pouches, a plate carrier with three magazine pouches and an
  admin pouch, side plates, a back panel with a radio and an antenna, shoulders,
  arms with elbow pads,
  gloved fists, a helmet with rails, ear cups and an NVG mount, goggles with dark
  lenses, and a rifle slung across the back with a two point sling. The two variants
  share geometry and differ in kit: olive canvas and plate for the operator, worn
  brown canvas and steel for the militia.
* **Five more surfaces:** `glove_leather` (oiled leather: pebble grain, creases,
  worn pale where it flexes), `uniform_canvas` and `militia_canvas` (ripstop with a
  visible warp and weft, dust), `armor_plate` and `militia_steel` (composite plate in
  a fabric cover, pitted and scuffed). 29 surfaces, 81 texture sets.
* **375 388 triangles** of weapons and thrown equipment and 57 680 triangles of
  bodies; `docs/art/operators.png` is the new body sheet.

### Bodies that move

* **The body is articulated, not one welded mesh.** The builder now joins and
  re-origins nine parts onto hip, knee, shoulder and elbow centres, and exports them
  under empties (`HipL`, `KneeL`, `ShoulderL`, `ElbowL` and their right hand
  counterparts) below a single root. The FBX therefore carries a working hierarchy,
  and a body can be posed by turning eight transforms.
* **Walk, run and crouch are real maths (`Kordon.Core.Characters.BodyGait`).** Cadence
  follows speed over stride, so a walking body takes steps at a walking rate and a
  sprinting body lengthens its stride instead of twitching. The knee folds while its
  leg travels forward and straightens as the foot plants, arms counter swing at just
  over half the amplitude, and two bob events per cycle match the two foot plants.
  Five core tests cover it: a standing body does not march, cadence matches
  speed/stride, the legs alternate and stay inside their joints, a crouch folds the
  knees, and a killed body falls.
* **The dead fall over (`OperatorBodyRig`).** Death used to switch the renderers off,
  so an opponent blinked out of the world the instant they were hit. A dead body now
  pitches forward over 0.85 s, sinks the last few centimetres and stays where it fell
  until it is cleaned up; a respawn stands it back up.
* **Bodies no longer slide.** `RemoteOperatorView` and the co-op `HostileAgent` feed
  the body's own frame to frame movement into the rig, so the legs match the actual
  velocity of the body in the world - a stationary body stands.
* **A volume that lies down fails the build.** `bw.loft` sweeps along Y by default, and
  the belt, hips, torso, carrier, helmet and helmet rim were all authored as stacks of
  horizontal rings without `axis="Z"`: they were lying flat, which is why the belt and
  the helmet rim read as discs and the body was 1.9 m deep. They are vertical volumes
  now, and `_verify_proportions` refuses to export a body that is wider, deeper,
  shorter or taller than an operator.

### Game

* **`RemoteOperatorView`** loads the authored body from
  `Resources/Kordon/Operators/operator` and drives it from the server snapshot;
  it only falls back to the capsule if the art is missing, so a missing import can
  never silently turn the game back into grey blocks. A 1.80 m capsule proxy gives
  the server the same hit volume the rules already assume (head above 1.5 m,
  thorax above 0.85 m).
* **Co-op hostiles** load `militia` the same way, with the same hit proxy.
* **The support hand animates.** `ViewmodelPose` gained `SupportHandBlend`: the left
  hand stays on the fore-end while the weapon is ready and travels with the magazine
  during a reload or an inspect, then returns. The adapter applies it to the
  `HandLeft` node, so a magazine can no longer fall out of a rifle while both hands
  stay put.

### Fixed

* `blob()` in the hand builder was built in unit space and then scaled per axis, so
  every knuckle and pad was five times its intended size and the first hand render
  was a set of boulders.
* The support hand and the sleeve were authored in the wrong frame: the palm ran
  down the grip instead of across it, which is what makes a hand read as a paddle.
  The hands are now built in the grip's own frame and rotated into the weapon by the
  grip rake.
* The first person forearms were straight tubes aimed sideways at the camera, which
  read as a pipe welded to the fist. They now drop away below the grip with a taper, a
  rolled glove cuff at the wrist and a sleeve band, and they stay out of the sight
  picture.
* The operator's arms reached for a rifle that was not in their hands; the rifle is
  slung across the back now, with a sling, so nothing floats.
* The slung rifle stuck 0.44 m straight out of the operator's spine, because it was
  rolled about its own barrel (`-27 degrees about Y`) and only then pitched. The pitch
  comes first now, so the weapon lies diagonally across the shoulder blades from the
  left shoulder to the right hip - the shape a slung rifle actually makes.
* Six body volumes - hips, belt, torso, plate carrier, helmet and helmet rim - were
  swept along Y instead of Z and therefore lay flat: the belt and the helmet rim read
  as discs, the torso as a slab on the operator's back, and the body was 1.9 m deep.
  `_verify_proportions` in the builder now fails the build on that class of mistake.
* The boot sole was longer than the boot, so every operator stood on a grey plate that
  stuck out in front of the toe and behind the heel. The sole is 0.215 m now, under
  the boot.
* The arms were authored crossing the waist, which read as hands on hips. They hang
  along the body with a relaxed bend, and the elbow joint moved with them.

### Tests

296 rules tests (289 + 2 + 5): the support hand stays on the fore-end with no reload,
reaches the magazine during one, and returns when it ends; a standing body does not
march, cadence follows speed over stride, the legs alternate and stay inside their
joints, a crouch folds the knees, and a killed body falls and stays down.

### Verification

`core-tests` run 40959332273 and the `v1.0.11` tag build are green: 296 core tests
passed, 111 scripts parsed with 0 syntax errors, the Unity side type checked against
the API stubs with 0 errors, and the design rule policy passed. `docs/art/operators.png`
and `docs/art/weapons-lineup.png` are rendered from the exported geometry with the
generated textures, and `docs/reports/` carries the reports of that run. A red run now
commits its own reports (`diagnose` job), because the Actions log download host is not
reachable from the machine this project is authored on.

## 1.0.10 - 2026-10-29

The weapon set was modelled a fourth time. The complaints on 1.0.9 were specific -
"holey", "flat", "not pretty" - so this pass attacked the three things that caused
them: parts that did not touch, faces with no detail on them, and a palette where
walnut, nylon and phosphate all rendered the same value of grey.

### Art

* **Placement is computed, not eyeballed.** Every weapon has one `layout()` table -
  receiver front/rear/top/bottom, bore line, grip station, magazine station, stock
  root - and every part is built from it. The grip, the magazine well, the trigger
  guard and the stock can no longer drift apart, and connected parts overlap by
  three to five millimetres on purpose: a two millimetre gap reads as a hole at
  viewmodel distance, which is what "holey" meant.
* **Surface detail that survives a render.** Receivers now carry raised panel
  frames - a machined border, not a pocket, because a shallow boolean on a lofted
  side can miss the wall and leave a floating plate - plus slotted screw heads, a
  bolt catch, a selector on both sides, a magazine release and a forward assist.
  The AK adds pressed receiver rivets and a channel; the shotgun a port plate and
  pins; the SMG moulded shell panels; the bolt action action screws.
* **Handguards.** M-LOK pockets are cut per side instead of punched through the
  tube, so a free float handguard no longer looks like a colander; they are two
  rows deep on the long guns. The walnut fore-end got a closed upper handguard over
  the gas tube, the pump a ribbed slide, and every handguard a barrel nut and a cap.
* **Grips and magazines are lofted.** A grip is a raked stack of rounded sections -
  0.86 of full width at the top, palm swell at the middle, flare at the butt - with
  finger grooves moulded into the front strap and stippled panels on the sides. The
  old grip was one side profile extruded through a constant width, which is why it
  read as a blade. Magazines are curved lofts with three transverse ribs, a lipped
  floor plate and a collar, and the witness holes are gone: they were the last
  deliberate holes in the set.
* **Stocks.** The AR stock rides on an exposed buffer tube with a castle nut, a
  clamp collar and an adjustment lever, so the butt is attached to the gun instead
  of glued to its back. The chassis stock is one solid body with a rounded
  thumbhole, a hinge block with a real pin and a cheek riser on posts - the previous
  version was two 11 mm plates with a 20 mm slot through them, i.e. a hole with a
  gun around it. The wire stock, the pump stock and the walnut AK stock were rebuilt
  against the receiver tang.
* **Sights and optics.** The AK front sight is a barrel band, a tower, two swept
  ears and a hooded post instead of a box with a hole; the gas block is a real
  block at the gas port with the tube leaving it rearwards. The red dot gained an
  objective hood, a rear eyepiece ring and a protected body; the prism and the scope
  kept their turrets and their coated lenses.
* **Colour ladder.** `gun_metal` is now dark phosphated steel (0.78 metallic, 0.38
  roughness, 0.34 albedo shade), `gun_bare` is bright machined steel at 1.0 metallic,
  `gun_polymer` near black nylon at 0.20 shade, `gun_olive` enamel at 0.36 and
  `gun_wood` oiled walnut at 0.46. On 1.0.9 every one of them sat within a few
  percent of the same value, which is why a walnut stock and a steel receiver looked
  like the same grey plastic.
* **Studio.** Key, rim, fill, kicker and top lights plus four soft boxes for
  reflections; the bench is placed under each weapon's own bounding box and falls to
  black in a metre and a half; framing is solved per weapon from the bounding box
  with the sensor aspect corrected.
* **261 640 triangles** across seven weapons and three grenades, from 200 434.

### Fixed

* `disc()` rotated about Y and then translated twice, so every QD socket sat half a
  metre behind its stock as a floating dot. It now builds a cylinder on the axis it
  is asked for and translates once.
* The trigger guard is swept stamped stock 11 mm thick instead of a 4.4 mm wire, and
  the magazine, guard and grip are ordered using the layout table rather than three
  hard coded offsets.
* `loft()` sections were fed `(axis, scale, scale, 0, curve)` for magazines, which
  displaces the curve along Y correctly; the old AK magazine was straight and 40 mm
  too long.
* Bipod legs pointed backwards along the barrel instead of folding down; the feet
  now sit under the muzzle end of the fore-end.

### Tests

Unchanged: 289 rules tests, the 16 viewmodel animation tests included.

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
