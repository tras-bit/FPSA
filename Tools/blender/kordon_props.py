#!/usr/bin/env python3
"""KORDON prop, cover and landmark modules.

These give both maps their readable silhouettes and their cover rhythm: crates and
pallets (destructible), drums, spools, sandbags, hoppers, conveyors, the kiln and
the chimney stack (the two PvP landmarks), plus the three co-op objective props
(fuel line, intel terminal, extraction pad) and the rail spur.

Every prop states how it behaves in game through its metadata, which build_kit.py
writes into ``kit_manifest.json`` for the Unity side:

* ``coverHeight``  - what stance clears it (0.6 crouch, 1.2 stand);
* ``destructible`` / ``integrity`` - limited tactical destruction;
* ``interactive`` - the server accepts an interact on it (doors, valves, terminals);
* ``surfaceKind`` - Kordon.Core.Combat.MaterialKind for penetration, impacts, audio.
"""
from __future__ import annotations

import math
from typing import List

import bpy

from kordon_art import (assign_surface, bolt_row, box, cylinder, flange, ladder, panel_lines,
                        join, register_module, slope_box, torus_band, wedge)


# --------------------------------------------------------------------------
# crates, pallets, drums - the destructible cover layer
# --------------------------------------------------------------------------
def _crate(size: float, height: float, surface: str = "wood_crate") -> List[bpy.types.Object]:
    half = size * 0.5
    parts = [box((size, size, height), (0.0, 0.0, height * 0.5), surface, "crate_body")]
    for sign in (-1.0, 1.0):
        parts.append(box((size + 0.02, 0.05, 0.09), (0.0, sign * (half - 0.03), height * 0.5),
                         "wood_crate", "batten"))
        parts.append(box((size + 0.02, 0.05, 0.09), (0.0, sign * (half - 0.03), height * 0.14),
                         "wood_crate", "batten"))
        parts.append(box((size + 0.02, 0.05, 0.09), (0.0, sign * (half - 0.03), height - 0.14),
                         "wood_crate", "batten"))
        parts.append(box((0.05, size + 0.02, 0.09), (sign * (half - 0.03), 0.0, height * 0.5),
                         "wood_crate", "batten"))
    for corner_x in (-1.0, 1.0):
        for corner_y in (-1.0, 1.0):
            parts.append(box((0.09, 0.09, 0.11),
                             (corner_x * (half - 0.03), corner_y * (half - 0.03), height - 0.055),
                             "steel_dark", "corner_bracket"))
            parts.append(box((0.09, 0.09, 0.11),
                             (corner_x * (half - 0.03), corner_y * (half - 0.03), 0.055),
                             "steel_dark", "corner_bracket"))
    parts.append(box((0.34, 0.012, 0.2), (0.0, half + 0.006, height * 0.62), "concrete_worn",
                     "stencil_plate"))
    return parts


def crate_1() -> bpy.types.Object:
    parts = _crate(1.0, 1.0)
    obj = join(parts, "crate_1")
    return register_module(obj, "crate_1", category="cover", coverHeight=1.0,
                           destructible=True, integrity=35.0, tags=["cover", "destructible"],
                           surfaceKind="Wood",
                           description="1 m slatted shipping crate (destructible cover)")


def crate_stack() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((1.25, 1.05, 0.14), (0.0, 0.0, 0.07), "wood_crate", "pallet"))
    for i in range(6):
        parts.append(box((0.14, 1.05, 0.09), (-0.55 + i * 0.22, 0.0, 0.045), "wood_crate",
                         "pallet_board"))
    for part in _crate(1.0, 1.0):
        part.location.z += 0.14
        parts.append(part)
    small = _crate(0.7, 0.6)
    for part in small:
        part.location.z += 1.14
        part.location.x += 0.12
        part.location.y -= 0.1
        parts.append(part)
    parts.append(box((1.1, 0.02, 0.3), (0.0, 0.52, 0.72), "hazard_paint", "stack_band"))
    obj = join(parts, "crate_stack")
    return register_module(obj, "crate_stack", category="cover", coverHeight=1.74,
                           destructible=True, integrity=60.0, tags=["cover", "destructible"],
                           surfaceKind="Wood",
                           description="Palletised crate stack, 1.74 m: standing cover that breaks")


def pallet_stack() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    for layer in range(3):
        z = 0.06 + layer * 0.15
        rotation = 0.0 if layer % 2 == 0 else 0.14
        for i in range(7):
            parts.append(box((1.2, 0.14, 0.035),
                             (0.0, -0.42 + i * 0.14, z + 0.05), "wood_crate", "deck_board"))
            parts[-1].rotation_euler = (0.0, 0.0, rotation)
        for sign in (-1.0, 1.0):
            parts.append(box((0.14, 0.9, 0.06), (sign * 0.5, 0.0, z + 0.02), "wood_crate",
                             "bearer"))
            parts.append(box((0.14, 0.9, 0.035), (sign * 0.5, 0.0, z + 0.085), "wood_crate",
                             "bearer"))
    obj = join(parts, "pallet_stack")
    return register_module(obj, "pallet_stack", category="cover", coverHeight=0.42,
                           destructible=True, integrity=25.0, blocksSight=False,
                           tags=["cover", "destructible"], surfaceKind="Wood",
                           description="Stack of pallets, 0.5 m: low cover, splinters when shot")


def barrel_cluster() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    positions = ((-0.34, -0.2, 0.0), (0.34, -0.18, 0.0), (0.0, 0.36, 0.0))
    surfaces = ("steel_rust", "steel_painted", "steel_rust")
    for (x, y, _z), surface in zip(positions, surfaces):
        parts.append(cylinder(0.29, 0.29, 0.9, (x, y, 0.45), (0, 0, 0), 18, surface, "drum"))
        parts.append(cylinder(0.3, 0.3, 0.04, (x, y, 0.9), (0, 0, 0), 18, "steel_dark", "lid"))
        for z in (0.2, 0.7):
            parts.append(torus_band(0.292, 0.018, (x, y, z), (0, 0, 0), 18, "steel_dark", "rib"))
        for sign in (-1.0, 1.0):
            parts.append(cylinder(0.035, 0.035, 0.03, (x + sign * 0.12, y, 0.925), (0, 0, 0),
                                  10, "hardened_steel", "bung"))
        parts.append(box((0.2, 0.012, 0.12), (x, y + 0.295, 0.55), "concrete_worn", "label"))
    parts.append(box((0.5, 0.42, 0.03), (0.0, -0.02, 0.015), "gravel", "spill_pad"))
    obj = join(parts, "barrel_cluster")
    return register_module(obj, "barrel_cluster", category="cover", coverHeight=0.92,
                           tags=["cover"], surfaceKind="Steel",
                           description="Three 200 L drums: waist height cover around the yard")


def cable_spool() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(cylinder(0.62, 0.62, 0.06, (0.0, 0.0, 0.42), (90, 0, 0), 20, "wood_crate", "flange"))
    parts.append(cylinder(0.62, 0.62, 0.06, (0.0, 0.0, 0.86), (90, 0, 0), 20, "wood_crate", "flange"))
    parts.append(cylinder(0.2, 0.2, 0.56, (0.0, 0.0, 0.64), (90, 0, 0), 14, "wood_crate", "core"))
    parts.append(cylinder(0.44, 0.44, 0.34, (0.0, 0.0, 0.64), (90, 0, 0), 18, "rubber", "cable"))
    for i in range(8):
        angle = i * math.pi * 0.25
        parts.append(box((0.06, 0.5, 0.06),
                         (math.cos(angle) * 0.6, math.sin(angle) * 0.0, 0.42 + math.sin(angle) * 0.0),
                         "wood_crate", "flange_batten"))
        parts[-1].rotation_euler = (0.0, 0.0, angle)
    for sign in (-1.0, 1.0):
        parts.append(box((0.1, 0.6, 0.1), (sign * 0.5, 0.0, 0.05), "wood_crate", "skid"))
    parts.append(box((0.28, 0.02, 0.16), (0.0, 0.63, 0.64), "concrete_worn", "label"))
    obj = join(parts, "cable_spool")
    return register_module(obj, "cable_spool", category="cover", coverHeight=1.5,
                           tags=["cover"], surfaceKind="Wood",
                           description="Cable spool, 1.24 m: rounded cover with a climbable top")


def sandbag_wall() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    rows = 5
    for row in range(rows):
        z = 0.11 + row * 0.19
        offset = 0.0 if row % 2 == 0 else 0.19
        count = 5 if row % 2 == 0 else 4
        for i in range(count):
            x = -0.76 + offset + i * 0.38
            parts.append(box((0.36, 0.34, 0.17), (x, 0.0, z), "sand", "bag"))
            parts.append(box((0.3, 0.3, 0.03), (x, 0.0, z + 0.09), "sand", "bag_crown"))
    parts.append(box((2.0, 0.4, 0.06), (0.0, 0.0, 0.03), "wood_crate", "timber_base"))
    obj = join(parts, "sandbag_wall")
    return register_module(obj, "sandbag_wall", category="cover", coverHeight=1.0,
                           blocksSight=True, tags=["cover"], surfaceKind="Sand",
                           description="2 m sandbag wall: soft cover that stops nothing forever")


def concrete_block_2() -> bpy.types.Object:
    parts = [box((2.0, 1.0, 1.0), (0.0, 0.0, 0.5), "concrete_worn", "block")]
    for sign in (-1.0, 1.0):
        parts.append(box((0.5, 1.02, 0.06), (sign * 0.7, 0.0, 0.94), "concrete_raw", "chamfer"))
        parts.append(cylinder(0.05, 0.05, 0.12, (sign * 0.55, 0.0, 1.02), (0, 0, 0), 10,
                              "hardened_steel", "lift_eye"))
    parts.append(box((0.7, 0.02, 0.3), (0.0, 0.51, 0.55), "concrete_raw", "cast_mark"))
    for i in range(3):
        parts.append(box((0.03, 1.02, 0.03), (-0.6 + i * 0.6, 0.0, 0.5), "concrete_raw", "seam"))
    obj = join(parts, "concrete_block_2")
    return register_module(obj, "concrete_block_2", category="cover", coverHeight=1.0,
                           tags=["cover"], surfaceKind="Concrete",
                           description="2 t precast block: solid cover that bullets do not cross")


def rubble_pile() -> bpy.types.Object:
    parts = [box((2.2, 1.8, 0.08), (0.0, 0.0, 0.04), "concrete_worn", "dust_bed")]
    for i in range(14):
        angle = i * 2.399
        radius = 0.2 + (i % 5) * 0.19
        x = math.cos(angle) * radius * 1.6
        y = math.sin(angle) * radius * 1.2
        size = 0.16 + (i % 4) * 0.11
        height = size * (0.5 + (i % 3) * 0.22)
        part = wedge((size, size * 0.85, height), (x, y, height * 0.5), "x" if i % 2 else "y",
                     "concrete_worn" if i % 3 else "brick", "rubble_chunk")
        part.rotation_euler = (0.0, 0.0, angle)
        parts.append(part)
    for i in range(4):
        angle = i * 1.7 + 0.4
        parts.append(slope_box((math.cos(angle) * 0.5, math.sin(angle) * 0.4, 0.2),
                               (math.cos(angle) * 1.0, math.sin(angle) * 0.8, 0.5),
                               0.03, 0.03, "steel_rust", "rebar"))
    obj = join(parts, "rubble_pile")
    return register_module(obj, "rubble_pile", category="cover", coverHeight=0.6,
                           blocksSight=False, tags=["cover", "destroyed"],
                           surfaceKind="Concrete",
                           description="Collapsed concrete and rebar: low cover after destruction")


def glass_panel_2() -> bpy.types.Object:
    parts = [box((2.2, 0.09, 2.2), (0.0, 0.0, 1.1), "steel_dark", "frame")]
    parts.append(box((2.0, 0.02, 2.0), (0.0, 0.0, 1.1), "glass", "glazing"))
    parts.append(box((0.05, 0.1, 2.0), (0.0, 0.0, 1.1), "steel_dark", "mullion"))
    for sign in (-1.0, 1.0):
        parts.append(box((0.1, 0.16, 0.1), (sign * 1.05, 0.0, 0.05), "steel_dark", "foot"))
        bolt_row(0.4, 2, sign * 1.05, 0.06, 0.05, radius=0.018, surface="hardened_steel",
                 axis="x", parts=parts, rotation_deg=(90, 0, 0))
    obj = join(parts, "glass_panel_2")
    return register_module(obj, "glass_panel_2", category="cover", coverHeight=2.2,
                           destructible=True, integrity=8.0, blocksMovement=True,
                           blocksSight=False, tags=["glass", "destructible"],
                           surfaceKind="Glass",
                           description="Glazed steel screen; one shot clears the pane")


# --------------------------------------------------------------------------
# plant equipment
# --------------------------------------------------------------------------
def hopper_3() -> bpy.types.Object:
    parts = [box((3.0, 3.0, 1.4), (0.0, 0.0, 2.0), "steel_painted", "bin")]
    for sign in (-1.0, 1.0):
        parts.append(box((3.06, 0.08, 0.12), (0.0, sign * 1.5, 2.7), "steel_dark", "top_band"))
        parts.append(box((3.06, 0.08, 0.12), (0.0, sign * 1.5, 1.5), "steel_dark", "band"))
        parts.append(box((0.08, 3.06, 0.12), (sign * 1.5, 0.0, 2.1), "steel_dark", "band"))
    parts.append(wedge((2.6, 2.6, 1.2), (0.0, 0.0, 0.75), "y", "steel_rust", "funnel_a"))
    parts.append(wedge((2.6, 2.6, 1.2), (0.0, 0.0, 0.75), "x", "steel_rust", "funnel_b"))
    parts.append(cylinder(0.28, 0.28, 0.5, (0.0, 0.0, 0.25), (0, 0, 0), 16, "steel_dark", "spout"))
    parts += flange(0.36, 0.05, (0.0, 0.0, 0.06), (0, 0, 0), "steel_dark", bolts=8)
    for corner in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        parts.append(box((0.16, 0.16, 1.3), (corner[0] * 1.35, corner[1] * 1.35, 0.65),
                         "steel_dark", "leg"))
        parts.append(box((0.34, 0.34, 0.08), (corner[0] * 1.35, corner[1] * 1.35, 0.04),
                         "concrete_worn", "footing"))
    parts.append(box((0.6, 0.02, 0.4), (0.0, 1.52, 2.3), "hazard_paint", "hazard_plate"))
    parts += ladder(1.6, 0.4, 0.28, start=(1.55, 0.0, 1.35), end=(1.55, 0.0, 2.9))
    obj = join(parts, "hopper_3")
    return register_module(obj, "hopper_3", category="equipment", coverHeight=2.9,
                           tags=["landmark"], surfaceKind="Steel",
                           description="3 m storage hopper on legs with a maintenance ladder")


def transformer_box() -> bpy.types.Object:
    parts = [box((0.9, 1.6, 0.14), (0.0, 0.0, 0.07), "concrete_worn", "plinth")]
    parts.append(box((0.8, 1.5, 1.9), (0.0, 0.0, 1.1), "steel_painted", "cabinet"))
    parts.append(box((0.84, 0.06, 1.5), (0.0, 0.0, 1.3), "steel_dark", "door_seam"))
    for i in range(9):
        parts.append(box((0.82, 0.02, 0.05), (0.0, -0.6 + i * 0.15, 1.85), "steel_dark", "vent"))
    parts.append(box((0.24, 0.3, 0.02), (0.0, 0.76, 1.5), "hazard_paint", "warning_plate"))
    for sign in (-1.0, 1.0):
        parts.append(cylinder(0.06, 0.045, 0.34, (sign * 0.24, 0.0, 2.2), (0, 0, 0), 12,
                              "ceramic_insulator", "insulator"))
        parts.append(torus_band(0.055, 0.014, (sign * 0.24, 0.0, 2.28), (0, 0, 0), 12,
                                "ceramic_insulator", "insulator_skirt"))
    parts.append(box((0.5, 0.5, 0.16), (0.0, 0.0, 2.05), "steel_dark", "cable_box"))
    parts.append(cylinder(0.05, 0.05, 0.6, (0.3, 0.2, 1.0), (0, 0, 0), 10, "steel_rust", "conduit"))
    parts.append(box((0.1, 0.12, 0.22), (-0.42, 0.3, 1.1), "hardened_steel", "handle"))
    obj = join(parts, "transformer_box")
    return register_module(obj, "transformer_box", category="equipment", coverHeight=2.2,
                           tags=["cover"], surfaceKind="Steel",
                           description="Substation cabinet with ceramic insulators")


def dumpster_2() -> bpy.types.Object:
    parts = [box((2.2, 1.3, 0.1), (0.0, 0.0, 0.3), "steel_dark", "base")]
    parts.append(box((2.2, 1.3, 1.0), (0.0, 0.0, 0.85), "steel_rust", "body"))
    for i in range(5):
        parts.append(box((0.05, 1.32, 0.9), (-0.88 + i * 0.44, 0.0, 0.85), "steel_dark", "rib"))
    parts.append(box((2.24, 1.34, 0.07), (0.0, 0.0, 1.38), "steel_dark", "top_rim"))
    parts.append(box((2.2, 1.26, 0.06), (0.0, 0.0, 1.44), "sheet_metal", "lid"))
    parts.append(box((0.5, 1.2, 0.04), (0.0, 0.0, 1.5), "sheet_metal", "lid_panel"))
    for sign in (-1.0, 1.0):
        parts.append(cylinder(0.12, 0.12, 0.1, (sign * 0.8, 0.0, 0.18), (0, 0, 0), 12,
                              "rubber", "wheel"))
        parts.append(box((0.1, 0.1, 0.2), (sign * 0.8, 0.55, 0.4), "hardened_steel", "castor"))
        parts.append(box((0.1, 0.1, 0.2), (sign * 0.8, -0.55, 0.4), "hardened_steel", "castor"))
    parts.append(box((0.6, 0.02, 0.3), (0.0, 0.66, 0.9), "concrete_worn", "stencil"))
    obj = join(parts, "dumpster_2")
    return register_module(obj, "dumpster_2", category="cover", coverHeight=1.5,
                           tags=["cover"], surfaceKind="Steel",
                           description="Skip bin, 1.5 m: standing cover beside the service doors")


def slag_pot() -> bpy.types.Object:
    parts = [cylinder(0.85, 0.72, 1.2, (0.0, 0.0, 0.66), (0, 0, 0), 20, "steel_rust", "pot")]
    parts.append(torus_band(0.87, 0.06, (0.0, 0.0, 1.2), (0, 0, 0), 20, "steel_dark", "rim"))
    parts.append(torus_band(0.78, 0.045, (0.0, 0.0, 0.35), (0, 0, 0), 20, "steel_dark", "band"))
    for sign in (-1.0, 1.0):
        parts.append(cylinder(0.09, 0.09, 0.34, (sign * 0.95, 0.0, 0.95), (0, 90, 0), 12,
                              "hardened_steel", "trunnion"))
        parts.append(box((0.2, 0.5, 0.16), (sign * 1.05, 0.0, 0.2), "steel_dark", "saddle"))
    parts.append(cylinder(0.7, 0.7, 0.06, (0.0, 0.0, 1.24), (0, 0, 0), 20, "hardened_steel",
                          "crust"))
    for i in range(6):
        angle = i * math.pi / 3.0
        parts.append(box((0.18, 0.1, 0.08), (math.cos(angle) * 0.5, math.sin(angle) * 0.5, 1.28),
                         "hardened_steel", "crust_lump"))
    obj = join(parts, "slag_pot")
    return register_module(obj, "slag_pot", category="equipment", coverHeight=1.3,
                           tags=["landmark"], surfaceKind="HardenedSteel",
                           description="Molten slag pot on trunnions: a furnace hall landmark")


def control_desk() -> bpy.types.Object:
    parts = [box((1.8, 0.7, 0.08), (0.0, 0.0, 0.78), "steel_dark", "worktop")]
    parts.append(wedge((1.8, 0.5, 0.24), (0.0, -0.06, 0.94), "y", "sheet_metal", "console_rake"))
    for sign in (-1.0, 1.0):
        parts.append(box((0.06, 0.7, 0.78), (sign * 0.85, 0.0, 0.39), "steel_painted", "pedestal"))
    parts.append(box((1.7, 0.05, 0.4), (0.0, 0.3, 0.5), "steel_dark", "knee_panel"))
    for i in range(2):
        x = -0.45 + i * 0.9
        parts.append(box((0.66, 0.05, 0.44), (x, -0.02, 1.24), "steel_dark", "monitor_case"))
        parts.append(box((0.58, 0.02, 0.36), (x, -0.05, 1.24), "screen_emissive", "monitor_screen"))
        parts.append(box((0.1, 0.08, 0.2), (x, 0.06, 0.98), "steel_dark", "monitor_stand"))
    for i in range(8):
        parts.append(box((0.05, 0.05, 0.03), (-0.7 + i * 0.2, -0.22, 1.02),
                         "hazard_paint" if i % 3 == 0 else "steel_dark", "indicator"))
    parts.append(box((0.4, 0.16, 0.02), (0.3, -0.16, 0.83), "rubber", "keyboard"))
    obj = join(parts, "control_desk")
    return register_module(obj, "control_desk", category="equipment", coverHeight=1.5,
                           blocksSight=False, tags=["interior"],
                           description="Plant control console with two live screens")


def locker_row() -> bpy.types.Object:
    parts = [box((1.86, 0.56, 0.08), (0.0, 0.0, 0.04), "steel_dark", "plinth")]
    for i in range(3):
        x = -0.62 + i * 0.62
        parts.append(box((0.58, 0.54, 1.86), (x, 0.0, 1.01), "steel_painted", "locker"))
        parts.append(box((0.5, 0.02, 0.3), (x, 0.28, 1.6), "steel_dark", "vent"))
        parts.append(box((0.06, 0.06, 0.16), (x + 0.2, 0.29, 1.0), "hardened_steel", "handle"))
        parts.append(box((0.16, 0.02, 0.1), (x - 0.12, 0.28, 1.34), "concrete_worn", "label"))
        parts.append(box((0.6, 0.02, 0.04), (x, 0.28, 1.9), "steel_dark", "top_seam"))
    parts.append(box((1.9, 0.6, 0.05), (0.0, 0.0, 1.96), "sheet_metal", "top"))
    obj = join(parts, "locker_row")
    return register_module(obj, "locker_row", category="equipment", coverHeight=1.98,
                           tags=["interior", "cover"],
                           description="Three crew lockers: full height cover in the offices")


def conveyor_6() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    start = (-3.0, 0.0, 1.1)
    end = (3.0, 0.0, 2.3)
    parts.append(slope_box(start, end, 1.0, 0.06, "sheet_metal", "frame_a"))
    parts.append(slope_box((start[0], start[1] + 0.55, start[2]),
                           (end[0], end[1] + 0.55, end[2]), 0.08, 0.24, "steel_painted", "side"))
    parts.append(slope_box((start[0], start[1] - 0.55, start[2]),
                           (end[0], end[1] - 0.55, end[2]), 0.08, 0.24, "steel_painted", "side"))
    parts.append(slope_box((start[0], start[1], start[2] + 0.1),
                           (end[0], end[1], end[2] + 0.1), 0.86, 0.04, "rubber", "belt"))
    for i in range(6):
        t = i / 5
        x = start[0] + (end[0] - start[0]) * t
        z = start[2] + (end[2] - start[2]) * t
        parts.append(cylinder(0.09, 0.09, 1.0, (x, 0.0, z - 0.08), (90, 0, 0), 12,
                              "hardened_steel", "idler"))
        parts.append(box((0.1, 0.1, z - 0.1), (x, 0.55, (z - 0.1) * 0.5), "steel_painted", "leg"))
        parts.append(box((0.1, 0.1, z - 0.1), (x, -0.55, (z - 0.1) * 0.5), "steel_painted", "leg"))
        parts.append(box((0.34, 0.34, 0.08), (x, 0.55, 0.04), "concrete_worn", "foot"))
        parts.append(box((0.34, 0.34, 0.08), (x, -0.55, 0.04), "concrete_worn", "foot"))
    parts.append(box((0.9, 1.0, 0.5), (3.2, 0.0, 2.5), "steel_rust", "head_chute"))
    parts.append(cylinder(0.22, 0.22, 1.02, (-3.05, 0.0, 1.12), (90, 0, 0), 14,
                          "hardened_steel", "drive_drum"))
    parts.append(box((0.34, 0.4, 0.4), (-3.3, 0.62, 1.12), "steel_dark", "gearbox"))
    obj = join(parts, "conveyor_6")
    return register_module(obj, "conveyor_6", category="equipment", coverHeight=2.8,
                           blocksSight=False, tags=["landmark"],
                           description="6 m inclined belt conveyor with idlers and a head chute")


def pipe_valve() -> bpy.types.Object:
    parts = [cylinder(0.11, 0.11, 0.9, (0.0, 0.0, 0.55), (0, 90, 0), 14, "steel_rust", "pipe")]
    parts += flange(0.16, 0.04, (-0.42, 0.0, 0.55), (0, 90, 0), "steel_dark", bolts=6)
    parts += flange(0.16, 0.04, (0.42, 0.0, 0.55), (0, 90, 0), "steel_dark", bolts=6)
    parts.append(box((0.2, 0.16, 0.22), (0.0, 0.0, 0.74), "steel_dark", "bonnet"))
    parts.append(cylinder(0.03, 0.03, 0.28, (0.0, 0.0, 0.94), (0, 0, 0), 10, "hardened_steel",
                          "stem"))
    parts.append(torus_band(0.19, 0.028, (0.0, 0.0, 1.08), (0, 0, 0), 16, "hazard_paint", "wheel"))
    for i in range(4):
        angle = i * math.pi * 0.5
        parts.append(box((0.36, 0.03, 0.03),
                         (0.0, math.sin(angle) * 0.0, 1.08), "hazard_paint", "spoke"))
        parts[-1].rotation_euler = (0.0, 0.0, angle)
    parts.append(box((0.3, 0.3, 0.06), (0.0, 0.0, 0.03), "concrete_worn", "pedestal"))
    obj = join(parts, "pipe_valve")
    return register_module(obj, "pipe_valve", category="equipment", coverHeight=1.2,
                           blocksMovement=False, interactive=True, tags=["valve"],
                           description="Handwheel gate valve on a process pipe (interactive)")


# --------------------------------------------------------------------------
# lighting, signage
# --------------------------------------------------------------------------
def light_mast_8() -> bpy.types.Object:
    parts = [box((0.7, 0.7, 0.24), (0.0, 0.0, 0.12), "concrete_worn", "base")]
    parts.append(cylinder(0.13, 0.09, 7.6, (0.0, 0.0, 4.0), (0, 0, 0), 12, "steel_painted", "pole"))
    parts.append(torus_band(0.135, 0.02, (0.0, 0.0, 0.4), (0, 0, 0), 12, "steel_dark", "collar"))
    parts.append(box((0.5, 0.5, 0.14), (0.0, 0.0, 7.7), "steel_dark", "head_plate"))
    for i in range(4):
        angle = i * math.pi * 0.5 + math.pi * 0.25
        x = math.cos(angle) * 0.34
        y = math.sin(angle) * 0.34
        parts.append(box((0.3, 0.22, 0.14), (x, y, 7.6), "sheet_metal", "flood_housing"))
        parts.append(box((0.24, 0.16, 0.02), (x, y, 7.52), "lamp_emissive", "flood_lens"))
        parts.append(box((0.06, 0.06, 0.2), (x * 0.6, y * 0.6, 7.72), "steel_dark", "bracket"))
    parts += ladder(3.0, 0.34, 0.3, start=(0.16, 0.0, 0.6), end=(0.16, 0.0, 3.6))
    parts.append(box((0.3, 0.16, 0.4), (0.0, 0.2, 1.2), "steel_dark", "junction_box"))
    obj = join(parts, "light_mast_8")
    return register_module(obj, "light_mast_8", category="lighting", coverHeight=7.8,
                           blocksSight=False, tags=["light", "landmark"],
                           description="8 m floodlight mast with four heads and a ladder")


def floodlight_wall() -> bpy.types.Object:
    parts = [box((0.12, 0.2, 0.2), (0.0, 0.0, 0.1), "steel_dark", "backplate")]
    parts.append(box((0.06, 0.24, 0.06), (0.1, 0.0, 0.16), "steel_dark", "arm"))
    parts.append(box((0.24, 0.3, 0.16), (0.28, 0.0, 0.2), "sheet_metal", "housing"))
    parts.append(box((0.02, 0.24, 0.12), (0.4, 0.0, 0.2), "lamp_emissive", "lens"))
    parts.append(box((0.08, 0.32, 0.03), (0.28, 0.0, 0.29), "steel_dark", "visor"))
    obj = join(parts, "floodlight_wall")
    return register_module(obj, "floodlight_wall", category="lighting", coverHeight=0.36,
                           blocksMovement=False, blocksSight=False, tags=["light"],
                           description="Wall mounted floodlight for interiors and tunnels")


def sign_plate() -> bpy.types.Object:
    parts = [box((1.4, 0.05, 0.9), (0.0, 0.0, 0.45), "sheet_metal", "plate")]
    parts.append(box((1.44, 0.07, 0.06), (0.0, 0.0, 0.88), "steel_dark", "top_rail"))
    parts.append(box((1.44, 0.07, 0.06), (0.0, 0.0, 0.02), "steel_dark", "bottom_rail"))
    parts.append(box((0.9, 0.012, 0.34), (-0.16, 0.03, 0.56), "hazard_paint", "text_field"))
    parts.append(box((0.3, 0.012, 0.3), (0.5, 0.03, 0.3), "steel_dark", "number_field"))
    for sign in (-1.0, 1.0):
        parts.append(cylinder(0.02, 0.02, 0.14, (sign * 0.62, 0.0, 0.78), (90, 0, 0), 8,
                              "hardened_steel", "fixing"))
        parts.append(cylinder(0.02, 0.02, 0.14, (sign * 0.62, 0.0, 0.14), (90, 0, 0), 8,
                              "hardened_steel", "fixing"))
    obj = join(parts, "sign_plate")
    return register_module(obj, "sign_plate", category="prop", coverHeight=0.9,
                           blocksMovement=False, blocksSight=False, tags=["signage"],
                           description="Hanging plant sign plate (numbering and hazard text)")


# --------------------------------------------------------------------------
# landmarks (the two silhouettes that orient players on Zavod-7)
# --------------------------------------------------------------------------
def furnace_kiln() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((5.4, 5.4, 0.3), (0.0, 0.0, 0.15), "concrete_worn", "plinth"))
    parts.append(cylinder(2.3, 2.3, 5.6, (0.0, 0.0, 3.1), (0, 0, 0), 26, "steel_rust", "shell"))
    parts.append(cylinder(2.34, 2.05, 1.5, (0.0, 0.0, 6.65), (0, 0, 0), 26, "steel_rust", "cone"))
    parts.append(cylinder(0.9, 0.9, 1.0, (0.0, 0.0, 7.6), (0, 0, 0), 18, "steel_dark", "stack"))
    parts.append(torus_band(0.95, 0.06, (0.0, 0.0, 8.05), (0, 0, 0), 18, "steel_dark", "cap_ring"))
    for z in (1.0, 3.1, 5.2):
        parts.append(torus_band(2.34, 0.07, (0.0, 0.0, z), (0, 0, 0), 26, "steel_dark", "tyre"))
    parts.append(torus_band(2.36, 0.09, (0.0, 0.0, 1.9), (0, 0, 0), 26, "hazard_paint",
                            "hazard_band"))
    # ring catwalk at 4 m with its own rails and supports
    for i in range(24):
        angle = i * math.pi / 12.0
        x = math.cos(angle) * 2.75
        y = math.sin(angle) * 2.75
        parts.append(box((0.62, 0.62, 0.08), (x, y, 4.0), "grating", "ring_deck"))
        parts[-1].rotation_euler = (0.0, 0.0, angle)
        parts.append(box((0.05, 0.05, 1.0), (math.cos(angle) * 3.05, math.sin(angle) * 3.05, 4.55),
                         "steel_painted", "ring_post"))
        parts.append(box((0.62, 0.05, 0.05),
                         (math.cos(angle) * 3.05, math.sin(angle) * 3.05, 5.02),
                         "steel_painted", "ring_rail"))
        parts[-1].rotation_euler = (0.0, 0.0, angle)
    for i in range(8):
        angle = i * math.pi * 0.25
        parts.append(slope_box((math.cos(angle) * 2.4, math.sin(angle) * 2.4, 0.6),
                               (math.cos(angle) * 2.75, math.sin(angle) * 2.75, 3.95),
                               0.1, 0.12, "steel_dark", "ring_bracket"))
    # four burner pipes with flanges, plus the charge chute and a control cabinet
    for i in range(4):
        angle = i * math.pi * 0.5 + math.pi * 0.25
        x = math.cos(angle) * 2.3
        y = math.sin(angle) * 2.3
        parts.append(cylinder(0.19, 0.19, 1.5, (x * 1.28, y * 1.28, 1.5),
                              (0.0, math.degrees(angle) + 90.0, 0.0), 12, "steel_dark", "burner"))
        parts += flange(0.26, 0.05, (x * 1.55, y * 1.55, 1.5),
                        (0.0, math.degrees(angle) + 90.0, 0.0), "steel_dark", bolts=6)
    parts.append(cylinder(0.42, 0.42, 1.9, (0.0, 2.9, 1.4), (18, 0, 0), 16, "steel_rust", "chute"))
    parts.append(box((1.1, 0.5, 1.5), (0.0, 3.6, 0.75), "steel_painted", "chute_housing"))
    parts.append(box((0.8, 0.6, 1.6), (-2.9, 2.4, 0.8), "steel_painted", "control_cabinet"))
    parts.append(box((0.5, 0.03, 0.4), (-2.9, 2.72, 1.3), "screen_emissive", "cabinet_screen"))
    parts += ladder(3.6, 0.42, 0.3, start=(2.5, 0.0, 0.3), end=(2.5, 0.0, 3.9))
    for z in (0.9, 2.1, 3.3):
        parts.append(box((0.34, 0.34, 0.06), (2.5, 0.28, z), "steel_dark", "ladder_standoff"))
    parts.append(box((0.9, 0.02, 0.5), (0.0, 2.31, 4.4), "hazard_paint", "plate"))
    obj = join(parts, "furnace_kiln")
    return register_module(obj, "furnace_kiln", category="landmark", coverHeight=8.2,
                           climbable=True, tags=["landmark", "kiln"], surfaceKind="Steel",
                           description="Rotary kiln, 8.2 m: the furnace hall landmark with a ring catwalk")


def chimney_stack() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((4.0, 4.0, 0.5), (0.0, 0.0, 0.25), "concrete_worn", "base"))
    parts.append(box((3.2, 3.2, 0.9), (0.0, 0.0, 0.95), "brick", "pedestal"))
    parts.append(cylinder(1.15, 0.78, 12.0, (0.0, 0.0, 7.4), (0, 0, 0), 22, "brick", "shaft"))
    for z in (4.0, 8.0, 11.4):
        parts.append(torus_band(1.02 if z < 6 else 0.9, 0.07, (0.0, 0.0, z), (0, 0, 0), 22,
                                "steel_dark", "band"))
    parts.append(torus_band(0.82, 0.09, (0.0, 0.0, 13.4), (0, 0, 0), 22, "hazard_paint",
                            "aviation_band"))
    parts.append(cylinder(0.86, 0.82, 0.5, (0.0, 0.0, 13.65), (0, 0, 0), 22, "steel_dark", "cap"))
    for i in range(10):
        angle = i * math.pi / 5.0
        parts.append(box((0.1, 0.1, 1.3), (math.cos(angle) * 1.0, math.sin(angle) * 1.0, 2.0),
                         "brick", "buttress"))
    parts.append(box((0.16, 0.16, 1.0), (0.0, 0.0, 14.0), "hardened_steel", "lightning_rod"))
    parts.append(cylinder(0.05, 0.05, 0.24, (0.0, 0.0, 14.6), (0, 0, 0), 8, "lamp_emissive",
                          "beacon"))
    parts.append(box((1.2, 0.1, 0.9), (0.0, 1.15, 0.75), "sheet_metal", "flue"))
    parts.append(cylinder(0.34, 0.34, 1.4, (0.0, 1.7, 0.75), (0, 90, 0), 14, "steel_rust",
                          "flue_pipe"))
    parts.append(box((0.7, 0.08, 1.6), (0.9, 1.1, 1.2), "steel_dark", "access_door"))
    parts.append(box((0.06, 0.1, 0.2), (1.15, 1.16, 1.2), "hardened_steel", "door_handle"))
    parts += ladder(9.0, 0.42, 0.32, start=(-1.2, 0.0, 1.5), end=(-1.2, 0.0, 10.5))
    for z in (2.4, 5.0, 7.6, 10.0):
        parts.append(box((0.4, 0.4, 0.05), (-1.2, 0.0, z), "steel_dark", "ladder_cage"))
    obj = join(parts, "chimney_stack")
    return register_module(obj, "chimney_stack", category="landmark", coverHeight=14.8,
                           tags=["landmark", "chimney"], surfaceKind="Brick",
                           description="14.8 m brick stack: visible from every route on Zavod-7")


# --------------------------------------------------------------------------
# rail spur (co-op insertion) and rolling stock
# --------------------------------------------------------------------------
def rail_track_4() -> bpy.types.Object:
    parts = [box((4.0, 2.6, 0.06), (0.0, 0.0, 0.03), "gravel", "ballast")]
    for i in range(9):
        x = -1.8 + i * 0.45
        parts.append(box((0.24, 2.2, 0.14), (x, 0.0, 0.11), "wood_crate", "sleeper"))
    for sign in (-1.0, 1.0):
        parts.append(box((4.0, 0.07, 0.13), (0.0, sign * 0.72, 0.22), "hardened_steel", "rail"))
        parts.append(box((4.0, 0.12, 0.03), (0.0, sign * 0.72, 0.17), "steel_dark", "rail_foot"))
    for i in range(4):
        x = -1.5 + i * 1.0
        for sign in (-1.0, 1.0):
            parts.append(box((0.05, 0.1, 0.05), (x, sign * 0.72, 0.17), "steel_dark", "clip"))
    parts.append(box((0.3, 2.4, 0.02), (1.6, 0.0, 0.07), "sand", "ballast_patch"))
    obj = join(parts, "rail_track_4")
    return register_module(obj, "rail_track_4", category="floor", walkable=True,
                           blocksMovement=False, blocksSight=False, coverHeight=0.3,
                           surfaceKind="Gravel", tags=["rail"],
                           description="4 m of standard gauge track on timber sleepers")


def rail_wagon_8() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((8.0, 2.9, 0.16), (0.0, 0.0, 1.06), "steel_rust", "underframe"))
    parts.append(box((7.6, 2.7, 1.1), (0.0, 0.0, 1.7), "steel_rust", "body"))
    parts.append(wedge((3.2, 2.5, 0.6), (-1.9, 0.0, 1.3), "y", "steel_dark", "hopper_a"))
    parts.append(wedge((3.2, 2.5, 0.6), (1.9, 0.0, 1.3), "y", "steel_dark", "hopper_b"))
    for i in range(9):
        x = -3.6 + i * 0.9
        parts.append(box((0.07, 2.76, 1.0), (x, 0.0, 1.7), "steel_dark", "rib"))
    parts.append(box((8.06, 2.94, 0.1), (0.0, 0.0, 2.28), "steel_dark", "top_rim"))
    for sign in (-1.0, 1.0):
        parts.append(box((0.08, 2.7, 0.5), (sign * 3.9, 0.0, 1.5), "steel_dark", "end_wall"))
        parts.append(box((0.5, 0.06, 0.5), (sign * 3.7, 1.2, 1.5), "hardened_steel", "buffer"))
        parts.append(box((0.5, 0.06, 0.5), (sign * 3.7, -1.2, 1.5), "hardened_steel", "buffer"))
        parts.append(cylinder(0.09, 0.09, 0.5, (sign * 4.1, 0.0, 1.06), (0, 90, 0), 10,
                              "steel_dark", "coupler"))
    for bogie in (-2.5, 2.5):
        parts.append(box((2.2, 2.0, 0.24), (bogie, 0.0, 0.86), "steel_dark", "bogie_frame"))
        for wheel in (-0.75, 0.75):
            for side in (-1.0, 1.0):
                parts.append(cylinder(0.42, 0.42, 0.12, (bogie + wheel, side * 1.02, 0.46),
                                      (90, 0, 0), 16, "hardened_steel", "wheel"))
                parts.append(cylinder(0.12, 0.12, 0.2, (bogie + wheel, side * 1.02, 0.46),
                                      (90, 0, 0), 10, "steel_dark", "axle_box"))
    parts += ladder(1.2, 0.4, 0.24, start=(3.5, 1.45, 1.1), end=(3.5, 1.45, 2.3))
    parts.append(box((1.4, 0.02, 0.5), (0.0, 1.39, 1.9), "concrete_worn", "stencil"))
    parts.append(box((0.9, 0.06, 0.9), (-3.0, 1.42, 1.7), "sheet_metal", "brake_cabinet"))
    obj = join(parts, "rail_wagon_8")
    return register_module(obj, "rail_wagon_8", category="landmark", coverHeight=2.4,
                           climbable=True, tags=["rail", "landmark", "cover"],
                           surfaceKind="Steel",
                           description="Eight metre hopper wagon on the rail spur: climbable cover")


# --------------------------------------------------------------------------
# co-op objective props
# --------------------------------------------------------------------------
def fuel_line() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((2.6, 0.9, 0.12), (0.0, 0.0, 0.06), "concrete_worn", "plinth"))
    for sign in (-1.0, 1.0):
        parts.append(box((0.14, 0.7, 0.9), (sign * 1.05, 0.0, 0.55), "steel_painted", "support"))
    parts.append(cylinder(0.16, 0.16, 3.0, (0.0, 0.0, 1.0), (0, 90, 0), 16, "steel_rust",
                          "main_pipe"))
    parts.append(cylinder(0.09, 0.09, 3.0, (0.0, 0.28, 0.72), (0, 90, 0), 12, "steel_dark",
                          "return_pipe"))
    for x in (-1.35, 0.0, 1.35):
        parts += flange(0.23, 0.05, (x, 0.0, 1.0), (0, 90, 0), "steel_dark", bolts=8)
    for sign in (-1.0, 1.0):
        parts.append(box((0.22, 0.2, 0.26), (sign * 0.7, 0.0, 1.24), "steel_dark", "valve_body"))
        parts.append(cylinder(0.03, 0.03, 0.3, (sign * 0.7, 0.0, 1.46), (0, 0, 0), 10,
                              "hardened_steel", "valve_stem"))
        parts.append(torus_band(0.17, 0.026, (sign * 0.7, 0.0, 1.62), (0, 0, 0), 14,
                                "hazard_paint", "valve_wheel"))
    parts.append(box((0.3, 0.16, 0.3), (0.2, 0.3, 1.16), "sheet_metal", "gauge_body"))
    parts.append(cylinder(0.11, 0.11, 0.03, (0.2, 0.4, 1.16), (90, 0, 0), 14, "screen_emissive",
                          "gauge_face"))
    parts.append(box((0.5, 0.34, 0.06), (0.0, -0.36, 1.0), "steel_dark", "charge_plate"))
    for corner in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        parts.append(cylinder(0.022, 0.022, 0.08, (corner[0] * 0.19, -0.36 + corner[1] * 0.12,
                                                   1.0 + corner[1] * 0.0), (90, 0, 0), 8,
                              "hardened_steel", "charge_bolt"))
    parts.append(box((0.6, 0.02, 0.24), (0.0, 0.46, 0.5), "hazard_paint", "hazard_band"))
    parts.append(box((0.34, 0.02, 0.34), (-1.2, 0.46, 1.0), "concrete_worn", "stencil"))
    obj = join(parts, "fuel_line")
    return register_module(obj, "fuel_line", category="objective", coverHeight=1.7,
                           interactive=True, destructible=True, integrity=90.0,
                           tags=["objective:sabotage_fuel_line"], surfaceKind="Steel",
                           description="Fuel line with two valves and two charge points (objective)")


def intel_terminal() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((1.6, 0.8, 0.1), (0.0, 0.0, 0.05), "concrete_worn", "plinth"))
    parts.append(box((0.7, 0.7, 2.0), (-0.45, 0.0, 1.05), "steel_dark", "server_rack"))
    for i in range(8):
        parts.append(box((0.62, 0.02, 0.12), (-0.45, 0.36, 0.4 + i * 0.2), "sheet_metal", "unit"))
        parts.append(box((0.05, 0.02, 0.05), (-0.68, 0.37, 0.4 + i * 0.2),
                         "screen_emissive" if i % 3 else "hazard_paint", "led"))
    parts.append(box((0.66, 0.04, 0.3), (-0.45, -0.36, 1.8), "steel_dark", "rack_vent"))
    parts.append(box((0.9, 0.6, 0.06), (0.45, 0.0, 0.76), "steel_painted", "desk_top"))
    for sign in (-1.0, 1.0):
        parts.append(box((0.06, 0.5, 0.72), (0.45 + sign * 0.4, 0.0, 0.38), "steel_painted",
                         "desk_leg"))
    parts.append(box((0.5, 0.05, 0.34), (0.45, -0.02, 1.02), "steel_dark", "monitor_case"))
    parts.append(box((0.44, 0.02, 0.28), (0.45, -0.05, 1.02), "screen_emissive", "monitor_screen"))
    parts.append(box((0.3, 0.14, 0.02), (0.45, 0.14, 0.8), "rubber", "keyboard"))
    parts.append(box((0.4, 0.3, 0.02), (0.45, 0.0, 1.3), "sheet_metal", "paper_tray"))
    parts.append(box((0.14, 0.14, 0.4), (0.85, -0.3, 0.98), "steel_dark", "cable_tray"))
    parts.append(cylinder(0.03, 0.03, 1.2, (0.85, -0.3, 0.4), (0, 0, 0), 8, "rubber", "cable"))
    obj = join(parts, "intel_terminal")
    return register_module(obj, "intel_terminal", category="objective", coverHeight=2.05,
                           interactive=True, tags=["objective:retrieve_intel", "interior"],
                           description="Control room terminal and server rack (retrieve objective)")


def extraction_pad() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((8.0, 8.0, 0.22), (0.0, 0.0, 0.11), "concrete_worn", "pad"))
    for sign in (-1.0, 1.0):
        parts.append(box((8.0, 0.3, 0.06), (0.0, sign * 3.7, 0.24), "hazard_paint", "edge_stripe"))
        parts.append(box((0.3, 8.0, 0.06), (sign * 3.7, 0.0, 0.24), "hazard_paint", "edge_stripe"))
    for i in range(8):
        angle = i * math.pi * 0.25
        x = math.cos(angle) * 2.6
        y = math.sin(angle) * 2.6
        parts.append(wedge((1.1, 0.5, 0.03), (x, y, 0.235), "x", "concrete_painted", "chevron"))
        parts[-1].rotation_euler = (0.0, 0.0, angle)
    parts.append(torus_band(2.0, 0.09, (0.0, 0.0, 0.235), (0, 0, 0), 32, "concrete_painted",
                            "circle_marking"))
    for corner in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        x = corner[0] * 3.4
        y = corner[1] * 3.4
        parts.append(box((0.3, 0.3, 0.5), (x, y, 0.4), "steel_dark", "light_housing"))
        parts.append(box((0.22, 0.22, 0.04), (x, y, 0.67), "lamp_emissive", "light_lens"))
        parts.append(torus_band(0.13, 0.03, (x * 0.6, y * 0.6, 0.24), (0, 0, 0), 12,
                                "hardened_steel", "tie_down"))
    parts.append(box((1.2, 0.3, 0.5), (0.0, -3.2, 0.45), "steel_painted", "marker_box"))
    parts.append(box((0.9, 0.02, 0.3), (0.0, -3.04, 0.5), "hazard_paint", "marker_plate"))
    parts.append(cylinder(0.06, 0.06, 1.6, (3.6, 3.6, 1.0), (0, 0, 0), 10, "steel_painted",
                          "windsock_pole"))
    parts.append(cylinder(0.16, 0.24, 0.7, (3.6, 3.9, 1.7), (0, 90, 0), 12, "hazard_paint",
                          "windsock"))
    obj = join(parts, "extraction_pad")
    return register_module(obj, "extraction_pad", category="objective", walkable=True,
                           walkHeight=0.22, blocksMovement=False, blocksSight=False,
                           coverHeight=0.5, tags=["extraction", "objective:extract"],
                           surfaceKind="Concrete",
                           description="8 x 8 m marked extraction pad with corner lights")
