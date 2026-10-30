#!/usr/bin/env python3
"""Operation Silent Kiln: the released co-operative map.

A night raid on a Veransk Militia Front ferroalloy works, 70 x 60 m, built from the
same modular kit as the free-for-all map so both share one art identity.  The
mission chain in ``Definitions/missions.json`` walks the team through this space and
every step is a navigation marker placed here - ``build_maps.py`` refuses to write
the map if an objective points at a marker that does not exist:

    reach_yard        nav_rail_spur        the cut rail spur south of the works,
                                           where the team walks in (six slots)
    breach_gate       nav_yard_gate        the 6 m yard gate in the south wall,
                                           destructible - or go round over the
                                           concrete loading ramp west of it
    sabotage_fuel     nav_fuel_line        the fuel line skid in the east pipe yard,
      line (x2)                            twice, from either side of the rack
    retrieve_intel    nav_control_room     the terminal in the west control block
    hold_kiln_hall    nav_kiln_hall        hold the kiln hall 45 s, which arms the
                                           extraction (unlocksExtraction)
    reach_extraction  nav_extraction_pad   the marked pad north east of the hall
    extract           nav_extraction_pad   stand on it while the board runs

Militia patrols use the same graph, so alarm states and flank routes are driven by
the same markers the objectives use.  Two to four operators insert together at the
spur, which is why the spawn slots are grouped instead of spread around the map.
"""
from __future__ import annotations

import math
from typing import Dict, List, Sequence, Tuple

import bpy

import kordon_art as art
import kordon_layout as lay

MAP_ID = "coop_silent_kiln"
MAP_NAME = "Operation Silent Kiln"
SCENE_NAME = "COOP_SilentKiln"
BOUNDS = (-35.0, -30.0, 35.0, 30.0)

HALL = (-14.0, 12.0, 14.0, 28.0)         # kiln hall, 28 x 16 m
CONTROL = (-30.0, -2.0, -14.0, 14.0)     # control block, 16 x 16 m, two storeys
PIPE_YARD = (16.0, -8.0, 32.0, 8.0)      # fuel line skid yard
SOUTH_WALL_Y = -10.0
RAIL_Y = -24.0
KILN = (0.0, 20.0)
RING = 6.21
EXTRACTION = (26.0, 24.0)
INSERTION = (0.0, -26.0)
GANTRY_Z = 4.0
DECK = 4.12
CONTROL_DECK_Z = 3.6
CONTROL_ROOF_Z = 7.2
ROOF_Z = 8.0
ROOF_WALK = 8.1

_MANIFEST: Dict[str, dict] = {}


def set_manifest(manifest: Dict[str, dict]) -> None:
    global _MANIFEST
    _MANIFEST = manifest
    lay.set_module_sizes({name: entry["size"] for name, entry in manifest.items()})


# --------------------------------------------------------------------------
# shells
# --------------------------------------------------------------------------
def build_shell_ground() -> bpy.types.Object:
    parts = [
        art.box((70.0, 60.0, 0.4), (0.0, 0.0, -0.2), "gravel", "ground"),
        art.box((28.4, 16.4, 0.1), (0.0, 20.0, 0.03), "concrete_raw", "hall_floor"),
        art.box((16.4, 16.4, 0.1), (-22.0, 6.0, 0.03), "concrete_worn", "control_floor"),
        art.box((16.4, 16.4, 0.08), (24.0, 0.0, 0.02), "concrete_worn", "pipe_pad"),
        art.box((48.4, 4.6, 0.12), (0.0, RAIL_Y, 0.04), "gravel", "ballast_bed"),
        art.box((10.0, 12.0, 0.06), (0.0, -16.0, 0.02), "concrete_worn", "gate_apron"),
        art.box((8.4, 8.4, 0.07), (EXTRACTION[0], EXTRACTION[1], 0.03), "concrete_worn",
                "pad_base"),
        art.box((1.2, 8.0, 0.03), (INSERTION[0], -20.0, 0.06), "hazard_paint",
                "insertion_lane"),
        art.box((1.6, 1.6, 0.02), (KILN[0], KILN[1], 0.09), "hazard_paint", "kiln_marker"),
        art.box((0.24, 10.0, 0.02), (-5.4, -15.0, 0.05), "hazard_paint", "walkway_line"),
        art.box((0.24, 10.0, 0.02), (5.4, -15.0, 0.05), "hazard_paint", "walkway_line"),
    ]
    for i in range(4):
        angle = i * math.pi * 0.5 + math.pi * 0.25
        part = art.box((2.2, 0.2, 0.02), (KILN[0] + math.cos(angle) * 5.0,
                                          KILN[1] + math.sin(angle) * 5.0, 0.09),
                       "hazard_paint", "kiln_hazard")
        part.rotation_euler = (0.0, 0.0, angle)
        parts.append(part)
    obj = art.join(parts, f"{MAP_ID}_ground")
    art.shade_flat(obj)
    art.bevel(obj, 0.015)
    art.unwrap(obj)
    art.set_origin_bottom_center(obj)
    return obj


def build_shell_ridge() -> bpy.types.Object:
    """Rock ridge north and west: silhouette, and a hard boundary for the fight."""
    parts = []
    for i in range(14):
        x = -34.0 + i * 5.0
        height = 2.6 + 1.8 * abs(math.sin(i * 1.7))
        parts.append(art.wedge((5.6, 3.2, height), (x, 29.4, height * 0.5 - 0.4), "y",
                               "concrete_raw", f"ridge_n{i}"))
    for i in range(11):
        y = -28.0 + i * 5.4
        height = 2.2 + 1.5 * abs(math.cos(i * 2.1))
        parts.append(art.wedge((3.2, 5.8, height), (-34.2, y, height * 0.5 - 0.4), "x",
                               "concrete_raw", f"ridge_w{i}"))
    obj = art.join(parts, f"{MAP_ID}_ridge")
    art.shade_flat(obj)
    art.bevel(obj, 0.02)
    art.unwrap(obj)
    art.set_origin_bottom_center(obj)
    return obj


SHELLS = {
    "ground": dict(builder=build_shell_ground, position=(0.0, 0.0, 0.0), yaw=0.0,
                   blocksSight=False, blocksMovement=False, walkable=True,
                   description="Terrain shell: ballast, aprons, pad, hazard paint"),
    "ridge": dict(builder=build_shell_ridge, position=(0.0, 0.0, 0.0), yaw=0.0,
                  blocksSight=True, blocksMovement=True,
                  description="Rock ridge closing the north and west edges"),
}


# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------
def insert(run_start: Sequence[float], run_end: Sequence[float], distance: float,
           module: str, out: List[dict], z: float = 0.0, tag: str = "") -> dict:
    unit = lay.unit_vector(run_start, run_end)
    point = art.mathutils.Vector((run_start[0], run_start[1], z)) + unit * distance
    entry = lay.placement(module, (point.x, point.y, z), lay.yaw_for(run_start, run_end),
                          tag)
    out.append(entry)
    return entry


def catwalk_run(start: Sequence[float], end: Sequence[float], out: List[dict],
                z: float = GANTRY_Z, fillers: Sequence[str] = (), tag: str = "catwalk"):
    return lay.side_run("catwalk_4", start, end, out, z=z, fillers=fillers, seal=False,
                        tag=tag)


def roof_tiles(out: List[dict], region: Sequence[float], z: float, skylights=(), omit=(),
               tag: str = "roof") -> int:
    placed = 0
    x = region[0] + 2.0
    while x <= region[2] - 2.0 + 1e-4:
        y = region[1] + 2.0
        while y <= region[3] - 2.0 + 1e-4:
            if (x, y) not in omit:
                module = "roof_skylight_4" if (x, y) in skylights else "roof_panel_4"
                out.append(lay.placement(module, (x, y, z), 0.0, tag))
                placed += 1
            y += 4.0
        x += 4.0
    return placed


# --------------------------------------------------------------------------
# the map
# --------------------------------------------------------------------------
def build_placements() -> Tuple[List[dict], Dict[str, list]]:
    out: List[dict] = []

    # ---- perimeter -------------------------------------------------------
    lay.side_run("fence_4", (-35.0, -30.0), (35.0, -30.0), out, fillers=("fence_4",),
                 openings=[(35.0, 9.0)], tag="perimeter")   # gap where the spur is cut
    lay.side_run("fence_4", (35.0, -30.0), (35.0, 30.0), out, fillers=("fence_4",),
                 tag="perimeter")
    lay.side_run("wall_4", (-35.0, 30.0), (35.0, 30.0), out, tag="perimeter")
    lay.side_run("wall_4", (-35.0, -30.0), (-35.0, 30.0), out, tag="perimeter")
    for corner in ((-35.0, 30.0), (35.0, 30.0)):
        out.append(lay.placement("corner_outer", corner, 0.0, "perimeter"))
    for corner in ((35.0, -30.0),):
        out.append(lay.placement("fence_corner", corner, 0.0, "perimeter"))

    # ---- south yard: rail spur, insertion, the gate wall -----------------
    for i in range(12):
        out.append(lay.placement("rail_track_4", (-30.0 + i * 4.0, RAIL_Y, 0.0), 0.0,
                                 "rail"))
    out.append(lay.placement("rail_wagon_8", (-12.0, RAIL_Y, 0.3), 0.0, "rail"))
    out.append(lay.placement("rail_wagon_8", (8.0, RAIL_Y, 0.3), 0.0, "rail"))
    lay.side_run("wall_4", (-20.0, SOUTH_WALL_Y), (20.0, SOUTH_WALL_Y), out,
                 openings=[(20.0, 7.4)], tag="gate_wall")
    insert((-20.0, SOUTH_WALL_Y), (20.0, SOUTH_WALL_Y), 20.0, "gate_yard_6", out,
           tag="yard_gate")
    lay.side_run("fence_4", (-34.0, SOUTH_WALL_Y), (-20.0, SOUTH_WALL_Y), out,
                 fillers=("fence_4",), tag="gate_wall")
    lay.side_run("fence_4", (20.0, SOUTH_WALL_Y), (34.0, SOUTH_WALL_Y), out,
                 fillers=("fence_4",), tag="gate_wall")
    # loading ramp west of the gate: the quiet second way in
    lay.side_run("barrier_concrete_2", (-30.0, -14.0), (-22.0, -14.0), out, fillers=(),
                 tag="ramp")
    lay.side_run("barrier_concrete_2", (-30.0, -12.0), (-22.0, -12.0), out, fillers=(),
                 tag="ramp")
    out.append(lay.placement("crate_stack", (-26.0, -13.0, 0.0), 0.0, "ramp"))
    out.append(lay.placement("sandbag_wall", (-18.0, -16.0, 0.0), 20.0, "yard_cover"))
    out.append(lay.placement("sandbag_wall", (16.0, -18.0, 0.0), 340.0, "yard_cover"))
    out.append(lay.placement("crate_stack", (-6.0, -16.0, 0.0), 8.0, "yard_cover"))
    out.append(lay.placement("crate_1", (4.0, -14.0, 0.0), 0.0, "yard_cover"))
    out.append(lay.placement("barrel_cluster", (10.0, -13.0, 0.0), 0.0, "yard_cover"))
    out.append(lay.placement("concrete_block_2", (-2.0, -20.0, 0.0), 90.0, "yard_cover"))
    out.append(lay.placement("pallet_stack", (12.0, -17.0, 0.0), 0.0, "yard_cover"))
    out.append(lay.placement("light_mast_8", (-24.0, -20.0, 0.0), 0.0, "light"))
    out.append(lay.placement("light_mast_8", (24.0, -20.0, 0.0), 0.0, "light"))
    out.append(lay.placement("sign_plate", (-6.4, -9.6, 2.2), 0.0, "signage"))

    # ---- kiln hall (objective: hold) -------------------------------------
    hall_openings = {
        "south": [(6.0, 4.1), (14.0, 6.4), (22.0, 4.1)],
        "north": [(14.0, 4.6)],
        "west": [(10.0, 4.1)],
        "east": [(4.0, 4.1), (12.0, 4.6)],
    }
    lay.room((HALL[0], HALL[1]), (HALL[2], HALL[3]), out, wall="wall_4", z=0.0, tiers=1,
             openings=hall_openings, tag="hall")
    lay.room((HALL[0], HALL[1]), (HALL[2], HALL[3]), out, wall="wall_4_window", z=3.6,
             tiers=1, openings={}, tag="hall_upper")
    lay.room((HALL[0], HALL[1]), (HALL[2], HALL[3]), out, wall="wall_low_4", z=7.2,
             tiers=1, openings={}, corner="corner_inner", tag="hall_parapet")
    insert((HALL[0], HALL[1]), (HALL[2], HALL[1]), 6.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[0], HALL[1]), (HALL[2], HALL[1]), 22.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[0], HALL[3]), (HALL[0], HALL[1]), 10.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[2], HALL[1]), (HALL[2], HALL[3]), 4.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[2], HALL[3]), (HALL[0], HALL[3]), 14.0, "door_sliding_2", out,
           tag="hall_door")
    roof_tiles(out, HALL, ROOF_Z, skylights={(-8.0, 14.0), (4.0, 26.0)}, tag="hall_roof")
    out.append(lay.placement("furnace_kiln", (KILN[0], KILN[1], 0.1), 0.0, "landmark"))
    out.append(lay.placement("chimney_stack", (-20.0, 24.0, 0.0), 0.0, "landmark"))
    out.append(lay.placement("slag_pot", (-8.0, 15.0, 0.1), 20.0, "plant"))
    out.append(lay.placement("hopper_3", (10.0, 26.0, 0.1), 200.0, "plant"))
    out.append(lay.placement("conveyor_6", (-8.0, 26.0, 0.0), 0.0, "plant"))
    out.append(lay.placement("control_desk", (11.0, 14.0, 0.1), 180.0, "plant"))
    out.append(lay.placement("pipe_valve", (-12.0, 20.0, 0.1), 90.0, "plant"))
    out.append(lay.placement("transformer_box", (-12.0, 14.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("crate_stack", (6.0, 15.0, 0.0), 0.0, "hall_cover"))
    out.append(lay.placement("sandbag_wall", (-4.0, 25.0, 0.0), 180.0, "hall_cover"))
    out.append(lay.placement("barrier_concrete_2", (8.0, 24.0, 0.0), 90.0, "hall_cover"))
    out.append(lay.placement("barrel_cluster", (-10.0, 24.0, 0.0), 0.0, "hall_cover"))
    for pillar in ((-11.0, 16.0), (11.0, 16.0), (-11.0, 24.0), (11.0, 24.0)):
        out.append(lay.placement("pillar_1", pillar, 0.0, "hall_structure"))
    for x in (-10.0, -2.0, 6.0):
        out.append(lay.placement("beam_4", (x, 20.0, 6.9), 0.0, "hall_structure"))
    for position in ((-13.6, 18.0, 3.2), (13.6, 18.0, 3.2), (0.0, 12.4, 3.2),
                     (0.0, 27.6, 3.2)):
        out.append(lay.placement("floodlight_wall", position, 0.0, "light"))

    # gantry ring around the kiln, one stair up from the hall floor
    for corner in ((KILN[0] - RING, KILN[1] - RING), (KILN[0] + RING, KILN[1] - RING),
                   (KILN[0] - RING, KILN[1] + RING), (KILN[0] + RING, KILN[1] + RING)):
        out.append(lay.placement("catwalk_corner", corner, GANTRY_Z, "gantry"))
    catwalk_run((KILN[0] - RING, KILN[1] - RING), (KILN[0] + RING, KILN[1] - RING), out,
                tag="gantry")
    catwalk_run((KILN[0] + RING, KILN[1] + RING), (KILN[0] - RING, KILN[1] + RING), out,
                tag="gantry")
    catwalk_run((KILN[0] - RING, KILN[1] - RING), (KILN[0] - RING, KILN[1] + RING), out,
                tag="gantry")
    catwalk_run((KILN[0] + RING, KILN[1] + RING), (KILN[0] + RING, KILN[1] - RING), out,
                tag="gantry")
    out.append(lay.placement("stairs_4", (KILN[0] - RING - 3.08, KILN[1] - 4.14, 0.0), 0.0,
                             "gantry_stairs"))
    out.append(lay.placement("column_steel_4", (KILN[0] - RING - 5.0, KILN[1] - 4.14), 0.0,
                             "hall_structure"))
    # maintenance ladders: onto the kiln ring, and up to the hall roof
    out.append(lay.placement("ladder_4", (KILN[0] + RING + 1.15, KILN[1] + 3.0, 0.0), 90.0,
                             "hall_structure"))
    out.append(lay.placement("ladder_4", (HALL[2] + 0.55, 24.0, 0.0), 90.0,
                             "hall_structure"))
    out.append(lay.placement("ladder_4", (HALL[2] + 0.55, 24.0, 4.05), 90.0,
                             "hall_structure"))

    # ---- control block (objective: intel) --------------------------------
    control_openings = {
        "south": [(8.0, 4.1)],
        "east": [(8.0, 4.6)],
        "north": [(8.0, 4.1)],
        "west": [],
    }
    lay.room((CONTROL[0], CONTROL[1]), (CONTROL[2], CONTROL[3]), out, wall="wall_4",
             z=0.0, tiers=1, openings=control_openings, tag="control")
    lay.room((CONTROL[0], CONTROL[1]), (CONTROL[2], CONTROL[3]), out, wall="wall_4_window",
             z=3.6, tiers=1, openings={}, tag="control_upper")
    insert((CONTROL[0], CONTROL[1]), (CONTROL[2], CONTROL[1]), 8.0, "wall_4_door", out,
           tag="control_door")
    insert((CONTROL[0], CONTROL[3]), (CONTROL[2], CONTROL[3]), 8.0, "wall_4_door", out,
           tag="control_door")
    lay.grid_fill("floor_slab_4", (CONTROL[0], CONTROL[1]), (CONTROL[2], CONTROL[3]), out,
                  z=CONTROL_DECK_Z, skip=lambda x, y: abs(x + 23.0) < 3.0 and y < 7.0,
                  tag="control_deck")
    lay.side_run("railing_4", (CONTROL[0] + 2.0, CONTROL[1] + 2.0),
                 (CONTROL[2] - 2.0, CONTROL[1] + 2.0), out, z=CONTROL_DECK_Z + 0.12,
                 fillers=(), seal=False, tag="control_deck")
    out.append(lay.placement("stairs_4", (-23.0, 2.0, 0.0), 90.0, "control_stairs"))
    roof_tiles(out, CONTROL, CONTROL_ROOF_Z, tag="control_roof")
    out.append(lay.placement("ladder_4", (CONTROL[0] - 0.5, 4.0, 0.0), 270.0,
                             "control_access"))
    out.append(lay.placement("ladder_4", (CONTROL[0] - 0.5, 4.0, 4.05), 270.0,
                             "control_access"))
    out.append(lay.placement("intel_terminal", (-23.0, 9.0, 0.1), 180.0, "objective"))
    out.append(lay.placement("control_desk", (-27.0, 11.0, 0.1), 90.0, "control_prop"))
    out.append(lay.placement("locker_row", (-17.0, 2.0, 0.1), 270.0, "control_prop"))
    out.append(lay.placement("transformer_box", (-28.0, 2.0, 0.1), 0.0, "control_prop"))
    out.append(lay.placement("cable_spool", (-18.0, 12.0, 0.0), 0.0, "control_prop"))
    out.append(lay.placement("crate_stack", (-26.0, 4.0, 0.0), 12.0, "control_cover"))
    out.append(lay.placement("control_desk", (-27.0, 6.0, CONTROL_DECK_Z + 0.1), 90.0,
                             "control_prop"))
    out.append(lay.placement("locker_row", (-18.0, 10.0, CONTROL_DECK_Z + 0.1), 180.0,
                             "control_prop"))
    out.append(lay.placement("light_mast_8", (-32.0, -6.0, 0.0), 0.0, "light"))
    out.append(lay.placement("floodlight_wall", (CONTROL[2] - 0.4, 6.0, 3.0), 270.0,
                             "light"))

    # ---- east pipe yard (objective: fuel line) ---------------------------
    for y in (-4.0, 4.0):
        lay.side_run("pipe_rack_4", (PIPE_YARD[0] + 1.0, y), (PIPE_YARD[2] - 1.0, y), out,
                     fillers=(), tag="pipes")
    lay.side_run("vent_duct_4", (PIPE_YARD[0] + 1.0, 0.0), (PIPE_YARD[2] - 1.0, 0.0), out,
                 z=2.8, fillers=(), tag="pipes")
    out.append(lay.placement("fuel_line", (24.0, 0.0, 0.0), 0.0, "objective"))
    out.append(lay.placement("hopper_3", (30.0, -6.0, 0.1), 90.0, "plant"))
    out.append(lay.placement("pipe_valve", (18.0, -4.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("pipe_valve", (30.0, 4.0, 0.1), 180.0, "plant"))
    out.append(lay.placement("transformer_box", (18.0, 6.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("dumpster_2", (31.0, -2.0, 0.0), 90.0, "yard_prop"))
    catwalk_run((18.0, -7.0), (18.0, 9.56), out, tag="east_catwalk")
    out.append(lay.placement("stairs_4", (18.0, -3.92, 0.0), 90.0, "east_stairs"))
    out.append(lay.placement("stairs_4", (18.0, 6.5, 0.0), 270.0, "east_stairs"))
    out.append(lay.placement("column_steel_4", (18.0, -6.0), 0.0, "east_structure"))
    out.append(lay.placement("column_steel_4", (18.0, 6.0), 0.0, "east_structure"))
    out.append(lay.placement("light_mast_8", (32.0, 8.0, 0.0), 0.0, "light"))
    lay.side_run("sandbag_wall", (22.0, -6.0), (26.0, -6.0), out, fillers=(),
                 tag="yard_cover")
    out.append(lay.placement("crate_stack", (20.0, 6.0, 0.0), 0.0, "yard_cover"))
    out.append(lay.placement("barrel_cluster", (28.0, 6.0, 0.0), 0.0, "yard_cover"))

    # ---- extraction pad (north east) -------------------------------------
    out.append(lay.placement("extraction_pad", EXTRACTION, 0.0, "objective"))
    out.append(lay.placement("floodlight_wall", (EXTRACTION[0] - 4.6, EXTRACTION[1], 2.6),
                             90.0, "light"))
    out.append(lay.placement("floodlight_wall", (EXTRACTION[0], EXTRACTION[1] + 4.6, 2.6),
                             180.0, "light"))
    lay.side_run("barrier_concrete_2", (EXTRACTION[0] - 6.0, EXTRACTION[1] - 6.0),
                 (EXTRACTION[0] + 6.0, EXTRACTION[1] - 6.0), out, fillers=(), tag="pad_edge")
    lay.side_run("barrier_concrete_2", (EXTRACTION[0] - 6.0, EXTRACTION[1] + 6.0),
                 (EXTRACTION[0] + 6.0, EXTRACTION[1] + 6.0), out, fillers=(), tag="pad_edge")
    out.append(lay.placement("sandbag_wall", (EXTRACTION[0] + 6.0, EXTRACTION[1], 0.0),
                             90.0, "pad_cover"))
    out.append(lay.placement("crate_stack", (EXTRACTION[0] - 6.0, EXTRACTION[1] + 2.0, 0.0),
                             0.0, "pad_cover"))
    out.append(lay.placement("sign_plate", (EXTRACTION[0] - 6.4, EXTRACTION[1] - 2.0, 2.2),
                             270.0, "signage"))
    out.append(lay.placement("light_mast_8", (EXTRACTION[0] + 6.0, EXTRACTION[1] + 6.0, 0.0),
                             0.0, "light"))

    # ---- yard between the gate and the hall ------------------------------
    lay.scatter(("crate_1", "crate_stack", "barrel_cluster", "pallet_stack",
                 "concrete_block_2", "sandbag_wall", "rubble_pile"),
                (-18.0, -8.0, 18.0, 10.0), 22, 5150, out, spacing=3.4,
                avoid=[(-6.0, -4.0, 6.0, 4.0), (-15.5, 10.5, 15.5, 29.5),
                       (-31.5, -3.5, -12.5, 15.5), (14.5, -9.5, 33.5, 9.5)])
    out.append(lay.placement("dumpster_2", (-10.0, 8.0, 0.0), 0.0, "yard_prop"))
    out.append(lay.placement("glass_panel_2", (6.0, 8.0, 0.0), 20.0, "yard_prop"))
    out.append(lay.placement("light_mast_8", (0.0, 4.0, 0.0), 0.0, "light"))
    out.append(lay.placement("light_mast_8", (12.0, 22.0, 0.0), 0.0, "light"))

    return out, {}


def build_spawns() -> List[dict]:
    """Six insertion slots beside the cut rail spur - the team walks in together."""
    spawns = []
    # the offsets keep every slot clear of the rail wagons: a spawn point inside a
    # wagon would be sealed off from the graph by the wagon's own shell
    for index, (dx, dy) in enumerate(((0.0, 0.0), (-2.2, -1.2), (2.2, -1.2),
                                      (-4.4, -3.4), (4.4, -3.4), (0.0, -2.8)), start=1):
        spawns.append({
            "id": f"insert_{index}",
            "zone": "I",
            "position": [round(INSERTION[0] + dx, 2), round(INSERTION[1] + dy, 2), 0.05],
            "yaw": 0.0,
            "marker": f"insert_{index}",
        })
    return spawns


def build_nav() -> Tuple[List[dict], List[dict]]:
    nodes: List[dict] = []

    def next_id() -> int:
        return max([node["id"] for node in nodes], default=0) + 1

    lay.nav_zone(nodes, (-32.0, -26.0, 32.0, 10.0), 0.15, "Exterior", step=6.0, cover=0.25)
    lay.nav_zone(nodes, (-32.0, 16.0, -16.0, 28.0), 0.15, "Exterior", step=6.0, cover=0.2)
    lay.nav_zone(nodes, (16.0, 12.0, 32.0, 19.0), 0.15, "Exterior", step=6.0, cover=0.2)
    lay.nav_zone(nodes, (HALL[0] + 1.6, HALL[1] + 1.6, HALL[2] - 1.6, HALL[3] - 1.6), 0.15,
                 "Indoor", step=5.0, cover=0.35)
    lay.nav_zone(nodes, (CONTROL[0] + 1.6, CONTROL[1] + 1.6, CONTROL[2] - 1.6,
                         CONTROL[3] - 1.6), 0.15, "Indoor", step=5.0, cover=0.3)
    lay.nav_zone(nodes, (PIPE_YARD[0] + 1.2, PIPE_YARD[1] + 1.2, PIPE_YARD[2] - 1.2,
                         PIPE_YARD[3] - 1.2), 0.15, "Exterior", step=5.0, cover=0.3)
    lay.nav_zone(nodes, (EXTRACTION[0] - 5.0, EXTRACTION[1] - 5.0, EXTRACTION[0] + 5.0,
                         EXTRACTION[1] + 5.0), 0.25, "Extraction", step=5.0, cover=0.1)
    lay.nav_zone(nodes, (-32.0, -29.5, 32.0, -24.5), 0.15, "Spawn", step=8.0, cover=0.3)
    lay.nav_zone(nodes, (-20.0, 12.0, 20.0, 28.0), 0.15, "Indoor", step=10.0, cover=0.2)
    # elevated
    lay.nav_zone(nodes, (-5.0, KILN[1] - RING - 1.0, 5.0, KILN[1] - RING + 1.0), DECK,
                 "Catwalk", step=4.0, cover=0.5)
    lay.nav_zone(nodes, (-5.0, KILN[1] + RING - 1.0, 5.0, KILN[1] + RING + 1.0), DECK,
                 "Catwalk", step=4.0, cover=0.5)
    lay.nav_zone(nodes, (-RING - 1.0, 15.0, -RING + 1.0, 25.0), DECK, "Catwalk", step=3.0,
                 cover=0.5)
    lay.nav_zone(nodes, (RING - 1.0, 15.0, RING + 1.0, 25.0), DECK, "Catwalk", step=3.0,
                 cover=0.5)
    lay.nav_zone(nodes, (17.2, -6.0, 18.8, 8.0), DECK, "Catwalk", step=4.0, cover=0.45)
    lay.nav_zone(nodes, (CONTROL[0] + 1.6, CONTROL[1] + 1.6, CONTROL[2] - 1.6,
                         CONTROL[3] - 1.6), CONTROL_DECK_Z + 0.05, "Indoor", step=6.0,
                 cover=0.3)
    lay.nav_zone(nodes, (CONTROL[0] + 1.6, CONTROL[1] + 1.6, CONTROL[2] - 1.6,
                         CONTROL[3] - 1.6), CONTROL_ROOF_Z + 0.15, "Elevated", step=8.0,
                 cover=0.1)
    lay.nav_zone(nodes, (HALL[0] + 2.0, HALL[1] + 2.0, HALL[2] - 2.0, HALL[3] - 2.0),
                 ROOF_WALK + 0.05, "Elevated", step=8.0, cover=0.1)

    for spawn in build_spawns():
        node = lay.nav_node(next_id(), spawn["position"], "Spawn", 0.4, spawn["marker"])
        nodes.append(node)
        spawn["navNode"] = node["id"]

    marked = [
        ((0.0, -21.0, 0.4), "Exterior", 0.6, "nav_rail_spur"),
        ((0.0, -10.0, 0.15), "Doorway", 0.3, "nav_yard_gate"),
        ((24.0, -2.0, 0.2), "Objective", 0.2, "nav_fuel_line"),
        ((-23.0, 6.6, 0.2), "Objective", 0.2, "nav_control_room"),
        ((-10.0, 20.0, 0.2), "Objective", 0.3, "nav_kiln_hall"),
        ((EXTRACTION[0], EXTRACTION[1], 0.25), "Extraction", 0.1, "nav_extraction_pad"),
        ((-26.0, -13.0, 0.15), "Exterior", 0.4, "nav_loading_ramp"),
        ((0.0, 12.0, 0.15), "Doorway", 0.3, "hall_south_gate"),
        ((0.0, 28.0, 0.15), "Doorway", 0.2, "hall_north_door"),
        ((-20.0, 21.0, 0.15), "Exterior", 0.0, "landmark_chimney"),
        ((-12.0, -21.0, 0.4), "Exterior", 0.8, "cover_wagon_west"),
        ((8.0, -21.0, 0.4), "Exterior", 0.8, "cover_wagon_east"),
        ((-14.0, 6.0, 0.15), "Doorway", 0.3, "control_yard_door"),
    ]
    for position, area, cover, marker in marked:
        nodes.append(lay.nav_node(next_id(), position, area, cover, marker))

    verticals = [
        # hall floor -> kiln ring gantry
        dict(bottom=(-12.6, KILN[1] - 4.14 - 1.2, 0.15), top=(-6.6, KILN[1] - 4.14, DECK),
             area_bottom="Indoor", area_top="Catwalk", marker_top="gantry_stair_west"),
        # pipe yard catwalk: two stairs
        dict(bottom=(18.0, -8.4, 0.15), top=(18.0, -1.4, DECK), area_bottom="Exterior",
             area_top="Catwalk", marker_top="pipe_catwalk_south"),
        dict(bottom=(18.0, 11.4, 0.15), top=(18.0, 4.4, DECK), area_bottom="Exterior",
             area_top="Catwalk", marker_top="pipe_catwalk_north"),
        # control block: internal stair to the upper floor
        dict(bottom=(-25.0, 0.6, 0.15), top=(-23.0, 6.6, CONTROL_DECK_Z + 0.05),
             area_bottom="Indoor", area_top="Indoor", marker_top="control_upper_landing"),
        # control block: external ladder to its roof
        dict(bottom=(-31.6, 4.0, 0.15), top=(-28.6, 4.0, CONTROL_ROOF_Z + 0.15),
             area_bottom="Exterior", area_top="Elevated", marker_top="control_ladder_top"),
        # loading ramp hop over the west barrier line
        dict(bottom=(-26.0, -15.6, 0.15), top=(-25.4, -12.6, 0.15),
             area_bottom="Exterior", area_top="Exterior", jump=True, jump_height=1.2,
             marker_top="nav_loading_ramp_top"),
        # yard -> through the gate
        dict(bottom=(0.0, -11.4, 0.15), top=(0.0, -8.4, 0.15), area_bottom="Exterior",
             area_top="Exterior"),
        # hall roof ladder
        dict(bottom=(15.8, 24.0, 0.15), top=(12.6, 24.0, ROOF_WALK + 0.05),
             area_bottom="Exterior", area_top="Elevated", marker_top="hall_roof_ladder"),
        # control block east door into the mid yard
        dict(bottom=(-15.4, 6.0, 0.15), top=(-12.4, 6.0, 0.15), area_bottom="Indoor",
             area_top="Exterior"),
    ]
    authored: List[dict] = []
    for spec in verticals:
        authored.append(lay.vertical(nodes, next_id, spec.pop("bottom"), spec.pop("top"),
                                     **spec))

    built, notes = lay.build_nav(nodes, authored, _MANIFEST)
    for note in notes:
        print(f"[silent_kiln] nav: {note}")
    nodes, links, problems = lay.prune_unreachable(nodes, built)
    for problem in problems:
        print(f"[silent_kiln] nav: {problem}")
    return nodes, links


def build_layout() -> dict:
    placements, _ = build_placements()
    spawns = build_spawns()
    return {
        "id": MAP_ID,
        "name": MAP_NAME,
        "scene": SCENE_NAME,
        "mode": "Cooperative",
        "modes": ["Cooperative"],
        "comment": [
            "Generated by Tools/blender/build_maps.py from Tools/blender/layouts/silent_kiln.py.",
            "The markers here are the locationIds used by Definitions/missions.json;",
            "build_maps.py fails the map when an objective points at a missing marker.",
        ],
        "bounds": {"min": [BOUNDS[0], BOUNDS[1], -1.0], "max": [BOUNDS[2], BOUNDS[3], 12.0]},
        "units": "metres",
        "up": "z",
        "shells": [{"id": name, **{k: v for k, v in spec.items() if k != "builder"}}
                   for name, spec in SHELLS.items()],
        "placements": placements,
        "spawns": spawns,
        "navGraphId": MAP_ID,
        "audioZones": [
            {"id": "hall", "region": list(HALL), "kind": "Indoor", "reverb": "large_hall",
             "height": 9.6},
            {"id": "control", "region": list(CONTROL), "kind": "Indoor", "reverb": "room",
             "height": 7.2},
            {"id": "yard", "region": list(BOUNDS), "kind": "Outdoor", "reverb": "night_yard",
             "height": 20.0},
        ],
        "landmarks": [
            {"id": "furnace_kiln", "position": [KILN[0], KILN[1], 0.0], "height": 8.1,
             "label": "Kiln"},
            {"id": "chimney_stack", "position": [-20.0, 24.0, 0.0], "height": 14.7,
             "label": "Stack"},
            {"id": "rail_wagon", "position": [-12.0, RAIL_Y, 0.0], "height": 2.3,
             "label": "Rail spur"},
            {"id": "extraction_pad", "position": [EXTRACTION[0], EXTRACTION[1], 0.0],
             "height": 2.0, "label": "Extraction"},
        ],
        "objectives": [
            {"id": "reach_yard", "marker": "nav_rail_spur"},
            {"id": "breach_gate", "marker": "nav_yard_gate", "destructible": "gate_yard_6"},
            {"id": "sabotage_fuel_line", "marker": "nav_fuel_line", "count": 2},
            {"id": "retrieve_intel", "marker": "nav_control_room"},
            {"id": "hold_kiln_hall", "marker": "nav_kiln_hall", "holdSeconds": 45},
            {"id": "reach_extraction", "marker": "nav_extraction_pad"},
            {"id": "extract", "marker": "nav_extraction_pad", "boardSeconds": 40},
        ],
        "rules": {
            "minPlayers": 2,
            "maxPlayers": 4,
            "timeLimitSeconds": 1500,
            "hostilesSimultaneous": 8,
            "hostilesTotalBudget": 34,
            "bleedOutSeconds": 45,
        },
    }
