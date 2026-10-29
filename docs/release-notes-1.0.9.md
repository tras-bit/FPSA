# KORDON 1.0.9

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

The first person weapon stopped being a single lerp and became an animation system,
and the weapon art was rebuilt a third time - this time as the parts the real guns
are made of, wearing metal, wood and polymer that actually respond to light.

### The viewmodel

The old viewmodel smoothed one position toward one target. That is why the weapon
slid between poses, the magazine teleported and the muzzle never settled. The
behaviour now lives in `Kordon.Core.Weapons.ViewmodelAnimator`, an engine free
class with 16 tests, and it composes six layers every frame:

| Layer | What it does |
|---|---|
| State pose | Hip, sights, sprint, magazine out, magazine in, charging, inspect, fumble and empty each own a pose, blended with a per state rate: the sights come up in about 120 ms, a reload settles slowly |
| Sway | The look rate drives a critically damped spring, so the weapon lags a fast turn by a few frames and returns to centre on its own |
| Bob | A figure eight tied to the footfall phase, scaled by speed and damped to 15 percent while aiming |
| Breathing | A slow drift that grows with the stamina model's breath value - the visible half of stamina to aim |
| Recoil | An impulse that kicks the weapon back and up, then a spring with real overshoot brings it down |
| Landing | A dip and a muzzle climb on touchdown, fed by the player's landing event |

The magazine has its own timeline: it drops out (quadratic fall and a roll), is
hidden, and the fresh one rises, overshoots and seats at exactly zero offset.
Inspect pulls the magazine out, turns it 62 degrees toward the eye and holds it
there - which is the beat the HUD is allowed to show a round count on. The charging
handle racks and runs home.

`WeaponViewmodel` is now the adapter: it reads aim, sprint, speed, breath, look
rate and landing from the player, forwards every ammunition beat, measures the
loaded FBX so each weapon aims down its own sights, and writes the pose onto the
weapon root, the magazine prop and the bolt. The magazine prop is exported with its
pivot at the top of the well, so it rotates out of the magwell instead of swinging
around the grip.

### The weapons

Seven weapons rebuilt, 200 434 triangles with the grenades:

* Receivers are lofted shells with a tapered nose, a dust cover with pressed ribs,
  a real ejection port - a boolean pocket with the bolt carrier visible inside - a
  selector, rivets and pins instead of blank side panels.
* The trigger guard is a tube swept along a polyline, with a curved trigger blade
  inside it, spanning the gap between the magazine and the grip.
* Furniture is profiled: the AK fore-end is a walnut lower with a belly, finger
  grooves and a separate upper over a real gas tube; the AR handguard is a free
  float tube with recessed M-LOK panels, a top rail and an accessory rail; stocks
  carry a comb, a cheek riser, a recoil pad and a sling slot.
* Optics are hollow tubes with a mount, a hood, turrets and a dark aperture behind
  glass: a red dot on the carbine, a prism on the marksman rifle, a three turret
  scope on the bolt gun, which also keeps a folded bipod under the fore-end.
* Five weapon surfaces with their own generated maps - `gun_metal`, `gun_bare`,
  `gun_wood`, `gun_olive`, `gun_polymer` - so phosphated steel is not galvanised
  sheet and walnut is not pallet wood. 24 surfaces, 66 texture sets.
* Every mesh is beveled, shaded smooth with split normals at 34 degrees and UV
  unwrapped, and the weapon maps repeat 4-5 times per island: a wall texture at
  1:1 on a receiver reads as hammered cast iron.

### Fixed

* Regenerating the texture set silently dropped `textureType: 1` from every normal
  map `.meta`, which would have imported all normal maps as colour. The generator
  writes it now, and the policy check that caught it stays.
* `WeaponViewmodel` bound the player's landing event in `Awake`, before the operator
  assigned the player, so the event never fired. It now binds in `Start`.
* The sidearm's slide carried its ejection port as a wide black notch; it is a
  pocket with the barrel visible inside now.

### Honest limits

This environment has no Unity editor and no artist. The weapon renders in
`docs/art/` are Cycles renders of the exported FBX wearing the generated textures -
they show the geometry and the materials, not the viewmodel in motion. The
animation is verified by unit tests against fixed inputs (settling times, overshoot,
whether the magazine leaves the well before the hand moves), not by a play test.
The models are authored procedurally part by part; they read as clean game assets
at viewmodel distance, not as scanned or hand sculpted AAA art, and the release
notes say so rather than pretending otherwise.

### Verification

| Check | Result |
|---|---|
| Code | 109 scripts, engine free rules core, game layer, netcode layer |
| Rules tested | 289 unit tests |
| Art | 60 modular kit models, 7 weapons + 3 grenades (200 434 tris), 66 texture sets, 24 materials |
| Weapon export re-import + bounding box check | 10 files, 200 434 tris |
| Design rule checks | PASS |

The full report is `docs/06_VERIFICATION.md`.
