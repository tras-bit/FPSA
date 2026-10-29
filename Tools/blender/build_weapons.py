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


def rail(y_from: float, y_to: float, z: float, width: float = 0.022,
         surface: str = "steel_dark") -> List[bpy.types.Object]:
    """Picatinny style rail: base plus evenly spaced teeth."""
    parts = [part((width, abs(y_to - y_from), 0.008), (0.0, (y_from + y_to) * 0.5, z),
                  surface, name="rail")]
    count = max(3, int(abs(y_to - y_from) / 0.018))
    step = (y_to - y_from) / count
    for i in range(count):
        y = y_from + step * (i + 0.5)
        parts.append(part((width * 1.12, step * 0.45, 0.006), (0.0, y, z + 0.006),
                          surface, name="rail_tooth"))
    return parts


def iron_sights(rear_y: float, front_y: float, base_z: float,
                height: float = 0.032) -> Tuple[List[bpy.types.Object], float]:
    """Iron sights: rear notch blocks and a protected front post. Returns parts and top Z."""
    parts = [
        part((0.040, 0.016, 0.010), (0.0, rear_y, base_z + 0.005), name="rear_base"),
        part((0.010, 0.014, height), (-0.013, rear_y, base_z + height * 0.5), name="rear_ear_l"),
        part((0.010, 0.014, height), (0.013, rear_y, base_z + height * 0.5), name="rear_ear_r"),
        part((0.030, 0.014, 0.008), (0.0, rear_y, base_z + height - 0.004), name="rear_bridge"),
        part((0.034, 0.016, 0.010), (0.0, front_y, base_z + 0.005), name="front_base"),
        part((0.012, 0.014, height + 0.006), (0.0, front_y, base_z + (height + 0.006) * 0.5),
             name="front_post"),
        part((0.006, 0.012, height), (-0.016, front_y, base_z + height * 0.5), name="front_wing_l"),
        part((0.006, 0.012, height), (0.016, front_y, base_z + height * 0.5), name="front_wing_r"),
    ]
    return parts, base_z + height + 0.006


def magazine(top_y: float, top_z: float, length: float, width: float = 0.030,
             depth: float = 0.075, curve: float = 18.0, segments: int = 3,
             surface: str = "steel_dark") -> List[bpy.types.Object]:
    """A curved box magazine built from progressively rotated segments."""
    parts = []
    y = top_y
    z = top_z
    for i in range(segments):
        angle = -i * 4.0 if curve >= 0 else i * 4.0
        seg_h = length / segments
        seg = part((width, depth, seg_h * 1.06), (0.0, 0.0, 0.0),
                   surface=surface, name="mag_segment")
        seg.rotation_euler = (math.radians(angle * 0.35), 0.0, math.radians(-angle * 0.12))
        seg.select_set(True)
        bpy.context.view_layer.objects.active = seg
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        seg.select_set(False)
        seg.location = (0.0, y, z - seg_h * 0.5)
        bpy.context.view_layer.update()
        parts.append(seg)
        z -= seg_h * 0.96
        y -= math.sin(math.radians(angle)) * seg_h * 0.4
    # Ribs down the narrow faces and a witness-hole strip: the two details that
    # make a magazine read as a magazine in the hand and mid-reload.
    rib_z = top_z - length * 0.5
    for i in range(3):
        parts.append(part((width * 1.08, 0.004, 0.006),
                          (0.0, top_y - depth * 0.5 - 0.001, rib_z - (i - 1) * length * 0.22),
                          surface="hardened_steel", name="mag_rib_front"))
        parts.append(part((width * 1.08, 0.004, 0.006),
                          (0.0, top_y + depth * 0.5 + 0.001, rib_z - (i - 1) * length * 0.22),
                          surface="hardened_steel", name="mag_rib_rear"))
    for i in range(4):
        parts.append(part((width * 0.62, 0.004, 0.005),
                          (0.0, top_y + depth * 0.5 + 0.002, top_z - 0.014 - i * 0.020),
                          surface="rubber", name="mag_witness"))
    # floor plate
    parts.append(part((width * 1.15, depth * 1.12, 0.012), (0.0, y, z + 0.004),
                      surface="rubber", name="mag_floor"))
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
        # ejection port: a dark recess panel on the right side
        part((0.004, 0.062, 0.030), (half_w, mid_y + 0.020, top_z - 0.006),
             surface="rubber", name="ejection_port"),
        # charging handle that the bolt animation drives visually
        part((0.016, 0.026, 0.010), (half_w + 0.008, mid_y - 0.040, top_z - 0.004),
             surface="hardened_steel", name="charging_handle"),
        # selector lever on the left side
        part((0.006, 0.044, 0.010), (-half_w, mid_y + 0.010, top_z - 0.026),
             surface="hardened_steel", name="selector"),
        # magazine well flare
        part((0.050, 0.086, 0.014), (0.0, mid_y + 0.050, -receiver_h * 0.36),
             surface="steel_dark", name="magwell_flare"),
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
    """Pistol grip: two stacked angled blocks and a thumb rest."""
    parts = [
        part((0.032, 0.042, length * 0.55), (0.0, center_y, top_z - length * 0.28),
             surface=surface, rotation_deg=(-14.0, 0.0, 0.0), name="grip_upper"),
        part((0.030, 0.040, length * 0.5), (0.0, center_y + 0.012, top_z - length * 0.78),
             surface=surface, rotation_deg=(-20.0, 0.0, 0.0), name="grip_lower"),
        part((0.036, 0.012, 0.012), (0.0, center_y - 0.028, top_z - length * 0.62),
             surface="steel_dark", name="trigger_guard_front"),
        part((0.020, 0.010, 0.010), (0.0, center_y - 0.006, top_z - length * 0.55),
             surface="steel_dark", name="trigger"),
    ]
    return parts


def stock(rear_y: float, top_z: float, length: float, surface: str = "rubber",
          style: str = "fixed") -> List[bpy.types.Object]:
    """Stock: fixed rifle stock, folding wire stock, or a bare receiver cap."""
    parts: List[bpy.types.Object] = []
    if style == "none":
        parts.append(part((0.034, 0.014, 0.060), (0.0, rear_y + 0.007, top_z - 0.020),
                          surface="rubber", name="end_cap"))
        return parts

    if style == "folding":
        parts.append(part((0.010, length, 0.010), (0.0, rear_y - length * 0.5, top_z - 0.012),
                          surface="steel_dark", name="stock_rail_top"))
        parts.append(part((0.010, length, 0.010), (0.0, rear_y - length * 0.5, top_z - 0.062),
                          surface="steel_dark", name="stock_rail_bottom"))
        parts.append(part((0.036, 0.012, 0.070), (0.0, rear_y - length, top_z - 0.038),
                          surface="rubber", name="butt_pad"))
        return parts

    parts.append(part((0.036, length, 0.052), (0.0, rear_y - length * 0.5, top_z - 0.028),
                      surface=surface, name="stock_body"))
    parts.append(part((0.038, length * 0.55, 0.022), (0.0, rear_y - length * 0.35, top_z - 0.070),
                      surface=surface, name="stock_cheek"))
    parts.append(part((0.040, 0.014, 0.080), (0.0, rear_y - length, top_z - 0.032),
                      surface="rubber", name="butt_pad"))
    return parts


def handguard(front_y: float, length: float, top_z: float, radius: float,
              surface: str = "wood_crate") -> List[bpy.types.Object]:
    """Handguard with vent slots and a lower heat shield."""
    parts = [
        part((radius * 2.5, length, 0.052), (0.0, front_y - length * 0.5, top_z),
             surface=surface, name="handguard"),
        part((radius * 1.6, length * 0.9, 0.012), (0.0, front_y - length * 0.5, top_z - 0.030),
             surface="steel_dark", name="heat_shield"),
    ]
    slots = max(3, int(length / 0.045))
    for i in range(slots):
        y = front_y - length * (i + 0.5) / slots
        parts.append(part((radius * 2.6, length / slots * 0.45, 0.012),
                          (0.0, y, top_z + 0.018), surface="steel_dark", name="vent"))
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
               mag=(0.125, 0.032, 6.0), sight_h=0.024, muzzle="none", bolt=None, pistol=True),
}


def build_weapon(weapon_id: str) -> Tuple[bpy.types.Object, bpy.types.Object, dict]:
    spec = SPECS[weapon_id]
    receiver_len = spec["receiver_len"]
    receiver_h = spec["receiver_h"]
    top_z = receiver_h * 0.5
    rear_y = receiver_len * 0.55
    front_y = -receiver_len * 0.45
    body: List[bpy.types.Object] = []

    # ---------------------------------------------------------------- receiver
    body.append(part((0.046, receiver_len, receiver_h), (0.0, 0.0, 0.0),
                     surface="steel_dark", name="receiver"))
    body.append(part((0.040, receiver_len * 0.86, 0.010), (0.0, -0.005, top_z + 0.005),
                     surface="steel_dark", name="receiver_top"))
    body.append(part((0.006, receiver_len * 0.42, 0.026), (0.024, -0.02, 0.002),
                     surface="sheet_metal", name="ejection_port"))
    body.append(part((0.048, 0.020, 0.030), (0.0, rear_y + 0.004, -0.006),
                     surface="rubber", name="receiver_rear"))

    # ------------------------------------------------------------------ barrel
    barrel_front = front_y - spec["barrel_len"]
    body.append(tube(spec["barrel_r"], spec["barrel_len"],
                     (0.0, (front_y + barrel_front) * 0.5, 0.004), surface="hardened_steel",
                     name="barrel"))
    body.append(tube(spec["barrel_r"] * 1.25, 0.030, (0.0, front_y - 0.012, 0.004),
                     surface="steel_dark", name="barrel_nut"))

    muzzle = spec["muzzle"]
    if muzzle == "brake":
        body.append(tube(spec["barrel_r"] * 1.7, 0.058, (0.0, barrel_front - 0.020, 0.004),
                         surface="hardened_steel", name="muzzle_brake"))
        for i in range(3):
            body.append(part((spec["barrel_r"] * 3.6, 0.008, 0.010),
                             (0.0, barrel_front - 0.006 - i * 0.016, 0.010),
                             surface="steel_dark", name="brake_port"))
    elif muzzle == "flash":
        body.append(tube(spec["barrel_r"] * 1.5, 0.046, (0.0, barrel_front - 0.016, 0.004),
                         surface="steel_dark", name="flash_hider"))
    elif muzzle == "cone":
        body.append(tube(spec["barrel_r"] * 1.6, 0.034, (0.0, barrel_front - 0.010, 0.004),
                         surface="steel_dark", name="muzzle_cone",
                         radius_top=spec["barrel_r"] * 2.1))
    elif muzzle == "bead":
        body.append(part((0.012, 0.012, 0.014), (0.0, barrel_front - 0.006, top_z * 0.55),
                         surface="hardened_steel", name="bead_sight"))

    # --------------------------------------------------------------- handguard
    if spec.get("pump"):
        body.extend(handguard(front_y - 0.055, spec["barrel_len"] * 0.42, top_z - 0.020,
                              spec["barrel_r"] * 1.9, surface=spec["surface"]))
        body.append(part((0.048, 0.120, 0.048), (0.0, front_y - 0.16, top_z - 0.048),
                         surface=spec["surface"], name="pump_grip"))
    elif spec["barrel_len"] > 0.10:
        body.extend(handguard(front_y - 0.012, spec["barrel_len"] * 0.52, top_z - 0.004,
                              spec["barrel_r"] * 1.8, surface=spec["surface"]))

    # -------------------------------------------------------------- stock/grip
    if spec["stock"] != "none":
        body.extend(stock(rear_y, top_z, spec["stock_len"], surface=spec["surface"],
                          style=spec["stock"]))
    else:
        body.extend(stock(rear_y, top_z, 0.0, style="none"))
    grip_y = rear_y - receiver_len * 0.34
    grip_top_z = -receiver_h * 0.3
    body.extend(grip(grip_y, grip_top_z,
                     length=0.09 if spec.get("pistol") else 0.115))

    # ----------------------------------------------------------------- sights
    sight_parts, sight_top = iron_sights(rear_y - receiver_len * 0.28,
                                        barrel_front + 0.075 if not spec.get("pistol")
                                        else front_y + 0.01,
                                        top_z + 0.010, height=0.028 if not spec.get("pistol") else 0.020)
    body.extend(sight_parts)
    body.extend(rail(rear_y - receiver_len * 0.10, front_y + 0.02, top_z + 0.012))
    body.extend(receiver_details(rear_y, receiver_len, receiver_h, top_z, spec))

    # ------------------------------------------------------------------ bolt
    bolt = spec.get("bolt")
    if bolt == "side":
        body.append(part((0.055, 0.012, 0.012), (0.030, rear_y - 0.075, top_z - 0.010),
                         surface="hardened_steel", name="bolt_handle"))
        body.append(part((0.016, 0.016, 0.016), (0.058, rear_y - 0.075, top_z - 0.010),
                         surface="rubber", name="bolt_knob"))
    elif bolt == "straight":
        body.append(part((0.014, 0.070, 0.014), (0.032, rear_y - 0.060, top_z - 0.006),
                         surface="hardened_steel", name="bolt_handle"))
        body.append(part((0.020, 0.020, 0.020), (0.032, rear_y - 0.095, top_z - 0.006),
                         surface="rubber", name="bolt_knob"))

    # ------------------------------------------------------------------ sling
    body.extend(sling_loop(rear_y - 0.02, -receiver_h * 0.55))
    if not spec.get("pistol"):
        body.extend(sling_loop(front_y - spec["barrel_len"] * 0.35, top_z - 0.048))

    weapon = finish(body, weapon_id)

    # ---------------------------------------------------------------- pivot
    # Move the geometry so the origin sits at the top of the pistol grip, i.e. in
    # the shooting hand: the viewmodel pose is a hand position, not a box centre.
    shift = mathutils.Matrix.Translation((0.0, -grip_y, -grip_top_z))
    weapon.data.transform(shift)
    weapon.data.update()
    sight_top -= grip_top_z

    # -------------------------------------------------------------- magazine
    if spec["mag"] is not None:
        mag_len, mag_depth, curve = spec["mag"]
        mag_parts = magazine(front_y + 0.045, -receiver_h * 0.4, mag_len,
                             width=0.036 if spec.get("pistol") else 0.030,
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
