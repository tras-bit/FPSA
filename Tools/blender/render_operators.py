#!/usr/bin/env python3
"""Render the operator sheet: the two authored bodies, front and three quarter.

    Tools/blender/render_operators.py

Writes ``docs/art/operators.png``.  Same studio as the weapon sheet - the real
exported geometry wearing the same generated textures the game loads - so the
sheet shows what another player's body looks like rather than a shaded preview.
"""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import build_operators as bo  # noqa: E402
import kordon_art as art  # noqa: E402
import render_weapons as rw  # noqa: E402

OUT = os.path.join(art.REPO_ROOT, "docs", "art", "operators.png")
VARIANTS = ("operator", "militia")


def render(variant: str, path: str, azimuth_deg: float) -> None:
    art.reset_scene()
    _root, _objects, _meta = bo.build_operator(variant)
    rw.setup((600, 940), samples=64)
    rw.apply_textures()
    rw.studio_floor(0.0)
    rw.lights()
    scene = bpy.context.scene

    import math
    centre = Vector((0.0, 0.0, 0.92))
    direction = Vector((math.sin(math.radians(azimuth_deg)) * 0.94,
                        -math.cos(math.radians(azimuth_deg)) * 0.94, 0.16)).normalized()
    camera = rw.camera(centre + direction * 7.4, centre, lens=80)
    camera.data.sensor_fit = "VERTICAL"
    scene.view_settings.exposure = 0.05
    scene.render.filepath = path
    rw.shoot(path)


def main() -> int:
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    import tempfile
    tmp = tempfile.mkdtemp(prefix="kordon-operators-")
    tiles = []
    for variant in VARIANTS:
        for index, azimuth in enumerate((22.0, 138.0)):
            path = os.path.join(tmp, f"{variant}_{index}.png")
            render(variant, path, azimuth)
            tiles.append((path, variant, index))
            print(f"[operators] rendered {variant} {azimuth:.0f} deg")

    from PIL import Image, ImageDraw, ImageFont

    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    tile_w, tile_h = 600, 940
    columns = len(tiles)
    sheet = Image.new("RGB", (columns * tile_w, tile_h + 40), (16, 18, 21))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(font_path, 22) if os.path.exists(font_path) else None
    for index, (path, variant, view) in enumerate(tiles):
        image = Image.open(path).convert("RGB")
        sheet.paste(image, (index * tile_w, 0))
        if font is not None:
            label = variant.upper() + ("  /  FRONT" if view == 0 else "  /  REAR")
            draw.text((index * tile_w + 18, tile_h + 8), label, font=font,
                      fill=(226, 118, 34))
    sheet.save(OUT)
    print(f"[operators] wrote {os.path.relpath(OUT, art.REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
