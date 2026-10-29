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
  grip, trigger guard, iron sights, sling loops, and a detachable magazine that is
  exported as its own object named ``Magazine`` so the reload animation can take it
  out of the weapon;
* surfaces come from kordon_art.SURFACES, so the weapon materials are the same
  generated, redistributable sets the map uses;
* every exported FBX is re-imported and its bounding box compared with the authored
  one - a broken export fails here instead of reaching the game.
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
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import kordon_art as art  # noqa: E402

# The barrel axis in the authored coordinate system.  Every part that wraps or
# sights the barrel is measured from this, so nothing floats.
BARREL_Z = 0.002

WEAPON_DIR = os.path.join(art.MODEL_DIR, "Resources", "Kordon", "Weapons")
MANIFEST = os.path.join(art.DATA_DIR, "Art", "weapon_manifest.json")


# --------------------------------------------------------------------------
# Build helpers
# --------------------------------------------------------------------------
def part(size: Sequence[float], center: Sequence[float], surface: str = "steel_dark",
         rotation_deg: Sequence[float] = (0.0, 0.0, 0.0), name: str = "part"):
    """One box part, optionally rotated and applied in place."""
    obj = art.box(size, center, surface=surface, name=name)
    if any(abs(r) > 1e-6 for r in rotation_deg):
        obj.rotation_euler = tuple(math.radians(r) for r in rotation_deg)
        art.apply_modifiers(obj)
        bpy.context.view_layer.update()
        # bake the rotation into the mesh so joining stays exact
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        obj.select_set(False)
    return obj


def tube(radius: float, depth: float, center: Sequence[float],
         surface: str = "steel_dark", segments: int = 16, name: str = "tube",
         radius_top: float = None):
    """Cylinder along the barrel axis (-Y): built along Z, then rotated 90 deg."""
    obj = art.cylinder(radius, radius if radius_top is None else radius_top, depth,
                       center=(0.0, 0.0, 0.0), segments=segments, surface=surface, name=name)
    obj.rotation_euler = (math.radians(90.0), 0.0, 0.0)
    bpy.context.view_layer.update()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.select_set(False)
    obj.location = tuple(center)
    bpy.context.view_layer.update()
    return obj


def pin(radius: float, length: float, center: Sequence[float], surface: str = "steel_dark",
        name: str = "pin", segments: int = 12, axis: str = "X") -> bpy.types.Object:
    """Small cylinder on the X axis: trigger pins, vent cutters, sling loops, cross pins."""
    obj = art.cylinder(radius, radius, length, center=(0.0, 0.0, 0.0), segments=segments,
                       surface=surface, name=name)
    if axis == "X":
        obj.rotation_euler = (0.0, math.radians(90.0), 0.0)
    bpy.context.view_layer.update()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.select_set(False)
    obj.location = tuple(center)
    bpy.context.view_layer.update()
    return obj


def rail(y_from: float, y_to: float, z: float, width: float = 0.022,
         surface: str = "steel_dark") -> List[bpy.types.Object]:
    """Picatinny style rail: base plus evenly spaced teeth."""
    parts = [part((width, abs(y_to - y_from), 0.005), (0.0, (y_from + y_to) * 0.5, z),
                  surface, name="rail")]
    count = max(4, int(abs(y_to - y_from) / 0.014))
    step = (y_to - y_from) / count
    for i in range(count):
        y = y_from + step * (i + 0.5)
        parts.append(part((width * 1.05, step * 0.5, 0.0035), (0.0, y, z + 0.004),
                          surface, name="rail_tooth"))
    return parts


# --------------------------------------------------------------------------
# Profile modelling
# --------------------------------------------------------------------------
# The first pass modelled every weapon from axis aligned boxes, which is why the
# renders read as blocks: a receiver has a flat top with chamfered upper corners, a
# grip is a bent oval that tapers, a stock is a skeleton, and a handguard is a tube
# with holes through it.  These helpers make those shapes possible without a
# modelling tool in the loop.


def octagon(half_x: float, half_z: float, cut: float) -> List[Tuple[float, float]]:
    """Eight point cross section: a rectangle with the corners taken off."""
    return [
        (-half_x + cut, -half_z),
        (half_x - cut, -half_z),
        (half_x, -half_z + cut),
        (half_x, half_z - cut),
        (half_x - cut, half_z),
        (-half_x + cut, half_z),
        (-half_x, half_z - cut),
        (-half_x, -half_z + cut),
    ]


def rounded(half_x: float, half_y: float, radius: float,
            steps: int = 3) -> List[Tuple[float, float]]:
    """Rounded rectangle cross section, used for grips, stocks and pistol frames."""
    points: List[Tuple[float, float]] = []
    corners = [
        (half_x - radius, half_y - radius, 0.0),
        (-half_x + radius, half_y - radius, 90.0),
        (-half_x + radius, -half_y + radius, 180.0),
        (half_x - radius, -half_y + radius, 270.0),
    ]
    for cx, cy, base in corners:
        for i in range(steps + 1):
            angle = math.radians(base + 90.0 * i / steps)
            points.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))
    return points


def loft(profile: Sequence[Tuple[float, float]], sections: Sequence[Sequence[float]],
         surface: str = "steel_dark", name: str = "loft",
         axis: str = "Y") -> bpy.types.Object:
    """Extrude a 2D profile along an axis with a scale and an offset per section.

    ``sections`` are ``(t, scale_a, scale_b, offset_a, offset_b)`` where ``t`` is the
    position along the extrusion axis.  With ``axis="Y"`` the profile lies in XZ and
    ``(a, b)`` are x and z; with ``axis="Z"`` the profile lies in XY and the axis is
    height, which is how the grips and the pistol frame are built.
    """
    count = len(profile)
    verts: List[Tuple[float, float, float]] = []
    faces: List[tuple] = []

    for section in sections:
        t, scale_a, scale_b, offset_a, offset_b = section
        for u, v in profile:
            a = u * scale_a + offset_a
            b = v * scale_b + offset_b
            verts.append((a, t, b) if axis == "Y" else (a, b, t))

    for index in range(len(sections) - 1):
        base = index * count
        nxt = (index + 1) * count
        for i in range(count):
            j = (i + 1) % count
            faces.append((base + i, base + j, nxt + j, nxt + i))

    faces.append(tuple(range(count - 1, -1, -1)))
    last = (len(sections) - 1) * count
    faces.append(tuple(last + i for i in range(count)))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    art.assign_surface(obj, surface)
    return obj


def cut_away(target: bpy.types.Object, cutters: Sequence[bpy.types.Object]) -> None:
    """Boolean difference, applied at once: this is how a port or a vent becomes a
    hole in the mesh instead of a dark sticker laid on top of it."""
    for cutter in cutters:
        modifier = target.modifiers.new(name="cut", type="BOOLEAN")
        modifier.operation = "DIFFERENCE"
        modifier.solver = "EXACT"
        modifier.object = cutter
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        bpy.data.objects.remove(cutter, do_unlink=True)


def iron_sights(rear_y: float, front_y: float, rear_z: float, front_z: float,
                height: float = 0.026) -> Tuple[List[bpy.types.Object], float]:
    """Iron sights: a notched rear on the receiver, a protected post on the gas block.

    The rear and the front sit at their own heights - they are on different parts of
    the weapon, and forcing one shared base floated the front post in the air.
    """
    parts = [
        part((0.036, 0.014, 0.008), (0.0, rear_y, rear_z + 0.004), name="rear_base"),
        part((0.008, 0.012, height), (-0.012, rear_y, rear_z + 0.008 + height * 0.5),
             name="rear_ear_l"),
        part((0.008, 0.012, height), (0.012, rear_y, rear_z + 0.008 + height * 0.5),
             name="rear_ear_r"),
        part((0.026, 0.012, 0.006), (0.0, rear_y, rear_z + 0.008 + height - 0.003),
             name="rear_bridge"),
        part((0.030, 0.014, 0.008), (0.0, front_y, front_z + 0.004), name="front_base"),
        part((0.010, 0.012, height + 0.004),
             (0.0, front_y, front_z + 0.008 + (height + 0.004) * 0.5), name="front_post"),
        part((0.005, 0.010, height), (-0.014, front_y, front_z + 0.008 + height * 0.5),
             name="front_wing_l"),
        part((0.005, 0.010, height), (0.014, front_y, front_z + 0.008 + height * 0.5),
             name="front_wing_r"),
    ]
    return parts, front_z + 0.012 + height


def magazine(top_y: float, top_z: float, length: float, width: float = 0.030,
             depth: float = 0.075, curve: float = 18.0, segments: int = 3,
             surface: str = "steel_dark") -> List[bpy.types.Object]:
    """Curved box magazine as a single lofted body.

    Built as one extrusion along Z with a per-section forward offset, so the curve
    is smooth instead of a stack of rotated boxes with visible steps and gaps.
    """
    profile = rounded(width * 0.5, depth * 0.5, min(width, depth) * 0.22, steps=2)
    sweep = math.radians(curve) * length  # total forward travel of the curve
    sections = []
    steps = max(4, segments * 2)
    for i in range(steps + 1):
        t = i / steps
        z = top_z - length * t
        y = top_y - sweep * (0.55 * t * t)  # mag curves forward as it leaves the well
        scale_y = 1.0 - 0.10 * t
        sections.append((z, 1.0, scale_y, 0.0, y))

    body = loft(profile, sections, surface=surface, name="magazine_body", axis="Z")
    parts: List[bpy.types.Object] = [body]

    # floor plate at the free end, sitting exactly on the last section
    end_z = sections[-1][0]
    end_y = sections[-1][4]
    parts.append(loft(rounded(width * 0.54, depth * 0.5, min(width, depth) * 0.2, steps=2),
                      [(end_z - 0.004, 1.02, 1.02, 0.0, end_y),
                       (end_z - 0.011, 0.96, 0.96, 0.0, end_y)],
                      surface="rubber", name="mag_floor", axis="Z"))

    # ribs and witness holes ride the same curve as the body
    for i in range(3):
        t = 0.22 + i * 0.22
        z = top_z - length * t
        y = top_y - sweep * (0.55 * t * t)
        for side in (-1.0, 1.0):
            parts.append(part((width * 1.02, 0.003, 0.005),
                              (0.0, y + side * depth * 0.5, z),
                              surface="hardened_steel", name="mag_rib"))
    return parts


def receiver_body(rear_y: float, front_y: float, receiver_h: float, half_w: float,
                  spec: dict) -> List[bpy.types.Object]:
    """The receiver shell: octagonal cross section, tapered nose, dust cover top."""
    half_z = receiver_h * 0.5
    profile = octagon(half_w, half_z, half_z * 0.42)
    length = rear_y - front_y
    body = loft(profile,
                [(rear_y, 1.0, 1.0, 0.0, 0.0),
                 (rear_y - length * 0.18, 1.0, 1.0, 0.0, 0.0),
                 (front_y + length * 0.16, 1.0, 0.96, 0.0, 0.0),
                 (front_y, 0.86, 0.80, 0.0, -0.002)],
                surface="steel_dark", name="receiver")

    parts: List[bpy.types.Object] = [
        body,
        # dust cover: a shallow dome along the top of the receiver
        loft(rounded(half_w * 0.86, 0.0065, 0.005, steps=4),
             [(rear_y - 0.012, 1.0, 1.0, 0.0, half_z + 0.002),
              (front_y + length * 0.22, 1.0, 1.0, 0.0, half_z + 0.002),
              (front_y + length * 0.16, 0.9, 0.8, 0.0, half_z + 0.001)],
             surface="steel_dark", name="dust_cover", axis="Y"),
        # rear trunnion: sits inside the octagonal profile so the back of the
        # receiver keeps its chamfer instead of turning into a plate
        loft(octagon(half_w * 0.94, receiver_h * 0.42, receiver_h * 0.16),
             [(rear_y - 0.020, 1.0, 1.0, 0.0, -0.002),
              (rear_y - 0.001, 0.97, 0.95, 0.0, -0.002)],
             surface="steel_dark", name="trunnion", axis="Y"),
    ]

    # magazine well: flares towards the bottom, angled like a rock and lock well
    well = loft(octagon(half_w * 1.0, 0.020, 0.006),
                [(-receiver_h * 0.30, 1.0, 1.0, 0.0, 0.0),
                 (-receiver_h * 0.78, 1.12, 1.0, 0.0, 0.0)],
                surface="steel_dark", name="magwell", axis="Z")
    well.location = (0.0, front_y + length * 0.26, 0.0)
    parts.append(well)
    return parts


def receiver_details(rear_y: float, receiver_len: float, receiver_h: float,
                     top_z: float, spec: dict) -> List[bpy.types.Object]:
    """Furniture that turns a box receiver into a readable weapon.

    Ejection port cover, charging handle, selector, magazine well flare, rivets
    and, on the long guns, handguard vent slots. All of it is authored geometry
    welded into the same joined mesh, so nothing here is a floating sticker.
    """
    mid_y = rear_y - receiver_len * 0.5
    half_w = 0.0215 if spec.get("pistol") else 0.0265
    parts: List[bpy.types.Object] = [
        # ejection port: a shallow recess, flush with the side, not a plate on it
        part((0.002, 0.054, 0.022), (half_w - 0.001, mid_y + 0.020, top_z - 0.010),
             surface="rubber", name="ejection_port"),
        # charging handle that the bolt animation drives visually
        part((0.012, 0.020, 0.008), (half_w + 0.005, mid_y - 0.040, top_z - 0.006),
             surface="hardened_steel", name="charging_handle"),
        # selector lever on the left side
        part((0.004, 0.038, 0.008), (-half_w - 0.001, mid_y + 0.010, top_z - 0.026),
             surface="hardened_steel", name="selector"),
    ]

    # rivets along both sides of the receiver
    for side in (-1.0, 1.0):
        for i in range(4):
            parts.append(part((0.003, 0.005, 0.005),
                              (side * half_w, mid_y - 0.060 + i * 0.040,
                               top_z - receiver_h * 0.62),
                              surface="hardened_steel", name="rivet"))

    # handguard vents, only where there is a handguard to vent
    if not spec.get("pistol") and spec["barrel_len"] > 0.10:
        # forward of the receiver on both cheeks of the handguard (the barrel
        # points at -Y, so "forward" is a smaller Y)
        vent_start = rear_y - receiver_len - 0.045
        for side in (-1.0, 1.0):
            for i in range(4):
                parts.append(part((0.004, 0.022, 0.011),
                                  (side * (spec["barrel_r"] * 2.0),
                                   vent_start - i * 0.038, top_z - 0.026),
                                  surface="rubber", name="handguard_vent"))

    return parts


def grip(center_y: float, top_z: float, length: float = 0.105,
         surface: str = "rubber") -> List[bpy.types.Object]:
    """Pistol grip: a raked, tapering oval with finger grooves and a beavertail.

    A grip is the part closest to the camera in a first person view, so a stack of
    two boxes was the most visible blockiness in the model.
    """
    profile = rounded(0.0155, 0.0215, 0.0075, steps=3)
    rake = 0.030  # how far the heel sits behind the wrist
    sections = [
        (top_z + 0.004, 1.02, 1.0, 0.0, center_y),
        (top_z - length * 0.22, 1.0, 1.02, 0.0, center_y + rake * 0.18),
        (top_z - length * 0.55, 0.95, 1.0, 0.0, center_y + rake * 0.55),
        (top_z - length * 0.85, 0.92, 0.95, 0.0, center_y + rake * 0.85),
        (top_z - length, 0.88, 0.88, 0.0, center_y + rake),
    ]
    parts: List[bpy.types.Object] = [
        loft(profile, sections, surface=surface, name="grip", axis="Z"),
        # beavertail: the web of the hand sits on this, and it hides the joint
        part((0.026, 0.030, 0.010), (0.0, center_y + 0.012, top_z + 0.006),
             surface=surface, rotation_deg=(8.0, 0.0, 0.0), name="beavertail"),
        # magazine floor plate at the heel of the grip
        part((0.030, 0.036, 0.008), (0.0, center_y + rake + 0.004, top_z - length - 0.002),
             surface="steel_dark", name="grip_floor"),
    ]

    for i in range(3):
        parts.append(part((0.034, 0.006, 0.005),
                          (0.0, center_y + 0.016 + rake * (0.25 + i * 0.22),
                           top_z - length * (0.30 + i * 0.22)),
                          surface="rubber", rotation_deg=(-18.0, 0.0, 0.0),
                          name="finger_groove"))

    # Trigger guard: a real loop around the trigger - front bar, bottom bar and a
    # rear bar that ties into the grip.  A single ring read as a decoration.
    guard_front_y = center_y - 0.052
    guard_bottom_z = top_z - length * 0.52
    guard_top_z = top_z - length * 0.10
    parts.append(part((0.010, 0.006, guard_top_z - guard_bottom_z),
                      (0.0, guard_front_y, (guard_top_z + guard_bottom_z) * 0.5),
                      surface="steel_dark", name="guard_front"))
    parts.append(part((0.010, abs(center_y - guard_front_y) + 0.006, 0.005),
                      (0.0, (guard_front_y + center_y) * 0.5, guard_bottom_z),
                      surface="steel_dark", name="guard_bottom"))
    parts.append(part((0.010, 0.006, 0.020),
                      (0.0, center_y - 0.004, top_z - length * 0.20),
                      surface="steel_dark", name="guard_rear"))
    parts.append(part((0.0055, 0.007, 0.024), (0.0, center_y - 0.024, top_z - length * 0.26),
                      surface="hardened_steel", rotation_deg=(0.0, 16.0, 0.0), name="trigger"))
    return parts


def stock(rear_y: float, top_z: float, length: float, surface: str = "rubber",
          style: str = "fixed") -> List[bpy.types.Object]:
    """Stock: a skeletonised rifle stock, a folding wire stock, or a receiver cap.

    The fixed stock is a butt plate, a cheek comb and two struts with an open gap
    between them; the earlier version was one solid box, which is what made the
    silhouette read as a plank.
    """
    parts: List[bpy.types.Object] = []
    if style == "none":
        parts.append(part((0.034, 0.014, 0.060), (0.0, rear_y + 0.007, top_z - 0.020),
                          surface="rubber", name="end_cap"))
        return parts

    if style == "folding":
        parts.append(tube(0.0055, length, (0.0, rear_y + length * 0.5, top_z - 0.012),
                          surface="steel_dark", name="stock_rail_top"))
        parts.append(tube(0.0055, length, (0.0, rear_y + length * 0.5, top_z - 0.062),
                          surface="steel_dark", name="stock_rail_bottom"))
        parts.append(loft(rounded(0.018, 0.037, 0.009, steps=3),
                          [(rear_y + length - 0.014, 1.0, 1.0, 0.0, top_z - 0.038),
                           (rear_y + length, 0.9, 0.94, 0.0, top_z - 0.038)],
                          surface="rubber", name="butt_pad", axis="Y"))
        return parts

    butt_y = rear_y + length
    # cheek comb: tapers towards the butt, sits under the sight line
    # hinge block where the stock meets the receiver
    parts.append(part((0.034, 0.018, 0.052), (0.0, rear_y + 0.010, top_z - 0.026),
                      surface="steel_dark", name="stock_hinge"))
    parts.append(loft(rounded(0.019, 0.034, 0.011, steps=3),
                      [(rear_y + 0.012, 0.92, 1.0, 0.0, top_z - 0.032),
                       (rear_y + length * 0.30, 0.96, 1.0, 0.0, top_z - 0.034),
                       (rear_y + length * 0.70, 0.94, 0.96, 0.0, top_z - 0.038),
                       (butt_y - 0.004, 0.90, 0.92, 0.0, top_z - 0.042)],
                      surface=surface, name="stock_body", axis="Y"))
    # cheek riser along the top of the stock
    parts.append(loft(rounded(0.016, 0.008, 0.004, steps=3),
                      [(rear_y + 0.020, 1.0, 1.0, 0.0, top_z - 0.008),
                       (butt_y - 0.010, 1.0, 0.9, 0.0, top_z - 0.014)],
                      surface=surface, name="stock_cheek", axis="Y"))
    # lower strut, angled up towards the receiver
    parts.append(loft(rounded(0.020, 0.050, 0.012, steps=3),
                      [(butt_y - 0.018, 1.0, 1.0, 0.0, top_z - 0.036),
                       (butt_y, 0.93, 0.94, 0.0, top_z - 0.036)],
                      surface="rubber", name="butt_pad", axis="Y"))
    # sling slot through the butt
    parts.append(part((0.010, 0.024, 0.004), (0.0, rear_y + length * 0.5, top_z - 0.062),
                      surface="steel_dark", name="sling_slot"))
    return parts


def handguard(front_y: float, length: float, top_z: float, radius: float,
              surface: str = "wood_crate") -> List[bpy.types.Object]:
    """Handguard: an octagonal tube with real ventilation holes cut through it.

    The holes are a boolean difference, so they read as holes from any angle; the
    earlier version laid dark rectangles on the surface instead.
    """
    half_x = radius * 1.30
    half_z = 0.024
    body = loft(octagon(half_x, half_z, min(half_x, half_z) * 0.5),
                [(front_y, 0.90, 0.88, 0.0, 0.0),
                 (front_y - length * 0.12, 1.0, 1.0, 0.0, 0.0),
                 (front_y - length * 0.72, 1.0, 1.0, 0.0, 0.0),
                 (front_y - length, 0.92, 0.90, 0.0, 0.0)],
                surface=surface, name="handguard")
    body.location = (0.0, 0.0, top_z)

    cutters: List[bpy.types.Object] = []
    holes = max(3, int(length / 0.042))
    for i in range(holes):
        y = front_y - length * (i + 0.6) / (holes + 0.2)
        # one cutter per station, on the X axis, drills through both cheeks
        cutters.append(pin(0.0068, half_x * 2.8, (0.0, y, top_z + 0.006),
                           surface="steel_dark", name="vent_cutter"))
    # and a slot along the top, the way a vented handguard is actually cut
    for i in range(2):
        y = front_y - length * (0.34 + i * 0.30)
        slot = part((0.010, length * 0.16, 0.030), (0.0, y, top_z + 0.026),
                    surface="steel_dark", name="slot_cutter")
        cutters.append(slot)

    if cutters:
        cut_away(body, cutters)

    parts: List[bpy.types.Object] = [body,
        part((half_x * 1.9, length * 0.92, 0.010), (0.0, front_y - length * 0.5,
             top_z - half_z - 0.002), surface="steel_dark", name="heat_shield"),
    ]
    return parts


def sling_loop(y: float, z: float) -> List[bpy.types.Object]:
    """Sling attachment loop: a small ring under the handguard / stock."""
    return [
        art.torus_band(0.014, 0.004, center=(0.0, y, z), rotation_deg=(90.0, 0.0, 0.0),
                       segments=12, surface="steel_dark", name="sling_loop"),
    ]


def finish(parts: Sequence[bpy.types.Object], name: str) -> bpy.types.Object:
    merged = art.join(list(parts), name)
    # Two bevel segments instead of one: the silhouette of a stamped receiver
    # reads as a rounded edge, not as a chamfer, at viewmodel distance.
    art.bevel(merged, width=0.0026, segments=2)
    art.apply_modifiers(merged)
    return merged


# --------------------------------------------------------------------------
# Weapon specifications
# --------------------------------------------------------------------------
# length: overall length in metres; rear/front: receiver extents; barrel: radius;
# surface: furniture material; mag: (length, depth, curve) or None; stock style.
SPECS: Dict[str, dict] = {
    "vk74": dict(length=0.945, receiver_len=0.285, receiver_h=0.072, barrel_r=0.0115,
                 barrel_len=0.360, surface="wood_crate", stock="fixed", stock_len=0.235,
                 mag=(0.185, 0.072, 26.0), sight_h=0.030, muzzle="brake", bolt=None),
    "tr30": dict(length=1.010, receiver_len=0.300, receiver_h=0.076, barrel_r=0.0110,
                 barrel_len=0.395, surface="rubber", stock="fixed", stock_len=0.250,
                 mag=(0.175, 0.074, 12.0), sight_h=0.032, muzzle="flash", bolt=None),
    "vs9": dict(length=0.620, receiver_len=0.250, receiver_h=0.068, barrel_r=0.0095,
                barrel_len=0.190, surface="rubber", stock="folding", stock_len=0.170,
                mag=(0.170, 0.038, 4.0), sight_h=0.028, muzzle="cone", bolt=None),
    "dmr14": dict(length=1.120, receiver_len=0.305, receiver_h=0.078, barrel_r=0.0120,
                  barrel_len=0.480, surface="steel_painted", stock="fixed", stock_len=0.255,
                  mag=(0.180, 0.078, 10.0), sight_h=0.034, muzzle="brake", bolt="side"),
    "sr338": dict(length=1.260, receiver_len=0.330, receiver_h=0.082, barrel_r=0.0135,
                  barrel_len=0.560, surface="rubber", stock="fixed", stock_len=0.290,
                  mag=(0.150, 0.082, 6.0), sight_h=0.036, muzzle="brake", bolt="straight"),
    "k870": dict(length=1.040, receiver_len=0.290, receiver_h=0.080, barrel_r=0.0145,
                 barrel_len=0.470, surface="wood_crate", stock="fixed", stock_len=0.245,
                 mag=None, sight_h=0.026, muzzle="bead", bolt=None, pump=True),
    "p9": dict(length=0.205, receiver_len=0.165, receiver_h=0.048, barrel_r=0.0085,
               barrel_len=0.060, surface="rubber", stock="none", stock_len=0.0,
               mag=(0.092, 0.030, 4.0), sight_h=0.024, muzzle="none", bolt=None, pistol=True),
}


def build_weapon(weapon_id: str) -> Tuple[bpy.types.Object, bpy.types.Object, dict]:
    """Assemble one weapon.

    Layout convention, used by every part below and by the viewmodel in Unity:

    * +Y is the rear of the weapon (stock), -Y is the muzzle;
    * Z is up, the receiver spans -receiver_h/2 .. +receiver_h/2;
    * the barrel axis is at BARREL_Z, which is what the handguard, the gas block
      and the front sight are all measured from.

    Every attachment is derived from its parent part here (the stock starts where
    the receiver ends, the magazine hangs off the well, the handguard wraps the
    barrel).  The previous version placed parts with hand written absolute
    offsets, which is how the stock ended up inside the receiver and the handguard
    floated above the barrel.
    """
    spec = SPECS[weapon_id]
    receiver_len = spec["receiver_len"]
    receiver_h = spec["receiver_h"]
    pistol = bool(spec.get("pistol"))
    top_z = receiver_h * 0.5
    rear_y = receiver_len * 0.55
    front_y = -receiver_len * 0.45
    half_w = 0.021 if pistol else 0.0245
    barrel_r = spec["barrel_r"]
    barrel_z = BARREL_Z
    barrel_front = front_y - spec["barrel_len"]
    body: List[bpy.types.Object] = []

    # ---------------------------------------------------------------- receiver
    body.extend(receiver_body(rear_y, front_y, receiver_h, half_w, spec))

    # ------------------------------------------------------------------- slide
    if pistol:
        # a pistol is a slide wrapped round the barrel, not a bare tube: this is the
        # part that makes the sidearm read as a handgun next to the rifle
        slide = loft(octagon(half_w * 0.94, receiver_h * 0.34, 0.006),
                     [(rear_y + 0.004, 0.98, 1.0, 0.0, top_z - 0.002),
                      (front_y - spec["barrel_len"] * 0.65, 1.0, 1.0, 0.0, top_z - 0.002),
                      (front_y - spec["barrel_len"], 0.94, 0.9, 0.0, top_z - 0.004)],
                     surface="hardened_steel", name="slide", axis="Y")
        body.append(slide)
        # slide serrations at the rear, cut as grooves rather than painted on
        for i in range(5):
            body.append(part((half_w * 1.9, 0.0035, receiver_h * 0.30),
                             (0.0, rear_y - 0.012 - i * 0.008, top_z - 0.002),
                             surface="steel_dark", name="slide_serration"))

    # ------------------------------------------------------------------ barrel
    body.append(tube(barrel_r, spec["barrel_len"] + 0.02,
                     (0.0, (front_y + barrel_front) * 0.5, barrel_z),
                     surface="hardened_steel", name="barrel"))
    body.append(tube(barrel_r * 1.35, 0.026, (0.0, front_y - 0.010, barrel_z),
                     surface="steel_dark", name="barrel_nut"))

    muzzle = spec["muzzle"]
    if muzzle == "brake":
        body.append(tube(barrel_r * 1.6, 0.052, (0.0, barrel_front + 0.020, barrel_z),
                         surface="hardened_steel", name="muzzle_brake"))
        for i in range(3):
            body.append(part((barrel_r * 3.0, 0.006, 0.008),
                             (0.0, barrel_front + 0.032 - i * 0.013, barrel_z + 0.006),
                             surface="steel_dark", name="brake_port"))
    elif muzzle == "flash":
        body.append(tube(barrel_r * 1.5, 0.042, (0.0, barrel_front + 0.016, barrel_z),
                         surface="steel_dark", name="flash_hider"))
    elif muzzle == "cone":
        body.append(tube(barrel_r * 1.7, 0.030, (0.0, barrel_front + 0.012, barrel_z),
                         surface="steel_dark", name="muzzle_cone",
                         radius_top=barrel_r * 2.1))
    elif muzzle == "bead":
        body.append(part((0.010, 0.010, 0.012), (0.0, barrel_front + 0.008, barrel_z + 0.012),
                         surface="hardened_steel", name="bead_sight"))

    # --------------------------------------------------------------- handguard
    handguard_top = barrel_z + 0.028
    if spec.get("pump"):
        length = spec["barrel_len"] * 0.44
        body.extend(handguard(front_y - 0.010, length, barrel_z, spec["barrel_r"] * 1.7,
                              surface=spec["surface"]))
        body.append(part((0.030, 0.130, 0.040),
                         (0.0, front_y - 0.010 - length * 0.55, barrel_z - 0.040),
                         surface=spec["surface"], name="pump_grip"))
        handguard_top = barrel_z + 0.026
    elif not pistol:
        length = min(spec["barrel_len"] * 0.62, 0.240)
        body.extend(handguard(front_y - 0.008, length, barrel_z, barrel_r * 1.55,
                              surface=spec["surface"]))
        # gas block on the far end of the handguard: carries the front sight
        body.append(part((half_w * 1.7, 0.026, 0.024),
                         (0.0, front_y - 0.008 - length - 0.010, barrel_z + 0.006),
                         surface="steel_dark", name="gas_block"))
        handguard_top = barrel_z + 0.030

    # -------------------------------------------------------------- stock/grip
    if spec["stock"] != "none":
        body.extend(stock(rear_y, top_z, spec["stock_len"], surface=spec["surface"],
                          style=spec["stock"]))
    else:
        body.extend(stock(rear_y, top_z, 0.0, style="none"))

    grip_top = -receiver_h * 0.22
    grip_y = rear_y - receiver_len * 0.34
    body.extend(grip(grip_y, grip_top,
                     length=0.088 if pistol else 0.115))

    # ----------------------------------------------------------------- sights
    if pistol:
        sight_parts, sight_top = iron_sights(rear_y - 0.018, front_y + 0.010,
                                             top_z + 0.012, top_z + 0.012, height=0.009)
    else:
        front_sight_y = front_y - 0.008 - min(spec["barrel_len"] * 0.62, 0.240) - 0.010
        # the gas block top is what the front sight is bolted to; deriving it from
        # the handguard height floated the post in the air
        gas_block_top = barrel_z + 0.018
        sight_parts, sight_top = iron_sights(rear_y - receiver_len * 0.30, front_sight_y,
                                             top_z + 0.006, gas_block_top,
                                             height=0.020)
    body.extend(sight_parts)
    if not pistol:
        # rail sits on the dust cover, i.e. on the receiver only - running it over
        # the gap in front of the receiver is how it ended up floating
        body.extend(rail(front_y + receiver_len * 0.10, rear_y - receiver_len * 0.30,
                         top_z + 0.006))
    else:
        body.extend(rail(front_y + 0.004, front_y + 0.026, top_z + 0.006, width=0.015))

    # ------------------------------------------------------------------ bolt
    bolt = spec.get("bolt")
    if bolt == "side":
        body.append(part((0.042, 0.010, 0.010), (0.028, rear_y - 0.055, top_z - 0.010),
                         surface="hardened_steel", name="bolt_handle"))
        body.append(tube(0.009, 0.016, (0.050, rear_y - 0.055, top_z - 0.010),
                         surface="rubber", name="bolt_knob"))
    elif bolt == "straight":
        body.append(part((0.012, 0.056, 0.012), (0.030, rear_y - 0.060, top_z - 0.006),
                         surface="hardened_steel", name="bolt_handle"))
        body.append(part((0.016, 0.016, 0.016), (0.030, rear_y - 0.088, top_z - 0.006),
                         surface="rubber", name="bolt_knob"))

    # ------------------------------------------------------------------ sling
    body.extend(sling_loop(rear_y - 0.020, -receiver_h * 0.62))
    if not pistol:
        body.extend(sling_loop(front_y - 0.030, barrel_z - 0.034))

    body.extend(receiver_details(rear_y, receiver_len, receiver_h, top_z, spec))

    weapon = finish(body, weapon_id)

    # ---------------------------------------------------------------- pivot
    # Move the geometry so the origin sits at the top of the pistol grip, i.e. in
    # the shooting hand: the viewmodel pose is a hand position, not a box centre.
    shift = mathutils.Matrix.Translation((0.0, -grip_y, -grip_top))
    weapon.data.transform(shift)
    weapon.data.update()
    sight_top -= grip_top

    # -------------------------------------------------------------- magazine
    if spec["mag"] is not None:
        mag_len, mag_depth, curve = spec["mag"]
        # A rifle hangs its magazine off the well in front of the trigger; a pistol
        # carries it inside the grip, so the two must not be placed the same way.
        well_y = (grip_y + 0.004) if pistol else front_y + receiver_len * 0.30
        mag_parts = magazine(well_y, grip_top - 0.006 if pistol else -receiver_h * 0.34,
                             mag_len,
                             width=0.036 if pistol else 0.030,
                             depth=mag_depth, curve=curve,
                             segments=3 if mag_len > 0.13 else 2)
        magazine_obj = finish(mag_parts, "Magazine")
        magazine_obj.data.transform(shift)
        magazine_obj.data.update()
    else:
        magazine_obj = None

    bounds = world_bounds([weapon] + ([magazine_obj] if magazine_obj is not None else []))
    return weapon, magazine_obj, dict(sightHeightM=round(sight_top, 4),
                                      lengthM=round(bounds[1][1] - bounds[0][1], 3))


# --------------------------------------------------------------------------
# Thrown equipment
# --------------------------------------------------------------------------
# Grenades are shown in flight and in the hand, so they get real models too:
# a notched fragmentation body, a smoke canister and a flash canister.
GRENADES: Dict[str, dict] = {
    "grenade_frag": dict(radius=0.030, height=0.098, ribs=9, surface="steel_painted",
                         lever=True, top="fuze"),
    "grenade_smoke": dict(radius=0.033, height=0.135, ribs=3, surface="steel_dark",
                          lever=True, top="vent"),
    "grenade_flash": dict(radius=0.029, height=0.108, ribs=4, surface="rubber",
                          lever=True, top="holes"),
}


def build_grenade(grenade_id: str) -> Tuple[bpy.types.Object, dict]:
    spec = GRENADES[grenade_id]
    radius = spec["radius"]
    height = spec["height"]
    parts: List[bpy.types.Object] = [
        art.cylinder(radius, radius, height, center=(0.0, 0.0, height * 0.5),
                     segments=16, surface=spec["surface"], name="body"),
        art.cylinder(radius * 0.72, radius * 0.72, 0.022,
                     center=(0.0, 0.0, height + 0.008), segments=14,
                     surface="steel_dark", name="top_cap"),
        art.cylinder(radius * 0.35, radius * 0.35, 0.020,
                     center=(0.0, 0.0, height + 0.026), segments=10,
                     surface="rubber", name="fuze_head"),
    ]

    # ribs around the body
    for i in range(spec["ribs"]):
        angle = math.radians(360.0 * i / spec["ribs"])
        parts.append(part((radius * 0.42, 0.010, height * 0.62),
                          (math.cos(angle) * radius * 0.92, math.sin(angle) * radius * 0.92,
                           height * 0.5),
                          surface="steel_dark", rotation_deg=(0.0, 0.0, math.degrees(angle)),
                          name="rib"))

    # lever / spoon down one side
    parts.append(part((0.010, radius * 1.15, height * 0.66),
                      (radius * 0.95, 0.0, height * 0.46), surface="sheet_metal",
                      rotation_deg=(0.0, 0.0, 0.0), name="lever"))
    parts.append(art.torus_band(radius * 0.68, 0.004, center=(0.0, 0.0, height + 0.030),
                                rotation_deg=(0.0, 0.0, 0.0), segments=12,
                                surface="sheet_metal", name="pin_ring"))

    if spec["top"] == "vent":
        for i in range(4):
            angle = math.radians(90.0 * i)
            parts.append(part((0.008, 0.008, 0.028),
                              (math.cos(angle) * radius * 0.8, math.sin(angle) * radius * 0.8,
                               height + 0.004), surface="steel_dark", name="vent"))
    elif spec["top"] == "holes":
        for i in range(6):
            angle = math.radians(60.0 * i)
            parts.append(part((0.006, 0.006, 0.010),
                              (math.cos(angle) * radius * 0.9, math.sin(angle) * radius * 0.9,
                               height * 0.82), surface="steel_dark", name="hole"))

    body = finish(parts, grenade_id)
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
            for i in range(3):
                lo[i] = min(lo[i], world[i])
                hi[i] = max(hi[i], world[i])
    return tuple(lo), tuple(hi)


def verify_export(path: str, expected: Tuple[Tuple[float, ...], Tuple[float, ...]]) -> int:
    """Re-import the FBX into a clean scene and compare the bounding box."""
    art.reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o.type == "MESH"]
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
        print(f"  {weapon_id:6s} {tris:5d} tris  {size / 1024:6.1f} KB  "
              f"{manifest[-1]['sizeM']}  sight {meta['sightHeightM']:.3f} m")

    # Rebuild the complete scene once and save it as the authoring source.
    if not args.only:
        art.reset_scene()
        y_cursor = 0.0
        for weapon_id in SPECS:
            weapon, magazine_obj, _ = build_weapon(weapon_id)
            if magazine_obj is not None:
                magazine_obj.parent = weapon
            weapon.location = (0.0, y_cursor, 0.0)
            bpy.context.view_layer.update()
            y_cursor -= 1.45
        art.save_blend(os.path.join(art.BLEND_DIR, "kordon_weapons.blend"))

    # Thrown equipment shares the folder: the projectile and the hand both use it.
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
        print(f"  {grenade_id:13s} {tris:5d} tris  {size / 1024:6.1f} KB  "
              f"{manifest[-1]['sizeM']}")

    existing = []
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
                   "Dimensions are metres; sightHeightM is the top of the iron sights "
                   "above the grip, which is what the viewmodel uses to align the aim pose.",
        "weapons": [by_id[key] for key in sorted(by_id.keys())],
    }
    art.write_json(MANIFEST, payload)

    print(f"weapons: {len(manifest)} files, {total_tris} triangles, "
          f"{time.time() - started:.1f}s")
    print(f"manifest: {MANIFEST}")


if __name__ == "__main__":
    main()
