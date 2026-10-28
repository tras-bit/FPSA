#!/usr/bin/env python3
"""Build the KORDON modular kit: .blend source, one FBX per module, manifest.

    Tools/blender.sh Tools/blender/build_kit.py                # everything
    Tools/blender.sh Tools/blender/build_kit.py --only wall_4  # one module
    KORDON_ART_DETAIL=0 Tools/blender.sh Tools/blender/build_kit.py --quick-check

Outputs

    ArtSource/Blender/kordon_modular_kit.blend                 authoring source
    Assets/_Project/Art/Models/Resources/Kordon/Kit/<id>.fbx    one file per module
    Assets/StreamingAssets/Data/Art/kit_manifest.json           metadata for Unity

Every exported FBX is re-imported into a clean scene and its bounding box is
compared with the recorded size, so a broken axis convention or an empty export
fails the script instead of reaching the repository.
"""
from __future__ import annotations

import argparse
import os
import sys
import time

import bpy
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import kordon_art as art  # noqa: E402
import kordon_modules  # noqa: E402
import kordon_props  # noqa: E402

BUILDERS = {}
for module in (kordon_modules, kordon_props):
    for name in dir(module):
        if name.startswith("_"):
            continue
        value = getattr(module, name)
        if callable(value) and getattr(value, "__module__", "") == module.__name__:
            doc = (value.__doc__ or "")
            if name.startswith("build") or name in ():
                continue
            BUILDERS[name] = value


def builder_list() -> list:
    """Module builders in a stable order: architecture first, then props."""
    order = [
        # walls and structure
        "wall_4", "wall_2", "wall_1", "wall_4_door", "wall_4_window", "wall_4_broken",
        "wall_low_4", "corner_inner", "corner_outer", "pillar_1",
        # floors
        "floor_slab_4", "floor_grate_4", "floor_dirt_4", "floor_hazard_4",
        # structure
        "column_steel_4", "beam_4", "truss_4", "roof_panel_4", "roof_skylight_4",
        "pipe_rack_4", "vent_duct_4",
        # verticality
        "catwalk_4", "catwalk_2", "catwalk_platform_4", "catwalk_corner", "railing_4",
        "stairs_4",
        "ladder_4",
        # doors and fences
        "door_sliding_2", "gate_yard_6", "fence_4", "fence_corner", "barrier_concrete_2",
        # cover and props
        "crate_1", "crate_stack", "pallet_stack", "barrel_cluster", "cable_spool",
        "sandbag_wall", "concrete_block_2", "rubble_pile", "glass_panel_2",
        # plant equipment
        "hopper_3", "transformer_box", "dumpster_2", "slag_pot", "control_desk",
        "locker_row", "conveyor_6", "pipe_valve",
        # lighting and signage
        "light_mast_8", "floodlight_wall", "sign_plate",
        # landmarks
        "furnace_kiln", "chimney_stack",
        # rail and co-op objectives
        "rail_track_4", "rail_wagon_8", "fuel_line", "intel_terminal", "extraction_pad",
    ]
    missing = [name for name in order if name not in BUILDERS]
    extra = sorted(set(BUILDERS) - set(order))
    if missing:
        raise SystemExit(f"[kit] builders missing from the order list: {missing}")
    if extra:
        print(f"[kit] warning: builders not in the order list: {extra}")
    return order


def build(wanted: list, fbx_dir: str, verify: bool) -> dict:
    art.reset_scene()
    entries = {}
    for index, name in enumerate(wanted):
        started = time.time()
        obj = BUILDERS[name]()
        entry = art.modules()[name]
        path = os.path.join(fbx_dir, f"{name}.fbx")
        obj.location = (0.0, 0.0, 0.0)
        size = art.export_fbx([obj], path)
        entry["fbx"] = os.path.relpath(path, art.REPO_ROOT).replace(os.sep, "/")
        entry["fbxBytes"] = size
        entries[name] = entry
        art.move_to_slot(obj, index)
        print(f"[kit] {name:<22} {entry['size'][0]:>6.2f} x {entry['size'][1]:>5.2f} x "
              f"{entry['size'][2]:>6.2f} m  {entry['triangles']:>6} tris  "
              f"{size / 1024:>7.1f} KiB  {time.time() - started:>5.1f}s")
    if verify:
        verify_exports(entries, fbx_dir)
    return entries


def verify_exports(entries: dict, fbx_dir: str) -> None:
    """Re-import every FBX into an empty file and compare its bounding box."""
    print("[kit] verifying exports (re-import into a clean scene)")
    art.reset_scene()
    problems = []
    for name, entry in sorted(entries.items()):
        path = os.path.join(fbx_dir, f"{name}.fbx")
        bpy.ops.import_scene.fbx(filepath=path)
        imported = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
        if not imported:
            problems.append(f"{name}: FBX imported no mesh")
            continue
        min_corner = [1e9, 1e9, 1e9]
        max_corner = [-1e9, -1e9, -1e9]
        for obj in imported:
            for corner in obj.bound_box:
                world = obj.matrix_world @ mathutils.Vector(corner)
                for axis in range(3):
                    min_corner[axis] = min(min_corner[axis], world[axis])
                    max_corner[axis] = max(max_corner[axis], world[axis])
        size = [max_corner[axis] - min_corner[axis] for axis in range(3)]
        expected = entry["size"]
        for axis, label in enumerate(("X", "Y", "Z")):
            if abs(size[axis] - expected[axis]) > max(0.06, expected[axis] * 0.03):
                problems.append(
                    f"{name}: imported {label} size {size[axis]:.3f} != authored "
                    f"{expected[axis]:.3f} (axis convention or scale is wrong)")
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete()
    if problems:
        raise SystemExit("[kit] FAIL\n  " + "\n  ".join(problems))
    print(f"[kit] all {len(entries)} exports re-import with the authored dimensions")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", default="", help="comma separated module ids")
    parser.add_argument("--fbx-dir", default=os.path.join(
        art.MODEL_DIR, "Resources", "Kordon", "Kit"))
    parser.add_argument("--blend", default=os.path.join(art.BLEND_DIR,
                                                        "kordon_modular_kit.blend"))
    parser.add_argument("--manifest", default=os.path.join(
        art.DATA_DIR, "Art", "kit_manifest.json"))
    parser.add_argument("--no-verify", action="store_true")
    parser.add_argument("--no-blend", action="store_true")
    args = parser.parse_args()

    wanted = builder_list()
    if args.only:
        requested = [name.strip() for name in args.only.split(",") if name.strip()]
        unknown = [name for name in requested if name not in BUILDERS]
        if unknown:
            raise SystemExit(f"[kit] unknown modules: {unknown}")
        wanted = requested

    started = time.time()
    entries = build(wanted, args.fbx_dir, verify=not args.no_verify)

    if not args.no_blend:
        # Rebuild in a clean scene so the overview grid has no leftovers, then save.
        art.reset_scene()
        art.clear_registry()
        for index, name in enumerate(wanted):
            obj = BUILDERS[name]()
            art.move_to_slot(obj, index)
        size = art.save_blend(args.blend)
        print(f"[kit] saved {os.path.relpath(args.blend, art.REPO_ROOT)} "
              f"({size / 1024 / 1024:.2f} MiB)")

    manifest = {
        "comment": [
            "Generated by Tools/blender/build_kit.py - do not edit by hand.",
            "One FBX per module under Assets/_Project/Art/Models/Resources/Kordon/Kit.",
            "Unity loads modules with Resources.Load<GameObject>(\"Kordon/Kit/<id>\") and",
            "assembles maps from Assets/StreamingAssets/Data/Maps/*.json.",
            "surfaceKind is a Kordon.Core.Combat.MaterialKind used for penetration, impact",
            "effects and footstep/impact audio; coverHeight drives the stance cover rules.",
        ],
        "generator": "Tools/blender/build_kit.py",
        "blenderVersion": bpy.app.version_string,
        "units": "metres, Z up, module origin at footprint centre on the ground",
        "fbxSettings": "forward -Z, up Y, metric, no embedded textures, no leaf bones",
        "generatedAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "moduleCount": len(entries),
        "totalTriangles": sum(entry["triangles"] for entry in entries.values()),
        "totalFbxBytes": sum(entry["fbxBytes"] for entry in entries.values()),
        "surfaces": {name: dict(kind=spec["kind"], texture=spec["tex"],
                                baseColor=[round(c, 4) for c in spec["color"]],
                                roughness=spec["rough"], metallic=spec["metal"])
                     for name, spec in art.SURFACES.items()},
        "modules": entries,
    }
    art.write_json(args.manifest, manifest)
    print(f"[kit] manifest {os.path.relpath(args.manifest, art.REPO_ROOT)}: "
          f"{manifest['moduleCount']} modules, {manifest['totalTriangles']} triangles, "
          f"{manifest['totalFbxBytes'] / 1024 / 1024:.2f} MiB of FBX")
    print(f"[kit] done in {time.time() - started:.1f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
