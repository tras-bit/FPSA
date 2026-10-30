#!/usr/bin/env python3
"""Render a review sheet of the gloved hands, close up.

    Tools/blender/render_hands.py [weapon_id ...]

Writes ``docs/art/hands-detail.png``: two tiles per weapon, the firing hand on the
pistol grip and the support hand under the handguard, each framed on the hand's own
bounds at a distance where the digits are readable.

The hands are the part of the viewmodel the player looks at for the whole match,
and they are the part that is hardest to judge from a silhouette: a finger diameter
taken out of context tells you nothing about whether four fingers read as four
fingers.  That is why this is a checked-in tool rather than a throwaway script - the
art pass is only honest if it is looked at after every change.

Renders the real geometry with the real generated textures, Cycles on CPU, headless.
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
DEFAULT_WEAPONS = ("tr30", "vk74")


def hand_centre_and_radius(name: str):
    """Bounding sphere of one hand in world space."""
    obj = bpy.data.objects.get(name)
    if obj is None:
        raise RuntimeError(f"no object named {name}")
    # Not the bounding box: the object is the hand *and* the forearm that leaves the
    # frame, so the box centre sits out in the sleeve.  The hand is where the
    # vertices are dense, so the median lands on the palm and a high percentile of
    # the distance from it encloses the fingers and the cuff.
    world = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    if not world:
        raise RuntimeError(f"{name} has no geometry")
    ordered = [sorted(p[i] for p in world) for i in range(3)]
    middle = len(world) // 2
    centre = Vector((ordered[0][middle], ordered[1][middle], ordered[2][middle]))
    distances = sorted((p - centre).length for p in world)
    radius = distances[int(len(distances) * 0.80)]
    return centre, max(radius, 0.012)


def shoot_hand(name: str, path: str, azimuth_deg: float, elevation_deg: float,
               margin: float = 1.35, lens: float = 78.0) -> None:
    """Frame one hand and render it.

    The distance is solved from the hand's radius and the lens' field of view, so the
    framing is the same between iterations: a change in the model cannot be hidden by
    a change in the shot.  Placing the camera at a multiple of the radius, which is
    what the first version did, put it inside the palm at 78 mm.
    """
    import math

    centre, radius = hand_centre_and_radius(name)
    half_fov = math.atan(18.0 * 0.5 / lens)
    distance = radius / math.tan(half_fov) * margin
    azimuth = math.radians(azimuth_deg)
    elevation = math.radians(elevation_deg)
    direction = Vector((math.sin(azimuth) * math.cos(elevation),
                        -math.cos(azimuth) * math.cos(elevation),
                        math.sin(elevation)))
    eye = centre + direction * distance
    rw.camera(eye, centre, lens=lens)
    rw.shoot(path)


SAMPLE_COUNT = int(os.environ.get("KORDON_HAND_SAMPLES", "44"))


def render_weapon_hands(weapon_id: str, directory: str) -> list:
    art.reset_scene()
    _weapon, _magazine, meta = bw.build_weapon(weapon_id)
    rw.setup((760, 560), samples=SAMPLE_COUNT)
    rw.apply_textures(tiling=3.2)  # the hands are small islands; tile tighter
    rw.studio_floor()
    rw.lights()
    tiles = []
    # The support hand is shot from the front-left: from behind it was only a pair of
    # forearms and the grip, which told the reviewer nothing about the fingers.
    shots = (("HandRight", 38.0, 16.0), ("HandLeft", -118.0, 22.0))
    for index, (name, azimuth, elevation) in enumerate(shots):
        path = os.path.join(directory, f"{weapon_id}_{index}.png")
        shoot_hand(name, path, azimuth, elevation)
        tiles.append(path)
        print(f"[hands] {weapon_id}: {name}")
    return tiles


def main() -> None:
    weapon_ids = tuple(sys.argv[1:]) or DEFAULT_WEAPONS
    os.makedirs(OUT_DIR, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix="kordon-hands-")
    sheets = []
    for weapon_id in weapon_ids:
        sheets.append((weapon_id, render_weapon_hands(weapon_id, tmp)))

    from PIL import Image, ImageDraw, ImageFont

    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    tile_w, tile_h, caption = 760, 560, 34
    columns = 2
    rows = len(sheets)
    sheet = Image.new("RGB", (columns * tile_w, rows * (tile_h + caption)), (16, 18, 21))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(font_path, 20) if os.path.exists(font_path) else None
    labels = ("firing hand", "support hand")
    for row, (weapon_id, tiles) in enumerate(sheets):
        for column, path in enumerate(tiles):
            image = Image.open(path).convert("RGB")
            x, y = column * tile_w, row * (tile_h + caption)
            sheet.paste(image, (x, y))
            if font is not None:
                draw.text((x + 16, y + tile_h + 7),
                          f"{weapon_id.upper()}   {labels[column]}", font=font,
                          fill=(226, 118, 34))
    out = os.path.join(OUT_DIR, "hands-detail.png")
    sheet.save(out)
    print(f"[hands] wrote {os.path.relpath(out, art.REPO_ROOT)}")


if __name__ == "__main__":
    main()
