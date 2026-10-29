#!/usr/bin/env python3
"""Render a review sheet of the weapon set from the authored Blender models.

    Tools/blender/render_weapons.sh   ->  python3 Tools/blender/render_weapons.py

Writes

    docs/art/weapons-lineup.png    every weapon, left side, to scale on one sheet
    docs/art/weapons-detail.png    the primary and the sidearm, three quarter view

This is a render of the real exported geometry (the same build_weapon() the FBX
export uses), not a drawing.  Cycles on CPU, so it runs headless in CI-like
environments.
"""
from __future__ import annotations

import math
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import kordon_art as art  # noqa: E402
import build_weapons as bw  # noqa: E402
from mathutils import Vector  # noqa: E402

OUT_DIR = os.path.join(art.REPO_ROOT, "docs", "art")
LINEUP = ("vk74", "tr30", "vs9", "dmr14", "sr338", "k870", "p9")


def setup(resolution, samples=28):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.film_transparent = True
    scene.world = scene.world or bpy.data.worlds.new("W")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs[0].default_value = (0.10, 0.11, 0.12, 1.0)
    background.inputs[1].default_value = 0.35


def add_light(name, energy, size, location, rotation_deg):
    light = bpy.data.lights.new(name, "AREA")
    light.energy = energy
    light.size = size
    obj = bpy.data.objects.new(name, light)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = tuple(math.radians(v) for v in rotation_deg)


def lights():
    add_light("key", 220, 1.6, (0.9, -1.1, 1.2), (38, 0, 38))
    add_light("fill", 90, 2.6, (-1.2, 0.6, 0.9), (62, 0, -45))
    add_light("rim", 130, 1.2, (-0.1, 1.1, 0.8), (-35, 0, -170))


def camera(location, target, lens=50.0, ortho=None):
    data = bpy.data.cameras.new("cam")
    obj = bpy.data.objects.new("cam", data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    if ortho is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho
    else:
        data.lens = lens
    direction = (Vector(target) - Vector(location)).normalized()
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = obj
    return obj


def shoot(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def render_weapon(weapon_id, path, cam_location, target, lens=50.0, resolution=(700, 320)):
    art.reset_scene()
    bw.build_weapon(weapon_id)
    setup(resolution)
    lights()
    camera(cam_location, target, lens=lens)
    shoot(path)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix="kordon-weapons-")
    tiles = []
    for index, weapon_id in enumerate(LINEUP):
        path = os.path.join(tmp, f"{index}_{weapon_id}.png")
        render_weapon(weapon_id, path, (-1.25, -0.10, 0.02), (0.0, -0.10, 0.0), lens=48)
        tiles.append(path)
        print(f"[weapons] rendered {weapon_id}")

    detail = os.path.join(OUT_DIR, "weapons-detail.png")
    render_weapon("vk74", detail, (0.62, 0.55, 0.30), (0.0, -0.15, -0.02),
                  lens=55, resolution=(900, 420))

    # Contact sheet: one row, captions under each tile.  Pillow is a build
    # dependency of the texture generator already.
    from PIL import Image, ImageDraw, ImageFont

    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    tile_w, tile_h = 700, 320
    columns = 2
    rows = (len(tiles) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * tile_w, rows * (tile_h + 34)), (18, 20, 23))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(font_path, 20) if os.path.exists(font_path) else None

    for index, (path, weapon_id) in enumerate(zip(tiles, LINEUP)):
        image = Image.open(path).convert("RGBA")
        column, row = index % columns, index // columns
        x, y = column * tile_w, row * (tile_h + 34)
        sheet.paste(image, (x, y), image)
        if font is not None:
            draw.text((x + 16, y + tile_h + 6), weapon_id.upper(), font=font,
                      fill=(226, 118, 34))

    lineup = os.path.join(OUT_DIR, "weapons-lineup.png")
    sheet.save(lineup)
    print(f"[weapons] wrote {os.path.relpath(lineup, art.REPO_ROOT)} and "
          f"{os.path.relpath(detail, art.REPO_ROOT)}")


if __name__ == "__main__":
    main()
