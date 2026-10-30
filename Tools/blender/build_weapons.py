#!/usr/bin/env python3
"""KORDON weapon models, authored part by part in Blender.

    Tools/blender.sh Tools/blender/build_weapons.py [--only <weapon id>]

Seven weapons and three grenades, all procedural, all built from the parts a real
firearm is made of: a receiver with an ejection port cut through it and the bolt
carrier visible inside, a barrel with a stepped profile and a real bore, a muzzle
device with cut ports, a handguard with recessed M-LOK pockets, a raked pistol
grip with a front strap that curves, a stamped trigger guard with a trigger blade
inside, a curved magazine with a floor plate and an optic that is a tube, not a
cylinder.

Conventions, because they are easy to get wrong and hard to see:

* ``-Y`` is the muzzle, ``+Y`` is the butt, ``+Z`` is up, metres throughout.
* Before export every weapon is re-origined so that ``(0, 0, 0)`` is the shooting
  hand: the top of the grip at the web of the hand.  ``WeaponViewmodel`` measures
  the rest (sight height, length) from the mesh.
* The magazine is a separate object with its node pivot at the top of the well,
  so the viewmodel can drop it out of the magwell instead of swinging it around
  the grip.
* Parts must overlap their neighbour by a few millimetres.  A gap of 2 mm at
  viewmodel distance reads as a hole, and that is what the previous pass looked
  like.

The file is a pure authoring tool: nothing here ships except the FBX files and
the manifest.
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
import bmesh
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kordon_art as art  # noqa: E402

WEAPON_DIR = os.path.join(art.MODEL_DIR, "Resources", "Kordon", "Weapons")
MANIFEST = os.path.join(art.DATA_DIR, "Art", "weapon_manifest.json")

# surfaces, as declared in kit_manifest.json
METAL = "gun_metal"        # anodised or phosphated receiver and barrel
BARE = "gun_bare"          # machined steel: bolts, pins, crowns, triggers
POLYMER = "gun_polymer"    # glass filled nylon: grips, handguards, stocks
WOOD = "gun_wood"          # oiled walnut
OLIVE = "gun_olive"        # painted chassis and coated barrels
RUBBER = "rubber"          # recoil pads, grip inserts, port shadow
GLASS = "glass"            # optic lenses


# --------------------------------------------------------------------------
# Mesh primitives
# --------------------------------------------------------------------------
def mesh_object(name: str, verts: Sequence, faces: Sequence, surface: str
                ) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.validate(verbose=False)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    art.assign_surface(obj, surface)
    return obj


def recalc(obj: bpy.types.Object) -> None:
    """Make a closed shell face outwards, per part."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def bake(obj: bpy.types.Object) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.select_set(False)
    return obj


def box(name: str, size: Sequence[float], centre: Sequence[float],
        surface: str = METAL, rot: Sequence[float] = (0.0, 0.0, 0.0),
        bevel: float = 0.0) -> bpy.types.Object:
    hx, hy, hz = (value * 0.5 for value in size)
    verts = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
             (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    obj = mesh_object(name, verts, faces, surface)
    obj.location = tuple(centre)
    if any(abs(angle) > 1e-9 for angle in rot):
        obj.rotation_euler = tuple(math.radians(v) for v in rot)
    bake(obj)
    if bevel > 0.0 and art.DETAIL:
        art.bevel(obj, width=bevel, segments=2, angle_deg=32.0)
    return obj


# --------------------------------------------------------------------------
# Cross sections (u, v) - mapped to (x, z) by the loft
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


def stadium(half_len: float, radius: float, steps: int = 5) -> List[Tuple[float, float]]:
    """Capsule: a rectangle with semicircular ends. Used for every slot cutter."""
    points: List[Tuple[float, float]] = []
    for step in range(steps + 1):
        angle = math.pi * step / steps
        points.append((radius * math.cos(angle), half_len + radius * math.sin(angle)))
    for step in range(steps + 1):
        angle = math.pi + math.pi * step / steps
        points.append((radius * math.cos(angle), -half_len + radius * math.sin(angle)))
    return points


def oval(half_x: float, half_z: float, steps: int = 14) -> List[Tuple[float, float]]:
    return [(half_x * math.cos(2.0 * math.pi * step / steps),
             half_z * math.sin(2.0 * math.pi * step / steps))
            for step in range(steps)]


# --------------------------------------------------------------------------
# Loft and revolve
# --------------------------------------------------------------------------
def loft(profile: Sequence[Tuple[float, float]],
         sections: Sequence[Sequence[float]], surface: str = METAL,
         name: str = "loft", axis: str = "Y", caps: bool = True,
         x_centre: float = 0.0) -> bpy.types.Object:
    """Sweep a profile along an axis: sections are (t, scale_u, scale_v, du, dv)."""
    count = len(profile)
    verts: List[Tuple[float, float, float]] = []
    for section in sections:
        position, scale_u, scale_v, offset_u, offset_v = section
        for u, v in profile:
            uu = u * scale_u + offset_u
            vv = v * scale_v + offset_v
            if axis == "Y":
                verts.append((x_centre + uu, position, vv))
            elif axis == "X":
                verts.append((position, uu, vv))
            else:
                verts.append((x_centre + uu, vv, position))

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
    recalc(obj)
    return obj


def prism(name: str, points: Sequence[Tuple[float, float]], half_w: float,
          surface: str = METAL, bevel_width: float = 0.0, segments: int = 3,
          x_centre: float = 0.0) -> bpy.types.Object:
    """Extrude a side profile (y, z) through a width and roll the edges."""
    count = len(points)
    verts: List[Tuple[float, float, float]] = []
    for x in (-half_w, half_w):
        for (y, z) in points:
            verts.append((x_centre + x, y, z))
    faces: List[tuple] = []
    for index in range(count):
        following = (index + 1) % count
        faces.append((index, following, count + following, count + index))
    faces.append(tuple(range(count - 1, -1, -1)))
    faces.append(tuple(count + index for index in range(count)))
    obj = mesh_object(name, verts, faces, surface)
    recalc(obj)
    if bevel_width > 0.0 and art.DETAIL:
        art.bevel(obj, width=bevel_width, segments=segments, angle_deg=26.0)
    return obj


def tube(name: str, rings: Sequence[Tuple[float, float]], z: float,
         surface: str = METAL, bore: float = 0.0, segments: int = 26,
         x: float = 0.0) -> bpy.types.Object:
    """A solid or bored cylinder along Y, from a list of (y, radius) rings.

    The bore is a real hole: a gun whose muzzle is capped reads as a toy, so the
    inner wall and the crown annulus are modelled and the inside is dark.
    """
    verts: List[Tuple[float, float, float]] = []
    for y, radius in rings:
        for index in range(segments):
            angle = 2.0 * math.pi * index / segments
            verts.append((x + radius * math.cos(angle), y, z + radius * math.sin(angle)))
    if bore > 0.0:
        for y, _radius in rings:
            for index in range(segments):
                angle = 2.0 * math.pi * index / segments
                verts.append((x + bore * math.cos(angle), y, z + bore * math.sin(angle)))
    faces: List[tuple] = []
    count = len(rings)

    def outer(ring: int, index: int) -> int:
        return ring * segments + index

    for ring in range(count - 1):
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((outer(ring, index), outer(ring, following),
                          outer(ring + 1, following), outer(ring + 1, index)))
    if bore > 0.0:
        inner_offset = count * segments

        def inner(ring: int, index: int) -> int:
            return inner_offset + ring * segments + index

        for ring in range(count - 1):
            for index in range(segments):
                following = (index + 1) % segments
                faces.append((inner(ring, index), inner(ring + 1, index),
                              inner(ring + 1, following), inner(ring, following)))
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((outer(0, index), outer(0, following),
                          inner(0, following), inner(0, index)))
            last = count - 1
            faces.append((outer(last, following), outer(last, index),
                          inner(last, index), inner(last, following)))
    else:
        for end, ring in ((0, 0), (1, count - 1)):
            centre = len(verts)
            verts.append((x, rings[ring][0], z))
            for index in range(segments):
                following = (index + 1) % segments
                if end == 0:
                    faces.append((centre, outer(ring, following), outer(ring, index)))
                else:
                    faces.append((centre, outer(ring, index), outer(ring, following)))
    obj = mesh_object(name, verts, faces, surface)
    recalc(obj)
    return obj


def band(name: str, y: float, radius: float, width: float, z: float,
         surface: str = METAL, bore: float = 0.0, segments: int = 26
         ) -> bpy.types.Object:
    return tube(name, [(y - width * 0.5, radius * 0.97), (y, radius),
                       (y + width * 0.5, radius * 0.97)], z, surface=surface,
                bore=bore, segments=segments)


def disc(name: str, centre: Sequence[float], radius: float, thickness: float,
         surface: str = BARE, axis: str = "X", segments: int = 16) -> bpy.types.Object:
    """A pin head, rivet or socket: a shallow cylinder on an axis.

    Built as a short tube along Y and then rotated onto the requested axis. The
    previous version rotated about Y (which leaves a Y tube where it was) and
    translated by the centre twice, which put every socket half a metre behind
    its stock - those were the small floating dots on the render sheet.
    """
    half = thickness * 0.5
    obj = tube(name, [(-half, radius), (half, radius)], 0.0, surface=surface,
               segments=segments)
    if axis == "X":
        obj.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
    elif axis == "Z":
        obj.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
    obj.data.transform(mathutils.Matrix.Translation((centre[0], centre[1], centre[2])))
    obj.data.update()
    return obj


def torus(name: str, centre: Sequence[float], radius: float, thickness: float,
          surface: str = BARE, segments: int = 22, axis: str = "X"
          ) -> bpy.types.Object:
    """A ring lying in the YZ plane (axis X) or the XY plane (axis Z)."""
    rotation = (0.0, 90.0, 0.0) if axis == "X" else (0.0, 0.0, 0.0)
    obj = art.torus_band(radius, thickness, center=centre,
                         rotation_deg=rotation, segments=segments,
                         surface=surface, name=name)
    bake(obj)
    return obj


# --------------------------------------------------------------------------
# Booleans
# --------------------------------------------------------------------------
def cut(target: bpy.types.Object, cutters: Sequence[bpy.types.Object]) -> None:
    for cutter in cutters:
        modifier = target.modifiers.new("cut", "BOOLEAN")
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
    recalc(target)


def slot_cutter(name: str, y: float, z: float, half_len: float, radius: float,
                x_centre: float, x_span: float, surface: str = METAL
                ) -> bpy.types.Object:
    """A capsule shaped cutter along X: one M-LOK slot, one sling slot, one port."""
    profile = stadium(half_len, radius)
    return loft(profile, [(-x_span * 0.5, 1.0, 1.0, y, z),
                          (x_span * 0.5, 1.0, 1.0, y, z)],
                surface=surface, name=name, axis="X")


def box_cutter(name: str, size: Sequence[float], centre: Sequence[float],
               bevel: float = 0.0) -> bpy.types.Object:
    return box(name, size, centre, surface=METAL, bevel=bevel)


# --------------------------------------------------------------------------
# Detail primitives
# --------------------------------------------------------------------------
def rail(name: str, y0: float, y1: float, z: float, width: float = 0.0212,
         height: float = 0.0060, surface: str = METAL, tooth: float = 0.010
         ) -> bpy.types.Object:
    """Picatinny rail: one lofted bar with a tooth every ten millimetres."""
    half = width * 0.5
    profile = chamfer_rect(half, height * 0.5, height * 0.30)
    length = abs(y1 - y0)
    count = max(2, int(length / tooth))
    direction = 1.0 if y1 > y0 else -1.0
    sections: List[Sequence[float]] = [(y0, 0.96, 0.70, 0.0, z)]
    for index in range(count):
        start = y0 + direction * length * index / count
        end = y0 + direction * length * (index + 1) / count
        sections.append((start, 1.0, 1.0, 0.0, z))
        sections.append((end - direction * 0.0035, 1.0, 1.0, 0.0, z))
        sections.append((end - direction * 0.0012, 0.80, 0.62, 0.0, z))
    sections.append((y1, 0.96, 0.70, 0.0, z))
    sections.sort(key=lambda section: section[0] * direction)
    return loft(profile, sections, surface=surface, name=name)


def pocket(target: bpy.types.Object, y: float, z: float, half_len: float,
           radius: float, side: float, depth: float, floor_surface: str = RUBBER,
           surface_x: float = 0.0) -> bpy.types.Object:
    """Cut a shallow recess into a shell and drop a dark floor into it."""
    cutter = slot_cutter("pocket", y, z, half_len, radius,
                         side * (surface_x - depth * 0.5), depth * 2.2)
    cut(target, [cutter])
    return loft(stadium(half_len * 0.94, radius * 0.9),
                [(side * (surface_x - depth), 1.0, 1.0, y, z),
                 (side * (surface_x - depth * 0.4), 1.0, 1.0, y, z)],
                surface=floor_surface, name="pocket_floor", axis="X")


def seam(name: str, y: float, z: float, length: float, height: float,
         half_w: float, side: float, surface: str = BARE) -> bpy.types.Object:
    """A thin raised panel line: it catches a highlight and breaks a flat face."""
    return box(name, (0.0016, length, height),
               (side * (half_w + 0.0002), y, z), surface=surface)


def panel_frame(name: str, x: float, y0: float, y1: float, z0: float, z1: float,
                side: float, surface: str = METAL, thickness: float = 0.0016,
                width: float = 0.0019) -> List[bpy.types.Object]:
    """Raised border on a flat receiver side, like a machined panel line.

    A raised frame is used instead of a recessed pocket because a shallow
    boolean on a lofted side face can miss the surface and leave a floating
    plate behind - the exact artefact this pass is removing.
    """
    cx = side * x
    cy = (y0 + y1) * 0.5
    cz = (z0 + z1) * 0.5
    parts: List[bpy.types.Object] = []
    for y in (y0, y1):
        parts.append(box(name, (thickness, width, z1 - z0), (cx, y, cz),
                         surface=surface, bevel=0.0004))
    for z in (z0, z1):
        parts.append(box(name, (thickness, y1 - y0, width), (cx, cy, z),
                         surface=surface, bevel=0.0004))
    return parts


def screw_head(name: str, x: float, y: float, z: float, side: float,
               radius: float = 0.0032, surface: str = BARE
               ) -> List[bpy.types.Object]:
    """Slotted screw head: a shallow disc with a dark slot across it."""
    head = disc(name, (side * x, y, z), radius, 0.0026, surface=surface,
                axis="X", segments=12)
    slot = box(name + "_slot", (0.0011, radius * 1.7, radius * 0.32),
               (side * (x + 0.0011), y, z), surface=RUBBER)
    return [head, slot]


def swivel(name: str, y: float, z: float, half_w: float, radius: float = 0.0085,
           surface: str = METAL) -> List[bpy.types.Object]:
    """Sling swivel: a plate, a boss and a thick ring, all touching."""
    parts = [box(name + "_plate", (half_w * 2.4, 0.020, 0.014),
                 (0.0, y, z), surface=surface, bevel=0.0022),
             box(name + "_boss", (0.007, 0.011, 0.013),
                 (0.0, y, z - 0.010), surface=surface)]
    parts.append(torus(name + "_ring", (0.0, y, z - 0.010 - radius * 0.75),
                       radius, 0.0034, surface=BARE, segments=24, axis="X"))
    return parts


# --------------------------------------------------------------------------
# Layout: every attachment is derived from one table
# --------------------------------------------------------------------------
def layout(spec: dict) -> dict:
    receiver_len = spec["receiver_len"]
    receiver_h = spec["receiver_h"]
    half_w = spec["half_w"]
    top = receiver_h * 0.5
    front = -receiver_len * 0.45
    rear = receiver_len * 0.55
    # +Y is the rear: the magazine sits forward of the grip, the grip sits near
    # the rear of the receiver, and the trigger sits between the two.
    grip_y = rear - receiver_len * spec.get("grip_frac", 0.17)
    mag_y = front + receiver_len * spec.get("mag_frac", 0.36)
    return dict(
        rl=receiver_len, rh=receiver_h, hw=half_w,
        front=front, rear=rear, top=top, bottom=-top,
        bore=top - receiver_h * spec.get("bore_drop", 0.30),
        grip_y=grip_y, mag_y=mag_y,
        front_y=front, rear_y=rear, top_z=top, bottom_z=-top,
        bore_z=top - receiver_h * spec.get("bore_drop", 0.30),
    )


# --------------------------------------------------------------------------
# Barrels, muzzle devices
# --------------------------------------------------------------------------
def barrel(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], float]:
    """Cold hammer forged barrel: chamber, shoulder, taper and a crowned muzzle."""
    radius = spec["barrel_r"]
    bore_z = ctx["bore"]
    start = ctx["front"] + 0.020
    end = start - spec["barrel_len"]
    rings = [
        (start, radius * 1.55),
        (start - 0.030, radius * 1.42),
        (start - 0.070, radius * 1.10),
        (start - 0.110, radius * 1.02),
        (end + 0.055, radius * 1.00),
        (end + 0.014, radius * 0.94),
        (end, radius * 0.90),
    ]
    parts = [tube("barrel", rings, bore_z, surface=spec.get("barrel_surface", METAL),
                  bore=radius * 0.52, segments=26)]
    parts.append(band("barrel_shoulder", start - 0.004, radius * 1.58, 0.007,
                      bore_z, surface=BARE, bore=radius * 1.30))
    return parts, end


def muzzle(name: str, y_end: float, radius: float, z: float, surface: str = METAL
           ) -> Tuple[List[bpy.types.Object], float]:
    """Muzzle device with cut ports and a real bore."""
    parts: List[bpy.types.Object] = []
    if name in ("ak_brake", "brake"):
        length = 0.060
        body = tube("brake", [
            (y_end - length, radius * 1.30), (y_end - length + 0.006, radius * 1.62),
            (y_end - 0.012, radius * 1.66), (y_end - 0.004, radius * 1.60),
            (y_end, radius * 1.46),
        ], z, surface=surface, bore=radius * 0.66, segments=24)
        cutters = []
        for side in (-1.0, 1.0):
            for index in range(2):
                cutters.append(slot_cutter(
                    "brake_port", y_end - 0.020 - index * 0.017, z + radius * 0.15,
                    0.0050, radius * 0.62, side * radius * 1.30, radius * 0.62))
        cutters.append(slot_cutter("brake_top", y_end - 0.024, z + radius * 0.95,
                                   0.0044, radius * 0.55, 0.0, radius * 1.1))
        cut(body, cutters)
        parts.append(body)
        parts.append(band("brake_crown", y_end - 0.002, radius * 1.50, 0.005, z,
                          surface=BARE, bore=radius * 0.70, segments=24))
        return parts, length
    if name == "birdcage":
        length = 0.058
        body = tube("hider", [
            (y_end - length, radius * 1.22), (y_end - length + 0.008, radius * 1.52),
            (y_end - 0.010, radius * 1.52), (y_end, radius * 1.44),
        ], z, surface=surface, bore=radius * 0.68, segments=24)
        cutters = []
        for index in range(4):
            cutter = slot_cutter("hider_port", y_end - 0.018, z, 0.0060, radius * 0.55,
                                 0.0, radius * 2.6)
            cutter.data.transform(mathutils.Matrix.Rotation(
                math.radians(45.0 + 90.0 * index), 4, "Y"))
            cutter.data.update()
            cutters.append(cutter)
        cut(body, cutters)
        parts.extend([body, band("hider_rim", y_end - 0.003, radius * 1.46, 0.004,
                                 z, surface=BARE, bore=radius * 0.70, segments=24)])
        return parts, length
    if name == "sniper":
        length = 0.072
        body = tube("brake", [
            (y_end - length, radius * 1.24), (y_end - length + 0.012, radius * 1.56),
            (y_end - 0.006, radius * 1.58), (y_end, radius * 1.40),
        ], z, surface=surface, bore=radius * 0.62, segments=24)
        cutters = []
        for side in (-1.0, 1.0):
            for index in range(3):
                cutters.append(slot_cutter(
                    "brake_port", y_end - 0.016 - index * 0.015, z,
                    0.0046, radius * 0.60, side * radius * 1.30, radius * 0.60))
        cut(body, cutters)
        parts.append(body)
        return parts, length
    if name == "cone":
        length = 0.044
        body = tube("hider", [(y_end - length, radius * 1.26), (y_end - 0.010,
                                                               radius * 1.44),
                              (y_end, radius * 1.16)],
                    z, surface=surface, bore=radius * 0.70, segments=24)
        cutters = [slot_cutter("hider_port", y_end - 0.014, z,
                               0.0046, radius * 0.55, side * radius * 1.30,
                               radius * 0.55)
                   for side in (-1.0, 1.0)]
        cut(body, cutters)
        parts.append(body)
        return parts, length
    return parts, 0.0


# --------------------------------------------------------------------------
# Receivers
# --------------------------------------------------------------------------
def receiver_ak(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """AK: stamped box, hinged dust cover with ribs, rear sight block, selector."""
    hw, top, bottom = ctx["hw"], ctx["top"], ctx["bottom"]
    front, rear, bore = ctx["front"], ctx["rear"], ctx["bore"]
    parts: List[bpy.types.Object] = []

    body = loft(chamfer_rect(hw, (top - bottom) * 0.5, 0.0045), [
        (front - 0.004, 0.93, 0.94, 0.0, 0.0),
        (front + 0.014, 1.00, 1.00, 0.0, 0.0),
        (rear - 0.010, 1.00, 1.00, 0.0, 0.0),
        (rear + 0.004, 0.97, 0.97, 0.0, 0.0),
    ], surface=METAL, name="receiver")
    parts.append(body)

    # dust cover: a half tube sitting on the receiver, with pressed ribs
    cover_rear = rear + 0.002
    cover_front = front + ctx["rl"] * 0.26
    cover = loft(round_rect(hw * 0.985, 0.013, 0.011, steps=4), [
        (cover_front + 0.006, 0.90, 0.62, 0.0, top - 0.008),
        (cover_front, 0.98, 0.92, 0.0, top - 0.006),
        (cover_front - 0.020, 1.00, 1.00, 0.0, top - 0.005),
        (cover_rear - 0.012, 1.00, 1.00, 0.0, top - 0.005),
        (cover_rear, 0.96, 0.86, 0.0, top - 0.007),
    ], surface=METAL, name="dust_cover")
    parts.append(cover)
    for index in range(3):
        parts.append(box("cover_rib", (hw * 1.80, 0.0055, 0.0030),
                         (0.0, cover_front - 0.052 - index * 0.030, top + 0.0068),
                         surface=METAL))

    # rear sight block on the receiver front, gas tube passes through it
    parts.append(box("rear_sight_block", (hw * 1.92, 0.030, 0.030),
                     (0.0, cover_front - 0.006, top - 0.010), surface=METAL,
                     bevel=0.0018))
    parts.append(box("rear_sight_leaf", (hw * 1.10, 0.008, 0.012),
                     (0.0, cover_front - 0.002, top + 0.010), surface=BARE,
                     rot=(28.0, 0.0, 0.0)))

    # ejection port on the right, with the bolt carrier visible inside
    port = box_cutter("port", (0.016, 0.062, 0.026),
                      (hw * 1.02, front + 0.086, bore + 0.006), bevel=0.0045)
    cut(body, [port])
    parts.append(box("port_shadow", (0.020, 0.066, 0.030),
                     (hw * 0.55, front + 0.086, bore + 0.006), surface=RUBBER))
    parts.append(tube("bolt_carrier", [(front + 0.058, hw * 0.56),
                                       (rear - 0.048, hw * 0.56)],
                      bore, surface=BARE, segments=18))

    # selector lever on the right: the long stamped paddle
    parts.append(prism("selector", [
        (front + 0.052, bore + 0.026), (front + 0.030, bore + 0.030),
        (rear - 0.040, bore + 0.020), (rear - 0.040, bore + 0.004),
        (front + 0.030, bore + 0.012), (front + 0.052, bore + 0.014),
    ], 0.0016, surface=METAL, bevel_width=0.0007, x_centre=hw + 0.0016))

    # magazine well, flared at the front so the magazine looks inserted
    parts.append(loft(chamfer_rect(hw * 1.02, 0.016, 0.004), [
        (ctx["mag_y"] + 0.044, 1.00, 1.00, 0.0, bottom + 0.004),
        (ctx["mag_y"] - 0.044, 1.00, 1.00, 0.0, bottom + 0.004),
    ], surface=METAL, name="magwell"))
    parts.append(box("magwell_lip", (hw * 2.1, 0.092, 0.005),
                     (0.0, ctx["mag_y"], bottom - 0.008), surface=METAL,
                     bevel=0.0016))
    for side in (-1.0, 1.0):
        parts.append(disc("takedown_pin", (side * (hw + 0.0012), rear - 0.028,
                                           bottom + 0.014), 0.0040, 0.0032))
    # stamped receiver: pressed channel and two rivets per side
    for side in (-1.0, 1.0):
        parts.extend(panel_frame("receiver_channel", hw,
                                 front + 0.028, rear - 0.030,
                                 bottom + 0.008, bottom + 0.026, side,
                                 thickness=0.0014, width=0.0016))
        for y in (front + 0.044, rear - 0.044):
            parts.extend(screw_head("receiver_rivet", hw + 0.0004, y,
                                    bottom + 0.017, side, 0.0028, surface=METAL))
    return parts


def receiver_ar(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """AR: flat top upper with a rail, lower with a magwell and a trigger housing."""
    hw, top, bottom, bore = ctx["hw"], ctx["top"], ctx["bottom"], ctx["bore"]
    front, rear = ctx["front"], ctx["rear"]
    parts: List[bpy.types.Object] = []

    upper_h = (top - bottom) * 0.46
    upper = loft(round_rect(hw, upper_h * 0.5, 0.007, steps=3), [
        (front + 0.006, 0.86, 0.86, 0.0, top - upper_h * 0.5),
        (front, 0.95, 0.95, 0.0, top - upper_h * 0.5),
        (front + 0.060, 1.00, 1.00, 0.0, top - upper_h * 0.5),
        (rear - 0.020, 1.00, 1.00, 0.0, top - upper_h * 0.5),
        (rear, 0.96, 0.96, 0.0, top - upper_h * 0.5),
    ], surface=METAL, name="upper")
    parts.append(upper)
    parts.append(rail("top_rail", front + 0.004, rear - 0.006,
                      top + 0.0012, 0.0212, 0.0062))

    lower_h = (top - bottom) * 0.42
    lower = loft(chamfer_rect(hw * 0.985, lower_h * 0.5, 0.0045), [
        (ctx["mag_y"] - 0.052, 0.86, 0.88, 0.0, bottom + lower_h * 0.5),
        (ctx["mag_y"] - 0.030, 1.00, 1.00, 0.0, bottom + lower_h * 0.5),
        (ctx["mag_y"] + 0.048, 1.00, 1.00, 0.0, bottom + lower_h * 0.5),
        (ctx["grip_y"] + 0.010, 0.98, 1.00, 0.0, bottom + lower_h * 0.5),
        (rear - 0.004, 0.90, 0.92, 0.0, bottom + lower_h * 0.5),
    ], surface=METAL, name="lower")
    parts.append(lower)

    # ejection port with the carrier behind it, forward assist and deflector
    port = box_cutter("port", (0.018, 0.060, 0.024),
                      (hw * 1.02, front + 0.072, bore + 0.010), bevel=0.0040)
    cut(upper, [port])
    parts.append(box("port_shadow", (0.012, 0.056, 0.022),
                     (hw * 0.28, front + 0.072, bore + 0.010), surface=RUBBER))
    parts.append(disc("forward_assist", (0.0, rear - 0.020, top - 0.008),
                      0.0065, 0.011, axis="Y"))
    parts.append(loft(round_rect(0.0075, 0.0090, 0.003), [
        (rear - 0.004, 1.0, 1.0, hw * 0.80, top - 0.012),
        (rear - 0.028, 1.0, 0.75, hw * 0.90, top - 0.012),
    ], surface=METAL, name="deflector"))

    # controls: bolt catch, magazine release, selector on both sides
    parts.append(box("bolt_catch", (0.0055, 0.030, 0.013),
                     (-hw - 0.0016, ctx["mag_y"] + 0.052, bottom + 0.020),
                     surface=METAL, bevel=0.0012))
    parts.append(disc("mag_release", (-hw - 0.0010, ctx["mag_y"] + 0.020,
                                      bottom + 0.020), 0.0052, 0.0034))
    for side in (-1.0, 1.0):
        parts.append(disc("selector_pivot", (side * (hw + 0.0010),
                                             ctx["grip_y"] - 0.062,
                                             bottom + 0.026), 0.0042, 0.0034))
        parts.append(box("selector_arm", (0.0030, 0.020, 0.0075),
                         (side * (hw + 0.0026), ctx["grip_y"] - 0.052,
                          bottom + 0.026), surface=METAL, bevel=0.0010))

    # machined panel lines and fastener heads: the lower is a flat slab
    # otherwise, and a flat slab is what reads as "unfinished" at render size
    for side in (-1.0, 1.0):
        parts.extend(panel_frame("lower_panel", hw * 0.985,
                                 ctx["mag_y"] + 0.052, ctx["grip_y"] - 0.012,
                                 bottom + 0.007, bottom + 0.033, side))
        for y in (ctx["mag_y"] + 0.042, ctx["grip_y"] - 0.022):
            for z in (bottom + 0.012, bottom + 0.028):
                parts.extend(screw_head("lower_screw", hw * 0.986, y, z, side,
                                        0.0025))
        parts.extend(screw_head("upper_screw", hw * 0.972, front + 0.030,
                                top - 0.020, side, 0.0024))
        parts.extend(screw_head("upper_screw", hw * 0.972, rear - 0.026,
                                top - 0.020, side, 0.0024))

    # buffer tube and castle nut: the stock rides on these
    tube_len = 0.200
    parts.append(tube("buffer_tube", [(rear - 0.012, 0.0158),
                                      (rear + tube_len, 0.0158)],
                      top - upper_h * 0.5, surface=METAL, segments=24))
    parts.append(band("castle_nut", rear + 0.016, 0.0190, 0.011,
                      top - upper_h * 0.5, surface=METAL, segments=22))
    parts.append(band("end_plate", rear + 0.004, 0.0200, 0.005,
                      top - upper_h * 0.5, surface=BARE, segments=22))
    return parts


def receiver_smg(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Polymer shelled compact: a moulded body with vents and a magazine shroud."""
    hw, top, bottom = ctx["hw"], ctx["top"], ctx["bottom"]
    front, rear, bore = ctx["front"], ctx["rear"], ctx["bore"]
    parts: List[bpy.types.Object] = []
    shell = loft(round_rect(hw, (top - bottom) * 0.5, 0.012, steps=4), [
        (front + 0.004, 0.88, 0.90, 0.0, 0.0),
        (front, 0.97, 0.97, 0.0, 0.0),
        (front + 0.040, 1.00, 1.00, 0.0, 0.0),
        (rear - 0.016, 1.00, 1.00, 0.0, 0.0),
        (rear, 0.94, 0.90, 0.0, 0.0),
    ], surface=POLYMER, name="shell")
    parts.append(shell)
    parts.append(rail("smg_rail", front + 0.010, rear - 0.010, top + 0.0012,
                      0.0190, 0.0056))
    for index in range(4):
        y = front - 0.012 - index * 0.014
        for side in (-1.0, 1.0):
            parts.append(box("vent", (0.0030, 0.009, 0.012),
                             (side * (hw - 0.0016), y, bore + 0.006),
                             surface=RUBBER))
    for side in (-1.0, 1.0):
        parts.append(seam("shell_seam", (front + rear) * 0.5, 0.0, ctx["rl"] * 0.42,
                          0.022, hw, side, surface=METAL))
    parts.append(box("ejection_port", (0.0060, 0.042, 0.014),
                     (hw * 0.99, front + 0.048, bore + 0.012), surface=RUBBER))
    for side in (-1.0, 1.0):
        parts.extend(panel_frame("shell_panel", hw, front + 0.014,
                                 rear - 0.022, bottom + 0.010,
                                 bottom + 0.030, side, thickness=0.0015))
        for y in (front + 0.026, rear - 0.034):
            parts.extend(screw_head("shell_screw", hw + 0.0005, y,
                                    bottom + 0.020, side, 0.0026))
    parts.append(box("magwell", (hw * 2.05, 0.086, 0.044),
                     (0.0, ctx["mag_y"], bottom - 0.010), surface=POLYMER,
                     bevel=0.0020))
    parts.append(disc("takedown_pin", (hw + 0.0012, rear - 0.024, bottom + 0.012),
                      0.0038, 0.0030))
    return parts


def receiver_bolt(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Bolt action: round action, bolt handle, chassis rails underneath."""
    hw, bore, front, rear = ctx["hw"], ctx["bore"], ctx["front"], ctx["rear"]
    parts: List[bpy.types.Object] = []
    action = tube("action", [
        (front + 0.010, hw * 0.94), (front - 0.004, hw * 1.02),
        (front - 0.030, hw * 1.06), (rear - 0.030, hw * 1.02),
        (rear, hw * 0.94),
    ], bore, surface=METAL, segments=26)
    parts.append(action)
    parts.append(band("recoil_lug", front - 0.028, hw * 1.16, 0.010, bore,
                      surface=METAL, segments=24))
    # bolt with a handle out to the right and a bolt knob
    parts.append(tube("bolt_body", [(rear - 0.080, hw * 0.60), (rear + 0.012, hw * 0.60)],
                      bore + 0.002, surface=BARE, segments=20))
    parts.append(tube("bolt_handle", [(rear - 0.058, hw * 0.30), (rear - 0.054, hw * 0.30)],
                      bore, surface=BARE, segments=14))
    handle = tube("bolt_handle_body", [(-0.002, 0.0055), (0.052, 0.0055)],
                  bore - 0.006, surface=BARE, segments=14, x=hw * 0.75)
    handle.data.transform(mathutils.Matrix.Rotation(math.radians(12.0), 4, "Y"))
    parts.append(handle)
    parts.append(torus("bolt_knob", (hw + 0.056, rear - 0.058, bore - 0.012),
                       0.011, 0.0058, surface=BARE, segments=18, axis="Y"))
    # ejection port and the trigger housing that carries the chassis
    port = box_cutter("port", (0.020, 0.056, 0.020),
                      (hw * 1.05, front + 0.080, bore + 0.004), bevel=0.0040)
    cut(action, [port])
    parts.append(tube("housing", [(front + 0.030, hw * 0.92),
                                  (rear - 0.040, hw * 0.92)],
                      bore - 0.046, surface=METAL, segments=20))
    for side in (-1.0, 1.0):
        parts.extend(screw_head("action_screw", hw * 1.02, front + 0.020,
                                bore - 0.024, side, 0.0034))
        parts.extend(screw_head("action_screw", hw * 1.02, rear - 0.024,
                                bore - 0.024, side, 0.0034))
    return parts


def receiver_shotgun(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Pump action: a deep receiver with a loading gate and a trigger plate."""
    hw, top, bottom = ctx["hw"], ctx["top"], ctx["bottom"]
    front, rear, bore = ctx["front"], ctx["rear"], ctx["bore"]
    parts: List[bpy.types.Object] = []
    body = loft(round_rect(hw, (top - bottom) * 0.5, 0.010, steps=4), [
        (front + 0.004, 0.88, 0.92, 0.0, 0.0),
        (front - 0.006, 0.98, 1.00, 0.0, 0.0),
        (front + 0.050, 1.00, 1.00, 0.0, 0.0),
        (rear - 0.012, 1.00, 0.98, 0.0, 0.0),
        (rear, 0.92, 0.90, 0.0, 0.0),
    ], surface=METAL, name="receiver")
    parts.append(body)
    port = box_cutter("port", (0.018, 0.058, 0.020),
                      (hw * 1.02, front + 0.068, bore + 0.006), bevel=0.0040)
    cut(body, [port])
    parts.append(box("port_shadow", (0.012, 0.054, 0.018),
                     (hw * 0.30, front + 0.068, bore + 0.006), surface=RUBBER))
    parts.append(box("loading_gate", (hw * 1.40, 0.040, 0.006),
                     (0.0, front + 0.078, bottom - 0.002), surface=METAL,
                     rot=(18.0, 0.0, 0.0)))
    # trigger plate drops below the receiver so the guard and trigger are not
    # buried inside the receiver silhouette
    parts.append(box("trigger_plate", (hw * 1.24, 0.074, 0.014),
                     (0.0, ctx["grip_y"] - 0.026, bottom - 0.012),
                     surface=METAL, bevel=0.0020))
    for side in (-1.0, 1.0):
        parts.append(disc("plate_pin", (side * 0.006, ctx["grip_y"] - 0.048,
                                        bottom - 0.004), 0.0032, 0.0030))
    parts.append(box("slide_track", (hw * 2.02, 0.088, 0.0075),
                     (0.0, front - 0.030, bottom + 0.030), surface=METAL))
    for side in (-1.0, 1.0):
        parts.extend(panel_frame("receiver_panel", hw, front + 0.020,
                                 rear - 0.018, bore + 0.014, top - 0.010,
                                 side, thickness=0.0015))
        for y in (front + 0.032, rear - 0.030):
            parts.extend(screw_head("receiver_pin", hw + 0.0004, y,
                                    bore + 0.022, side, 0.0030, surface=METAL))
    return parts


# --------------------------------------------------------------------------
# Handguards
# --------------------------------------------------------------------------
def handguard_wood(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """AK fore-end: walnut lower with a belly and a separate upper over the gas tube."""
    bore = ctx["bore"]
    length = spec.get("handguard_len", 0.190)
    start = ctx["front"] + 0.006
    end = start - length
    parts: List[bpy.types.Object] = []
    lower = loft(round_rect(0.0252, 0.0198, 0.0085, steps=4), [
        (start + 0.002, 0.88, 0.84, 0.0, bore - 0.012),
        (start - 0.016, 1.00, 1.00, 0.0, bore - 0.014),
        (end + 0.030, 1.00, 1.00, 0.0, bore - 0.014),
        (end + 0.008, 0.97, 0.95, 0.0, bore - 0.013),
        (end, 0.92, 0.88, 0.0, bore - 0.012),
    ], surface=WOOD, name="handguard_lower")
    parts.append(lower)
    # finger grooves along the sides and the palm swell
    for side in (-1.0, 1.0):
        for index in range(3):
            y = start - 0.048 - index * 0.043
            parts.append(box("handguard_groove", (0.0026, 0.024, 0.0075),
                             (side * 0.0242, y, bore - 0.024), surface=WOOD))
    parts.append(loft(round_rect(0.0172, 0.0125, 0.0068, steps=3), [
        (start - 0.010, 0.90, 0.80, 0.0, bore + 0.024),
        (start - 0.028, 1.00, 1.00, 0.0, bore + 0.026),
        (end + 0.030, 1.00, 1.00, 0.0, bore + 0.026),
        (end + 0.012, 0.94, 0.90, 0.0, bore + 0.024),
    ], surface=WOOD, name="handguard_upper"))
    parts.append(band("handguard_retainer", start - 0.004, 0.0272, 0.012, bore - 0.013,
                      surface=METAL, segments=24))
    return parts


def handguard_mlok(ctx: dict, spec: dict, surface: str) -> List[bpy.types.Object]:
    """Free float tube: rounded octagon, recessed M-LOK pockets, rails top and bottom."""
    bore = ctx["bore"]
    radius = spec["barrel_r"] * spec.get("handguard_scale", 2.6)
    length = spec.get("handguard_len", 0.250)
    start = ctx["front"] + 0.004
    end = start - length
    parts: List[bpy.types.Object] = []
    shell = loft(round_rect(radius, radius * 0.94, radius * 0.66, steps=5), [
        (start, 0.94, 0.94, 0.0, bore - 0.002),
        (start - 0.016, 1.00, 1.00, 0.0, bore - 0.002),
        (end + 0.030, 1.00, 1.00, 0.0, bore - 0.002),
        (end + 0.008, 0.95, 0.93, 0.0, bore - 0.002),
        (end, 0.88, 0.86, 0.0, bore - 0.002),
    ], surface=surface, name="handguard")
    parts.append(shell)
    parts.append(band("barrel_nut", start + 0.002, radius * 1.04, 0.016, bore - 0.002,
                      surface=METAL, segments=24))
    slots = max(3, int(length / 0.056))
    for side in (-1.0, 1.0):
        for row in (-1.0, 1.0):
            for index in range(slots):
                y = start - length * (index + 0.62) / (slots + 0.30)
                cutter = slot_cutter("mlok", y, bore - 0.002 + row * radius * 0.34,
                                     0.0150, 0.0042, side * radius * 0.60,
                                     radius * 0.98)
                cut(shell, [cutter])
                parts.append(box("mlok_floor", (0.0020, 0.028, 0.0082),
                                 (side * radius * 0.30,
                                  y, bore - 0.002 + row * radius * 0.34),
                                 surface=RUBBER))
    parts.append(rail("handguard_rail", start - 0.004, end + 0.006,
                      bore + radius * 0.90, 0.0210, 0.0058))
    parts.append(rail("under_rail", start - 0.028, start - length * 0.52,
                      bore - radius * 0.92, 0.0180, 0.0052))
    parts.append(band("handguard_cap", end + 0.006, radius * 0.94, 0.008,
                      bore - 0.002, surface=METAL, segments=24))
    return parts


def handguard_pump(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Shotgun pump: ribbed walnut fore-end on a slide tube."""
    bore = ctx["bore"]
    length = spec.get("handguard_len", 0.190)
    start = ctx["front"] - 0.030
    end = start - length
    parts: List[bpy.types.Object] = []
    parts.append(tube("slide_tube", [(start + 0.020, 0.0148), (end - 0.010, 0.0148)],
                      bore - 0.004, surface=METAL, segments=22))
    parts.append(loft(round_rect(0.0262, 0.0232, 0.011, steps=4), [
        (start, 0.80, 0.78, 0.0, bore - 0.008),
        (start - 0.016, 1.00, 1.00, 0.0, bore - 0.008),
        (end + 0.018, 1.00, 1.00, 0.0, bore - 0.008),
        (end, 0.88, 0.86, 0.0, bore - 0.008),
    ], surface=WOOD, name="pump"))
    for index in range(4):
        y = start - 0.030 - index * 0.032
        for side in (-1.0, 1.0):
            parts.append(box("pump_rib", (0.0030, 0.020, 0.010),
                             (side * 0.0248, y, bore - 0.010), surface=WOOD))
    return parts


# --------------------------------------------------------------------------
# Stocks
# --------------------------------------------------------------------------
def stock_ar(ctx: dict, spec: dict, surface: str = POLYMER,
             cheek_riser: bool = False) -> List[bpy.types.Object]:
    """Collapsible stock: wedge body on an exposed buffer tube, rubber pad.

    The stock used to start at the receiver, so the buffer tube was buried and
    the butt looked like a slab glued to the back. It now rides 80 mm out on
    the tube, with a castle nut and a collar at the joint.
    """
    hw = ctx["hw"]
    axis_z = ctx["top"] - (ctx["top"] - ctx["bottom"]) * 0.30
    length = spec.get("stock_len", 0.205)
    start = ctx["rear"] + 0.070
    end = start + length
    parts: List[bpy.types.Object] = []

    outline = [
        (start, 0.020), (start + 0.020, 0.028), (start + 0.070, 0.030),
        (end - 0.040, 0.033), (end - 0.010, 0.038), (end, 0.038),
        (end, -0.060), (end - 0.010, -0.061), (end - 0.044, -0.050),
        (end - 0.086, -0.030), (start + 0.058, -0.020), (start + 0.016, -0.006),
    ]
    parts.append(prism("stock", [(y, axis_z + dz) for y, dz in outline],
                       hw * 0.92, surface=surface, bevel_width=0.0075, segments=4))
    # collar that clamps the stock to the tube
    parts.append(box("stock_collar", (hw * 2.06, 0.020, 0.052),
                     (0.0, start + 0.008, axis_z - 0.006), surface=METAL,
                     bevel=0.0026))
    parts.append(box("stock_lever", (0.016, 0.030, 0.010),
                     (0.0, start + 0.030, axis_z - 0.028), surface=METAL,
                     bevel=0.0014))
    pad = [(end - 0.004, 0.039), (end + 0.014, 0.035),
           (end + 0.014, -0.058), (end - 0.004, -0.060)]
    parts.append(prism("recoil_pad", [(y, axis_z + dz) for y, dz in pad],
                       hw * 0.90, surface=RUBBER, bevel_width=0.0046, segments=2))
    # comb: a slight raised ridge along the top of the butt
    parts.append(box("stock_comb", (hw * 1.10, length * 0.52, 0.008),
                     (0.0, start + length * 0.52, axis_z + 0.036), surface=surface,
                     bevel=0.0028))
    for side in (-1.0, 1.0):
        parts.extend(screw_head("stock_screw", hw * 0.93, start + 0.052,
                                axis_z - 0.010, side, 0.0026))
        parts.append(disc("qd_socket", (side * (hw * 0.86), end - 0.086,
                                        axis_z - 0.030), 0.0044, 0.0034))
    parts.extend(swivel("stock_swivel", end - 0.052, axis_z - 0.044, hw * 0.82))
    if cheek_riser:
        riser = [(start + 0.040, axis_z + 0.040), (start + 0.056, axis_z + 0.058),
                 (end - 0.050, axis_z + 0.060), (end - 0.032, axis_z + 0.040)]
        parts.append(prism("cheek_riser", riser, hw * 0.78, surface=RUBBER,
                           bevel_width=0.0042, segments=3))
    return parts


def stock_ak(ctx: dict, spec: dict, surface: str = WOOD) -> List[bpy.types.Object]:
    """Fixed walnut butt stock: wrist, comb and a steel butt plate."""
    hw = ctx["hw"]
    length = spec.get("stock_len", 0.235)
    axis_z = ctx["top"] - (ctx["top"] - ctx["bottom"]) * 0.40
    start = ctx["rear"] - 0.014
    end = start + length
    outline = [
        (start, 0.020), (start + 0.046, 0.018), (end - 0.034, 0.024),
        (end, 0.028), (end, -0.058), (end - 0.022, -0.056),
        (end - 0.074, -0.044), (start + 0.062, -0.038), (start + 0.016, -0.014),
    ]
    parts: List[bpy.types.Object] = [
        prism("stock", [(y, axis_z + dz) for y, dz in outline], hw * 0.84,
              surface=surface, bevel_width=0.0065, segments=4),
    ]
    parts.append(box("butt_plate", (hw * 1.72, 0.009, 0.086),
                     (0.0, end + 0.003, axis_z - 0.015), surface=METAL,
                     bevel=0.0022))
    parts.append(box("sling_slot", (hw * 1.84, 0.016, 0.008),
                     (0.0, end - 0.048, axis_z - 0.040), surface=METAL))
    parts.extend(swivel("stock_swivel", end - 0.050, axis_z - 0.046, hw * 0.80))
    return parts


def stock_chassis(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """One-piece chassis: a solid skeleton with a single rounded cut-out.

    The previous version was two 11 mm plates with a 20 mm hole through them,
    which read as a see-through frame from every angle - thin plates and open
    voids are what made the sheet look holey. This is one thick body with a
    thumbhole, a hinge block on the receiver and a real cheek riser.
    """
    hw = ctx["hw"]
    bore = ctx["bore"]
    length = spec.get("stock_len", 0.260)
    start = ctx["rear"] - 0.012
    end = start + length
    parts: List[bpy.types.Object] = []

    outline = [
        (start, bore + 0.006), (start + 0.050, bore + 0.010),
        (end - 0.044, bore + 0.014), (end, bore + 0.014),
        (end, bore - 0.092), (end - 0.040, bore - 0.098),
        (start + 0.090, bore - 0.086), (start + 0.014, bore - 0.052),
    ]
    body = prism("chassis", outline, 0.0205, surface=OLIVE,
                 bevel_width=0.0042, segments=3)
    # thumbhole: a capsule, so the cut-out has round ends instead of corners
    hole_y = start + length * 0.58
    hole_z = bore - 0.044
    points = [(hole_y + u, hole_z + v) for u, v in
              stadium(length * 0.150, 0.0165, 6)]
    hole = prism("chassis_hole", points, 0.0260, surface=OLIVE)
    cut(body, [hole])
    parts.append(body)

    # hinge block: the chassis folds onto the receiver, so it needs a real joint
    parts.append(box("chassis_hinge", (0.045, 0.038, 0.056),
                     (0.0, start + 0.014, bore - 0.026), surface=METAL,
                     bevel=0.0034))
    parts.append(torus("chassis_hinge_pin", (0.0, start + 0.006, bore - 0.052),
                       0.010, 0.0034, surface=BARE, segments=20, axis="X"))
    # Cheek riser, seated on the spine.  It used to sit 4 mm above it with two posts
    # buried inside the body, which on the sheet looked like a bar floating over the stock.
    riser_z = bore + 0.0215
    parts.append(box("cheek_riser", (0.031, 0.132, 0.019),
                     (0.0, start + length * 0.52, riser_z), surface=RUBBER,
                     bevel=0.0045))
    for side in (-1.0, 1.0):
        parts.append(loft(oval(0.0028, 0.0028, steps=8), [
            (bore + 0.008, 1.0, 1.0, side * 0.0225, start + length * 0.52),
            (riser_z, 1.0, 1.0, side * 0.0225, start + length * 0.52),
        ], surface=BARE, name="riser_post", axis="Z"))
    parts.append(box("recoil_pad", (0.038, 0.020, 0.110),
                     (0.0, end + 0.004, bore - 0.040), surface=RUBBER,
                     bevel=0.0050))
    parts.append(box("butt_hook", (0.038, 0.032, 0.015),
                     (0.0, end - 0.012, bore + 0.006), surface=OLIVE,
                     bevel=0.0030))
    # bottom accessory rail: the sort of thing a chassis always wears
    parts.append(rail("chassis_rail", start + 0.052, start + 0.150,
                      bore - 0.098, 0.0180, 0.0052))
    return parts


def stock_shotgun(ctx: dict, spec: dict, surface: str = WOOD) -> List[bpy.types.Object]:
    """Walnut butt stock swept from a rounded section, with a fitted recoil pad.

    It used to be a straight-sided slab extruded through its width with a 98 mm box on
    the end.  The box was taller than the wood, so the silhouette read as a plank with a
    plate bolted on.  A stock is a solid that tapers in both axes and drops slightly at
    the heel; lofting a rounded section through those sizes gets both, and the pad is
    now cut from the stock's own end instead of being a separate, larger box.
    """
    hw = ctx["hw"]
    length = spec.get("stock_len", 0.270)
    axis_z = ctx["bore"] - 0.012
    start = ctx["rear"] - 0.012
    end = start + length

    profile = round_rect(hw * 0.78, 0.030, 0.013, steps=4)
    sections = [
        # (y, width scale, height scale, x offset, drop)
        (start, 0.92, 0.72, 0.0, 0.000),
        (start + 0.048, 0.98, 0.80, 0.0, 0.001),
        (start + 0.110, 1.00, 0.94, 0.0, -0.001),
        (start + 0.190, 1.03, 1.10, 0.0, -0.005),
        (end - 0.012, 1.05, 1.19, 0.0, -0.008),
        (end, 1.02, 1.16, 0.0, -0.009),
    ]
    parts: List[bpy.types.Object] = [
        loft(profile, [(y, su, sv, du, axis_z + dv) for y, su, sv, du, dv in sections],
             surface=surface, name="stock"),
        # recoil pad: the same section, slightly proud, and only 13 mm thick
        loft(round_rect(hw * 0.78 * 1.03, 0.030 * 1.19, 0.013, steps=4), [
            (end - 0.002, 1.0, 1.0, 0.0, axis_z - 0.008),
            (end + 0.011, 0.98, 0.98, 0.0, axis_z - 0.009),
        ], surface=RUBBER, name="butt_pad"),
        # wrist: fills the gap between the receiver tang and the comb
        loft(round_rect(hw * 0.62, 0.021, 0.011, steps=3), [
            (ctx["rear"] + 0.002, 0.90, 0.86, 0.0, axis_z - 0.004),
            (ctx["rear"] + 0.022, 1.00, 1.00, 0.0, axis_z - 0.006),
            (start + 0.040, 1.00, 0.98, 0.0, axis_z - 0.007),
        ], surface=surface, name="wrist"),
        # comb: the ridge the cheek rests on.  It follows the top of the stock rather
        # than a fixed height - at a fixed 24 mm it broke the surface near the wrist and
        # sank into it at the butt, which read as a bar floating over the wood.
        loft(round_rect(hw * 0.46, 0.0050, 0.0042, steps=3), [
            (start + 0.050, 0.86, 1.00, 0.0, axis_z + 0.0200),
            (start + 0.140, 1.00, 1.00, 0.0, axis_z + 0.0262),
            (end - 0.016, 1.00, 0.94, 0.0, axis_z + 0.0315),
        ], surface=surface, name="comb"),
        box("butt_plate_screw", (0.012, 0.004, 0.012),
            (0.0, end + 0.012, axis_z - 0.009), surface=BARE, bevel=0.0014),
    ]
    parts.extend(swivel("stock_swivel", end - 0.056, axis_z - 0.040, hw * 0.80))
    return parts


def stock_wire(ctx: dict, spec: dict) -> List[bpy.types.Object]:
    """Folding skeleton stock: a closed frame with a comb bar and a rounded shoulder plate.

    Two rods and a box read as loose scrap in silhouette - the frame was open at the back
    so nothing connected the rods to the plate.  A folding stock is a closed loop: the
    rods are joined by a vertical strut at the hinge, a cross bar gives the cheek
    something to rest on, and the plate is a rounded solid rather than a slab.
    """
    hw = ctx["hw"]
    length = spec.get("stock_len", 0.150)
    axis_z = ctx["top"] - (ctx["top"] - ctx["bottom"]) * 0.42
    start = ctx["rear"] + 0.004
    end = start + length
    side_x = hw * 0.62
    upper = axis_z + 0.012
    lower = axis_z - 0.030

    parts: List[bpy.types.Object] = []
    for side in (-1.0, 1.0):
        parts.append(tube("stock_rod", [(start, 0.0046), (end - 0.006, 0.0044)],
                          upper, surface=BARE, segments=14, x=side * side_x))
        parts.append(tube("stock_rod_low", [(start, 0.0042), (end - 0.020, 0.0040)],
                          lower, surface=BARE, segments=14, x=side * side_x))
        # the strut that closes the frame at the hinge: a vertical rod, swept along Z
        parts.append(loft(oval(0.0038, 0.0038, steps=10), [
            (lower, 1.0, 1.0, side * side_x, end - 0.018),
            (upper, 1.0, 1.0, side * side_x, end - 0.018),
        ], surface=BARE, name="stock_strut", axis="Z"))
    # comb bar across the frame, and the hinge knuckle it folds on
    parts.append(loft(oval(0.0050, 0.0050, steps=10), [
        (-side_x, 1.0, 1.0, end - 0.018, upper + 0.006),
        (side_x, 1.0, 1.0, end - 0.018, upper + 0.006),
    ], surface=BARE, name="stock_comb", axis="X"))
    parts.append(box("stock_hinge", (hw * 1.20, 0.024, 0.022),
                     (0.0, start + 0.002, axis_z - 0.008), surface=METAL,
                     bevel=0.0026))
    # shoulder plate: rounded, slightly angled, with a rubber face
    parts.append(loft(round_rect(hw * 0.72, 0.040, 0.016, steps=4), [
        (end - 0.014, 0.86, 0.90, 0.0, axis_z - 0.008),
        (end + 0.002, 1.00, 1.00, 0.0, axis_z - 0.009),
        (end + 0.012, 0.94, 0.96, 0.0, axis_z - 0.010),
    ], surface=METAL, name="shoulder_plate"))
    parts.append(loft(round_rect(hw * 0.62, 0.034, 0.015, steps=4), [
        (end + 0.012, 1.00, 1.00, 0.0, axis_z - 0.010),
        (end + 0.020, 0.92, 0.94, 0.0, axis_z - 0.010),
    ], surface=RUBBER, name="shoulder_pad"))
    return parts


# --------------------------------------------------------------------------
# Grip and trigger group
# --------------------------------------------------------------------------
def grip(ctx: dict, spec: dict, surface: str = POLYMER, length: float = 0.104,
         rake: float = 20.0) -> List[bpy.types.Object]:
    """Pistol grip: a raked stack of rounded sections with a palm swell.

    The previous grip was a side profile extruded through a constant width,
    which produced the flat blade that showed up in every render.  A real grip
    changes section along its length (narrow at the top, swelled at the palm,
    flared at the butt), so it is lofted instead.
    """
    top = ctx["bottom"] + 0.020
    y_top = ctx["grip_y"]
    scale = length / 0.104
    table = [
        (0.000, 0.86, 0.74), (0.012, 0.98, 0.90), (0.030, 1.02, 0.99),
        (0.052, 1.00, 1.02), (0.074, 0.99, 1.00), (0.093, 1.03, 1.05),
        (0.104, 0.90, 0.92),
    ]
    profile = round_rect(0.0168, 0.0165, 0.0072, steps=6)
    sections = []
    for t, w, d in table:
        sections.append((-t * scale, w, d, 0.0, math.tan(math.radians(rake)) * t * scale))
    body = loft(profile, sections, surface=surface, name="grip", axis="Z",
                x_centre=0.0)
    body.data.transform(mathutils.Matrix.Translation((0.0, y_top, top)))
    parts: List[bpy.types.Object] = [body]

    # finger grooves across the front strap, and a butt cap
    for index in range(3):
        t = (0.030 + index * 0.023) * scale
        parts.append(box("grip_groove", (0.0330, 0.0075, 0.0052),
                         (0.0, y_top + math.tan(math.radians(rake)) * t
                          - 0.0165 * (0.99 - index * 0.01),
                          top - t), surface=RUBBER, bevel=0.0018))
    parts.append(loft(round_rect(0.0158, 0.0160, 0.0062, steps=5),
                      [(0.0, 1.0, 1.0, 0.0, 0.0), (-0.008, 0.94, 0.92, 0.0, 0.0)],
                      surface=RUBBER, name="grip_cap", axis="Z"))
    parts[-1].data.transform(mathutils.Matrix.Translation(
        (0.0, y_top + math.tan(math.radians(rake)) * length,
         top - length)))
    tangent_y = math.tan(math.radians(rake))
    for side in (-1.0, 1.0):
        for seat in (0.34, 0.72):
            t = length * seat
            parts.append(box("grip_panel", (0.0020, 0.030, 0.034 * scale),
                             (side * 0.0168, y_top + tangent_y * t, top - t),
                             surface=RUBBER, bevel=0.0008))
        t = length * 0.72
        parts.append(disc("grip_screw", (side * 0.0172, y_top + tangent_y * t + 0.006,
                                         top - t), 0.0030, 0.0038, axis="X",
                          surface=BARE))
    return parts


def trigger_group(ctx: dict, spec: dict, guard_len: float = 0.070) -> List[bpy.types.Object]:
    """Trigger guard, trigger blade, bow release and selector.

    The first version was a flat stirrup with a 4.4 mm blade tucked so far back that
    the receiver's trigger housing hid it: from the side the guard looked empty, which
    is what "where is the trigger" means.  A trigger has to be the most readable thing
    under the receiver - it is where the eye goes.  So the guard is a deeper loop, the
    blade is a curved 7 mm-wide shoe hanging in the middle of the opening, and there is
    a trigger pin through the housing, a magazine release behind the guard and a
    selector above the grip.
    """
    z_top = ctx["bottom"] + 0.014
    if spec.get("stock") == "shotgun":
        y_rear = ctx["rear"] - 0.014
    else:
        y_rear = ctx["grip_y"] - 0.012
    y_front = y_rear - guard_len
    drop = spec.get("guard_drop", 0.030)

    # The guard: a flat bar swept round a proper loop, deeper than it is wide, so the
    # opening reads as an opening from the side.
    points = [(y_front, z_top + 0.004),
              (y_front - 0.002, z_top - drop * 0.30),
              (y_front + 0.006, z_top - drop * 0.72),
              (y_front + 0.022, z_top - drop),
              (y_rear - 0.026, z_top - drop),
              (y_rear - 0.008, z_top - drop * 0.72),
              (y_rear - 0.002, z_top - drop * 0.30),
              (y_rear, z_top + 0.004)]
    parts: List[bpy.types.Object] = [
        sweep_bar("trigger_guard", points, 0.0068, 0.0044, surface=METAL),
    ]

    # The blade: an S-curved shoe, wide enough to see, its face turned forward, sitting
    # in the middle of the guard opening where a finger would actually be.
    blade_y = y_front + guard_len * 0.34
    # 9 mm across, 8 mm front to back, with a face that curves forward under the finger
    # and a rounded toe - a trigger is a shoe.  It sits in the front half of the
    # opening: tucked against the rear of the guard it was hidden by the housing.
    blade = loft(round_rect(0.0060, 0.0034, 0.0026, steps=3), [
        (z_top - 0.003, 1.00, 1.00, 0.0, blade_y + 0.0035),
        (z_top - 0.010, 0.98, 1.00, 0.0, blade_y + 0.0015),
        (z_top - 0.017, 0.92, 0.98, 0.0, blade_y - 0.0015),
        (z_top - 0.023, 0.78, 0.92, 0.0, blade_y - 0.0045),
        (z_top - 0.027, 0.56, 0.80, 0.0, blade_y - 0.0065),
    ], surface=BARE, name="trigger", axis="Z")
    parts.append(blade)

    # trigger pin: one flush pin head on each side of the housing, level with the
    # blade's pivot, so the pivot is visible from the outside instead of floating
    for side in (-1.0, 1.0):
        parts.append(disc("trigger_pin", (side * ctx["hw"] * 1.005, blade_y + 0.010,
                                          z_top - 0.0025),
                          0.0028, 0.0016, surface=BARE))

    # magazine release paddle behind the guard, and the selector above the grip
    parts.append(box("mag_release", (0.0080, 0.016, 0.0070),
                     (0.0, y_rear + 0.020, z_top - drop * 0.30),
                     surface=BARE, bevel=0.0012))
    # selector: a disc through the receiver with a short lever pointing forward
    parts.append(disc("selector", (ctx["hw"] * 0.53, y_rear + 0.030, z_top + 0.006),
                      0.0055, 0.0018, surface=BARE))
    parts.append(sweep_bar("selector_lever", [
        (y_rear + 0.030, z_top + 0.006), (y_rear + 0.012, z_top + 0.008),
    ], 0.0018, 0.0034, surface=BARE, x_centre=ctx["hw"] * 0.56))
    return parts


def sweep_bar(name: str, points: Sequence[Tuple[float, float]], half_w: float,
              half_t: float, surface: str = METAL, x_centre: float = 0.0
              ) -> bpy.types.Object:
    """Sweep a rectangular profile along a polyline in YZ (flat bar stock)."""
    verts: List[Tuple[float, float, float]] = []
    for index, (y, z) in enumerate(points):
        previous = points[max(0, index - 1)]
        following = points[min(len(points) - 1, index + 1)]
        tangent = mathutils.Vector((0.0, following[0] - previous[0],
                                    following[1] - previous[1]))
        if tangent.length < 1e-6:
            tangent = mathutils.Vector((0.0, 1.0, 0.0))
        tangent.normalize()
        up = tangent.cross(mathutils.Vector((1.0, 0.0, 0.0))).normalized()
        for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            verts.append((x_centre + sx * half_w,
                          y + up.y * sz * half_t,
                          z + up.z * sz * half_t))
    faces: List[tuple] = []
    for ring in range(len(points) - 1):
        first = ring * 4
        second = (ring + 1) * 4
        for i in range(4):
            j = (i + 1) % 4
            faces.append((first + i, first + j, second + j, second + i))
    faces.append((3, 2, 1, 0))
    base = (len(points) - 1) * 4
    faces.append((base + 0, base + 1, base + 2, base + 3))
    obj = mesh_object(name, verts, faces, surface)
    recalc(obj)
    return obj


# --------------------------------------------------------------------------
# Magazines
# --------------------------------------------------------------------------
def magazine_parts(spec: dict, ctx: dict) -> Tuple[List[bpy.types.Object], Tuple[float, float]]:
    """Curved magazine: lofted body, transverse ribs, lipped floor plate."""
    mag = spec["mag"]
    length = mag["length"]
    half_w = mag["width"] * 0.5
    half_d = mag["depth"] * 0.5
    curve = math.radians(mag.get("curve", 8.0))
    surface = mag.get("surface", METAL)
    pivot_y = ctx["mag_y"]
    pivot_z = ctx["bottom"] + 0.020

    profile = round_rect(half_w, half_d, 0.0058, steps=6)
    sections = []
    steps = 8
    for index in range(steps):
        t = index / (steps - 1)
        forward = -math.sin(curve) * length * t * t
        taper = 1.0 - 0.05 * t
        sections.append((-length * t, taper, taper, 0.0, forward))
    body = loft(profile, sections, surface=surface, name="magazine", axis="Z")
    body.data.transform(mathutils.Matrix.Translation((0.0, pivot_y, pivot_z)))
    parts: List[bpy.types.Object] = [body]

    tip_forward = -math.sin(curve) * length
    # transverse stiffening ribs: what makes a magazine read as pressed steel
    for index, t in enumerate((0.24, 0.52, 0.80)):
        forward = -math.sin(curve) * length * t * t
        taper = 1.0 - 0.05 * t
        parts.append(box("mag_rib", (mag["width"] * (taper + 0.10),
                                     mag["depth"] * (taper + 0.06), 0.0072),
                         (0.0, pivot_y + forward, pivot_z - length * t),
                         surface=surface, bevel=0.0016))
    # floor plate: a lipped base plate, same finish as the body so it does not
    # read as a bright shim stuck under the magazine
    parts.append(box("floor_lip", (mag["width"] * 1.10, mag["depth"] * 1.06, 0.0042),
                     (0.0, pivot_y + tip_forward, pivot_z - length - 0.0072),
                     surface=surface, bevel=0.0012))
    parts.append(box("floor_plate", (mag["width"] * 1.16, mag["depth"] * 1.10, 0.0092),
                     (0.0, pivot_y + tip_forward, pivot_z - length - 0.0030),
                     surface=surface, bevel=0.0022))
    parts.append(box("mag_collar", (mag["width"] * 1.26, mag["depth"] * 1.12, 0.012),
                     (0.0, pivot_y + 0.002, pivot_z - 0.006), surface=METAL,
                     bevel=0.0024))
    parts.append(box("mag_latch", (half_w * 1.7, 0.011, 0.008),
                     (0.0, pivot_y + half_d * 0.92, pivot_z - 0.010),
                     surface=BARE, bevel=0.0014))
    return parts, (pivot_y, pivot_z)


def side_hole(half_w: float) -> float:
    return half_w * 0.98


# --------------------------------------------------------------------------
# Sights and optics
# --------------------------------------------------------------------------
def optic_reddot(ctx: dict, spec: dict, rail_z: float, y: float
                 ) -> Tuple[List[bpy.types.Object], float]:
    """Aimpoint style red dot: mount, 30 mm tube, hood, turrets, coated lenses."""
    parts: List[bpy.types.Object] = []
    axis_z = rail_z + 0.030
    body = tube("optic_tube", [(y + 0.034, 0.0168), (y + 0.030, 0.0158),
                               (y - 0.028, 0.0158), (y - 0.032, 0.0172)],
                axis_z, surface=METAL, segments=24)
    parts.append(body)
    parts.append(band("objective_ring", y - 0.030, 0.0192, 0.010, axis_z,
                      surface=METAL, segments=26))
    parts.append(torus("objective_hood", (0.0, y - 0.034, axis_z), 0.0182, 0.0026,
                       surface=METAL, segments=24, axis="Y"))
    parts.append(band("eyepiece_ring", y + 0.032, 0.0186, 0.008, axis_z,
                      surface=METAL, segments=24))
    parts.append(disc("front_lens", (0.0, y - 0.0305, axis_z), 0.0146, 0.0030,
                      axis="Y", surface=GLASS))
    parts.append(disc("rear_lens", (0.0, y + 0.0315, axis_z), 0.0140, 0.0030,
                      axis="Y", surface=GLASS))
    parts.append(disc("battery_cap", (0.0, y - 0.006, axis_z - 0.016), 0.0068, 0.020,
                      axis="Z", surface=METAL))
    parts.append(disc("zero_turret", (0.0, y + 0.004, axis_z + 0.017), 0.0062, 0.016,
                      axis="Z", surface=METAL))
    parts.append(disc("windage_turret", (0.0170, y + 0.004, axis_z), 0.0058, 0.010,
                      axis="X", surface=METAL))
    # mount: clamp, riser and lever
    parts.append(box("optic_base", (0.0300, 0.044, 0.012),
                     (0.0, y, rail_z + 0.006), surface=METAL, bevel=0.0022))
    parts.append(box("optic_clamp", (0.0330, 0.020, 0.014),
                     (0.0, y - 0.008, rail_z - 0.001), surface=METAL, bevel=0.0020))
    parts.append(box("optic_lever", (0.0075, 0.026, 0.008),
                     (0.0205, y - 0.008, rail_z - 0.002), surface=BARE, bevel=0.0016))
    return parts, axis_z + 0.0172


def optic_prism(ctx: dict, spec: dict, rail_z: float, y: float
                ) -> Tuple[List[bpy.types.Object], float]:
    """Prism sight: fixed magnification, big square objective, tight housing."""
    parts: List[bpy.types.Object] = []
    axis_z = rail_z + 0.034
    parts.append(loft(round_rect(0.0175, 0.0170, 0.008, steps=4), [
        (y - 0.044, 0.94, 0.94, 0.0, axis_z),
        (y - 0.020, 1.00, 1.00, 0.0, axis_z),
        (y + 0.030, 1.00, 1.00, 0.0, axis_z),
        (y + 0.042, 0.92, 0.92, 0.0, axis_z),
    ], surface=METAL, name="prism_body"))
    parts.append(band("prism_bell", y - 0.042, 0.0196, 0.010, axis_z,
                      surface=METAL, segments=22))
    parts.append(disc("front_lens", (0.0, y - 0.0435, axis_z), 0.0160, 0.0032,
                      axis="Y", surface=GLASS))
    parts.append(disc("rear_lens", (0.0, y + 0.0415, axis_z), 0.0140, 0.0032,
                      axis="Y", surface=GLASS))
    parts.append(disc("turret_top", (0.0, y + 0.006, axis_z + 0.018), 0.0064, 0.018,
                      axis="Z", surface=METAL))
    parts.append(box("prism_base", (0.0300, 0.052, 0.014),
                     (0.0, y - 0.002, rail_z + 0.007), surface=METAL, bevel=0.0022))
    parts.append(box("prism_clamp", (0.0330, 0.022, 0.014),
                     (0.0, y - 0.014, rail_z), surface=METAL, bevel=0.0020))
    return parts, axis_z + 0.0196


def optic_scope(ctx: dict, spec: dict, rail_z: float, y: float
                ) -> Tuple[List[bpy.types.Object], float]:
    """Three turret riflescope: tapered tube, bell objective, 30 mm rings."""
    parts: List[bpy.types.Object] = []
    axis_z = rail_z + 0.038
    parts.append(tube("scope_tube", [
        (y + 0.062, 0.0150), (y + 0.056, 0.0142), (y - 0.020, 0.0142),
        (y - 0.034, 0.0150), (y - 0.070, 0.0212), (y - 0.082, 0.0220),
        (y - 0.090, 0.0210),
    ], axis_z, surface=OLIVE, segments=26))
    parts.append(band("scope_eyepiece", y + 0.058, 0.0168, 0.012, axis_z,
                      surface=METAL, segments=24))
    parts.append(band("scope_bell", y - 0.086, 0.0230, 0.010, axis_z,
                      surface=METAL, segments=24))
    parts.append(disc("scope_lens", (0.0, y - 0.0915, axis_z), 0.0208, 0.0032,
                      axis="Y", surface=GLASS))
    parts.append(disc("scope_eye", (0.0, y + 0.0635, axis_z), 0.0140, 0.0032,
                      axis="Y", surface=GLASS))
    parts.append(disc("elevation_turret", (0.0, y - 0.006, axis_z + 0.020), 0.0080,
                      0.024, axis="Z", surface=METAL))
    parts.append(disc("windage_turret", (0.0190, y - 0.006, axis_z), 0.0078, 0.014,
                      axis="X", surface=METAL))
    parts.append(disc("parallax_turret", (-0.0190, y - 0.006, axis_z), 0.0074, 0.014,
                      axis="X", surface=METAL))
    for ring_y in (y - 0.048, y + 0.034):
        parts.append(band("scope_ring", ring_y, 0.0176, 0.016, axis_z,
                          surface=METAL, segments=22))
        parts.append(box("scope_mount", (0.0300, 0.018, 0.030),
                         (0.0, ring_y, rail_z + 0.014), surface=METAL,
                         bevel=0.0020))
    return parts, axis_z + 0.0220


def iron_sights_ak(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], float]:
    """AK front sight: a barrel band, a tower with two swept ears and a hooded post."""
    bore = ctx["bore"]
    y = ctx["front"] - spec["barrel_len"] + 0.058
    parts: List[bpy.types.Object] = [
        band("sight_band", y, spec["barrel_r"] * 1.45, 0.026, bore,
             surface=METAL, segments=24),
        prism("sight_base", [(y - 0.013, bore + 0.008), (y - 0.013, bore + 0.026),
                             (y + 0.013, bore + 0.026), (y + 0.013, bore + 0.008)],
              0.0125, surface=METAL, bevel_width=0.0018, x_centre=0.0),
    ]
    for side in (-1.0, 1.0):
        parts.append(prism("sight_ear",
                           [(y - 0.013, bore + 0.020), (y - 0.013, bore + 0.044),
                            (y - 0.004, bore + 0.050), (y + 0.011, bore + 0.043),
                            (y + 0.011, bore + 0.020)],
                           0.0042, surface=METAL, bevel_width=0.0012,
                           x_centre=side * 0.0108))
    parts.append(disc("front_post", (0.0, y + 0.002, bore + 0.030), 0.0022, 0.036,
                      axis="Z", surface=BARE, segments=12))
    parts.append(torus("sight_hood", (0.0, y + 0.002, bore + 0.046), 0.0125, 0.0026,
                       surface=METAL, segments=22, axis="Y"))
    return parts, bore + 0.050


def iron_sights_ar(ctx: dict, spec: dict) -> Tuple[List[bpy.types.Object], float]:
    """Folding backup sights: a protected post in front, an aperture at the rear."""
    bore = ctx["bore"]
    front_y = ctx["front"] - 0.030
    parts: List[bpy.types.Object] = [
        box("buis_front", (0.0180, 0.014, 0.028), (0.0, front_y, bore + 0.030),
            surface=METAL, bevel=0.0016),
        disc("buis_post", (0.0, front_y, bore + 0.046), 0.0018, 0.014,
             axis="Z", surface=BARE),
    ]
    return parts, bore + 0.052


def bipod(ctx: dict, spec: dict, y: float) -> List[bpy.types.Object]:
    """Folded Harris style bipod under the fore-end: mount, two legs, feet.

    The legs were built as tubes at their absolute position and then rotated about the
    object origin, which is the weapon's origin, not the mount - so both legs and their
    feet ended up somewhere else entirely, and what was left on the sheet was a floating
    stump and two thin tails.  Each leg is now built at the origin, swung there, and
    translated onto the mount; the feet are placed on the real tip of the leg.
    """
    bore = ctx["bore"]
    mount_z = bore - 0.042
    parts: List[bpy.types.Object] = [
        box("bipod_mount", (0.026, 0.034, 0.016), (0.0, y, bore - 0.030),
            surface=METAL, bevel=0.0022),
        box("bipod_block", (0.020, 0.022, 0.014), (0.0, y - 0.004, mount_z),
            surface=METAL, bevel=0.0016),
    ]
    leg_length = 0.086
    for side in (-1.0, 1.0):
        # 5.6 mm, not 4.4: at 4.4 the folded legs read as loose wire rather than as the
        # tubes a Harris bipod is actually made of.
        leg = tube("bipod_leg", [(0.0, 0.0056), (leg_length, 0.0044)],
                   0.0, surface=METAL, segments=14)
        leg.data.transform(mathutils.Matrix.Rotation(
            math.radians(-72.0), 4, "X"))
        leg.data.transform(mathutils.Matrix.Rotation(
            math.radians(-side * 7.0), 4, "Y"))
        leg.data.transform(mathutils.Matrix.Translation(
            (side * 0.012, y - 0.004, mount_z)))
        leg.data.update()
        parts.append(leg)
        # the foot sits on the leg's actual end, so the two can never drift apart
        tip = max((leg.matrix_world @ vertex.co for vertex in leg.data.vertices),
                  key=lambda point: point.z)
        parts.append(disc("bipod_foot", (tip.x, tip.y, tip.z),
                          0.0062, 0.016, axis="Z", surface=RUBBER))
    return parts


# --------------------------------------------------------------------------
# Specs
# --------------------------------------------------------------------------
SPECS: Dict[str, dict] = {
    "vk74": dict(kind="ak", receiver_len=0.245, receiver_h=0.070, half_w=0.0270,
                 barrel_r=0.0095, barrel_len=0.330, furniture=WOOD, stock="ak",
                 stock_len=0.235, handguard="wood", handguard_len=0.185,
                 mag=dict(length=0.185, width=0.032, depth=0.074, curve=22.0,
                          surface=METAL),
                 muzzle="ak_brake", optic=None, bore_drop=0.30, sights="ak",
                 barrel_surface=METAL),
    "tr30": dict(kind="ar", receiver_len=0.250, receiver_h=0.078, half_w=0.0245,
                 barrel_r=0.0092, barrel_len=0.285, furniture=POLYMER, stock="ar",
                 stock_len=0.205, handguard="mlok", handguard_len=0.250,
                 handguard_scale=2.55,
                 mag=dict(length=0.175, width=0.030, depth=0.072, curve=7.0,
                          surface=METAL),
                 muzzle="birdcage", optic="reddot", bore_drop=0.32, sights="ar"),
    "vs9": dict(kind="smg", receiver_len=0.205, receiver_h=0.072, half_w=0.0240,
                barrel_r=0.0088, barrel_len=0.145, furniture=POLYMER, stock="wire",
                stock_len=0.145, handguard="mlok", handguard_len=0.115,
                handguard_scale=2.45,
                mag=dict(length=0.165, width=0.030, depth=0.062, curve=9.0,
                         surface=METAL),
                muzzle="cone", optic=None, bore_drop=0.28, sights="ar"),
    "dmr14": dict(kind="dmr", receiver_len=0.260, receiver_h=0.080, half_w=0.0250,
                  barrel_r=0.0104, barrel_len=0.420, furniture=OLIVE, stock="ar",
                  stock_len=0.230, handguard="mlok", handguard_len=0.290,
                  handguard_scale=2.65,
                  mag=dict(length=0.170, width=0.030, depth=0.076, curve=6.0,
                           surface=METAL),
                  muzzle="birdcage", optic="prism", bore_drop=0.32, sights="ar",
                  barrel_surface=OLIVE),
    "sr338": dict(kind="sniper", receiver_len=0.285, receiver_h=0.082, half_w=0.0255,
                  barrel_r=0.0122, barrel_len=0.510, furniture=OLIVE, stock="chassis",
                  stock_len=0.255, handguard="mlok", handguard_len=0.320,
                  handguard_scale=2.45,
                  mag=dict(length=0.150, width=0.032, depth=0.082, curve=4.0,
                           surface=METAL),
                  muzzle="sniper", optic="scope", bore_drop=0.30, sights="ar",
                  bipod=True, barrel_surface=OLIVE),
    "k870": dict(kind="shotgun", receiver_len=0.215, receiver_h=0.080, half_w=0.0262,
                 barrel_r=0.0122, barrel_len=0.470, furniture=WOOD, stock="shotgun",
                 stock_len=0.265, handguard="pump", handguard_len=0.180,
                 mag=None, muzzle="none", optic=None, bore_drop=0.26, sights="bead"),
    "p9": dict(kind="pistol", receiver_len=0.170, receiver_h=0.038, half_w=0.0138,
               barrel_r=0.0078, barrel_len=0.028, furniture=POLYMER, stock="none",
               stock_len=0.0, handguard="none",
               mag=dict(length=0.072, width=0.024, depth=0.036, curve=4.0,
                        surface=METAL),
               muzzle="none", optic=None, bore_drop=0.0, sights="pistol"),
}


# --------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------
def build_long_gun(spec: dict):
    ctx = layout(spec)
    kind = spec["kind"]
    parts: List[bpy.types.Object] = []
    sight_top = ctx["top"] + 0.020

    if kind == "ak":
        parts.extend(receiver_ak(ctx, spec))
    elif kind in ("ar", "dmr"):
        parts.extend(receiver_ar(ctx, spec))
    elif kind == "smg":
        parts.extend(receiver_smg(ctx, spec))
    elif kind == "sniper":
        parts.extend(receiver_bolt(ctx, spec))
    else:
        parts.extend(receiver_shotgun(ctx, spec))

    barrel_parts, muzzle_y = barrel(ctx, spec)
    parts.extend(barrel_parts)
    muzzle_parts, muzzle_len = muzzle(spec["muzzle"], muzzle_y, spec["barrel_r"],
                                      ctx["bore"], surface=spec.get("barrel_surface", METAL))
    parts.extend(muzzle_parts)
    forward_reach = muzzle_y - muzzle_len

    handguard = spec.get("handguard", "none")
    if handguard == "wood":
        parts.extend(handguard_wood(ctx, spec))
    elif handguard == "mlok":
        parts.extend(handguard_mlok(ctx, spec, spec["furniture"]))
    elif handguard == "pump":
        parts.extend(handguard_pump(ctx, spec))

    if kind == "ak":
        handguard_end = ctx["front"] + 0.006 - spec["handguard_len"]
        gas_y = handguard_end - 0.030
        parts.append(loft(round_rect(ctx["hw"] * 0.62, 0.017, 0.006),
                          [(gas_y + 0.018, 1.0, 1.0, 0.0, ctx["bore"] + 0.004),
                           (gas_y + 0.008, 1.05, 1.05, 0.0, ctx["bore"] + 0.004),
                           (gas_y - 0.014, 1.05, 1.05, 0.0, ctx["bore"] + 0.004),
                           (gas_y - 0.020, 0.92, 0.92, 0.0, ctx["bore"] + 0.004)],
                          surface=METAL, name="gas_block"))
        parts.append(tube("gas_tube", [(ctx["front"] - 0.002, 0.0095),
                                       (gas_y + 0.012, 0.0095)],
                          ctx["bore"] + 0.026, surface=METAL, bore=0.0072,
                          segments=20))
        parts.append(box("sling_loop_base", (0.012, 0.016, 0.010),
                         (0.0, gas_y - 0.002, ctx["bore"] - 0.024),
                         surface=METAL, bevel=0.0016))
        sight_parts, front_top = iron_sights_ak(ctx, spec)
        parts.extend(sight_parts)
        sight_top = max(sight_top, front_top)

    if kind == "shotgun":
        rib_start = ctx["front"] - 0.010
        parts.append(box("vent_rib", (0.012, spec["barrel_len"] * 0.94, 0.0055),
                         (0.0, rib_start - spec["barrel_len"] * 0.47,
                          ctx["bore"] + spec["barrel_r"] + 0.004),
                         surface=METAL, bevel=0.0010))
        parts.append(tube("magazine_tube", [(rib_start - 0.020, spec["barrel_r"] * 0.82),
                                            (rib_start - spec["barrel_len"] * 0.88,
                                             spec["barrel_r"] * 0.82)],
                          ctx["bore"] - spec["barrel_r"] * 2.0, surface=METAL,
                          segments=20))
        parts.append(band("tube_cap", rib_start - spec["barrel_len"] * 0.88,
                          spec["barrel_r"] * 0.92, 0.010,
                          ctx["bore"] - spec["barrel_r"] * 2.0, surface=METAL,
                          segments=20))
        parts.append(band("barrel_band", rib_start - spec["barrel_len"] * 0.86,
                          0.020, 0.014, ctx["bore"] - spec["barrel_r"] * 0.9,
                          surface=METAL, segments=20))
        parts.append(disc("bead", (0.0, forward_reach + 0.020,
                                   ctx["bore"] + spec["barrel_r"] + 0.006),
                          0.0024, 0.008, axis="Z", surface=BARE))
        sight_top = ctx["bore"] + spec["barrel_r"] + 0.014

    if kind == "smg":
        parts.append(rail("smg_under_rail", ctx["front"] - 0.070,
                          ctx["front"] - 0.100, ctx["bore"] - ctx["hw"] * 0.92,
                          0.017, 0.0050))

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

    # the grip is moulded plastic on every weapon in the set, including the
    # walnut ones: a timber grip reads as part of the stock
    if kind == "sniper":
        parts.extend(grip(ctx, spec, POLYMER, length=0.098, rake=14.0))
    elif kind != "shotgun":
        parts.extend(grip(ctx, spec, POLYMER))
    parts.extend(trigger_group(ctx, spec))

    sight_top = max(sight_top, _sight_top_for(kind, ctx, spec))
    optic = spec.get("optic")
    if optic == "reddot":
        optic_parts, optic_top = optic_reddot(ctx, spec, sight_top - 0.018,
                                              ctx["rear"] - 0.090)
        parts.extend(optic_parts)
        sight_top = optic_top
    elif optic == "prism":
        optic_parts, optic_top = optic_prism(ctx, spec, sight_top - 0.018,
                                             ctx["rear"] - 0.100)
        parts.extend(optic_parts)
        sight_top = optic_top
    elif optic == "scope":
        optic_parts, optic_top = optic_scope(ctx, spec, sight_top - 0.018,
                                             ctx["rear"] - 0.120)
        parts.extend(optic_parts)
        sight_top = optic_top

    if spec.get("bipod"):
        parts.extend(bipod(ctx, spec, ctx["front"] + 0.010 - spec["handguard_len"] * 0.55))

    parts.extend(swivel("front_swivel", ctx["front"] - 0.058,
                        ctx["bore"] - ctx["hw"] * 1.02, ctx["hw"] * 0.62))

    mag_parts: List[bpy.types.Object] = []
    mag_pivot = None
    if spec.get("mag"):
        mag_parts, mag_pivot = magazine_parts(spec, ctx)
    return parts, mag_parts, sight_top, ctx["grip_y"], ctx["bottom"], mag_pivot


def _sight_top_for(kind: str, ctx: dict, spec: dict) -> float:
    """Height of the rear sight or rail, as built by the receiver."""
    if kind in ("ar", "dmr"):
        return ctx["top"] + 0.008
    if kind == "ak":
        return ctx["top"] + 0.022
    if kind == "smg":
        return ctx["top"] + 0.008
    return ctx["top"] + 0.010


# --------------------------------------------------------------------------
# Sidearm
# --------------------------------------------------------------------------
def build_pistol(spec: dict):
    """Sidearm: polymer frame, steel slide, exposed barrel hood, night sights."""
    receiver_len = spec["receiver_len"]
    half_w = spec["half_w"]
    height = spec["receiver_h"]
    grip_y = 0.026
    muzzle_y = -receiver_len * 0.62
    slide_start = receiver_len * 0.40

    # The slide is the whole silhouette of a sidearm at a glance, and a straight prism
    # with a 3.5 mm chamfer read as a brick.  A 6 mm round on the top corners, a longer
    # nose taper and a real crowned bore at the front are what make it read as a slide.
    parts: List[bpy.types.Object] = [
        loft(round_rect(half_w, height * 0.5, 0.0060, steps=3), [
            (muzzle_y - 0.007, 0.74, 0.78, 0.0, 0.0),
            (muzzle_y - 0.001, 0.88, 0.92, 0.0, 0.0),
            (muzzle_y + 0.010, 0.99, 1.00, 0.0, 0.0),
            (slide_start - 0.020, 1.00, 1.00, 0.0, 0.0),
            (slide_start, 0.94, 0.96, 0.0, 0.0),
        ], surface=METAL, name="slide"),
    ]
    # muzzle: a bored crown inside the slide face, not a painted dot
    parts.append(tube("pistol_crown", [(muzzle_y - 0.009, 0.0082),
                                       (muzzle_y + 0.002, 0.0082)],
                      0.0, surface=BARE, bore=0.0046, segments=20))
    slide = parts[0]
    # ejection port: a real pocket with the barrel hood visible inside
    port = box_cutter("port", (half_w * 2.2, 0.026, 0.010),
                      (0.0, muzzle_y + 0.062, height * 0.22), bevel=0.0022)
    cut(slide, [port])
    parts.append(box("port_shadow", (half_w * 1.4, 0.024, 0.009),
                     (0.0, muzzle_y + 0.062, height * 0.22), surface=RUBBER))
    parts.append(loft(chamfer_rect(half_w * 0.66, height * 0.20, 0.0022), [
        (muzzle_y + 0.030, 1.0, 1.0, 0.0, height * 0.24),
        (muzzle_y + 0.086, 1.0, 1.0, 0.0, height * 0.24),
    ], surface=BARE, name="barrel_hood"))
    # slide serrations front and rear
    for base_y in (muzzle_y + 0.020, slide_start - 0.048):
        for index in range(5):
            y = base_y + index * 0.0072
            for side in (-1.0, 1.0):
                parts.append(box("serration", (0.0016, 0.0034, height * 0.72),
                                 (side * (half_w - 0.0002), y, 0.0),
                                 surface=BARE))
    # frame, dust cover, accessory rail
    parts.append(loft(chamfer_rect(half_w * 0.94, height * 0.42, 0.004), [
        (muzzle_y + 0.026, 0.90, 0.86, 0.0, -height * 0.55),
        (muzzle_y + 0.052, 1.00, 1.00, 0.0, -height * 0.55),
        (slide_start, 1.00, 1.00, 0.0, -height * 0.55),
        (receiver_len * 0.52, 0.96, 0.94, 0.0, -height * 0.55),
    ], surface=POLYMER, name="frame"))
    parts.append(rail("pistol_rail", muzzle_y + 0.044, muzzle_y + 0.092,
                      -height * 0.55 - height * 0.42 - 0.0024, 0.0165, 0.0046))
    parts.append(box("trigger_guard_front", (half_w * 1.7, 0.008, 0.020),
                     (0.0, -0.010, -height * 1.02), surface=POLYMER,
                     bevel=0.0018))
    parts.append(sweep_bar("trigger_guard", [
        (-0.028, -height * 0.62), (-0.028, -height * 1.05),
        (0.016, -height * 1.05), (0.020, -height * 0.66),
    ], half_w * 0.62, 0.0028, surface=POLYMER))
    # The blade used to be a 3 mm sliver tucked under the frame; a sidearm's trigger is
    # the thing the eye checks first, so it is 11 mm across now, with the blade safety
    # lever in the middle of it - the detail that makes a striker-fired pistol read.
    blade = loft(round_rect(0.0055, 0.0032, 0.0024, steps=3), [
        (-height * 0.66, 1.00, 1.00, 0.0, -0.004),
        (-height * 0.82, 0.96, 1.00, 0.0, -0.005),
        (-height * 0.98, 0.88, 0.94, 0.0, -0.008),
        (-height * 1.08, 0.66, 0.80, 0.0, -0.011),
    ], surface=BARE, name="trigger", axis="Z")
    parts.append(blade)
    parts.append(loft(round_rect(0.0018, 0.0016, 0.0008, steps=3), [
        (-height * 0.70, 1.0, 1.0, 0.0, -0.0085),
        (-height * 0.96, 0.9, 0.9, 0.0, -0.0125),
    ], surface=METAL, name="trigger_safety", axis="Z"))
    for side in (-1.0, 1.0):
        parts.append(disc("trigger_pin", (side * half_w * 0.97, -0.004,
                                          -height * 0.60),
                          0.0024, 0.0014, surface=BARE))
    # grip: raked, stippled, with a backstrap
    grip_parts = [loft(round_rect(half_w * 0.94, 0.0175, 0.008, steps=3), [
        (-0.028, 0.96, 0.92, 0.0, grip_y),
        (-0.040, 1.00, 1.00, 0.0, grip_y - 0.002),
        (-0.086, 1.00, 1.00, 0.0, grip_y - 0.012),
        (-0.098, 0.94, 0.94, 0.0, grip_y - 0.016),
    ], surface=POLYMER, name="grip", axis="Z")]
    for index in range(3):
        grip_parts.append(tube("grip_groove",
                               [(-half_w * 0.86, 0.0032), (half_w * 0.86, 0.0032)],
                               -0.052 - index * 0.016, surface=POLYMER,
                               segments=10, x=0.0))
    for side in (-1.0, 1.0):
        grip_parts.append(box("grip_panel", (0.0018, 0.026, 0.044),
                              (side * half_w * 0.92, grip_y - 0.006, -0.066),
                              surface=RUBBER))
    merged = art.join(grip_parts, "grip_shell")
    parts.append(merged)
    parts.append(disc("mag_release", (-half_w * 0.9, -0.006, -0.006), 0.0030, 0.0030))
    parts.append(box("slide_stop", (half_w * 2.02, 0.024, 0.0045),
                     (0.0, -0.006, -height * 0.10), surface=BARE))
    # sights
    parts.append(box("rear_sight", (0.0140, 0.008, 0.0055),
                     (0.0, slide_start - 0.014, height * 0.52), surface=BARE,
                     bevel=0.0010))
    parts.append(box("front_sight", (0.0042, 0.006, 0.0055),
                     (0.0, muzzle_y + 0.012, height * 0.52), surface=BARE,
                     bevel=0.0008))
    sight_top = height * 0.55

    mag_parts, mag_pivot = magazine_parts(
        spec, dict(mag_y=grip_y - 0.004, bottom=-0.036, hw=half_w))
    return parts, mag_parts, sight_top, grip_y, -0.028, mag_pivot


def build_weapon(weapon_id: str):
    """Assemble one weapon and re-origin it into the shooting hand."""
    spec = SPECS[weapon_id]
    if spec["kind"] == "pistol":
        parts, mag_parts, sight_top, grip_y, grip_top, mag_pivot = build_pistol(spec)
    else:
        parts, mag_parts, sight_top, grip_y, grip_top, mag_pivot = build_long_gun(spec)

    # The blade is pulled out of the joined body so it can be its own node: a trigger
    # that never moves is the detail that makes a first person weapon feel like a prop.
    trigger_parts = [part for part in parts if part.name == "trigger"]
    parts = [part for part in parts if part.name != "trigger"]

    weapon = finish(parts, weapon_id, bevel_width=spec.get("bevel", 0.0014))
    shift = mathutils.Matrix.Translation((0.0, -grip_y, -grip_top))
    sight_top -= grip_top

    # The hands are built before the weapon is re-origined: the layout table is authored
    # in the weapon's own space, so measuring the fore-end against a mesh that has
    # already been shifted put the support hand ~95 mm off along the barrel.  Both are
    # moved by the same shift afterwards.
    import build_hands
    hands = build_hands.add_hands(spec, layout(spec), weapon=weapon)

    weapon.data.transform(shift)
    weapon.data.update()

    magazine_obj = None
    if mag_parts and mag_pivot is not None:
        magazine_obj = finish(mag_parts, "Magazine", bevel_width=0.0008,
                              unwrap_margin=0.004)
        magazine_obj.data.transform(shift)
        pivot = mathutils.Vector((0.0, mag_pivot[0] - grip_y, mag_pivot[1] - grip_top))
        magazine_obj.data.transform(mathutils.Matrix.Translation(-pivot))
        magazine_obj.data.update()
        magazine_obj.location = pivot
        bpy.context.view_layer.update()

    # The hands are authored in this weapon's space (see build_hands.py) and shipped
    # inside the same FBX as separate nodes, so the viewmodel can find and animate them
    # without an anchor table.
    for hand in hands:
        hand.data.transform(shift)
        hand.data.update()

    # Authored positions are approximations; the surface is the truth.  Each hand is
    # snapped until its palm touches the weapon, and a hand that is out by more than a
    # couple of centimetres fails the build.
    for hand in hands:
        gap = build_hands.seat_on_weapon(hand, weapon)
        print(f"[weapons] {weapon_id}: {hand.name} seated, was {gap * 1000:.1f} mm off")

    trigger_obj = None
    if trigger_parts:
        raw = trigger_parts[0]
        trigger_obj = finish([raw], "Trigger", bevel_width=0.0006,
                             unwrap_margin=0.004)
        trigger_obj.data.transform(shift)
        # pivot on the blade's top edge, where the trigger pin is
        pivot = mathutils.Vector((0.0, 0.0, 0.0))
        for vertex in trigger_obj.data.vertices:
            pivot.z = max(pivot.z, vertex.co.z)
        pivot.y = 0.0
        trigger_obj.data.transform(mathutils.Matrix.Translation(-pivot))
        trigger_obj.data.update()
        trigger_obj.location = pivot
        bpy.context.view_layer.update()

    bounds = world_bounds([weapon] + ([magazine_obj] if magazine_obj is not None else [])
                          + ([trigger_obj] if trigger_obj is not None else []) + hands)
    return weapon, magazine_obj, dict(sightHeightM=round(sight_top, 4),
                                      lengthM=round(bounds[1][1] - bounds[0][1], 3),
                                      hands=hands, trigger=trigger_obj)


# --------------------------------------------------------------------------
# Finishing: join, bevel, smooth, unwrap
# --------------------------------------------------------------------------
def smooth_shade(obj: bpy.types.Object, angle_deg: float = 33.0) -> None:
    """Smooth shading with split normals, so fillets catch light and flats stay flat."""
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    modifier = obj.modifiers.new("KordonSmooth", "EDGE_SPLIT")
    modifier.split_angle = math.radians(angle_deg)
    art.apply_modifiers(obj)


def finish(parts: Sequence[bpy.types.Object], name: str, bevel_width: float = 0.0014,
           segments: int = 2, unwrap_margin: float = 0.002) -> bpy.types.Object:
    merged = art.join([part for part in parts if part is not None], name)
    if art.DETAIL:
        art.bevel(merged, width=bevel_width, segments=segments, angle_deg=34.0)
        smooth_shade(merged)
        art.unwrap(merged, island_margin=unwrap_margin)
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
        tube("body", [(0.0, radius * 0.62), (0.012, radius),
                      (height * 0.55, radius), (height * 0.90, radius * 0.94),
                      (height, radius * 0.70)],
             0.0, surface=spec["surface"], segments=22),
        tube("cap", [(height - 0.006, radius * 0.54), (height + 0.010, radius * 0.50)],
             0.0, surface=METAL, segments=18),
        tube("fuze", [(height + 0.008, radius * 0.30), (height + 0.028, radius * 0.34)],
             0.0, surface=POLYMER, segments=14),
    ]
    # The body, cap and fuze are swept along +Y and stood up along +Z.  The rotation was
    # -90 degrees, which maps +Y to -Z: the body hung *below* the origin while every part
    # authored afterwards (ribs, safety lever, pin ring, vents) was placed in +Z.  The
    # exported grenade was therefore inverted and split in two, 160 mm apart - measured
    # height 257 mm for a 98 mm body.  +90 degrees puts +Y on +Z, which is the frame the
    # rest of the assembly was written in.
    for part in parts:
        part.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
        part.data.update()
    if spec["sleeve"]:
        for index in range(spec["ribs"]):
            angle = math.radians(360.0 * index / spec["ribs"])
            parts.append(box("fragment_rib",
                             (radius * 0.34, 0.010, height * 0.62),
                             (math.cos(angle) * radius * 0.92,
                              math.sin(angle) * radius * 0.92, height * 0.48),
                             surface=METAL, rot=(0.0, 0.0, math.degrees(angle))))
    # The safety lever is the last part still authored in the swept frame, where the
    # first coordinate ran along the body and the second radially.  Everything around it
    # had already been placed standing up, so the lever lay flat and stuck 92 mm out of
    # the side of a 60 mm grenade.  Written in the standing frame: radial in Y, height
    # in Z, a tapered strip down the side.
    parts.append(prism("safety_lever", [
        (radius * 1.02, height * 0.10), (radius * 0.70, height * 0.94),
        (radius * 0.80, height * 0.97), (radius * 1.13, height * 0.12),
    ], 0.0036, surface=BARE, bevel_width=0.0010))
    parts.append(torus("pin_ring", (0.0, 0.0, height + 0.030), radius * 0.28, 0.0030,
                       surface=BARE, segments=16, axis="Z"))
    parts.append(tube("pull_pin", [(-radius * 0.34, 0.0020), (radius * 0.34, 0.0020)],
                      height + 0.030, surface=BARE, segments=10))
    if spec["top"] == "vent":
        for index in range(4):
            angle = math.radians(90.0 * index)
            parts.append(box("vent", (0.010, 0.006, 0.020),
                             (math.cos(angle) * radius * 0.72,
                              math.sin(angle) * radius * 0.72, height + 0.004),
                             surface=RUBBER))
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
        objects = ([weapon] + ([magazine_obj] if magazine_obj is not None else [])
                   + ([meta["trigger"]] if meta.get("trigger") is not None else [])
                   + meta.get("hands", []))
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
            for hand in _meta.get("hands", []):
                hand.location.x += index * 1.45
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
