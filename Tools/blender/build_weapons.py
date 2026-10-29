#!/usr/bin/env python3
"""Build the KORDON weapon set: first person models, .blend source, manifest.

    Tools/blender.sh Tools/blender/build_weapons.py            # everything
    Tools/blender.sh Tools/blender/build_weapons.py --only vk74

Outputs

    ArtSource/Blender/kordon_weapons.blend                       authoring source
    Assets/_Project/Art/Models/Resources/Kordon/Weapons/<id>.fbx one file per weapon
    Assets/StreamingAssets/Data/Art/weapon_manifest.json          dimensions for Unity

Conventions (the same ones the kit and the maps use)

* 1 Blender unit = 1 metre, Z up, the barrel points along **-Y**, which is what
  Unity reads as +Z (forward) after the FBX axis conversion;
* the origin sits at the top of the pistol grip, i.e. in the shooting hand, so the
  viewmodel pose is a hand position and not a bounding box centre;
* every weapon is a separate authored mesh: receiver, barrel, handguard, stock,
  grip, trigger group, iron sights or optic, sling swivels, and a detachable
  magazine that is exported as its own object named ``Magazine``, pivoted at the
  top of the magazine so the reload animation rotates it out of the well instead
  of swinging it around the grip;
* surfaces come from kordon_art.SURFACES, so the weapon materials are the same
  generated, redistributable sets the map uses;
* every exported FBX is re-imported and its bounding box compared with the authored
  one - a broken export fails here instead of reaching the game.

Modelling notes (third art pass)

The first pass was boxes. The second pass lofted profiles but still assembled the
guns from a handful of large shapes, so at viewmodel distance they read as a
receiver with sticks attached. This pass models each part as the part it is:

* receivers are lofted shells with a tapered nose, a dust cover with pressed ribs,
  a real ejection port (a boolean pocket with the bolt carrier visible inside),
  a selector, rivets and pins instead of blank side panels;
* the trigger guard is a tube swept along a polyline, and the trigger is a separate
  curved blade inside it;
* furniture is profiled: the AK fore-end is a walnut lower with a belly, a finger
  groove and a separate upper over the gas tube; the AR handguard is a free float
  tube with recessed M-LOK panels and a toothed top rail; stocks carry a comb, a
  cheek riser, a recoil pad and a sling slot;
* optics are hollow tubes with a mount, a hood, turrets and glass; the sniper keeps
  a folded bipod under the fore-end and a bolt handle on the right;
* every part is beveled, shaded smooth with split normals at 34 degrees, UV
  unwrapped and textured with the game's own generated metal, polymer and wood.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time
from typing import Dict, List, Sequence, Tuple

import bpy
import bmesh  # only importable once bpy has loaded the Blender python runtime
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import kordon_art as art  # noqa: E402

WEAPON_DIR = os.path.join(art.MODEL_DIR, "Resources", "Kordon", "Weapons")
MANIFEST = os.path.join(art.DATA_DIR, "Art", "weapon_manifest.json")

# --------------------------------------------------------------------------
# Materials
# --------------------------------------------------------------------------
METAL = "gun_metal"           # anodised receiver, phosphated barrel
BARE = "gun_bare"             # bolt, pins, crown: machined steel
POLYMER = "gun_polymer"       # grip, handguard, stock shell
WOOD = "gun_wood"             # oiled walnut
OLIVE = "gun_olive"           # painted chassis
RUBBER = "rubber"             # recoil pad, grip inserts
GLASS = "glass"               # optic lenses


# --------------------------------------------------------------------------
# Mesh primitives
# --------------------------------------------------------------------------
def _link(obj: bpy.types.Object) -> bpy.types.Object:
    bpy.context.collection.objects.link(obj)
    return obj


def mesh_object(name: str, verts: Sequence, faces: Sequence, surface: str
                ) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.validate(verbose=False)
    mesh.update()
    obj = _link(bpy.data.objects.new(name, mesh))
    art.assign_surface(obj, surface)
    return obj


def recalc_normals(obj: bpy.types.Object) -> None:
    """Make every closed shell face outwards.

    Each primitive is a closed shell, so recalculating per part is safe and keeps
    the exported normals correct without a whole mesh pass at the end (which
    flipped the boolean pockets of the previous build).
    """
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def bake(obj: bpy.types.Object) -> bpy.types.Object:
    """Apply object transform into the mesh so booleans and joins behave."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.select_set(False)
    return obj


def boxpart(size: Sequence[float], centre: Sequence[float],
            surface: str = METAL, rotation_deg: Sequence[float] = (0.0, 0.0, 0.0),
            name: str = "part") -> bpy.types.Object:
    hx, hy, hz = (value * 0.5 for value in size)
    verts = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
             (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    obj = mesh_object(name, verts, faces, surface)
    obj.location = tuple(centre)
    if any(abs(angle) > 1e-9 for angle in rotation_deg):
        obj.rotation_euler = tuple(math.radians(angle) for angle in rotation_deg)
    return bake(obj)


# --------------------------------------------------------------------------
# Cross sections
# --------------------------------------------------------------------------
def chamfer_rect(half_x: float, half_z: float, cut: float) -> List[Tuple[float, float]]:
    cut = min(cut, half_x * 0.48, half_z * 0.48)
    return [(-half_x + cut, -half_z), (half_x - cut, -half_z),
            (half_x, -half_z + cut), (half_x, half_z - cut),
            (half_x - cut, half_z), (-half_x + cut, half_z),
            (-half_x, half_z - cut), (-half_x, -half_z + cut)]


def round_rect(half_x: float, half_z: float, radius: float,
               steps: int = 3) -> List[Tuple[float, float]]:
    radius = max(1e-4, min(radius, half_x * 0.98, half_z * 0.98))
    points: List[Tuple[float, float]] = []
    corners = ((half_x - radius, -half_z + radius, -90.0),
               (half_x - radius, half_z - radius, 0.0),
               (-half_x + radius, half_z - radius, 90.0),
               (-half_x + radius, -half_z + radius, 180.0))
    for cx, cz, start in corners:
        for step in range(steps + 1):
            angle = math.radians(start + 90.0 * step / steps)
            points.append((cx + radius * math.cos(angle),
                           cz + radius * math.sin(angle)))
    return points


def oval(half_x: float, half_z: float, steps: int = 14,
         squash: float = 1.0) -> List[Tuple[float, float]]:
    points = []
    for step in range(steps):
        angle = 2.0 * math.pi * step / steps
        points.append((half_x * math.cos(angle),
                       half_z * math.sin(angle) * (1.0 + squash * math.cos(angle))))
    return points


def stadium(half_len: float, radius: float, steps: int = 6) -> List[Tuple[float, float]]:
    """Capsule cross section: a rectangle with semicircular ends.

    The straight sides have to sit at +-radius; the first version connected the two
    arcs on the centre line, which collapsed every M-LOK slot and sling slot cutter
    into a peanut and made the boolean cut a hole instead of a slot.
    """
    points = []
    for step in range(steps + 1):
        angle = math.pi * step / steps
        points.append((radius * math.cos(angle),
                       half_len + radius * math.sin(angle)))
    for step in range(steps + 1):
        angle = math.pi + math.pi * step / steps
        points.append((radius * math.cos(angle),
                       -half_len + radius * math.sin(angle)))
    return points


# --------------------------------------------------------------------------
# Loft: sweep a profile along an axis with a scale and an offset per section
# --------------------------------------------------------------------------
def loft(profile: Sequence[Tuple[float, float]],
         sections: Sequence[Sequence[float]], surface: str = METAL,
         name: str = "loft", axis: str = "Y", caps: bool = True) -> bpy.types.Object:
    """sections: (position, scale_u, scale_v, offset_u, offset_v).

    axis "Y" sweeps the profile through the YZ... precisely: ``u`` maps to X and
    ``v`` maps to Z; axis "X" maps ``u`` to Y and ``v`` to Z; axis "Z" maps ``u`` to
    X and ``v`` to Y.
    """
    count = len(profile)
    verts: List[Tuple[float, float, float]] = []
    for section in sections:
        position, scale_u, scale_v, offset_u, offset_v = section
        for u, v in profile:
            uu = u * scale_u + offset_u
            vv = v * scale_v + offset_v
            if axis == "Y":
                verts.append((uu, position, vv))
            elif axis == "X":
                verts.append((position, uu, vv))
            else:
                verts.append((uu, vv, position))

    faces: List[tuple] = []
    for index in range(len(sections) - 1):
        first = index * count
        second = (index + 1) * count
        for i in range(count):
            j = (i + 1) % count
            faces.append((first + i, first + j, second + j, second + i))
    if caps:
        faces.append(tuple(range(count - 1, -1, -1)))
        base = (len(sections) - 1) * count
        faces.append(tuple(base + i for i in range(count)))

    obj = mesh_object(name, verts, faces, surface)
    recalc_normals(obj)
    return obj


def profile_box(half_x: float, half_z: float, cut: float) -> List[Tuple[float, float]]:
    return chamfer_rect(half_x, half_z, cut)


# --------------------------------------------------------------------------
# Turned parts: barrels, tubes, muzzle devices (a real bore, not a cap)
# --------------------------------------------------------------------------
def turned(name: str, y0: float, y1: float, radius0: float, radius1: float,
           centre_x: float = 0.0, centre_z: float = 0.0, bore: float = 0.0,
           segments: int = 26, surface: str = METAL,
           steps: Sequence[Sequence[float]] | None = None) -> bpy.types.Object:
    """A lathed part around the barrel axis (-Y), optionally hollow.

    ``steps`` lets a barrel carry its profile: [(y, radius), ...] with y from y0 to
    y1. A bore of zero closes the ends with a fan.
    """
    if steps is None:
        steps = [(y0, radius0), (y1, radius1)]
    rings = list(steps)
    verts: List[Tuple[float, float, float]] = []
    for y, radius in rings:
        for i in range(segments):
            angle = 2.0 * math.pi * i / segments
            verts.append((centre_x + radius * math.cos(angle), y,
                          centre_z + radius * math.sin(angle)))
    faces: List[tuple] = []
    for ring in range(len(rings) - 1):
        a = ring * segments
        b = (ring + 1) * segments
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((a + i, a + j, b + j, b + i))

    if bore > 1e-5:
        inner_base = len(verts)
        for y, _radius in rings:
            for i in range(segments):
                angle = 2.0 * math.pi * i / segments
                verts.append((centre_x + bore * math.cos(angle), y,
                              centre_z + bore * math.sin(angle)))
        for ring in range(len(rings) - 1):
            a = inner_base + ring * segments
            b = inner_base + (ring + 1) * segments
            for i in range(segments):
                j = (i + 1) % segments
                faces.append((a + i, b + i, b + j, a + j))
        for node in (0, len(rings) - 1):
            outer = node * segments
            inner = inner_base + node * segments
            for i in range(segments):
                j = (i + 1) % segments
                faces.append((outer + i, outer + j, inner + j, inner + i))
    else:
        first = len(verts)
        verts.append((centre_x, rings[0][0], centre_z))
        last = len(verts)
        verts.append((centre_x, rings[-1][0], centre_z))
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((first, j, i))
            base = (len(rings) - 1) * segments
            faces.append((last, base + i, base + j))

    obj = mesh_object(name, verts, faces, surface)
    recalc_normals(obj)
    return obj


def ring(name: str, y: float, radius: float, thickness: float, depth: float,
         centre_z: float = 0.0, segments: int = 26,
         surface: str = METAL) -> bpy.types.Object:
    return turned(name, y - depth * 0.5, y + depth * 0.5, radius, radius,
                  centre_z=centre_z, bore=radius - thickness, segments=segments,
                  surface=surface)


def subtract(target: bpy.types.Object, cutters: Sequence[bpy.types.Object]) -> None:
    """Boolean difference, applied immediately so the next cutter sees the result."""
    for cutter in cutters:
        modifier = target.modifiers.new("KordonCut", "BOOLEAN")
        modifier.operation = "DIFFERENCE"
        modifier.object = cutter
        modifier.solver = "EXACT"
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        try:
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        except RuntimeError:
            target.modifiers.remove(modifier)
        bpy.data.objects.remove(cutter, do_unlink=True)
    recalc_normals(target)


def slot_cutter(y: float, z: float, half_len: float, radius: float,
                x_centre: float, x_span: float, name: str = "slot") -> bpy.types.Object:
    """A capsule shaped cutter lying across X: the shape of every real port."""
    profile = stadium(half_len, radius, steps=5)
    return loft(profile, [(-x_span * 0.5, 1.0, 1.0, y, z),
                          (x_span * 0.5, 1.0, 1.0, y, z)],
                name=name, axis="X")


def drill(obj: bpy.types.Object, y: float, z: float, radius: float,
          x_span: float, name: str = "drill") -> None:
    """Round hole straight through a part along X (rivet, pin, sling slot)."""
    cutter = turned(name, -x_span * 0.5, x_span * 0.5, radius, radius,
                    centre_z=0.0, segments=16, surface=METAL)
    cutter.data.transform(mathutils.Matrix.Translation((0.0, y, z)))
    cutter.data.update()
    subtract(obj, [cutter])


# --------------------------------------------------------------------------
# Detail primitives
# --------------------------------------------------------------------------
def pin(centre: Sequence[float], radius: float, length: float,
        axis: str = "X", surface: str = BARE, name: str = "pin") -> bpy.types.Object:
    """A round pin, rivet or screw head: turned along Y, then stood on its axis."""
    obj = turned(name, -length * 0.5, length * 0.5, radius, radius, segments=14,
                 surface=surface)
    if axis == "X":
        obj.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
    elif axis == "Z":
        obj.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
    obj.data.transform(mathutils.Matrix.Translation(tuple(centre)))
    obj.data.update()
    return obj


def railed(name: str, y0: float, y1: float, z: float, width: float,
           height: float, surface: str = METAL, tooth: float = 0.0072) -> bpy.types.Object:
    """Picatinny rail as one lofted bar with a tooth every ten millimetres."""
    half = width * 0.5
    profile = chamfer_rect(half, height * 0.5, height * 0.30)
    sections: List[Sequence[float]] = []
    length = abs(y1 - y0)
    count = max(2, int(length / tooth))
    direction = 1.0 if y1 > y0 else -1.0
    sections.append((y0, 1.0, 0.72, 0.0, z))
    for index in range(count):
        start = y0 + direction * length * index / count
        end = y0 + direction * length * (index + 1) / count
        sections.append((start + direction * 0.0014, 1.0, 1.0, 0.0, z))
        sections.append((end - direction * 0.0014, 1.0, 1.0, 0.0, z))
        sections.append((end - direction * 0.0010, 0.74, 0.55, 0.0, z))
    sections.append((y1, 1.0, 0.72, 0.0, z))
    sections.sort(key=lambda section: section[0] * direction)
    return loft(profile, sections, surface=surface, name=name)


def panel(obj: bpy.types.Object, size: Sequence[float], centre: Sequence[float],
          rotation_deg: Sequence[float] = (0.0, 0.0, 0.0),
          surface: str = METAL, name: str = "panel") -> bpy.types.Object:
    """A raised or recessed plate: panel lines, M-LOK slots, ejection ports."""
    return boxpart(size, centre, surface=surface, rotation_deg=rotation_deg,
                   name=name)


def screw(centre: Sequence[float], radius: float = 0.0028, length: float = 0.004,
          axis: str = "X", name: str = "screw") -> List[bpy.types.Object]:
    bolt = pin(centre, radius, length, axis=axis, surface=BARE, name=name)
    return [bolt]


def sweep_yz(name: str, points: Sequence[Tuple[float, float]], radius: float,
             surface: str = METAL, sides: int = 8,
             x_centre: float = 0.0) -> bpy.types.Object:
    """Sweep a small circle along a polyline in the YZ plane.

    This is how the trigger guard is made: a real loop of round bar, not three
    boxes in a U.
    """
    verts: List[Tuple[float, float, float]] = []
    for index, (y, z) in enumerate(points):
        previous = points[max(0, index - 1)]
        following = points[min(len(points) - 1, index + 1)]
        tangent = mathutils.Vector((0.0, following[0] - previous[0],
                                    following[1] - previous[1]))
        if tangent.length < 1e-6:
            tangent = mathutils.Vector((0.0, 1.0, 0.0))
        tangent.normalize()
        normal = mathutils.Vector((1.0, 0.0, 0.0))
        binormal = tangent.cross(normal).normalized()
        for side in range(sides):
            angle = 2.0 * math.pi * side / sides
            offset = normal * (radius * math.cos(angle)) + binormal * (radius * math.sin(angle))
            verts.append((x_centre + offset.x, y + offset.y, z + offset.z))

    faces: List[tuple] = []
    for ring in range(len(points) - 1):
        a = ring * sides
        b = (ring + 1) * sides
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((a + i, a + j, b + j, b + i))
    first = len(verts)
    verts.append((x_centre, points[0][0], points[0][1]))
    last = len(verts)
    verts.append((x_centre, points[-1][0], points[-1][1]))
    base = (len(points) - 1) * sides
    for i in range(sides):
        j = (i + 1) % sides
        faces.append((first, j, i))
        faces.append((last, base + i, base + j))

    obj = mesh_object(name, verts, faces, surface)
    recalc_normals(obj)
    return obj


def sling_loop(centre: Sequence[float], radius: float = 0.011,
               axis: str = "X", name: str = "sling") -> List[bpy.types.Object]:
    plate = boxpart((0.006, 0.020, 0.014), centre, surface=METAL,
                    name=name + "_plate")
    rotation = (0.0, 90.0, 90.0) if axis == "X" else (0.0, 0.0, 0.0)
    loop = art.torus_band(radius, 0.0022, center=(centre[0], centre[1], centre[2] - 0.014),
                          rotation_deg=rotation, segments=18, surface=METAL,
                          name=name + "_ring")
    bake(loop)
    return [plate, loop]


# --------------------------------------------------------------------------
# Layout: every attachment is derived from the receiver, never hard coded twice
# --------------------------------------------------------------------------
def layout(spec: dict) -> dict:
    receiver_len = spec["receiver_len"]
    receiver_h = spec["receiver_h"]
    half_w = spec["half_w"]
    top_z = receiver_h * 0.5
    return dict(
        rl=receiver_len,
        rh=receiver_h,
        hw=half_w,
        rear_y=receiver_len * 0.55,
        front_y=-receiver_len * 0.45,
        top_z=top_z,
        bottom_z=-receiver_h * 0.5,
        bore_z=top_z - receiver_h * spec.get("bore_drop", 0.30),
        grip_y=receiver_len * 0.55 - receiver_len * spec.get("grip_frac", 0.26),
        mag_y=(receiver_len * 0.55 - receiver_len * 0.45
               + receiver_len * spec.get("mag_frac", 0.22)),
    )


# --------------------------------------------------------------------------
# Receivers
# --------------------------------------------------------------------------
def dust_cover(y0: float, y1: float, half_w: float, top_z: float) -> List[bpy.types.Object]:
    """Pressed top cover with two rolled ribs, the way a stamped AK is made."""
    profile = round_rect(half_w * 0.97, 0.020, 0.013, steps=3)
    body = loft(profile, [
        (y0, 0.92, 0.86, 0.0, top_z - 0.013),
        (y0 + 0.014, 1.00, 1.00, 0.0, top_z - 0.013),
        (y1 - 0.012, 1.00, 1.00, 0.0, top_z - 0.013),
        (y1, 0.94, 0.90, 0.0, top_z - 0.013),
    ], surface=METAL, name="dust_cover")
    parts = [body]
    for side in (-1.0, 1.0):
        parts.append(loft(chamfer_rect(0.0013, 0.0060, 0.0008), [
            (y0 + 0.020, 1.0, 0.55, side * half_w * 0.62, top_z + 0.0025),
            (y0 + 0.040, 1.0, 1.00, side * half_w * 0.62, top_z + 0.0025),
            (y1 - 0.030, 1.0, 1.00, side * half_w * 0.62, top_z + 0.0025),
            (y1 - 0.016, 1.0, 0.70, side * half_w * 0.62, top_z + 0.0025),
        ], surface=METAL, name="cover_rib"))
    return parts


def ejection_port(shell: bpy.types.Object, y: float, z: float, length: float,
                  height: float, half_w: float, side: float = 1.0) -> List[bpy.types.Object]:
    """Cut a pocket and put a bolt face at the bottom of it.

    A hole cut straight through a solid shell shows nothing; the pocket plus the
    carrier inside is what reads as a working action at viewmodel distance.
    """
    cutter = boxpart((0.020, length, height),
                     (side * (half_w + 0.002), y, z), surface=METAL,
                     name="port_cutter")
    subtract(shell, [cutter])
    parts: List[bpy.types.Object] = [
        turned("bolt", y - length * 0.62, y + length * 0.62, 0.0105, 0.0105,
               centre_x=side * (half_w - 0.014), centre_z=z, segments=18,
               surface=BARE),
        boxpart((0.003, length * 0.92, height * 0.86),
                (side * (half_w - 0.008), y, z), surface=RUBBER,
                name="port_shadow"),
        boxpart((0.0035, length * 0.16, height * 1.20),
                (side * (half_w + 0.0005), y + length * 0.58, z), surface=METAL,
                name="port_lip"),
    ]
    return parts


def receiver_ak(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], List[bpy.types.Object]]:
    """Stamped AK receiver: shell, top cover, selector, rivets, ejection port."""
    half_w = ctx["hw"]
    front_y = ctx["front_y"]
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    rh = ctx["rh"]
    parts: List[bpy.types.Object] = []

    shell = loft(chamfer_rect(half_w, rh * 0.5, 0.009), [
        (front_y + 0.012, 0.88, 0.92, 0.0, -0.002),
        (front_y, 0.96, 0.98, 0.0, 0.0),
        (front_y + 0.055, 1.00, 1.00, 0.0, 0.0),
        (rear_y - 0.030, 1.00, 1.00, 0.0, 0.0),
        (rear_y, 0.95, 0.95, 0.0, 0.0),
    ], surface=METAL, name="receiver")
    parts.append(shell)
    parts.extend(ejection_port(shell, front_y + 0.095, top_z - 0.019, 0.062, 0.024,
                               half_w))
    parts.extend(dust_cover(rear_y - 0.170, rear_y - 0.006, half_w, top_z))

    # selector lever on the right: the long stamped paddle of an AK
    parts.append(loft(chamfer_rect(0.0018, 0.008, 0.0015), [
        (rear_y - 0.055, 1.0, 1.0, half_w + 0.0016, top_z - 0.014),
        (rear_y - 0.070, 1.0, 1.6, half_w + 0.0016, top_z - 0.020),
        (front_y + 0.070, 1.0, 1.1, half_w + 0.0016, top_z - 0.024),
        (front_y + 0.050, 0.8, 0.8, half_w + 0.0016, top_z - 0.026),
    ], surface=BARE, name="selector"))
    for y in (front_y + 0.030, front_y + 0.070, rear_y - 0.045, rear_y - 0.085):
        for side in (-1.0, 1.0):
            parts.append(pin((side * (half_w + 0.0008), y, -0.018), 0.0034, 0.004,
                             name="rivet"))

    # magazine well flare and the rear tang that ties into the stock
    parts.append(loft(chamfer_rect(half_w * 1.06, 0.012, 0.004), [
        (ctx["mag_y"] + 0.048, 1.0, 1.0, 0.0, -rh * 0.5 - 0.004),
        (ctx["mag_y"] - 0.010, 0.82, 0.80, 0.0, -rh * 0.5 - 0.004),
    ], surface=METAL, name="magwell_flare"))
    parts.append(boxpart((0.030, 0.048, 0.012), (0.0, rear_y + 0.012, -0.030),
                         surface=METAL, name="tang"))

    sights: List[bpy.types.Object] = []
    # rear sight block and its leaf, standing on the receiver, not floating
    block = boxpart((0.030, 0.034, 0.020), (0.0, front_y + 0.010, top_z + 0.008),
                    surface=METAL, name="rear_sight_block")
    parts.append(block)
    leaf = loft(chamfer_rect(0.0135, 0.0022, 0.0012), [
        (front_y + 0.020, 1.0, 1.0, 0.0, top_z + 0.020),
        (front_y - 0.008, 1.0, 1.0, 0.0, top_z + 0.027),
    ], surface=METAL, name="sight_leaf")
    parts.append(leaf)
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.004, 0.014, 0.010),
                             (side * 0.0105, front_y - 0.002, top_z + 0.028),
                             surface=METAL, name="sight_ear"))
    sights.extend([leaf])
    return parts, sights


def receiver_ar(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], List[bpy.types.Object]]:
    """AR pattern: flat top upper with a rail, lower with a magwell and trigger."""
    half_w = ctx["hw"]
    front_y = ctx["front_y"]
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    rh = ctx["rh"]
    parts: List[bpy.types.Object] = []

    upper = loft(chamfer_rect(half_w, rh * 0.34, 0.010), [
        (front_y + 0.010, 0.90, 0.90, 0.0, top_z - rh * 0.30),
        (front_y, 0.97, 0.98, 0.0, top_z - rh * 0.30),
        (front_y + 0.070, 1.00, 1.00, 0.0, top_z - rh * 0.30),
        (rear_y - 0.020, 1.00, 1.00, 0.0, top_z - rh * 0.30),
        (rear_y, 0.94, 0.94, 0.0, top_z - rh * 0.30),
    ], surface=METAL, name="upper_receiver")
    parts.append(upper)
    parts.extend(ejection_port(upper, front_y + 0.085, top_z - rh * 0.26, 0.058, 0.024,
                               half_w))
    parts.append(railed("top_rail", front_y + 0.012, rear_y - 0.008,
                        top_z - rh * 0.30 + rh * 0.34 + 0.0035, 0.0215, 0.0075))

    lower = loft(chamfer_rect(half_w * 0.99, rh * 0.30, 0.008), [
        (ctx["mag_y"] - 0.075, 0.88, 0.86, 0.0, -rh * 0.30),
        (ctx["mag_y"] - 0.045, 1.00, 1.00, 0.0, -rh * 0.30),
        (ctx["mag_y"] + 0.040, 1.00, 1.00, 0.0, -rh * 0.30),
        (rear_y - 0.030, 0.96, 1.05, 0.0, -rh * 0.30),
        (rear_y, 0.80, 0.85, 0.0, -rh * 0.30),
    ], surface=METAL, name="lower_receiver")
    parts.append(lower)
    parts.append(loft(chamfer_rect(half_w * 1.05, 0.014, 0.004), [
        (ctx["mag_y"] + 0.030, 1.0, 1.0, 0.0, -rh * 0.58),
        (ctx["mag_y"] - 0.030, 0.86, 0.80, 0.0, -rh * 0.58),
    ], surface=METAL, name="magwell_flare"))
    # mag release and bolt catch, both on the left side of the lower
    parts.append(pin((-half_w - 0.001, ctx["mag_y"] + 0.028, -rh * 0.34), 0.0055,
                     0.006, name="mag_release"))
    parts.append(boxpart((0.006, 0.026, 0.012),
                         (-half_w - 0.001, ctx["mag_y"] + 0.080, -rh * 0.28),
                         surface=METAL, name="bolt_catch"))
    # forward assist and brass deflector
    parts.append(pin((half_w * 0.55, rear_y - 0.018, top_z - rh * 0.24), 0.008, 0.020,
                     axis="Y", name="forward_assist"))
    parts.append(loft(round_rect(0.008, 0.010, 0.004), [
        (rear_y - 0.006, 1.0, 1.0, half_w * 0.72, top_z - rh * 0.30),
        (rear_y - 0.030, 1.0, 1.0, half_w * 0.86, top_z - rh * 0.30),
    ], surface=METAL, name="deflector"))
    # charging handle at the rear of the upper
    parts.append(boxpart((0.026, 0.030, 0.010), (0.0, rear_y + 0.004, top_z - rh * 0.20),
                         surface=METAL, name="charging_handle"))
    for side in (-1.0, 1.0):
        parts.append(pin((side * 0.011, rear_y + 0.002, top_z - rh * 0.20), 0.004,
                         0.020, axis="Y", name="handle_wing"))

    # buffer tube, castle nut and end plate: the stock mounts on these
    tube_len = 0.195
    parts.append(turned("buffer_tube", rear_y - 0.010, rear_y + tube_len, 0.0165, 0.0165,
                        centre_z=top_z - rh * 0.34, segments=22, surface=METAL))
    parts.append(ring("castle_nut", rear_y + 0.018, 0.0195, 0.0045, 0.020,
                      centre_z=top_z - rh * 0.34, segments=22))
    sights: List[bpy.types.Object] = []
    return parts, sights


def receiver_smg(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], List[bpy.types.Object]]:
    """Polymer shelled compact: a slab body with a moulded grip tang and vents."""
    half_w = ctx["hw"]
    front_y = ctx["front_y"]
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    rh = ctx["rh"]
    parts: List[bpy.types.Object] = []

    shell = loft(round_rect(half_w, rh * 0.5, 0.014, steps=3), [
        (front_y + 0.010, 0.88, 0.90, 0.0, 0.0),
        (front_y, 0.97, 0.98, 0.0, 0.0),
        (front_y + 0.050, 1.00, 1.00, 0.0, 0.0),
        (rear_y - 0.020, 1.00, 1.00, 0.0, 0.0),
        (rear_y, 0.93, 0.94, 0.0, 0.0),
    ], surface=POLYMER, name="shell")
    parts.append(shell)
    parts.extend(ejection_port(shell, front_y + 0.075, top_z - 0.016, 0.052, 0.021,
                               half_w))
    parts.append(railed("top_rail", front_y + 0.010, rear_y - 0.012,
                        top_z + 0.004, 0.019, 0.006))
    # moulded side panels and a set of cooling slots on the handguard line
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.0025, 0.090, 0.026),
                             (side * (half_w + 0.0006), rear_y - 0.085, -0.004),
                             surface=POLYMER, name="side_panel"))
    # charging handle on the left, cocking slot along the top cover
    parts.append(boxpart((0.012, 0.016, 0.012),
                         (-half_w - 0.004, rear_y - 0.055, top_z - 0.004),
                         surface=BARE, name="charging_handle"))
    parts.append(boxpart((0.006, 0.070, 0.004),
                         (-half_w * 0.4, rear_y - 0.055, top_z + 0.001),
                         surface=RUBBER, name="cocking_slot"))
    # iron sights: a protected rear notch and a front post ring
    rear = boxpart((0.026, 0.012, 0.014), (0.0, rear_y - 0.020, top_z + 0.014),
                   surface=METAL, name="rear_sight")
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.005, 0.012, 0.016),
                             (side * 0.0105, rear_y - 0.020, top_z + 0.017),
                             surface=METAL, name="rear_ear"))
    parts.append(rear)
    front_y_far = ctx["front_y"] - spec["barrel_len"] + 0.020
    front = boxpart((0.022, 0.014, 0.012), (0.0, front_y_far, top_z + 0.010),
                    surface=METAL, name="front_sight")
    parts.append(front)
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.004, 0.014, 0.024),
                             (side * 0.009, front_y_far, top_z + 0.020),
                             surface=METAL, name="front_ear"))
    parts.append(pin((0.0, front_y_far, top_z + 0.016), 0.0016, 0.012, axis="Z",
                     name="front_post"))
    return parts, [rear, front]


def receiver_bolt(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], List[bpy.types.Object]]:
    """Bolt action: round receiver, bolt handle, chassis rails underneath."""
    half_w = ctx["hw"]
    front_y = ctx["front_y"]
    rear_y = ctx["rear_y"]
    bore_z = ctx["bore_z"]
    parts: List[bpy.types.Object] = []

    body = turned("action", front_y + 0.006, rear_y, half_w * 1.02, half_w * 1.02,
                  centre_z=bore_z, segments=24, surface=METAL,
                  steps=[(front_y + 0.006, half_w * 1.06),
                         (front_y - 0.010, half_w * 1.10),
                         (rear_y - 0.030, half_w * 1.02),
                         (rear_y, half_w * 0.92)])
    parts.append(body)
    parts.append(ring("recoil_lug", front_y - 0.030, half_w * 1.16, 0.004, 0.014,
                      centre_z=bore_z, segments=24))
    # bolt: a cylinder riding in the action, with the handle out to the right
    parts.append(turned("bolt_body", rear_y - 0.075, rear_y + 0.014, half_w * 0.62,
                        half_w * 0.62, centre_z=bore_z + 0.004, segments=18,
                        surface=BARE))
    parts.append(pin((half_w * 0.9, rear_y - 0.052, bore_z), 0.0055, 0.055,
                     axis="X", surface=BARE, name="bolt_handle"))
    parts.append(art.torus_band(0.011, 0.0055,
                                center=(half_w + 0.030, rear_y - 0.052, bore_z),
                                rotation_deg=(0.0, 90.0, 0.0), segments=16,
                                surface=BARE, name="bolt_knob"))
    bake(parts[-1])
    # ejection port on the right of the action
    cutter = boxpart((0.020, 0.052, 0.018), (half_w * 1.05, front_y + 0.070, bore_z),
                     surface=METAL, name="port_cutter")
    subtract(body, [cutter])
    parts.append(boxpart((0.003, 0.048, 0.016), (half_w * 0.72, front_y + 0.070, bore_z),
                         surface=RUBBER, name="port_shadow"))
    # trigger housing and stock bolts
    parts.append(boxpart((half_w * 1.5, 0.090, 0.020), (0.0, ctx["grip_y"] + 0.010,
                                                        bore_z - 0.046),
                         surface=METAL, name="trigger_housing"))
    for side in (-1.0, 1.0):
        parts.append(pin((side * half_w * 0.82, ctx["grip_y"] - 0.020, bore_z - 0.046),
                         0.0035, 0.006, name="action_screw"))
    # a 20 MOA rail for the scope
    parts.append(railed("scope_rail", front_y + 0.020, rear_y - 0.020,
                        bore_z + half_w * 1.10 + 0.004, 0.021, 0.008))
    sights: List[bpy.types.Object] = []
    return parts, sights


def receiver_shotgun(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], List[bpy.types.Object]]:
    """Pump action receiver: port, loading gate, cross bolt safety."""
    half_w = ctx["hw"]
    front_y = ctx["front_y"]
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    rh = ctx["rh"]
    bore_z = ctx["bore_z"]
    parts: List[bpy.types.Object] = []

    shell = loft(chamfer_rect(half_w, rh * 0.5, 0.009), [
        (front_y + 0.010, 0.90, 0.92, 0.0, 0.0),
        (front_y, 0.98, 0.99, 0.0, 0.0),
        (front_y + 0.050, 1.00, 1.00, 0.0, 0.0),
        (rear_y - 0.024, 1.00, 1.00, 0.0, 0.0),
        (rear_y, 0.92, 0.94, 0.0, 0.0),
    ], surface=METAL, name="receiver")
    parts.append(shell)
    parts.extend(ejection_port(shell, front_y + 0.070, bore_z + 0.006, 0.056, 0.022,
                               half_w))
    # loading gate under the receiver and the shell lifter above it
    parts.append(boxpart((0.030, 0.060, 0.004), (0.0, front_y + 0.075, -rh * 0.48),
                         surface=BARE, name="loading_gate"))
    # cross bolt safety behind the trigger guard on the lower tang
    parts.append(pin((0.0, ctx["grip_y"] + 0.030, -rh * 0.52), 0.004, 0.014,
                     axis="X", surface=BARE, name="safety"))
    # bead front sight base
    sights: List[bpy.types.Object] = []
    return parts, sights


# --------------------------------------------------------------------------
# Barrels and muzzle devices
# --------------------------------------------------------------------------
def barrel(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], float]:
    """Bore, chamber step and muzzle thread. Returns the parts and the muzzle Y."""
    radius = spec["barrel_r"]
    front_y = ctx["front_y"]
    bore_z = ctx["bore_z"]
    end = front_y - spec["barrel_len"]
    steps = [
        (front_y + 0.008, radius * 1.42),
        (front_y - 0.052, radius * 1.34),
        (front_y - 0.086, radius * 1.06),
        (end + 0.070, radius * 1.00),
        (end + 0.012, radius * 0.94),
        (end, radius * 0.90),
    ]
    parts = [turned("barrel", steps[0][0], end, radius, radius,
                    centre_z=bore_z, segments=24, surface=METAL,
                    bore=radius * 0.52, steps=steps)]
    parts.append(ring("barrel_shoulder", front_y - 0.010, radius * 1.48, 0.003, 0.012,
                      centre_z=bore_z, segments=24))
    return parts, end


def muzzle_device(style: str, y_end: float, radius: float, z: float
                  ) -> Tuple[List[bpy.types.Object], float]:
    """Muzzle device with real cut ports. Returns the parts and how far it reaches."""
    parts: List[bpy.types.Object] = []
    if style == "none":
        return parts, 0.0
    if style == "ak_brake":
        length = 0.058
        body = turned("brake", y_end - length, y_end, radius * 1.55, radius * 1.55,
                      centre_z=z, segments=22, surface=METAL, bore=radius * 0.66)
        cutters = []
        for side in (-1.0, 1.0):
            for index in range(2):
                cutters.append(slot_cutter(y_end - 0.016 - index * 0.021, z, 0.0062,
                                           0.0042, side * radius * 1.1, 0.030))
        cutters.append(slot_cutter(y_end - 0.038, z + radius * 1.20, 0.0060, 0.0032,
                                   0.0, 0.026))
        subtract(body, cutters)
        parts.extend([body,
                      ring("brake_crown", y_end - length + 0.003, radius * 1.62,
                           0.0022, 0.005, centre_z=z, segments=22)])
        return parts, length
    if style == "birdcage":
        length = 0.062
        body = turned("hider", y_end - length, y_end, radius * 1.45, radius * 1.50,
                      centre_z=z, segments=22, surface=METAL, bore=radius * 0.70,
                      steps=[(y_end - length, radius * 1.45),
                             (y_end - length + 0.008, radius * 1.52),
                             (y_end - 0.004, radius * 1.44)])
        cutters = []
        for index in range(4):
            angle = math.radians(45.0 + 90.0 * index)
            cutter = slot_cutter(y_end - 0.020, z, 0.0075, 0.0038, 0.0, 0.020)
            cutter.data.transform(mathutils.Matrix.Rotation(angle, 4, "Y"))
            cutter.data.update()
            cutters.append(cutter)
        subtract(body, cutters)
        parts.extend([body,
                      ring("hider_rim", y_end - length + 0.004, radius * 1.58, 0.002, 0.006,
                           centre_z=z, segments=22)])
        return parts, length
    if style == "sniper":
        length = 0.075
        body = turned("brake", y_end - length, y_end, radius * 1.62, radius * 1.52,
                      centre_z=z, segments=22, surface=METAL, bore=radius * 0.68)
        cutters = []
        for side in (-1.0, 1.0):
            for index in range(3):
                cutters.append(slot_cutter(y_end - 0.018 - index * 0.019, z, 0.0056,
                                           0.0040, side * radius * 1.2, 0.034))
        for index in range(2):
            cutters.append(slot_cutter(y_end - 0.026 - index * 0.024, z + radius * 1.15,
                                       0.0050, 0.0028, 0.0, 0.024))
        subtract(body, cutters)
        parts.append(body)
        return parts, length
    if style == "cone":
        length = 0.040
        body = turned("flash_hider", y_end - length, y_end, radius * 1.55, radius * 1.30,
                      centre_z=z, segments=22, surface=METAL, bore=radius * 0.72)
        cutters = []
        for side in (-1.0, 1.0):
            cutters.append(slot_cutter(y_end - 0.016, z, 0.0060, 0.0032,
                                       side * radius * 1.1, 0.028))
        subtract(body, cutters)
        parts.append(body)
        return parts, length
    return parts, 0.0


# --------------------------------------------------------------------------
# Handguards
# --------------------------------------------------------------------------
def handguard_wood(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """AK fore-end: walnut lower with a belly, a separate upper over the gas tube."""
    front_y = ctx["front_y"]
    bore_z = ctx["bore_z"]
    length = spec.get("handguard_len", 0.185)
    start = front_y - 0.010
    end = start - length
    parts: List[bpy.types.Object] = []

    lower = loft(round_rect(0.0245, 0.0215, 0.008, steps=3), [
        (start, 0.82, 0.76, 0.0, bore_z - 0.011),
        (start - length * 0.12, 1.00, 1.00, 0.0, bore_z - 0.014),
        (start - length * 0.62, 1.02, 1.02, 0.0, bore_z - 0.016),
        (end + 0.010, 0.95, 0.92, 0.0, bore_z - 0.014),
        (end, 0.86, 0.80, 0.0, bore_z - 0.012),
    ], surface=WOOD, name="handguard_lower")
    parts.append(lower)
    # finger grooves across the belly of the fore-end
    for index in range(3):
        y = start - length * (0.30 + 0.16 * index)
        parts.append(loft(round_rect(0.0235, 0.0030, 0.0025, steps=2), [
            (y - 0.0035, 1.0, 1.0, 0.0, bore_z - 0.030),
            (y + 0.0035, 1.0, 1.0, 0.0, bore_z - 0.030),
        ], surface=WOOD, name="groove"))

    upper_start = start - length * 0.06
    parts.append(loft(round_rect(0.0165, 0.0120, 0.006, steps=3), [
        (upper_start, 0.86, 0.80, 0.0, bore_z + 0.0235),
        (upper_start - length * 0.16, 1.00, 1.00, 0.0, bore_z + 0.0250),
        (upper_start - length * 0.74, 1.00, 1.00, 0.0, bore_z + 0.0250),
        (upper_start - length * 0.92, 0.88, 0.84, 0.0, bore_z + 0.0240),
    ], surface=WOOD, name="handguard_upper"))
    # iron ferrule at each end of the lower, and the gas tube under the upper
    for y, radius in ((start - 0.002, 0.0275), (end + 0.004, 0.0265)):
        parts.append(ring("ferrule", y, radius, 0.0025, 0.010, centre_z=bore_z - 0.012,
                          segments=20))
    parts.append(turned("gas_tube", upper_start - 0.004, upper_start - length * 0.90,
                        0.0090, 0.0090, centre_z=bore_z + 0.0245, segments=18,
                        surface=METAL, bore=0.0072))
    return parts


def handguard_mlok(ctx: dict, spec: dict, surface: str) -> List[bpy.types.Object]:
    """Free float tube with recessed M-LOK panels and a toothed top rail."""
    front_y = ctx["front_y"]
    bore_z = ctx["bore_z"]
    radius = spec["barrel_r"] * spec.get("handguard_scale", 3.0)
    length = spec.get("handguard_len", 0.255)
    start = front_y - 0.012
    end = start - length
    parts: List[bpy.types.Object] = []

    shell = loft(octagon_profile(radius, radius * 0.96, radius * 0.22), [
        (start, 0.90, 0.90, 0.0, bore_z),
        (start - 0.020, 1.00, 1.00, 0.0, bore_z),
        (end + 0.010, 1.00, 1.00, 0.0, bore_z),
        (end, 0.94, 0.94, 0.0, bore_z),
    ], surface=surface, name="handguard")
    parts.append(shell)
    parts.append(ring("barrel_nut", start + 0.004, radius * 1.02, 0.0055, 0.024,
                      centre_z=bore_z, segments=24))

    # M-LOK slots: pockets 3 mm deep on both cheeks, with a dark floor inside
    slots = max(3, int(length / 0.048))
    for side in (-1.0, 1.0):
        for index in range(slots):
            y = start - length * (index + 0.72) / (slots + 0.3)
            cutter = slot_cutter(y, bore_z - 0.002, 0.0155, 0.0046,
                                 side * (radius + 0.005), 0.016)
            subtract(shell, [cutter])
            parts.append(boxpart((0.0024, 0.031, 0.0092),
                                 (side * (radius - 0.0055), y, bore_z - 0.002),
                                 surface=RUBBER, name="mlok_floor"))
    # top rail continuing the receiver line, and an accessory rail underneath
    parts.append(railed("handguard_rail", start - 0.006, end + 0.010,
                        bore_z + radius * 0.94 + 0.0035, 0.0210, 0.0062))
    parts.append(railed("under_rail", start - 0.030, start - length * 0.45,
                        bore_z - radius * 0.94 - 0.002, 0.019, 0.006))
    return parts


def handguard_pump(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Shotgun fore-end: ribbed walnut slide over the tube magazine."""
    front_y = ctx["front_y"]
    bore_z = ctx["bore_z"]
    length = spec.get("handguard_len", 0.190)
    start = front_y - 0.085
    end = start - length
    parts: List[bpy.types.Object] = [
        loft(round_rect(0.0245, 0.0245, 0.012, steps=3), [
            (start, 0.86, 0.84, 0.0, bore_z - 0.026),
            (start - length * 0.14, 1.00, 1.00, 0.0, bore_z - 0.029),
            (end + length * 0.10, 0.98, 0.98, 0.0, bore_z - 0.030),
            (end, 0.86, 0.84, 0.0, bore_z - 0.027),
        ], surface=WOOD, name="pump"),
    ]
    for index in range(6):
        y = start - length * (0.16 + 0.135 * index)
        parts.append(loft(round_rect(0.0240, 0.0026, 0.0022, steps=2), [
            (y - 0.0032, 1.0, 1.0, 0.0, bore_z - 0.030),
            (y + 0.0032, 1.0, 1.0, 0.0, bore_z - 0.030),
        ], surface=WOOD, name="pump_rib"))
    return parts


def octagon_profile(half_x: float, half_z: float, cut: float) -> List[Tuple[float, float]]:
    return chamfer_rect(half_x, half_z, cut)


# --------------------------------------------------------------------------
# Stocks
# --------------------------------------------------------------------------
def stock_ak(ctx: dict, spec: dict, surface: str = WOOD) -> List[bpy.types.Object]:
    """Fixed walnut stock: comb, drop at the heel, sling slot, steel butt plate."""
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    length = spec.get("stock_len", 0.285)
    start = rear_y + 0.002
    end = start + length
    comb = top_z - 0.006
    parts: List[bpy.types.Object] = [
        loft(chamfer_rect(0.0195, 0.0450, 0.013), [
            (start, 0.80, 0.60, 0.0, comb - 0.032),
            (start + 0.030, 0.92, 0.72, 0.0, comb - 0.038),
            (start + length * 0.42, 1.00, 0.94, 0.0, comb - 0.048),
            (end - 0.030, 1.00, 1.00, 0.0, comb - 0.053),
            (end, 0.96, 0.98, 0.0, comb - 0.055),
        ], surface=surface, name="stock"),
        # comb strip: the raised line along the top of an AK stock
        loft(round_rect(0.0125, 0.0035, 0.0028, steps=2), [
            (start + 0.050, 0.9, 0.6, 0.0, comb + 0.002),
            (start + 0.070, 1.0, 1.0, 0.0, comb + 0.002),
            (end - 0.030, 1.0, 1.0, 0.0, comb + 0.002),
            (end - 0.016, 0.8, 0.7, 0.0, comb + 0.002),
        ], surface=surface, name="comb"),
        boxpart((0.040, 0.012, 0.100), (0.0, end + 0.004, comb - 0.052),
                surface=BARE, rotation_deg=(-4.0, 0.0, 0.0), name="butt_plate"),
    ]
    # sling slot through the butt
    slot = slot_cutter(end - 0.052, comb - 0.058, 0.0135, 0.0042, 0.0, 0.060)
    subtract(parts[0], [slot])
    for side in (-1.0, 1.0):
        parts.append(pin((side * 0.0165, end - 0.018, comb - 0.020), 0.0034, 0.004,
                         name="butt_screw"))
    return parts


def stock_ar(ctx: dict, spec: dict, surface: str = POLYMER,
             cheek_riser: bool = False) -> List[bpy.types.Object]:
    """Collapsible stock riding the buffer tube, with a rubber recoil pad."""
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    rh = ctx["rh"]
    axis_z = top_z - rh * 0.34
    length = spec.get("stock_len", 0.240)
    start = rear_y + 0.024
    end = start + length
    parts: List[bpy.types.Object] = [
        loft(round_rect(0.0205, 0.0360, 0.007, steps=2), [
            (start, 0.84, 0.72, 0.0, axis_z - 0.004),
            (start + 0.030, 1.00, 0.96, 0.0, axis_z - 0.008),
            (end - 0.030, 1.00, 1.00, 0.0, axis_z - 0.011),
            (end, 0.92, 0.94, 0.0, axis_z - 0.012),
        ], surface=surface, name="stock"),
        boxpart((0.038, 0.014, 0.0850), (0.0, end + 0.005, axis_z - 0.012),
                surface=RUBBER, name="recoil_pad"),
        # adjustment lever under the stock and the QD sling socket
        boxpart((0.020, 0.026, 0.012), (0.0, start + 0.045, axis_z - 0.044),
                surface=METAL, name="adjust_lever"),
    ]
    if cheek_riser:
        parts.append(loft(round_rect(0.0165, 0.0060, 0.005, steps=2), [
            (start + 0.030, 0.9, 0.6, 0.0, axis_z + 0.040),
            (start + 0.050, 1.0, 1.0, 0.0, axis_z + 0.041),
            (end - 0.030, 1.0, 1.0, 0.0, axis_z + 0.041),
        ], surface=surface, name="cheek_riser"))
    parts.extend(sling_loop((0.0, end - 0.070, axis_z - 0.048), radius=0.010,
                            axis="X", name="stock_qd"))
    return parts


def stock_chassis(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Skeleton chassis: side plates with a cut-out, cheek riser, recoil pad."""
    rear_y = ctx["rear_y"]
    bore_z = ctx["bore_z"]
    length = spec.get("stock_len", 0.300)
    start = rear_y - 0.010
    end = start + length
    parts: List[bpy.types.Object] = []
    for side in (-1.0, 1.0):
        plate = boxpart((0.013, length, 0.096),
                        (side * 0.019, start + length * 0.5, bore_z - 0.050),
                        surface=OLIVE, name="chassis_plate")
        opening = slot_cutter(start + length * 0.54, bore_z - 0.048, 0.040, 0.014,
                              side * 0.022, 0.024)
        subtract(plate, [opening])
        parts.append(plate)
    parts.append(boxpart((0.044, 0.040, 0.030), (0.0, start + 0.020, bore_z - 0.030),
                         surface=OLIVE, name="chassis_top"))
    # cheek riser on two posts
    parts.append(boxpart((0.032, 0.130, 0.016), (0.0, start + 0.120, bore_z - 0.004),
                         surface=RUBBER, name="cheek_riser"))
    for y in (start + 0.070, start + 0.170):
        parts.append(pin((0.0, y, bore_z - 0.012), 0.005, 0.040, axis="Z",
                         surface=BARE, name="riser_post"))
    parts.append(boxpart((0.042, 0.020, 0.118), (0.0, end + 0.004, bore_z - 0.060),
                         surface=RUBBER, rotation_deg=(-3.0, 0.0, 0.0),
                         name="recoil_pad"))
    # butt hook: the chassis underside curves up into the shoulder
    parts.append(loft(chamfer_rect(0.020, 0.012, 0.005), [
        (end - 0.060, 0.8, 0.8, 0.0, bore_z - 0.118),
        (end - 0.010, 1.0, 1.0, 0.0, bore_z - 0.122),
        (end + 0.010, 0.7, 0.7, 0.0, bore_z - 0.112),
    ], surface=OLIVE, name="butt_hook"))
    parts.append(pin((0.0, end - 0.020, bore_z - 0.130), 0.008, 0.020, axis="Y",
                     surface=BARE, name="length_knob"))
    parts.append(loft(chamfer_rect(0.020, 0.024, 0.008), [
        (start - 0.004, 1.0, 1.0, 0.0, bore_z - 0.052),
        (start + 0.040, 1.0, 1.0, 0.0, bore_z - 0.052),
    ], surface=OLIVE, name="grip_base"))
    return parts


def stock_shotgun(ctx: dict, spec: dict, surface: str = WOOD) -> List[bpy.types.Object]:
    """Straight walnut butt stock with a rubber pad and a slim wrist."""
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    length = spec.get("stock_len", 0.280)
    start = rear_y + 0.002
    end = start + length
    comb = top_z - 0.004
    parts = [
        loft(chamfer_rect(0.0185, 0.0430, 0.014), [
            (start, 0.80, 0.72, 0.0, comb - 0.048),
            (start + 0.040, 1.00, 1.00, 0.0, comb - 0.050),
            (start + length * 0.50, 0.98, 0.97, 0.0, comb - 0.056),
            (end - 0.016, 0.96, 0.95, 0.0, comb - 0.062),
            (end, 0.94, 0.93, 0.0, comb - 0.064),
        ], surface=surface, name="stock"),
        boxpart((0.036, 0.016, 0.106), (0.0, end + 0.006, comb - 0.058),
                surface=RUBBER, rotation_deg=(-5.0, 0.0, 0.0), name="butt_pad"),
    ]
    return parts


def stock_wire(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Folding wire stock: two rods and a shoulder plate, deployed."""
    rear_y = ctx["rear_y"]
    top_z = ctx["top_z"]
    length = spec.get("stock_len", 0.190)
    start = rear_y + 0.006
    end = start + length
    parts: List[bpy.types.Object] = [
        boxpart((0.034, 0.022, 0.030), (0.0, start, top_z - 0.030),
                surface=METAL, name="hinge"),
    ]
    for side in (-1.0, 1.0):
        parts.append(sweep_yz("rod", [(start, top_z - 0.034),
                                      (start + length * 0.55, top_z - 0.040),
                                      (end - 0.010, top_z - 0.046)],
                              0.0042, surface=METAL, sides=8, x_centre=side * 0.015))
        parts.append(sweep_yz("rod_low", [(start, top_z - 0.060),
                                          (start + length * 0.50, top_z - 0.070),
                                          (end - 0.010, top_z - 0.076)],
                              0.0036, surface=METAL, sides=8, x_centre=side * 0.015))
    parts.append(loft(round_rect(0.018, 0.026, 0.009, steps=2), [
        (end - 0.012, 0.9, 0.9, 0.0, top_z - 0.058),
        (end, 1.0, 1.0, 0.0, top_z - 0.058),
    ], surface=RUBBER, name="shoulder_plate"))
    return parts


# --------------------------------------------------------------------------
# Grips and the trigger group
# --------------------------------------------------------------------------
def grip_rifle(ctx: dict, spec: dict, surface: str = POLYMER,
               length: float = 0.108, rake: float = 17.0) -> List[bpy.types.Object]:
    """Raked pistol grip with a palm swell and finger grooves on the front strap."""
    top = ctx["bottom_z"] + 0.014
    y_top = ctx["grip_y"]
    steps = 7
    sections: List[Sequence[float]] = []
    for index in range(steps):
        t = index / (steps - 1)
        depth = length * t
        scale = 1.0 - 0.16 * t * t
        bulge = 1.0 + 0.10 * math.sin(math.pi * min(1.0, t * 1.2))
        y = y_top - math.tan(math.radians(rake)) * depth
        sections.append((top - depth, scale * bulge, scale, 0.0, y))
    parts = [loft(round_rect(0.0165, 0.0215, 0.010, steps=3), sections,
                  surface=surface, name="grip", axis="Z")]
    # front strap grooves
    for index in range(3):
        t = 0.22 + index * 0.18
        depth = length * t
        parts.append(loft(round_rect(0.0125, 0.0022, 0.0018, steps=2), [
            (top - depth - 0.003, 1.0, 1.0, 0.0,
             y_top - math.tan(math.radians(rake)) * depth - 0.0175),
            (top - depth + 0.003, 1.0, 1.0, 0.0,
             y_top - math.tan(math.radians(rake)) * depth - 0.0175),
        ], surface=surface, name="grip_groove", axis="Z"))
    # stippled side panels and the grip screw
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.0018, 0.036, 0.052),
                             (side * 0.0135, y_top - 0.020, top - length * 0.55),
                             surface=RUBBER, rotation_deg=(0.0, 0.0, 0.0),
                             name="grip_panel"))
    parts.append(pin((0.0, y_top - 0.012, top - length * 0.62), 0.0040, 0.030,
                     axis="X", surface=BARE, name="grip_screw"))
    return parts


def trigger_group(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Trigger guard swept along a polyline, with a curved trigger blade inside."""
    z_top = ctx["bottom_z"] + 0.006
    # the guard spans the gap between the magazine and the grip, the way it does on
    # the real thing, instead of hanging off one hard coded point
    y_front = (ctx["mag_y"] + 0.046) if spec.get("mag") else (ctx["rear_y"] - 0.030)
    y_rear = ctx["grip_y"] - 0.024
    drop = spec.get("guard_drop", 0.026)
    points = [(y_front, z_top),
              (y_front - 0.002, z_top - drop * 0.62),
              (y_front + 0.010, z_top - drop),
              (y_rear - 0.012, z_top - drop),
              (y_rear, z_top - drop * 0.60),
              (y_rear + 0.002, z_top)]
    parts = [sweep_yz("trigger_guard", points, 0.0038, surface=METAL, sides=8)]
    blade = loft(chamfer_rect(0.0040, 0.0075, 0.0020), [
        (z_top - 0.004, 1.0, 1.0, 0.0, y_front + 0.014),
        (z_top - 0.016, 0.9, 0.9, 0.0, y_front + 0.016),
        (z_top - 0.026, 0.7, 0.7, 0.0, y_front + 0.021),
    ], surface=BARE, name="trigger", axis="Z")
    parts.append(blade)
    parts.append(pin((0.0, y_front + 0.016, z_top - 0.004), 0.0030, 0.026, axis="X",
                     surface=BARE, name="trigger_pin"))
    return parts


# --------------------------------------------------------------------------
# Sights and optics
# --------------------------------------------------------------------------
def gas_block(ctx: dict, y: float, spec: dict) -> List[bpy.types.Object]:
    bore_z = ctx["bore_z"]
    half_w = ctx["hw"]
    radius = spec["barrel_r"]
    parts = [
        loft(chamfer_rect(half_w * 0.62, radius * 1.6, 0.004), [
            (y + 0.014, 0.9, 0.9, 0.0, bore_z + 0.004),
            (y, 1.0, 1.0, 0.0, bore_z + 0.008),
            (y - 0.014, 0.9, 0.9, 0.0, bore_z + 0.006),
        ], surface=METAL, name="gas_block"),
        turned("gas_pipe", y + 0.016, y + 0.030, 0.0092, 0.0092, centre_z=bore_z + 0.024,
               segments=16, surface=METAL, bore=0.0074),
    ]
    return parts


def front_sight_block(ctx: dict, y: float, spec: dict) -> Tuple[List[bpy.types.Object], float]:
    """AK front sight: a forged block with two protective ears and a post."""
    bore_z = ctx["bore_z"]
    half_w = ctx["hw"]
    top = bore_z + spec["barrel_r"] * 2.2 + 0.013
    parts: List[bpy.types.Object] = [
        loft(chamfer_rect(half_w * 0.58, 0.016, 0.004), [
            (y + 0.012, 0.85, 0.85, 0.0, bore_z + 0.010),
            (y, 1.0, 1.0, 0.0, bore_z + 0.014),
            (y - 0.012, 0.85, 0.85, 0.0, bore_z + 0.010),
        ], surface=METAL, name="front_sight_base"),
    ]
    for side in (-1.0, 1.0):
        parts.append(loft(chamfer_rect(0.0042, 0.0085, 0.002), [
            (y + 0.008, 1.0, 1.0, side * 0.0100, top - 0.005),
            (y - 0.008, 1.0, 1.0, side * 0.0100, top - 0.005),
        ], surface=METAL, name="front_ear"))
    parts.append(pin((0.0, y, top - 0.002), 0.0018, 0.020, axis="Z", name="front_post"))
    return parts, top


def optic_reddot(ctx: dict, spec: dict, rail_z: float, y: float
                 ) -> Tuple[List[bpy.types.Object], float]:
    """Aimpoint style tube on a riser: mount, body, hood, turret, glass."""
    tube_r = 0.0165
    axis_z = rail_z + 0.034
    length = 0.082
    parts: List[bpy.types.Object] = [
        boxpart((0.030, 0.048, 0.012), (0.0, y, rail_z + 0.006), surface=METAL,
                name="optic_mount"),
        boxpart((0.022, 0.020, 0.020), (0.0, y + 0.014, rail_z + 0.014),
                surface=METAL, name="optic_riser"),
        turned("optic_body", y - length * 0.5, y + length * 0.5, tube_r, tube_r,
               centre_z=axis_z, segments=24, surface=METAL, bore=tube_r - 0.0025),
        ring("optic_bell", y - length * 0.5 + 0.008, tube_r * 1.14, 0.0028, 0.012,
             centre_z=axis_z, segments=24),
        ring("optic_eyepiece", y + length * 0.5 - 0.008, tube_r * 1.10, 0.0028, 0.010,
             centre_z=axis_z, segments=24),
        turned("lens", y - length * 0.5 + 0.004, y - length * 0.5 + 0.0065,
               tube_r - 0.0035, tube_r - 0.0035, centre_z=axis_z, segments=24,
               surface=GLASS),
        turned("aperture", y - length * 0.5 + 0.0066, y - length * 0.5 + 0.0100,
               tube_r - 0.0032, tube_r - 0.0032, centre_z=axis_z, segments=24,
               surface=RUBBER),
    ]
    # the elevation turret has to stand up, not lie along the barrel
    turret = turned("elevation", -0.008, 0.008, 0.0085, 0.0085, segments=16,
                    surface=METAL)
    turret.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
    turret.data.transform(mathutils.Matrix.Translation((0.0, y - 0.004,
                                                       axis_z + tube_r + 0.004)))
    turret.data.update()
    parts.append(turret)
    windage = turned("windage", -0.007, 0.007, 0.0080, 0.0080, segments=16,
                     surface=METAL)
    windage.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
    windage.data.transform(mathutils.Matrix.Translation((tube_r + 0.004, y - 0.004,
                                                         axis_z)))
    windage.data.update()
    parts.append(windage)
    parts.append(pin((0.0, y - 0.026, rail_z + 0.006), 0.0035, 0.034, axis="X",
                     surface=BARE, name="mount_bolt"))
    return parts, axis_z + tube_r


def optic_prism(ctx: dict, spec: dict, rail_z: float, y: float
                ) -> Tuple[List[bpy.types.Object], float]:
    """Prism scope: a shorter, fatter tube with a big objective and a turret cluster."""
    tube_r = 0.0185
    axis_z = rail_z + 0.040
    length = 0.128
    parts: List[bpy.types.Object] = [
        boxpart((0.032, 0.070, 0.014), (0.0, y, rail_z + 0.007), surface=OLIVE,
                name="prism_mount"),
        turned("prism_body", y - length * 0.5, y + length * 0.5, tube_r, tube_r,
               centre_z=axis_z, segments=24, surface=OLIVE, bore=tube_r - 0.0030),
        ring("prism_bell", y - length * 0.5 + 0.010, tube_r * 1.22, 0.0030, 0.020,
             centre_z=axis_z, segments=24, surface=OLIVE),
        ring("prism_eye", y + length * 0.5 - 0.008, tube_r * 1.06, 0.0030, 0.014,
             centre_z=axis_z, segments=24, surface=OLIVE),
        turned("prism_lens", y - length * 0.5 + 0.012, y - length * 0.5 + 0.0155,
               tube_r * 1.10, tube_r * 1.10, centre_z=axis_z, segments=24,
               surface=GLASS),
        turned("prism_aperture", y - length * 0.5 + 0.0156, y - length * 0.5 + 0.0190,
               tube_r * 1.08, tube_r * 1.08, centre_z=axis_z, segments=24,
               surface=RUBBER),
    ]
    turret = turned("prism_turret", -0.010, 0.010, 0.0105, 0.0105, segments=16,
                    surface=OLIVE)
    turret.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
    turret.data.transform(mathutils.Matrix.Translation((0.0, y, axis_z + tube_r + 0.006)))
    turret.data.update()
    parts.append(turret)
    for side in (-1.0, 1.0):
        knob = turned("prism_knob", -0.008, 0.008, 0.0080, 0.0080, segments=16,
                      surface=OLIVE)
        knob.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
        knob.data.transform(mathutils.Matrix.Translation((side * (tube_r + 0.005), y,
                                                          axis_z)))
        knob.data.update()
        parts.append(knob)
    return parts, axis_z + tube_r


def optic_scope(ctx: dict, spec: dict, rail_z: float, y: float
                ) -> Tuple[List[bpy.types.Object], float]:
    """Long range scope: 30 mm tube, bell objective, eyepiece, three turrets."""
    tube_r = 0.0150
    axis_z = rail_z + 0.034
    front = y - 0.230
    rear = y + 0.120
    parts: List[bpy.types.Object] = [
        turned("scope_tube", front + 0.030, rear - 0.030, tube_r, tube_r,
               centre_z=axis_z, segments=24, surface=METAL, bore=tube_r - 0.0025),
        turned("scope_bell", front, front + 0.040, tube_r * 1.55, tube_r * 1.55,
               centre_z=axis_z, segments=24, surface=METAL, bore=tube_r * 1.55 - 0.0035),
        turned("scope_eye", rear - 0.034, rear, tube_r * 1.28, tube_r * 1.34,
               centre_z=axis_z, segments=24, surface=METAL, bore=tube_r - 0.0025),
        turned("scope_lens", front + 0.002, front + 0.006, tube_r * 1.44, tube_r * 1.44,
               centre_z=axis_z, segments=24, surface=GLASS),
        turned("scope_aperture", front + 0.0061, front + 0.0100, tube_r * 1.42,
               tube_r * 1.42, centre_z=axis_z, segments=24, surface=RUBBER),
    ]
    # two rings and their mounts, clamping the tube to the rail
    for mount_y in (y - 0.048, y + 0.052):
        parts.append(ring("scope_ring", mount_y, tube_r * 1.22, 0.0040, 0.016,
                          centre_z=axis_z, segments=20))
        parts.append(boxpart((0.030, 0.022, axis_z - rail_z + 0.006),
                             (0.0, mount_y, rail_z + (axis_z - rail_z) * 0.5),
                             surface=METAL, name="scope_mount"))
        parts.append(boxpart((0.034, 0.014, 0.010), (0.0, mount_y, rail_z + 0.005),
                             surface=METAL, name="scope_clamp"))
    turret = turned("scope_elevation", -0.011, 0.011, 0.0115, 0.0115, segments=16,
                    surface=METAL)
    turret.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
    turret.data.transform(mathutils.Matrix.Translation((0.0, y - 0.010,
                                                       axis_z + tube_r + 0.008)))
    turret.data.update()
    parts.append(turret)
    side_turret = turned("scope_windage", -0.010, 0.010, 0.0105, 0.0105, segments=16,
                         surface=METAL)
    side_turret.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
    side_turret.data.transform(mathutils.Matrix.Translation((tube_r + 0.008,
                                                             y - 0.010, axis_z)))
    side_turret.data.update()
    parts.append(side_turret)
    par = turned("parallax", -0.009, 0.009, 0.0105, 0.0105, segments=16, surface=METAL)
    par.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
    par.data.transform(mathutils.Matrix.Translation((-tube_r - 0.008, y - 0.010, axis_z)))
    par.data.update()
    parts.append(par)
    return parts, axis_z + tube_r * 1.34


def iron_sights_ak(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], float]:
    front_y = ctx["front_y"] - spec["barrel_len"] + 0.030
    parts, top = front_sight_block(ctx, front_y, spec)
    return parts, top


# --------------------------------------------------------------------------
# Magazine: a separate object, pivoted at the top of the well
# --------------------------------------------------------------------------
def magazine(spec: dict, ctx: dict) -> Tuple[List[bpy.types.Object], float, float]:
    """Curved magazine body with stamped ribs and an overhanging floor plate.

    Returns the parts and the pivot (Y, Z) the game rotates it around, which is the
    top of the magazine and not the origin of the weapon.
    """
    dims = spec["mag"]
    width = dims["width"]
    depth = dims["depth"]
    length = dims["length"]
    curve = dims["curve"]
    surface = dims.get("surface", POLYMER)
    top_z = ctx["bottom_z"] + 0.004
    y_top = ctx["mag_y"]

    steps = 9
    sections: List[Sequence[float]] = []
    offsets: List[float] = []
    for index in range(steps):
        t = index / (steps - 1)
        drop = length * t
        forward = -math.sin(math.radians(curve)) * length * t * t
        scale = 1.0 - 0.05 * t
        sections.append((top_z - drop, scale, scale, 0.0, y_top + forward))
        offsets.append(y_top + forward)
    body = loft(chamfer_rect(width * 0.5, depth * 0.5, 0.0045), sections,
                surface=surface, name="magazine", axis="Z")
    parts = [body]

    # floor plate, slightly wider than the body and proud at the front
    parts.append(boxpart((width * 1.08, depth * 1.06, 0.011),
                         (0.0, offsets[-1] + depth * 0.02,
                          top_z - length - 0.004), surface=BARE,
                         name="mag_floor"))
    # stamped ribs down the front and rear spines
    for side in (-1.0, 1.0):
        rib_sections = [(top_z - length * (0.06 + 0.88 * i / 8),
                         1.0, 1.0, 0.0,
                         offsets[min(8, int(round(i * 8 / 8)))] + side * depth * 0.52)
                        for i in range(9)]
        parts.append(loft(chamfer_rect(width * 0.34, 0.0032, 0.0012), rib_sections,
                          surface=surface, name="mag_rib", axis="Z"))
    # magazine catch notch at the front of the body
    parts.append(boxpart((width * 0.62, 0.008, 0.008),
                         (0.0, y_top - depth * 0.5 - 0.002, top_z - 0.020),
                         surface=BARE, name="mag_catch"))
    return parts, y_top, top_z


# --------------------------------------------------------------------------
# Bipod (folded under the fore-end)
# --------------------------------------------------------------------------
def bipod(ctx: dict, spec: dict, y: float) -> List[bpy.types.Object]:
    bore_z = ctx["bore_z"]
    radius = spec["barrel_r"]
    z = bore_z - radius * 2.6
    parts = [boxpart((0.030, 0.030, 0.016), (0.0, y, z), surface=OLIVE,
                     name="bipod_hinge")]
    for side in (-1.0, 1.0):
        parts.append(sweep_yz("bipod_leg", [(y - 0.008, z - 0.002),
                                            (y + 0.050, z - 0.008),
                                            (y + 0.115, z - 0.016)],
                              0.0040, surface=OLIVE, sides=8,
                              x_centre=side * 0.014))
        parts.append(boxpart((0.011, 0.018, 0.008),
                             (side * 0.014, y + 0.117, z - 0.018),
                             surface=RUBBER, name="bipod_foot"))
    return parts


# --------------------------------------------------------------------------
# Sidearm
# --------------------------------------------------------------------------
def build_pistol(spec: dict):
    """Recoil operated sidearm: slide, frame, raked grip, magazine in the grip."""
    half_w = spec["half_w"]
    height = spec["receiver_h"]
    top_z = height * 0.5
    rear_y = 0.082
    front_y = -0.092
    grip_top = -0.026
    grip_y = 0.024
    parts: List[bpy.types.Object] = []

    slide = loft(chamfer_rect(half_w, height * 0.52, 0.0055), [
        (rear_y, 0.96, 0.92, 0.0, top_z),
        (rear_y - 0.010, 1.00, 1.00, 0.0, top_z),
        (front_y + 0.012, 1.00, 1.00, 0.0, top_z),
        (front_y, 0.94, 0.92, 0.0, top_z - 0.001),
    ], surface=METAL, name="slide")
    parts.append(slide)
    parts.extend(ejection_port(slide, rear_y - 0.038, top_z + 0.009, 0.026, 0.009,
                               half_w))
    # slide serrations, front and rear, both cheeks
    for index in range(6):
        y = rear_y - 0.014 - index * 0.0055
        for side in (-1.0, 1.0):
            parts.append(boxpart((0.0012, 0.0024, height * 0.62),
                                 (side * (half_w + 0.0004), y, top_z),
                                 surface=METAL, name="serration"))
    for index in range(4):
        y = front_y + 0.016 + index * 0.0055
        for side in (-1.0, 1.0):
            parts.append(boxpart((0.0012, 0.0024, height * 0.52),
                                 (side * (half_w + 0.0004), y, top_z - 0.002),
                                 surface=METAL, name="serration"))
    # barrel, crown and guide rod under the muzzle
    parts.append(turned("barrel_in_slide", rear_y - 0.030, front_y + 0.010,
                        spec["barrel_r"] * 0.92, spec["barrel_r"] * 0.92,
                        centre_z=top_z + 0.010, segments=18, surface=BARE,
                        bore=spec["barrel_r"] * 0.55))
    parts.append(ring("slide_crown", front_y + 0.001, spec["barrel_r"] * 1.05, 0.0022,
                      0.006, centre_z=top_z + 0.010, segments=20))
    parts.append(turned("guide_rod", front_y + 0.004, front_y + 0.026,
                        spec["barrel_r"] * 0.46, spec["barrel_r"] * 0.46,
                        centre_z=top_z - 0.008, segments=14, surface=BARE))
    # sights: a rear notch between two blocks and a front post
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.0045, 0.006, 0.0050),
                             (side * 0.0075, rear_y - 0.008, top_z + height * 0.52 + 0.002),
                             surface=METAL, name="rear_sight"))
    parts.append(boxpart((0.0030, 0.005, 0.0045), (0.0, front_y + 0.010,
                                                   top_z + height * 0.52 + 0.001),
                         surface=METAL, name="front_sight"))
    parts.append(boxpart((0.006, 0.010, 0.0030), (0.0, rear_y - 0.010,
                                                  top_z - height * 0.52 - 0.001),
                         surface=BARE, name="firing_pin_plate"))

    # frame: dust cover, rails, accessory rail and the beavertail
    frame = loft(chamfer_rect(half_w * 0.98, height * 0.30, 0.005), [
        (rear_y + 0.004, 0.90, 0.90, 0.0, -height * 0.18),
        (rear_y - 0.010, 1.00, 1.00, 0.0, -height * 0.18),
        (front_y + 0.040, 1.00, 1.00, 0.0, -height * 0.18),
        (front_y + 0.010, 0.92, 0.88, 0.0, -height * 0.20),
    ], surface=POLYMER, name="frame")
    parts.append(frame)
    parts.append(railed("pistol_rail", front_y + 0.012, front_y + 0.058,
                        -height * 0.34, 0.017, 0.005))
    parts.append(boxpart((0.030, 0.024, 0.010), (0.0, rear_y - 0.002, -height * 0.16),
                         surface=POLYMER, name="beavertail"))

    # grip: raked polymer block with stippled panels
    rake = math.radians(17.0)
    steps = 7
    sections: List[Sequence[float]] = []
    for index in range(steps):
        t = index / (steps - 1)
        drop = 0.080 * t
        sections.append((grip_top - drop, 1.0 - 0.06 * t, 1.0 - 0.10 * t, 0.0,
                         grip_y - math.tan(rake) * drop))
    parts.append(loft(chamfer_rect(half_w * 1.00, 0.0225, 0.011), sections,
                      surface=POLYMER, name="grip", axis="Z"))
    for side in (-1.0, 1.0):
        parts.append(boxpart((0.0016, 0.030, 0.050),
                             (side * (half_w * 0.92), grip_y - 0.004,
                              grip_top - 0.040), surface=RUBBER,
                             rotation_deg=(0.0, 0.0, 17.0), name="grip_panel"))
    parts.append(boxpart((0.020, 0.030, 0.008), (0.0, grip_y - 0.006,
                                                 grip_top - 0.074),
                         surface=POLYMER, name="mag_floor"))
    # trigger guard and trigger
    guard_points = [(front_y + 0.070, grip_top + 0.006),
                    (front_y + 0.068, grip_top - 0.016),
                    (front_y + 0.082, grip_top - 0.024),
                    (grip_y - 0.022, grip_top - 0.024),
                    (grip_y - 0.012, grip_top - 0.010),
                    (grip_y - 0.010, grip_top + 0.004)]
    parts.append(sweep_yz("pistol_guard", guard_points, 0.0034, surface=POLYMER,
                          sides=8))
    parts.append(loft(chamfer_rect(0.0032, 0.0050, 0.0016), [
        (grip_top - 0.004, 1.0, 1.0, 0.0, front_y + 0.076),
        (grip_top - 0.018, 0.9, 0.9, 0.0, front_y + 0.079),
        (grip_top - 0.024, 0.7, 0.8, 0.0, front_y + 0.083),
    ], surface=BARE, name="pistol_trigger", axis="Z"))
    parts.append(pin((0.0, grip_top - 0.002, 0.0), 0.0030, 0.030, axis="X",
                     surface=BARE, name="trigger_pin"))
    # magazine release and slide stop
    parts.append(pin((-half_w - 0.0006, grip_y - 0.030, grip_top + 0.010), 0.0042,
                     0.005, name="mag_release"))
    parts.append(boxpart((0.004, 0.020, 0.006), (-half_w - 0.0006, front_y + 0.070,
                                                 -height * 0.22), surface=BARE,
                         name="slide_stop"))

    sight_top = top_z + height * 0.52 + 0.006
    return parts, None, sight_top, grip_y, grip_top


# --------------------------------------------------------------------------
# Specifications: metres, tuned against the real weapons they stand in for
# --------------------------------------------------------------------------
SPECS: Dict[str, dict] = {
    "vk74": dict(kind="ak", receiver_len=0.256, receiver_h=0.074, half_w=0.0275,
                 barrel_r=0.0100, barrel_len=0.352, furniture=WOOD, stock="ak",
                 stock_len=0.285, handguard="wood", handguard_len=0.185,
                 mag=dict(length=0.192, width=0.033, depth=0.082, curve=24.0,
                          surface="gun_metal"),
                 muzzle="ak_brake", optic=None, bore_drop=0.30, sling=True),
    "tr30": dict(kind="ar", receiver_len=0.250, receiver_h=0.075, half_w=0.0255,
                 barrel_r=0.0098, barrel_len=0.300, furniture=POLYMER, stock="ar",
                 stock_len=0.215, handguard="mlok", handguard_len=0.255,
                 handguard_scale=2.70,
                 mag=dict(length=0.180, width=0.030, depth=0.080, curve=7.0,
                          surface="gun_metal"),
                 muzzle="birdcage", optic="reddot", bore_drop=0.34, sling=True),
    "vs9": dict(kind="smg", receiver_len=0.215, receiver_h=0.068, half_w=0.0245,
                barrel_r=0.0090, barrel_len=0.150, furniture=POLYMER, stock="wire",
                stock_len=0.150, handguard="mlok", handguard_len=0.120,
                handguard_scale=2.60,
                mag=dict(length=0.170, width=0.030, depth=0.064, curve=8.0,
                         surface="gun_metal"),
                muzzle="cone", optic=None, bore_drop=0.30, sling=True),
    "dmr14": dict(kind="dmr", receiver_len=0.270, receiver_h=0.076, half_w=0.0255,
                  barrel_r=0.0108, barrel_len=0.440, furniture=OLIVE, stock="ar",
                  stock_len=0.245, handguard="mlok", handguard_len=0.300,
                  handguard_scale=2.75,
                  mag=dict(length=0.176, width=0.030, depth=0.082, curve=5.0,
                           surface="gun_metal"),
                  muzzle="birdcage", optic="prism", bore_drop=0.34, sling=True),
    "sr338": dict(kind="sniper", receiver_len=0.300, receiver_h=0.080, half_w=0.0260,
                  barrel_r=0.0130, barrel_len=0.560, furniture=OLIVE, stock="chassis",
                  stock_len=0.300, handguard="mlok", handguard_len=0.340,
                  handguard_scale=2.35,
                  mag=dict(length=0.150, width=0.032, depth=0.088, curve=4.0,
                           surface="gun_metal"),
                  muzzle="sniper", optic="scope", bore_drop=0.30, sling=True,
                  bipod=True),
    "k870": dict(kind="shotgun", receiver_len=0.230, receiver_h=0.078, half_w=0.0265,
                 barrel_r=0.0132, barrel_len=0.500, furniture=WOOD, stock="shotgun",
                 stock_len=0.285, handguard="pump", handguard_len=0.190,
                 mag=None, muzzle="none", optic=None, bore_drop=0.18, sling=True,
                 grip_frac=-0.22),
    "p9": dict(kind="pistol", receiver_len=0.170, receiver_h=0.038, half_w=0.0138,
               barrel_r=0.0078, barrel_len=0.030, furniture=POLYMER, stock="none",
               stock_len=0.0, handguard="none",
               mag=dict(length=0.072, width=0.024, depth=0.036, curve=4.0,
                        surface="gun_metal"),
               muzzle="none", optic=None, bore_drop=0.0, sling=False),
}


def build_long_gun(spec: dict):
    """Assemble a shoulder weapon. Returns parts, magazine parts and anchors."""
    ctx = layout(spec)
    kind = spec["kind"]
    parts: List[bpy.types.Object] = []
    sight_top = ctx["top_z"] + 0.020

    if kind == "ak":
        receiver_parts, _ = receiver_ak(ctx, spec)
    elif kind == "ar":
        receiver_parts, _ = receiver_ar(ctx, spec)
    elif kind == "smg":
        receiver_parts, _ = receiver_smg(ctx, spec)
    elif kind == "sniper":
        receiver_parts, _ = receiver_bolt(ctx, spec)
    elif kind == "shotgun":
        receiver_parts, _ = receiver_shotgun(ctx, spec)
    else:
        receiver_parts, _ = receiver_ar(ctx, spec)
    parts.extend(receiver_parts)

    barrel_parts, muzzle_y = barrel(ctx, spec)
    parts.extend(barrel_parts)
    muzzle_parts, muzzle_len = muzzle_device(spec["muzzle"], muzzle_y,
                                             spec["barrel_r"], ctx["bore_z"])
    parts.extend(muzzle_parts)

    handguard = spec.get("handguard", "none")
    if handguard == "wood":
        parts.extend(handguard_wood(ctx, spec))
    elif handguard == "mlok":
        parts.extend(handguard_mlok(ctx, spec, spec["furniture"]))
    elif handguard == "pump":
        parts.extend(handguard_pump(ctx, spec))

    if kind == "ak":
        handguard_end = ctx["front_y"] + 0.006 - spec["handguard_len"]
        gas_y = handguard_end - 0.018
        parts.extend(gas_block(ctx, gas_y, spec))
        # the gas tube runs from the receiver to the gas block, above the barrel,
        # and is what the upper handguard is clamped onto
        parts.append(turned("gas_tube_run", ctx["front_y"] - 0.004, gas_y + 0.012,
                            0.0105, 0.0105, centre_z=ctx["bore_z"] + 0.030,
                            segments=18, surface=METAL, bore=0.0084))
        parts.append(boxpart((ctx["hw"] * 1.05, 0.030, 0.034),
                             (0.0, ctx["front_y"] - 0.008,
                              ctx["bore_z"] + 0.026), surface=METAL,
                             name="rear_sight_block"))
        parts.append(boxpart((0.020, 0.010, 0.026),
                             (0.0, gas_y + 0.004, ctx["bore_z"] + 0.030),
                             surface=METAL, name="gas_block_tower"))
        front_parts, front_top = iron_sights_ak(ctx, spec)
        parts.extend(front_parts)
        sight_top = max(sight_top, front_top, ctx["bore_z"] + 0.052)
    if kind == "shotgun":
        # vent rib along the barrel and the tube magazine under it
        rib_start = ctx["front_y"] - 0.020
        parts.append(boxpart((0.014, spec["barrel_len"] * 0.94, 0.005),
                             (0.0, rib_start - spec["barrel_len"] * 0.47,
                              ctx["bore_z"] + spec["barrel_r"] + 0.004),
                             surface=METAL, name="vent_rib"))
        parts.append(turned("magazine_tube", rib_start - 0.010,
                            rib_start - spec["barrel_len"] * 0.86,
                            spec["barrel_r"] * 0.84, spec["barrel_r"] * 0.84,
                            centre_z=ctx["bore_z"] - spec["barrel_r"] * 2.1,
                            segments=20, surface=METAL, bore=spec["barrel_r"] * 0.62))
        parts.append(ring("tube_cap", rib_start - spec["barrel_len"] * 0.86,
                          spec["barrel_r"] * 0.95, 0.004, 0.012,
                          centre_z=ctx["bore_z"] - spec["barrel_r"] * 2.1, segments=20))
        parts.append(pin((0.0, muzzle_y - 0.006, ctx["bore_z"] + spec["barrel_r"] + 0.010),
                         0.0022, 0.006, axis="Z", surface=BARE, name="bead"))
        # barrel band clamping the tube magazine to the barrel
        band_y = rib_start - spec["barrel_len"] * 0.90
        parts.append(boxpart((0.048, 0.016, 0.048),
                             (0.0, band_y, ctx["bore_z"] - spec["barrel_r"] * 0.90),
                             surface=METAL, name="barrel_band"))
        sight_top = ctx["bore_z"] + spec["barrel_r"] + 0.014
    if kind == "smg":
        parts.append(railed("smg_rail", ctx["front_y"] - 0.070, ctx["front_y"] - 0.180,
                            ctx["bore_z"] + ctx["hw"] * 0.95, 0.017, 0.005))

    stock = spec.get("stock")
    if stock == "ak":
        parts.extend(stock_ak(ctx, spec, spec["furniture"]))
    elif stock == "ar":
        parts.extend(stock_ar(ctx, spec, spec["furniture"],
                              cheek_riser=(kind == "dmr")))
    elif stock == "chassis":
        parts.extend(stock_chassis(ctx, spec))
    elif stock == "shotgun":
        parts.extend(stock_shotgun(ctx, spec, spec["furniture"]))
    elif stock == "wire":
        parts.extend(stock_wire(ctx, spec))

    if kind == "sniper":
        parts.extend(grip_rifle(ctx, spec, OLIVE, length=0.100, rake=12.0))
    elif kind != "shotgun":
        parts.extend(grip_rifle(ctx, spec, spec["furniture"]))
    parts.extend(trigger_group(ctx, spec))

    rail_z = rail_top(kind, ctx)
    optic = spec.get("optic")
    if optic == "reddot":
        optic_parts, optic_top = optic_reddot(ctx, spec, rail_z, ctx["rear_y"] - 0.100)
        parts.extend(optic_parts)
        sight_top = optic_top
    elif optic == "prism":
        optic_parts, optic_top = optic_prism(ctx, spec, rail_z, ctx["rear_y"] - 0.110)
        parts.extend(optic_parts)
        sight_top = optic_top
    elif optic == "scope":
        optic_parts, optic_top = optic_scope(ctx, spec, rail_z, ctx["rear_y"] - 0.130)
        parts.extend(optic_parts)
        sight_top = optic_top

    if spec.get("bipod"):
        parts.extend(bipod(ctx, spec, ctx["front_y"] - spec["handguard_len"] * 0.55))
    if spec.get("sling", True):
        parts.extend(sling_loop((0.0, ctx["front_y"] - 0.075,
                                 ctx["bore_z"] - ctx["hw"] * 1.05), radius=0.010,
                                axis="X", name="front_sling"))

    mag_parts: List[bpy.types.Object] = []
    mag_pivot = (ctx["mag_y"], ctx["bottom_z"] + 0.004)
    if spec.get("mag"):
        mag_parts, pivot_y, pivot_z = magazine(spec, ctx)
        mag_pivot = (pivot_y, pivot_z)

    grip_top = ctx["bottom_z"]
    return parts, mag_parts, sight_top, ctx["grip_y"], grip_top, mag_pivot


def rail_top(kind: str, ctx: dict) -> float:
    """Height of the top rail, computed exactly as the receiver builder places it."""
    rh = ctx["rh"]
    top_z = ctx["top_z"]
    if kind == "ar" or kind == "dmr":
        return top_z - rh * 0.30 + rh * 0.34 + 0.0035 + 0.0038
    if kind == "sniper":
        return ctx["bore_z"] + ctx["hw"] * 1.10 + 0.004 + 0.0040
    if kind == "smg":
        return top_z + 0.004 + 0.0030
    if kind == "shotgun":
        return top_z + 0.002
    return top_z + 0.006


def build_weapon(weapon_id: str):
    """Assemble one weapon and re-origin it into the shooting hand."""
    spec = SPECS[weapon_id]
    if spec["kind"] == "pistol":
        parts, mag_parts, sight_top, grip_y, grip_top = build_pistol(spec)
        mag_pivot = None
        if spec.get("mag"):
            mag_parts, pivot_y, pivot_z = magazine(
                spec, dict(bottom_z=-0.030, mag_y=0.020, hw=spec["half_w"],
                           rear_y=0.082, front_y=-0.092, top_z=0.020,
                           bore_z=0.020, rh=spec["receiver_h"], rl=0.174,
                           grip_y=0.026))
            mag_pivot = (pivot_y, pivot_z)
    else:
        parts, mag_parts, sight_top, grip_y, grip_top, mag_pivot = build_long_gun(spec)

    weapon = finish(parts, weapon_id, bevel_width=spec.get("bevel", 0.0016))
    shift = mathutils.Matrix.Translation((0.0, -grip_y, -grip_top))
    weapon.data.transform(shift)
    weapon.data.update()
    sight_top -= grip_top

    magazine_obj = None
    if mag_parts and mag_pivot is not None:
        magazine_obj = finish(mag_parts, "Magazine", bevel_width=0.0008)
        # the magazine is its own object, so it needs the same re-origin as the body
        magazine_obj.data.transform(shift)
        pivot = mathutils.Vector((0.0, mag_pivot[0] - grip_y, mag_pivot[1] - grip_top))
        magazine_obj.data.transform(mathutils.Matrix.Translation(-pivot))
        magazine_obj.data.update()
        magazine_obj.location = pivot
        bpy.context.view_layer.update()

    bounds = world_bounds([weapon] + ([magazine_obj] if magazine_obj is not None else []))
    return weapon, magazine_obj, dict(sightHeightM=round(sight_top, 4),
                                      lengthM=round(bounds[1][1] - bounds[0][1], 3))


# --------------------------------------------------------------------------
# Finishing: join, bevel, smooth, unwrap
# --------------------------------------------------------------------------
def smooth_shade(obj: bpy.types.Object, angle_deg: float = 34.0) -> None:
    """Smooth shading with the normals split on hard edges.

    Flat shading is what makes a beveled mesh still look like a milled block; with
    split normals the fillets catch a highlight and the flats stay flat.
    """
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    modifier = obj.modifiers.new("KordonSmooth", "EDGE_SPLIT")
    modifier.split_angle = math.radians(angle_deg)
    art.apply_modifiers(obj)


def finish(parts: Sequence[bpy.types.Object], name: str, bevel_width: float = 0.0016,
           segments: int = 2) -> bpy.types.Object:
    merged = art.join([part for part in parts if part is not None], name)
    if art.DETAIL:
        art.bevel(merged, width=bevel_width, segments=segments, angle_deg=36.0)
        smooth_shade(merged)
        art.unwrap(merged, island_margin=0.002)
    return merged


# --------------------------------------------------------------------------
# Thrown equipment
# --------------------------------------------------------------------------
GRENADES: Dict[str, dict] = {
    "grenade_frag": dict(radius=0.030, height=0.098, ribs=9, surface=OLIVE,
                         top="fuze", sleeve=True),
    "grenade_smoke": dict(radius=0.033, height=0.135, ribs=3, surface=METAL,
                          top="vent", sleeve=False),
    "grenade_flash": dict(radius=0.029, height=0.108, ribs=4, surface=POLYMER,
                          top="holes", sleeve=False),
}


def build_grenade(grenade_id: str) -> Tuple[bpy.types.Object, dict]:
    spec = GRENADES[grenade_id]
    radius = spec["radius"]
    height = spec["height"]
    parts: List[bpy.types.Object] = [
        turned("body", 0.0, height, radius, radius * 0.96, segments=22,
               surface=spec["surface"],
               steps=[(0.004, radius * 0.62), (0.014, radius),
                      (height * 0.55, radius), (height * 0.90, radius * 0.94),
                      (height, radius * 0.70)]),
        turned("cap", height - 0.004, height + 0.012, radius * 0.52, radius * 0.52,
               segments=18, surface=METAL),
        turned("fuze", height + 0.010, height + 0.030, radius * 0.30, radius * 0.34,
               segments=14, surface=POLYMER),
    ]
    # the body is built along Y by turned(); stand it up on Z
    for part in parts:
        part.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
        part.data.update()

    if spec["sleeve"]:
        for index in range(spec["ribs"]):
            angle = math.radians(360.0 * index / spec["ribs"])
            parts.append(boxpart((radius * 0.34, 0.010, height * 0.62),
                                 (math.cos(angle) * radius * 0.92,
                                  math.sin(angle) * radius * 0.92, height * 0.48),
                                 surface=METAL,
                                 rotation_deg=(0.0, 0.0, math.degrees(angle)),
                                 name="fragment_rib"))
    # safety lever and its pin ring
    parts.append(loft(chamfer_rect(0.0060, 0.0028, 0.0016), [
        (height * 0.10, 1.0, 1.0, radius * 0.98, 0.0),
        (height * 0.96, 1.0, 1.0, radius * 0.62, 0.0),
    ], surface=BARE, name="lever", axis="Z"))
    ring_obj = art.torus_band(radius * 0.30, 0.0032,
                              center=(radius * 0.86, 0.0, height + 0.026),
                              rotation_deg=(90.0, 0.0, 0.0), segments=16,
                              surface=BARE, name="pin_ring")
    bake(ring_obj)
    parts.append(ring_obj)
    parts.append(pin((radius * 0.86, 0.0, height + 0.026), 0.0022, 0.050, axis="X",
                     surface=BARE, name="pull_pin"))

    if spec["top"] == "vent":
        for index in range(4):
            angle = math.radians(90.0 * index)
            parts.append(boxpart((0.010, 0.006, 0.020),
                                 (math.cos(angle) * radius * 0.72,
                                  math.sin(angle) * radius * 0.72, height + 0.004),
                                 surface=RUBBER, name="vent"))
    body = finish(parts, grenade_id, bevel_width=0.0009)
    bounds = world_bounds([body])
    return body, dict(heightM=round(bounds[1][2] - bounds[0][2], 3),
                      radiusM=round((bounds[1][0] - bounds[0][0]) * 0.5, 3))


# --------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------
def world_bounds(objects: Sequence[bpy.types.Object]) -> Tuple[Tuple[float, ...], Tuple[float, ...]]:
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for obj in objects:
        if obj is None or obj.type != "MESH":
            continue
        matrix = obj.matrix_world
        for vertex in obj.data.vertices:
            world = matrix @ vertex.co
            for axis in range(3):
                lo[axis] = min(lo[axis], world[axis])
                hi[axis] = max(hi[axis], world[axis])
    return tuple(lo), tuple(hi)


def verify_export(path: str, expected: Tuple[Tuple[float, ...], Tuple[float, ...]]) -> int:
    """Re-import the FBX into a clean scene and compare the bounding box."""
    art.reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    imported = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not imported:
        raise RuntimeError(f"{path}: import produced no meshes")
    lo, hi = world_bounds(imported)
    for axis in range(3):
        if abs((hi[axis] - lo[axis]) - (expected[1][axis] - expected[0][axis])) > 0.004:
            raise RuntimeError(f"{path}: axis {axis} size mismatch "
                               f"{(hi[axis] - lo[axis]):.4f} vs "
                               f"{(expected[1][axis] - expected[0][axis]):.4f}")
    tris = 0
    for obj in imported:
        tris += sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)
    return tris


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", default=None)
    args = parser.parse_args()

    weapon_ids = [args.only] if args.only else list(SPECS.keys())
    started = time.time()
    manifest: List[dict] = []
    total_tris = 0
    os.makedirs(WEAPON_DIR, exist_ok=True)

    for weapon_id in weapon_ids:
        art.reset_scene()
        weapon, magazine_obj, meta = build_weapon(weapon_id)
        objects = [weapon] + ([magazine_obj] if magazine_obj is not None else [])
        bounds = world_bounds(objects)
        path = os.path.join(WEAPON_DIR, f"{weapon_id}.fbx")
        size = art.export_fbx(objects, path)
        tris = verify_export(path, bounds)
        total_tris += tris
        manifest.append(dict(
            id=weapon_id,
            model=f"Kordon/Weapons/{weapon_id}",
            triangles=tris,
            bytes=size,
            lengthM=meta["lengthM"],
            sightHeightM=meta["sightHeightM"],
            sizeM=[round(bounds[1][i] - bounds[0][i], 3) for i in range(3)],
            hasMagazine=magazine_obj is not None,
        ))
        print(f"  {weapon_id:13s} {tris:6d} tris  {size / 1024:6.1f} KB  "
              f"{manifest[-1]['sizeM']}  sight {meta['sightHeightM']:.3f}")


    for grenade_id in GRENADES:
        art.reset_scene()
        body, meta = build_grenade(grenade_id)
        bounds = world_bounds([body])
        path = os.path.join(WEAPON_DIR, f"{grenade_id}.fbx")
        size = art.export_fbx([body], path)
        tris = verify_export(path, bounds)
        total_tris += tris
        manifest.append(dict(
            id=grenade_id,
            model=f"Kordon/Weapons/{grenade_id}",
            triangles=tris,
            bytes=size,
            lengthM=meta["heightM"],
            sightHeightM=0.0,
            sizeM=[round(bounds[1][i] - bounds[0][i], 3) for i in range(3)],
            hasMagazine=False,
        ))
        print(f"  {grenade_id:13s} {tris:6d} tris  {size / 1024:6.1f} KB  "
              f"{manifest[-1]['sizeM']}")

    existing: List[dict] = []
    if os.path.exists(MANIFEST):
        try:
            with open(MANIFEST, "r", encoding="utf-8") as handle:
                existing = json.load(handle).get("weapons", [])
        except (ValueError, OSError):
            existing = []

    by_id = {entry["id"]: entry for entry in existing}
    for entry in manifest:
        by_id[entry["id"]] = entry
    payload = {
        "comment": "First person weapon models built by Tools/blender/build_weapons.py. "
                   "Dimensions are metres; sightHeightM is the top of the sights above "
                   "the grip, which is what the viewmodel uses to align the aim pose.",
        "weapons": [by_id[key] for key in sorted(by_id.keys())],
    }
    art.write_json(MANIFEST, payload)

    # The authoring source: every weapon in one scene, spaced out, magazines
    # parented, so a human can open the file and inspect or restyle the set.
    if not args.only:
        art.reset_scene()
        for index, weapon_id in enumerate(SPECS):
            weapon, magazine_obj, _meta = build_weapon(weapon_id)
            if magazine_obj is not None:
                magazine_obj.parent = weapon
            weapon.location = (index * 1.45, 0.0, 0.0)
            bpy.context.view_layer.update()
        blend_path = os.path.join(art.BLEND_DIR, "kordon_weapons.blend")
        art.save_blend(blend_path)
        print(f"blend: {blend_path}")

    print(f"weapons: {len(manifest)} files, {total_tris} triangles, "
          f"{time.time() - started:.1f}s")
    print(f"manifest: {MANIFEST}")


if __name__ == "__main__":
    main()
