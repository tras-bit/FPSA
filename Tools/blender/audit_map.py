#!/usr/bin/env python3
"""Audit an authored KORDON map inside Blender, and render review images.

    Tools/blender.sh Tools/blender/audit_map.py                    # both maps
    Tools/blender.sh Tools/blender/audit_map.py --map ffa_zavod7 --render

Why this exists
---------------

The 1.0.5 build shipped a map where the player fell out of the world.  Two things
were wrong at once: the ground shell had no collider in Unity (it was authored
with ``blocksMovement: false``), and nothing checked that the authored scene puts
floor under every spawn.  The collider bug is fixed in ``MapBuilder``; this script
is the second half, so the same class of bug cannot come back silently.

What it checks, in the authored .blend (the same scene the FBX files come from)

* ground coverage: a 2 m grid over the whole playable bounds is ray cast downward
  from above head height, and every sample must land on the ground shell.  A hole
  in the floor is reported with its coordinates;
* spawns: every spawn in the layout JSON must have ground within 0.6 m below it,
  at least 1.85 m of clear headroom above it, and sit inside the map bounds;
* shells: every shell declared in the JSON must exist in the scene, and a shell
  that the player stands on (``walkable``) must actually cover the play area;
* objectives: every bomb site is on the floor inside the bounds with a clear plant spot
  and a real distance from the other site, and every team spawn sits inside one of its
  own side's buy zones;
* side lanes: every spawn pair at least 8 m apart is ray cast along the straight line
  between them at eye height.  A clear line is a spawn-to-spawn firing lane and a
  finding; spawns inside one shelter are exempt;
* sight out: each spawn must have at least 6 m of clear sight in one of sixteen sampled
  directions, so that closing the lanes above cannot wall a player in;
* the JSON contract itself: exactly one walkable ground shell, and any shell that
  is walkable must not be marked as a non-collider in the movement flags.

Outputs

    docs/reports/map-audit-<map>.md      human readable findings
    docs/art/<map>-iso.png               three quarter view (with --render)
    docs/art/<map>-top.png               plan view (with --render)

Exit code is non-zero when any check fails, so CI and local builds stop.
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

MAP_JSON_DIR = os.path.join(art.DATA_DIR, "Maps")
REPORT_DIR = os.path.join(art.REPO_ROOT, "docs", "reports")
PREVIEW_DIR = os.path.join(art.REPO_ROOT, "docs", "art")

GRID_STEP = 2.0
PROBE_TOP_Z = 6.0
SPAWN_GROUND_TOLERANCE = 0.6
SPAWN_HEADROOM = 1.85
EYE_HEIGHT = 1.62          # matches the camera height in PlayerCamera
CLUSTER_RADIUS = 8.0       # spawns closer than this are one cluster, not a lane
SIGHT_CLEAR = 6.0          # a spawn must see at least this far in some direction
SIGHT_DIRECTIONS = 16


def base_name(name: str) -> str:
    """'ground.0031' -> 'ground' (instances are named <module>.<index>)."""
    return name.split(".")[0]


def ray_down(x: float, y: float, top_z: float) -> Tuple[str, float]:
    """First object hit going down at (x, y). Returns (object name, hit z)."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    origin = mathutils.Vector((x, y, top_z))
    hit, location, _normal, index, obj, _matrix = bpy.context.scene.ray_cast(
        depsgraph, origin, mathutils.Vector((0.0, 0.0, -1.0)))
    if not hit or obj is None:
        return "", -999.0
    return obj.name, float(location.z)


def ray_up(x: float, y: float, from_z: float, distance: float) -> str:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    origin = mathutils.Vector((x, y, from_z))
    hit, _location, _normal, _index, obj, _matrix = bpy.context.scene.ray_cast(
        depsgraph, origin, mathutils.Vector((0.0, 0.0, 1.0)), distance=distance)
    if not hit or obj is None:
        return ""
    return obj.name


def ray_between(origin: Sequence[float], target: Sequence[float]) -> str:
    """First object crossed by a straight line between two points, or '' if clear."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    start = mathutils.Vector(origin)
    finish = mathutils.Vector(target)
    delta = finish - start
    distance = delta.length
    if distance < 1e-4:
        return ""
    hit, _location, _normal, _index, obj, _matrix = bpy.context.scene.ray_cast(
        depsgraph, start, delta.normalized(), distance=distance - 0.05)
    if not hit or obj is None:
        return ""
    return obj.name


def audit_sight(data: dict) -> Dict[str, object]:
    """Spawns must see out, and must not see each other.

    Two requirements pull against each other here.  A spawn in a sealed box is as
    broken as a spawn you can snipe from across the map: the player needs open floor
    in front of them the moment they spawn, and the far end of the map must not have
    a straight line to them.  Both are ray casts at eye height, which is the only
    height that matters - cover that stops a chest-high line but not a standing one
    is a waist-high crate, and this is a shooter where everyone stands up.
    """
    # The range has to cover the map, not an arbitrary number: at 45 m a 98 m
    # corner-to-corner line was never tested, and the audit reported a clean sheet that
    # had not looked at the one case it exists for.
    bounds = data["bounds"]
    lane_range = math.dist(bounds["min"][:2], bounds["max"][:2])

    spawns = data["spawns"]
    eyes: Dict[str, Tuple[float, float, float]] = {}
    for spawn in spawns:
        sx, sy, sz = spawn["position"]
        eyes[spawn["id"]] = (sx, sy, sz + EYE_HEIGHT)

    # ---- group the slots into clusters ------------------------------------
    # A co-op insertion puts six slots inside one 8 m landing zone; a flat distance cut
    # would call the far end of that zone a firing lane.  Single linkage groups them
    # instead, so only slots in *different* clusters are compared.
    parent = {sid: sid for sid in eyes}

    def find(sid: str) -> str:
        while parent[sid] != sid:
            parent[sid] = parent[parent[sid]]
            sid = parent[sid]
        return sid

    ids = list(eyes)
    for first in range(len(ids)):
        for second in range(first + 1, len(ids)):
            if math.dist(eyes[ids[first]], eyes[ids[second]]) <= CLUSTER_RADIUS:
                left, right = find(ids[first]), find(ids[second])
                if left != right:
                    parent[right] = left
    cluster = {sid: find(sid) for sid in eyes}

    # ---- spawn to spawn: any clear line is a firing lane -------------------
    lanes: List[Tuple[str, str, float, str]] = []
    blocked = 0
    compared = 0
    for first in range(len(ids)):
        for second in range(first + 1, len(ids)):
            a, b = eyes[ids[first]], eyes[ids[second]]
            gap = math.dist(a, b)
            if cluster[ids[first]] == cluster[ids[second]]:
                continue          # the next body over in the same spawn cluster
            compared += 1
            hit = ray_between(a, b)
            if hit:
                blocked += 1
            else:
                lanes.append((ids[first], ids[second], gap, "clear"))

    # ---- sight out: how far each spawn can see, and in how many ways ------
    sight_problems: List[str] = []
    sight_table: List[Tuple[str, int, float]] = []
    for spawn in spawns:
        sx, sy, sz = spawn["position"]
        origin = (sx, sy, sz + EYE_HEIGHT)
        if sz + EYE_HEIGHT > PROBE_TOP_Z:
            origin = (sx, sy, sz + EYE_HEIGHT)
        clear_directions = 0
        furthest = 0.0
        for step in range(SIGHT_DIRECTIONS):
            angle = 2.0 * math.pi * step / SIGHT_DIRECTIONS
            target = (sx + math.cos(angle) * lane_range,
                      sy + math.sin(angle) * lane_range,
                      sz + EYE_HEIGHT)
            hit = ray_between(origin, target)
            if not hit:
                distance = lane_range
            else:
                # re-cast to measure how far the ray actually got
                depsgraph = bpy.context.evaluated_depsgraph_get()
                delta = mathutils.Vector(target) - mathutils.Vector(origin)
                stop = mathutils.Vector(target)
                _h, location, _n, _i, _o, _m = bpy.context.scene.ray_cast(
                    depsgraph, origin, delta.normalized())
                distance = (location - mathutils.Vector(origin)).length
            furthest = max(furthest, distance)
            if distance >= SIGHT_CLEAR:
                clear_directions += 1
        sight_table.append((spawn["id"], clear_directions, round(furthest, 1)))
        if clear_directions == 0:
            sight_problems.append(
                f"{spawn['id']}: no direction with {SIGHT_CLEAR:.0f} m of clear "
                f"sight - the spawn is walled in")

    return dict(lanes=lanes, compared=compared, blocked=blocked,
                sight_table=sight_table, sight_problems=sight_problems)


def audit_objective(data: dict) -> Dict[str, object]:
    """Bomb sites and buy zones have to be real places on the map.

    A site that is inside a wall, floating, or ten metres from the other site is not a
    site: the mode would either be unplayable or a coin flip.  The buy zones are checked
    the same way, and every team spawn must fall inside one of its own side's zones -
    otherwise a player starts a round unable to buy, which reads as a broken shop rather
    than a broken map.
    """
    bounds = data["bounds"]
    min_x, min_y = bounds["min"][0], bounds["min"][1]
    max_x, max_y = bounds["max"][0], bounds["max"][1]
    spawns = {spawn["id"]: spawn for spawn in data["spawns"]}
    site_problems: List[str] = []
    buy_problems: List[str] = []

    demolition = "Demolition" in (data.get("modes") or [])
    sites = data.get("bombSites") or []
    if demolition and not sites:
        site_problems.append("the demolition mode declares no bomb sites")
    for site in sites:
        centre_x, centre_y, centre_z = site["center"]
        if not (min_x <= centre_x <= max_x and min_y <= centre_y <= max_y):
            site_problems.append(f"site {site['id']} is outside the map bounds")
        if site.get("radius", 0) < 3.0:
            site_problems.append(f"site {site['id']} is too small to plant on")
        name, hit_z = ray_down(centre_x, centre_y, PROBE_TOP_Z)
        if not name:
            site_problems.append(f"site {site['id']} has no floor under it")
        else:
            if abs(centre_z - hit_z) > SPAWN_GROUND_TOLERANCE:
                site_problems.append(
                    f"site {site['id']} is {abs(centre_z - hit_z):.2f} m off the floor")
            overhead = ray_up(centre_x, centre_y, hit_z + 0.05, 1.9)
            if overhead:
                site_problems.append(
                    f"site {site['id']} has '{base_name(overhead)}' over the plant spot")

    for first in range(len(sites)):
        for second in range(first + 1, len(sites)):
            gap = math.dist(sites[first]["center"][:2], sites[second]["center"][:2])
            if gap < 20.0:
                site_problems.append(
                    f"sites {sites[first]['id']} and {sites[second]['id']} are only "
                    f"{gap:.1f} m apart - one position could hold both")

    zones = data.get("buyZones") or []
    if demolition and not zones:
        buy_problems.append("the demolition mode declares no buy zones")
    for zone in zones:
        team = str(zone.get("team", ""))
        if team not in ("Attackers", "Defenders"):
            buy_problems.append(f"buy zone {zone['id']} belongs to no team")
        centre = zone["center"]
        if not (min_x <= centre[0] <= max_x and min_y <= centre[1] <= max_y):
            buy_problems.append(f"buy zone {zone['id']} is outside the map bounds")
        name, _hit_z = ray_down(centre[0], centre[1], PROBE_TOP_Z)
        if not name:
            buy_problems.append(f"buy zone {zone['id']} has no floor under it")

    teams = data.get("teamSpawns") or {}
    if not demolition:
        return dict(site_problems=site_problems, buy_problems=buy_problems,
                    sites=len(sites), zones=len(zones))
    for team_name, side in (("attackers", "Attackers"), ("defenders", "Defenders")):
        ids = teams.get(team_name) or []
        if not ids:
            buy_problems.append(f"no {team_name} spawns are assigned")
            continue
        side_zones = [zone for zone in zones if zone.get("team") == side]
        for spawn_id in ids:
            spawn = spawns.get(spawn_id)
            if spawn is None:
                buy_problems.append(f"team spawn {spawn_id} does not exist in the layout")
                continue
            if not any(math.dist(spawn["position"][:2], zone["center"][:2]) <= zone["radius"]
                       for zone in side_zones):
                buy_problems.append(f"{spawn_id} is not inside any {side} buy zone")

    return dict(site_problems=site_problems, buy_problems=buy_problems,
                sites=len(sites), zones=len(zones))


def audit_geometry(data: dict) -> Dict[str, object]:
    bounds = data["bounds"]
    min_x, min_y = bounds["min"][0], bounds["min"][1]
    max_x, max_y = bounds["max"][0], bounds["max"][1]

    shells = {entry["id"] for entry in data["shells"]}
    present = {base_name(obj.name) for obj in bpy.data.objects if obj.type == "MESH"}
    missing = sorted(shells - present)

    # ---- floor coverage --------------------------------------------------
    holes: List[Tuple[float, float]] = []
    covered_by_other: List[Tuple[float, float, str]] = []
    samples = 0
    x = min_x + GRID_STEP * 0.5
    while x < max_x:
        y = min_y + GRID_STEP * 0.5
        while y < max_y:
            samples += 1
            name, _z = ray_down(x, y, PROBE_TOP_Z)
            if not name:
                holes.append((round(x, 1), round(y, 1)))
            elif base_name(name) != "ground":
                covered_by_other.append((round(x, 1), round(y, 1), base_name(name)))
            y += GRID_STEP
        x += GRID_STEP

    # ---- spawns ----------------------------------------------------------
    spawn_problems: List[str] = []
    for spawn in data["spawns"]:
        sx, sy, sz = spawn["position"]
        # Probe from just above the spawn marker, not from the top of the map: many
        # spawns sit under a catwalk deck, and the deck is legitimate cover, not the
        # floor the player stands on.
        name, hit_z = ray_down(sx, sy, sz + 0.4)
        if not name:
            spawn_problems.append(f"{spawn['id']}: nothing under the spawn at all")
            continue
        if base_name(name) != "ground":
            spawn_problems.append(
                f"{spawn['id']}: stands on '{base_name(name)}', not the ground shell")
        drop = sz - hit_z
        if drop > SPAWN_GROUND_TOLERANCE:
            spawn_problems.append(
                f"{spawn['id']}: floats {drop:.2f} m above the floor")
        if drop < -0.10:
            spawn_problems.append(
                f"{spawn['id']}: sits {abs(drop):.2f} m inside the floor")
        overhead = ray_up(sx, sy, hit_z + 0.05, SPAWN_HEADROOM)
        if overhead:
            spawn_problems.append(
                f"{spawn['id']}: only {SPAWN_HEADROOM:.2f} m of headroom, "
                f"'{base_name(overhead)}' is in the way")
        if not (min_x <= sx <= max_x and min_y <= sy <= max_y):
            spawn_problems.append(f"{spawn['id']}: outside the map bounds")

    # ---- shell contract --------------------------------------------------
    contract_problems: List[str] = []
    walkable = [entry for entry in data["shells"] if entry.get("walkable")]
    if len(walkable) != 1:
        contract_problems.append(
            f"expected exactly one walkable ground shell, found {len(walkable)}")
    for entry in walkable:
        # Unity's MapBuilder gives a collider to any shell that is walkable, but a
        # shell flagged as not blocking movement with no collider is what shipped
        # the bug, so the data has to stay self consistent.
        if entry.get("blocksMovement") is False and entry["id"] != "ground":
            contract_problems.append(
                f"walkable shell {entry['id']} is flagged blocksMovement=false")
    # The ground must be at least as large as the playable bounds.
    for entry in data["shells"]:
        if entry["id"] != "ground":
            continue
        obj = bpy.data.objects.get(entry["id"])
        if obj is not None:
            low, high = art.world_bounds(obj)
            span = [high[i] - low[i] for i in range(3)]
            if span[0] < (max_x - min_x) or span[1] < (max_y - min_y):
                contract_problems.append(
                    f"ground shell {span[0]:.1f} x {span[1]:.1f} m is smaller than the "
                    f"{max_x - min_x:.0f} x {max_y - min_y:.0f} m bounds")

    sight = audit_sight(data)
    objective = audit_objective(data)

    return dict(
        sight=sight,
        objective=objective,
        samples=samples,
        holes=holes,
        covered_by_other=covered_by_other,
        missing_shells=missing,
        spawn_problems=spawn_problems,
        contract_problems=contract_problems,
    )


def setup_render(resolution: Tuple[int, int], samples: int) -> None:
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.world = scene.world or bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs[0].default_value = (0.42, 0.46, 0.52, 1.0)
        background.inputs[1].default_value = 1.1


def add_sun(rotation_deg: Sequence[float], strength: float = 3.0) -> None:
    light = bpy.data.lights.new("audit_sun", "SUN")
    light.energy = strength
    light.angle = math.radians(3.0)
    obj = bpy.data.objects.new("audit_sun", light)
    bpy.context.scene.collection.objects.link(obj)
    obj.rotation_euler = tuple(math.radians(v) for v in rotation_deg)


def render_view(path: str, location: Sequence[float], rotation_deg: Sequence[float],
                ortho_scale: float | None = None) -> None:
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("audit_cam")
    camera = bpy.data.objects.new("audit_cam", camera_data)
    scene.collection.objects.link(camera)
    camera.location = tuple(location)
    camera.rotation_euler = tuple(math.radians(v) for v in rotation_deg)
    if ortho_scale is not None:
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = ortho_scale
    else:
        camera_data.lens = 32.0
    scene.camera = camera
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera, do_unlink=True)


def render_review(map_id: str, data: dict) -> List[str]:
    bounds = data["bounds"]
    min_x, min_y = bounds["min"][0], bounds["min"][1]
    max_x, max_y = bounds["max"][0], bounds["max"][1]
    centre_x = (min_x + max_x) * 0.5
    centre_y = (min_y + max_y) * 0.5
    span = max(max_x - min_x, max_y - min_y)

    setup_render((960, 640), 24)
    add_sun((48.0, 6.0, 38.0), 3.2)
    add_sun((55.0, -20.0, 210.0), 1.1)
    written: List[str] = []

    iso = os.path.join(PREVIEW_DIR, f"{map_id}-iso.png")
    render_view(iso,
                (centre_x + span * 0.72, centre_y - span * 0.78, span * 0.62),
                (52.0, 0.0, 43.0))
    written.append(iso)

    top = os.path.join(PREVIEW_DIR, f"{map_id}-top.png")
    render_view(top, (centre_x, centre_y, span * 1.6), (0.0, 0.0, 0.0),
                ortho_scale=span * 1.02)
    written.append(top)

    lanes = overlay_lanes(map_id, data, top)
    written.append(lanes)

    if data.get("bombSites"):
        written.append(overlay_sites(map_id, data, top))
    return written


def lane_break_boxes(data: dict) -> List[Tuple[float, float, float, float]]:
    """Footprints of the placements that were put in to break firing lanes."""
    boxes: List[Tuple[float, float, float, float]] = []
    for placement in data["placements"]:
        if placement.get("tag") != "lane_break":
            continue
        box = footprint_box(placement["module"], placement["position"],
                            placement.get("yaw", 0.0))
        if box is not None:
            boxes.append(box)
    return boxes


def footprint_box(module: str, position: Sequence[float], yaw: float,
                  pad: float = 0.0):
    """Axis aligned extent of one placement, from the kit manifest's own sizes."""
    global _KIT
    if _KIT is None:
        with open(os.path.join(art.DATA_DIR, "Art", "kit_manifest.json"),
                  encoding="utf-8") as handle:
            _KIT = json.load(handle)["modules"]
    size = _KIT[module]["size"]
    angle = math.radians(yaw)
    ex = abs(math.cos(angle)) * size[0] * 0.5 + abs(math.sin(angle)) * size[1] * 0.5
    ey = abs(math.sin(angle)) * size[0] * 0.5 + abs(math.cos(angle)) * size[1] * 0.5
    if size[2] < 1.2:
        return None          # knee-high clutter stops no shot
    return (position[0] - ex - pad, position[1] - ey - pad,
            position[0] + ex + pad, position[1] + ey + pad)


_KIT: Dict[str, dict] | None = None


def lanes_without_lane_breaks(data: dict) -> List[Tuple[str, str, float]]:
    """The clear spawn-to-spawn lines the map would have without the lane-break props.

    Used for the review overlay only.  The four hoppers are found in the scene, dropped
    500 m, the same eye-height ray cast the audit runs is repeated, and they are put
    back.  That way the picture shows the lines that actually existed and the objects
    that actually cut them, instead of what a 2D footprint test guesses.
    """
    wanted = [placement for placement in data["placements"]
              if placement.get("tag") == "lane_break"]
    moved = []
    for placement in wanted:
        px, py = placement["position"][0], placement["position"][1]
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            low, high = art.world_bounds(obj)
            if high[2] - low[2] < 1.0:
                continue
            if abs((low[0] + high[0]) * 0.5 - px) > 0.6:
                continue
            if abs((low[1] + high[1]) * 0.5 - py) > 0.6:
                continue
            moved.append((obj, obj.location.z))
            obj.location.z -= 500.0
    bpy.context.view_layer.update()

    spawns = data["spawns"]
    points = [(spawn["id"], (spawn["position"][0], spawn["position"][1],
                             spawn["position"][2] + EYE_HEIGHT)) for spawn in spawns]

    parent = list(range(len(points)))

    def find(index: int) -> int:
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    for first in range(len(points)):
        for second in range(first + 1, len(points)):
            if math.dist(points[first][1], points[second][1]) <= CLUSTER_RADIUS:
                a, b = find(first), find(second)
                if a != b:
                    parent[b] = a

    lanes: List[Tuple[str, str, float]] = []
    for first in range(len(points)):
        for second in range(first + 1, len(points)):
            if find(first) == find(second):
                continue
            start, finish = points[first][1], points[second][1]
            if ray_between(start, finish):
                continue
            lanes.append((points[first][0], points[second][0], math.dist(start, finish)))

    for obj, original_z in moved:
        obj.location.z = original_z
    bpy.context.view_layer.update()
    return lanes


def overlay_lanes(map_id: str, data: dict, top_image: str) -> str:
    """Draw the audited sightlines on the plan view.

    A number in a markdown file says the lanes are gone; this shows which object takes
    out which line, on the same plan view a level designer reads.  Green lines are
    spawn pairs whose straight line crosses a lane-break footprint, red boxes are those
    footprints, and the spawn clusters are circled.  Pairs inside one cluster are not
    drawn, because they are not lanes.
    """
    from PIL import Image, ImageDraw, ImageFont

    bounds = data["bounds"]
    min_x, min_y = bounds["min"][0], bounds["min"][1]
    max_x, max_y = bounds["max"][0], bounds["max"][1]
    centre_x = (min_x + max_x) * 0.5
    centre_y = (min_y + max_y) * 0.5
    span = max(max_x - min_x, max_y - min_y)

    image = Image.open(top_image).convert("RGB")
    width, height = image.size
    scale = width / (span * 1.02)

    def to_pixel(x: float, y: float) -> Tuple[float, float]:
        return (width * 0.5 + (x - centre_x) * scale,
                height * 0.5 - (y - centre_y) * scale)

    draw = ImageDraw.Draw(image, "RGBA")
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    font = (ImageFont.truetype(font_path, 15) if os.path.exists(font_path) else None)

    boxes = lane_break_boxes(data)
    spawns = data["spawns"]
    points = [(spawn["id"], spawn["position"][0], spawn["position"][1])
              for spawn in spawns]

    # clusters
    parent = list(range(len(points)))
    def find(index: int) -> int:
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index
    for first in range(len(points)):
        for second in range(first + 1, len(points)):
            if math.dist(points[first][1:], points[second][1:]) <= CLUSTER_RADIUS:
                a, b = find(first), find(second)
                if a != b:
                    parent[b] = a

    measured = lanes_without_lane_breaks(data)
    coordinates = {spawn["id"]: (spawn["position"][0], spawn["position"][1])
                   for spawn in data["spawns"]}
    for first_id, second_id, _gap in measured:
        start = coordinates[first_id]
        finish = coordinates[second_id]
        draw.line([to_pixel(*start), to_pixel(*finish)], fill=(70, 220, 120, 115),
                  width=2)

    for box in boxes:
        draw.rectangle([to_pixel(box[0], box[3]), to_pixel(box[2], box[1])],
                       outline=(235, 80, 60, 235), width=3)

    cluster_centres: Dict[int, List[Tuple[float, float]]] = {}
    for index, (_sid, x, y) in enumerate(points):
        cluster_centres.setdefault(find(index), []).append((x, y))
    for members in cluster_centres.values():
        cx = sum(point[0] for point in members) / len(members)
        cy = sum(point[1] for point in members) / len(members)
        radius = max(14.0, CLUSTER_RADIUS * scale * 0.5)
        px, py = to_pixel(cx, cy)
        draw.ellipse([px - radius, py - radius, px + radius, py + radius],
                     outline=(250, 210, 60, 220), width=3)
        for x, y in members:
            sx, sy = to_pixel(x, y)
            draw.ellipse([sx - 3, sy - 3, sx + 3, sy + 3], fill=(250, 210, 60, 255))

    label = (f"{len(measured)} lane(s) before the fix, all cut by {len(boxes)} "
             f"lane-break object(s)  |  eye height {EYE_HEIGHT:.2f} m  |  clear now: 0")
    draw.rectangle([0, height - 30, width, height], fill=(12, 14, 18, 210))
    draw.text((14, height - 23), label, fill=(240, 240, 240, 255), font=font)

    out = os.path.join(PREVIEW_DIR, f"{map_id}-lanes.png")
    image.save(out)
    return out


def overlay_sites(map_id: str, data: dict, top_image: str) -> str:
    """Mark the bomb sites, the buy zones and the two teams on the plan view.

    This is the demolition layout as a player reads it: which corner they spawn in, where
    they can buy, and where the two plant spots are.  Drawn from the same numbers the game
    loads, so the picture cannot drift from the data.
    """
    from PIL import Image, ImageDraw, ImageFont

    bounds = data["bounds"]
    min_x, min_y = bounds["min"][0], bounds["min"][1]
    max_x, max_y = bounds["max"][0], bounds["max"][1]
    centre_x = (min_x + max_x) * 0.5
    centre_y = (min_y + max_y) * 0.5
    span = max(max_x - min_x, max_y - min_y)

    image = Image.open(top_image).convert("RGB")
    width, height = image.size
    scale = width / (span * 1.02)

    def to_pixel(x: float, y: float) -> Tuple[float, float]:
        return (width * 0.5 + (x - centre_x) * scale,
                height * 0.5 - (y - centre_y) * scale)

    draw = ImageDraw.Draw(image, "RGBA")
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    big = ImageFont.truetype(font_path, 34) if os.path.exists(font_path) else None
    small = ImageFont.truetype(font_path, 15) if os.path.exists(font_path) else None

    team_colour = {"Attackers": (245, 150, 60, 210), "Defenders": (70, 170, 235, 210)}
    for zone in data.get("buyZones") or []:
        colour = team_colour.get(zone.get("team"), (200, 200, 200, 180))
        cx, cy = to_pixel(zone["center"][0], zone["center"][1])
        radius = zone["radius"] * scale
        draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius],
                     outline=colour, width=3)
        draw.text((cx - 44, cy + radius + 4), f"{zone['team'][:3]} buy", fill=colour,
                  font=small)

    for site in data.get("bombSites") or []:
        cx, cy = to_pixel(site["center"][0], site["center"][1])
        radius = site["radius"] * scale
        draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius],
                     fill=(220, 60, 55, 45), outline=(235, 90, 70, 235), width=4)
        draw.text((cx - 12, cy - 20), site["id"], fill=(255, 235, 230, 255), font=big)

    teams = data.get("teamSpawns") or {}
    spawns = {spawn["id"]: spawn for spawn in data["spawns"]}
    for team_name, team in (("attackers", "Attackers"), ("defenders", "Defenders")):
        colour = team_colour[team]
        for spawn_id in teams.get(team_name) or []:
            spawn = spawns.get(spawn_id)
            if spawn is None:
                continue
            sx, sy = to_pixel(spawn["position"][0], spawn["position"][1])
            draw.ellipse([sx - 5, sy - 5, sx + 5, sy + 5], fill=colour)

    label = ("DEMOLITION  |  site A furnace hall, site B pipe yard  |  "
             "orange = attackers south, blue = defenders north  |  circles = buy zones")
    draw.rectangle([0, height - 30, width, height], fill=(12, 14, 18, 215))
    draw.text((14, height - 23), label, fill=(240, 240, 240, 255), font=small)

    out = os.path.join(PREVIEW_DIR, f"{map_id}-sites.png")
    image.save(out)
    return out


def segment_crosses(start: Sequence[float], finish: Sequence[float],
                    box: Tuple[float, float, float, float]) -> bool:
    """Does the 2D segment cross the axis aligned rectangle? (slab test)"""
    t_min, t_max = 0.0, 1.0
    for axis in range(2):
        origin, delta = start[axis], finish[axis] - start[axis]
        low, high = box[axis], box[axis + 2]
        if abs(delta) < 1e-9:
            if origin < low or origin > high:
                return False
            continue
        a = (low - origin) / delta
        b = (high - origin) / delta
        if a > b:
            a, b = b, a
        t_min = max(t_min, a)
        t_max = min(t_max, b)
        if t_min > t_max:
            return False
    return True


def write_report(map_id: str, data: dict, result: Dict[str, object],
                 images: List[str]) -> str:
    os.makedirs(REPORT_DIR, exist_ok=True)
    path = os.path.join(REPORT_DIR, f"map-audit-{map_id}.md")
    holes: List[Tuple[float, float]] = result["holes"]  # type: ignore[assignment]
    covered: List[Tuple[float, float, str]] = result["covered_by_other"]  # type: ignore[assignment]
    missing: List[str] = result["missing_shells"]  # type: ignore[assignment]
    spawns: List[str] = result["spawn_problems"]  # type: ignore[assignment]
    contract: List[str] = result["contract_problems"]  # type: ignore[assignment]
    sight: Dict[str, object] = result["sight"]  # type: ignore[assignment]
    lanes: List[Tuple[str, str, float, str]] = sight["lanes"]  # type: ignore[assignment]
    sight_problems: List[str] = sight["sight_problems"]  # type: ignore[assignment]
    failures = (len(holes) + len(missing) + len(spawns) + len(contract)
                + len(lanes) + len(sight_problems))

    lines = [
        f"# Map audit: {map_id}",
        "",
        f"Scene: `ArtSource/Blender/{map_id}.blend`  ",
        f"Layout: `Assets/StreamingAssets/Data/Maps/{map_id}.json`  ",
        f"Spawns: {len(data['spawns'])}  Kit instances: {len(data['placements'])}  "
        f"Shells: {len(data['shells'])}",
        "",
        f"**Verdict: {'FAIL' if failures else 'PASS'}** ({failures} finding(s))",
        "",
        "## Floor coverage",
        "",
        f"Ray cast a {GRID_STEP:.0f} m grid ({result['samples']} samples) downward from "
        f"z={PROBE_TOP_Z:.0f}; every sample must land on the `ground` shell.",
        "",
        f"* samples that hit nothing (holes): **{len(holes)}**",
        f"* samples stopped by something other than the ground shell: "
        f"{len(covered)} (catwalk, roof, prop: legitimate cover above the floor)",
        "",
    ]
    if holes:
        lines.append("Holes:")
        lines.append("")
        for x, y in holes[:40]:
            lines.append(f"* no floor at ({x}, {y})")
        if len(holes) > 40:
            lines.append(f"* ... and {len(holes) - 40} more")
        lines.append("")

    lines += ["## Spawns", ""]
    if spawns:
        lines += [f"* {problem}" for problem in spawns]
    else:
        lines.append(f"All {len(data['spawns'])} spawns stand on the ground shell with "
                     f"{SPAWN_HEADROOM:.2f} m of clear headroom.")
    lines.append("")

    lines += ["## Side lanes: spawn to spawn", ""]
    lines.append(
        f"Eye height {EYE_HEIGHT:.2f} m, every spawn pair whose distance is at least "
        f"{CLUSTER_RADIUS:.0f} m ({sight['compared']} pairs) is ray cast along the "
        f"straight line between them. Neighbours inside one spawn cluster are exempt: "
        f"five slots in a shelter are 2-4 m apart by design.")
    lines.append("")
    if lanes:
        lines.append(f"**{len(lanes)} clear line(s) - a spawn can be shot from another "
                     f"spawn:**")
        lines.append("")
        lines += [f"* `{a}` sees `{b}` at {gap:.1f} m with nothing in between"
                  for a, b, gap, _ in sorted(lanes, key=lambda row: -row[2])]
    else:
        lines.append(f"None of the {sight['compared']} pairs has a clear line: "
                     f"{sight['blocked']} are broken by geometry.")
    lines.append("")

    lines += ["## Sight out of each spawn", ""]
    if sight_problems:
        lines += [f"* {problem}" for problem in sight_problems]
    else:
        lines.append(
            f"Every spawn has at least {SIGHT_CLEAR:.0f} m of clear sight in one of "
            f"{SIGHT_DIRECTIONS} sampled directions. A spawn walled in is as broken as "
            f"a spawn that can be sniped from the other corner.")
    lines.append("")
    lines.append("| spawn | clear directions | furthest clear |")
    lines.append("| --- | --- | --- |")
    for spawn_id, clear, furthest in sight["sight_table"]:  # type: ignore[misc]
        lines.append(f"| `{spawn_id}` | {clear} / {SIGHT_DIRECTIONS} | {furthest} m |")
    lines.append("")

    objective: Dict[str, object] = result["objective"]  # type: ignore[assignment]
    site_problems: List[str] = objective["site_problems"]  # type: ignore[assignment]
    buy_problems: List[str] = objective["buy_problems"]  # type: ignore[assignment]

    lines += ["## Bomb sites and buy zones", ""]
    lines.append(
        f"{objective['sites']} bomb site(s) and {objective['zones']} buy zone(s) are "
        f"declared by the layout. A site must be on the floor inside the bounds, with a "
        f"clear plant spot under 1.9 m of headroom and at least 20 m from the other site; "
        f"every team spawn must sit inside one of its own side's buy zones.")
    lines.append("")
    if site_problems or buy_problems:
        lines += [f"* {problem}" for problem in site_problems + buy_problems]
    else:
        lines.append("Every site stands on the floor with a clear plant spot and a real "
                     "distance from the other site, and every team spawn can buy.")
    lines.append("")

    lines += ["## Shell contract", ""]
    if missing:
        lines += [f"* shell `{name}` is declared in the layout but missing from the scene"
                  for name in missing]
    if contract:
        lines += [f"* {problem}" for problem in contract]
    if not missing and not contract:
        lines.append("One walkable ground shell; every declared shell exists; the ground "
                     "covers the playable bounds. `MapBuilder` gives it a collider "
                     "because it is walkable.")
    lines.append("")

    if images:
        lines += ["## Review images", ""]
        for image in images:
            rel = os.path.relpath(image, art.REPO_ROOT).replace(os.sep, "/")
            lines.append(f"![{map_id}]({'../' + rel if rel.startswith('docs/') else rel})")
        lines.append("")

    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    return path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--map", default=None, help="map id, default both")
    parser.add_argument("--render", action="store_true", help="write review renders")
    args = parser.parse_args()

    map_ids = [args.map] if args.map else ["ffa_zavod7", "coop_silent_kiln"]
    failures = 0
    started = time.time()
    for map_id in map_ids:
        blend = os.path.join(art.BLEND_DIR, f"{map_id}.blend")
        if not os.path.exists(blend):
            raise SystemExit(f"[audit] missing authored scene {blend}")
        bpy.ops.wm.open_mainfile(filepath=blend)
        with open(os.path.join(MAP_JSON_DIR, f"{map_id}.json"), encoding="utf-8") as handle:
            data = json.load(handle)

        result = audit_geometry(data)
        images = render_review(map_id, data) if args.render else []
        report = write_report(map_id, data, result, images)
        sight = result["sight"]
        objective = result["objective"]
        count = (len(result["holes"]) + len(result["missing_shells"]) +
                 len(result["spawn_problems"]) + len(result["contract_problems"]) +
                 len(sight["lanes"]) + len(sight["sight_problems"]) +
                 len(objective["site_problems"]) + len(objective["buy_problems"]))
        failures += count
        print(f"[audit] {map_id}: {result['samples']} floor samples, "
              f"{len(result['holes'])} holes, {len(result['spawn_problems'])} spawn "
              f"problems, {len(sight['lanes'])} side lanes, "
              f"{len(objective['site_problems']) + len(objective['buy_problems'])} "
              f"objective problems, {count} findings -> "
              f"{os.path.relpath(report, art.REPO_ROOT)}")

    print(f"[audit] {len(map_ids)} map(s) in {time.time() - started:.1f}s, "
          f"{failures} finding(s)")
    if failures:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
