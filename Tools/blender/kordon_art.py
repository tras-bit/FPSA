#!/usr/bin/env python3
"""Shared helpers for the KORDON art pipeline (headless Blender 4.5 LTS).

Everything the game ships as 3D content is built by these scripts:

* ``build_kit.py``   - the modular industrial kit (walls, floors, structure,
  catwalks, doors, cover, props, landmarks) -> ``.blend`` source + one FBX per
  module + ``kit_manifest.json``;
* ``build_maps.py``  - the PvP map ``Zavod-7`` and the co-op map
  ``Operation Silent Kiln``, assembled from kit modules -> ``.blend`` source,
  layout JSON for the Unity map builder, spawn points and the navigation graph.

Conventions used everywhere:

* 1 Blender unit = 1 metre, Z up, module origin at the centre of its footprint
  on the ground (z = 0), so a layout entry is just a position plus a yaw;
* every mesh is authored (no unmodified primitives): boxes carry bevels, panel
  lines, ribs, flanges or bolts, and each part is assigned a named surface;
* surfaces are the single link between Blender, the texture generator
  (``Tools/textures``) and the Unity materials (``Tools/unity``), and each one
  maps to a ``Kordon.Core.Combat.MaterialKind`` so penetration, impact effects,
  footstep audio and noise all read the same value;
* module metadata (footprint, cover height, destructibility, audio surface) is
  written to JSON manifests, because the FBX exporter does not carry Blender
  custom properties.  The ``.blend`` files keep them for humans.

Nothing here imports Unity: the exports are plain FBX/PNG/JSON.
"""
from __future__ import annotations

import json
import math
import os
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import bpy
import mathutils

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_DIR = os.path.join(REPO_ROOT, "ArtSource", "Blender")
MODEL_DIR = os.path.join(REPO_ROOT, "Assets", "_Project", "Art", "Models")
DATA_DIR = os.path.join(REPO_ROOT, "Assets", "StreamingAssets", "Data")

# --------------------------------------------------------------------------
# Surfaces
# --------------------------------------------------------------------------
# kind  -> Kordon.Core.Combat.MaterialKind (penetration / impact / audio)
# color -> linear-ish base colour used for the Blender material and as the seed
#          colour for the procedural texture generator
# tex   -> texture set id, see Tools/textures/make_textures.py
SURFACES: Dict[str, dict] = {
    "concrete_raw": dict(kind="Concrete", color=(0.36, 0.35, 0.33), rough=0.94, metal=0.0,
                         tex="concrete_raw"),
    "concrete_painted": dict(kind="Concrete", color=(0.30, 0.33, 0.30), rough=0.86, metal=0.0,
                             tex="concrete_painted"),
    "concrete_worn": dict(kind="Concrete", color=(0.44, 0.42, 0.38), rough=0.97, metal=0.0,
                          tex="concrete_worn"),
    "brick": dict(kind="Brick", color=(0.34, 0.24, 0.20), rough=0.90, metal=0.0, tex="brick"),
    "steel_painted": dict(kind="Steel", color=(0.20, 0.27, 0.20), rough=0.62, metal=0.55,
                          tex="steel_painted"),
    "steel_dark": dict(kind="Steel", color=(0.11, 0.12, 0.13), rough=0.55, metal=0.80,
                       tex="steel_dark"),
    "steel_rust": dict(kind="Steel", color=(0.32, 0.20, 0.13), rough=0.88, metal=0.35,
                       tex="steel_rust"),
    "sheet_metal": dict(kind="SheetMetal", color=(0.42, 0.44, 0.45), rough=0.48, metal=0.85,
                        tex="sheet_metal"),
    "grating": dict(kind="SheetMetal", color=(0.24, 0.25, 0.26), rough=0.60, metal=0.85,
                    tex="grating"),
    "hardened_steel": dict(kind="HardenedSteel", color=(0.52, 0.53, 0.55), rough=0.34, metal=0.95,
                           tex="hardened_steel"),
    "wood_crate": dict(kind="Wood", color=(0.38, 0.29, 0.18), rough=0.88, metal=0.0,
                       tex="wood_crate"),
    "rubber": dict(kind="Rubber", color=(0.06, 0.06, 0.07), rough=0.96, metal=0.0, tex="rubber"),
    "glass": dict(kind="Glass", color=(0.55, 0.62, 0.62), rough=0.08, metal=0.0, tex="glass"),
    "gravel": dict(kind="Gravel", color=(0.30, 0.28, 0.25), rough=0.99, metal=0.0, tex="gravel"),
    "sand": dict(kind="Sand", color=(0.45, 0.40, 0.31), rough=0.99, metal=0.0, tex="sand"),
    "ceramic_insulator": dict(kind="Ceramic", color=(0.55, 0.52, 0.45), rough=0.42, metal=0.0,
                              tex="ceramic"),
    "lamp_emissive": dict(kind="Glass", color=(1.00, 0.86, 0.55), rough=0.20, metal=0.0,
                          tex="none", emissive=(14.0, 9.6, 4.2)),
    "screen_emissive": dict(kind="Glass", color=(0.35, 0.72, 0.55), rough=0.25, metal=0.0,
                            tex="none", emissive=(1.6, 3.4, 2.4)),
    "hazard_paint": dict(kind="Steel", color=(0.72, 0.55, 0.08), rough=0.70, metal=0.20,
                         tex="hazard"),
    # Weapon-specific surfaces.  A rifle in a render at this scale needs a darker,
    # less saturated palette than a crate or a wall: oiled walnut instead of pallet
    # wood, phosphated steel instead of galvanised sheet, painted chassis green.
    "gun_wood": dict(kind="Wood", color=(0.132, 0.071, 0.033), rough=0.42, metal=0.0,
                     tex="gunwood", shade=0.46),
    "gun_metal": dict(kind="Steel", color=(0.160, 0.166, 0.176), rough=0.38, metal=0.78,
                      tex="gunmetal", shade=0.34),
    "gun_bare": dict(kind="Steel", color=(0.44, 0.445, 0.455), rough=0.24, metal=1.0,
                     tex="gunbare", shade=0.55),
    "gun_olive": dict(kind="Steel", color=(0.140, 0.156, 0.090), rough=0.54, metal=0.08,
                      tex="gunolive", shade=0.36),
    "gun_polymer": dict(kind="Rubber", color=(0.078, 0.079, 0.081), rough=0.74, metal=0.0,
                        tex="gunpolymer", shade=0.20),
    # First person gloves: dark oiled leather with a fine pebble grain, worn pale
    # on the knuckles.  Its own surface, so the hands never pick up the stipple of
    # the weapon's moulded nylon.
    "glove_leather": dict(kind="Rubber", color=(0.120, 0.096, 0.076), rough=0.80,
                          metal=0.0, tex="gloveleather", shade=0.30),
    # Operator clothing.  Canvas and plate are two different materials on purpose:
    # a uniform that renders the same as the armour over it makes a soldier look
    # like one grey shape, which is exactly what the capsule bodies did.
    "uniform_canvas": dict(kind="Rubber", color=(0.098, 0.118, 0.072), rough=0.92,
                           metal=0.0, tex="uniformcanvas", shade=0.30),
    "armor_plate": dict(kind="Rubber", color=(0.080, 0.086, 0.070), rough=0.86,
                        metal=0.0, tex="armorplate", shade=0.30),
    "militia_canvas": dict(kind="Rubber", color=(0.118, 0.096, 0.062), rough=0.94,
                           metal=0.0, tex="militiacanvas", shade=0.32),
    "militia_steel": dict(kind="Steel", color=(0.130, 0.115, 0.096), rough=0.72,
                          metal=0.35, tex="militiasteel", shade=0.30),
}

_MATERIAL_CACHE: Dict[str, bpy.types.Material] = {}

# KORDON_ART_DETAIL=0 skips bevels and UV unwrapping for a fast iteration loop;
# every committed export is built with detail on.
DETAIL = os.environ.get("KORDON_ART_DETAIL", "1") != "0"


def surface_material(surface: str) -> bpy.types.Material:
    """Create (once) and return the Blender material for a surface id."""
    if surface in _MATERIAL_CACHE:
        return _MATERIAL_CACHE[surface]
    if surface not in SURFACES:
        raise KeyError(f"unknown surface '{surface}'")

    spec = SURFACES[surface]
    name = f"KRD_{surface}"
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    if principled is not None:
        colour = spec["color"]
        principled.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
        principled.inputs["Roughness"].default_value = spec["rough"]
        principled.inputs["Metallic"].default_value = spec["metal"]
        emission = spec.get("emissive")
        if emission is not None:
            principled.inputs["Emission Color"].default_value = (
                emission[0], emission[1], emission[2], 1.0)
            principled.inputs["Emission Strength"].default_value = 1.0
    if surface == "glass" and principled is not None:
        # Transparent in Blender; the Unity material written by Tools/unity is the one
        # that actually renders in game (transparent Standard shader).
        principled.inputs["Alpha"].default_value = 0.28
        principled.inputs["IOR"].default_value = 1.5
        material.use_backface_culling = False
    material["kordon.surface"] = surface
    material["kordon.materialKind"] = spec["kind"]
    material["kordon.texture"] = spec["tex"]
    _MATERIAL_CACHE[surface] = material
    return material


# --------------------------------------------------------------------------
# Scene setup
# --------------------------------------------------------------------------
def reset_scene() -> bpy.types.Scene:
    """Empty the file, set metric units and enable the exporters we need."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for module in ("io_scene_fbx", "io_scene_gltf2"):
        try:
            bpy.ops.preferences.addon_enable(module=module)
        except Exception:
            pass

    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.curves):
        for item in list(block):
            try:
                block.remove(item)
            except ReferenceError:
                pass
    _MATERIAL_CACHE.clear()

    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"
    scene.name = "Kordon"
    return scene


def _link(obj: bpy.types.Object) -> bpy.types.Object:
    bpy.context.collection.objects.link(obj)
    return obj


# --------------------------------------------------------------------------
# Primitive builders (authored meshes, never a bare default cube)
# --------------------------------------------------------------------------
def box(size: Sequence[float], center: Sequence[float] = (0.0, 0.0, 0.0),
        surface: str = "steel_dark", name: str = "box") -> bpy.types.Object:
    """Axis aligned box built from an explicit mesh (exact size, clean normals)."""
    hx, hy, hz = (s * 0.5 for s in size)
    cx, cy, cz = center
    v = [
        (cx - hx, cy - hy, cz - hz), (cx + hx, cy - hy, cz - hz),
        (cx + hx, cy + hy, cz - hz), (cx - hx, cy + hy, cz - hz),
        (cx - hx, cy - hy, cz + hz), (cx + hx, cy - hy, cz + hz),
        (cx + hx, cy + hy, cz + hz), (cx - hx, cy + hy, cz + hz),
    ]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(v, [], f)
    mesh.update()
    obj = _link(bpy.data.objects.new(name, mesh))
    assign_surface(obj, surface)
    return obj


def cylinder(radius_bottom: float, radius_top: float, depth: float,
             center: Sequence[float] = (0.0, 0.0, 0.0),
             rotation_deg: Sequence[float] = (0.0, 0.0, 0.0),
             segments: int = 18, surface: str = "steel_dark",
             name: str = "cylinder", cap: bool = True) -> bpy.types.Object:
    """Cylinder / cone / tube segment with explicit ngon caps."""
    half = depth * 0.5
    verts: List[Tuple[float, float, float]] = []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        verts.append((radius_bottom * math.cos(a), radius_bottom * math.sin(a), -half))
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        verts.append((radius_top * math.cos(a), radius_top * math.sin(a), half))

    faces: List[tuple] = []
    for i in range(segments):
        j = (i + 1) % segments
        faces.append((i, j, segments + j, segments + i))
    if cap and radius_bottom > 1e-5:
        faces.append(tuple(range(segments - 1, -1, -1)))
    if cap and radius_top > 1e-5:
        faces.append(tuple(range(segments, segments * 2)))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = tuple(center)
    obj.rotation_euler = tuple(math.radians(r) for r in rotation_deg)
    _link(obj)
    assign_surface(obj, surface)
    return obj


def torus_band(radius: float, tube: float, center: Sequence[float],
               rotation_deg: Sequence[float] = (0.0, 0.0, 0.0),
               segments: int = 24, surface: str = "steel_dark",
               name: str = "band") -> bpy.types.Object:
    """A ring / flange / tyre band, built as an extruded polygon ring."""
    verts: List[Tuple[float, float, float]] = []
    faces: List[tuple] = []
    sides = 6
    verts = []
    faces = []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        ca, sa = math.cos(a), math.sin(a)
        for j in range(sides):
            b = 2.0 * math.pi * j / sides
            rr = radius + tube * math.cos(b)
            verts.append((rr * ca, rr * sa, tube * math.sin(b)))
    for i in range(segments):
        ni = (i + 1) % segments
        for j in range(sides):
            nj = (j + 1) % sides
            faces.append((i * sides + j, i * sides + nj, ni * sides + nj, ni * sides + j))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = tuple(center)
    obj.rotation_euler = tuple(math.radians(r) for r in rotation_deg)
    _link(obj)
    assign_surface(obj, surface)
    return obj


def wedge(size: Sequence[float], center: Sequence[float], axis: str = "x",
          surface: str = "steel_dark", name: str = "wedge") -> bpy.types.Object:
    """A ramp / gusset / chamfer block: a box whose top face is collapsed to one side."""
    hx, hy, hz = (s * 0.5 for s in size)
    cx, cy, cz = center
    v = [
        (cx - hx, cy - hy, cz - hz), (cx + hx, cy - hy, cz - hz),
        (cx + hx, cy + hy, cz - hz), (cx - hx, cy + hy, cz - hz),
        (cx - hx, cy - hy, cz + hz), (cx + hx, cy - hy, cz + hz),
        (cx + hx, cy + hy, cz + hz), (cx - hx, cy + hy, cz + hz),
    ]
    if axis == "x":
        v[1] = (cx + hx, cy - hy, cz + hz)
        v[2] = (cx + hx, cy + hy, cz + hz)
    elif axis == "y":
        v[2] = (cx + hx, cy + hy, cz + hz)
        v[3] = (cx - hx, cy + hy, cz + hz)
    else:
        v[4] = (cx - hx, cy - hy, cz - hz)
        v[7] = (cx - hx, cy + hy, cz - hz)
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(v, [], f)
    mesh.update()
    obj = _link(bpy.data.objects.new(name, mesh))
    assign_surface(obj, surface)
    return obj


def slope_box(start: Sequence[float], end: Sequence[float], width: float, thickness: float,
              surface: str = "steel_dark", name: str = "slope") -> bpy.types.Object:
    """A box whose long axis runs from `start` to `end`.

    Used for stair stringers, handrails, ramps, conveyor frames and ladder rails:
    anything that is not axis aligned.  `width` is measured horizontally sideways,
    `thickness` perpendicular to the slope.
    """
    origin = mathutils.Vector(start)
    direction = mathutils.Vector(end) - origin
    length = direction.length
    if length < 1e-5:
        raise ValueError("slope_box needs distinct start and end")
    u = direction.normalized()
    v = u.cross(mathutils.Vector((0.0, 0.0, 1.0)))
    if v.length < 1e-5:
        v = mathutils.Vector((0.0, 1.0, 0.0))
    v.normalize()
    w = u.cross(v).normalized()

    corners = []
    for su in (0.0, length):
        for sv in (-width * 0.5, width * 0.5):
            for sw in (-thickness * 0.5, thickness * 0.5):
                corners.append(origin + u * su + v * sv + w * sw)
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(c) for c in corners], [], faces)
    mesh.update()
    obj = _link(bpy.data.objects.new(name, mesh))
    assign_surface(obj, surface)
    return obj


def ladder(length: float, width: float, rung_step: float,
           start: Sequence[float] = (0.0, 0.0, 0.0),
           end: Optional[Sequence[float]] = None,
           surface: str = "steel_dark") -> List[bpy.types.Object]:
    """Two rails plus rungs, straight or sloped (cage ladders use the same helper)."""
    target = tuple(end) if end is not None else (start[0], start[1], start[2] + length)
    parts = []
    for sign in (-1.0, 1.0):
        offset = mathutils.Vector((0.0, sign * width * 0.5, 0.0))
        parts.append(slope_box(tuple(mathutils.Vector(start) + offset),
                               tuple(mathutils.Vector(target) + offset),
                               0.05, 0.06, surface, "ladder_rail"))
    span = mathutils.Vector(target) - mathutils.Vector(start)
    total = span.length
    steps = max(2, int(round(total / rung_step)))
    for i in range(steps + 1):
        t = i / steps
        centre = mathutils.Vector(start) + span * t
        parts.append(box((0.045, width, 0.045), tuple(centre), "hardened_steel", "rung"))
    return parts


def panel_lines(length: float, height: float, thickness: float, count: int,
                x: float, y: float, z: float, surface: str = "steel_dark",
                axis: str = "x", depth: float = 0.02, width: float = 0.035,
                parts: Optional[List[bpy.types.Object]] = None) -> List[bpy.types.Object]:
    """Recessed-looking seam strips that break up a flat panel."""
    out = parts if parts is not None else []
    for i in range(count):
        t = (i + 1) / (count + 1)
        if axis == "x":
            center = (x + length * t, y, z)
            size = (width, thickness + depth * 2.0, height)
        else:
            center = (x, y + length * t, z)
            size = (thickness + depth * 2.0, width, height)
        out.append(box(size, center, surface, "seam"))
    return out


def bolt_row(length: float, count: int, x: float, y: float, z: float,
             radius: float = 0.022, surface: str = "hardened_steel",
             axis: str = "x", parts: Optional[List[bpy.types.Object]] = None,
             rotation_deg: Sequence[float] = (90.0, 0.0, 0.0)) -> List[bpy.types.Object]:
    """A line of hex bolt heads, the detail that sells a bolted steel structure."""
    out = parts if parts is not None else []
    for i in range(count):
        t = 0.0 if count == 1 else i / (count - 1)
        offset = length * (t - 0.5)
        center = (x + offset, y, z) if axis == "x" else (x, y + offset, z)
        out.append(cylinder(radius, radius, radius * 1.6, center, rotation_deg, 6, surface, "bolt"))
    return out


def flange(radius: float, thickness: float, center: Sequence[float],
           rotation_deg: Sequence[float] = (0.0, 0.0, 0.0),
           surface: str = "steel_dark", bolts: int = 6) -> List[bpy.types.Object]:
    """Pipe flange: a disc plus its bolts."""
    parts = [cylinder(radius, radius, thickness, center, rotation_deg, 20, surface, "flange")]
    for i in range(bolts):
        a = 2.0 * math.pi * i / bolts
        local = mathutils.Vector((math.cos(a) * radius * 0.78, math.sin(a) * radius * 0.78, 0.0))
        rot = mathutils.Euler(tuple(math.radians(r) for r in rotation_deg), "XYZ").to_matrix()
        world = rot @ local + mathutils.Vector(center)
        parts.append(cylinder(thickness * 0.35, thickness * 0.35, thickness * 2.1,
                              tuple(world), rotation_deg, 6, "hardened_steel", "flange_bolt"))
    return parts


def ibeam(length: float, depth: float, width: float, web_t: float, flange_t: float,
          center: Sequence[float], axis: str = "x",
          surface: str = "steel_painted") -> List[bpy.types.Object]:
    """Rolled steel I profile running along `axis`.

    `depth` is the profile height (Z for horizontal beams, X for columns),
    `width` the flange width.  Web and two flanges, nothing else - exactly what a
    real rolled section looks like from arm's length.
    """
    parts: List[bpy.types.Object] = []
    cx, cy, cz = center
    if axis == "x":
        parts.append(box((length, web_t, depth), center, surface, "ibeam_web"))
        for sign in (-1.0, 1.0):
            parts.append(box((length, width, flange_t),
                             (cx, cy, cz + sign * (depth * 0.5 - flange_t * 0.5)),
                             surface, "ibeam_flange"))
    elif axis == "y":
        parts.append(box((web_t, length, depth), center, surface, "ibeam_web"))
        for sign in (-1.0, 1.0):
            parts.append(box((width, length, flange_t),
                             (cx, cy, cz + sign * (depth * 0.5 - flange_t * 0.5)),
                             surface, "ibeam_flange"))
    else:  # vertical column: the profile depth runs along X
        parts.append(box((depth, web_t, length), center, surface, "ibeam_web"))
        for sign in (-1.0, 1.0):
            parts.append(box((flange_t, width, length),
                             (cx + sign * (depth * 0.5 - flange_t * 0.5), cy, cz),
                             surface, "ibeam_flange"))
    return parts


# --------------------------------------------------------------------------
# Object level utilities
# --------------------------------------------------------------------------
def assign_surface(obj: bpy.types.Object, surface: str) -> None:
    material = surface_material(surface)
    if material.name not in [slot.name for slot in obj.material_slots]:
        obj.data.materials.append(material)
    index = [slot.name for slot in obj.material_slots].index(material.name)
    for poly in obj.data.polygons:
        poly.material_index = index
    obj["kordon.surface"] = surface


def assign_faces(obj: bpy.types.Object, indices: Iterable[int], surface: str) -> None:
    material = surface_material(surface)
    names = [slot.name for slot in obj.material_slots]
    if material.name not in names:
        obj.data.materials.append(material)
        names.append(material.name)
    index = names.index(material.name)
    for i in indices:
        obj.data.polygons[i].material_index = index


def join(parts: Sequence[bpy.types.Object], name: str) -> bpy.types.Object:
    """Join authored parts into one object; transforms are baked by the join."""
    parts = [p for p in parts if p is not None]
    if not parts:
        raise ValueError(f"module {name} has no geometry")
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    target = parts[0]
    bpy.context.view_layer.objects.active = target
    if len(parts) > 1:
        bpy.ops.object.join()
    target.name = name
    target.data.name = name
    return target


def bevel(obj: bpy.types.Object, width: float = 0.022, segments: int = 2,
          angle_deg: float = 32.0) -> None:
    """Add and apply an angle-limited bevel: no razor-sharp CG edges anywhere."""
    modifier = obj.modifiers.new("KordonBevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    modifier.angle_limit = math.radians(angle_deg)
    apply_modifiers(obj)


def apply_modifiers(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for modifier in list(obj.modifiers):
        try:
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        except RuntimeError:
            obj.modifiers.remove(modifier)


def subdivide(obj: bpy.types.Object, cuts: int = 1) -> None:
    """Add edge loops so bevels and panel lines have somewhere to sit."""
    if cuts <= 0:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.subdivide(number_cuts=cuts)
    bpy.ops.object.mode_set(mode="OBJECT")


def unwrap(obj: bpy.types.Object, island_margin: float = 0.012,
           angle_limit_deg: float = 68.0) -> None:
    """Smart UV project: every shipped mesh is unwrapped so textures are not smeared."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle_limit_deg),
                             island_margin=island_margin,
                             area_weight=0.0,
                             correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def shade_flat(obj: bpy.types.Object) -> None:
    for poly in obj.data.polygons:
        poly.use_smooth = False


def set_origin_bottom_center(obj: bpy.types.Object) -> None:
    """Move the object origin to the centre of its footprint at ground level."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    bounds = [obj.matrix_world @ mathutils.Vector(corner) for corner in obj.bound_box]
    min_x = min(v.x for v in bounds)
    max_x = max(v.x for v in bounds)
    min_y = min(v.y for v in bounds)
    max_y = max(v.y for v in bounds)
    min_z = min(v.z for v in bounds)
    cursor = bpy.context.scene.cursor.location.copy()
    bpy.context.scene.cursor.location = ((min_x + max_x) * 0.5, (min_y + max_y) * 0.5, min_z)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = cursor
    obj.location = ((min_x + max_x) * 0.5, (min_y + max_y) * 0.5, min_z)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    obj.location = (0.0, 0.0, 0.0)


def world_bounds(obj: bpy.types.Object) -> Tuple[Tuple[float, float, float],
                                                 Tuple[float, float, float]]:
    corners = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    return ((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)),
            (max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))


def footprint(obj: bpy.types.Object) -> Tuple[float, float]:
    (min_x, min_y, _), (max_x, max_y, _) = world_bounds(obj)
    return (round(max_x - min_x, 3), round(max_y - min_y, 3))


def triangle_count(obj: bpy.types.Object) -> int:
    total = 0
    for poly in obj.data.polygons:
        total += len(poly.vertices) - 2
    return total


# --------------------------------------------------------------------------
# Module registry
# --------------------------------------------------------------------------
_MODULES: Dict[str, dict] = {}


def register_module(obj: bpy.types.Object, module_id: str, **meta) -> bpy.types.Object:
    """Finalise a module: bevel, unwrap, origin, metadata, registry entry."""
    obj.name = module_id
    obj.data.name = module_id
    shade_flat(obj)
    bevel_width = meta.pop("bevel", 0.022)
    if DETAIL:
        bevel(obj, bevel_width)
        unwrap(obj)
    set_origin_bottom_center(obj)

    width, depth = footprint(obj)
    (_, _, min_z), (_, _, max_z) = world_bounds(obj)

    # Dominant surface decides the audio/impact material kind of the module as a whole.
    face_count: Dict[int, int] = {}
    for poly in obj.data.polygons:
        face_count[poly.material_index] = face_count.get(poly.material_index, 0) + 1
    dominant = max(face_count, key=lambda i: face_count[i]) if face_count else 0
    dominant_name = (obj.material_slots[dominant].name.replace("KRD_", "")
                     if len(obj.material_slots) > dominant else "steel_dark")
    dominant_kind = SURFACES.get(dominant_name, {}).get("kind", "Steel")
    entry = {
        "id": module_id,
        "size": [round(width, 3), round(depth, 3), round(max_z - min_z, 3)],
        "triangles": triangle_count(obj),
        "surfaces": sorted({slot.name.replace("KRD_", "") for slot in obj.material_slots}),
        "coverHeight": round(float(meta.get("coverHeight", max(0.0, max_z - min_z))), 3),
        "dominantSurface": dominant_name,
        "surfaceKind": meta.get("surfaceKind", dominant_kind),
        "walkable": bool(meta.get("walkable", False)),
        "walkHeight": float(meta.get("walkHeight", 0.0)),
        "climbable": bool(meta.get("climbable", False)),
        "destructible": bool(meta.get("destructible", False)),
        "integrity": float(meta.get("integrity", 0.0)),
        "blocksMovement": bool(meta.get("blocksMovement", True)),
        "blocksSight": bool(meta.get("blocksSight", True)),
        "interactive": bool(meta.get("interactive", False)),
        "category": meta.get("category", "prop"),
        "tags": list(meta.get("tags", [])),
        "description": meta.get("description", ""),
    }

    for key, value in entry.items():
        obj[f"kordon.{key}"] = value if not isinstance(value, (list, dict)) else json.dumps(value)
    obj["kordon.module"] = module_id

    _MODULES[module_id] = entry
    return obj


def modules() -> Dict[str, dict]:
    return dict(_MODULES)


def clear_registry() -> None:
    _MODULES.clear()


def move_to_slot(obj: bpy.types.Object, index: int, columns: int = 6,
                 spacing: float = 12.0) -> None:
    """Park a finished module on the kit overview grid inside the .blend file."""
    column = index % columns
    row = index // columns
    obj.location = (column * spacing, -row * spacing, 0.0)


# --------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------
def select_only(objects: Iterable[bpy.types.Object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    first = list(objects)[0]
    bpy.context.view_layer.objects.active = first


def export_fbx(objects: Sequence[bpy.types.Object], path: str) -> int:
    """Export the given objects to one FBX.

    Settings are the Blender -> Unity convention: metric units at 1 unit = 1 m,
    forward -Z / up Y, no baked space transform, no embedded textures (Unity gets
    its own materials from Tools/unity).  build_kit.py re-imports every file it
    writes and compares the bounding box, so a broken export cannot slip through.
    """
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(objects)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        path_mode="COPY",
        embed_textures=False,
        batch_mode="OFF",
    )
    bpy.ops.object.select_all(action="DESELECT")
    return os.path.getsize(path)


def save_blend(path: str) -> int:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path)
    return os.path.getsize(path)


def write_json(path: str, payload) -> int:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False, sort_keys=False)
        handle.write("\n")
    return os.path.getsize(path)


def place(module: bpy.types.Object, position: Sequence[float], yaw_deg: float = 0.0,
          name: Optional[str] = None) -> bpy.types.Object:
    """Instance a module into a map (linked duplicate, so the .blend stays small)."""
    # Linked duplicate: the mesh (and therefore its material slots) is shared, which
    # keeps the map .blend small even with hundreds of placements.
    copy = bpy.data.objects.new(name or module.name, module.data)
    bpy.context.collection.objects.link(copy)
    copy.location = tuple(position)
    copy.rotation_euler = (0.0, 0.0, math.radians(yaw_deg))
    for key in module.keys():
        if key.startswith("kordon."):
            copy[key] = module[key]
    return copy


def layout_entry(module_id: str, position: Sequence[float], yaw_deg: float = 0.0) -> dict:
    return {
        "module": module_id,
        "position": [round(float(position[0]), 3), round(float(position[1]), 3),
                     round(float(position[2]), 3)],
        "yaw": round(float(yaw_deg), 2),
    }


def ray_blocked(origin: Sequence[float], target: Sequence[float],
                ignore: Iterable[bpy.types.Object] = ()) -> bool:
    """True when a straight line between two points hits something solid."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    direction = mathutils.Vector(target) - mathutils.Vector(origin)
    distance = direction.length
    if distance < 1e-4:
        return False
    direction.normalize()
    ignore_set = {o.name for o in ignore}
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name in ignore_set or obj.hide_get():
            continue
        result, _location, _normal, _index = obj.ray_cast(
            obj.matrix_world.inverted() @ mathutils.Vector(origin),
            (obj.matrix_world.inverted().to_3x3() @ direction).normalized(),
            distance,
            depsgraph=depsgraph,
        )
        if result:
            return True
    return False
