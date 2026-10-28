#!/usr/bin/env python3
"""Top down plan rendering for a generated map layout.

Blender cannot render in this environment (no GL device), and a plan view is
exactly what a map review needs anyway: routes, sight lines, spawn balance and
cover density are all readable from above.  This draws the layout JSON plus its
navigation graph to a PNG with Pillow.

    python3 Tools/blender/draw_layout.py            # every map JSON in Data/Maps
"""
from __future__ import annotations

import json
import math
import os
from typing import Dict, List, Sequence, Tuple

from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MAPS_DIR = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "Data", "Maps")
NAV_DIR = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "Data", "Nav")
ART_DIR = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "Data", "Art")
OUT_DIR = os.path.join(REPO_ROOT, "docs", "art")

FONT_PATH = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
SCALE = 11.0            # pixels per metre
MARGIN = 70
LEGEND_H = 176

CATEGORY_COLORS = {
    "wall": (120, 116, 108),
    "floor": (74, 72, 68),
    "structure": (96, 104, 118),
    "verticality": (58, 122, 150),
    "door": (168, 122, 60),
    "gate": (150, 108, 54),
    "fence": (112, 118, 96),
    "prop": (140, 108, 76),
    "landmark": (168, 74, 58),
    "objective": (92, 148, 96),
    "lighting": (196, 176, 92),
}
ELEVATED_TINT = (150, 190, 214)
AREA_COLORS = {
    "Exterior": (214, 196, 150),
    "Indoor": (176, 190, 214),
    "Tunnel": (150, 140, 168),
    "Catwalk": (120, 196, 214),
    "Elevated": (150, 214, 176),
    "Spawn": (214, 150, 150),
    "Doorway": (214, 186, 140),
    "Objective": (160, 214, 150),
}


def _font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    try:
        return ImageFont.truetype(FONT_BOLD if bold else FONT_PATH, size)
    except OSError:
        return ImageFont.load_default()


class Plan:
    def __init__(self, bounds: Sequence[float], scale: float = SCALE):
        self.min_x, self.min_y, self.max_x, self.max_y = bounds
        self.scale = scale
        self.width = int((self.max_x - self.min_x) * scale) + MARGIN * 2
        self.height = int((self.max_y - self.min_y) * scale) + MARGIN * 2 + LEGEND_H
        self.image = Image.new("RGBA", (self.width, self.height), (24, 24, 26, 255))
        self.draw = ImageDraw.Draw(self.image, "RGBA")
        # Elevated geometry goes on its own layer so a plan reader can see the
        # ground floor through roofs and catwalks.
        self.layer = Image.new("RGBA", (self.width, self.height), (0, 0, 0, 0))
        self.ldraw = ImageDraw.Draw(self.layer, "RGBA")

    def to_px(self, x: float, y: float) -> Tuple[float, float]:
        return (MARGIN + (x - self.min_x) * self.scale,
                MARGIN + (self.max_y - y) * self.scale)

    def rect(self, x0: float, y0: float, x1: float, y1: float, fill=None, outline=None,
             width: int = 1, elevated: bool = False) -> None:
        a = self.to_px(x0, y1)
        b = self.to_px(x1, y0)
        draw = self.ldraw if elevated else self.draw
        draw.rectangle([a, b], fill=fill, outline=outline, width=width)

    def quad(self, centre: Sequence[float], size: Sequence[float], yaw_deg: float,
             fill=None, outline=None, width: int = 1, elevated: bool = False) -> None:
        cx, cy = centre[0], centre[1]
        half_x, half_y = size[0] * 0.5, size[1] * 0.5
        angle = math.radians(yaw_deg)
        cos_a, sin_a = math.cos(angle), math.sin(angle)
        points = []
        for sx, sy in ((-half_x, -half_y), (half_x, -half_y), (half_x, half_y),
                       (-half_x, half_y)):
            rx = sx * cos_a - sy * sin_a
            ry = sx * sin_a + sy * cos_a
            points.append(self.to_px(cx + rx, cy + ry))
        draw = self.ldraw if elevated else self.draw
        draw.polygon(points, fill=fill, outline=outline, width=width)

    def circle(self, x: float, y: float, radius_px: float, fill=None, outline=None,
               width: int = 1) -> None:
        px, py = self.to_px(x, y)
        self.draw.ellipse([px - radius_px, py - radius_px, px + radius_px, py + radius_px],
                          fill=fill, outline=outline, width=width)

    def line(self, a: Sequence[float], b: Sequence[float], fill, width: int = 1) -> None:
        self.draw.line([self.to_px(a[0], a[1]), self.to_px(b[0], b[1])], fill=fill,
                       width=width)

    def text(self, x: float, y: float, label: str, fill=(226, 226, 226), size: int = 13,
             bold: bool = False, anchor: str = "mm") -> None:
        px, py = self.to_px(x, y)
        self.draw.text((px, py), label, font=_font(size, bold), fill=fill, anchor=anchor)

    def text_px(self, x: int, y: int, label: str, fill=(226, 226, 226), size: int = 13,
                bold: bool = False, anchor: str = "la") -> None:
        self.draw.text((x, y), label, font=_font(size, bold), fill=fill, anchor=anchor)

    def finish(self) -> None:
        """Composite the elevated layer over the ground plan."""
        self.image = Image.alpha_composite(self.image, self.layer)
        self.draw = ImageDraw.Draw(self.image, "RGBA")


def draw(data: dict, nodes: List[dict], links: List[dict], manifest: Dict[str, dict],
         path: str) -> str:
    bounds = data["bounds"]
    plan = Plan((bounds["min"][0], bounds["min"][1], bounds["max"][0], bounds["max"][1]))

    # north arrow + grid
    plan.text_px(MARGIN - 46, MARGIN - 46, "N", (200, 200, 200), 20, True)
    plan.draw.line([(MARGIN - 40, MARGIN - 26), (MARGIN - 40, MARGIN - 6)],
                   fill=(200, 200, 200), width=2)
    grid_step = 10.0
    x = bounds["min"][0]
    while x <= bounds["max"][0] + 1e-6:
        plan.line((x, bounds["min"][1]), (x, bounds["max"][1]), (40, 40, 44), 1)
        plan.text(x, bounds["min"][1] - 2.4, f"{x:.0f}", (110, 110, 116), 11)
        x += grid_step
    y = bounds["min"][1]
    while y <= bounds["max"][1] + 1e-6:
        plan.line((bounds["min"][0], y), (bounds["max"][0], y), (40, 40, 44), 1)
        plan.text(bounds["min"][0] - 3.0, y, f"{y:.0f}", (110, 110, 116), 11)
        y += grid_step
    plan.rect(bounds["min"][0], bounds["min"][1], bounds["max"][0], bounds["max"][1],
              outline=(96, 96, 104), width=2)

    # audio zones (dashed look via outline only)
    for zone in data.get("audioZones", []):
        if zone["kind"] == "Outdoor":
            continue
        region = zone["region"]
        plan.rect(region[0], region[1], region[2], region[3],
                  outline=(90, 120, 150), width=2)
        plan.text((region[0] + region[2]) * 0.5, region[3] - 1.4, zone["id"],
                  (126, 168, 200), 13, True)

    # nav links first so the geometry sits on top
    node_by_id = {node["id"]: node for node in nodes}
    for link in links:
        a = node_by_id.get(link["from"])
        b = node_by_id.get(link["to"])
        if not a or not b:
            continue
        colour = (196, 150, 90) if link.get("jump") else (70, 96, 110)
        plan.line((a["position"]["x"], a["position"]["y"]),
                  (b["position"]["x"], b["position"]["y"]), colour,
                  2 if link.get("jump") else 1)

    # shells, then instances, sorted by height so elevated parts read on top
    for shell in data.get("shells", []):
        size = shell.get("size", [0, 0, 0])
        position = shell.get("position", [0, 0, 0])
        plan.quad(position, (size[0], size[1]), shell.get("yaw", 0.0),
                  fill=(46, 46, 50, 255), outline=(86, 86, 92))

    placements = sorted(data["placements"], key=lambda entry: entry["position"][2])
    for entry in placements:
        module = entry["module"]
        info = manifest.get(module, {})
        size = info.get("size", [2.0, 2.0, 2.0])
        category = info.get("category", "prop")
        base_colour = CATEGORY_COLORS.get(category, (120, 120, 120))
        elevated = entry["position"][2] > 1.0
        if elevated:
            colour = tuple(int(c * 0.45 + t * 0.55) for c, t in
                           zip(base_colour, ELEVATED_TINT)) + (110,)
            outline = (190, 220, 236, 150)
        else:
            colour = base_colour
            outline = (20, 20, 22)
        plan.quad(entry["position"], (size[0], size[1]), entry["yaw"], fill=colour,
                  outline=outline, elevated=elevated)
        if category in ("landmark", "objective") or module in ("gate_yard_6",):
            plan.text(entry["position"][0], entry["position"][1], module.replace("_", " "),
                      (246, 236, 214), 12, True)

    plan.finish()

    # nav nodes
    for node in nodes:
        position = node["position"]
        colour = AREA_COLORS.get(node["area"], (200, 200, 200))
        radius = 4.2 if node.get("marker") else 2.6
        plan.circle(position["x"], position["y"], radius, fill=colour,
                    outline=(16, 16, 18), width=1)
        if node.get("marker") and not node["marker"].startswith("spawn_"):
            plan.text(position["x"], position["y"] + 1.6, node["marker"], colour, 11, False)

    # spawns
    for spawn in data.get("spawns", []):
        x, y, _z = spawn["position"]
        plan.circle(x, y, 6.0, fill=(226, 92, 74), outline=(250, 236, 230), width=2)
        plan.text(x, y - 2.0, spawn["zone"], (255, 240, 236), 12, True)

    # landmarks
    for landmark in data.get("landmarks", []):
        x, y, _z = landmark["position"]
        plan.circle(x, y, 9.0, outline=(246, 214, 120), width=2)
        plan.text(x, y - 3.4, landmark["label"], (246, 214, 120), 13, True)

    # title + legend
    footer_y = plan.height - LEGEND_H + 16
    plan.text_px(MARGIN - 46, footer_y - 40,
                 f"{data['name']}  ({data['id']})  -  {data.get('mode', '')}",
                 (240, 240, 240), 22, True)
    plan.text_px(MARGIN - 46, footer_y - 12,
                 f"{len(placements)} kit instances, {len(data.get('shells', []))} shells, "
                 f"{len(nodes)} nav nodes, {len(links)} links, "
                 f"{len(data.get('spawns', []))} spawn slots",
                 (170, 170, 178), 14)
    def legend_row(items, y_offset, swatch):
        cursor = MARGIN - 46
        row = y_offset
        for label, colour in items:
            width = 24 + int(_font(13).getlength(label)) + 18
            if cursor + width > plan.width - 30:
                cursor = MARGIN - 46
                row += 22
            swatch(cursor, row, colour)
            plan.text_px(cursor + 22, row, label, (190, 190, 196), 13)
            cursor += width
        return row

    def square(cursor, row, colour):
        plan.draw.rectangle([cursor, row, cursor + 16, row + 16], fill=colour,
                            outline=(20, 20, 22))

    def disc(cursor, row, colour):
        plan.draw.ellipse([cursor, row + 1, cursor + 14, row + 15], fill=colour,
                          outline=(20, 20, 22))

    last = legend_row(list(CATEGORY_COLORS.items()), footer_y + 18, square)
    legend_row(list(AREA_COLORS.items()), last + 26, disc)
    plan.text_px(MARGIN - 46, footer_y + 78,
                 "routes: 1 yard + furnace hall (ground)   2 catwalks, gantry and roof "
                 "(elevated, blue)   3 service tunnel (covered)",
                 (150, 178, 196), 13)
    plan.text_px(MARGIN - 46, footer_y + 98,
                 "generated by Tools/blender/draw_layout.py from the map layout JSON",
                 (120, 120, 126), 12)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    plan.image.save(path)
    return path


def main() -> int:
    with open(os.path.join(ART_DIR, "kit_manifest.json"), encoding="utf-8") as handle:
        manifest = json.load(handle)["modules"]
    written = []
    for name in sorted(os.listdir(MAPS_DIR)):
        if not name.endswith(".json"):
            continue
        map_id = name[:-len(".json")]
        with open(os.path.join(MAPS_DIR, name), encoding="utf-8") as handle:
            data = json.load(handle)
        nav_path = os.path.join(NAV_DIR, f"{map_id}.json")
        nodes, links = [], []
        if os.path.exists(nav_path):
            with open(nav_path, encoding="utf-8") as handle:
                graph = json.load(handle)
            nodes, links = graph["nodes"], graph["links"]
        out = os.path.join(OUT_DIR, f"{map_id}-layout.png")
        draw(data, nodes, links, manifest, out)
        written.append(out)
        print(f"[draw] {out}")
    return 0 if written else 1


if __name__ == "__main__":
    raise SystemExit(main())
