#!/usr/bin/env python3
"""Render the trigger group of each weapon.

    Tools/blender/render_triggers.py [weapon_id ...]

Writes ``docs/art/weapons-triggers.png``: the guard, the blade, the pins, the magazine
release and the selector of each weapon, framed from the trigger group's own bounds.

This exists because the trigger was the part of the weapon nobody could find: it was a
4 mm sliver hidden inside the receiver's housing, and every render of the whole weapon
hid it behind the hand.  A part that cannot be seen in a render is a part that cannot
be reviewed.  The framing is derived from the geometry, never guessed - the first two
attempts aimed the camera at the grip because the guard's height was assumed.
"""
from __future__ import annotations

import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import kordon_art as art  # noqa: E402
import build_weapons as bw  # noqa: E402
import render_weapons as rw  # noqa: E402
from mathutils import Vector  # noqa: E402

OUT_DIR = os.path.join(art.REPO_ROOT, "docs", "art")
WEAPONS = ("tr30", "vk74", "p9")
SAMPLES = int(os.environ.get("KORDON_TRIGGER_SAMPLES", "28"))


def render_one(weapon_id: str, path: str) -> None:
    """Build one weapon, keep the trigger group, frame it from its own bounds."""
    art.reset_scene()
    spec = bw.SPECS[weapon_id]
    if spec["kind"] == "pistol":
        # The sidearm's guard and blade are built inline with the frame, so the whole
        # weapon is kept and the camera is aimed at the trigger node.
        bw.build_weapon(weapon_id)
        trigger = bpy.data.objects.get("Trigger")
        if trigger is None:
            raise RuntimeError(f"{weapon_id}: no Trigger node was exported")
        points = [trigger.matrix_world @ Vector(corner) for corner in trigger.bound_box]
    else:
        ctx = bw.layout(spec)
        parts = bw.trigger_group(ctx, spec)
        keep = {part.name for part in parts}
        for obj in list(bpy.data.objects):
            if obj.type == "MESH" and obj.name not in keep:
                bpy.data.objects.remove(obj, do_unlink=True)
        points = [obj.matrix_world @ Vector(corner)
                  for obj in parts for corner in obj.bound_box]

    low = Vector((min(p.x for p in points), min(p.y for p in points),
                  min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points),
                   max(p.z for p in points)))
    centre = (low + high) * 0.5
    extent = max((high - low).x, (high - low).z, 0.02)

    rw.setup((720, 520), samples=SAMPLES)
    rw.apply_textures(tiling=3.0)
    rw.studio_floor(low.z - 0.012)
    rw.lights()
    # A pistol's guard is part of the frame, so the shot keeps the frame in and pulls
    # back far enough to clear it; a rifle's trigger group is shown on its own.
    reach = extent * (8.0 if spec["kind"] == "pistol" else 2.6)
    eye = centre + Vector((-reach, -reach * 0.35, reach * 0.42))
    rw.camera(eye, centre, lens=88)
    rw.shoot(path)
    print(f"[triggers] {weapon_id}: {extent * 1000:.0f} mm of trigger group")


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix="kordon-triggers-")
    tiles = []
    chosen = tuple(sys.argv[1:]) or WEAPONS
    for weapon_id in chosen:
        path = os.path.join(tmp, f"{weapon_id}.png")
        render_one(weapon_id, path)
        tiles.append((weapon_id, path))

    from PIL import Image, ImageDraw, ImageFont

    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    tile_w, tile_h, caption = 720, 520, 36
    sheet = Image.new("RGB", (tile_w * len(tiles), tile_h + caption), (16, 18, 21))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(font_path, 22) if os.path.exists(font_path) else None
    for index, (weapon_id, path) in enumerate(tiles):
        sheet.paste(Image.open(path).convert("RGB"), (index * tile_w, 0))
        if font is not None:
            draw.text((index * tile_w + 18, tile_h + 7), weapon_id.upper(), font=font,
                      fill=(226, 118, 34))
    out = os.path.join(OUT_DIR, "weapons-triggers.png")
    sheet.save(out)
    print(f"[triggers] wrote {os.path.relpath(out, art.REPO_ROOT)}")


if __name__ == "__main__":
    main()
