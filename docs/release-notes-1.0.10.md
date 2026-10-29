# KORDON 1.0.10

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

A modelling pass aimed at the three complaints on 1.0.9 - holey, flat, ugly. Parts
that did not touch, faces with nothing on them, and a palette where walnut, nylon
and phosphate all rendered the same value of grey.

### The weapons

Every weapon is placed from one `layout()` table: receiver front, rear, top and
bottom, bore line, grip station, magazine station, stock root. The grip, the
magazine well, the trigger guard and the stock are derived from those numbers, so
they cannot drift apart, and joined parts overlap by three to five millimetres on
purpose. Two millimetre gaps are what "holey" looked like.

* **Receivers** carry raised panel frames - a machined border rather than a pocket,
  because a shallow boolean on a lofted side can miss the wall and leave a floating
  plate - slotted screw heads, a bolt catch, a selector on both sides, a magazine
  release, a forward assist, a port plate. The AK gets pressed rivets and a stamped
  channel; the shotgun gets pins and a loading gate; the SMG gets moulded shell
  panels; the bolt action gets action screws and a real bolt handle.
* **Handguards** have per side M-LOK pockets instead of slots punched through the
  tube, two rows on the long guns, a barrel nut and a cap. The walnut fore-end got a
  closed upper handguard over the gas tube; the pump got a ribbed slide.
* **Grips** are lofted stacks of rounded sections - 0.86 width at the top, palm
  swell in the middle, flare at the butt - with moulded finger grooves and stippled
  side panels. The old grip was a side profile extruded through a constant width,
  which is exactly why it read as a blade.
* **Magazines** are curved lofts with three transverse ribs, a lipped floor plate, a
  collar and a latch. The witness holes are gone: they were the last deliberate
  holes in the set.
* **Stocks.** The AR stock rides on an exposed buffer tube with a castle nut, a clamp
  collar and an adjustment lever. The chassis stock is one solid body with a rounded
  thumbhole, a hinge block with a real pin and a cheek riser on posts - it used to be
  two 11 mm plates with a 20 mm slot through them, which is a hole with a gun around
  it. The wire stock, the pump stock and the walnut AK stock were rebuilt against the
  receiver tang.
* **Sights.** The AK front sight is a barrel band, a tower, two swept ears and a
  hooded post; the gas block is a block at the gas port with the tube leaving it
  rearwards. The red dot gained an objective hood, an eyepiece ring and a protected
  body.
* **261 640 triangles** across seven weapons and three grenades.

### The materials and the studio

* The value ladder was rebuilt, because on 1.0.9 everything sat within a few percent
  of the same grey. Dark phosphated steel (`gun_metal`, 0.78 metallic, 0.38
  roughness, 0.34 albedo shade), bright machined steel (`gun_bare`, 1.0 metallic),
  near black nylon (`gun_polymer`, 0.20 shade), enamel (`gun_olive`, 0.36) and oiled
  walnut (`gun_wood`, 0.46).
* Weapon maps repeat five to six times per UV island, with the normal strength held
  at 0.6: a stronger bump turns a receiver into stone at viewmodel distance.
* The review studio is key, rim, fill, kicker and top lights plus four soft boxes for
  reflections; the bench is placed under each weapon's own bounding box and falls to
  black within a metre and a half; framing is solved per weapon from the bounding box
  with the sensor aspect corrected, so no tile has the gun floating in dead space.

### Fixed

* `disc()` rotated about Y and translated twice, which put every QD socket half a
  metre behind its stock as a floating dot.
* The trigger guard is swept 11 mm stamped stock instead of 4.4 mm wire, and the
  magazine, guard and grip are ordered from the layout table rather than three hard
  coded offsets.
* Bipod legs pointed backwards along the barrel instead of folding down under the
  fore-end.
* `loft()` section offsets put the magazine curve on the wrong axis on the AK: the
  magazine was straight and 40 mm too long.

### Honest limits

There is no Unity editor, no artist and no sculpting tool in this environment. The
weapon renders in `docs/art/` are Cycles renders of the exported FBX wearing the
generated textures: they show geometry and materials, not the viewmodel in motion.
The models are authored procedurally, part by part, from primitives and lofts. They
now read as clean, connected game assets - no floating parts, no see-through frames,
materials that separate walnut from nylon from steel - but they are **not** scanned
or hand sculpted AAA art, and this sheet is not a substitute for an artist's pass.
Nothing on the animation side changed in this build: the 1.0.9 layered viewmodel
animator with its 16 tests is what runs.

### Verification

| Check | Result |
|---|---|
| Code | 109 scripts, engine free rules core, game layer, netcode layer |
| Rules tested | 289 unit tests |
| Art | 60 modular kit models, 7 weapons + 3 grenades (261 640 tris), 66 texture sets, 24 materials |
| Weapon export re-import + bounding box check | 10 files, 261 640 tris |
| Design rule checks | PASS |
| Windows binary | Not built here - see *Honest limits* in `docs/06_VERIFICATION.md` |

## Installing

Unity 6 LTS (`6000.0.32f1`) opens the project in the repository root. Wine or a
Windows machine is needed to build the player; the dedicated server is the same
project with the `Kordon.DedicatedServer` scene.
