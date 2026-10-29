# KORDON 1.0.11

A tactical first person shooter in the fictional Veransk corridor, made in Unity 6
for Windows. Free-for-All is the release mode: 20 operators, no teams, first to
**50 personal kills**. The 2-4 player co-op operation *Silent Kiln* ships with it.

## What changed in this build

The weapon has hands, and the other players have bodies.

### First person hands

Until now the first person weapon was held by nobody: a rifle floated in front of
the camera. Every weapon model now ships with the two gloved hands that hold it,
built in the weapon's own space and exported as separate nodes (`HandRight`,
`HandLeft`) inside the same FBX, so the viewmodel can animate them with no anchor
table and no per weapon offsets:

* the **right hand** closes on the pistol grip: a lofted palm, four fingers curling
  round the front strap onto the far side, the thumb over the top, knuckle pads,
  stitched seams, a wrist strap and a jacket cuff;
* the **left hand** cradles the fore-end at that weapon's own handguard radius, and
  it is animated: it stays on the fore-end while the weapon is ready, travels with
  the magazine during a reload, and returns when the reload ends;
* the **sidearm**'s support hand wraps the shooting hand; the **pump gun**'s right
  hand closes on the stock wrist, because it has no pistol grip.

Each finger is a swept tube through named joints with a rounded joint at every
knuckle and a rounded cap at the tip. It is authored anatomy, not a stack of boxes.

### Operator bodies

Every other player in a match was a Unity capsule and a sphere, and the co-op
militia were the same capsule tinted rust. There are now two authored bodies,
`operator.fbx` and `militia.fbx`, 1.73 m to the top of the helmet and 28 840
triangles each: boots, legs, knee pads, a belt with pouches, a plate carrier with
three magazine pouches and an admin pouch, side plates, a back panel carrying a
radio and an antenna, shoulders and arms with elbow pads, gloved fists, a helmet
with rails, ear cups and an NVG mount, goggles with dark lenses, and a rifle slung
across the back on a two point sling. The variants share geometry and differ in
kit: olive canvas and plate for the operator, worn brown canvas and steel for the
militia.

`RemoteOperatorView` loads the body and falls back to the old capsule only when the
art is missing, so a broken import cannot quietly turn the game back into grey
blocks. A 1.74 m capsule proxy - the authored body to the top of its helmet - gives
the server the hit volume the rules already assume (head above 1.5 m, thorax above
0.85 m), so what a player sees is what the server can hit, and a round that passes
over a helmet no longer counts as a headshot.

### Bodies that walk and fall

The body is not one welded mesh. `build_operators.py` joins and re-origins nine
parts onto hip, knee, shoulder and elbow centres and exports them under empties, so
the FBX carries a hierarchy a game can actually pose.

`Kordon.Core.Characters.BodyGait` decides how far each joint turns: cadence follows
speed over stride, so a walking body takes steps at a walking rate and a sprinting
body lengthens its stride; the knee folds while its leg travels forward and
straightens as the foot plants; arms counter swing at just over half the amplitude;
two bobs per cycle match the two foot plants. Five core tests cover it.

`OperatorBodyRig` applies that to the eight nodes, reading the body's own movement
so the legs match its real velocity, and it also owns death: a killed body pitches
forward over 0.85 s and stays where it fell instead of switching its renderers off
and vanishing.

### Materials

Five new surfaces: `glove_leather` (oiled leather with pebble grain, creases and
worn highlights), `uniform_canvas` and `militia_canvas` (ripstop with a visible
warp and weft, dust in the folds), `armor_plate` and `militia_steel` (composite
plate in a fabric cover, pitted and scuffed). 29 surfaces, 81 texture sets.

### Numbers

| Asset | Triangles |
|---|---|
| Seven weapons + three grenades (now with hands) | 375 388 |
| Operator body | 28 840 |
| Militia body | 28 840 |

### Honest limits

There is no Unity editor, no animator and no sculpting tool in this environment.
The hands and bodies are procedural geometry built from lofts and swept tubes: they
read as connected, correctly proportioned game assets, but they are **not** sculpted
or scanned AAA character art. The bodies have no armature and no skinned animation:
they are rigid parts on joint nodes, so they walk, crouch, swing their arms and fall
over, but they do not aim independently of their torso, they do not reload, and they
do not blend a walk into a run the way a motion captured rig would. A hit still
resolves as a damage
number, not as a ragdoll. The weapon and operator sheets in `docs/art/` are Cycles
renders of the exported FBX with the generated textures; they are not a play test.

### Verification

| Check | Result |
|---|---|
| Code | 109 scripts, engine free rules core, game layer, netcode layer |
| Rules tested | 296 unit tests (2 new: the support hand follows the magazine; 5 new: the body gait and the death fall) |
| Art | 60 kit modules, 7 weapons + 3 grenades + hands, 2 bodies, 81 texture sets, 29 materials |
| Export re-import + bounding box check | 10 weapon files, 2 body files; the body also checks its own proportions |
| Design rule checks | PASS |
| Windows binary | Not built here - see *Honest limits* in `docs/06_VERIFICATION.md` |

## Installing

Unity 6 LTS (`6000.0.32f1`) opens the project in the repository root. Wine or a
Windows machine is needed to build the player; the dedicated server is the same
project with the `Kordon.DedicatedServer` scene.
