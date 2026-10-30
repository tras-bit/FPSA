#!/usr/bin/env python3
"""Zavod-7: the released free-for-all map (ferroalloy plant, Veransk corridor).

88 x 68 m of abandoned ferroalloy works, built only from the modular kit plus two
map specific shells (the ground plane and the hall gables).  Everything is placed
by code so the map is reproducible and reviewable as a diff.

Three routes, as the design brief requires:

    Route 1  Yard and furnace hall (ground)
             South yard with the cut rail spur and wagon cover, the concrete apron,
             and straight into the hall through the 8 m main gate or the two side
             doors.  Long sight lines down the hall to the rotary kiln, hard cover
             everywhere, the shortest path between any two spawns - and the one
             everybody fights over.
    Route 2  Catwalks, gantry and roof (elevated)
             The ring gantry at +4 m around the kiln (two stairs up from the hall
             floor), the internal stair tower in the hall's south east bay that
             comes out through a roof hatch at +8 m, the east pipe yard catwalk
             with two stairs, and the office block: internal stair to the upper
             floor, external ladder to its roof, then a 2 m bridge onto the hall
             roof.  Six breakable roof lights look straight down onto the kiln.
    Route 3  Service tunnel (covered)
             38 m of covered service tunnel from the south fence into the hall's
             south west bay, with a cross passage exiting both sides at mid yard.
             Dark, quiet, short - and it comes out behind whoever holds the hall.

Four corner spawn shelters carry five slots each, which is exactly the twenty the
free-for-all rules require; each shelter is open towards the map centre so nobody
spawns facing a wall.  Destructibles (the north yard gate, the six roof lights,
crates, pallet stacks, glazed screens, fences) are listed in the map JSON because
the kit manifest already marks them, and the server drives them from there.

Coordinates are metres, Z up, +Y north, yaw in degrees around Z (0 = the module's
+X faces world +X).  Module dimensions come from kit_manifest.json, so a kit change
moves the layout instead of silently overlapping it.
"""
from __future__ import annotations

import math
from typing import Dict, List, Sequence, Tuple

import bpy

import kordon_art as art
import kordon_layout as lay

MAP_ID = "ffa_zavod7"
MAP_NAME = "Zavod-7"
SCENE_NAME = "FFA_Zavod7"
BOUNDS = (-44.0, -34.0, 44.0, 34.0)

# Regions reused by geometry, navigation, audio zones and spawn rules.
HALL = (-20.0, 4.0, 20.0, 28.0)          # furnace hall, 40 x 24 m
OFFICE = (-38.0, 8.0, -22.0, 24.0)       # two storey office / control block
PIPE_YARD = (22.0, -8.0, 40.0, 20.0)     # east pipe yard
TUNNEL = (-14.0, -34.0, -6.0, 4.0)       # service tunnel (route 3)
RAIL_Y = -24.0
KILN = (0.0, 17.0)
RING = 6.21                                # kiln gantry half span (3 x 4.14 m runs)
GANTRY_Z = 4.0
DECK = 4.12                                # catwalk walking surface at GANTRY_Z
OFFICE_DECK_Z = 3.6
OFFICE_ROOF_Z = 7.2
ROOF_Z = 8.0                               # hall roof tiles
ROOF_WALK = 8.1
SPAWN_ZONES = {
    "A": (-38.0, -28.0),   # south west
    "B": (38.0, -28.0),    # south east
    "C": (-38.0, 28.0),    # north west
    "D": (38.0, 28.0),     # north east
}

_MANIFEST: Dict[str, dict] = {}


def set_manifest(manifest: Dict[str, dict]) -> None:
    global _MANIFEST
    _MANIFEST = manifest
    lay.set_module_sizes({name: entry["size"] for name, entry in manifest.items()})


# --------------------------------------------------------------------------
# shells: geometry that belongs to this map only
# --------------------------------------------------------------------------
def build_shell_ground() -> bpy.types.Object:
    """One object: ballast, aprons, building floors, painted markings."""
    parts = [
        art.box((88.0, 68.0, 0.4), (0.0, 0.0, -0.2), "gravel", "ground"),
        art.box((88.2, 12.0, 0.06), (0.0, -4.0, 0.01), "concrete_worn", "apron"),
        art.box((40.4, 24.4, 0.1), (0.0, 16.0, 0.03), "concrete_raw", "hall_floor"),
        art.box((16.4, 16.4, 0.1), (-30.0, 16.0, 0.03), "concrete_worn", "office_floor"),
        art.box((18.4, 28.4, 0.08), (31.0, 6.0, 0.02), "concrete_worn", "pipe_pad"),
        art.box((28.4, 4.6, 0.12), (-28.0, RAIL_Y, 0.04), "gravel", "ballast_west"),
        art.box((36.4, 4.6, 0.12), (13.0, RAIL_Y, 0.04), "gravel", "ballast_east"),
        art.box((8.4, 34.4, 0.07), (-10.0, -17.0, 0.02), "concrete_worn", "tunnel_apron"),
        art.box((10.0, 8.0, 0.06), (0.0, -14.0, 0.02), "concrete_worn", "gate_apron"),
        art.box((1.6, 1.6, 0.02), (KILN[0], KILN[1], 0.09), "hazard_paint", "kiln_marker"),
        art.box((6.0, 3.0, 0.05), (24.0, -18.0, 0.05), "sheet_metal", "weighbridge"),
    ]
    # painted walk ways either side of the main gate, and the kiln hazard ring
    for sign in (-1.0, 1.0):
        parts.append(art.box((0.24, 14.0, 0.02), (sign * 4.4, -3.0, 0.05), "hazard_paint",
                             "walkway_line"))
        parts.append(art.box((6.4, 0.24, 0.02), (sign * 7.6, 3.6, 0.05), "hazard_paint",
                             "gate_line"))
    for i in range(4):
        angle = i * math.pi * 0.5 + math.pi * 0.25
        part = art.box((2.2, 0.2, 0.02), (KILN[0] + math.cos(angle) * 5.2,
                                          KILN[1] + math.sin(angle) * 5.2, 0.09),
                       "hazard_paint", "kiln_hazard")
        part.rotation_euler = (0.0, 0.0, angle)
        parts.append(part)
    for i in range(6):
        parts.append(art.box((0.3, 1.2, 0.14), (-34.0 + i * 1.4, -30.0, 0.07),
                             "concrete_worn", "tyre_stop"))
    obj = art.join(parts, f"{MAP_ID}_ground")
    art.shade_flat(obj)
    art.bevel(obj, 0.015)
    art.unwrap(obj)
    art.set_origin_bottom_center(obj)
    return obj


def build_shell_roof_structure() -> bpy.types.Object:
    """Hall roof structure that is not part of the reusable kit.

    Purlins and trusses sit under the roof tiles, a fascia trim runs inside the
    parapet, and a roof monitor (a louvred smoke vent) gives the roof its own
    landmark and its only real cover.  Nothing here rises above 9.6 m, so chest
    height line of sight across the roof stays honest.
    """
    parts = []
    depth = HALL[3] - HALL[1]
    centre_y = (HALL[1] + HALL[3]) * 0.5
    width = HALL[2] - HALL[0]
    # fascia trim inside the parapet
    for sign in (-1.0, 1.0):
        parts.append(art.box((0.18, depth + 0.4, 0.26), (sign * (width * 0.5 - 0.1),
                                                         centre_y, ROOF_Z + 0.02),
                             "steel_dark", "fascia"))
    for sign in (-1.0, 1.0):
        parts.append(art.box((width + 0.4, 0.18, 0.26), (0.0, centre_y + sign * (depth * 0.5
                                                                                  - 0.1),
                                                         ROOF_Z + 0.02), "steel_dark",
                             "fascia"))
    # purlins under the deck
    for i in range(10):
        parts.append(art.box((0.18, depth, 0.22), (HALL[0] + 2.0 + i * 4.0, centre_y,
                                                   ROOF_Z - 0.3), "steel_dark", "purlin"))
    # three trusses across the hall
    for y in (HALL[1] + 4.0, centre_y, HALL[3] - 4.0):
        parts += art.ibeam(width, 0.42, 0.2, 0.05, 0.06, (0.0, y, ROOF_Z - 0.75), "x",
                           "steel_dark")
        for i in range(9):
            parts.append(art.box((0.09, 0.09, 0.42), (HALL[0] + 2.4 + i * 4.4, y,
                                                      ROOF_Z - 0.5), "steel_dark",
                                 "truss_brace"))
    # roof monitor / smoke vent: roof landmark and cover
    monitor = (-16.0, 18.0)
    parts.append(art.box((6.0, 4.0, 1.5), (monitor[0], monitor[1], ROOF_Z + 0.85),
                         "sheet_metal", "monitor_body"))
    parts.append(art.box((6.3, 4.3, 0.14), (monitor[0], monitor[1], ROOF_Z + 1.62),
                         "steel_dark", "monitor_cap"))
    for i in range(6):
        parts.append(art.box((5.6, 0.06, 0.14), (monitor[0], monitor[1] - 1.7 + i * 0.68,
                                                 ROOF_Z + 1.2), "steel_dark", "louvre"))
        parts.append(art.box((0.06, 3.6, 0.14), (monitor[0] - 2.8 + i * 1.12, monitor[1],
                                                 ROOF_Z + 1.2), "steel_dark", "louvre"))
    parts.append(art.cylinder(0.34, 0.34, 1.1, (monitor[0] + 2.0, monitor[1],
                                                ROOF_Z + 2.1), (0, 0, 0), 12,
                              "steel_dark", "vent_stack"))
    obj = art.join(parts, f"{MAP_ID}_roof_structure")
    art.shade_flat(obj)
    art.bevel(obj, 0.015)
    art.unwrap(obj)
    art.set_origin_bottom_center(obj)
    return obj


SHELLS = {
    "ground": dict(builder=build_shell_ground, position=(0.0, 0.0, 0.0), yaw=0.0,
                   blocksSight=False, blocksMovement=False, walkable=True,
                   description="Terrain shell: ballast, aprons, floors, painted markings"),
    "roof_structure": dict(builder=build_shell_roof_structure, position=(0.0, 0.0, 0.0),
                           yaw=0.0, blocksSight=True, blocksMovement=True,
                           description="Hall roof purlins, trusses, fascia and monitor"),
}


# --------------------------------------------------------------------------
# placement helpers
# --------------------------------------------------------------------------
def insert(run_start: Sequence[float], run_end: Sequence[float], distance: float,
           module: str, out: List[dict], z: float = 0.0, tag: str = "") -> dict:
    """Place one module at a distance along a run (doors, gates, bridges)."""
    unit = lay.unit_vector(run_start, run_end)
    point = art.mathutils.Vector((run_start[0], run_start[1], z)) + unit * distance
    entry = lay.placement(module, (point.x, point.y, z), lay.yaw_for(run_start, run_end),
                          tag)
    out.append(entry)
    return entry


def catwalk_run(start: Sequence[float], end: Sequence[float], out: List[dict],
                z: float = GANTRY_Z, fillers: Sequence[str] = ("catwalk_2",),
                tag: str = "catwalk") -> int:
    """A catwalk run never seals by overlap - rails crossing rails read as a bug."""
    return lay.side_run("catwalk_4", start, end, out, z=z, fillers=fillers, seal=False,
                        tag=tag)


def roof_tiles(out: List[dict], region: Sequence[float], z: float, skylights=(),
               omit=(), tag: str = "roof") -> int:
    placed = 0
    x = region[0] + 2.0
    while x <= region[2] - 2.0 + 1e-4:
        y = region[1] + 2.0
        while y <= region[3] - 2.0 + 1e-4:
            if (x, y) in omit:
                y += 4.0
                continue
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
    counts: Dict[str, list] = {}

    # ---- perimeter -------------------------------------------------------
    # South and east palisade fence, open where the tunnel mouth is.
    lay.side_run("fence_4", (-44.0, -34.0), (-14.2, -34.0), out, fillers=("fence_4",),
                 tag="perimeter")
    lay.side_run("fence_4", (-5.8, -34.0), (44.0, -34.0), out, fillers=("fence_4",),
                 tag="perimeter")
    lay.side_run("fence_4", (44.0, -34.0), (44.0, 34.0), out, fillers=("fence_4",),
                 tag="perimeter")
    # West fence with a 12 m gap: the rail spur used to come in here.
    lay.side_run("fence_4", (-44.0, -34.0), (-44.0, -30.2), out, fillers=("fence_4",),
                 tag="perimeter")
    lay.side_run("fence_4", (-44.0, -17.8), (-44.0, 34.0), out, fillers=("fence_4",),
                 tag="perimeter")
    # North plant wall with the destructible yard gate in the middle.
    lay.side_run("wall_4", (-44.0, 34.0), (-3.2, 34.0), out, tag="perimeter")
    lay.side_run("wall_4", (3.2, 34.0), (44.0, 34.0), out, tag="perimeter")
    insert((-44.0, 34.0), (44.0, 34.0), 44.0, "gate_yard_6", out, tag="north_gate")
    for corner in ((-44.0, 34.0), (44.0, 34.0)):
        out.append(lay.placement("corner_outer", corner, 0.0, "perimeter"))
    for corner in ((-44.0, -34.0), (44.0, -34.0)):
        out.append(lay.placement("fence_corner", corner, 0.0, "perimeter"))

    # ---- rail spur through the south yard --------------------------------
    for i in range(7):
        out.append(lay.placement("rail_track_4", (-42.0 + i * 4.0, RAIL_Y, 0.0), 0.0,
                                 "rail"))
    for i in range(8):
        out.append(lay.placement("rail_track_4", (-2.0 + i * 4.0, RAIL_Y, 0.0), 0.0, "rail"))
    out.append(lay.placement("rail_wagon_8", (-28.0, RAIL_Y, 0.3), 0.0, "rail"))
    out.append(lay.placement("rail_wagon_8", (14.0, RAIL_Y, 0.3), 0.0, "rail"))
    # the tunnel crosses the spur: concrete barriers mark the gap
    out.append(lay.placement("barrier_concrete_2", (-13.0, -20.4, 0.0), 0.0, "rail"))
    out.append(lay.placement("barrier_concrete_2", (-7.0, -20.4, 0.0), 0.0, "rail"))
    out.append(lay.placement("barrier_concrete_2", (-13.0, -27.6, 0.0), 0.0, "rail"))
    out.append(lay.placement("barrier_concrete_2", (-7.0, -27.6, 0.0), 0.0, "rail"))

    # ---- furnace hall ----------------------------------------------------
    hall_ground_openings = {
        "south": [(10.0, 8.2), (20.0, 8.2), (32.0, 4.1)],   # tunnel mouth, gate, door
        "north": [(6.0, 4.1), (20.0, 4.6), (34.0, 4.1)],    # doors and a yard opening
        "west": [(14.0, 4.6), (20.0, 4.1)],                 # office link, door
        "east": [(6.0, 4.6), (20.0, 4.1)],                  # pipe yard, door
    }
    lay.room((HALL[0], HALL[1]), (HALL[2], HALL[3]), out, wall="wall_4", z=0.0, tiers=1,
             openings=hall_ground_openings, tag="hall")
    lay.room((HALL[0], HALL[1]), (HALL[2], HALL[3]), out, wall="wall_4_window", z=3.6,
             tiers=1, openings={}, tag="hall_upper")
    lay.room((HALL[0], HALL[1]), (HALL[2], HALL[3]), out, wall="wall_low_4", z=7.2,
             tiers=1, openings={"west": [(14.0, 4.6)]}, corner="corner_inner",
             tag="hall_parapet")
    # doors into the ground tier openings
    insert((HALL[0], HALL[1]), (HALL[2], HALL[1]), 32.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[2], HALL[1]), (HALL[2], HALL[3]), 20.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[2], HALL[3]), (HALL[0], HALL[3]), 6.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[2], HALL[3]), (HALL[0], HALL[3]), 34.0, "wall_4_door", out, tag="hall_door")
    insert((HALL[0], HALL[3]), (HALL[0], HALL[1]), 20.0, "wall_4_door", out, tag="hall_door")
    # broken wall section: the yard can be entered without a door at all
    insert((HALL[0], HALL[3]), (HALL[0], HALL[1]), 8.0, "wall_4_broken", out, z=3.6,
           tag="hall_upper")

    # roof with six breakable lights and the stair hatch
    roof_tiles(out, HALL, ROOF_Z,
               skylights={(-10.0, 10.0), (-2.0, 10.0), (6.0, 10.0),
                          (-10.0, 26.0), (-2.0, 26.0), (6.0, 26.0)},
               omit={(14.0, 22.0), (18.0, 22.0)}, tag="hall_roof")
    out.append(lay.placement("railing_4", (14.0, 20.0, ROOF_WALK), 0.0, "hall_roof"))
    out.append(lay.placement("railing_4", (18.0, 20.0, ROOF_WALK), 0.0, "hall_roof"))
    out.append(lay.placement("railing_4", (12.0, 22.0, ROOF_WALK), 90.0, "hall_roof"))

    # hall interior: landmark kiln, plant, structure and cover
    out.append(lay.placement("furnace_kiln", (KILN[0], KILN[1], 0.1), 0.0, "landmark"))
    out.append(lay.placement("chimney_stack", (-30.0, 20.0, 0.0), 0.0, "landmark"))
    out.append(lay.placement("slag_pot", (-9.0, 8.0, 0.1), 25.0, "plant"))
    out.append(lay.placement("slag_pot", (9.0, 25.0, 0.1), 200.0, "plant"))
    out.append(lay.placement("hopper_3", (-16.0, 25.0, 0.1), 200.0, "plant"))
    out.append(lay.placement("conveyor_6", (9.0, 7.0, 0.0), 0.0, "plant"))
    out.append(lay.placement("control_desk", (16.0, 26.0, 0.1), 180.0, "plant"))
    out.append(lay.placement("locker_row", (-17.0, 22.0, 0.1), 90.0, "plant"))
    out.append(lay.placement("transformer_box", (-17.0, 7.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("pipe_rack_4", (-6.0, 26.4, 0.0), 0.0, "plant"))
    out.append(lay.placement("pipe_rack_4", (-2.0, 26.4, 0.0), 0.0, "plant"))
    out.append(lay.placement("vent_duct_4", (6.0, 26.2, 2.9), 0.0, "plant"))
    out.append(lay.placement("crate_stack", (-13.0, 14.0, 0.0), 8.0, "hall_cover"))
    out.append(lay.placement("sandbag_wall", (-4.0, 24.0, 0.0), 180.0, "hall_cover"))
    out.append(lay.placement("barrier_concrete_2", (10.0, 24.0, 0.0), 90.0, "hall_cover"))
    out.append(lay.placement("crate_1", (4.0, 8.0, 0.0), 14.0, "hall_cover"))
    out.append(lay.placement("barrel_cluster", (-6.0, 12.0, 0.0), 0.0, "hall_cover"))
    for pillar in ((-11.0, 12.0), (11.0, 12.0), (-11.0, 24.0), (11.0, 24.0)):
        out.append(lay.placement("pillar_1", pillar, 0.0, "hall_structure"))
    for column in ((-18.0, 8.0), (-18.0, 26.0), (18.0, 8.0), (18.0, 26.0)):
        out.append(lay.placement("column_steel_4", column, 0.0, "hall_structure"))
    for x in (-14.0, -6.0, 2.0, 10.0):
        out.append(lay.placement("beam_4", (x, 16.0, 6.9), 0.0, "hall_structure"))
    for position in ((-19.6, 10.0, 3.2), (-19.6, 22.0, 3.2), (19.6, 10.0, 3.2),
                     (19.6, 22.0, 3.2), (-4.0, 4.4, 3.2), (8.0, 4.4, 3.2),
                     (-4.0, 27.6, 3.2), (8.0, 27.6, 3.2)):
        out.append(lay.placement("floodlight_wall", position, 0.0, "light"))

    # ---- kiln gantry at +4 m ---------------------------------------------
    ring_cx, ring_cy = KILN
    for corner in ((ring_cx - RING, ring_cy - RING), (ring_cx + RING, ring_cy - RING),
                   (ring_cx - RING, ring_cy + RING), (ring_cx + RING, ring_cy + RING)):
        out.append(lay.placement("catwalk_corner", corner, GANTRY_Z, "gantry"))
    catwalk_run((ring_cx - RING, ring_cy - RING), (ring_cx + RING, ring_cy - RING), out,
                fillers=(), tag="gantry")
    catwalk_run((ring_cx + RING, ring_cy + RING), (ring_cx - RING, ring_cy + RING), out,
                fillers=(), tag="gantry")
    catwalk_run((ring_cx - RING, ring_cy - RING), (ring_cx - RING, ring_cy + RING), out,
                fillers=(), tag="gantry")
    catwalk_run((ring_cx + RING, ring_cy + RING), (ring_cx + RING, ring_cy - RING), out,
                fillers=(), tag="gantry")
    for y in (ring_cy - RING, ring_cy + RING):
        out.append(lay.placement("railing_4", (ring_cx - RING - 0.9, y, DECK), 90.0,
                                 "gantry"))
        out.append(lay.placement("railing_4", (ring_cx + RING + 0.9, y, DECK), 90.0,
                                 "gantry"))
    # two stairs from the hall floor onto the ring
    out.append(lay.placement("stairs_4", (ring_cx - RING - 3.08, ring_cy - 4.14, 0.0), 0.0,
                             "gantry_stairs"))
    out.append(lay.placement("stairs_4", (ring_cx + RING + 3.08, ring_cy + 4.14, 0.0), 180.0,
                             "gantry_stairs"))
    for column in ((ring_cx - RING - 5.0, ring_cy - 4.14), (ring_cx + RING + 5.0,
                                                           ring_cy + 4.14)):
        out.append(lay.placement("column_steel_4", column, 0.0, "gantry_structure"))
    # maintenance ladder on the outside of the ring's east run
    out.append(lay.placement("ladder_4", (ring_cx + RING + 1.15, ring_cy + 3.0, 0.0), 90.0,
                             "gantry_structure"))

    # ---- internal stair tower to the roof (hall south east bay) ----------
    tower_x = 16.0
    out.append(lay.placement("stairs_4", (tower_x, 8.0, 0.0), 90.0, "roof_stairs"))
    out.append(lay.placement("catwalk_platform_4", (tower_x, 13.2, GANTRY_Z), 0.0,
                             "roof_stairs"))
    out.append(lay.placement("stairs_4", (tower_x, 18.38, GANTRY_Z), 90.0, "roof_stairs"))
    out.append(lay.placement("railing_4", (tower_x - 2.0, 13.2, DECK), 90.0, "roof_stairs"))
    for column in ((tower_x + 2.4, 10.0), (tower_x + 2.4, 16.0)):
        out.append(lay.placement("column_steel_4", column, 0.0, "roof_structure"))

    # ---- office / control block (west, two storeys) ----------------------
    office_openings = {
        "south": [(8.0, 4.1)],
        "north": [(8.0, 4.1)],
        "east": [(8.0, 4.6)],
        "west": [(8.0, 4.1)],
    }
    lay.room((OFFICE[0], OFFICE[1]), (OFFICE[2], OFFICE[3]), out, wall="wall_4", z=0.0,
             tiers=1, openings=office_openings, tag="office")
    lay.room((OFFICE[0], OFFICE[1]), (OFFICE[2], OFFICE[3]), out, wall="wall_4_window",
             z=3.6, tiers=1, openings={}, tag="office_upper")
    insert((OFFICE[0], OFFICE[1]), (OFFICE[2], OFFICE[1]), 8.0, "wall_4_door", out,
           tag="office_door")
    insert((OFFICE[0], OFFICE[3]), (OFFICE[2], OFFICE[3]), 8.0, "wall_4_door", out,
           tag="office_door")
    insert((OFFICE[0], OFFICE[3]), (OFFICE[0], OFFICE[1]), 8.0, "wall_4_door", out,
           tag="office_door")
    # upper floor deck (stair well left open) and its edge railings
    lay.grid_fill("floor_slab_4", (OFFICE[0], OFFICE[1]), (OFFICE[2], OFFICE[3]), out,
                  z=OFFICE_DECK_Z, skip=lambda x, y: abs(x + 30.0) < 2.6 and y < 16.0,
                  tag="office_deck")
    lay.side_run("railing_4", (OFFICE[0] + 2.0, OFFICE[1] + 2.0),
                 (OFFICE[2] - 2.0, OFFICE[1] + 2.0), out, z=OFFICE_DECK_Z + 0.12,
                 fillers=(), seal=False, tag="office_deck")
    lay.side_run("railing_4", (OFFICE[0] + 2.0, OFFICE[3] - 2.0),
                 (OFFICE[2] - 2.0, OFFICE[3] - 2.0), out, z=OFFICE_DECK_Z + 0.12,
                 fillers=(), seal=False, tag="office_deck")
    out.append(lay.placement("stairs_4", (-30.0, 12.0, 0.0), 90.0, "office_stairs"))
    roof_tiles(out, OFFICE, OFFICE_ROOF_Z, tag="office_roof")
    out.append(lay.placement("ladder_4", (OFFICE[0] - 0.5, 20.0, 0.0), 270.0, "office_access"))
    out.append(lay.placement("ladder_4", (OFFICE[0] - 0.5, 20.0, 4.05), 270.0,
                             "office_access"))
    out.append(lay.placement("railing_4", (OFFICE[0] + 2.0, OFFICE[3] - 0.2,
                                           OFFICE_ROOF_Z + 0.1), 0.0, "office_roof"))
    # roof bridge onto the hall roof: office roof 7.3 -> bridge 7.72 -> hall roof 8.1
    out.append(lay.placement("catwalk_2", (-21.0, 16.0, 7.6), 0.0, "bridge"))
    out.append(lay.placement("floodlight_wall", (OFFICE[2] - 0.4, 12.0, 3.0), 270.0,
                             "light"))
    out.append(lay.placement("floodlight_wall", (OFFICE[2] - 0.4, 20.0, 3.0), 270.0,
                             "light"))
    # interior furniture, both floors
    out.append(lay.placement("control_desk", (-30.0, 20.0, 0.1), 180.0, "office_prop"))
    out.append(lay.placement("locker_row", (-36.0, 12.0, 0.1), 90.0, "office_prop"))
    out.append(lay.placement("transformer_box", (-24.0, 10.0, 0.1), 180.0, "office_prop"))
    out.append(lay.placement("crate_stack", (-34.0, 20.0, 0.0), 0.0, "office_prop"))
    out.append(lay.placement("control_desk", (-34.0, 16.0, OFFICE_DECK_Z + 0.1), 90.0,
                             "office_prop"))
    out.append(lay.placement("locker_row", (-25.0, 20.0, OFFICE_DECK_Z + 0.1), 180.0,
                             "office_prop"))

    # ---- east pipe yard and its catwalk ----------------------------------
    for y in (-4.0, 4.0, 12.0):
        lay.side_run("pipe_rack_4", (PIPE_YARD[0] + 1.0, y), (PIPE_YARD[2] - 1.0, y), out,
                     fillers=(), tag="pipes")
    for y in (0.0, 8.0, 16.0):
        lay.side_run("vent_duct_4", (PIPE_YARD[0] + 1.0, y), (PIPE_YARD[2] - 1.0, y), out,
                     z=2.8, fillers=(), tag="pipes")
    out.append(lay.placement("hopper_3", (36.0, -4.0, 0.1), 90.0, "plant"))
    out.append(lay.placement("transformer_box", (24.0, 18.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("pipe_valve", (28.0, -4.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("pipe_valve", (34.0, 4.0, 0.1), 180.0, "plant"))
    out.append(lay.placement("pipe_valve", (26.0, 12.0, 0.1), 0.0, "plant"))
    out.append(lay.placement("dumpster_2", (38.0, 10.0, 0.0), 90.0, "yard_prop"))
    out.append(lay.placement("cable_spool", (26.0, -6.0, 0.0), 20.0, "yard_prop"))
    out.append(lay.placement("crate_stack", (34.0, 16.0, 0.0), 0.0, "yard_prop"))
    out.append(lay.placement("sandbag_wall", (30.0, 8.0, 0.0), 20.0, "yard_cover"))
    catwalk_run((24.0, -7.0), (24.0, 17.84), out, fillers=(), tag="east_catwalk")
    lay.side_run("railing_4", (25.0, -7.0), (25.0, 17.8), out, z=DECK, fillers=(),
                 seal=False, tag="east_catwalk")
    out.append(lay.placement("stairs_4", (24.0, -3.92, 0.0), 90.0, "east_stairs"))
    out.append(lay.placement("stairs_4", (24.0, 14.92, 0.0), 270.0, "east_stairs"))
    for column in ((24.0, -6.0), (24.0, 6.0), (24.0, 17.0)):
        out.append(lay.placement("column_steel_4", column, 0.0, "east_structure"))
    out.append(lay.placement("light_mast_8", (38.0, -6.0, 0.0), 0.0, "light"))
    out.append(lay.placement("floodlight_wall", (22.4, 0.0, 3.0), 90.0, "light"))

    # ---- service tunnel (route 3) ---------------------------------------
    west_wall = ((TUNNEL[0], TUNNEL[1]), (TUNNEL[0], TUNNEL[3]))
    east_wall = ((TUNNEL[2], TUNNEL[3]), (TUNNEL[2], TUNNEL[1]))
    lay.side_run("wall_4", *west_wall, out, openings=[(18.27, 4.1)], tag="tunnel")
    lay.side_run("wall_4", *east_wall, out, openings=[(18.27, 4.1)], tag="tunnel")
    lay.grid_fill("floor_slab_4", (TUNNEL[0], TUNNEL[1] + 2.0), (TUNNEL[2], TUNNEL[3]), out,
                  z=3.6, tag="tunnel_roof")
    lay.side_run("vent_duct_4", (TUNNEL[0] + 1.6, -30.0), (TUNNEL[0] + 1.6, 0.0), out,
                 z=2.7, fillers=(), tag="tunnel_services")
    lay.side_run("pipe_rack_4", (TUNNEL[2] - 1.2, -30.0), (TUNNEL[2] - 1.2, 0.0), out,
                 fillers=(), tag="tunnel_services")
    for y in (-30.0, -22.0, -8.0, 0.0):
        out.append(lay.placement("floodlight_wall", (TUNNEL[2] - 0.3, y, 2.4), 0.0, "light"))
    out.append(lay.placement("crate_stack", (-12.4, -12.0, 0.0), 12.0, "tunnel_cover"))
    out.append(lay.placement("pallet_stack", (-7.6, -26.0, 0.0), 0.0, "tunnel_cover"))
    out.append(lay.placement("barrel_cluster", (-12.2, -2.0, 0.0), 0.0, "tunnel_cover"))
    out.append(lay.placement("sandbag_wall", (-8.0, -20.0, 0.0), 180.0, "tunnel_cover"))

    # ---- yard cover, props and lighting ---------------------------------
    avoid = [
        (-42.5, RAIL_Y - 2.6, 42.5, RAIL_Y + 2.6),
        (HALL[0] - 1.0, HALL[1] - 1.0, HALL[2] + 1.0, HALL[3] + 1.0),
        (TUNNEL[0] - 1.5, TUNNEL[1], TUNNEL[2] + 1.5, TUNNEL[3]),
        (OFFICE[0] - 1.5, OFFICE[1] - 1.5, OFFICE[2] + 1.5, OFFICE[3] + 1.5),
        (PIPE_YARD[0] - 1.0, PIPE_YARD[1] - 1.0, PIPE_YARD[2] + 1.0, PIPE_YARD[3] + 1.0),
        (-31.0, 17.0, -27.0, 23.0),                       # chimney
        (-42.0, -32.0, -33.0, -23.0), (33.0, -32.0, 42.0, -23.0),
        (-42.0, 23.0, -33.0, 32.0), (33.0, 23.0, 42.0, 32.0),
    ]
    lay.scatter(("crate_1", "crate_stack", "barrel_cluster", "concrete_block_2",
                 "pallet_stack", "sandbag_wall", "cable_spool", "dumpster_2",
                 "rubble_pile"),
                (-40.0, -32.0, 40.0, 1.0), 40, 7101, out, spacing=3.4, avoid=avoid)
    lay.scatter(("crate_1", "barrel_cluster", "pallet_stack", "rubble_pile",
                 "concrete_block_2"),
                (-40.0, 29.0, 40.0, 33.0), 12, 7102, out, spacing=3.2, avoid=avoid)
    lay.scatter(("glass_panel_2", "barrier_concrete_2", "crate_1", "sandbag_wall"),
                (-18.0, -20.0, 20.0, -2.0), 14, 7103, out, spacing=4.0, avoid=avoid)
    lay.scatter(("crate_1", "barrel_cluster", "pallet_stack"),
                (22.0, 22.0, 40.0, 32.0), 8, 7104, out, spacing=3.6, avoid=avoid)
    for position in ((-32.0, -14.0), (32.0, -14.0), (-32.0, 6.0), (0.0, -30.0),
                     (18.0, -12.0), (-24.0, 30.0), (24.0, 30.0)):
        out.append(lay.placement("light_mast_8", position, 0.0, "light"))
    out.append(lay.placement("sign_plate", (-14.4, -33.2, 2.2), 0.0, "signage"))
    out.append(lay.placement("sign_plate", (20.4, 12.0, 2.4), 90.0, "signage"))
    out.append(lay.placement("sign_plate", (-22.4, 20.0, 2.4), 270.0, "signage"))
    out.append(lay.placement("sign_plate", (-0.4, 33.4, 2.4), 180.0, "signage"))

    # ---- lane breaks ------------------------------------------------------
    #
    # The first audit that actually tested sightlines found the map's real defect: the
    # four corner spawns sat at the ends of three open service corridors - the west strip
    # between the office block and the fence, the east yard between the pipe racks and
    # the fence, and the strip behind the furnace hall - and a player spawning at one
    # end could be shot from the other, 53 to 79 m away, through twenty-seven clear
    # lines.  Props only covered the middle of the map; nothing stood in those strips.
    #
    # These five objects cut every one of those lines without walling the strips off:
    # two of them sit beside a 3-4 m gap that a player can still walk through, and the
    # blast wall across the north strip stands 1 m off the hall so the strip stays
    # walkable behind it.  They are placed where the kit already had nothing, so no
    # spawn shelter and no scatter prop moved.
    # Solid hoppers, not a conveyor run and not a thin wall: measured against the real
    # lines, the conveyor's lattice let an eye-height ray through between its legs, and
    # the blast wall at y 28.8-29.2 missed the eastern spawn slots by ten centimetres -
    # they sit at y 29.3, so the line ran just past the wall's face.  These four
    # footprints are solid at 1.6 m and they cover every line with margin.
    out.append(lay.placement("hopper_3", (37.5, -18.0, 0.0), 0.0, "lane_break"))
    out.append(lay.placement("hopper_3", (40.5, -18.0, 0.0), 0.0, "lane_break"))
    out.append(lay.placement("hopper_3", (-40.6, -12.0, 0.0), 0.0, "lane_break"))
    out.append(lay.placement("hopper_3", (0.0, 29.8, 0.0), 0.0, "lane_break"))

    # ---- spawn shelters: four corners, five slots each -------------------
    #
    # The walls belong on the corner side of the shelter, with the opening towards the
    # yard: built the other way round, a spawn was a box with a wall between the player
    # and the map, and the props sat in the five slots themselves - which is why the
    # first editor run spawned the camera inside a crate.
    for zone, (cx, cy) in sorted(SPAWN_ZONES.items()):
        face_x = 1.0 if cx < 0 else -1.0   # from the corner towards the yard
        face_y = 1.0 if cy < 0 else -1.0
        yaw = math.degrees(math.atan2(face_y, face_x))
        # back wall (against the corner) and the two side walls; open to the yard
        lay.side_run("wall_4", (cx - 4.0, cy - face_y * 4.0), (cx + 4.0, cy - face_y * 4.0),
                     out, tag=f"spawn_{zone}")
        # One back wall only. Side walls turned the shelter into a pen whose corner
        # slots stared at 4 m of concrete, so the yard side stays open.
        # a lean-to roof over the back half of the shelter, not over the firing line
        out.append(lay.placement("floor_slab_4", (cx, cy - face_y * 2.0, 3.6), 0.0,
                                 f"spawn_{zone}"))
        out.append(lay.placement("floor_slab_4", (cx - face_x * 2.0, cy, 3.6), 0.0,
                                 f"spawn_{zone}"))
        # props stand clear of the five slots, which are within 1.7 m of the centre
        out.append(lay.placement("crate_1", (cx - face_x * 2.9, cy - face_y * 2.9, 0.0),
                                 0.0, f"spawn_{zone}"))
        out.append(lay.placement("barrel_cluster", (cx - face_x * 2.6, cy + face_y * 3.4,
                                                    0.0), 0.0, f"spawn_{zone}"))
        out.append(lay.placement("sandbag_wall", (cx + face_x * 6.4, cy - face_y * 6.4, 0.0),
                                 yaw, f"spawn_{zone}"))
        out.append(lay.placement("floodlight_wall",
                                 (cx - face_x * 3.6, cy + face_y * 1.2, 2.4), yaw,
                                 f"spawn_{zone}"))

    counts["placements"] = out
    return out, counts


def build_spawns() -> List[dict]:
    """Twenty spawn slots: five per corner shelter, facing into the map."""
    spawns = []
    for zone, (cx, cy) in sorted(SPAWN_ZONES.items()):
        yaw = math.degrees(math.atan2(-cy, -cx))
        for index, (dx, dy) in enumerate(((0.0, 0.0), (1.7, -1.3), (-1.7, -1.3),
                                          (1.7, 1.3), (-1.7, 1.3)), start=1):
            spawns.append({
                "id": f"spawn_{zone.lower()}{index}",
                "zone": zone,
                "position": [round(cx + dx, 2), round(cy + dy, 2), 0.05],
                "yaw": round(yaw, 2),
                "marker": f"spawn_{zone.lower()}{index}",
            })
    return spawns


def build_nav() -> Tuple[List[dict], List[dict]]:
    nodes: List[dict] = []

    def next_id() -> int:
        return max([node["id"] for node in nodes], default=0) + 1

    # ---- ground ----------------------------------------------------------
    lay.nav_zone(nodes, (-40.0, -32.0, 40.0, 1.0), 0.15, "Exterior", step=8.0, cover=0.2)
    lay.nav_zone(nodes, (-40.0, 29.0, 40.0, 33.0), 0.15, "Exterior", step=8.0, cover=0.2)
    lay.nav_zone(nodes, (-18.0, 6.0, 18.0, 26.0), 0.15, "Indoor", step=7.0, cover=0.35)
    lay.nav_zone(nodes, (-13.0, -32.0, -7.0, 2.0), 0.15, "Tunnel", step=7.0, cover=0.3)
    lay.nav_zone(nodes, (-36.0, 10.0, -24.0, 22.0), 0.15, "Indoor", step=6.0, cover=0.3)
    lay.nav_zone(nodes, (23.5, -6.5, 38.5, 18.5), 0.15, "Exterior", step=7.0, cover=0.25)
    for zone, (cx, cy) in sorted(SPAWN_ZONES.items()):
        lay.nav_zone(nodes, (cx - 4.0, cy - 4.0, cx + 4.0, cy + 4.0), 0.15, "Spawn",
                     step=4.0, cover=0.45)
    # ---- elevated --------------------------------------------------------
    lay.nav_zone(nodes, (-5.0, 9.8, 5.0, 11.8), DECK, "Catwalk", step=4.0, cover=0.5)
    lay.nav_zone(nodes, (-5.0, 22.2, 5.0, 24.2), DECK, "Catwalk", step=4.0, cover=0.5)
    lay.nav_zone(nodes, (-7.2, 12.0, -5.2, 22.0), DECK, "Catwalk", step=3.0, cover=0.5)
    lay.nav_zone(nodes, (5.2, 12.0, 7.2, 22.0), DECK, "Catwalk", step=3.0, cover=0.5)
    lay.nav_zone(nodes, (23.2, -6.0, 24.8, 17.0), DECK, "Catwalk", step=4.0, cover=0.45)
    lay.nav_zone(nodes, (-36.0, 10.0, -24.0, 22.0), OFFICE_DECK_Z + 0.05, "Indoor",
                 step=6.0, cover=0.3)
    lay.nav_zone(nodes, (-36.0, 10.0, -24.0, 22.0), OFFICE_ROOF_Z + 0.15, "Elevated",
                 step=8.0, cover=0.1)
    lay.nav_zone(nodes, (-18.0, 6.0, 18.0, 26.0), ROOF_WALK + 0.05, "Elevated", step=8.0,
                 cover=0.1)
    lay.nav_zone(nodes, (14.0, 11.0, 18.0, 15.4), DECK, "Catwalk", step=4.0, cover=0.4)

    # ---- spawns ----------------------------------------------------------
    for spawn in build_spawns():
        node = lay.nav_node(next_id(), spawn["position"], "Spawn", 0.45, spawn["marker"])
        nodes.append(node)
        spawn["navNode"] = node["id"]

    # ---- marked nodes ----------------------------------------------------
    marked = [
        ((0.0, 11.0, 0.15), "Objective", 0.0, "landmark_kiln"),
        ((-30.0, 17.0, 0.15), "Exterior", 0.0, "landmark_chimney"),
        ((-28.0, -21.0, 0.5), "Exterior", 0.8, "cover_rail_wagon_west"),
        ((14.0, -21.0, 0.5), "Exterior", 0.8, "cover_rail_wagon_east"),
        ((-10.0, -21.0, 0.15), "Exterior", 0.3, "rail_crossing"),
        ((0.0, 33.0, 0.15), "Doorway", 0.3, "north_gate"),
        ((0.0, 3.0, 0.15), "Doorway", 0.3, "hall_main_gate"),
        ((12.0, 3.0, 0.15), "Doorway", 0.3, "hall_south_door"),
        ((0.0, 29.0, 0.15), "Doorway", 0.2, "hall_north_opening"),
        ((-21.0, 16.0, 0.15), "Doorway", 0.3, "office_hall_link"),
        ((-30.0, 7.0, 0.15), "Doorway", 0.3, "office_south_door"),
        ((-10.0, -33.0, 0.15), "Doorway", 0.2, "tunnel_south_mouth"),
        ((-10.0, 3.0, 0.15), "Doorway", 0.2, "tunnel_hall_mouth"),
        ((-5.0, -15.7, 0.15), "Doorway", 0.3, "tunnel_side_exit"),
        ((24.0, -18.0, 0.15), "Exterior", 0.4, "weighbridge"),
        ((-6.21, 17.0, DECK), "Catwalk", 0.4, "gantry_west"),
        ((6.21, 17.0, DECK), "Catwalk", 0.4, "gantry_east"),
        ((24.0, 8.0, DECK), "Catwalk", 0.3, "pipe_catwalk_mid"),
    ]
    for position, area, cover, marker in marked:
        nodes.append(lay.nav_node(next_id(), position, area, cover, marker))

    # ---- authored vertical connectors ------------------------------------
    # A height change can never be inferred from line of sight, so every stair,
    # ladder and bridge gets nodes at both ends, placed clear of the geometry
    # they belong to (in front of the first step, just past the last one).
    verticals = [
        # hall floor -> kiln ring gantry, west stair
        dict(bottom=(-13.6, 12.86, 0.15), top=(-6.6, 12.86, DECK), area_bottom="Indoor",
             area_top="Catwalk", marker_top="gantry_stair_west"),
        # hall floor -> kiln ring gantry, east stair
        dict(bottom=(13.6, 21.14, 0.15), top=(6.6, 21.14, DECK), area_bottom="Indoor",
             area_top="Catwalk", marker_top="gantry_stair_east"),
        # kiln maintenance ladder, outside the ring's east run
        dict(bottom=(8.6, 20.0, 0.15), top=(6.8, 20.0, DECK), area_bottom="Indoor",
             area_top="Catwalk", marker_top="kiln_ladder_top"),
        # internal stair tower: hall floor -> mid platform -> roof hatch
        dict(bottom=(13.8, 6.0, 0.15), top=(16.0, 12.0, DECK), area_bottom="Indoor",
             area_top="Catwalk", marker_top="stair_tower_mid"),
        dict(bottom=(16.0, 14.8, DECK), top=(16.0, 22.4, ROOF_WALK + 0.05),
             area_bottom="Catwalk", area_top="Elevated", marker_top="roof_hatch"),
        # office block: internal stair to the upper floor
        dict(bottom=(-31.8, 10.0, 0.15), top=(-30.0, 16.4, OFFICE_DECK_Z + 0.05),
             area_bottom="Indoor", area_top="Indoor", marker_top="office_upper_landing"),
        # office block: external ladder to its roof
        dict(bottom=(-39.6, 20.0, 0.15), top=(-36.8, 20.0, OFFICE_ROOF_Z + 0.15),
             area_bottom="Exterior", area_top="Elevated", marker_top="office_ladder_top"),
        # east pipe yard catwalk: two stairs up from the yard
        dict(bottom=(24.0, -8.4, 0.15), top=(24.0, -1.4, DECK), area_bottom="Exterior",
             area_top="Catwalk", marker_top="pipe_catwalk_south"),
        dict(bottom=(24.0, 19.4, 0.15), top=(24.0, 12.4, DECK), area_bottom="Exterior",
             area_top="Catwalk", marker_top="pipe_catwalk_north"),
    ]
    authored: List[dict] = []
    for spec in verticals:
        authored.append(lay.vertical(nodes, next_id, spec.pop("bottom"), spec.pop("top"),
                                     **spec))

    # Office roof -> 2 m bridge -> hall roof: three nodes, two links, one route.
    bridge_left = lay.nav_node(next_id(), (-24.6, 16.0, OFFICE_ROOF_Z + 0.15), "Elevated",
                               0.1, "office_roof_edge")
    nodes.append(bridge_left)
    bridge_mid = lay.nav_node(next_id(), (-21.0, 16.0, 7.72), "Catwalk", 0.2,
                              "office_roof_bridge")
    nodes.append(bridge_mid)
    bridge_right = lay.nav_node(next_id(), (-18.0, 16.0, ROOF_WALK + 0.05), "Elevated",
                                0.1, "hall_roof_west")
    nodes.append(bridge_right)
    authored.append(lay.nav_link(bridge_left["id"], bridge_mid["id"], cost=3.8))
    authored.append(lay.nav_link(bridge_mid["id"], bridge_right["id"], cost=3.4))

    built, notes = lay.build_nav(nodes, authored, _MANIFEST)
    for note in notes:
        print(f"[zavod7] nav: {note}")
    nodes, links, problems = lay.prune_unreachable(nodes, built)
    for problem in problems:
        print(f"[zavod7] nav: {problem}")
    return nodes, links


def build_layout() -> dict:
    """Everything except the nav graph, which needs the assembled scene."""
    placements, _ = build_placements()
    spawns = build_spawns()
    return {
        "id": MAP_ID,
        "name": MAP_NAME,
        "scene": SCENE_NAME,
        "mode": "FreeForAll",
        "comment": [
            "Generated by Tools/blender/build_maps.py from Tools/blender/layouts/zavod7.py.",
            "Kit modules load at runtime with Resources.Load<GameObject>(\"Kordon/Kit/<id>\"),",
            "shells with Resources.Load<GameObject>(\"Kordon/Maps/ffa_zavod7/<id>\").",
            "Positions are metres, yaw is degrees around Z, z is the module origin height.",
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
            {"id": "office", "region": list(OFFICE), "kind": "Indoor", "reverb": "room",
             "height": 7.2},
            {"id": "tunnel", "region": list(TUNNEL), "kind": "Indoor", "reverb": "tunnel",
             "height": 3.6},
            {"id": "yard", "region": list(BOUNDS), "kind": "Outdoor", "reverb": "yard",
             "height": 20.0},
        ],
        "landmarks": [
            {"id": "chimney_stack", "position": [-30.0, 20.0, 0.0], "height": 14.7,
             "label": "Plant stack"},
            {"id": "furnace_kiln", "position": [KILN[0], KILN[1], 0.0], "height": 8.1,
             "label": "Rotary kiln"},
            {"id": "rail_spur", "position": [-28.0, RAIL_Y, 0.0], "height": 2.3,
             "label": "Rail spur"},
            {"id": "stair_tower", "position": [16.0, 13.0, 0.0], "height": 8.1,
             "label": "Stair tower"},
        ],
        "routes": [
            {"id": "yard_hall", "name": "Yard and furnace hall", "kind": "ground",
             "markers": ["hall_main_gate", "landmark_kiln", "hall_north_opening"]},
            {"id": "catwalks_roof", "name": "Catwalks, gantry and roof", "kind": "elevated",
             "markers": ["gantry_west", "stair_tower_mid", "roof_hatch",
                         "office_ladder_top", "pipe_catwalk_mid"]},
            {"id": "service_tunnel", "name": "Service tunnel", "kind": "covered",
             "markers": ["tunnel_south_mouth", "tunnel_side_exit", "tunnel_hall_mouth"]},
        ],
        "rules": {
            "maxPlayers": 20,
            "killTarget": 50,
            "spawnProtectionSeconds": 2.5,
            "respawnDelaySeconds": 5.0,
            "spawnSelection": "zone-round-robin-with-distance-weighting",
        },
    }
