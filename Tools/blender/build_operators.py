#!/usr/bin/env python3
"""Author the operator body: the soldier other players and the militia wear.

    Tools/blender/build_operators.py

Writes

    Assets/_Project/Art/Models/Resources/Kordon/Operators/operator.fbx
    Assets/_Project/Art/Models/Resources/Kordon/Operators/militia.fbx
    ArtSource/Blender/kordon_operators.blend
    Assets/StreamingAssets/Data/Art/operator_manifest.json

Until 1.0.11 every other player in a match was a Unity capsule and a sphere, and
the co-op militia were the same capsule tinted rust.  That is a grey block in a
finished game: it is the silhouette every opponent sees, and the hit zones the
server resolves are read off it.  This builds a real operator instead.

Conventions, identical to the kit and the maps so MapSpace keeps working:

* metres, Z up, origin at the centre of the footprint on the ground;
* the operator faces Blender +Y, which is Unity +Z at yaw 0;
* 1.82 m tall, 0.62 m across the shoulders, matching the operator contract the
  gameplay rules already use for spawn clearance and hit zones.

The two variants share geometry and differ in surface: ``operator`` is an
operator in the corridor's own kit, ``militia`` wears the co-op enemy's older
canvas and steel.
"""
from __future__ import annotations

import math
import os
import sys
from typing import Dict, List, Sequence, Tuple

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import build_weapons as bw  # noqa: E402
import build_hands as bh  # noqa: E402
import kordon_art as art  # noqa: E402

OUT_DIR = os.path.join(art.REPO_ROOT, "Assets", "_Project", "Art", "Models",
                       "Resources", "Kordon", "Operators")
MANIFEST = os.path.join(art.DATA_DIR, "Art", "operator_manifest.json")

HEIGHT = 1.82
SHOULDER = 0.62

UNIFORM = "uniform_canvas"
ARMOR = "armor_plate"
LEATHER = "glove_leather"
RUBBER = "rubber"
METAL = "gun_metal"
BARE = "gun_bare"
GLASS = "glass"


# --------------------------------------------------------------------------
# Limbs and body volumes
# --------------------------------------------------------------------------
def limb(name: str, points: Sequence[Sequence[float]], radii: Sequence[float],
         surface: str, segments: int = 12) -> bpy.types.Object:
    """A limb: a swept tube with rounded joints at both ends."""
    return bh.chain(name, points, radii, surface, segments=segments)


def pad(name: str, centre: Sequence[float], radii: Sequence[float],
        surface: str = ARMOR) -> bpy.types.Object:
    """An armour pad: a soft slab that wraps the limb it protects."""
    return bh.blob(name, centre, radii, surface)


def slab(name: str, size: Sequence[float], centre: Sequence[float],
         surface: str = ARMOR, rot: Sequence[float] = (0.0, 0.0, 0.0),
         bevel: float = 0.006) -> bpy.types.Object:
    return bw.box(name, size, centre, surface=surface, rot=rot, bevel=bevel)


# --------------------------------------------------------------------------
# The body
# --------------------------------------------------------------------------
def _side_tag(side: float) -> str:
    return "l" if side < 0.0 else "r"


# Joint centres in body space.  Every articulated mesh is authored exactly as
# before and then re-origined on one of these points, so the exported hierarchy
# turns about the joint a person actually turns about.
JOINTS: Dict[str, Tuple[float, float, float]] = {
    "thigh_l": (-0.118, 0.004, 0.880),
    "thigh_r": (0.118, 0.004, 0.880),
    "shin_l": (-0.118, 0.008, 0.490),
    "shin_r": (0.118, 0.008, 0.490),
    "upperarm_l": (-0.2042, 0.005, 1.336),
    "upperarm_r": (0.2042, 0.005, 1.336),
    "forearm_l": (-0.2131, 0.030, 1.075),
    "forearm_r": (0.2131, 0.030, 1.075),
}

PARENTS: Dict[str, str] = {
    "shin_l": "thigh_l",
    "shin_r": "thigh_r",
    "forearm_l": "upperarm_l",
    "forearm_r": "upperarm_r",
}

# group -> (mesh name Unity sees, joint empty name Unity sees)
GROUP_NAMES: Dict[str, Tuple[str, str]] = {
    "body": ("Body", ""),
    "thigh_l": ("ThighL", "HipL"),
    "thigh_r": ("ThighR", "HipR"),
    "shin_l": ("ShinL", "KneeL"),
    "shin_r": ("ShinR", "KneeR"),
    "upperarm_l": ("UpperArmL", "ShoulderL"),
    "upperarm_r": ("UpperArmR", "ShoulderR"),
    "forearm_l": ("ForearmL", "ElbowL"),
    "forearm_r": ("ForearmR", "ElbowR"),
}

GROUP_ORDER = ("body", "thigh_l", "thigh_r", "shin_l", "shin_r",
               "upperarm_l", "upperarm_r", "forearm_l", "forearm_r")


def _empty(name: str, location: Sequence[float]) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=tuple(location))
    empty = bpy.context.view_layer.objects.active
    empty.name = name
    empty.empty_display_size = 0.05
    return empty


def _assemble(groups: Dict[str, List[bpy.types.Object]], variant: str):
    """Join each group, re-origin it on its joint, and build the bone hierarchy.

    The body is not rigged with an armature: it is a set of rigid parts parented
    to empties that sit exactly on the hip, knee, shoulder and elbow centres.
    That is what makes the body animate - a walking soldier whose legs swing from
    the hip - without a skinning pass that cannot be verified without an editor.
    Rounded joints and the knee pads close the seam at every pivot.
    """
    root = _empty("OperatorRoot", (0.0, 0.0, 0.0))
    root.empty_display_size = 0.2
    meshes: List[bpy.types.Object] = []
    mesh_by_key: Dict[str, bpy.types.Object] = {}
    joints: Dict[str, bpy.types.Object] = {}

    for key in GROUP_ORDER:
        parts = groups.get(key, [])
        if not parts:
            continue
        mesh_name, joint_name = GROUP_NAMES[key]
        mesh = art.join(parts, mesh_name)
        art.bevel(mesh, width=0.0035, segments=2, angle_deg=32.0)
        _smooth(mesh)
        art.unwrap(mesh, island_margin=0.003)
        meshes.append(mesh)
        if key == "body":
            mesh_by_key[key] = mesh
            mesh.parent = root
            mesh.matrix_parent_inverse = Matrix.Identity(4)
            mesh.location = (0.0, 0.0, 0.0)
            continue

        # origin on the joint: the vertices are baked back into joint space, so a
        # rotation of the empty swings the part about the joint and nothing else.
        joint = Vector(JOINTS[key])
        mesh.data.transform(Matrix.Translation(-joint))
        joints[key] = joint_name
        mesh.name = mesh_name
        mesh_by_key[key] = mesh

    # empty hierarchy first, then attach the meshes to their joints
    empties: Dict[str, bpy.types.Object] = {}
    for key in GROUP_ORDER:
        if key == "body" or key not in joints:
            continue
        parent_key = PARENTS.get(key)
        base = Vector(JOINTS[parent_key]) if parent_key else Vector((0.0, 0.0, 0.0))
        local = Vector(JOINTS[key]) - base
        empty = _empty(joints[key], (0.0, 0.0, 0.0))
        empty.parent = empties.get(parent_key, root)
        empty.matrix_parent_inverse = Matrix.Identity(4)
        empty.location = local
        empties[key] = empty

    for key in GROUP_ORDER:
        if key == "body" or key not in joints:
            continue
        mesh = mesh_by_key[key]
        mesh.parent = empties[key]
        mesh.matrix_parent_inverse = Matrix.Identity(4)
        mesh.location = (0.0, 0.0, 0.0)

    bpy.context.view_layer.update()
    exported: List[bpy.types.Object] = [root] + list(empties.values()) + meshes
    bounds = bw.world_bounds(meshes)
    _verify_proportions(variant, bounds)
    meta = dict(
        id=variant,
        heightM=round(bounds[1][2] - bounds[0][2], 3),
        widthM=round(bounds[1][0] - bounds[0][0], 3),
        depthM=round(bounds[1][1] - bounds[0][1], 3),
        triangles=sum(sum(max(0, len(p.vertices) - 2) for p in m.data.polygons)
                      for m in meshes),
        parts=[m.name for m in meshes],
        joints=[joints[k] for k in GROUP_ORDER if k in joints],
    )
    return root, exported, meta


def _verify_proportions(variant: str, bounds) -> None:
    """Refuse to export a body whose volumes are lying down.

    ``bw.loft`` sweeps along Y by default, so a torso authored as a stack of rings in
    the horizontal plane is a slab on its back unless it is told axis="Z".  That
    mistake shipped once: the belt, hips, torso, carrier and helmet all read as flat
    discs, and the body was 1.9 m deep.  A human silhouette has hard numbers, so the
    build checks them instead of trusting the next edit.
    """
    lo, hi = bounds
    height = hi[2] - lo[2]
    depth = hi[1] - lo[1]
    width = hi[0] - lo[0]
    problems = []
    if lo[2] < -0.02:
        problems.append(f"geometry {lo[2]:.3f} m below the ground plane")
    if not (1.70 <= height <= 1.86):
        problems.append(f"height {height:.3f} m is not an adult operator")
    if depth > 0.78:
        problems.append(f"depth {depth:.3f} m: a volume is lying on its back")
    if not (0.52 <= width <= 0.72):
        problems.append(f"width {width:.3f} m is not a pair of shoulders")
    if problems:
        raise RuntimeError(f"{variant}: " + "; ".join(problems))


def build_operator(variant: str = "operator"):
    """Build one operator standing at attention, rifle slung across the back."""
    militia = variant == "militia"
    uniform = "militia_canvas" if militia else UNIFORM
    armor = "militia_steel" if militia else ARMOR
    groups: Dict[str, List[bpy.types.Object]] = {key: [] for key in GROUP_ORDER}

    def add(key: str, obj: bpy.types.Object) -> None:
        groups[key].append(obj)

    # ---- feet and legs ---------------------------------------------------
    for side in (-1.0, 1.0):
        tag = _side_tag(side)
        x = side * 0.118
        # boot: a sole slab and an upper that rises over the ankle
        add("shin_" + tag, slab("boot_sole", (0.112, 0.215, 0.024),
                                (x, -0.031, 0.012), surface=RUBBER, bevel=0.006))
        boot = bw.loft(bw.round_rect(0.052, 0.064, 0.028, steps=6), [
            (-0.120, 0.86, 0.72, 0.0, 0.062),
            (-0.070, 1.00, 0.96, 0.0, 0.070),
            (0.010, 1.02, 1.04, 0.0, 0.086),
            (0.058, 0.96, 0.92, 0.0, 0.100),
        ], surface=LEATHER, name="boot", axis="Y")
        boot.data.transform(Matrix.Translation((x, 0.0, 0.0)))
        add("shin_" + tag, boot)
        # shin, knee, thigh: three volumes with the knee between them
        add("shin_" + tag,
            limb("shin", [(x, 0.010, 0.112), (x, 0.012, 0.300), (x, 0.006, 0.470)],
                 [0.054, 0.050, 0.060], uniform))
        add("shin_" + tag,
            pad("knee_pad", (x, -0.026, 0.490), (0.050, 0.046, 0.058), armor))
        add("thigh_" + tag,
            limb("thigh", [(x, 0.004, 0.500), (x, 0.000, 0.690), (x, -0.002, 0.880)],
                 [0.066, 0.078, 0.086], uniform))
        # drop-leg holster on the right thigh
        if side > 0:
            add("thigh_" + tag, slab("holster", (0.070, 0.100, 0.190),
                                     (x + 0.075, -0.010, 0.660), surface=LEATHER,
                                     bevel=0.010))
            add("thigh_" + tag, slab("holster_strap", (0.086, 0.032, 0.020),
                                     (x + 0.060, -0.010, 0.740), surface=RUBBER,
                                     bevel=0.006))

    # ---- hips and belt ---------------------------------------------------
    # pelvis: taller than it is wide, so it reads as hips rather than a skirt
    add("body", bw.loft(bw.round_rect(0.138, 0.100, 0.052, steps=8), [
        (0.835, 0.86, 0.90, 0.0, 0.0),
        (0.875, 0.98, 1.00, 0.0, 0.0),
        (0.930, 1.04, 1.02, 0.0, 0.0),
        (0.985, 1.02, 1.00, 0.0, 0.0),
        (1.018, 0.96, 0.94, 0.0, 0.0),
    ], surface=uniform, name="hips", axis="Z"))
    # The belt is a ring in the horizontal plane. bw.band() sweeps along Y, which
    # would lay a leather disc across the operator's waist, so it is lofted here.
    add("body", bw.loft(bw.round_rect(0.158, 0.114, 0.062, steps=8), [
        (0.952, 1.03, 1.03, 0.0, 0.0),
        (0.966, 1.05, 1.05, 0.0, 0.0),
        (0.998, 1.05, 1.05, 0.0, 0.0),
        (1.010, 1.02, 1.02, 0.0, 0.0),
    ], surface=LEATHER, name="belt", axis="Z"))
    # belt kit: magazine pouch, dump pouch, radio pouch
    for x, size in ((-0.120, (0.070, 0.060, 0.090)), (0.120, (0.060, 0.055, 0.085))):
        add("body", slab("belt_pouch", size, (x, -0.130, 0.960), surface=uniform))
    add("body", slab("dump_pouch", (0.090, 0.075, 0.100),
                     (0.040, 0.150, 0.955), surface=uniform))

    # ---- torso: waist, ribcage, chest, collar ----------------------------
    add("body", bw.loft(bw.round_rect(0.150, 0.108, 0.062, steps=8), [
        (0.985, 0.94, 0.88, 0.0, 0.0),
        (1.060, 1.02, 0.98, 0.0, 0.0),
        (1.170, 1.14, 1.04, 0.0, 0.002),
        (1.280, 1.22, 1.06, 0.0, 0.006),
        (1.360, 1.18, 1.00, 0.0, 0.010),
        (1.410, 0.90, 0.84, 0.0, 0.012),
    ], surface=uniform, name="torso", axis="Z"))
    add("body", limb("neck", [(0.0, 0.012, 1.400), (0.0, 0.012, 1.480)],
                     [0.055, 0.050], uniform))

    # ---- plate carrier ---------------------------------------------------
    add("body", bw.loft(bw.round_rect(0.158, 0.114, 0.058, steps=8), [
        (1.050, 0.96, 0.90, 0.0, 0.0),
        (1.110, 1.02, 1.00, 0.0, 0.0),
        (1.230, 1.06, 1.04, 0.0, 0.006),
        (1.330, 1.02, 0.98, 0.0, 0.010),
        (1.380, 0.88, 0.84, 0.0, 0.010),
    ], surface=armor, name="carrier", axis="Z"))
    # front pouches: three magazine pouches and an admin pouch
    for index in range(3):
        add("body", slab("mag_pouch", (0.052, 0.040, 0.130),
                         (-0.055 + index * 0.055, -0.150, 1.145), surface=uniform,
                         bevel=0.010))
        add("body", slab("pouch_flap", (0.056, 0.014, 0.036),
                         (-0.055 + index * 0.055, -0.166, 1.196), surface=uniform,
                         bevel=0.006))
    add("body", slab("admin_pouch", (0.120, 0.026, 0.070),
                     (0.0, -0.152, 1.268), surface=uniform, bevel=0.008))
    # side plate and shoulder straps
    for side in (-1.0, 1.0):
        add("body", slab("side_plate", (0.018, 0.116, 0.140),
                         (side * 0.166, 0.0, 1.170), surface=armor))
        add("body", slab("carrier_strap", (0.048, 0.028, 0.170),
                         (side * 0.088, -0.108, 1.340), surface=uniform,
                         rot=(0.0, side * 12.0, 0.0), bevel=0.008))
    # back panel with a radio and an antenna
    add("body", slab("back_panel", (0.220, 0.036, 0.230),
                     (0.0, 0.150, 1.190), surface=armor, bevel=0.010))
    add("body", slab("radio", (0.090, 0.055, 0.140),
                     (-0.085, 0.190, 1.190), surface=METAL, bevel=0.008))
    add("body", bh.chain("antenna",
                         [(-0.085, 0.196, 1.255), (-0.090, 0.188, 1.380),
                          (-0.096, 0.180, 1.470)],
                         [0.0055, 0.0045, 0.0038], BARE, segments=8))

    # ---- rifle slung on the back ----------------------------------------
    for piece in _rifle():
        add("body", piece)

    # ---- arms: shoulder to elbow to the wrist the hand is authored at ----
    for side in (-1.0, 1.0):
        tag = _side_tag(side)
        sx = side * 0.222
        shoulder = Vector((sx * 0.92, 0.005, 1.336))
        add("upperarm_" + tag, pad("shoulder", (shoulder.x, shoulder.y, shoulder.z),
                                   (0.066, 0.076, 0.064), uniform))
        # elbow back and out, forearm angled forward: a relaxed ready carry
        elbow = Vector((sx * 0.96, 0.030, 1.075))
        add("upperarm_" + tag,
            limb("upper_arm", [shoulder, (shoulder + elbow) * 0.5, elbow],
                 [0.062, 0.056, 0.048], uniform))
        add("forearm_" + tag, pad("elbow_pad", (elbow.x, elbow.y - 0.010, elbow.z),
                                  (0.046, 0.048, 0.046), armor))
        wrist = Vector((sx * 0.88, -0.040, 0.858))
        mid = (elbow + wrist) * 0.5 + Vector((0.008 * side, -0.010, -0.006))
        add("forearm_" + tag, limb("forearm", [elbow, mid, wrist],
                                   [0.050, 0.046, 0.040], uniform))
        # fist, in a glove, turned to face the body
        add("forearm_" + tag,
            bh.blob("fist", (wrist.x, wrist.y - 0.030, wrist.z - 0.004),
                    (0.040, 0.052, 0.046), LEATHER))
        cuff = bw.band("glove_cuff", 0.0, 0.046, 0.016, 0.0,
                       surface=LEATHER, segments=18)
        cuff.data.transform(Matrix.Rotation(math.radians(90.0), 4, "X"))
        cuff.data.transform(Matrix.Translation(
            (wrist.x, wrist.y + 0.012, wrist.z + 0.006)))
        add("forearm_" + tag, cuff)

    # ---- head and helmet -------------------------------------------------
    add("body", bh.blob("head", (0.0, -0.005, 1.575), (0.079, 0.092, 0.102), uniform))
    # balaclava face, goggles and a helmet over the top
    add("body", bh.blob("goggles", (0.0, -0.070, 1.596), (0.076, 0.036, 0.034), armor))
    add("body", bh.blob("goggle_lens", (0.0, -0.094, 1.598), (0.066, 0.016, 0.026),
                        GLASS))
    add("body", bw.loft(bw.round_rect(0.098, 0.106, 0.052, steps=8), [
        (1.560, 1.02, 1.02, 0.0, 0.0),
        (1.610, 1.04, 1.04, 0.0, -0.004),
        (1.660, 0.96, 0.98, 0.0, -0.008),
        (1.700, 0.80, 0.84, 0.0, -0.012),
        (1.726, 0.46, 0.50, 0.0, -0.014),
    ], surface=armor, name="helmet", axis="Z"))
    add("body", bw.loft(bw.round_rect(0.100, 0.108, 0.050, steps=8), [
        (1.548, 1.02, 1.02, 0.0, 0.0),
        (1.556, 1.06, 1.06, 0.0, 0.0),
        (1.572, 1.06, 1.06, 0.0, 0.0),
        (1.580, 1.01, 1.01, 0.0, 0.0),
    ], surface=armor, name="helmet_rim", axis="Z"))
    for side in (-1.0, 1.0):
        add("body", slab("helmet_rail", (0.014, 0.110, 0.018),
                         (side * 0.100, -0.010, 1.612), surface=METAL, bevel=0.004))
        add("body", bh.blob("ear_cup", (side * 0.096, 0.010, 1.545),
                            (0.022, 0.040, 0.040), METAL))
    add("body", slab("nvg_mount", (0.048, 0.030, 0.040),
                     (0.0, -0.106, 1.660), surface=METAL, bevel=0.006))
    add("body", slab("counterweight", (0.090, 0.036, 0.060),
                     (0.0, 0.112, 1.640), surface=uniform, bevel=0.010))

    return _assemble(groups, variant)


def _rifle() -> List[bpy.types.Object]:
    """A compact rifle held across the chest: silhouette at gameplay range.

    Deliberately far cheaper than the first person weapon - a remote operator is
    seen at ten metres, not through the sights - but it is a rifle, not a stick:
    receiver, handguard, magazine, stock, optic and muzzle.
    """
    parts: List[bpy.types.Object] = []
    y0, z0 = -0.170, 1.055
    # receiver and handguard, angled across the body toward the left hand
    receiver = bw.loft(bw.chamfer_rect(0.026, 0.030, 0.006), [
        (y0 + 0.180, 1.0, 1.0, 0.0, z0),
        (y0 + 0.020, 1.0, 1.0, 0.0, z0),
        (y0 - 0.030, 0.92, 0.94, 0.0, z0),
    ], surface=METAL, name="rifle_receiver")
    parts.append(receiver)
    parts.append(bw.loft(bw.round_rect(0.024, 0.026, 0.010, steps=6), [
        (y0 - 0.030, 1.0, 1.0, 0.0, z0 + 0.002),
        (y0 - 0.120, 0.98, 0.96, 0.0, z0 + 0.002),
        (y0 - 0.200, 0.92, 0.90, 0.0, z0 + 0.002),
    ], surface=ARMOR, name="rifle_handguard"))
    parts.append(bw.tube("rifle_barrel", [(y0 - 0.200, 0.011), (y0 - 0.320, 0.009)],
                         z0 + 0.004, surface=METAL, bore=0.005, segments=14))
    parts.append(bw.tube("rifle_muzzle", [(y0 - 0.320, 0.014), (y0 - 0.352, 0.014)],
                         z0 + 0.004, surface=METAL, bore=0.007, segments=14))
    parts.append(bw.loft(bw.chamfer_rect(0.020, 0.052, 0.008), [
        (y0 - 0.026, 1.0, 1.0, 0.0, z0 - 0.046),
        (y0 - 0.070, 1.0, 1.0, 0.0, z0 - 0.070),
    ], surface=METAL, name="rifle_mag", axis="Y"))
    parts.append(bw.box("rifle_grip", (0.026, 0.036, 0.084),
                        (0.0, y0 + 0.090, z0 - 0.060), surface=ARMOR,
                        rot=(18.0, 0.0, 0.0), bevel=0.006))
    parts.append(bw.box("rifle_stock", (0.030, 0.130, 0.062),
                        (0.0, y0 + 0.240, z0 - 0.004), surface=ARMOR, bevel=0.008))
    parts.append(bw.tube("rifle_optic", [(y0 + 0.070, 0.020), (y0 + 0.010, 0.020)],
                         z0 + 0.048, surface=METAL, segments=16))
    parts.append(bw.box("rifle_mount", (0.030, 0.040, 0.026),
                        (0.0, y0 + 0.040, z0 + 0.030), surface=METAL, bevel=0.004))
    # The rifle is slung across the back, muzzle down.  A rifle held at the ready
    # would look better, but then the arms have to reach a moving pose from a
    # static mesh: a slung weapon is what a soldier actually looks like walking,
    # and it cannot drift out of the hands because there are none on it.
    rifle = art.join(parts, "rifle")
    grip_point = Vector((0.0, y0 + 0.090, z0 - 0.060))
    rifle.data.transform(Matrix.Translation(-grip_point))
    # Lie the rifle across the back, not out behind it.  Rotating about Y first only
    # spun it about its own barrel, which is why it used to stick 0.44 m out of the
    # operator's spine: the pitch has to come first, then the diagonal across the
    # shoulder blades, so the weapon's long axis ends up in the plane of the back.
    rifle.data.transform(Matrix.Rotation(math.radians(-78.0), 4, "X"))
    rifle.data.transform(Matrix.Rotation(math.radians(-26.0), 4, "Y"))
    rifle.data.transform(Matrix.Translation((0.0, 0.205, 1.105)))
    # The sling is authored in body space, not weapon space, so it is returned
    # separately: joining it with the rifle would rotate it with the weapon and
    # leave it lying on the floor next to the operator.
    sling = [
        bh.chain("sling_a",
                 [(-0.130, 0.055, 1.330), (-0.090, 0.150, 1.210),
                  (-0.010, 0.200, 1.135)],
                 [0.020, 0.018, 0.017], LEATHER, segments=8),
        bh.chain("sling_b",
                 [(0.135, 0.060, 1.320), (0.105, 0.165, 1.080),
                  (0.060, 0.205, 0.960)],
                 [0.020, 0.018, 0.016], LEATHER, segments=8),
    ]
    return [rifle] + sling


def _smooth(obj: bpy.types.Object, angle_deg: float = 34.0) -> None:
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    modifier = obj.modifiers.new("KordonSmooth", "EDGE_SPLIT")
    modifier.split_angle = math.radians(angle_deg)
    art.apply_modifiers(obj)


# --------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------
def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    entries: List[dict] = []
    for variant in ("operator", "militia"):
        art.reset_scene()
        root, objects, meta = build_operator(variant)
        path = os.path.join(OUT_DIR, variant + ".fbx")
        bounds = bw.world_bounds([o for o in objects if o.type == "MESH"])
        size = art.export_fbx(objects, path)
        triangles = bw.verify_export(path, bounds)
        meta["triangles"] = triangles
        meta["bytes"] = size
        meta["model"] = "Kordon/Operators/" + variant
        entries.append(meta)
        print(f"  {variant:10s} {triangles:6d} tris  {size / 1024:7.1f} KB  "
              f"{meta['heightM']} m tall  {len(meta['parts'])} parts")

    payload = {
        "comment": "Player and militia bodies authored by Tools/blender/build_operators.py. "
                   "Origin at the footprint centre on the ground, facing Blender +Y "
                   "(Unity +Z at yaw 0), 1.82 m tall, matching the operator contract.",
        "operators": entries,
    }
    art.write_json(MANIFEST, payload)

    art.reset_scene()
    for index, variant in enumerate(("operator", "militia")):
        root, _objects, _meta = build_operator(variant)
        root.location = (index * 1.6, 0.0, 0.0)
        bpy.context.view_layer.update()
    blend_path = os.path.join(art.BLEND_DIR, "kordon_operators.blend")
    art.save_blend(blend_path)
    print(f"blend: {blend_path}")
    print(f"operators: {len(entries)} files, "
          f"{sum(e['triangles'] for e in entries)} triangles")
    return 0


if __name__ == "__main__":
    sys.exit(main())
