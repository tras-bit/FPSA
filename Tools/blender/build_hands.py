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
def _resample(points: Sequence[Sequence[float]], radii: Sequence[float],
              steps: int = 4) -> Tuple[List[Vector], List[float]]:
    """Smooth a control polyline into a curve, keeping the radii continuous.

    A finger was five control points joined by straight segments, so every knuckle was a
    corner and the top of each phalanx was a flat facet - which is why wrapped fingers
    read as slabs even after the surface was remeshed.  Catmull-Rom through the same
    control points gives the curl a continuous bend, and the radii are interpolated with
    it so the taper survives.
    """
    control = [Vector(p) for p in points]
    if len(control) < 3:
        return control, list(radii)

    def point_at(index: int) -> Vector:
        return control[min(max(index, 0), len(control) - 1)]

    smooth_points: List[Vector] = []
    smooth_radii: List[float] = []
    for index in range(len(control) - 1):
        p0, p1 = point_at(index - 1), point_at(index)
        p2, p3 = point_at(index + 1), point_at(index + 2)
        r1 = radii[min(index, len(radii) - 1)]
        r2 = radii[min(index + 1, len(radii) - 1)]
        for step in range(steps):
            t = step / float(steps)
            t2 = t * t
            t3 = t2 * t
            smooth_points.append(0.5 * (
                (2.0 * p1) +
                (-p0 + p2) * t +
                (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 +
                (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3))
            smooth_radii.append(r1 + (r2 - r1) * t)

    smooth_points.append(control[-1])
    smooth_radii.append(radii[-1])
    return smooth_points, smooth_radii


def chain(name: str, points: Sequence[Sequence[float]], radii: Sequence[float],
          surface: str, segments: int = 14,
          flatten: float = 1.0) -> bpy.types.Object:
    """A tapered tube through a polyline, with rounded ends.

    Frames are parallel transported (each ring's normal is projected onto the plane
    perpendicular to the new tangent), which is what keeps a curled finger from
    twisting inside out at the knuckle.  ``flatten`` squashes the cross-section along
    the swept binormal, which is what turns a sausage into a finger: a finger is wider
    than it is thick, and its flat side lies against the grip.
    """
    pts, radii = _resample(points, radii)
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
                      + binormal * (math.sin(angle) * radius * flatten))
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
    profile = bw.round_rect(radii[0], radii[1], min(radii) * 0.55, steps=10)
    sections = []
    steps = 11
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


def fuse(parts: Sequence[bpy.types.Object], name: str, surface: str = GLOVE,
         voxel: float = 0.0013, target_faces: int = 4200,
         grooves: Sequence[bpy.types.Object] = ()) -> bpy.types.Object:
    """Union authored lumps into one watertight skin.

    Every finger, knuckle, pad and the thumb were separate closed solids that
    interpenetrate where they meet.  Interpenetration is fine on a receiver, where the
    overlap is a seam; on a hand it leaves a crease that is darker than the crevice of
    a real gloved fist, and from the shooting angle the creases read as holes punched
    through the glove.  There is no way to build a believable hand out of loose lumps.

    So the authored solids are voxel-remeshed into a single manifold surface: the
    overlaps become one continuous skin, every crease becomes a real fillet, and the
    result is decimated back to a sane face count.  The remesh is done on the pieces
    that share a material, so the leather glove fuses with itself and stays leather.

    ``grooves`` are cutters subtracted *after* the remesh.  A raised ridge on a fused
    surface just becomes another soft bump, because the remesh smooths it into its
    surroundings; a groove is what the eye actually reads as a metacarpal, and a
    subtraction is the only way to get one.
    """
    merged = art.join(list(parts), name + "_raw")
    remesh = merged.modifiers.new("KordonFuse", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = voxel
    remesh.use_smooth_shade = True
    art.apply_modifiers(merged)

    # Metacarpal grooves and the crease at the base of the fingers.
    for cutter in grooves:
        boolean = merged.modifiers.new("KordonGroove", "BOOLEAN")
        boolean.operation = "DIFFERENCE"
        boolean.solver = "EXACT"
        boolean.object = cutter
        try:
            bpy.ops.object.select_all(action="DESELECT")
            merged.select_set(True)
            bpy.context.view_layer.objects.active = merged
            bpy.ops.object.modifier_apply(modifier=boolean.name)
        except RuntimeError as error:
            print(f"[hands] groove boolean failed: {error}")
            merged.modifiers.remove(boolean)
        bpy.data.objects.remove(cutter, do_unlink=True)

    # A voxel remesh leaves a faint orange-peel facet.  At the distance the hand is
    # seen from, that facet is the difference between leather and polycarbonate.
    smooth = merged.modifiers.new("KordonRelax", "SMOOTH")
    smooth.factor = 0.55
    smooth.iterations = 9
    art.apply_modifiers(merged)

    faces = len(merged.data.polygons)
    if faces > target_faces:
        decimate = merged.modifiers.new("KordonDecimate", "DECIMATE")
        decimate.decimate_type = "COLLAPSE"
        decimate.ratio = target_faces / float(faces)
        art.apply_modifiers(merged)

    # Close anything the boolean or the decimator left open, weld the seams, and
    # refuse to ship a glove with holes in it.  This is the check that was missing:
    # the crevices between the authored lumps were only half the problem, the other
    # half was 162 open boundary edges nobody looked at.
    _make_watertight(merged)

    merged.data.materials.clear()
    merged.data.materials.append(art.surface_material(surface))
    for polygon in merged.data.polygons:
        polygon.material_index = 0
    merged.name = name
    merged.data.name = name
    return merged


def mirror_across_x(obj: bpy.types.Object) -> bpy.types.Object:
    """Mirror a hand about the weapon's centre plane, keeping it outward-facing.

    A support hand on a pistol grip is the shooting hand wrapped the other way round:
    mirrored, slid down the grip, and closed a little tighter.  Building it from the
    fore-end hand (palm up under a handguard) cannot work - there is no handguard on a
    sidearm, which is why the pistol pair looked like two hands holding air.
    """
    obj.data.transform(Matrix.Scale(-1.0, 4, (1.0, 0.0, 0.0)))
    obj.data.update()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def _make_watertight(obj: bpy.types.Object) -> None:
    """Weld, fill any open boundary, and assert the result is a closed surface."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00002)
    bpy.ops.mesh.fill_holes(sides=0)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    boundary = _boundary_edges(obj)
    if boundary:
        raise RuntimeError(f"{obj.name}: {boundary} open edges after welding - the "
                           "glove would be see-through")


def _boundary_edges(obj: bpy.types.Object) -> int:
    import bmesh
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    count = sum(1 for edge in mesh.edges if len(edge.link_faces) == 1)
    mesh.free()
    return count


def _smooth_shaded(obj: bpy.types.Object) -> None:
    """Plain smooth shading for a fused organic surface.

    The edge-split used everywhere else in this project is right for a receiver, where
    it keeps the flats flat; on a remeshed hand it turns every irregular triangle into
    a visible facet, which is why the first fused hand looked like carved stone.
    """
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


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
    palm_profile = bw.round_rect(0.0082 * s, 0.0256 * s, 0.0080 * s, steps=8)
    palm = bw.loft(palm_profile, [
        (0.036 * s, 0.86, 0.80, 0.0, 0.004 * s),
        (0.016 * s, 0.98, 0.96, 0.0, 0.002 * s),
        (-0.006 * s, 1.02, 1.00, 0.0, 0.0),
        (-0.020 * s, 1.00, 0.98, 0.0, 0.0),
        (-0.030 * s, 0.90, 0.90, 0.0, 0.0),
    ], surface=GLOVE, name="hand_palm", axis="Y")
    palm.data.transform(Matrix.Translation((0.0245 * s, depth * 0.55, -0.056 * s)))
    parts.append(palm)

    # Metacarpals.  The back of a hand is not a dome: four long ridges run from the
    # wrist to the knuckles and the skin between them is slightly hollow.  Without
    # them the fused glove reads as a boxing glove, which is what the review render
    # showed.  These are thin, flattened lofts, one per finger.
    parts.append(blob("dorsum", (0.0282 * s, depth * 0.50, -0.0562 * s),
                      (0.0058 * s, 0.0236 * s, 0.0218 * s), GLOVE))
    # The valleys between the metacarpals are shaped into the dorsum volume itself:
    # cutting them with a boolean left open edges (a glove you could see through) and
    # the groove was too shallow to read anyway.
    grooves = []

    # the heel of the palm, riding the backstrap, and the web at the thumb base
    parts.append(_stitch("wrist_crease", 0.0245 * s, depth * 1.16, -0.056 * s, 0.030 * s))
    parts.append(blob("palm_heel", (0.0205 * s, depth * 0.98, -0.072 * s),
                      (0.0104 * s, 0.0132 * s, 0.0158 * s), GLOVE))
    parts.append(blob("palm_web", (0.0192 * s, depth * 0.55, -0.022 * s),
                      (0.0112 * s, 0.0122 * s, 0.0128 * s), GLOVE))
    # Hypothenar: the muscle pad along the little finger's side of the palm.  Without
    # it the palm ends in the same thickness edge on both sides and reads as a block.
    parts.append(blob("palm_hypothenar", (0.0225 * s, depth * 0.52, -0.083 * s),
                      (0.0135 * s, 0.0175 * s, 0.0155 * s), GLOVE))

    # ---- four fingers, wrapping the front strap and closing on the far side
    # 17.6 mm spacing against a 9.2 mm radius: neighbouring fingers touch, so the voxel
    # union welds them into one fist.  At the old 18.2 mm spacing with 8.6 mm fingers a
    # sub-millimetre gap survived the remesh and the fist read as four separate sausages.
    finger_z = (-0.0292, -0.0468, -0.0644, -0.0820)
    for index, z in enumerate(finger_z):
        t = z * s
        lean = -0.004 * s * index if index < 2 else 0.004 * s * (index - 1)
        # The middle finger reaches furthest round the strap and the little finger
        # least: four fingers of one length is the giveaway of a modelled mitten.
        reach = (0.0, 0.0035, 0.0018, -0.0040)[index] * s
        knuckle = (0.0250 * s + lean, -0.004 * s, t)
        mid_right = (0.0225 * s + lean, -depth - 0.010 * s, t - 0.002 * s)
        mid_front = (0.0050 * s, -depth - 0.018 * s - reach, t - 0.004 * s)
        distal = (-0.0130 * s, -depth - 0.006 * s - reach, t - 0.006 * s)
        tip = (-0.0195 * s, depth * 0.30, t - 0.009 * s - reach * 0.4)
        points = [knuckle, mid_right, mid_front, distal, tip]
        radii = [0.0092 * s, 0.0087 * s, 0.0079 * s, 0.0069 * s, 0.0054 * s]
        parts.append(chain("finger", points, radii, GLOVE, segments=14, flatten=0.86))
        # knuckle: a pad on the outside of the first joint, plus a seam behind it
        # The knuckle is the finger's own first section.  A bead on top of it fused
        # into a visible lump, so there is no separate knuckle part any more.

    # The saddle between the thumb's metacarpal and the index metacarpal, and the
    # crease where the wrapped fingers meet the back of the hand.  Both are real, but
    # in the fused glove they opened into what the review render showed as soft holes;
    # they need a shallow floor, not a shaft into the glove.
    parts.append(blob("thenar_saddle", (0.0248 * s, depth * 0.72, -0.0300 * s),
                      (0.0078 * s, 0.0090 * s, 0.0064 * s), GLOVE))
    parts.append(blob("knuckle_crease", (0.0272 * s, depth * 1.02, -0.0560 * s),
                      (0.0052 * s, 0.0060 * s, 0.0180 * s), GLOVE))

    # ---- thumb: crosses the top of the grip toward the far side
    parts.append(chain("thumb",
                       [(0.0160 * s, depth * 0.80, -0.021 * s),
                        (0.0090 * s, depth * 0.18, -0.014 * s),
                        (-0.0020 * s, -depth * 0.40, -0.0125 * s),
                        (-0.0130 * s, -depth * 0.92, -0.0105 * s)],
                       [0.0112 * s, 0.0100 * s, 0.0088 * s, 0.0066 * s], GLOVE,
                       flatten=0.80))
    parts.append(blob("thumb_base", (0.0200 * s, depth * 0.86, -0.025 * s),
                      (0.0092 * s, 0.0100 * s, 0.0112 * s), GLOVE))
    # Web of the hand, between the thumb and the index finger.  Leaving it out left a
    # real gap there with the trigger guard visible through it, which is what reads as
    # a hole in the glove.
    parts.append(blob("thumb_web", (0.0218 * s, depth * 0.22, -0.0245 * s),
                      (0.0100 * s, 0.0120 * s, 0.0096 * s), GLOVE))
    # the joint at the base of the thumb's distal phalanx: the one that catches light
    parts.append(blob("thumb_knuckle", (-0.0020 * s, -depth * 0.40, -0.0125 * s),
                      (0.0068 * s, 0.0072 * s, 0.0070 * s), GLOVE))

    # The two stitched seams that used to sit on the back of the hand are gone: after
    # the glove was fused they were 2 mm bars on the outside of the skin, and from the
    # shooting angle they read as a wire sticking out between the fingers.  A seam is
    # a groove in the leather, not a rod laid on it, and the fused surface carries the
    # crease on its own.

    # ---- wrist and sleeve: the arm has to leave the frame somewhere
    strap = bw.loft(bw.round_rect(0.0148 * s, 0.0270 * s, 0.0110 * s, steps=6),
                    [(0.030 * s, 1.00, 1.00, 0.0, 0.0),
                     (0.040 * s, 1.02, 1.02, 0.0, 0.0),
                     (0.046 * s, 0.96, 0.98, 0.0, 0.0)],
                    surface=GLOVE, name="wrist_strap", axis="Y")
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
                       [0.0206 * s, 0.0228 * s], GLOVE, segments=14))
    # the elastic band of the cuff, sitting proud of the sleeve where it meets it
    parts.append(chain("cuff_band",
                       [wrist + Vector((0.0022, 0.0235, -0.0092)) * s,
                        wrist + Vector((0.0028, 0.0315, -0.0120)) * s],
                       [0.0234 * s, 0.0234 * s], GLOVE, segments=14))
    # The forearm drops away below the grip and slightly back: that is where a
    # right elbow sits, and it keeps the sleeve out of the sight picture.
    arm = [chain("forearm",
                 [wrist + Vector((0.001, 0.014, -0.006)) * s,
                  wrist + Vector((0.006, 0.040, -0.075)) * s,
                  wrist + Vector((0.010, 0.050, -0.140)) * s,
                  wrist + Vector((0.014, 0.058, -0.235)) * s],
                 [0.0192 * s, 0.0216 * s, 0.0246 * s, 0.0278 * s], CUFF,
                 segments=14)]

    # One watertight glove instead of a dozen interpenetrating lumps: the crevices
    # between them were reading as holes through the hand.
    # A 1.05 mm voxel decimated to 5000 faces left 4-5 mm triangles on the back of the
    # hand, and a triangle that wide catches light as a facet: the glove read as a
    # faceted lump rather than as skin.  Finer voxel, twice the faces, smoother.
    glove = fuse(parts, "HandRight_glove", GLOVE, voxel=0.00090 * s,
                 target_faces=11000, grooves=grooves)
    _smooth_shaded(glove)
    for part in arm:
        _smooth(part, 40.0)
    hand = art.join([glove] + arm, "HandRight")
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

    palm = bw.loft(bw.round_rect(0.0126 * s, 0.0280 * s, 0.0098 * s, steps=6),
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
    # 20 mm pitch with 20.8 mm tubes merged all four into one slab, which is what the
    # first review render showed.  Nine millimetres on a 21 mm pitch leaves a gap that
    # reads as separate digits, and the index reaches furthest round the handguard.
    for index, y in enumerate((0.0315, 0.0105, -0.0105, -0.0315)):
        t = y * s
        reach = (0.0060, 0.0090, 0.0075, 0.0020)[index] * s
        knuckle = (-0.0230 * s, t, -r + 0.002 * s)
        side = (-0.0330 * s, t, -r + 0.012 * s)
        top = (-0.0290 * s, t - 0.002 * s, -r + 0.030 * s)
        over = (-0.0150 * s, t - 0.004 * s, -r + 0.040 * s - reach * 0.35)
        tip = (-0.0020 * s - reach * 0.30, t - 0.008 * s, -r + 0.042 * s - reach)
        # More sections along the finger and a rounder cross-section: at 10 segments and
        # a 0.80 flatten the wrapped fingers read as flat slabs rather than fingers.
        radii = [0.0094 * s, 0.0090 * s, 0.0083 * s, 0.0074 * s, 0.0062 * s]
        parts.append(chain("finger", [knuckle, side, top, over, tip], radii, GLOVE,
                           segments=14, flatten=0.90))
        parts.append(blob("knuckle", (-0.0295 * s, t, -r + 0.007 * s),
                          (0.0052 * s, 0.0074 * s, 0.0074 * s), GLOVE))

    # thumb along the near (left) side, pointing forward
    parts.append(chain("thumb",
                       [(0.0200 * s, 0.020 * s, -r - 0.006 * s),
                        (0.0290 * s, 0.004 * s, -r + 0.006 * s),
                        (0.0300 * s, -0.014 * s, -r + 0.018 * s),
                        (0.0240 * s, -0.030 * s, -r + 0.026 * s)],
                       [0.0122 * s, 0.0112 * s, 0.0098 * s, 0.0082 * s], GLOVE))
    parts.append(blob("thumb_base", (0.0160 * s, 0.024 * s, -r - 0.006 * s),
                      (0.0118 * s, 0.0140 * s, 0.0132 * s), GLOVE))
    parts.append(blob("thumb_web", (0.0080 * s, 0.012 * s, -r - 0.010 * s),
                      (0.0130 * s, 0.0140 * s, 0.0120 * s), GLOVE))

    for index in range(2):
        parts.append(_stitch("hand_seam", -0.0170 * s,
                             (0.012 - index * 0.024) * s, -r - 0.026 * s, 0.050 * s))

    # wrist and forearm: comes in from the shooter's left and below
    strap = bw.loft(bw.round_rect(0.0140 * s, 0.0265 * s, 0.0110 * s, steps=6),
                    [(0.030 * s, 1.0, 1.0, 0.0, 0.0),
                     (0.040 * s, 1.02, 1.02, 0.0, 0.0),
                     (0.046 * s, 0.96, 0.98, 0.0, 0.0)],
                    surface=GLOVE, name="wrist_strap", axis="Y")
    strap.data.transform(Matrix.Translation((-0.0050 * s, 0.0, -r - 0.016 * s)))
    parts.append(strap)
    wrist = Vector((-0.0050 * s, -0.004 * s, -r - 0.020 * s))
    parts.append(chain("glove_cuff",
                       [wrist, wrist + Vector((-0.002, 0.026, -0.012)) * s],
                       [0.0198 * s, 0.0224 * s], GLOVE, segments=14))
    # The support elbow is tucked under the handguard, so the forearm swings out to
    # the shooter's left as it leaves the frame.  Dropping straight down read as a
    # pipe welded to the glove.
    arm = [chain("forearm",
                 [wrist + Vector((-0.001, 0.014, -0.008)) * s,
                  wrist + Vector((-0.014, 0.040, -0.070)) * s,
                  wrist + Vector((-0.030, 0.052, -0.130)) * s,
                  wrist + Vector((-0.052, 0.060, -0.220)) * s],
                 [0.0188 * s, 0.0212 * s, 0.0242 * s, 0.0274 * s], CUFF,
                 segments=14)]

    glove = fuse(parts, "HandLeft_glove", GLOVE, voxel=0.00090 * s,
                 target_faces=11000)
    _smooth_shaded(glove)
    for part in arm:
        _smooth(part, 40.0)
    hand = art.join([glove] + arm, "HandLeft")
    art.unwrap(hand, island_margin=0.004)
    if abs(tilt_deg) > 1e-6:
        hand.data.transform(Matrix.Rotation(math.radians(tilt_deg), 4, "Y"))
    hand.data.transform(Matrix.Translation((0.0, fore_y, bore_z)))
    hand.data.update()
    return hand


# --------------------------------------------------------------------------
# Where the hands go on a given weapon
# --------------------------------------------------------------------------
def hands_for(spec: dict, ctx: dict,
              weapon: bpy.types.Object = None) -> Tuple[bpy.types.Object, bpy.types.Object]:
    """Build the pair of hands for one weapon from its layout table.

    When the weapon mesh is available its real fore-end is measured and the support hand
    is built around that, instead of the hand-written guess in the spec.
    """
    kind = spec["kind"]
    if kind == "pistol":
        # the frame grip is shorter and narrower than a rifle grip
        right = right_hand(0.026, -0.028, rake_deg=18.0, grip_half=0.0132,
                           grip_depth=0.0175, scale=0.86)
        support = right_hand(0.026, -0.028, rake_deg=18.0, grip_half=0.0132,
                             grip_depth=0.0182, scale=0.80)
        mirror_across_x(support)
        # down the grip and a touch forward: the support fingers close over the
        # shooting hand's little and ring fingers, which is what the two-handed hold
        # actually is
        # mostly back and only a little down: the support palm closes behind the
        # shooting hand's fingers, not under the magazine well
        support.data.transform(Matrix.Translation((0.007, 0.027, -0.013)))
        support.data.update()
        support.name = "HandLeft"
        support.data.name = "HandLeft"
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
        # Sidearm: the support hand cups the shooting hand, so it is placed on the
        # shooting hand, not 80 mm forward under the frame where there is nothing to
        # hold.  That placement was why the pistol pair in the review sheet looked
        # like two hands gripping empty air.
        return right, support

    # The support hand wraps measured geometry when the mesh exists.
    bore = ctx["bore"]
    if weapon is not None:
        bottom, half_width = measure_fore_end(weapon, fore_y, bore)
        radius = half_width + 0.0035          # the glove sits on the surface, not in it
        bore = bottom + radius                 # the curl centre follows the measured bottom
    return right, left_hand(fore_y, bore, radius=radius)


def measure_fore_end(weapon: bpy.types.Object, y: float, bore_z: float,
                     reach: float = 0.40) -> Tuple[float, float]:
    """Measure the fore-end's bottom edge and half-width at one point along the weapon.

    The support hand wraps a fore-end whose size was written into the weapon spec by
    hand, and the spec was wrong by up to twelve millimetres: the fingers closed short
    of the surface and the hand read as hovering.  The mesh is the only trustworthy
    source, so the cross-section is measured from it - the bottom by a ray cast down
    from above, the width by rays cast in from both sides at the bore line - and the
    hand is built around those numbers.

    Returns ``(bottom_z, half_width)`` in world space.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    scene = bpy.context.scene

    # Cast *up* from below: the first thing that stops the ray is the underside of the
    # fore-end.  Casting down from above finds the top rail, which is not a bottom.
    hit_up, location_up, _n, _i, _o, _m = scene.ray_cast(
        depsgraph, Vector((0.0, y, bore_z - reach)), Vector((0.0, 0.0, 1.0)))
    bottom = location_up.z if hit_up else bore_z - 0.02

    half_width = 0.0
    for sign in (-1.0, 1.0):
        hit, location, _n, _i, _o, _m = scene.ray_cast(
            depsgraph, Vector((sign * reach, y, bore_z)), Vector((-sign, 0.0, 0.0)))
        if hit:
            half_width = max(half_width, abs(location.x))

    if half_width < 0.004:
        half_width = 0.012
    return bottom, half_width


def seat_on_weapon(hand: bpy.types.Object, weapon: bpy.types.Object,
                   palm_z_from_top: float = 0.070, max_shift: float = 0.075) -> float:
    """Move a hand vertically until it touches the weapon; return the gap it closed.

    Every hand is authored against an assumed position, and the assumptions were wrong
    by up to two centimetres: on the review sheet the support hands hung under the
    handguards with their fingers closed on air.  The surface is the truth, so the hand
    is snapped to it.

    The correction is **vertical only**.  Snapping to the nearest skin - the first
    version - pulled the VS9's support hand up onto the top of the receiver, because
    from a palm below the fore-end the nearest surface is the top of the gun.  A hand
    belongs on the side of the weapon it was authored on.
    """
    weapon_points = [weapon.matrix_world @ vertex.co for vertex in weapon.data.vertices]
    hand_points = [hand.matrix_world @ vertex.co for vertex in hand.data.vertices]
    low = Vector((min(p.x for p in hand_points), min(p.y for p in hand_points),
                  min(p.z for p in hand_points)))
    high = Vector((max(p.x for p in hand_points), max(p.y for p in hand_points),
                   max(p.z for p in hand_points)))
    palm = Vector(((low.x + high.x) * 0.5, (low.y + high.y) * 0.5,
                   high.z - palm_z_from_top))

    weapon_high = max(p.z for p in weapon_points)
    weapon_low = min(p.z for p in weapon_points)
    underneath = palm.z < (weapon_high + weapon_low) * 0.5

    depsgraph = bpy.context.evaluated_depsgraph_get()
    direction = Vector((0.0, 0.0, 1.0 if underneath else -1.0))
    hit, location, _normal, _index, _object, _matrix = bpy.context.scene.ray_cast(
        depsgraph, Vector((palm.x, palm.y, palm.z)), direction)

    if hit:
        offset = Vector((0.0, 0.0, location.z - palm.z))
    else:
        step = max(1, len(weapon_points) // 6000)
        nearest = min(weapon_points[::step], key=lambda point: (point - palm).length)
        offset = Vector((0.0, 0.0, nearest.z - palm.z))

    if offset.length > max_shift:
        raise RuntimeError(
            f"{hand.name} is {offset.length * 1000:.0f} mm out of place on "
            f"{weapon.name}: that is not a seating error, that is a hand built for "
            "another weapon")

    hand.data.transform(Matrix.Translation(offset))
    hand.data.update()
    return offset.length


def add_hands(spec: dict, ctx: dict,
              weapon: bpy.types.Object = None) -> List[bpy.types.Object]:
    """Build both hands and return them as exportable objects."""
    right, left = hands_for(spec, ctx, weapon)
    return [right, left]
