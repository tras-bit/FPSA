#!/usr/bin/env python3
"""Toolchain smoke test for the headless Blender used by this project.

Run it with the project wrapper so the loader stubs and the `bpy` module are set up:

    Tools/blender.sh Tools/blender/probe.py

It imports the real Blender 4.5 LTS as a Python module, builds a cube, writes a
`.blend` file and exports it as FBX and glTF, then reports the byte sizes.  If any
of those steps fails the art pipeline cannot run in this environment and the
failure is printed instead of hidden.
"""
import os
import sys
import tempfile

import bpy


def main() -> int:
    print(f"[probe] blender {bpy.app.version_string} (file version {bpy.app.version_file})")
    print(f"[probe] python {sys.version.split()[0]}")

    bpy.ops.wm.read_factory_settings(use_empty=True)

    # Factory settings reset the preferences, which disables the bundled import/export
    # add-ons.  Enable the ones the art pipeline needs (4.5 ships glTF as an extension).
    for module in ("io_scene_fbx", "io_scene_gltf2"):
        try:
            bpy.ops.preferences.addon_enable(module=module)
        except Exception as error:  # noqa: BLE001 - the module may not exist in this build
            print(f"[probe] add-on {module} not enabled: {error}")

    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0.0, 0.0, 1.0))
    cube = bpy.context.active_object
    cube.name = "ProbeCube"
    print(f"[probe] created {cube.name} with {len(cube.data.polygons)} faces")

    out_dir = tempfile.mkdtemp(prefix="kordon-blender-probe-")
    blend_path = os.path.join(out_dir, "probe.blend")
    fbx_path = os.path.join(out_dir, "probe.fbx")
    glb_path = os.path.join(out_dir, "probe.glb")

    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=False, apply_scale_options="FBX_SCALE_ALL")
    bpy.ops.export_scene.gltf(filepath=glb_path, export_format="GLB", use_selection=False)

    for path in (blend_path, fbx_path, glb_path):
        size = os.path.getsize(path)
        if size <= 0:
            print(f"[probe] FAIL {os.path.basename(path)} is empty")
            return 1
        print(f"[probe] wrote {os.path.basename(path)} ({size} bytes)")

    print("[probe] OK - .blend save, FBX export and glTF export all work headless")
    return 0


if __name__ == "__main__":
    sys.exit(main())
