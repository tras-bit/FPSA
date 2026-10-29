#!/usr/bin/env python3
"""Draw the HUD's bottom right block in both of its states.

    python3 Tools/blender/draw_hud.py

Writes docs/art/ui-hud-layout.png.  This is a layout reference, not a screenshot:
the environment has no Unity editor, so the HUD cannot be captured in game.

Every number here mirrors the code:

* rectangle sizes, gaps and the edge inset come from
  `Kordon.Core.Ui.HudLayout` (unit tested in `HudLayoutTests`);
* what is visible in each state comes from `Kordon.Core.Ui.HudAmmoPolicy`
  (unit tested in `HudAmmoPolicyTests`) - the pouch, the magazine icons and the
  exact round count only exist while a magazine is out of the weapon;
* the magazine silhouettes are drawn the way `HudIcons.Magazine` rasterises them.
"""
from __future__ import annotations

import os

from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO_ROOT, "docs", "art", "ui-hud-layout.png")

W, H = 1000, 540
BLOCK_X = 46                 # left edge of the drawn block

# --- mirrors Assets/_Project/Scripts/Core/Ui/HudLayout.cs -------------------
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
    """Row bands, bottom up, exactly as HudLayout stacks them."""
    out = {}
    bottom = 0.0
    for name, height, gap in (
        ("card", CARD_HEIGHT, GAP_CARD_MAGS),
        ("primary", MAG_ROW_HEIGHT, GAP_MAGS_SIDEARM),
        ("sidearm", SIDEARM_ROW_HEIGHT, GAP_SIDEARM_INSPECT),
        ("inspect", INSPECT_HEIGHT, 0.0),
    ):
        out[name] = (bottom, height)
        bottom += height + gap
    return out


def band(layout: dict, name: str, origin_y: float):
    """Screen box for a row: origin_y is the bottom edge of the block."""
    bottom, height = layout[name]
    top = origin_y - bottom - height
    return BLOCK_X, top, BLOCK_X + COLUMN_WIDTH, top + height


def rounded(draw, rect, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(rect, radius=radius, fill=fill, outline=outline, width=width)


def magazine(draw, x, y, w, h, colour, chambered=False):
    """The silhouette HudIcons.Magazine rasterises: curved body, ribs, floor plate."""
    dark = tuple(int(c * 0.62) for c in colour)
    rib = tuple(int(c * 0.84) for c in colour)
    lean_max = w * 0.22

    def left_at(yy):
        t = max(0.0, min(1.0, (yy - y) / h))
        return x + lean_max * (1.0 - t) ** 2

    for yy in range(int(y) + 2, int(y + h) - 2):
        t = max(0.0, min(1.0, (yy - y) / h))
        left = left_at(yy)
        draw.line([(left, yy), (left + w * (1.0 - 0.12 * t), yy)], fill=colour)

    draw.line([(left_at(y + 2), y + 2), (left_at(y + 2) + w, y + 2)], fill=dark, width=2)
    draw.line([(left_at(y + h - 3), y + h - 3), (left_at(y + h - 3) + w * 0.9, y + h - 3)],
              fill=dark, width=3)
    for t in (0.3, 0.52, 0.74):
        yy = y + h * t
        draw.line([(left_at(yy), yy), (left_at(yy) + w * (1.0 - 0.12 * t), yy)],
                  fill=rib, width=1)
    if chambered:
        cx = left_at(y + 3) + w * 0.5
        draw.rectangle([cx - 2, y + 1, cx + 2, y + 4], fill=(255, 221, 158))


def draw_block(draw, origin_x, origin_y, pouch_visible):
    """One state of the block. pouch_visible is HudAmmoPolicy.ShowPouch."""
    layout = rows()

    # ---- loadout card ---------------------------------------------------
    left, top, right, bottom = band(layout, "card", origin_y)
    left += origin_x - BLOCK_X
    right += origin_x - BLOCK_X
    rounded(draw, [left, top, right, bottom], 12, (11, 13, 16, 235),
            outline=(255, 255, 255, 34))
    draw.text((left + 14, top + 9), "LOADOUT", font=font(12, True), fill=(158, 166, 160, 235))
    draw.line([(left + 12, top + 25), (right - 12, top + 25)], fill=(255, 255, 255, 26))

    slots = [
        ("1", "VK-74 KLYUCH", "4 MAG" if pouch_visible else "", True),
        ("2", "P-9 KORT", "", False),
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
        kw = draw.textlength(key, font=font(12, True))
        draw.text((left + 29 - kw / 2, row_top + 5), key, font=font(12, True),
                  fill=INK if active else DIM)

        colour = ACCENT if active else DIM
        draw.text((left + 48, row_top + 3), name, font=font(16, True), fill=colour)
        if state:
            state_colour = PAPER if index == 2 else colour
            sw = draw.textlength(state, font=font(14))
            draw.text((right - 18 - sw, row_top + 5), state, font=font(14),
                      fill=state_colour)

    # ---- pouch rows: only while a magazine is out -----------------------
    for name, caption, icons, icon_w, icon_h, states in (
        ("primary", "PRIMARY MAGS", 4, MAG_ICON_W, MAG_ICON_H,
         ("empty", "partial", "full", "full")),
        ("sidearm", "SIDEARM MAGS", 3, SIDE_ICON_W, SIDE_ICON_H,
         ("empty", "full", "full")),
    ):
        left, top, right, bottom = band(layout, name, origin_y)
        left += origin_x - BLOCK_X
        right += origin_x - BLOCK_X
        if not pouch_visible:
            # An empty band: the pouch is not on screen at all until it is checked.
            draw.line([(left, (top + bottom) / 2), (right, (top + bottom) / 2)],
                      fill=(255, 255, 255, 12))
            continue

        draw.text((left + 2, top + icon_h * 0.5 - 6), caption, font=font(11, True),
                  fill=(158, 166, 160, 210))
        x = right
        for state in states:
            colour = {"full": FULL, "partial": PARTIAL, "empty": EMPTY}[state]
            x -= icon_w
            magazine(draw, x, top + (icon_h and 0), icon_w, icon_h, colour,
                     chambered=state in ("full", "partial"))

    # ---- inspect readout ------------------------------------------------
    left, top, right, bottom = band(layout, "inspect", origin_y)
    left += origin_x - BLOCK_X
    right += origin_x - BLOCK_X
    text = "VK-74 30 ROUND   14 / 30" if pouch_visible else ""
    if text:
        tw = draw.textlength(text, font=font(20, True))
        draw.text((right - tw, top + 3), text, font=font(20, True), fill=ACCENT)
    else:
        draw.text((left + 2, top + 5), "R  check magazine",
                  font=font(12), fill=(150, 158, 152, 170))


def main() -> None:
    image = Image.new("RGB", (W, H), (26, 29, 32))
    draw = ImageDraw.Draw(image, "RGBA")

    # Two columns: weapon in hands, then the magazine pulled out.
    for index, (title, visible) in enumerate((
        ("WEAPON IN HANDS  -  nothing about ammunition on screen", False),
        ("MAGAZINE OUT  -  pouch and exact count appear", True),
    )):
        origin_x = 24 + index * (W // 2)
        draw.rounded_rectangle([origin_x, 22, origin_x + W // 2 - 24, H - 26], 10,
                               fill=(38, 41, 45), outline=(255, 255, 255, 18))
        draw.text((origin_x + 16, 32), title, font=font(12, True),
                  fill=(226, 228, 220, 230))
        draw_block(draw, origin_x + 20, H - 80, visible)

    caption = ("HUD bottom right block - geometry from Kordon.Core.Ui.HudLayout, "
               "visibility from Kordon.Core.Ui.HudAmmoPolicy. Layout mock, not a screenshot.")
    draw.text((24, H - 16), caption, font=font(12), fill=(150, 158, 152, 210))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    image.save(OUT)
    print(f"wrote {os.path.relpath(OUT, REPO_ROOT)}")


if __name__ == "__main__":
    main()
