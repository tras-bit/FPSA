#!/usr/bin/env python3
"""Procedural texture set for KORDON, generated from the kit's surface contract.

    python3 Tools/textures/generate_textures.py

Every material in the game is one of the nineteen surfaces declared in
``Assets/StreamingAssets/Data/Art/kit_manifest.json`` - the same names the Blender
modules assign to their faces.  This tool turns each surface into a texture set
and a Unity material:

    Assets/_Project/Art/Textures/T_<surface>_albedo.png    sRGB base colour
    Assets/_Project/Art/Textures/T_<surface>_normal.png    tangent space normal
    Assets/_Project/Art/Textures/T_<surface>_metsmo.png    R metallic / A smoothness
    Assets/_Project/Art/Materials/M_<surface>.mat          Built-in Standard shader
    Assets/StreamingAssets/Data/Art/surface_materials.json tiling + shader parameters

All maps are value noise built from seeded grids, so a given seed always yields
the same texture: regenerating is safe and diffs are meaningful.  No third party
texture pack is involved - everything here is generated, which keeps the project
freely redistributable.

Normal maps come from the height field by finite differences; the metallic map
packs metallic in R and smoothness in A, which is exactly what the Standard
shader's metallic workflow samples (_MetallicGlossMap).
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
from typing import Dict, Tuple

import numpy as np
from PIL import Image

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTURE_DIR = os.path.join(REPO_ROOT, "Assets", "_Project", "Art", "Textures")
# Materials live in a Resources folder: MapBuilder resolves them at runtime
# by surface name (Resources.Load<Material>("Kordon/Materials/M_<surface>")).
MATERIAL_DIR = os.path.join(REPO_ROOT, "Assets", "_Project", "Art", "Resources",
                            "Kordon", "Materials")
DATA_DIR = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "Data", "Art")
SIZE = 512
SEED = 20261028


# --------------------------------------------------------------------------
# noise toolkit
# --------------------------------------------------------------------------
def _bilinear(grid: np.ndarray, size: int) -> np.ndarray:
    h, w = grid.shape
    y = np.linspace(0.0, h - 1, size, endpoint=False)
    x = np.linspace(0.0, w - 1, size, endpoint=False)
    yi = np.floor(y).astype(int)
    xi = np.floor(x).astype(int)
    yf = (y - yi)[:, None]
    xf = (x - xi)[None, :]
    yi1 = np.minimum(yi + 1, h - 1)
    xi1 = np.minimum(xi + 1, w - 1)
    top = grid[np.ix_(yi, xi)] * (1 - xf) + grid[np.ix_(yi, xi1)] * xf
    bot = grid[np.ix_(yi1, xi)] * (1 - xf) + grid[np.ix_(yi1, xi1)] * xf
    return top * (1 - yf) + bot * yf


def value_noise(rng: np.random.Generator, cells: int, size: int = SIZE) -> np.ndarray:
    return _bilinear(rng.random((cells + 1, cells + 1)), size)


def fbm(rng: np.random.Generator, cells: int = 4, octaves: int = 5,
        gain: float = 0.5) -> np.ndarray:
    total = np.zeros((SIZE, SIZE))
    amplitude = 1.0
    normalise = 0.0
    for _ in range(octaves):
        total += value_noise(rng, cells, SIZE) * amplitude
        normalise += amplitude
        amplitude *= gain
        cells *= 2
    return total / normalise


def ridge(rng: np.random.Generator, cells: int = 6, octaves: int = 4) -> np.ndarray:
    noise = fbm(rng, cells, octaves)
    return 1.0 - np.abs(2.0 * noise - 1.0)


def worley(rng: np.random.Generator, cells: int = 24) -> Tuple[np.ndarray, np.ndarray]:
    """Cheap Worley: distance to the nearest jittered cell point, plus cell id."""
    points = (np.stack(np.meshgrid(np.arange(cells + 1), np.arange(cells + 1),
                                   indexing="ij"), -1).astype(float)
              + rng.random((cells + 1, cells + 1, 2)))
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) * (cells / SIZE)
    best = np.full((SIZE, SIZE), 1e9)
    best_id = np.zeros((SIZE, SIZE))
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cy = np.clip(np.floor(yy).astype(int) + dy, 0, cells)
            cx = np.clip(np.floor(xx).astype(int) + dx, 0, cells)
            p = points[cy, cx]
            distance = np.hypot(p[..., 0] - yy, p[..., 1] - xx)
            nearer = distance < best
            best = np.where(nearer, distance, best)
            best_id = np.where(nearer, points[cy, cx, 0] * 31.7 + points[cy, cx, 1], best_id)
    return best, best_id


def mix(a: np.ndarray, b: np.ndarray, t: np.ndarray) -> np.ndarray:
    t = np.clip(t, 0.0, 1.0)
    if t.ndim == 2:
        t = t[..., None]
    return a * (1.0 - t) + b * t


def tint(rgb: Tuple[float, float, float]) -> np.ndarray:
    return np.array(rgb, dtype=float)[None, None, :]


def blur(array: np.ndarray, radius: int) -> np.ndarray:
    """Separable box blur over a periodic array.

    Weapon surfaces need it. A height field built straight from per pixel noise
    turns into a normal map with a random normal every two pixels, which is what
    made the 1.0.9 receivers look like cast gravel: the eye reads it as chipped
    stone, not as metal. Blurring the height before the finite difference gives
    the finish a scale.
    """
    if radius <= 0:
        return array
    out = array.astype(float)
    for axis in (0, 1):
        pad = [(0, 0), (0, 0)]
        pad[axis] = (radius + 1, radius)
        cumulative = np.cumsum(np.pad(out, pad, mode="wrap"), axis=axis)
        lead = np.take(cumulative, np.arange(2 * radius + 1, cumulative.shape[axis]), axis=axis)
        trail = np.take(cumulative, np.arange(0, cumulative.shape[axis] - 2 * radius - 1), axis=axis)
        out = (lead - trail) / (2 * radius + 1)
    return out


def normal_from_height(height: np.ndarray, strength: float = 2.2) -> np.ndarray:
    dy, dx = np.gradient(height * strength)
    normal = np.stack([-dx, -dy, np.ones_like(dx)], -1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return normal * 0.5 + 0.5


def to_png(path: str, array: np.ndarray, srgb: bool) -> None:
    data = np.clip(array, 0.0, 1.0)
    if srgb:
        data = np.where(data <= 0.0031308, data * 12.92,
                        1.055 * np.power(data, 1 / 2.4) - 0.055)
    image = Image.fromarray((data * 255.0 + 0.5).astype(np.uint8))
    image.save(path)


def guid_of(name: str) -> str:
    return hashlib.md5(("kordon-guid:" + name).encode("utf-8")).hexdigest()


# --------------------------------------------------------------------------
# surface recipes: each returns (albedo, height, metallic, smoothness)
# --------------------------------------------------------------------------
def _concrete(base, rng, painted=False, worn=False):
    blotch = fbm(rng, 5, 5)
    speckle = value_noise(rng, 192)
    cracks = np.clip(ridge(rng, 7, 4) - 0.72, 0.0, 1.0) * 3.2
    pits = np.clip(value_noise(rng, 96) - 0.78, 0.0, 1.0) * 4.0
    colour = tint(base) * (0.86 + 0.28 * blotch[..., None])
    colour *= (0.94 + 0.12 * speckle[..., None])
    if painted:
        wear = np.clip(fbm(rng, 24, 4) - 0.55, 0.0, 1.0) * 2.4
        colour = mix(colour, tint((0.4, 0.39, 0.37)), wear * 0.7)
    if worn:
        stain = np.clip(fbm(rng, 9, 4) - 0.45, 0.0, 1.0) * 1.6
        colour *= (1.0 - 0.22 * stain[..., None])
    colour *= (1.0 - 0.45 * cracks[..., None])
    height = blotch * 0.35 + speckle * 0.2 - cracks * 0.5 - pits * 0.35
    smooth = np.clip(0.1 + 0.08 * blotch - 0.06 * cracks, 0.02, 0.4)
    return colour, height, np.zeros_like(height), smooth


def _brick(base, rng):
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    rows = 12
    cols = 6
    row_id = np.floor(yy * rows)
    offset = np.where(row_id % 2 == 0, 0.0, 0.5)
    bx = (xx * cols + offset) % 1.0
    by = (yy * rows) % 1.0
    mortar = ((bx < 0.06) | (bx > 0.94) | (by < 0.10) | (by > 0.90)).astype(float)
    brick_id = np.floor(xx * cols + offset) + row_id * 37.0
    shade = 0.82 + 0.36 * ((np.sin(brick_id * 12.9898) * 43758.5453) % 1.0)
    grain = fbm(rng, 40, 4)
    colour = tint(base) * shade[..., None] * (0.9 + 0.2 * grain[..., None])
    colour = mix(colour, tint((0.52, 0.5, 0.47)), mortar * 0.9)
    height = (1.0 - mortar) * (0.5 + 0.3 * grain) - mortar * 0.6
    return colour, height, np.zeros_like(height), np.full_like(height, 0.08)


def _steel(base, rng, rust_amount=0.25, brushed=True, streaks=0.0):
    blotch = fbm(rng, 8, 4)
    fine = value_noise(rng, 220)
    rust_mask = np.clip(fbm(rng, 12, 5) - (0.62 - rust_amount * 0.35), 0.0, 1.0) * 2.6
    colour = tint(base) * (0.9 + 0.2 * blotch[..., None])
    if brushed:
        brush = value_noise(rng, 8)
        brush = _bilinear(np.repeat(brush, 8, axis=0), SIZE)
        colour *= (0.96 + 0.08 * brush[..., None])
    if streaks > 0.0:
        lines = np.clip(np.sin(np.arange(SIZE)[None, :] * 0.9 + fbm(rng, 3, 3) * 9.0),
                        0.96, 1.0)
        colour *= (1.0 - streaks * (1.0 - lines)[..., None])
    rust_colour = mix(tint((0.31, 0.16, 0.09)), tint((0.45, 0.27, 0.14)),
                      fbm(rng, 30, 4))
    colour = mix(colour, rust_colour, np.clip(rust_mask, 0.0, 1.0))
    height = blotch * 0.15 + fine * 0.1 - np.clip(rust_mask, 0.0, 1.0) * 0.35
    metallic = np.clip(0.9 - 0.75 * np.clip(rust_mask, 0.0, 1.0), 0.0, 1.0)
    smooth = np.clip(0.45 - 0.3 * np.clip(rust_mask, 0.0, 1.0) + 0.06 * fine, 0.05, 0.7)
    return colour, height, metallic, smooth


def _sheet_metal(base, rng):
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    corrugation = np.sin(xx * np.pi * 2 * 8) * 0.5 + 0.5
    cells, _ids = worley(rng, 14)
    spangle = np.clip(1.0 - cells * 2.2, 0.0, 1.0)
    grime = fbm(rng, 9, 4)
    colour = tint(base) * (0.9 + 0.14 * corrugation[..., None])
    colour *= (0.92 + 0.16 * spangle[..., None])
    colour *= (0.9 + 0.2 * grime[..., None])
    height = corrugation * 0.7 + spangle * 0.1 + grime * 0.08
    metallic = np.full_like(height, 0.9)
    smooth = np.clip(0.5 + 0.12 * corrugation - 0.15 * grime, 0.1, 0.8)
    return colour, height, metallic, smooth


def _grating(base, rng):
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    grid = 12
    gx = (xx * grid) % 1.0
    gy = (yy * grid) % 1.0
    bar = ((gx < 0.28) | (gy < 0.28)).astype(float)
    diagonal = ((np.abs(gx - gy) < 0.12) | (np.abs(gx + gy - 1.0) < 0.12)).astype(float)
    solid = np.clip(bar + diagonal, 0.0, 1.0)
    fine = value_noise(rng, 200)
    colour = tint(base) * (0.88 + 0.18 * fine[..., None]) * (0.35 + 0.65 * solid[..., None])
    height = solid * 0.8 + fine * 0.06
    metallic = solid * 0.9
    smooth = np.clip(0.35 + 0.15 * solid, 0.1, 0.6)
    return colour, height, metallic, smooth


def _wood(base, rng):
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    planks = 4
    plank_id = np.floor(xx * planks)
    seam = ((xx * planks) % 1.0 < 0.03).astype(float)
    # grain runs along the planks (vertically in UV), modulated by stretched noise
    grain = _bilinear(np.repeat(value_noise(rng, 6, 64), 8, axis=1), SIZE)
    rings = np.sin((xx * 96.0 + grain * 7.0) * np.pi) * 0.5 + 0.5
    rings = rings * 0.6 + 0.4 * (_bilinear(np.repeat(value_noise(rng, 10, 64), 8, axis=1), SIZE))
    shade = 0.85 + 0.3 * ((np.sin(plank_id * 78.233) * 43758.5453) % 1.0)
    colour = tint(base) * shade[..., None] * (0.82 + 0.3 * rings[..., None])
    colour *= (1.0 - 0.5 * seam[..., None])
    height = rings * 0.12 + grain * 0.15 - seam * 0.5
    return colour, height, np.zeros_like(height), np.full_like(height, 0.1)


def _gravel(base, rng, sandy=False):
    cells, ids = worley(rng, 40 if not sandy else 64)
    pebble = np.clip(1.0 - cells * 2.6, 0.0, 1.0)
    shade = 0.7 + 0.6 * ((np.sin(ids * 12.9898) * 43758.5453) % 1.0)[..., None]
    fine = value_noise(rng, 240)
    colour = tint(base) * shade * (0.9 + 0.2 * fine[..., None])
    if sandy:
        dunes = ridge(rng, 10, 3)
        colour *= (0.92 + 0.16 * dunes[..., None])
        height = dunes * 0.35 + fine * 0.15 + pebble * 0.1
    else:
        gaps = np.clip(cells * 3.0 - 1.4, 0.0, 1.0)
        colour *= (1.0 - 0.55 * gaps[..., None])
        height = pebble * 0.8 - gaps * 0.5 + fine * 0.1
    return colour, height, np.zeros_like(height), np.full_like(height, 0.05)


def _hazard(base, rng):
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    stripes = ((xx + yy) * 6.0) % 1.0 < 0.5
    wear = np.clip(fbm(rng, 26, 5) - 0.5, 0.0, 1.0) * 2.2
    chips = np.clip(value_noise(rng, 160) - 0.8, 0.0, 1.0) * 5.0
    colour = np.where(stripes[..., None], tint((0.09, 0.09, 0.09)), tint(base))
    colour *= (0.88 + 0.2 * fbm(rng, 10, 4)[..., None])
    colour = mix(colour, tint((0.35, 0.34, 0.33)), np.clip(wear, 0.0, 1.0) * 0.8)
    colour *= (1.0 - 0.5 * np.clip(chips, 0.0, 1.0)[..., None])
    height = 0.2 * fbm(rng, 14, 4) - np.clip(chips, 0.0, 1.0) * 0.4 - wear * 0.1
    metallic = np.clip(wear, 0.0, 1.0) * 0.6
    smooth = np.clip(0.35 - 0.15 * wear, 0.05, 0.5)
    return colour, height, metallic, smooth


def _plain(base, rng, smooth_value, speckle=0.1):
    fine = value_noise(rng, 200)
    blotch = fbm(rng, 7, 4)
    colour = tint(base) * (0.94 + 0.12 * blotch[..., None]) * (1.0 - speckle * fine[..., None])
    height = blotch * 0.12 + fine * 0.05
    return colour, height, np.zeros_like(height), np.full_like(height, smooth_value)


def _gun_steel(base, rng):
    """Parkerised steel: even, slightly oily, scratched only where things rub.

    The 1.0.9 recipe summed a per pixel height into a strong normal map, so at
    viewmodel distance a receiver looked like a slab of asphalt. A real finish is
    almost flat: a phosphated grain you only see at arm's length, a slow oil film
    that shifts the value by a few percent, and a few bright scratches.
    """
    oil = fbm(rng, 4, 3)                                # long oil film
    blast = blur(value_noise(rng, 384), 2)              # phosphating grain
    drift = blur(value_noise(rng, 64), 4)               # machining drift
    scratches = np.clip(ridge(rng, 96, 3) - 0.935, 0.0, 1.0) * 9.0
    bare = tint((0.40, 0.405, 0.415))

    colour = tint(base) * (0.975 + 0.075 * oil[..., None])
    colour *= (0.99 + 0.025 * blast[..., None]) * (0.995 + 0.012 * drift[..., None])
    colour = mix(colour, bare, np.clip(scratches, 0.0, 1.0) * 0.45)
    metallic = np.clip(0.94 + 0.03 * oil - 0.20 * scratches, 0.0, 1.0)
    smooth = np.clip(0.48 + 0.06 * (oil - 0.5) + 0.20 * np.clip(scratches, 0.0, 1.0),
                     0.34, 0.86)
    height = 0.05 * blast + 0.07 * drift - 0.22 * scratches
    return colour, blur(height, 1), metallic, smooth


def _gun_bare(base, rng):
    """Machined steel: lathe marks on barrels, milling marks on carriers.

    Bright and much smoother than the finished parts around it - that value and
    roughness contrast is most of what separates a barrel from a receiver.
    """
    yy, _xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    polish = fbm(rng, 5, 3)
    turning = blur(np.sin((yy * 150.0 + polish * 4.0) * np.pi) * 0.5 + 0.5, 1)
    brush = blur(value_noise(rng, 320), 1)

    colour = tint(base) * (0.97 + 0.06 * polish[..., None]) * (0.99 + 0.02 * turning[..., None])
    metallic = np.clip(0.97 + 0.02 * polish, 0.0, 1.0)
    smooth = np.clip(0.68 + 0.05 * (polish - 0.5) - 0.025 * turning, 0.52, 0.88)
    height = 0.04 * turning + 0.03 * brush
    return colour, blur(height, 1), metallic, smooth


def _gun_polymer(base, rng):
    """Glass filled nylon: matte, finely stippled, with worn, polished high spots."""
    stipple = blur(value_noise(rng, 288), 1)
    moulding = fbm(rng, 36, 3)
    scuff = np.clip(fbm(rng, 120, 3) - 0.80, 0.0, 1.0) * 6.0

    colour = tint(base) * (0.98 + 0.045 * moulding[..., None]) * (0.985 + 0.03 * stipple[..., None])
    colour = mix(colour, tint((0.115, 0.117, 0.122)), np.clip(scuff, 0.0, 1.0) * 0.40)
    smooth = np.clip(0.30 + 0.05 * (moulding - 0.5) + 0.10 * np.clip(scuff, 0.0, 1.0),
                     0.20, 0.50)
    height = 0.14 * stipple + 0.05 * moulding - 0.08 * scuff
    return colour, blur(height, 1), np.zeros_like(height), smooth


def _uniform_canvas(base, rng):
    """Ripstop canvas: a visible warp and weft, seams faint, no big blotches."""
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float)
    warp = np.sin(xx * (np.pi * 96.0 / SIZE)) * 0.5 + 0.5
    weft = np.sin(yy * (np.pi * 96.0 / SIZE)) * 0.5 + 0.5
    weave = blur(0.5 * (warp + weft), 1)
    slub = blur(value_noise(rng, 64), 2)
    fold = fbm(rng, 8, 3)
    dust = np.clip(fbm(rng, 18, 3) - 0.66, 0.0, 1.0) * 2.4

    colour = tint(base) * (0.94 + 0.08 * slub[..., None]) * (0.98 + 0.03 * weave[..., None])
    colour = colour * (0.96 + 0.07 * fold[..., None])
    colour = mix(colour, tint((0.22, 0.21, 0.18)), np.clip(dust, 0.0, 1.0) * 0.45)
    smooth = np.clip(0.14 + 0.05 * slub + 0.10 * np.clip(dust, 0.0, 1.0), 0.06, 0.30)
    height = 0.22 * weave + 0.10 * slub + 0.06 * fold
    return colour, blur(height, 1), np.zeros_like(height), smooth


def _armor_plate(base, rng):
    """Ceramic composite plate in a fabric cover: matte, finely pitted, scuffed."""
    pits = blur(value_noise(rng, 200), 1)
    cover = blur(value_noise(rng, 90), 1)
    scuff = np.clip(fbm(rng, 22, 3) - 0.74, 0.0, 1.0) * 3.0
    edge = np.clip(ridge(rng, 30, 2) - 0.86, 0.0, 1.0) * 6.0

    colour = tint(base) * (0.95 + 0.07 * cover[..., None]) * (0.98 + 0.03 * pits[..., None])
    colour = mix(colour, tint((0.26, 0.26, 0.25)), np.clip(scuff, 0.0, 1.0) * 0.55)
    colour = colour * (1.0 - 0.12 * np.clip(edge, 0.0, 1.0)[..., None])
    smooth = np.clip(0.20 + 0.06 * (cover - 0.5) + 0.20 * np.clip(scuff, 0.0, 1.0), 0.10, 0.48)
    height = 0.12 * pits + 0.06 * cover - 0.14 * np.clip(edge, 0.0, 1.0)
    return colour, blur(height, 1), np.zeros_like(height), smooth


def _glove_leather(base, rng):
    """Oiled leather glove: pebble grain, creases, worn pale where it flexes.

    A glove is read from half a metre, so the grain has to be fine - a coarse cell
    pattern reads as reptile skin - while the creases carry the value.
    """
    pebble = blur(value_noise(rng, 190), 1)
    grain = blur(value_noise(rng, 420), 1)
    creases = np.clip(ridge(rng, 14, 3) - 0.62, 0.0, 1.0) * 2.2
    scuff = np.clip(fbm(rng, 26, 3) - 0.72, 0.0, 1.0) * 3.0

    colour = tint(base) * (0.93 + 0.10 * pebble[..., None]) * (0.99 + 0.02 * grain[..., None])
    colour = colour * (1.0 - 0.18 * np.clip(creases, 0.0, 1.0)[..., None])
    colour = mix(colour, tint((0.26, 0.225, 0.185)), np.clip(scuff, 0.0, 1.0) * 0.55)
    smooth = np.clip(0.30 + 0.04 * (pebble - 0.5) + 0.16 * np.clip(scuff, 0.0, 1.0)
                     - 0.08 * np.clip(creases, 0.0, 1.0), 0.18, 0.52)
    height = 0.16 * pebble + 0.05 * grain - 0.22 * np.clip(creases, 0.0, 1.0)
    return colour, blur(height, 1), np.zeros_like(height), smooth


def _gun_paint(base, rng):
    """Olive drab enamel over an alloy chassis, chipped down to bare metal.

    Chips are thin and rare. Broad chipping low down is what made the 1.0.9
    enamel read as camouflage paint rather than a coat that has been knocked.
    """
    film = fbm(rng, 4, 3)
    grain = blur(value_noise(rng, 320), 1)
    chips = np.clip(ridge(rng, 150, 3) - 0.915, 0.0, 1.0) * 12.0

    colour = tint(base) * (0.97 + 0.06 * film[..., None]) * (0.995 + 0.012 * grain[..., None])
    colour = mix(colour, tint((0.29, 0.295, 0.305)), np.clip(chips, 0.0, 1.0) * 0.85)
    metallic = np.clip(chips * 0.9, 0.0, 1.0)
    smooth = np.clip(0.42 + 0.05 * (film - 0.5) + 0.22 * np.clip(chips, 0.0, 1.0),
                     0.28, 0.80)
    height = 0.03 * grain + 0.03 * film - 0.26 * chips
    return colour, blur(height, 1), metallic, smooth


def _gun_wood(base, rng):
    """Oiled walnut: grain that follows the blank, pores across it, no planks.

    The map wood recipe cuts boards with seams; on a rifle stock that reads as a
    crate. This is one piece of timber: long dark grain lines, fine pores, and a
    varnish soft enough to keep the wood matte.
    """
    yy, _xx = np.mgrid[0:SIZE, 0:SIZE].astype(float) / SIZE
    warp = fbm(rng, 5, 3)
    rings = np.sin((yy * 44.0 + warp * 6.0) * np.pi) * 0.5 + 0.5
    fine = np.sin((yy * 190.0 + warp * 3.0) * np.pi) * 0.5 + 0.5
    figure = fbm(rng, 11, 4)
    pores = np.clip(value_noise(rng, 300) - 0.66, 0.0, 1.0)

    colour = tint(base) * (0.91 + 0.12 * rings[..., None])
    colour *= (0.98 + 0.04 * figure[..., None]) * (0.99 + 0.015 * fine[..., None])
    colour *= (1.0 - 0.14 * pores[..., None])
    smooth = np.clip(0.42 + 0.05 * figure - 0.07 * pores, 0.26, 0.64)
    height = 0.05 * rings + 0.02 * fine - 0.08 * pores
    return colour, blur(height, 1), np.zeros_like(height), smooth


RECIPES = {
    "concrete_raw": lambda base, rng: _concrete(base, rng),
    "concrete_painted": lambda base, rng: _concrete(base, rng, painted=True),
    "concrete_worn": lambda base, rng: _concrete(base, rng, worn=True),
    "brick": _brick,
    "steel_painted": lambda base, rng: _steel(base, rng, rust_amount=0.3),
    "steel_dark": lambda base, rng: _steel(base, rng, rust_amount=0.15),
    "steel_rust": lambda base, rng: _steel(base, rng, rust_amount=0.9),
    "sheet_metal": _sheet_metal,
    "grating": _grating,
    "hardened_steel": lambda base, rng: _steel(base, rng, rust_amount=0.02, streaks=0.5),
    "wood_crate": _wood,
    "rubber": lambda base, rng: _plain(base, rng, 0.06, speckle=0.25),
    "glass": lambda base, rng: _plain(base, rng, 0.92, speckle=0.02),
    "gravel": lambda base, rng: _gravel(base, rng),
    "sand": lambda base, rng: _gravel(base, rng, sandy=True),
    "ceramic": lambda base, rng: _plain(base, rng, 0.6, speckle=0.05),
    "hazard": _hazard,
    # Weapon surfaces get their own texture names: they reuse the map recipes, but
    # writing them under the map's file names would darken every wall and crate too.
    "gunmetal": _gun_steel,
    "gunwood": _gun_wood,
    "gunolive": _gun_paint,
    "gunpolymer": _gun_polymer,
    "gunbare": _gun_bare,
    "gloveleather": _glove_leather,
    "uniformcanvas": _uniform_canvas,
    "armorplate": _armor_plate,
    "militiacanvas": _uniform_canvas,
    "militiasteel": _armor_plate,
}

TILING = {
    "concrete_raw": 2.0, "concrete_painted": 2.0, "concrete_worn": 2.0, "brick": 2.0,
    "steel_painted": 1.5, "steel_dark": 1.5, "steel_rust": 1.5, "sheet_metal": 1.5,
    "grating": 3.0, "hardened_steel": 1.0, "wood_crate": 1.0, "rubber": 1.5,
    "glass": 1.0, "gravel": 3.0, "sand": 3.0, "ceramic": 1.0, "hazard": 1.0,
    # Weapon maps repeat several times across a viewmodel sized UV island: at 1.0 the
    # blotches of a wall texture read as hammered cast iron on an anodised receiver.
    "gunmetal": 5.5, "gunwood": 1.8, "gunolive": 4.5, "gunpolymer": 6.0, "gunbare": 4.0,
    "gloveleather": 7.0, "uniformcanvas": 5.0, "armorplate": 6.0,
    "militiacanvas": 5.0, "militiasteel": 6.0,
}


# --------------------------------------------------------------------------
# Unity asset sidecars
# --------------------------------------------------------------------------
TEXTURE_META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  textureType: {texture_type}
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 4
    mipBias: 0
    wrapU: 0
    wrapV: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsAlphas: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

MATERIAL_META = """fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 2100000
  userData:
  assetBundleName:
  assetBundleVariant:
"""

MATERIAL_BODY = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Shader: {{fileID: 46, guid: 0000000000000000f000000000000000, type: 0}}
  m_ValidKeywords: [{keywords}]
  m_InvalidKeywords: []
  m_LightmapFlags: {lightmap_flags}
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: {render_queue}
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _BumpMap:
        m_Texture: {bump}
        m_Scale: {{x: {tiling}, y: {tiling}}}
        m_Offset: {{x: 0, y: 0}}
    - _DetailAlbedoMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _DetailMask:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _DetailNormalMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _EmissionMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _MainTex:
        m_Texture: {albedo}
        m_Scale: {{x: {tiling}, y: {tiling}}}
        m_Offset: {{x: 0, y: 0}}
    - _MetallicGlossMap:
        m_Texture: {metsmo}
        m_Scale: {{x: {tiling}, y: {tiling}}}
        m_Offset: {{x: 0, y: 0}}
    - _OcclusionMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _ParallaxMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _BumpScale: 1
    - _Cutoff: 0.5
    - _DetailNormalMapScale: 1
    - _DstBlend: {dst_blend}
    - _GlossMapScale: 1
    - _Glossiness: {glossiness}
    - _GlossyReflections: 1
    - _Metallic: {metallic}
    - _Mode: {mode}
    - _OcclusionStrength: 1
    - _Parallax: 0.02
    - _SmoothnessTextureChannel: 0
    - _SpecularHighlights: 1
    - _SrcBlend: {src_blend}
    - _UVSec: 0
    - _ZWrite: {zwrite}
    m_Colors:
    - _Color: {{r: 1, g: 1, b: 1, a: {alpha}}}
    - _EmissionColor: {{r: {emission}, a: 1}}
"""


def texture_ref(guid: str) -> str:
    return f"{{fileID: 2800000, guid: {guid}, type: 3}}"


def write_text(path: str, content: str) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def main() -> int:
    with open(os.path.join(DATA_DIR, "kit_manifest.json"), encoding="utf-8") as handle:
        surfaces = json.load(handle)["surfaces"]

    os.makedirs(TEXTURE_DIR, exist_ok=True)
    os.makedirs(MATERIAL_DIR, exist_ok=True)
    material_index: Dict[str, dict] = {}

    for surface, spec in surfaces.items():
        texture_name = spec.get("texture", "none")
        base = tuple(spec["baseColor"])
        rng = np.random.default_rng(SEED + int(hashlib.md5(surface.encode()).hexdigest()[:8], 16) % 100000)
        entry: Dict[str, object] = {
            "surface": surface,
            "kind": spec["kind"],
            "material": f"M_{surface}",
            "tiling": TILING.get(texture_name, 1.0),
            "roughness": spec["roughness"],
            "metallic": spec["metallic"],
        }

        albedo_ref = "{fileID: 0}"
        bump_ref = "{fileID: 0}"
        metsmo_ref = "{fileID: 0}"
        if texture_name in RECIPES:
            albedo, height, metallic, smooth = RECIPES[texture_name](base, rng)
            # Per surface shade: the weapon surfaces reuse the map recipes (so the
            # whole project still ships twenty two generated surfaces) but need a
            # much darker value than a wall or a crate, otherwise a phosphated
            # receiver renders as galvanised sheet.
            shade = float(spec.get("shade", 1.0))
            if abs(shade - 1.0) > 1e-6:
                albedo = np.clip(albedo * shade, 0.0, 1.0)
            # Weapon surfaces carry their detail in the silhouette and the
            # roughness, not in a deep bump: a strong normal turns a receiver
            # into stone at viewmodel distance.
            strength = 0.6 if texture_name.startswith("gun") else 2.2
            maps = {
                "albedo": (albedo, True),
                "normal": (normal_from_height(height, strength), False),
                "metsmo": (np.stack([metallic, np.ones_like(height), np.zeros_like(height),
                                     smooth], -1), False),
            }
            for suffix, (array, srgb) in maps.items():
                file_name = f"T_{texture_name}_{suffix}.png"
                to_png(os.path.join(TEXTURE_DIR, file_name), array, srgb)
                write_text(os.path.join(TEXTURE_DIR, file_name + ".meta"),
                           TEXTURE_META.format(guid=guid_of(file_name),
                                               srgb=1 if srgb else 0,
                                               texture_type=1 if suffix == "normal" else 0))
                entry[suffix] = f"Textures/{file_name}"
            albedo_ref = texture_ref(guid_of(f"T_{texture_name}_albedo.png"))
            bump_ref = texture_ref(guid_of(f"T_{texture_name}_normal.png"))
            metsmo_ref = texture_ref(guid_of(f"T_{texture_name}_metsmo.png"))
            keywords = "_METALLICGLOSSMAP, _NORMALMAP"
        else:
            keywords = "_EMISSION"

        emissive = surface.endswith("_emissive")
        transparent = surface == "glass"
        if emissive:
            keywords = "_EMISSION"
            emission = "{:.3f}, g: {:.3f}, b: {:.3f}".format(*base)
            lightmap_flags = 1
            entry["emission"] = list(base)
        else:
            emission = "0, g: 0, b: 0"
            lightmap_flags = 4
        body = MATERIAL_BODY.format(
            name=f"M_{surface}",
            keywords=keywords,
            lightmap_flags=lightmap_flags,
            render_queue=3000 if transparent else -1,
            bump=bump_ref,
            albedo=albedo_ref,
            metsmo=metsmo_ref,
            tiling=entry["tiling"],
            dst_blend=10 if transparent else 0,
            glossiness=round(1.0 - spec["roughness"], 3),
            metallic=spec["metallic"],
            mode=3 if transparent else 0,
            src_blend=5 if transparent else 1,
            zwrite=0 if transparent else 1,
            alpha=0.35 if transparent else 1,
            emission=emission,
        )
        write_text(os.path.join(MATERIAL_DIR, f"M_{surface}.mat"), body)
        write_text(os.path.join(MATERIAL_DIR, f"M_{surface}.mat.meta"),
                   MATERIAL_META.format(guid=guid_of(f"M_{surface}.mat")))
        entry["transparent"] = transparent
        entry["emissive"] = emissive
        material_index[surface] = entry
        print(f"[tex] {surface:<18} -> M_{surface}"
              f"{'  (' + texture_name + ')' if texture_name in RECIPES else '  (emissive)'}")

    payload = {
        "comment": [
            "Surface -> Unity material contract. Generated by",
            "Tools/textures/generate_textures.py from the kit manifest's surface table;",
            "the same surface names are assigned to faces in Blender, so a mesh's",
            "material slot and its Unity material are one lookup apart.",
        ],
        "shader": "Standard (Built-in render pipeline, metallic workflow)",
        "materials": material_index,
    }
    with open(os.path.join(DATA_DIR, "surface_materials.json"), "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)
        fh.write("\n")
    print(f"[tex] {len(material_index)} materials, "
          f"{len(os.listdir(TEXTURE_DIR)) // 2} texture sets written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
