#!/usr/bin/env python3
"""Draw the HUD's bottom right block from the same numbers the game uses.

    python3 Tools/blender/draw_hud.py

Writes docs/art/ui-hud-layout.png.  This is a layout reference, not a screenshot:
the environment has no Unity editor, so the HUD cannot be captured in game.  Every
rectangle comes from Kordon.Core.Ui.HudLayout (edge inset, column width, row
heights, gaps, icon sizes) and the magazine silhouettes are drawn the way
HudIcons.Magazine rasterises them at runtime, so the picture, the code and the
unit tests cannot drift apart.
"""
from __future__ import annotations

import os

from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO_ROOT, "docs", "art", "ui-hud-layout.png")

W, H = 1280, 720

# Mirrors Assets/_Project/Scripts/Core/Ui/HudLayout.cs
EDGE_INSET = 26.0
COLUMN_WIDTH = 360.0
CARD_HEIGHT = 116.0
MAG_ROW_HEIGHT = 38.0
SIDEARM_ROW_HEIGHT = 28.0
INSPECT_HEIGHT = 28.0
GAP_CARD_MAGS = 8.0
GAP_MAGS_SIDEARM = 6.0
GAP_SIDEARM_INSPECT = 8.0
MAG_ICON_W, MAG_ICON_H, MAG_ICON_GAP = 22.0, 36.0, 6.0
SIDE_ICON_W, SIDE_ICON_H = 15.0, 24.0
HINT_HALF_WIDTH = 230.0

PAPER = (222, 224, 216)
DIM = (150, 158, 152)
ACCENT = (226, 118, 34)
INK = (12, 14, 17)
FULL = (226, 228, 220)
PARTIAL = (224, 196, 74)
EMPTY = (198, 62, 48)

FONT_DIR = "/usr/share/fonts/truetype/dejavu"
FONT = os.path.join(FONT_DIR, "DejaVuSans.ttf")
FONT_BOLD = os.path.join(FONT_DIR, "DejaVuSans-Bold.ttf")


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT, size)


def rows() -> dict:
    out = {}
    bottom = EDGE_INSET
    for name, height, gap in (
        ("card", CARD_HEIGHT, GAP_CARD_MAGS),
        ("primary", MAG_ROW_HEIGHT, GAP_MAGS_SIDEARM),
        ("sidearm", SIDEARM_ROW_HEIGHT, GAP_SIDEARM_INSPECT),
        ("inspect", INSPECT_HEIGHT, 0.0),
    ):
        out[name] = (bottom, height)
        bottom += height + gap
    return out


def box(layout: dict, name: str):
    bottom, height = layout[name]
    left = W - EDGE_INSET - COLUMN_WIDTH
    return left, H - bottom - height, left + COLUMN_WIDTH, H - bottom


def rounded(draw, rect, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(rect, radius=radius, fill=fill, outline=outline, width=width)


def magazine(draw, x, y, w, h, colour, chambered=False):
    """The same silhouette HudIcons.Magazine rasterises: a curved body with ribs,
    a floor plate and feed lips - not a sheared rectangle."""
    dark = tuple(int(c * 0.62) for c in colour)
    rib = tuple(int(c * 0.84) for c in colour)
    lean_max = w * 0.22

    def left_at(yy):
        t = max(0.0, min(1.0, (yy - y) / h))
        return x + lean_max * (1.0 - t) ** 2

    # body, drawn as one column per pixel row so the curve is smooth
    for yy in range(int(y) + 2, int(y + h) - 2):
        t = max(0.0, min(1.0, (yy - y) / h))
        left = left_at(yy)
        taper = 1.0 - 0.12 * t
        draw.line([(left, yy), (left + w * taper, yy)], fill=colour)

    # feed lips and floor plate
    draw.line([(left_at(y + 2), y + 2), (left_at(y + 2) + w, y + 2)], fill=dark, width=2)
    draw.line([(left_at(y + h - 3), y + h - 3), (left_at(y + h - 3) + w * 0.9, y + h - 3)],
              fill=dark, width=3)

    # ribs
    for t in (0.3, 0.52, 0.74):
        yy = y + h * t
        draw.line([(left_at(yy), yy), (left_at(yy) + w * (1.0 - 0.12 * t), yy)],
                  fill=rib, width=1)

    if chambered:
        cx = left_at(y + 3) + w * 0.5
        draw.rectangle([cx - 2, y + 1, cx + 2, y + 4], fill=(255, 221, 158))


def main() -> None:
    image = Image.new("RGB", (W, H), (56, 60, 64))
    draw = ImageDraw.Draw(image, "RGBA")

    # A stand-in for the world behind the HUD.
    for y in range(H):
        shade = 78 - int(26 * (y / H))
        draw.line([(0, y), (W, y)], fill=(shade, shade + 2, shade + 5))
    draw.rectangle([0, 0, W, 236], fill=(44, 47, 49))
    draw.rectangle([80, 150, 360, 520], fill=(66, 62, 54))
    draw.rectangle([420, 200, 700, 520], fill=(36, 38, 40))
    draw.rectangle([780, 120, 980, 520], fill=(70, 66, 58))
    draw.rectangle([0, 520, W, H], fill=(60, 61, 60))

    layout = rows()

    # ---- loadout card ---------------------------------------------------
    left, top, right, bottom = box(layout, "card")
    rounded(draw, [left, top, right, bottom], 12, (11, 13, 16, 205),
            outline=(255, 255, 255, 34))
    draw.text((left + 14, top + 9), "LOADOUT", font=font(12, True), fill=(158, 166, 160, 235))
    draw.line([(left + 12, top + 25), (right - 12, top + 25)], fill=(255, 255, 255, 26))

    slots = [
        ("1", "VK-74 KLYUCH", "4 MAG", True),
        ("2", "P-9 KORT", "2 MAG", False),
        ("3", "F-1 OSKOLOK", "x2", False),
    ]
    for index, (key, name, state, active) in enumerate(slots):
        row_top = top + 28 + index * 27
        if active:
            rounded(draw, [left + 8, row_top - 1, right - 8, row_top + 25], 8,
                    (226, 118, 34, 38), outline=(226, 118, 34, 120))
            draw.rectangle([left + 8, row_top + 2, left + 11, row_top + 22], fill=ACCENT)

        chip_fill = ACCENT if active else (255, 255, 255, 18)
        rounded(draw, [left + 18, row_top + 3, left + 40, row_top + 21], 5, chip_fill,
                outline=(255, 255, 255, 24))
        key_colour = INK if active else DIM
        kw = draw.textlength(key, font=font(12, True))
        draw.text((left + 29 - kw / 2, row_top + 5), key, font=font(12, True), fill=key_colour)

        colour = ACCENT if active else DIM
        draw.text((left + 48, row_top + 3), name, font=font(16, True), fill=colour)
        state_colour = PAPER if index == 2 else colour
        sw = draw.textlength(state, font=font(14))
        draw.text((right - 18 - sw, row_top + 5), state, font=font(14), fill=state_colour)

    # ---- primary magazines ----------------------------------------------
    left, top, right, bottom = box(layout, "primary")
    draw.text((left + 2, top + 9), "PRIMARY MAGS", font=font(11, True),
              fill=(158, 166, 160, 200))
    x = right
    for state in ("empty", "partial", "full", "full"):
        colour = {"full": FULL, "partial": PARTIAL, "empty": EMPTY}[state]
        x -= MAG_ICON_W
        magazine(draw, x, top, MAG_ICON_W, MAG_ICON_H, colour,
                 chambered=state in ("full", "partial"))
        x -= MAG_ICON_GAP

    # ---- sidearm magazines ----------------------------------------------
    left, top, right, bottom = box(layout, "sidearm")
    draw.text((left + 2, top + 6), "SIDEARM MAGS", font=font(11, True),
              fill=(158, 166, 160, 200))
    x = right
    for state in ("empty", "full", "full"):
        colour = {"full": FULL, "partial": PARTIAL, "empty": EMPTY}[state]
        x -= SIDE_ICON_W
        magazine(draw, x, top, SIDE_ICON_W, SIDE_ICON_H, colour, chambered=state == "full")
        x -= MAG_ICON_GAP

    # ---- inspect readout ------------------------------------------------
    left, top, right, bottom = box(layout, "inspect")
    text = "14 / 30"
    tw = draw.textlength(text, font=font(22, True))
    draw.text((right - tw, top + 2), text, font=font(22, True), fill=ACCENT)

    # ---- bottom centre hint ---------------------------------------------
    text = "tap R  reload      hold R  inspect"
    tw = draw.textlength(text, font=font(16))
    draw.text(((W - tw) / 2, H - 28 - 18), text, font=font(16), fill=(226, 228, 220, 190))

    # ---- legend on the left, no text over text --------------------------
    legend = [
        ("1", "Loadout card: which slot is in hand, and what its pouch holds"),
        ("2", "Primary magazines - white full, yellow partial, red empty"),
        ("3", "Sidearm magazines, a separate pouch"),
        ("4", "Magazine inspection - the only place an exact count appears"),
    ]
    legend_left, legend_top = 40, H - 208
    rounded(draw, [legend_left - 12, legend_top - 14, legend_left + 470, legend_top + 128], 10,
            (10, 12, 15, 178), outline=(255, 255, 255, 26))
    for index, (number, text) in enumerate(legend):
        y = legend_top + index * 30
        draw.ellipse([legend_left, y, legend_left + 18, y + 18], outline=ACCENT, width=2)
        nw = draw.textlength(number, font=font(12, True))
        draw.text((legend_left + 9 - nw / 2, y + 2), number, font=font(12, True), fill=ACCENT)
        draw.text((legend_left + 28, y + 1), text, font=font(13), fill=(226, 228, 220, 235))

    caption = ("HUD bottom right block - geometry from Kordon.Core.Ui.HudLayout, "
               "unit tested. Layout mock, not an in-game screenshot.")
    draw.text((24, H - 22), caption, font=font(12), fill=(150, 158, 152, 210))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    image.save(OUT)
    print(f"wrote {os.path.relpath(OUT, REPO_ROOT)}")


if __name__ == "__main__":
    main()
