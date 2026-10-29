#!/usr/bin/env python3
"""Author the first person gloved hands and attach them to every weapon build.

    Tools/blender/build_hands.py       (imported by build_weapons.py)

The weapon models were held by nothing: a rifle floated in front of the camera.
This module builds the two hands that hold it - a right hand closing on the pistol
grip and a left hand cradling the fore-end - as gloved geometry, in the weapon's own
space, and hands the finished objects back to the weapon exporter so they travel
inside each weapon FBX as the nodes ``HandRight`` and ``HandLeft``.

Why in the weapon's space instead of a separate rig: the viewmodel animates the
weapon root, the magazine prop and the bolt as transforms.  Authoring the hands
where they belong for *this* weapon (this grip rake, this handguard radius) means
the game needs no anchor table, no axis guessing and no per weapon offsets - and
the hands cannot drift off the grip when a weapon is re-proportioned.

Anatomy is authored from joint tables, not from boxes: every finger is three
phalanges between named joints with a rounded joint at each knuckle, the palm is a
slab whose long axis runs wrist to knuckles, and the two hands are mirrored.  That
is what reads as a hand at viewmodel distance.  It is not a scanned hand.
"""
from __future__ import annotations

import math
from typing import List, Sequence, Tuple

import bpy
from mathutils import Matrix, Vector

import build_weapons as bw
import kordon_art as art

GLOVE = "glove_leather"
PAD = "rubber"
CUFF = "gun_polymer"   # jacket sleeve: dark nylon, not a painted tube


# --------------------------------------------------------------------------
# Primitives
# --------------------------------------------------------------------------
def chain(name: str, points: Sequence[Sequence[float]], radii: Sequence[float],
          surface: str, segments: int = 10) -> bpy.types.Object:
    """A tapered tube through a polyline, with rounded ends.

    Frames are parallel transported (each ring's normal is projected onto the plane
    perpendicular to the new tangent), which is what keeps a curled finger from
    twisting inside out at the knuckle.
    """
    pts = [Vector(p) for p in points]
    count = len(pts)
    tangents: List[Vector] = []
    for index in range(count):
        if index == 0:
            tangent = pts[1] - pts[0]
        elif index == count - 1:
            tangent = pts[-1] - pts[-2]
        else:
            tangent = pts[index + 1] - pts[index - 1]
        if tangent.length < 1e-9:
            tangent = Vector((0.0, 1.0, 0.0))
        tangents.append(tangent.normalized())

    reference = Vector((0.0, 0.0, 1.0))
    if abs(tangents[0].dot(reference)) > 0.9:
        reference = Vector((1.0, 0.0, 0.0))
    normal = (reference - tangents[0] * reference.dot(tangents[0])).normalized()

    verts: List[Tuple[float, float, float]] = []
    rings: List[int] = []
    for index, point in enumerate(pts):
        tangent = tangents[index]
        normal = (normal - tangent * normal.dot(tangent))
        if normal.length < 1e-7:
            normal = tangent.cross(Vector((1.0, 0.0, 0.0)))
        normal.normalize()
        binormal = tangent.cross(normal).normalized()
        radius = radii[min(index, len(radii) - 1)]
        rings.append(len(verts))
        for step in range(segments):
            angle = 2.0 * math.pi * step / segments
            offset = (normal * (math.cos(angle) * radius)
                      + binormal * (math.sin(angle) * radius))
            verts.append(tuple(point + offset))

    faces: List[tuple] = []
    for ring in range(count - 1):
        first, second = rings[ring], rings[ring + 1]
        for step in range(segments):
            following = (step + 1) % segments
            faces.append((first + step, first + following,
                          second + following, second + step))

    for end in (0, count - 1):
        radius = radii[0 if end == 0 else len(radii) - 1]
        direction = (tangents[0] * -1.0) if end == 0 else tangents[-1]
        tip = pts[0] if end == 0 else pts[-1]
        ring = rings[end]
        cap_start = len(verts)
        for step in range(segments):
            base = Vector(verts[ring + step])
            offset = (base - tip) * 0.55 + direction * radius * 0.62
            verts.append(tuple(tip + offset))
        centre = len(verts)
        verts.append(tuple(tip + direction * radius * 0.95))
        for step in range(segments):
            following = (step + 1) % segments
            if end == 0:
                faces.append((ring + step, ring + following,
                              cap_start + following, cap_start + step))
                faces.append((cap_start + step, cap_start + following, centre))
            else:
                faces.append((ring + following, ring + step,
                              cap_start + step, cap_start + following))
                faces.append((cap_start + following, cap_start + step, centre))

    obj = bw.mesh_object(name, verts, faces, surface)
    bw.recalc(obj)
    return obj


def blob(name: str, centre: Sequence[float], radii: Sequence[float],
         surface: str) -> bpy.types.Object:
    """A rounded lump (knuckle, heel of the hand, thumb base): a lofted sphere."""
    # profile is in metres, so the loft is already the right size; the sections
    # only taper it into a lump
    profile = bw.round_rect(radii[0], radii[1], min(radii) * 0.55, steps=8)
    sections = []
    steps = 7
    for index in range(steps):
        t = index / (steps - 1)
        scale = math.sin(math.pi * t) * 0.45 + 0.58
        sections.append((-radii[2] + 2.0 * radii[2] * t, scale, scale, 0.0, 0.0))
    sections[0] = (-radii[2], 0.004, 0.004, 0.0, 0.0)
    sections[-1] = (radii[2], 0.004, 0.004, 0.0, 0.0)
    obj = bw.loft(profile, sections, surface=surface, name=name, axis="Z")
    obj.data.transform(Matrix.Translation(tuple(centre)))
    obj.data.update()
    return obj


def _stitch(name: str, x: float, y: float, z: float, length: float,
            surface: str = PAD, axis: str = "Y") -> bpy.types.Object:
    """A raised glove seam: a thin rounded ridge that catches a highlight."""
    size = (0.0018, length, 0.0022) if axis == "Y" else (length, 0.0018, 0.0022)
    return bw.box(name, size, (x, y, z), surface=surface, bevel=0.0006)


def _smooth(obj: bpy.types.Object, angle_deg: float = 34.0) -> None:
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    modifier = obj.modifiers.new("KordonSmooth", "EDGE_SPLIT")
    modifier.split_angle = math.radians(angle_deg)
    art.apply_modifiers(obj)


# --------------------------------------------------------------------------
# Right hand: closes on the pistol grip
# --------------------------------------------------------------------------
def right_hand(grip_y: float, grip_top: float, rake_deg: float = 20.0,
               grip_half: float = 0.0168, grip_depth: float = 0.0165,
               scale: float = 1.0) -> bpy.types.Object:
    """Gloved right hand wrapped around a pistol grip.

    Built in the grip's own frame - origin at the top of the grip, grip axis down
    -Z, muzzle toward -Y, the shooter's side on +X - then rotated by the rake and
    moved onto the grip.  The palm's long axis runs from the wrist (rear) to the
    knuckles (front), the four fingers wrap the front strap and finish on the far
    side, and the thumb crosses the top of the grip.
    """
    s = scale
    parts: List[bpy.types.Object] = []
    depth = grip_depth

    # ---- palm: slab from the wrist crease to the knuckle line
    palm_profile = bw.round_rect(0.0128 * s, 0.0330 * s, 0.0100 * s, steps=6)
    palm = bw.loft(palm_profile, [
        (0.036 * s, 0.86, 0.80, 0.0, 0.004 * s),
        (0.016 * s, 0.98, 0.96, 0.0, 0.002 * s),
        (-0.006 * s, 1.02, 1.00, 0.0, 0.0),
        (-0.020 * s, 1.00, 0.98, 0.0, 0.0),
        (-0.030 * s, 0.90, 0.90, 0.0, 0.0),
    ], surface=GLOVE, name="hand_palm", axis="Y")
    palm.data.transform(Matrix.Translation((0.0245 * s, depth * 0.55, -0.056 * s)))
    parts.append(palm)

    # the heel of the palm, riding the backstrap, and the web at the thumb base
    parts.append(_stitch("wrist_crease", 0.0245 * s, depth * 1.16, -0.056 * s, 0.030 * s))
    parts.append(blob("palm_heel", (0.0210 * s, depth * 0.95, -0.070 * s),
                      (0.0170 * s, 0.0210 * s, 0.0250 * s), GLOVE))
    parts.append(blob("palm_web", (0.0195 * s, depth * 0.55, -0.022 * s),
                      (0.0165 * s, 0.0180 * s, 0.0180 * s), GLOVE))

    # ---- four fingers, wrapping the front strap and closing on the far side
    finger_z = (-0.0295, -0.0477, -0.0659, -0.0841)
    for index, z in enumerate(finger_z):
        t = z * s
        lean = -0.004 * s * index if index < 2 else 0.004 * s * (index - 1)
        knuckle = (0.0250 * s + lean, -0.002 * s, t)
        mid_right = (0.0215 * s + lean, -depth - 0.008 * s, t - 0.002 * s)
        mid_front = (0.0060 * s, -depth - 0.014 * s, t - 0.003 * s)
        distal = (-0.0110 * s, -depth - 0.004 * s, t - 0.005 * s)
        tip = (-0.0165 * s, depth * 0.35, t - 0.007 * s)
        points = [knuckle, mid_right, mid_front, distal, tip]
        radii = [0.0089 * s, 0.0086 * s, 0.0081 * s, 0.0075 * s, 0.0066 * s]
        parts.append(chain("finger", points, radii, GLOVE))
        # knuckle: a pad on the outside of the first joint, plus a seam behind it
        parts.append(blob("knuckle", (0.0330 * s + lean, 0.012 * s, t + 0.001 * s),
                          (0.0060 * s, 0.0085 * s, 0.0082 * s), GLOVE))

    # ---- thumb: crosses the top of the grip toward the far side
    parts.append(chain("thumb",
                       [(0.0150 * s, depth * 0.75, -0.018 * s),
                        (0.0075 * s, depth * 0.20, -0.012 * s),
                        (-0.0030 * s, -depth * 0.35, -0.010 * s),
                        (-0.0110 * s, -depth * 0.75, -0.012 * s)],
                       [0.0104 * s, 0.0096 * s, 0.0088 * s, 0.0076 * s], GLOVE))
    parts.append(blob("thumb_base", (0.0180 * s, depth * 0.80, -0.021 * s),
                      (0.0110 * s, 0.0120 * s, 0.0135 * s), GLOVE))

    # ---- seams: two stitched lines across the back of the hand
    for index in range(2):
        parts.append(_stitch("hand_seam", 0.0330 * s,
                             (0.014 - index * 0.026) * s, -0.056 * s, 0.052 * s))

    # ---- wrist and sleeve: the arm has to leave the frame somewhere
    strap = bw.loft(bw.round_rect(0.0170 * s, 0.0300 * s, 0.0120 * s, steps=6),
                    [(0.030 * s, 1.00, 1.00, 0.0, 0.0),
                     (0.040 * s, 1.02, 1.02, 0.0, 0.0),
                     (0.046 * s, 0.96, 0.98, 0.0, 0.0)],
                    surface=PAD, name="wrist_strap", axis="Y")
    strap.data.transform(Matrix.Translation((0.0240 * s, depth * 0.55, -0.058 * s)))
    parts.append(strap)

    # The forearm leaves the hand backwards, outwards and down - the direction a
    # right elbow actually sits when the weapon is at the hip.  Authored as a swept
    # polyline rather than a lofted tube along +Y, because a tube pointing straight
    # back crosses the whole receiver and reads as a log lying on the gun.
    wrist = Vector((0.0250 * s, depth * 0.50, -0.056 * s))
    # The forearm runs mostly back towards the camera and down, with only a little
    # sideways travel: a straight tube to the right reads as a pipe welded to the
    # fist.  A glove cuff sits at the wrist and the sleeve widens as it leaves.
    parts.append(chain("glove_cuff",
                       [wrist, wrist + Vector((0.002, 0.026, -0.010)) * s],
                       [0.0200 * s, 0.0192 * s], PAD, segments=12))
    # The forearm drops away below the grip and slightly back: that is where a
    # right elbow sits, and it keeps the sleeve out of the sight picture.
    parts.append(chain("forearm",
                       [wrist + Vector((0.001, 0.014, -0.006)) * s,
                        wrist + Vector((0.006, 0.040, -0.075)) * s,
                        wrist + Vector((0.010, 0.050, -0.140)) * s,
                        wrist + Vector((0.014, 0.058, -0.235)) * s],
                       [0.0176 * s, 0.0200 * s, 0.0234 * s, 0.0268 * s], CUFF,
                       segments=12))

    hand = art.join(parts, "HandRight")
    art.bevel(hand, width=0.0011 * s, segments=2, angle_deg=38.0)
    _smooth(hand)
    art.unwrap(hand, island_margin=0.004)
    hand.data.transform(Matrix.Rotation(math.radians(rake_deg), 4, "X"))
    hand.data.transform(Matrix.Translation((0.0, grip_y, grip_top)))
    hand.data.update()
    del grip_half
    return hand


# --------------------------------------------------------------------------
# Left hand: cradles the fore-end
# --------------------------------------------------------------------------
def left_hand(fore_y: float, bore_z: float, radius: float = 0.030,
              tilt_deg: float = 0.0, scale: float = 1.0) -> bpy.types.Object:
    """Gloved support hand under a handguard: palm up, fingers over the far side."""
    s = scale
    r = radius
    parts: List[bpy.types.Object] = []

    palm = bw.loft(bw.round_rect(0.0135 * s, 0.0300 * s, 0.0105 * s, steps=6),
                   [(0.040 * s, 0.86, 0.82, 0.0, 0.0),
                    (0.018 * s, 0.98, 0.96, 0.0, 0.0),
                    (-0.004 * s, 1.02, 1.00, 0.0, 0.0),
                    (-0.022 * s, 1.00, 0.98, 0.0, 0.0),
                    (-0.034 * s, 0.88, 0.88, 0.0, 0.0)],
                   surface=GLOVE, name="hand_palm", axis="Y")
    palm.data.transform(Matrix.Translation((-0.0040 * s, -0.004 * s, -r - 0.014 * s)))
    parts.append(palm)
    parts.append(blob("palm_heel", (-0.0060 * s, -0.030 * s, -r - 0.012 * s),
                      (0.0165 * s, 0.0240 * s, 0.0170 * s), GLOVE))

    # four fingers over the far (right) side of the fore-end
    for index, y in enumerate((0.030, 0.010, -0.010, -0.030)):
        t = y * s
        knuckle = (-0.0230 * s, t, -r + 0.002 * s)
        side = (-0.0330 * s, t, -r + 0.012 * s)
        top = (-0.0290 * s, t - 0.002 * s, -r + 0.030 * s)
        over = (-0.0150 * s, t - 0.004 * s, -r + 0.040 * s)
        tip = (-0.0020 * s, t - 0.008 * s, -r + 0.042 * s)
        radii = [0.0104 * s, 0.0100 * s, 0.0092 * s, 0.0085 * s, 0.0074 * s]
        parts.append(chain("finger", [knuckle, side, top, over, tip], radii, GLOVE))
        parts.append(blob("knuckle", (-0.0295 * s, t, -r + 0.007 * s),
                          (0.0060 * s, 0.0085 * s, 0.0085 * s), GLOVE))

    # thumb along the near (left) side, pointing forward
    parts.append(chain("thumb",
                       [(0.0200 * s, 0.020 * s, -r - 0.006 * s),
                        (0.0290 * s, 0.004 * s, -r + 0.006 * s),
                        (0.0300 * s, -0.014 * s, -r + 0.018 * s),
                        (0.0240 * s, -0.030 * s, -r + 0.026 * s)],
                       [0.0128 * s, 0.0118 * s, 0.0102 * s, 0.0086 * s], GLOVE))
    parts.append(blob("thumb_base", (0.0160 * s, 0.024 * s, -r - 0.006 * s),
                      (0.0125 * s, 0.0150 * s, 0.0140 * s), GLOVE))

    for index in range(2):
        parts.append(_stitch("hand_seam", -0.0170 * s,
                             (0.012 - index * 0.024) * s, -r - 0.026 * s, 0.050 * s))

    # wrist and forearm: comes in from the shooter's left and below
    strap = bw.loft(bw.round_rect(0.0160 * s, 0.0290 * s, 0.0120 * s, steps=6),
                    [(0.030 * s, 1.0, 1.0, 0.0, 0.0),
                     (0.040 * s, 1.02, 1.02, 0.0, 0.0),
                     (0.046 * s, 0.96, 0.98, 0.0, 0.0)],
                    surface=PAD, name="wrist_strap", axis="Y")
    strap.data.transform(Matrix.Translation((-0.0050 * s, 0.0, -r - 0.016 * s)))
    parts.append(strap)
    wrist = Vector((-0.0050 * s, -0.004 * s, -r - 0.020 * s))
    parts.append(chain("glove_cuff",
                       [wrist, wrist + Vector((-0.002, 0.026, -0.012)) * s],
                       [0.0196 * s, 0.0188 * s], PAD, segments=12))
    parts.append(chain("forearm",
                       [wrist + Vector((-0.001, 0.014, -0.008)) * s,
                        wrist + Vector((-0.005, 0.038, -0.080)) * s,
                        wrist + Vector((-0.009, 0.048, -0.144)) * s,
                        wrist + Vector((-0.013, 0.056, -0.240)) * s],
                       [0.0172 * s, 0.0196 * s, 0.0230 * s, 0.0264 * s], CUFF,
                       segments=12))

    hand = art.join(parts, "HandLeft")
    art.bevel(hand, width=0.0011 * s, segments=2, angle_deg=38.0)
    _smooth(hand)
    art.unwrap(hand, island_margin=0.004)
    if abs(tilt_deg) > 1e-6:
        hand.data.transform(Matrix.Rotation(math.radians(tilt_deg), 4, "Y"))
    hand.data.transform(Matrix.Translation((0.0, fore_y, bore_z)))
    hand.data.update()
    return hand


# --------------------------------------------------------------------------
# Where the hands go on a given weapon
# --------------------------------------------------------------------------
def hands_for(spec: dict, ctx: dict) -> Tuple[bpy.types.Object, bpy.types.Object]:
    """Build the pair of hands for one weapon from its layout table."""
    kind = spec["kind"]
    if kind == "pistol":
        # the frame grip is shorter and narrower than a rifle grip
        right = right_hand(0.026, -0.028, rake_deg=18.0, grip_half=0.0132,
                           grip_depth=0.0175, scale=0.92)
    elif kind == "shotgun":
        # no pistol grip on the pump gun: the right hand closes on the wrist of
        # the stock, behind the trigger plate
        right = right_hand(ctx["grip_y"] - 0.014, ctx["bottom"] + 0.004,
                           rake_deg=32.0, grip_depth=0.0175, scale=1.0)
    else:
        rake = 14.0 if kind == "sniper" else 20.0
        right = right_hand(ctx["grip_y"], ctx["bottom"] + 0.020, rake_deg=rake,
                           grip_depth=0.0165, scale=1.0)

    handguard = spec.get("handguard", "none")
    if handguard == "wood":
        fore_y = ctx["front"] - spec["handguard_len"] * 0.42
        radius = 0.0290
    elif handguard == "mlok":
        fore_y = ctx["front"] - spec["handguard_len"] * 0.46
        radius = spec["barrel_r"] * spec.get("handguard_scale", 2.6) * 0.96
    elif handguard == "pump":
        fore_y = ctx["front"] - spec["handguard_len"] * 0.46
        radius = 0.0295
    else:
        # sidearm: the support hand wraps the shooting hand, so it sits just
        # ahead of and below the grip rather than on a fore-end
        left = left_hand(-0.004, -0.052, radius=0.0300, tilt_deg=-12.0, scale=0.86)
        return right, left

    return right, left_hand(fore_y, ctx["bore"], radius=radius)


def add_hands(spec: dict, ctx: dict) -> List[bpy.types.Object]:
    """Build both hands and return them as exportable objects."""
    right, left = hands_for(spec, ctx)
    return [right, left]
