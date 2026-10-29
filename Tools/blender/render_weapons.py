#!/usr/bin/env python3
"""Render a review sheet of the weapon set from the authored Blender models.

    Tools/blender/render_weapons.py

Writes

    docs/art/weapons-lineup.png    every weapon, three quarter view, on one sheet
    docs/art/weapons-detail.png    the carbine receiver and the sidearm, close up

This is a render of the real exported geometry (the same build_weapon() the FBX
export uses) wearing the same generated surface textures the game loads, so the
sheet shows what the player actually sees rather than a shaded preview.  Cycles on
CPU, so it runs headless in CI-like environments.
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
TEXTURE_DIR = os.path.join(art.REPO_ROOT, "Assets", "_Project", "Art", "Textures")
LINEUP = ("vk74", "tr30", "vs9", "dmr14", "sr338", "k870", "p9")


# --------------------------------------------------------------------------
# Textures: the render uses the generated maps, not a flat colour
# --------------------------------------------------------------------------
def apply_textures(tiling: float = 2.4) -> None:
    """Wire the generated albedo / normal / mask maps into every surface material.

    kordon_art builds flat coloured materials because the game applies the textures
    through Unity materials.  For a review render that is not good enough - flat
    paint is what made the first pass look like plastic - so the same PNGs the game
    loads are mapped here, with a tiling node because smart UV islands normalise to
    the 0..1 square.
    """
    for material in bpy.data.materials:
        surface = material.get("kordon.surface")
        texture = material.get("kordon.texture")
        if not surface or not texture or texture == "none" or not material.use_nodes:
            continue
        albedo = os.path.join(TEXTURE_DIR, f"T_{texture}_albedo.png")
        normal = os.path.join(TEXTURE_DIR, f"T_{texture}_normal.png")
        metsmo = os.path.join(TEXTURE_DIR, f"T_{texture}_metsmo.png")
        if not os.path.exists(albedo):
            continue

        nodes = material.node_tree.nodes
        links = material.node_tree.links
        principled = nodes.get("Principled BSDF")
        if principled is None:
            continue

        # per texture density: wood wants a coarse grain, phosphated steel a fine
        # one, otherwise the wear patches read as camouflage blotches
        density = {"gunwood": 1.6, "gunmetal": 5.0, "gunolive": 4.5,
                   "gunpolymer": 5.0, "gunbare": 4.0,
                   "hardened_steel": 9.0, "rubber": 6.0}.get(texture, tiling)
        coordinates = nodes.new("ShaderNodeTexCoord")
        mapping = nodes.new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (density, density, density)
        links.new(coordinates.outputs["UV"], mapping.inputs["Vector"])

        image = nodes.new("ShaderNodeTexImage")
        image.image = bpy.data.images.load(albedo, check_existing=True)
        image.image.colorspace_settings.name = "sRGB"
        links.new(mapping.outputs["Vector"], image.inputs["Vector"])
        links.new(image.outputs["Color"], principled.inputs["Base Color"])

        if os.path.exists(normal):
            normal_map = nodes.new("ShaderNodeTexImage")
            normal_map.image = bpy.data.images.load(normal, check_existing=True)
            normal_map.image.colorspace_settings.name = "Non-Color"
            links.new(mapping.outputs["Vector"], normal_map.inputs["Vector"])
            normal_node = nodes.new("ShaderNodeNormalMap")
            links.new(normal_map.outputs["Color"], normal_node.inputs["Color"])
            if principled.inputs.get("Normal") is not None:
                links.new(normal_node.outputs["Normal"], principled.inputs["Normal"])

        if os.path.exists(metsmo):
            # R holds metallic, A holds smoothness: split and drive both inputs
            mask = nodes.new("ShaderNodeTexImage")
            mask.image = bpy.data.images.load(metsmo, check_existing=True)
            mask.image.colorspace_settings.name = "Non-Color"
            links.new(mapping.outputs["Vector"], mask.inputs["Vector"])
            separate = nodes.new("ShaderNodeSeparateColor")
            links.new(mask.outputs["Color"], separate.inputs["Color"])
            links.new(separate.outputs["Red"], principled.inputs["Metallic"])
            invert = nodes.new("ShaderNodeMath")
            invert.operation = "SUBTRACT"
            invert.inputs[0].default_value = 1.0
            links.new(mask.outputs["Alpha"], invert.inputs[1])
            links.new(invert.outputs["Value"], principled.inputs["Roughness"])


# --------------------------------------------------------------------------
# Studio
# --------------------------------------------------------------------------
def setup(resolution, samples=48):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = 0.25
    scene.world = scene.world or bpy.data.worlds.new("W")
    build_environment(scene.world)


def build_environment(world):
    """Camera sees a dark backdrop, reflections see a studio dome.

    A metal's look is almost entirely what it reflects.  The previous world was one
    flat colour, so a metal receiver could only ever show three hard specular hits
    from the area lights, which is exactly what "grey plastic" means.  A vertical
    gradient - cool floor, neutral walls, warm overhead - lifted by 2.5 stops, gives
    every bevelled edge something to pick up.  The Light Path mix keeps that dome out
    of the frame, so the sheet still reads on black.
    """
    world.use_nodes = True
    nodes = world.node_tree.nodes
    links = world.node_tree.links
    nodes.clear()

    backdrop = nodes.new("ShaderNodeBackground")
    backdrop.inputs[0].default_value = (0.007, 0.008, 0.010, 1.0)
    backdrop.inputs[1].default_value = 1.0

    coordinates = nodes.new("ShaderNodeTexCoord")
    split = nodes.new("ShaderNodeSeparateXYZ")
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.010, 0.011, 0.014, 1.0)
    ramp.color_ramp.elements[1].position = 0.46
    ramp.color_ramp.elements[1].color = (0.10, 0.11, 0.13, 1.0)
    upper = ramp.color_ramp.elements.new(0.74)
    upper.color = (0.46, 0.47, 0.52, 1.0)
    top = ramp.color_ramp.elements.new(0.94)
    top.color = (0.95, 0.92, 0.86, 1.0)

    dome = nodes.new("ShaderNodeBackground")
    dome.inputs[1].default_value = 4.2

    path = nodes.new("ShaderNodeLightPath")
    mix = nodes.new("ShaderNodeMixShader")
    output = nodes.new("ShaderNodeOutputWorld")

    links.new(coordinates.outputs["Generated"], split.inputs["Vector"])
    links.new(split.outputs["Z"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], dome.inputs[0])
    links.new(path.outputs["Is Camera Ray"], mix.inputs[0])
    links.new(dome.outputs[0], mix.inputs[1])
    links.new(backdrop.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], output.inputs["Surface"])


def add_light(name, energy, size, location, rotation_deg, colour):
    light = bpy.data.lights.new(name, "AREA")
    light.energy = energy
    light.size = size
    light.color = colour
    obj = bpy.data.objects.new(name, light)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = tuple(math.radians(v) for v in rotation_deg)


def studio_floor(z: float = -0.14):
    """Dark workbench surface with a soft pool of light under the weapon.

    A flat plane held one value and reflected the dome evenly, which turned the
    whole frame into flat grey.  A radial falloff keeps the eye on the model and
    reads as a lit table rather than a render plane.
    """
    bpy.ops.mesh.primitive_plane_add(size=16, location=(0.0, 0.0, z))
    obj = bpy.context.object
    material = bpy.data.materials.new("floor")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes["Principled BSDF"]
    principled.inputs["Roughness"].default_value = 0.30

    coordinates = nodes.new("ShaderNodeTexCoord")
    gradient = nodes.new("ShaderNodeTexGradient")
    gradient.gradient_type = "RADIAL"
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.070, 0.071, 0.076, 1.0)
    ramp.color_ramp.elements[1].position = 0.62
    ramp.color_ramp.elements[1].color = (0.009, 0.009, 0.011, 1.0)
    links.new(coordinates.outputs["Generated"], gradient.inputs["Vector"])
    links.new(gradient.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], principled.inputs["Base Color"])
    obj.data.materials.append(material)


def lights():
    # warm key, cool fill, hard rim: the rim is what separates a black receiver from
    # a black background, which is the whole problem with rendering dark metal
    add_light("key", 34, 2.2, (0.85, -1.05, 1.30), (36, 0, 36), (1.0, 0.95, 0.88))
    add_light("fill", 6, 3.0, (-1.30, 0.55, 0.85), (62, 0, -54), (0.82, 0.89, 1.0))
    add_light("rim", 32, 1.2, (-0.25, 1.25, 0.95), (-38, 0, -172), (0.94, 0.97, 1.0))
    add_light("kicker", 18, 0.9, (0.95, 0.85, 0.60), (-52, 0, 132), (1.0, 0.97, 0.93))
    add_light("top", 9, 2.4, (0.0, 0.10, 1.45), (0, 0, 0), (1.0, 0.98, 0.95))


def camera(location, target, lens=55.0):
    data = bpy.data.cameras.new("cam")
    obj = bpy.data.objects.new("cam", data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    data.lens = lens
    direction = (Vector(target) - Vector(location)).normalized()
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = obj
    return obj


def frame_weapon(weapon, lens=62.0, azimuth=132.0, elevation=8.0, margin=1.02):
    """Place the camera so the whole weapon fits, from a three quarter angle.

    Framing by the bounding sphere is simple but wasteful: a rifle is long and thin,
    so it ends up a speck in the middle of the tile.  This projects the eight corners
    of the bounding box onto the camera's own right/up axes and solves the distance
    for the axis that runs out first.
    """
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for vertex in weapon.data.vertices:
        world = weapon.matrix_world @ vertex.co
        for axis in range(3):
            lo[axis] = min(lo[axis], world[axis])
            hi[axis] = max(hi[axis], world[axis])
    centre = (lo + hi) * 0.5

    azimuth_rad = math.radians(azimuth)
    elevation_rad = math.radians(elevation)
    direction = Vector((
        math.sin(azimuth_rad) * math.cos(elevation_rad),
        math.cos(azimuth_rad) * math.cos(elevation_rad),
        math.sin(elevation_rad),
    )).normalized()

    right = Vector((0.0, 0.0, 1.0)).cross(direction).normalized()
    up = direction.cross(right).normalized()

    half_w = half_h = depth = 0.0
    for dx in (lo.x, hi.x):
        for dy in (lo.y, hi.y):
            for dz in (lo.z, hi.z):
                offset = Vector((dx, dy, dz)) - centre
                half_w = max(half_w, abs(offset.dot(right)))
                half_h = max(half_h, abs(offset.dot(up)))
                depth = max(depth, abs(offset.dot(direction)))

    horizontal = 2.0 * math.atan(18.0 / lens)
    vertical = 2.0 * math.atan(9.0 / lens)
    distance = max(half_w / math.tan(horizontal * 0.5),
                   half_h / math.tan(vertical * 0.5)) * margin + depth
    distance = max(distance, 0.30)

    location = centre + direction * distance
    camera(location, centre, lens=lens)
    return centre


def shoot(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def render_weapon(weapon_id, path, resolution=(880, 460), closeup=None):
    art.reset_scene()
    weapon, _magazine, _meta = bw.build_weapon(weapon_id)
    setup(resolution)
    apply_textures()
    studio_floor()
    lights()
    if closeup is None:
        frame_weapon(weapon)
    else:
        camera(closeup[0], closeup[1], lens=closeup[2])
    shoot(path)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix="kordon-weapons-")
    tiles = []
    for index, weapon_id in enumerate(LINEUP):
        path = os.path.join(tmp, f"{index}_{weapon_id}.png")
        render_weapon(weapon_id, path)
        tiles.append(path)
        print(f"[weapons] rendered {weapon_id}")

    # Detail sheet: the carbine receiver, where all the small parts live, plus the
    # sidearm at a size where its slide serrations are readable.
    detail = os.path.join(OUT_DIR, "weapons-detail.png")
    art.reset_scene()
    bw.build_weapon("tr30")
    setup((1100, 560), samples=64)
    apply_textures()
    studio_floor()
    lights()
    camera((0.72, -0.60, 0.30), (0.00, 0.01, -0.01), lens=58)
    shoot(detail)

    from PIL import Image, ImageDraw, ImageFont

    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    tile_w, tile_h = 880, 460
    columns = 2
    rows = (len(tiles) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * tile_w, rows * (tile_h + 38)), (16, 18, 21))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(font_path, 22) if os.path.exists(font_path) else None

    for index, (path, weapon_id) in enumerate(zip(tiles, LINEUP)):
        image = Image.open(path).convert("RGB")
        column, row = index % columns, index // columns
        x, y = column * tile_w, row * (tile_h + 38)
        sheet.paste(image, (x, y))
        if font is not None:
            draw.text((x + 18, y + tile_h + 8), weapon_id.upper(), font=font,
                      fill=(226, 118, 34))

    lineup = os.path.join(OUT_DIR, "weapons-lineup.png")
    sheet.save(lineup)
    print(f"[weapons] wrote {os.path.relpath(lineup, art.REPO_ROOT)} and "
          f"{os.path.relpath(detail, art.REPO_ROOT)}")


if __name__ == "__main__":
    main()
