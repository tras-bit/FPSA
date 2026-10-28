#!/usr/bin/env python3
"""KORDON architecture modules: walls, floors, structure, catwalks, doors, fences.

Every module is authored geometry on a 1 m / 4 m grid, with the origin at the
centre of its footprint on the ground.  Modules are the vocabulary both maps are
built from, so their dimensions are deliberately consistent:

* wall height 3.6 m, thickness 0.3 m (a storey is 4.0 m including the slab);
* doorways 1.2 x 2.2 m, windows 1.8 x 1.2 m with a 1.3 m sill;
* catwalk deck 1.5 m wide, rail 1.05 m, deck thickness 0.12 m;
* stairs rise 4 m over a 6 m run at 1.2 m width (a full storey per flight).
"""
from __future__ import annotations

import math
from typing import List, Optional, Sequence

import bpy

from kordon_art import (assign_surface, bevel, bolt_row, box, cylinder, flange, ibeam,
                        join, ladder, panel_lines, register_module, slope_box, torus_band,
                        wedge)

WALL_HEIGHT = 3.6
WALL_THICK = 0.3
DECK_WIDTH = 1.5
DECK_THICK = 0.12
RAIL_HEIGHT = 1.05


# --------------------------------------------------------------------------
# shared sub-assemblies
# --------------------------------------------------------------------------
def _plinth_and_coping(length: float, thickness: float, height: float,
                       surface: str = "concrete_worn") -> List[bpy.types.Object]:
    """Kick plate at the bottom and a coping at the top of a wall run."""
    parts = [box((length + 0.04, thickness + 0.08, 0.16), (0.0, 0.0, 0.08), surface, "plinth")]
    parts.append(box((length + 0.06, thickness + 0.10, 0.10), (0.0, 0.0, height - 0.05),
                     surface, "coping"))
    return parts


def _wall_panel(length: float, thickness: float, height: float, surface: str,
                seams: int) -> List[bpy.types.Object]:
    parts = [box((length, thickness, height), (0.0, 0.0, height * 0.5), surface, "wall")]
    if seams > 0:
        panel_lines(length, height * 0.92, thickness, seams, -length * 0.5, 0.0,
                    height * 0.5, "concrete_worn", axis="x", depth=0.0, width=0.03,
                    parts=parts)
    # formwork tie rods: two rows of small recessed discs
    for row_z in (height * 0.32, height * 0.68):
        bolt_row(length * 0.86, max(2, int(length / 1.6)), 0.0, thickness * 0.5 + 0.005,
                 row_z, radius=0.028, surface="concrete_worn", axis="x", parts=parts,
                 rotation_deg=(90.0, 0.0, 0.0))
    return parts


def _door_frame(width: float, height: float, thickness: float) -> List[bpy.types.Object]:
    frame_t = 0.09
    parts = [
        box((frame_t, thickness + 0.06, height), (-width * 0.5 - frame_t * 0.5, 0.0, height * 0.5),
            "steel_dark", "door_jamb"),
        box((frame_t, thickness + 0.06, height), (width * 0.5 + frame_t * 0.5, 0.0, height * 0.5),
            "steel_dark", "door_jamb"),
        box((width + frame_t * 2.0, thickness + 0.06, frame_t), (0.0, 0.0, height + frame_t * 0.5),
            "steel_dark", "door_lintel"),
    ]
    bolt_row(width + frame_t * 2.0, 5, 0.0, thickness * 0.5 + 0.04, height + frame_t * 0.5,
             radius=0.02, surface="hardened_steel", axis="x", parts=parts,
             rotation_deg=(90.0, 0.0, 0.0))
    return parts


def _glazing(width: float, height: float, thickness: float = 0.02,
             mullions: int = 2) -> List[bpy.types.Object]:
    parts = [box((width, thickness, height), (0.0, 0.0, 0.0), "glass", "glass")]
    for i in range(mullions):
        t = (i + 1) / (mullions + 1)
        parts.append(box((0.05, thickness + 0.05, height), (width * (t - 0.5), 0.0, 0.0),
                         "steel_dark", "mullion"))
    return parts


# --------------------------------------------------------------------------
# walls
# --------------------------------------------------------------------------
def _build_wall(module_id: str, length: float, height: float = WALL_HEIGHT,
                thickness: float = WALL_THICK, surface: str = "concrete_raw",
                opening: Optional[dict] = None, broken: bool = False,
                **meta) -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    if opening is None:
        parts += _wall_panel(length, thickness, height, surface, seams=max(0, int(length) - 1))
    else:
        width = opening["width"]
        sill = opening.get("sill", 0.0)
        top = sill + opening["height"]
        side = (length - width) * 0.5
        for sign in (-1.0, 1.0):
            center_x = sign * (width * 0.5 + side * 0.5)
            parts += _wall_panel(side, thickness, height, surface, seams=0)
            for part in parts[-1:]:
                part.location.x = center_x
            if side > 0.9:
                panel_lines(side, height * 0.9, thickness, 1, center_x - side * 0.5, 0.0,
                            height * 0.5, "concrete_worn", axis="x", depth=0.0, width=0.03,
                            parts=parts)
        if sill > 0.0:
            parts.append(box((width, thickness, sill), (0.0, 0.0, sill * 0.5), surface, "sill_wall"))
        head = height - top
        if head > 0.01:
            parts.append(box((width, thickness, head), (0.0, 0.0, top + head * 0.5),
                             surface, "head_wall"))
        parts += _door_frame(width, opening["height"], thickness)
        for part in parts:
            if part.name in ("door_jamb", "door_lintel"):
                part.location.z += sill
        if opening.get("leaf"):
            leaf = box((width - 0.04, 0.06, opening["height"] - 0.04),
                       (0.0, 0.0, sill + (opening["height"] - 0.04) * 0.5 + 0.02),
                       "steel_painted", "door_leaf")
            parts.append(leaf)
            parts.append(box((0.05, 0.14, 0.32), (width * 0.5 - 0.22, 0.06, sill + 1.05),
                             "hardened_steel", "door_handle"))
            for strip_z in (sill + 0.5, sill + opening["height"] - 0.5):
                parts.append(box((width - 0.12, 0.075, 0.06), (0.0, 0.0, strip_z),
                                 "steel_dark", "door_stiffener"))
        if opening.get("glass"):
            glass = _glazing(width - 0.1, opening["height"] - 0.1)
            for part in glass:
                part.location.z = sill + opening["height"] * 0.5
            parts += glass

    if broken:
        # A collapsed top edge: chunks removed so the wall reads as cover, not scenery.
        chunks = int(max(2, length * 1.5))
        for i in range(chunks):
            t = (i + 0.5) / chunks - 0.5
            drop = 0.35 + 0.5 * abs(math.sin(i * 2.7))
            parts.append(box((length / chunks * 0.9, thickness + 0.02, drop),
                             (length * t, 0.0, height - drop * 0.5 + 0.02),
                             "concrete_worn", "broken_cut"))
        for i in range(4):
            t = (i * 0.37 + 0.11) % 1.0 - 0.5
            parts.append(wedge((0.32, thickness * 0.8, 0.28),
                               (length * t, 0.0, height - 0.36), "x", "concrete_worn", "rubble"))

    parts += _plinth_and_coping(length, thickness, height)
    obj = join(parts, module_id)
    meta.setdefault("category", "architecture")
    meta.setdefault("coverHeight", height if not broken else height - 0.7)
    return register_module(obj, module_id, **meta)


def wall_4() -> bpy.types.Object:
    return _build_wall("wall_4", 4.0, description="4 m exterior wall run, 3.6 m high")


def wall_2() -> bpy.types.Object:
    return _build_wall("wall_2", 2.0, description="2 m wall run for tight interiors")


def wall_1() -> bpy.types.Object:
    return _build_wall("wall_1", 1.0, description="1 m wall filler / pillar spacing")


def wall_4_door() -> bpy.types.Object:
    return _build_wall("wall_4_door", 4.0,
                       opening={"width": 1.2, "height": 2.2, "leaf": True},
                       tags=["doorway"], interactive=True,
                       description="4 m wall with a 1.2 x 2.2 m steel door")


def wall_4_window() -> bpy.types.Object:
    return _build_wall("wall_4_window", 4.0,
                       opening={"width": 1.8, "height": 1.2, "sill": 1.3, "glass": True},
                       tags=["window", "glass"],
                       description="4 m wall with a glazed opening; the glass is breakable")


def wall_4_broken() -> bpy.types.Object:
    return _build_wall("wall_4_broken", 4.0, height=2.4, broken=True,
                       coverHeight=1.7, tags=["cover", "destroyed"],
                       description="Collapsed wall section: standing cover with sight gaps")


def wall_low_4() -> bpy.types.Object:
    return _build_wall("wall_low_4", 4.0, height=1.15, thickness=0.25,
                       surface="concrete_worn", coverHeight=1.15, tags=["cover"],
                       description="1.15 m parapet: crouch cover along the yard apron")


def corner_inner() -> bpy.types.Object:
    parts = [box((WALL_THICK, WALL_THICK, WALL_HEIGHT), (0.0, 0.0, WALL_HEIGHT * 0.5),
                 "concrete_raw", "corner")]
    parts.append(box((WALL_THICK + 0.06, WALL_THICK + 0.06, 0.16), (0.0, 0.0, 0.08),
                     "concrete_worn", "plinth"))
    parts.append(box((WALL_THICK + 0.08, WALL_THICK + 0.08, 0.1), (0.0, 0.0, WALL_HEIGHT - 0.05),
                     "concrete_worn", "coping"))
    obj = join(parts, "corner_inner")
    return register_module(obj, "corner_inner", category="architecture",
                           coverHeight=WALL_HEIGHT,
                           description="Internal 90 degree wall corner")


def corner_outer() -> bpy.types.Object:
    parts = [box((WALL_THICK + 0.1, WALL_THICK + 0.1, WALL_HEIGHT),
                 (0.0, 0.0, WALL_HEIGHT * 0.5), "concrete_raw", "corner")]
    for sign in (-1.0, 1.0):
        parts.append(box((0.08, WALL_THICK + 0.12, WALL_HEIGHT - 0.3),
                         (sign * (WALL_THICK * 0.5 + 0.02), 0.0, WALL_HEIGHT * 0.5),
                         "concrete_worn", "quoin"))
    parts.append(box((WALL_THICK + 0.2, WALL_THICK + 0.2, 0.18), (0.0, 0.0, 0.09),
                     "concrete_worn", "plinth"))
    parts.append(box((WALL_THICK + 0.16, WALL_THICK + 0.16, 0.12),
                     (0.0, 0.0, WALL_HEIGHT - 0.06), "concrete_worn", "coping"))
    obj = join(parts, "corner_outer")
    return register_module(obj, "corner_outer", category="architecture",
                           coverHeight=WALL_HEIGHT,
                           description="External 90 degree wall corner with quoins")


def pillar_1() -> bpy.types.Object:
    parts = [box((0.5, 0.5, 4.0), (0.0, 0.0, 2.0), "concrete_raw", "shaft")]
    for corner in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        parts.append(box((0.09, 0.09, 4.0), (corner[0] * 0.24, corner[1] * 0.24, 2.0),
                         "concrete_worn", "chamfer"))
    parts.append(box((0.66, 0.66, 0.18), (0.0, 0.0, 0.09), "concrete_worn", "base"))
    parts.append(box((0.62, 0.62, 0.14), (0.0, 0.0, 4.0 - 0.07), "concrete_worn", "capital"))
    for z in (1.0, 2.0, 3.0):
        parts.append(box((0.54, 0.54, 0.035), (0.0, 0.0, z), "concrete_worn", "band"))
    obj = join(parts, "pillar_1")
    return register_module(obj, "pillar_1", category="architecture", coverHeight=4.0,
                           description="500 mm concrete column, 4 m, chamfered corners")


# --------------------------------------------------------------------------
# floors
# --------------------------------------------------------------------------
def floor_slab_4() -> bpy.types.Object:
    parts = [box((4.0, 4.0, 0.25), (0.0, 0.0, -0.125), "concrete_raw", "slab")]
    for axis in ("x", "y"):
        for t in (-1.0, 1.0):
            center = (t * 1.0, 0.0, -0.02) if axis == "x" else (0.0, t * 1.0, -0.02)
            size = (0.025, 4.0, 0.02) if axis == "x" else (4.0, 0.025, 0.02)
            parts.append(box(size, center, "concrete_worn", "expansion_joint"))
    for corner in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        parts.append(box((0.3, 0.3, 0.03), (corner[0] * 1.85, corner[1] * 1.85, 0.005),
                         "concrete_worn", "corner_patch"))
    obj = join(parts, "floor_slab_4")
    return register_module(obj, "floor_slab_4", category="floor", walkable=True,
                           blocksMovement=False, blocksSight=False, coverHeight=0.0,
                           description="4 x 4 m concrete slab with expansion joints")


def floor_grate_4() -> bpy.types.Object:
    parts = [box((4.0, 4.0, 0.04), (0.0, 0.0, -0.06), "grating", "grate_plate")]
    for i in range(9):
        t = (i / 8 - 0.5) * 3.8
        parts.append(box((0.05, 3.9, 0.1), (t, 0.0, -0.03), "steel_dark", "bearing_bar"))
    for i in range(5):
        t = (i / 4 - 0.5) * 3.8
        parts.append(box((3.9, 0.045, 0.07), (0.0, t, -0.02), "steel_dark", "cross_bar"))
    for sign_x in (-1.0, 1.0):
        parts.append(box((0.12, 4.0, 0.14), (sign_x * 1.94, 0.0, -0.05), "steel_dark", "frame"))
    for sign_y in (-1.0, 1.0):
        parts.append(box((4.0, 0.12, 0.14), (0.0, sign_y * 1.94, -0.05), "steel_dark", "frame"))
    obj = join(parts, "floor_grate_4")
    return register_module(obj, "floor_grate_4", category="floor", walkable=True,
                           blocksMovement=False, blocksSight=True, coverHeight=0.0,
                           surfaceKind="SheetMetal",
                           description="4 x 4 m steel grating panel in a welded frame")


def floor_dirt_4() -> bpy.types.Object:
    parts = [box((4.0, 4.0, 0.1), (0.0, 0.0, -0.05), "gravel", "ballast")]
    for i in range(16):
        angle = i * 2.399
        radius = 0.3 + (i % 5) * 0.3
        x = math.cos(angle) * radius * 1.55
        y = math.sin(angle) * radius * 1.35
        size = 0.09 + (i % 3) * 0.05
        x = max(-1.82, min(1.82, x))
        y = max(-1.82, min(1.82, y))
        parts.append(box((size, size * 0.8, size * 0.5), (x, y, size * 0.2 - 0.05),
                         "gravel" if i % 2 else "concrete_worn", "stone"))
    for i in range(3):
        parts.append(box((0.9 + i * 0.4, 0.05, 0.02), (0.0, -1.5 + i * 1.5, -0.005),
                         "sand", "tyre_track"))
    obj = join(parts, "floor_dirt_4")
    return register_module(obj, "floor_dirt_4", category="floor", walkable=True,
                           blocksMovement=False, blocksSight=False, coverHeight=0.0,
                           surfaceKind="Gravel",
                           description="4 x 4 m gravel / ballast ground with stones and tracks")


def floor_hazard_4() -> bpy.types.Object:
    parts = [box((4.0, 4.0, 0.06), (0.0, 0.0, -0.03), "sheet_metal", "plate")]
    for i in range(10):
        x = -1.8 + i * 0.4
        parts.append(wedge((0.2, 3.6, 0.012), (x, 0.0, 0.006), "x", "hazard_paint", "stripe"))
    for sign in (-1.0, 1.0):
        parts.append(box((4.0, 0.1, 0.1), (0.0, sign * 1.95, -0.02), "steel_dark", "edge"))
    obj = join(parts, "floor_hazard_4")
    return register_module(obj, "floor_hazard_4", category="floor", walkable=True,
                           blocksMovement=False, blocksSight=False, coverHeight=0.0,
                           tags=["hazard"],
                           description="4 x 4 m chequered plate with hazard stripes")


# --------------------------------------------------------------------------
# structure
# --------------------------------------------------------------------------
def column_steel_4() -> bpy.types.Object:
    parts = ibeam(4.0, 0.3, 0.3, 0.022, 0.032, (0.0, 0.0, 2.0), axis="z",
                  surface="steel_painted")
    parts.append(box((0.5, 0.5, 0.03), (0.0, 0.0, 0.015), "steel_dark", "base_plate"))
    parts.append(box((0.44, 0.44, 0.025), (0.0, 0.0, 4.0 - 0.012), "steel_dark", "cap_plate"))
    bolt_row(0.36, 4, 0.0, 0.0, 0.035, radius=0.022, surface="hardened_steel", axis="x",
             parts=parts, rotation_deg=(0.0, 0.0, 0.0))
    for z in (1.3, 2.7):
        parts.append(box((0.36, 0.36, 0.05), (0.0, 0.0, z), "steel_dark", "stiffener"))
    for sign in (-1.0, 1.0):
        parts.append(wedge((0.18, 0.18, 0.18), (sign * 0.13, 0.0, 0.12), "x",
                           "steel_dark", "gusset"))
    obj = join(parts, "column_steel_4")
    return register_module(obj, "column_steel_4", category="structure", coverHeight=4.0,
                           description="4 m steel I column with base plate and stiffeners")


def beam_4() -> bpy.types.Object:
    parts = ibeam(4.0, 0.32, 0.22, 0.018, 0.028, (0.0, 0.0, 0.16), axis="x",
                  surface="steel_painted")  # 4 m along X, 320 mm deep
    for sign in (-1.0, 1.0):
        parts.append(box((0.03, 0.26, 0.36), (sign * 1.99, 0.0, 0.16), "steel_dark", "end_plate"))
        bolt_row(0.2, 3, sign * 1.97, 0.0, 0.16, radius=0.02, surface="hardened_steel",
                 axis="y", parts=parts, rotation_deg=(0.0, 90.0, 0.0))
    for i in range(3):
        x = -1.2 + i * 1.2
        parts.append(box((0.05, 0.24, 0.3), (x, 0.0, 0.16), "steel_dark", "stiffener"))
    obj = join(parts, "beam_4")
    return register_module(obj, "beam_4", category="structure", coverHeight=0.32,
                           blocksMovement=False,
                           description="4 m horizontal I beam with end plates")


def truss_4() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    for z in (0.06, 0.44):
        parts.append(box((4.0, 0.06, 0.06), (0.0, 0.0, z), "steel_painted", "chord"))
        parts.append(box((4.0, 0.06, 0.06), (0.0, 0.14, z), "steel_painted", "chord"))
    for i in range(5):
        x = -1.4 + i * 0.7
        parts.append(box((0.05, 0.2, 0.05), (x, 0.07, 0.25), "steel_dark", "vertical"))
        for sign in (-1.0, 1.0):
            parts.append(wedge((0.42, 0.05, 0.36), (x + sign * 0.21, 0.07, 0.25), "x",
                               "steel_dark", "diagonal"))
    for i in range(9):
        x = -1.6 + i * 0.4
        parts.append(box((0.04, 0.2, 0.04), (x, 0.07, 0.02), "steel_dark", "gusset"))
    obj = join(parts, "truss_4")
    return register_module(obj, "truss_4", category="structure", coverHeight=0.5,
                           blocksMovement=False, blocksSight=False,
                           description="4 m lattice roof truss")


def roof_panel_4() -> bpy.types.Object:
    parts = [box((4.0, 4.0, 0.03), (0.0, 0.0, 0.015), "sheet_metal", "deck")]
    for i in range(14):
        y = -1.86 + i * 0.286
        parts.append(box((4.0, 0.14, 0.05), (0.0, y, 0.05), "sheet_metal", "corrugation"))
    for sign in (-1.0, 1.0):
        parts.append(box((0.08, 4.0, 0.09), (sign * 1.96, 0.0, 0.045), "steel_dark", "edge_trim"))
        parts.append(box((4.0, 0.08, 0.09), (0.0, sign * 1.96, 0.045), "steel_dark", "edge_trim"))
    for i in range(4):
        for j in range(2):
            x = -1.4 + i * 0.95
            y = -1.0 + j * 2.0
            parts.append(cylinder(0.018, 0.018, 0.05, (x, y, 0.09), (0, 0, 0), 8,
                                  "hardened_steel", "roof_bolt"))
    obj = join(parts, "roof_panel_4")
    return register_module(obj, "roof_panel_4", category="structure", walkable=True,
                           walkHeight=0.1, blocksMovement=False,
                           description="4 x 4 m corrugated roof deck with trim and fixings")


def roof_skylight_4() -> bpy.types.Object:
    parts = [box((4.0, 4.0, 0.06), (0.0, 0.0, 0.03), "steel_dark", "kerb")]
    parts.append(box((3.6, 3.6, 0.07), (0.0, 0.0, 0.08), "sheet_metal", "kerb_inner"))
    for sign in (-1.0, 1.0):
        parts.append(box((3.5, 0.06, 0.09), (0.0, sign * 0.9, 0.14), "steel_dark", "glazing_bar"))
    parts.append(box((0.06, 3.5, 0.09), (0.0, 0.0, 0.14), "steel_dark", "glazing_bar"))
    glass = _glazing(3.4, 3.4, thickness=0.02, mullions=0)
    for part in glass:
        part.rotation_euler = (math.radians(90.0), 0.0, 0.0)
        part.location.z = 0.16
    parts += glass
    obj = join(parts, "roof_skylight_4")
    return register_module(obj, "roof_skylight_4", category="structure",
                           blocksMovement=False, tags=["glass", "destructible"],
                           destructible=True, integrity=8.0, coverHeight=0.2,
                           description="4 x 4 m roof light; the glazing can be shot out")


def pipe_rack_4() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    for sign in (-1.0, 1.0):
        parts.append(box((0.12, 0.5, 1.1), (sign * 1.5, 0.0, 0.55), "steel_painted", "support"))
        parts.append(box((0.2, 0.62, 0.05), (sign * 1.5, 0.0, 0.025), "concrete_worn", "footing"))
    radii = (0.11, 0.085, 0.065)
    heights = (1.22, 1.02, 1.36)
    for radius, z in zip(radii, heights):
        parts.append(cylinder(radius, radius, 4.0, (0.0, 0.0, z), (0.0, 90.0, 0.0), 16,
                              "steel_rust" if radius > 0.1 else "steel_dark", "pipe"))
        for sign in (-1.0, 1.0):
            parts += flange(radius + 0.035, 0.035, (sign * 1.55, 0.0, z),
                            (0.0, 90.0, 0.0), "steel_dark", bolts=6)
    parts.append(box((4.0, 0.05, 0.05), (0.0, 0.28, 0.9), "hardened_steel", "cable_tray"))
    obj = join(parts, "pipe_rack_4")
    return register_module(obj, "pipe_rack_4", category="structure", coverHeight=1.4,
                           blocksMovement=False,
                           description="4 m pipe rack: three process pipes on steel bents")


def vent_duct_4() -> bpy.types.Object:
    parts = [cylinder(0.28, 0.28, 4.0, (0.0, 0.0, 0.3), (0.0, 90.0, 0.0), 18,
                      "sheet_metal", "duct")]
    for x in (-1.4, 0.0, 1.4):
        parts += flange(0.32, 0.04, (x, 0.0, 0.3), (0.0, 90.0, 0.0), "sheet_metal", bolts=8)
    for x in (-1.8, 1.8):
        parts.append(box((0.1, 0.62, 0.62), (x, 0.0, 0.3), "steel_dark", "hanger"))
    parts.append(box((0.5, 0.36, 0.24), (0.7, 0.0, 0.62), "sheet_metal", "branch"))
    obj = join(parts, "vent_duct_4")
    return register_module(obj, "vent_duct_4", category="structure", coverHeight=0.62,
                           blocksMovement=False, blocksSight=False,
                           description="4 m round ventilation duct with flanges and a branch")


# --------------------------------------------------------------------------
# catwalks, stairs, ladders (the vertical route of both maps)
# --------------------------------------------------------------------------
def _deck(length: float, width: float, surface: str = "grating") -> List[bpy.types.Object]:
    parts = [box((length, width, DECK_THICK), (0.0, 0.0, DECK_THICK * 0.5), surface, "deck")]
    bars = max(3, int(width / 0.18))
    for i in range(bars):
        y = (i / (bars - 1) - 0.5) * (width - 0.12)
        parts.append(box((length - 0.04, 0.035, DECK_THICK + 0.03), (0.0, y, DECK_THICK * 0.5),
                         "steel_dark", "bearing_bar"))
    for sign in (-1.0, 1.0):
        parts.append(box((length, 0.1, 0.22), (0.0, sign * (width * 0.5 + 0.05), 0.02),
                         "steel_dark", "edge_beam"))
    return parts


def _rail(length: float, y: float, base_z: float, axis: str = "x",
          posts: Optional[int] = None, kick: bool = True) -> List[bpy.types.Object]:
    parts: List[bpy.types.Object] = []
    count = posts if posts is not None else max(2, int(round(length / 1.5)) + 1)
    top_z = base_z + RAIL_HEIGHT
    for i in range(count):
        t = 0.0 if count == 1 else (i / (count - 1) - 0.5) * length
        centre = (t, y, base_z + RAIL_HEIGHT * 0.5) if axis == "x" else (y, t, base_z + RAIL_HEIGHT * 0.5)
        size = (0.05, 0.05, RAIL_HEIGHT) if axis == "x" else (0.05, 0.05, RAIL_HEIGHT)
        parts.append(box(size, centre, "steel_painted", "rail_post"))
        parts.append(box((0.09, 0.09, 0.02) if axis == "x" else (0.09, 0.09, 0.02),
                         (centre[0], centre[1], base_z + 0.01), "steel_dark", "post_base"))
    for rail_z, thickness in ((top_z, 0.06), (base_z + RAIL_HEIGHT * 0.52, 0.04)):
        size = (length, 0.05, thickness) if axis == "x" else (0.05, length, thickness)
        centre = (0.0, y, rail_z) if axis == "x" else (y, 0.0, rail_z)
        parts.append(box(size, centre, "steel_painted", "rail"))
    if kick:
        size = (length, 0.025, 0.12) if axis == "x" else (0.025, length, 0.12)
        centre = (0.0, y, base_z + 0.06) if axis == "x" else (y, 0.0, base_z + 0.06)
        parts.append(box(size, centre, "hazard_paint", "kick_plate"))
    return parts


def _catwalk(length: float, module_id: str, description: str) -> bpy.types.Object:
    parts = _deck(length, DECK_WIDTH)
    for sign in (-1.0, 1.0):
        parts += _rail(length, sign * (DECK_WIDTH * 0.5 + 0.03), DECK_THICK)
    beams = max(1, int(round(length / 1.4)))
    for i in range(beams + 1):
        x = -length * 0.5 + length * (i / beams)
        parts.append(box((0.14, DECK_WIDTH + 0.2, 0.2), (x, 0.0, -0.16), "steel_dark",
                         "cross_beam"))
    for sign in (-1.0, 1.0):
        bolt_row(0.5, 2, sign * (length * 0.5 - 0.1), 0.0, -0.02, radius=0.02,
                 surface="hardened_steel", axis="y", parts=parts, rotation_deg=(0.0, 0.0, 0.0))
    obj = join(parts, module_id)
    return register_module(obj, module_id, category="verticality", walkable=True,
                           walkHeight=DECK_THICK, blocksMovement=False, blocksSight=True,
                           coverHeight=RAIL_HEIGHT + DECK_THICK, surfaceKind="SheetMetal",
                           description=description)


def catwalk_4() -> bpy.types.Object:
    return _catwalk(4.0, "catwalk_4",
                    "4 m catwalk run, 1.5 m wide grating with both rails")


def catwalk_2() -> bpy.types.Object:
    return _catwalk(2.0, "catwalk_2",
                    "2 m catwalk run: filler piece so gantries close exactly")


def catwalk_platform_4() -> bpy.types.Object:
    parts = _deck(4.0, 4.0)
    for sign in (-1.0, 1.0):
        parts += _rail(4.0, sign * 2.02, DECK_THICK)
    for x in (-1.4, 0.0, 1.4):
        parts.append(box((0.14, 4.2, 0.2), (x, 0.0, -0.16), "steel_dark", "cross_beam"))
    for y in (-1.4, 1.4):
        parts.append(box((4.2, 0.14, 0.2), (0.0, y, -0.16), "steel_dark", "cross_beam"))
    parts.append(box((1.2, 1.2, 0.02), (0.0, 0.0, DECK_THICK + 0.011), "hazard_paint",
                     "hatch_marking"))
    obj = join(parts, "catwalk_platform_4")
    return register_module(obj, "catwalk_platform_4", category="verticality", walkable=True,
                           walkHeight=DECK_THICK, blocksMovement=False, blocksSight=True,
                           coverHeight=RAIL_HEIGHT + DECK_THICK, surfaceKind="SheetMetal",
                           description="4 x 4 m catwalk landing, rails on the long sides")


def catwalk_corner() -> bpy.types.Object:
    parts = _deck(DECK_WIDTH, DECK_WIDTH)
    parts += _rail(DECK_WIDTH, -(DECK_WIDTH * 0.5 + 0.03), DECK_THICK, axis="x", posts=2)
    parts += _rail(DECK_WIDTH, -(DECK_WIDTH * 0.5 + 0.03), DECK_THICK, axis="y", posts=2)
    parts.append(box((DECK_WIDTH + 0.2, DECK_WIDTH + 0.2, 0.18), (0.0, 0.0, -0.14),
                     "steel_dark", "corner_beam"))
    obj = join(parts, "catwalk_corner")
    return register_module(obj, "catwalk_corner", category="verticality", walkable=True,
                           walkHeight=DECK_THICK, blocksMovement=False, blocksSight=True,
                           coverHeight=RAIL_HEIGHT + DECK_THICK, surfaceKind="SheetMetal",
                           description="90 degree catwalk corner, rails on the two outer edges")


def railing_4() -> bpy.types.Object:
    parts = _rail(4.0, 0.0, 0.0, axis="x", kick=False)
    parts.append(box((4.0, 0.12, 0.06), (0.0, 0.0, 0.03), "steel_dark", "base_plate"))
    obj = join(parts, "railing_4")
    return register_module(obj, "railing_4", category="verticality",
                           blocksMovement=True, blocksSight=False, coverHeight=RAIL_HEIGHT,
                           description="4 m edge railing for roofs, pits and landings")


def stairs_4() -> bpy.types.Object:
    rise, run, width = 4.0, 6.0, 1.2
    steps = 16
    step_rise = rise / steps
    step_run = run / steps
    parts: List[bpy.types.Object] = []
    for i in range(steps):
        x = -run * 0.5 + step_run * (i + 0.5)
        z = step_rise * (i + 0.5)
        parts.append(box((step_run, width, step_rise), (x, 0.0, z), "grating", "tread"))
        parts.append(box((step_run + 0.01, width, 0.03), (x, 0.0, z + step_rise * 0.5 + 0.015),
                         "steel_dark", "nosing"))
    for sign in (-1.0, 1.0):
        parts.append(wedge((run, 0.09, rise), (0.0, sign * (width * 0.5 + 0.06), rise * 0.5),
                           "x", "steel_painted", "stringer"))
        parts.append(slope_box((-run * 0.5, sign * (width * 0.5 + 0.06), 0.1),
                               (run * 0.5, sign * (width * 0.5 + 0.06), rise + 0.1),
                               0.05, 0.06, "steel_painted", "handrail"))
        parts.append(slope_box((-run * 0.5, sign * (width * 0.5 + 0.06), 0.62),
                               (run * 0.5, sign * (width * 0.5 + 0.06), rise + 0.62),
                               0.04, 0.04, "steel_painted", "mid_rail"))
        for i in range(5):
            t = i / 4
            x = -run * 0.5 + run * t
            z = rise * t
            parts.append(box((0.05, 0.05, 1.05), (x, sign * (width * 0.5 + 0.06), z + 0.55),
                             "steel_painted", "rail_post"))
    # No landing: the flight is exactly 6 m of run so the map can butt a
    # catwalk_platform_4 against its top and keep the module grid honest.
    parts.append(box((0.16, width + 0.24, 0.24), (run * 0.5 + 0.06, 0.0, rise - 0.12),
                     "steel_dark", "top_nosing_beam"))
    obj = join(parts, "stairs_4")
    return register_module(obj, "stairs_4", category="verticality", walkable=True,
                           climbable=True, walkHeight=rise, blocksMovement=False,
                           blocksSight=False, coverHeight=rise + RAIL_HEIGHT,
                           surfaceKind="SheetMetal",
                           description="Steel stair: 4 m rise over a 6 m run, both handrails")


def ladder_4() -> bpy.types.Object:
    parts = ladder(4.0, 0.45, 0.3, start=(0.0, 0.0, 0.0))
    for z in (0.4, 2.0, 3.6):
        for sign in (-1.0, 1.0):
            parts.append(box((0.3, 0.05, 0.05), (sign * 0.14, 0.0, z), "steel_dark", "standoff"))
    for i in range(6):
        z = 1.2 + i * 0.5
        parts.append(box((0.04, 0.62, 0.04), (0.0, 0.0, z), "steel_painted", "cage_hoop"))
    obj = join(parts, "ladder_4")
    return register_module(obj, "ladder_4", category="verticality", climbable=True,
                           walkable=False, blocksMovement=False, blocksSight=False,
                           coverHeight=4.0,
                           description="4 m caged fixed ladder (maintenance access)")


# --------------------------------------------------------------------------
# doors, gates, fences, barriers
# --------------------------------------------------------------------------
def door_sliding_2() -> bpy.types.Object:
    parts = [box((2.6, 0.16, 2.7), (0.0, 0.0, 1.35), "steel_dark", "frame")]
    parts.append(box((2.3, 0.09, 2.5), (0.0, 0.0, 1.3), "steel_painted", "leaf"))
    for y_offset in (-0.75, 0.75):
        parts.append(box((0.08, 0.11, 2.4), (y_offset, 0.0, 1.3), "steel_dark", "stiffener"))
    parts.append(box((2.2, 0.1, 0.08), (0.0, 0.0, 2.42), "hardened_steel", "top_rail"))
    for x in (-0.9, -0.3, 0.3, 0.9):
        parts.append(cylinder(0.05, 0.05, 0.12, (x, 0.0, 2.46), (0, 0, 0), 10,
                              "hardened_steel", "roller"))
    parts.append(box((0.18, 0.16, 0.3), (1.0, 0.1, 1.15), "hardened_steel", "handle"))
    parts.append(box((0.5, 0.02, 0.5), (0.0, 0.085, 1.9), "hazard_paint", "hazard_plate"))
    obj = join(parts, "door_sliding_2")
    return register_module(obj, "door_sliding_2", category="door", interactive=True,
                           blocksMovement=True, blocksSight=True, coverHeight=2.7,
                           tags=["door"],
                           description="2.4 m sliding steel door on a top rail (interactive)")


def gate_yard_6() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    for sign in (-1.0, 1.0):
        parts.append(box((0.16, 0.16, 3.2), (sign * 3.0, 0.0, 1.6), "steel_painted", "gate_post"))
        parts.append(box((0.34, 0.34, 0.12), (sign * 3.0, 0.0, 0.06), "concrete_worn", "post_foot"))
        parts.append(box((2.84, 0.09, 2.9), (sign * 1.5, 0.0, 1.5), "steel_painted", "leaf_frame"))
        parts.append(box((2.7, 0.05, 0.09), (sign * 1.5, 0.0, 2.94), "steel_dark", "top_rail"))
        for i in range(9):
            x = sign * 1.5 + (i / 8 - 0.5) * 2.6
            parts.append(box((0.03, 0.03, 2.7), (x, 0.0, 1.5), "steel_rust", "picket"))
        for z in (0.6, 1.5, 2.4):
            parts.append(box((2.7, 0.035, 0.035), (sign * 1.5, 0.0, z), "steel_rust", "rail"))
        parts.append(wedge((2.7, 0.05, 0.5), (sign * 1.5, 0.0, 2.7), "x", "steel_dark", "brace"))
    parts.append(box((0.12, 0.14, 0.5), (0.0, 0.0, 1.1), "hardened_steel", "latch"))
    parts.append(cylinder(0.05, 0.05, 0.16, (0.0, 0.12, 1.1), (90, 0, 0), 10,
                          "hardened_steel", "latch_pin"))
    obj = join(parts, "gate_yard_6")
    return register_module(obj, "gate_yard_6", category="door", interactive=True,
                           destructible=True, integrity=120.0,
                           blocksMovement=True, blocksSight=False, coverHeight=3.2,
                           tags=["gate", "objective:breach_gate"],
                           description="6 m double leaf yard gate; breachable co-op objective")


def fence_4() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    for sign in (-1.0, 1.0):
        parts.append(box((0.08, 0.08, 2.1), (sign * 1.96, 0.0, 1.05), "steel_painted", "post"))
        parts.append(box((0.2, 0.2, 0.08), (sign * 1.96, 0.0, 0.04), "concrete_worn", "foot"))
    parts.append(box((4.0, 0.05, 0.05), (0.0, 0.0, 2.06), "steel_painted", "top_rail"))
    parts.append(box((4.0, 0.05, 0.05), (0.0, 0.0, 0.08), "steel_painted", "bottom_rail"))
    for i in range(12):
        x = -1.83 + i * 0.333
        parts.append(box((0.022, 0.022, 1.95), (x, 0.0, 1.05), "steel_rust", "mesh_bar"))
    for z in (0.5, 1.0, 1.5):
        parts.append(box((3.9, 0.018, 0.018), (0.0, 0.0, z), "steel_rust", "mesh_wire"))
    obj = join(parts, "fence_4")
    return register_module(obj, "fence_4", category="fence", blocksMovement=True,
                           blocksSight=False, coverHeight=2.1,
                           description="4 m x 2.1 m palisade fence panel")


def fence_corner() -> bpy.types.Object:
    parts: List[bpy.types.Object] = []
    parts.append(box((0.1, 0.1, 2.1), (0.0, 0.0, 1.05), "steel_painted", "post"))
    parts.append(box((0.22, 0.22, 0.08), (0.0, 0.0, 0.04), "concrete_worn", "foot"))
    for axis in ("x", "y"):
        parts.append(box((0.9, 0.05, 0.05) if axis == "x" else (0.05, 0.9, 0.05),
                         (0.45 if axis == "x" else 0.0, 0.0 if axis == "x" else -0.45, 2.06),
                         "steel_painted", "rail"))
        for i in range(4):
            t = 0.12 + i * 0.24
            if axis == "x":
                parts.append(box((0.022, 0.022, 1.95), (t, 0.0, 1.05), "steel_rust", "bar"))
            else:
                parts.append(box((0.022, 0.022, 1.95), (0.0, -t, 1.05), "steel_rust", "bar"))
    obj = join(parts, "fence_corner")
    return register_module(obj, "fence_corner", category="fence", blocksMovement=True,
                           blocksSight=False, coverHeight=2.1,
                           description="Fence corner post with two short panel stubs")


def barrier_concrete_2() -> bpy.types.Object:
    parts = [box((2.0, 0.42, 0.14), (0.0, 0.0, 0.07), "concrete_worn", "base")]
    parts.append(box((1.94, 0.3, 0.5), (0.0, 0.0, 0.36), "concrete_raw", "body"))
    parts.append(wedge((1.9, 0.24, 0.24), (0.0, 0.0, 0.72), "y", "concrete_raw", "top"))
    for sign in (-1.0, 1.0):
        parts.append(cylinder(0.03, 0.03, 0.1, (sign * 0.6, 0.0, 0.66), (0, 0, 0), 8,
                              "hardened_steel", "lift_pin"))
    parts.append(box((0.5, 0.02, 0.16), (0.0, 0.16, 0.42), "hazard_paint", "marking"))
    obj = join(parts, "barrier_concrete_2")
    return register_module(obj, "barrier_concrete_2", category="cover", coverHeight=0.86,
                           tags=["cover"],
                           description="2 m jersey barrier: standing cover for the yard")
