#!/usr/bin/env python3
"""Build the KORDON maps from the modular kit.

    Tools/blender.sh Tools/blender/build_maps.py                 # both maps
    Tools/blender.sh Tools/blender/build_maps.py --map ffa_zavod7

For every map this writes

    ArtSource/Blender/<map>.blend                       authoring source (kit + shells)
    Assets/_Project/Art/Models/Resources/Kordon/Maps/<map>/<shell>.fbx
    Assets/StreamingAssets/Data/Maps/<map>.json         layout: shells, placements,
                                                        spawns, audio zones, landmarks
    Assets/StreamingAssets/Data/Nav/<map>.json          hand authored navigation graph
                                                        (Kordon.Core.Nav.NavGraph schema)
    docs/art/<map>-layout.png                           top down plan for review

Checks that run before anything is written, and abort the build when they fail:

* every placement references a module the kit actually exports;
* nothing is placed outside the declared map bounds;
* navigation links are computed by real chest height line of sight through the
  assembled geometry (``scene.ray_cast``), unreachable grid nodes are pruned, and
  the result is validated exactly like ``NavGraph.Validate`` does in C#;
* the co-op map carries a navigation marker for every objective location in
  ``missions.json``, so objectives cannot point at empty space;
* the free-for-all map carries twenty marked spawn nodes (four zones x five).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from typing import Dict, List

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "layouts"))

import kordon_art as art  # noqa: E402
import kordon_layout as lay  # noqa: E402
import build_kit  # noqa: E402

LIBRARY_PARK_X = 4000.0
MAP_JSON_DIR = os.path.join(art.DATA_DIR, "Maps")
NAV_JSON_DIR = os.path.join(art.DATA_DIR, "Nav")
PREVIEW_DIR = os.path.join(art.REPO_ROOT, "docs", "art")


def load_manifest() -> Dict[str, dict]:
    path = os.path.join(art.DATA_DIR, "Art", "kit_manifest.json")
    if not os.path.exists(path):
        raise SystemExit("[maps] kit manifest missing - run Tools/blender/build_kit.py first")
    with open(path, encoding="utf-8") as handle:
        manifest = json.load(handle)["modules"]
    lay.set_module_sizes({name: entry["size"] for name, entry in manifest.items()})
    return manifest


def build_library(wanted: List[str]) -> Dict[str, bpy.types.Object]:
    """Build each needed kit module once and park it far from the map volume."""
    library: Dict[str, bpy.types.Object] = {}
    for index, name in enumerate(wanted):
        obj = build_kit.BUILDERS[name]()
        obj.location = (LIBRARY_PARK_X, (index % 24) * 14.0, -(index // 24) * 14.0)
        library[name] = obj
    return library


def sight_manifest(layout_module, manifest: Dict[str, dict]) -> Dict[str, dict]:
    """Kit manifest plus this map's shells, so ray casts respect shell transparency."""
    merged = dict(manifest)
    for name, spec in layout_module.SHELLS.items():
        merged[name] = {"blocksSight": bool(spec.get("blocksSight", True)),
                        "blocksMovement": bool(spec.get("blocksMovement", True)),
                        "size": [0.0, 0.0, 0.0]}
    return merged


def assemble(layout_module, manifest: Dict[str, dict]) -> dict:
    """Place shells and kit instances; the nav graph is built afterwards."""
    layout_module.set_manifest(sight_manifest(layout_module, manifest))
    data = layout_module.build_layout()

    needed = sorted({entry["module"] for entry in data["placements"]})
    unknown = [name for name in needed if name not in manifest]
    if unknown:
        raise SystemExit(f"[maps] placements reference modules the kit does not export: "
                         f"{unknown}")

    art.reset_scene()
    library = build_library(build_kit.builder_list())

    # shells: map specific geometry, exported as their own FBX files
    shell_dir = os.path.join(art.MODEL_DIR, "Resources", "Kordon", "Maps", data["id"])
    shell_objects = {}
    for spec in data["shells"]:
        shell_id = spec["id"]
        builder = layout_module.SHELLS[shell_id]["builder"]
        obj = builder()
        obj.location = (0.0, 0.0, 0.0)
        path = os.path.join(shell_dir, f"{shell_id}.fbx")
        size = art.export_fbx([obj], path)
        spec["fbx"] = os.path.relpath(path, art.REPO_ROOT).replace(os.sep, "/")
        spec["fbxBytes"] = size
        spec["size"] = [round(v, 3) for v in art.world_bounds(obj)[1]]
        spec["triangles"] = art.triangle_count(obj)
        shell_objects[shell_id] = obj
        obj.location = (LIBRARY_PARK_X + 400.0, 0.0, 0.0)
        print(f"[maps] shell {shell_id:<12} {spec['triangles']:>7} tris  "
              f"{size / 1024:>8.1f} KiB")

    # instances
    instances: List[str] = []
    for shell_id, obj in shell_objects.items():
        spec = next(s for s in data["shells"] if s["id"] == shell_id)
        art.place(obj, spec["position"], spec["yaw"], shell_id)
        instances.append(shell_id)
    for index, entry in enumerate(data["placements"]):
        module = entry["module"]
        source = library.get(module)
        if source is None:
            raise SystemExit(f"[maps] module {module} was not built")
        # Instance names keep the module id before a dot: kordon_layout strips the
        # suffix when it decides whether a ray cast hit blocks line of sight.
        instance = art.place(source, entry["position"], entry["yaw"],
                             f"{module}.{index:04d}")
        instance["kordon.blocksSight"] = manifest[module].get("blocksSight", True)
        instances.append(module)

    print(f"[maps] {data['id']}: {len(data['placements'])} kit instances, "
          f"{len(shell_objects)} shells")
    return data


def check_bounds(data: dict) -> List[str]:
    problems = []
    bounds = data["bounds"]
    min_x, min_y = bounds["min"][0], bounds["min"][1]
    max_x, max_y = bounds["max"][0], bounds["max"][1]
    for entry in data["placements"]:
        x, y, _z = entry["position"]
        size = lay.module_size(entry["module"])
        reach = max(size[0], size[1]) * 0.5 + 0.5
        if not (min_x - reach <= x <= max_x + reach and min_y - reach <= y <= max_y + reach):
            problems.append(f"{entry['module']} at ({x}, {y}) is outside the map bounds")
    return problems


def check_spawns(data: dict, nav_nodes: List[dict]) -> List[str]:
    problems = []
    markers = {node["marker"] for node in nav_nodes if node["marker"]}
    for spawn in data["spawns"]:
        if spawn["marker"] not in markers:
            problems.append(f"spawn {spawn['id']} has no navigation marker")
    if data.get("mode") == "FreeForAll":
        if len(data["spawns"]) != 20:
            problems.append(f"free-for-all needs 20 spawn slots, found {len(data['spawns'])}")
        zones = {spawn["zone"] for spawn in data["spawns"]}
        if len(zones) != 4:
            problems.append(f"expected 4 spawn zones, found {sorted(zones)}")
    return problems


def check_objective_markers(data: dict, nav_nodes: List[dict]) -> List[str]:
    """Every co-op objective location must exist as a navigation marker."""
    problems = []
    missions_path = os.path.join(art.DATA_DIR, "Definitions", "missions.json")
    with open(missions_path, encoding="utf-8") as handle:
        missions = json.load(handle)["missions"]
    markers = {node["marker"] for node in nav_nodes if node["marker"]}
    for mission in missions:
        if mission.get("mapSceneName") != data["scene"]:
            continue
        for objective in mission["objectives"]:
            location = objective.get("locationId", "")
            if not location:
                problems.append(f"{mission['id']}/{objective['id']} has no locationId")
            elif location not in markers:
                problems.append(f"{mission['id']}/{objective['id']} points at nav marker "
                                f"'{location}' which {data['id']} does not have")
    return problems


def write_nav(data: dict, nodes: List[dict], links: List[dict]) -> str:
    payload = {
        "comment": [
            f"Navigation graph for {data['name']} ({data['id']}), hand authored in",
            "Tools/blender/layouts and linked by real line of sight through the assembled",
            "geometry. Consumed by Kordon.Core.Nav.NavGraph.FromJson - ids are integers,",
            "markers are the strings objectives, spawns and AI anchors refer to.",
        ],
        "id": data["id"],
        "map": data["scene"],
        "generatedBy": "Tools/blender/build_maps.py",
        "generatedAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "nodeCount": len(nodes),
        "linkCount": len(links),
        "nodes": nodes,
        "links": links,
    }
    path = os.path.join(NAV_JSON_DIR, f"{data['id']}.json")
    art.write_json(path, payload)
    return path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--map", default="", help="only build this map id")
    parser.add_argument("--no-blend", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args()

    manifest = load_manifest()
    layouts = [("ffa_zavod7", "zavod7"), ("coop_silent_kiln", "silent_kiln")]
    if args.map:
        layouts = [entry for entry in layouts if entry[0] == args.map]
        if not layouts:
            raise SystemExit(f"[maps] unknown map id '{args.map}'")

    started = time.time()
    failures: List[str] = []
    for map_id, module_name in layouts:
        print(f"[maps] === {map_id} ===")
        layout_module = __import__(module_name)
        data = assemble(layout_module, manifest)
        nodes, links = layout_module.build_nav()
        data["nav"] = {"nodeCount": len(nodes), "linkCount": len(links)}

        problems = check_bounds(data)
        problems += check_spawns(data, nodes)
        problems += check_objective_markers(data, nodes)
        errors = lay.validate_nav(map_id, nodes, links)
        problems += errors
        if problems:
            failures.extend(f"{map_id}: {problem}" for problem in problems)
            print(f"[maps] {map_id}: {len(problems)} problem(s)")
            for problem in problems[:20]:
                print(f"         - {problem}")
            continue

        nav_path = write_nav(data, nodes, links)
        map_path = os.path.join(MAP_JSON_DIR, f"{map_id}.json")
        art.write_json(map_path, {key: value for key, value in data.items()
                                  if key != "nav"})
        triangles = sum(manifest[entry["module"]]["triangles"]
                        for entry in data["placements"])
        triangles += sum(spec.get("triangles", 0) for spec in data["shells"])
        print(f"[maps] {map_id}: {len(nodes)} nav nodes, {len(links)} links, "
              f"{len(data['spawns'])} spawns, ~{triangles / 1000:.0f}k triangles instanced")
        print(f"[maps] wrote {os.path.relpath(map_path, art.REPO_ROOT)} and "
              f"{os.path.relpath(nav_path, art.REPO_ROOT)}")

        if not args.no_blend:
            blend_path = os.path.join(art.BLEND_DIR, f"{map_id}.blend")
            size = art.save_blend(blend_path)
            print(f"[maps] saved {os.path.relpath(blend_path, art.REPO_ROOT)} "
                  f"({size / 1024 / 1024:.2f} MiB)")

        if not args.no_preview:
            import draw_layout
            preview = os.path.join(PREVIEW_DIR, f"{map_id}-layout.png")
            draw_layout.draw(data, nodes, links, manifest, preview)
            print(f"[maps] preview {os.path.relpath(preview, art.REPO_ROOT)}")

    if failures:
        print(f"[maps] FAIL ({len(failures)} problems)")
        return 1
    print(f"[maps] done in {time.time() - started:.1f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
