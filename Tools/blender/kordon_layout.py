#!/usr/bin/env python3
"""Layout and navigation authoring helpers shared by both maps.

A map layout is plain data (module id + position + yaw) produced by the functions
here, so the maps are reviewable as numbers and reproducible byte for byte:

* ``side_run``   - place modules along a segment, skipping door/gate openings and
  filling the remainder with 2 m / 1 m pieces;
* ``room``       - four walls with declared openings, optional second tier;
* ``grid_fill``  - floor / roof tiles over a rectangle;
* ``scatter``    - deterministic prop scatter inside a rectangle (seeded);
* ``nav_zone``   - navigation nodes on a grid inside a walkable zone;
* ``build_nav``  - link nodes by chest height line of sight through the real
  geometry (``scene.ray_cast``), then add the authored vertical connectors;
* ``validate_nav`` - the same checks ``Kordon.Core.Nav.NavGraph.Validate`` runs,
  so a disconnected graph fails here instead of in the test suite.
"""
from __future__ import annotations

import math
import random
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import bpy
import mathutils

Vector = mathutils.Vector

NAV_AREAS = {"Open", "Cover", "Indoor", "Elevated", "Tunnel", "Doorway", "Exterior",
             "Catwalk", "Objective", "Extraction", "Spawn"}

LOS_PROBE_HEIGHT = 1.35
MAX_LINK_DISTANCE = 14.0
MIN_LINK_DISTANCE = 1.2


# --------------------------------------------------------------------------
# placements
# --------------------------------------------------------------------------
def placement(module: str, position: Sequence[float], yaw: float = 0.0,
              tag: str = "") -> dict:
    """One placement entry; a two component position is read as z = 0."""
    z = float(position[2]) if len(position) > 2 else 0.0
    entry = {
        "module": module,
        "position": [round(float(position[0]), 3), round(float(position[1]), 3),
                     round(z, 3)],
        "yaw": round(float(yaw) % 360.0, 2),
    }
    if tag:
        entry["tag"] = tag
    return entry


def _unit(start: Sequence[float], end: Sequence[float]) -> Tuple[Vector, float]:
    delta = Vector((end[0] - start[0], end[1] - start[1], 0.0))
    length = delta.length
    return (delta.normalized() if length > 1e-6 else Vector((1.0, 0.0, 0.0))), length


def unit_vector(start: Sequence[float], end: Sequence[float]) -> Vector:
    """Horizontal unit vector from start to end (public wrapper over _unit)."""
    return _unit(start, end)[0]


def yaw_for(start: Sequence[float], end: Sequence[float]) -> float:
    unit, _ = _unit(start, end)
    return math.degrees(math.atan2(unit.y, unit.x))


def side_run(module: str, start: Sequence[float], end: Sequence[float],
             out: List[dict], z: float = 0.0, step: Optional[float] = None,
             openings: Sequence[Tuple[float, float]] = (),
             fillers: Sequence[str] = ("wall_2", "wall_1"), seal: bool = True,
             tag: str = "") -> int:
    """Place `module` end to end from start to end, leaving the openings clear.

    `openings` are (distance along the run, width) pairs - doorways, gates or
    breached sections.  Leftover space shorter than a full module is filled with
    the smaller filler modules, and with `seal` the last filler is aligned to the
    end of the segment so a wall run can never finish with a gap: an overlapping
    wall is invisible, a crack leaks line of sight and lets bullets through.
    Pass `seal=False` for catwalks and railings, where an overlap would show.
    """
    unit, length = _unit(start, end)
    yaw = yaw_for(start, end)
    module_step = step if step else _module_length(module)
    if module_step <= 0:
        raise ValueError(f"unknown module '{module}' - build the kit first")

    blocked: List[Tuple[float, float]] = []
    for centre, width in openings:
        blocked.append((max(0.0, centre - width * 0.5), min(length, centre + width * 0.5)))
    blocked.sort()

    intervals: List[Tuple[float, float]] = []
    cursor = 0.0
    for low, high in blocked:
        if low > cursor + 0.01:
            intervals.append((cursor, low))
        cursor = max(cursor, high)
    if cursor < length - 0.01:
        intervals.append((cursor, length))

    placed = 0
    for low, high in intervals:
        span = high - low
        count = int(math.floor(span / module_step + 1e-4))
        for i in range(count):
            distance = low + module_step * (i + 0.5)
            point = Vector((start[0], start[1], z)) + unit * distance
            out.append(placement(module, (point.x, point.y, z), yaw, tag))
            placed += 1
        remainder = span - count * module_step
        cursor_distance = low + count * module_step
        for filler in fillers:
            filler_step = _module_length(filler)
            while remainder >= filler_step - 1e-3 and filler_step > 0:
                point = Vector((start[0], start[1], z)) + unit * (cursor_distance + filler_step * 0.5)
                out.append(placement(filler, (point.x, point.y, z), yaw, tag))
                cursor_distance += filler_step
                remainder -= filler_step
                placed += 1
            if remainder < 0.25:
                break
        if seal and remainder > 0.04 and fillers:
            plug = fillers[-1]
            plug_step = _module_length(plug)
            if plug_step > 0:
                point = Vector((start[0], start[1], z)) + unit * (high - plug_step * 0.5)
                out.append(placement(plug, (point.x, point.y, z), yaw, tag))
                placed += 1
    return placed


def grid_fill(module: str, min_corner: Sequence[float], max_corner: Sequence[float],
              out: List[dict], z: float = 0.0, step: Optional[float] = None,
              yaw: float = 0.0, skip=None, tag: str = "") -> int:
    """Tile a rectangle with a floor / roof module."""
    size = step if step else _module_length(module)
    placed = 0
    x = min_corner[0] + size * 0.5
    while x <= max_corner[0] + 1e-4:
        y = min_corner[1] + size * 0.5
        while y <= max_corner[1] + 1e-4:
            if skip is None or not skip(x, y):
                out.append(placement(module, (x, y, z), yaw, tag))
                placed += 1
            y += size
        x += size
    return placed


def stack(module: str, position: Sequence[float], levels: int, out: List[dict],
          level_height: float = 3.6, yaw: float = 0.0, tag: str = "") -> int:
    """Stack the same module vertically (two tier hall walls, chimney bands...)."""
    for level in range(levels):
        out.append(placement(module, (position[0], position[1],
                                      position[2] + level * level_height), yaw, tag))
    return levels


def scatter(items: Sequence[str], region: Sequence[float], count: int, seed: int,
            out: List[dict], z: float = 0.0, spacing: float = 2.2,
            avoid: Sequence[Sequence[float]] = ()) -> int:
    """Deterministic prop scatter: same seed, same map, every run."""
    rng = random.Random(seed)
    min_x, min_y, max_x, max_y = region
    placed_points: List[Tuple[float, float]] = []
    placed = 0
    attempts = 0
    while placed < count and attempts < count * 60:
        attempts += 1
        x = rng.uniform(min_x, max_x)
        y = rng.uniform(min_y, max_y)
        if any((x - px) ** 2 + (y - py) ** 2 < spacing * spacing for px, py in placed_points):
            continue
        if any(min_x2 <= x <= max_x2 and min_y2 <= y <= max_y2
               for min_x2, min_y2, max_x2, max_y2 in avoid):
            continue
        module = items[placed % len(items)] if len(items) > 1 else items[0]
        out.append(placement(module, (x, y, z), rng.uniform(0.0, 360.0), "scatter"))
        placed_points.append((x, y))
        placed += 1
    return placed


_MODULE_SIZES: Dict[str, Sequence[float]] = {}


def set_module_sizes(sizes: Dict[str, Sequence[float]]) -> None:
    """Fed from kit_manifest.json so layout maths uses the authored dimensions."""
    _MODULE_SIZES.clear()
    for module, size in sizes.items():
        _MODULE_SIZES[module] = size


def _module_length(module: str) -> float:
    size = _MODULE_SIZES.get(module)
    return float(size[0]) if size else 0.0


def module_size(module: str) -> Sequence[float]:
    return _MODULE_SIZES.get(module, (0.0, 0.0, 0.0))


def room(min_corner: Sequence[float], max_corner: Sequence[float], out: List[dict],
         wall: str = "wall_4", z: float = 0.0, tiers: int = 1, tier_height: float = 3.6,
         openings: Optional[Dict[str, Sequence[Tuple[float, float]]]] = None,
         corner: str = "corner_outer", tag: str = "") -> int:
    """Four walls around a rectangle with per-side openings."""
    openings = openings or {}
    min_x, min_y = min_corner[0], min_corner[1]
    max_x, max_y = max_corner[0], max_corner[1]
    sides = {
        "south": ((min_x, min_y), (max_x, min_y)),
        "north": ((max_x, max_y), (min_x, max_y)),
        "west": ((min_x, max_y), (min_x, min_y)),
        "east": ((max_x, min_y), (max_x, max_y)),
    }
    placed = 0
    for level in range(tiers):
        for name, (start, end) in sides.items():
            placed += side_run(wall, start, end, out, z=z + level * tier_height,
                               openings=openings.get(name, ()), tag=tag)
    for cx, cy in ((min_x, min_y), (max_x, min_y), (max_x, max_y), (min_x, max_y)):
        for level in range(tiers):
            out.append(placement(corner, (cx, cy, z + level * tier_height), 0.0, tag))
            placed += 1
    return placed


# --------------------------------------------------------------------------
# navigation
# --------------------------------------------------------------------------
def nav_node(node_id: int, position: Sequence[float], area: str = "Open",
             cover: float = 0.0, marker: str = "") -> dict:
    if area not in NAV_AREAS:
        raise ValueError(f"unknown nav area '{area}' (must match Kordon.Core.Nav.NavArea)")
    return {
        "id": int(node_id),
        "position": {"x": round(float(position[0]), 3),
                     "y": round(float(position[1]), 3),
                     "z": round(float(position[2]), 3)},
        "area": area,
        "cover": round(float(cover), 3),
        "marker": marker,
    }


def nav_zone(nodes: List[dict], region: Sequence[float], z: float, area: str = "Exterior",
             step: float = 6.0, cover: float = 0.0, margin: float = 1.0) -> List[int]:
    """Grid of navigation nodes over a walkable rectangle; returns the new ids."""
    min_x, min_y, max_x, max_y = region
    next_id = max([node["id"] for node in nodes], default=0) + 1
    created: List[int] = []
    x = min_x + margin
    while x <= max_x - margin + 1e-4:
        y = min_y + margin
        while y <= max_y - margin + 1e-4:
            nodes.append(nav_node(next_id, (x, y, z), area, cover))
            created.append(next_id)
            next_id += 1
            y += step
        x += step
    return created


def nav_link(a: int, b: int, cost: float = 0.0, crouch: bool = False, jump: bool = False,
             jump_height: float = 0.0, one_way: bool = False) -> dict:
    link = {"from": int(a), "to": int(b)}
    if cost > 0:
        link["cost"] = round(float(cost), 3)
    if crouch:
        link["crouch"] = True
    if jump:
        link["jump"] = True
        link["jumpHeight"] = round(float(jump_height), 3)
    if one_way:
        link["oneWay"] = True
    return link


def _non_blocking_names(manifest: Dict[str, dict]) -> set:
    return {module for module, entry in manifest.items() if not entry.get("blocksSight", True)}


def _base_name(name: str) -> str:
    return name.split(".")[0]


def build_nav(nodes: List[dict], authored_links: List[dict], manifest: Dict[str, dict],
              max_distance: float = MAX_LINK_DISTANCE) -> Tuple[List[dict], List[str]]:
    """Link nodes that can see each other at chest height, then add the authored
    vertical connectors (stairs, ladders, jumps, one way drops).

    Line of sight is tested against the real assembled geometry with
    ``scene.ray_cast``, and modules the kit marks as not blocking sight (floors,
    railings, ladders, fences, lights) are ignored.
    """
    notes: List[str] = []
    pass_through = _non_blocking_names(manifest)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    scene = bpy.context.scene

    links: List[dict] = []
    seen_pairs = set()
    for i, first in enumerate(nodes):
        for second in nodes[i + 1:]:
            a = Vector((first["position"]["x"], first["position"]["y"], first["position"]["z"]))
            b = Vector((second["position"]["x"], second["position"]["y"], second["position"]["z"]))
            delta = b - a
            horizontal = math.hypot(delta.x, delta.y)
            if horizontal > max_distance or horizontal < MIN_LINK_DISTANCE:
                continue
            if abs(delta.z) > 1.2:
                continue  # height changes need an authored connector
            start = a + Vector((0.0, 0.0, LOS_PROBE_HEIGHT))
            direction = (b + Vector((0.0, 0.0, LOS_PROBE_HEIGHT))) - start
            distance = direction.length
            direction.normalize()
            hit, _loc, _nor, _idx, obj, _mat = scene.ray_cast(depsgraph, start, direction,
                                                             distance=distance)
            if hit and _base_name(obj.name) not in pass_through:
                continue
            key = (min(first["id"], second["id"]), max(first["id"], second["id"]))
            if key in seen_pairs:
                continue
            seen_pairs.add(key)
            links.append(nav_link(first["id"], second["id"]))

    known = {node["id"] for node in nodes}
    for link in authored_links:
        if link["from"] not in known or link["to"] not in known:
            notes.append(f"authored link references an unknown node: {link}")
            continue
        key = (min(link["from"], link["to"]), max(link["from"], link["to"]))
        if key in seen_pairs:
            continue
        seen_pairs.add(key)
        links.append(link)

    notes.append(f"{len(nodes)} nodes, {len(links)} links "
                 f"({len(links) - len(authored_links)} by line of sight)")
    return links, notes


def vertical(nodes: List[dict], next_id, bottom: Sequence[float], top: Sequence[float],
             area_bottom: str = "Exterior", area_top: str = "Catwalk",
             marker_bottom: str = "", marker_top: str = "", jump: bool = False,
             jump_height: float = 1.1, cover: float = 0.2) -> dict:
    """Create the two nodes at the ends of a stair, ladder or drop and link them.

    Line of sight cannot infer a height change, so every vertical connector in a
    map is authored explicitly: the nodes are placed clear of the geometry they
    belong to (in front of the first step, just past the last one) and the link
    carries the real travelled distance as its cost.
    """
    a = nav_node(next_id(), bottom, area_bottom, cover, marker_bottom)
    nodes.append(a)                      # appended before the next id is taken
    b = nav_node(next_id(), top, area_top, cover, marker_top)
    nodes.append(b)
    travelled = math.dist((bottom[0], bottom[1], bottom[2]), (top[0], top[1], top[2]))
    link = nav_link(a["id"], b["id"], cost=round(max(travelled, 1.0), 3), jump=jump,
                    jump_height=jump_height if jump else 0.0)
    return link


def reachable(nodes: List[dict], links: List[dict]) -> set:
    adjacency: Dict[int, set] = {node["id"]: set() for node in nodes}
    for link in links:
        adjacency.setdefault(link["from"], set()).add(link["to"])
        adjacency.setdefault(link["to"], set()).add(link["from"])
    if not nodes:
        return set()
    start = nodes[0]["id"]
    stack = [start]
    seen = {start}
    while stack:
        current = stack.pop()
        for neighbour in adjacency.get(current, ()):
            if neighbour not in seen:
                seen.add(neighbour)
                stack.append(neighbour)
    return seen


def prune_unreachable(nodes: List[dict], links: List[dict],
                      keep_markers: bool = True) -> Tuple[List[dict], List[dict], List[str]]:
    """Drop grid nodes that ended up inside geometry; never drop a marked node."""
    seen = reachable(nodes, links)
    problems = []
    kept = []
    for node in nodes:
        if node["id"] in seen:
            kept.append(node)
            continue
        if node["marker"] and keep_markers:
            problems.append(f"marked node {node['id']} ({node['marker']}) is unreachable")
            kept.append(node)
        else:
            problems.append(f"dropped unreachable node {node['id']} at "
                            f"({node['position']['x']}, {node['position']['y']}, "
                            f"{node['position']['z']})")
    ids = {node["id"] for node in kept}
    kept_links = [link for link in links if link["from"] in ids and link["to"] in ids]
    return kept, kept_links, problems


def validate_nav(graph_id: str, nodes: List[dict], links: List[dict]) -> List[str]:
    """Mirror Kordon.Core.Nav.NavGraph.Validate so CI cannot be surprised."""
    errors: List[str] = []
    if not nodes:
        return [f"{graph_id}: graph has no nodes"]
    ids = {node["id"] for node in nodes}
    if len(ids) != len(nodes):
        errors.append(f"{graph_id}: duplicate node ids")
    for link in links:
        if link["from"] not in ids:
            errors.append(f"{graph_id}: link references unknown node {link['from']}")
        if link["to"] not in ids:
            errors.append(f"{graph_id}: link references unknown node {link['to']}")
        if link.get("cost", 0.0) < 0:
            errors.append(f"{graph_id}: negative link cost {link['from']}->{link['to']}")
    seen = reachable(nodes, links)
    for node in nodes:
        if node["id"] not in seen:
            errors.append(f"{graph_id}: node {node['id']} is unreachable from node "
                          f"{nodes[0]['id']}")
    if not errors:
        isolated = [node["id"] for node in nodes
                    if not any(link["from"] == node["id"] or link["to"] == node["id"]
                               for link in links)]
        if isolated:
            errors.append(f"{graph_id}: nodes without links: {isolated}")
    return errors


def nearest_node(nodes: List[dict], position: Sequence[float]) -> Optional[dict]:
    best = None
    best_distance = float("inf")
    for node in nodes:
        delta = (Vector((node["position"]["x"], node["position"]["y"], node["position"]["z"]))
                 - Vector(tuple(position)))
        distance = delta.length
        if distance < best_distance:
            best_distance = distance
            best = node
    return best
