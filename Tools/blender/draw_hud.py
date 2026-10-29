#!/usr/bin/env python3
"""Draw the HUD's lower right column from the same numbers the game uses.

    python3 Tools/blender/draw_hud.py

Writes docs/art/ui-hud-layout.png.  This is a layout reference, not a screenshot:
the environment has no Unity editor, so the HUD cannot be captured in game.  The
rectangles come from Kordon.Core.Ui.HudLayout (edge inset, column width, row
heights, row gap) so the picture and the code cannot drift apart silently; the
unit tests check the same arithmetic against six screen sizes.
"""
from __future__ import annotations

import os

from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO_ROOT, "docs", "art", "ui-hud-layout.png")

W, H = 1280, 720

# Mirrors Assets/_Project/Scripts/Core/Ui/HudLayout.cs
EDGE_INSET = 28.0
COLUMN_WIDTH = 336.0
WEAPON_PANEL_HEIGHT = 96.0
MAGAZINE_ROW_HEIGHT = 16.0
SIDEARM_ROW_HEIGHT = 13.0
GRENADE_LINE_HEIGHT = 24.0
INSPECT_LINE_HEIGHT = 26.0
ROW_GAP = 8.0

PAPER = (222, 224, 216)
DIM = (158, 166, 160)
ACCENT = (224, 117, 33)
INK = (13, 15, 18)
SURFACE = (33, 37, 42)
TRACK = (44, 47, 52)

FONT_DIR = "/usr/share/fonts/truetype/dejavu"
FONT = os.path.join(FONT_DIR, "DejaVuSans.ttf")
FONT_BOLD = os.path.join(FONT_DIR, "DejaVuSans-Bold.ttf")


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT, size)


def column_rows() -> dict:
    rows = {}
    bottom = EDGE_INSET
    for name, height in (
        ("weapon", WEAPON_PANEL_HEIGHT),
        ("primary", MAGAZINE_ROW_HEIGHT),
        ("sidearm", SIDEARM_ROW_HEIGHT),
        ("grenade", GRENADE_LINE_HEIGHT),
        ("inspect", INSPECT_LINE_HEIGHT),
    ):
        rows[name] = (bottom, height)
        bottom += height + ROW_GAP
    return rows


def rect(rows: dict, name: str) -> tuple[float, float, float, float]:
    bottom, height = rows[name]
    left = W - EDGE_INSET - COLUMN_WIDTH
    # PIL's origin is top left, the layout's is bottom left
    return left, H - bottom - height, left + COLUMN_WIDTH, H - bottom


def magazine(draw: ImageDraw.ImageDraw, x: float, y: float, w: float, h: float,
             colour: tuple[int, int, int], chambered: bool = False) -> None:
    draw.rectangle([x, y, x + w, y + h], fill=colour)
    if chambered:
        draw.rectangle([x + w * 0.62, y + 3, x + w * 0.62 + 3, y + h - 3], fill=INK)


def main() -> None:
    image = Image.new("RGB", (W, H), (58, 62, 66))
    draw = ImageDraw.Draw(image, "RGBA")

    # A stand-in for the world behind the HUD: floor, a wall band, a lit doorway.
    for y in range(H):
        shade = 74 - int(30 * (y / H))
        draw.line([(0, y), (W, y)], fill=(shade, shade + 2, shade + 4))
    draw.rectangle([0, 0, W, 250], fill=(46, 48, 50))
    draw.rectangle([120, 150, 420, 470], fill=(64, 60, 52))
    draw.rectangle([470, 190, 760, 470], fill=(38, 40, 42))
    draw.rectangle([820, 120, 1010, 470], fill=(70, 66, 58))
    draw.rectangle([0, 470, W, H], fill=(62, 62, 60))

    rows = column_rows()

    # ---- weapon panel ---------------------------------------------------
    left, top, right, bottom = rect(rows, "weapon")
    draw.rectangle([left, top, right, bottom], fill=(*INK, 150))
    draw.rectangle([left, top, right, top + 1], fill=(*DIM, 90))
    slots = [
        ("1", "VK-74 KLYUCH", "4 MAG", True),
        ("2", "P-9 KORT", "3 MAG", False),
        ("3", "F-1 OSKOLOK", "HELD", False),
    ]
    for index, (key, name, count, active) in enumerate(slots):
        row_top = top + 14 + index * 28
        if active:
            draw.rectangle([left + 4, row_top - 4, right - 4, row_top + 22],
                           fill=(*ACCENT, 42))
        colour = ACCENT if active else DIM
        draw.text((left + 12, row_top), key, font=font(15), fill=colour)
        draw.text((left + 36, row_top - 1), name, font=font(16, True), fill=colour)
        text = count
        width = draw.textlength(text, font=font(15))
        draw.text((right - 14 - width, row_top), text, font=font(15), fill=colour)

    # ---- primary magazines ---------------------------------------------
    left, top, right, bottom = rect(rows, "primary")
    x = right
    for state in ("full", "full", "partial", "empty"):
        colour = {"full": PAPER, "partial": (222, 196, 76), "empty": (196, 62, 48)}[state]
        x -= 44
        magazine(draw, x, top, 44, MAGAZINE_ROW_HEIGHT - 1, colour,
                 chambered=state in ("full", "partial"))
        x -= 5

    # ---- sidearm magazines ---------------------------------------------
    left, top, right, bottom = rect(rows, "sidearm")
    x = right
    for state in ("full", "full", "empty"):
        colour = {"full": PAPER, "partial": (222, 196, 76), "empty": (196, 62, 48)}[state]
        x -= 30
        magazine(draw, x, top, 30, SIDEARM_ROW_HEIGHT, colour)
        x -= 5

    # ---- grenade line and inspection readout ---------------------------
    left, top, right, bottom = rect(rows, "grenade")
    text = "F-1 OSKOLOK   x2   3 select"
    width = draw.textlength(text, font=font(18))
    draw.text((right - width, top + 2), text, font=font(18), fill=PAPER)

    left, top, right, bottom = rect(rows, "inspect")
    text = "MAGAZINE  14 / 30  PARTIAL"
    width = draw.textlength(text, font=font(24))
    draw.text((right - width, top + 2), text, font=font(24), fill=ACCENT)

    # ---- bottom centre hint --------------------------------------------
    text = "tap R  reload     hold R  inspect magazine"
    width = draw.textlength(text, font=font(18))
    draw.text(((W - width) * 0.5, H - 24 - 20), text, font=font(18), fill=(*PAPER, 190))

    # ---- callouts -------------------------------------------------------
    draw.line([left - 26, top + 12, left - 8, top + 12], fill=ACCENT, width=2)
    labels = [
        ("weapon panel: which slot is in hand, how many magazines it has left", "weapon"),
        ("primary magazines (white full / yellow partial / red empty)", "primary"),
        ("sidearm magazines - their own pouch", "sidearm"),
        ("grenade line", "grenade"),
        ("magazine inspection (only after R is held)", "inspect"),
    ]
    for text, name in labels:
        left, top, right, bottom = rect(rows, name)
        draw.line([left - 26, top + 6, left - 8, top + 6], fill=ACCENT, width=2)
        draw.text((left - 26 - 8 - draw.textlength(text, font=font(14)), top - 3),
                  text, font=font(14), fill=(*PAPER, 210))

    caption = ("HUD lower right column - geometry from Kordon.Core.Ui.HudLayout, "
               "verified by HudLayoutTests. Layout mock, not an in-game screenshot.")
    draw.text((24, H - 26), caption, font=font(13), fill=(*DIM, 210))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    image.save(OUT)
    print(f"wrote {os.path.relpath(OUT, REPO_ROOT)}")


if __name__ == "__main__":
    main()
