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
        # Weapon maps repeat five to six times across a viewmodel sized island:
        # any coarser and the blotches of the metal map read as cast iron rather
        # than as a machined, phosphated surface.
        density = {"gunwood": 1.8, "gunmetal": 5.5, "gunolive": 4.5,
                   "gunpolymer": 6.0, "gunbare": 4.0,
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

            # Cavity shading from a real ambient occlusion trace. This is what a
            # game asset gets from a baked AO map; Cycles can trace it directly.
            # It uses AO rather than Geometry.Pointiness because pointiness on a
            # low poly beveled mesh saturates near 1 on every smooth shaded face,
            # which turned the whole weapon into polished white metal.
            ao = nodes.new("ShaderNodeAmbientOcclusion")
            ao.inputs["Distance"].default_value = 0.035
            ao.samples = 6
            ao.only_local = True
            cavity = nodes.new("ShaderNodeValToRGB")
            cavity.color_ramp.elements[0].position = 0.25
            cavity.color_ramp.elements[0].color = (1.0, 1.0, 1.0, 1.0)
            cavity.color_ramp.elements[1].position = 0.92
            cavity.color_ramp.elements[1].color = (0.0, 0.0, 0.0, 1.0)
            links.new(ao.outputs["Color"], cavity.inputs["Fac"])

            # albedo: dirty in the cavities, scuffed bright on the edges
            grime = nodes.new("ShaderNodeMixRGB")
            grime.blend_type = "MULTIPLY"
            grime.inputs["Fac"].default_value = 0.55
            grime.inputs["Color2"].default_value = (0.34, 0.34, 0.34, 1.0)
            links.new(image.outputs["Color"], grime.inputs["Color1"])
            cavity_strength = nodes.new("ShaderNodeMath")
            cavity_strength.operation = "MULTIPLY"
            cavity_strength.inputs[1].default_value = 0.75
            links.new(cavity.outputs["Color"], cavity_strength.inputs[0])
            links.new(cavity_strength.outputs["Value"], grime.inputs["Fac"])

            links.new(grime.outputs["Color"], principled.inputs["Base Color"])

            # roughness: polished on the edges, rough in the crud
            rough_cavity = nodes.new("ShaderNodeMixRGB")
            rough_cavity.blend_type = "MIX"
            rough_cavity.inputs["Color2"].default_value = (0.82, 0.82, 0.82, 1.0)
            links.new(cavity.outputs["Color"], rough_cavity.inputs["Fac"])
            links.new(invert.outputs["Value"], rough_cavity.inputs["Color1"])
            links.new(rough_cavity.outputs["Color"], principled.inputs["Roughness"])

            # bare metal appears where the finish has rubbed off
            links.new(separate.outputs["Red"], principled.inputs["Metallic"])


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
    scene.view_settings.exposure = 0.62
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
    backdrop.inputs[0].default_value = (0.0025, 0.0028, 0.0034, 1.0)
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
    dome.inputs[1].default_value = 1.7

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
    """Dark bench with a soft pool of light directly under the weapon.

    A plane with one value and a huge radial ramp reads as a lit wall: the 1.0.9
    sheet had a 0.07 grey floor filling the frame and the weapon was darker than
    the background, which is why every model looked washed out.  This ramp is
    driven by the object coordinates in metres, so it falls to near black within
    a metre and a half of the model.
    """
    bpy.ops.mesh.primitive_plane_add(size=24, location=(0.0, 0.0, z))
    obj = bpy.context.object
    material = bpy.data.materials.new("floor")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes["Principled BSDF"]
    principled.inputs["Roughness"].default_value = 0.62

    coordinates = nodes.new("ShaderNodeTexCoord")
    length = nodes.new("ShaderNodeVectorMath")
    length.operation = "LENGTH"
    map_range = nodes.new("ShaderNodeMapRange")
    map_range.inputs["From Min"].default_value = 0.08
    map_range.inputs["From Max"].default_value = 1.60
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.050, 0.051, 0.055, 1.0)
    ramp.color_ramp.elements[1].position = 1.0
    ramp.color_ramp.elements[1].color = (0.0035, 0.0035, 0.0042, 1.0)
    links.new(coordinates.outputs["Object"], length.inputs[0])
    links.new(length.outputs["Value"], map_range.inputs["Value"])
    links.new(map_range.outputs["Result"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], principled.inputs["Base Color"])
    obj.data.materials.append(material)


def softbox(name, size, location, rotation_deg, strength, colour=(1.0, 1.0, 1.0)):
    """An emissive card the camera never sees but the metal does.

    A gun is dark, low albedo metal: almost everything you read of its shape is
    the environment reflected in it.  Area lights alone leave the flat panels with
    nothing to reflect - which is why the earlier receivers came out as one flat
    grey silhouette - so the studio gets a few large, soft, bright cards overhead
    and to the sides.  They are hidden from camera rays, so the sheet still reads
    on black.
    """
    bpy.ops.mesh.primitive_plane_add(size=size, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = tuple(math.radians(v) for v in rotation_deg)
    material = bpy.data.materials.new(name + "_emit")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs[0].default_value = (*colour, 1.0)
    emission.inputs[1].default_value = strength
    output = nodes.new("ShaderNodeOutputMaterial")
    material.node_tree.links.new(emission.outputs[0], output.inputs[0])
    obj.data.materials.append(material)
    obj.visible_camera = False
    obj.visible_diffuse = False
    return obj


def lights():
    # warm key, cool fill, hard rim: the rim is what separates a black receiver from
    # a black background, which is the whole problem with rendering dark metal
    add_light("key", 70, 1.2, (0.78, -0.95, 1.15), (32, 0, 40), (1.0, 0.95, 0.88))
    add_light("fill", 16, 2.0, (-1.15, -0.55, 0.62), (55, 0, -62), (0.82, 0.89, 1.0))
    add_light("rim", 105, 0.9, (-0.45, 1.15, 0.78), (-52, 0, -145), (0.90, 0.94, 1.0))
    add_light("kicker", 34, 0.7, (1.05, 0.72, 0.48), (-56, 0, 128), (1.0, 0.97, 0.93))
    add_light("top", 22, 1.6, (0.05, 0.05, 1.45), (0, 0, 0), (1.0, 0.98, 0.95))
    # reflections: a long overhead strip, two side cards and a dark flag behind
    softbox("strip_over", 5.0, (0.0, 0.30, 1.05), (0, 0, 0), 2.6, (1.0, 0.97, 0.92))
    softbox("strip_left", 3.2, (-1.55, -0.35, 0.55), (0, 90, 0), 1.6, (0.86, 0.92, 1.0))
    softbox("strip_right", 3.0, (1.60, 0.55, 0.50), (0, -90, 0), 2.0, (1.0, 0.96, 0.90))
    softbox("strip_back", 2.4, (0.35, 1.70, 0.70), (0, 0, 0), 1.4, (1.0, 0.95, 0.90))


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


def frame_parts(objects, lens=68.0, azimuth=34.0, elevation=13.0, margin=1.06):
    """Frame several objects at once with the same solve frame_weapon uses.

    The lineup framed only the weapon, and the hands leaving the frame are wider than
    the weapon: the sidearm and the shotgun were cropped at the edge of their tiles.
    """
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        if obj is None:
            continue
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
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

    scene = bpy.context.scene
    aspect = scene.render.resolution_y / max(1, scene.render.resolution_x)
    vertical_half = 18.0 * min(1.0, aspect)
    horizontal = 2.0 * math.atan(18.0 / lens)
    vertical = 2.0 * math.atan(vertical_half / lens)
    distance = max(half_w / math.tan(horizontal * 0.5),
                   half_h / math.tan(vertical * 0.5)) * margin + depth * 0.6
    distance = max(distance, 0.30)

    camera(centre + direction * distance, centre, lens=lens)
    return centre


def frame_weapon(weapon, lens=68.0, azimuth=34.0, elevation=13.0, margin=1.05):
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

    # Blender's AUTO sensor fit puts the 36 mm sensor on the long side, so the
    # vertical half angle depends on the tile aspect; assuming 18 mm there left
    # every weapon floating in a third of its tile.
    scene = bpy.context.scene
    aspect = scene.render.resolution_y / max(1, scene.render.resolution_x)
    vertical_half = 18.0 * min(1.0, aspect)
    horizontal = 2.0 * math.atan(18.0 / lens)
    vertical = 2.0 * math.atan(vertical_half / lens)
    # distance to the bounding box centre: the near face is `depth` closer than the
    # centre, so half of it is added back to keep the front of the weapon in frame
    distance = max(half_w / math.tan(horizontal * 0.5),
                   half_h / math.tan(vertical * 0.5)) * margin + depth * 0.6
    distance = max(distance, 0.30)

    location = centre + direction * distance
    camera(location, centre, lens=lens)
    return centre


def shoot(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


SAMPLES = int(os.environ.get("KORDON_WEAPON_SAMPLES", "48"))


def render_weapon(weapon_id, path, resolution=(880, 460), closeup=None):
    art.reset_scene()
    weapon, _magazine, meta = bw.build_weapon(weapon_id)
    setup(resolution, samples=SAMPLES)
    apply_textures()
    floor_z = min((weapon.matrix_world @ vertex.co).z for vertex in weapon.data.vertices)
    studio_floor(floor_z - 0.012)
    lights()
    if closeup is None:
        # Frame the weapon *and the hands*: the forearm leaving the frame is the widest
        # thing in the shot, and framing the weapon alone cropped the pistol and shotgun.
        parts = [weapon] + list(meta.get("hands", []))
        if _magazine is not None:
            parts.append(_magazine)
        if meta.get("trigger") is not None:
            parts.append(meta["trigger"])
        frame_parts(parts)
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
    _weapon, _magazine, _meta = bw.build_weapon("tr30")
    setup((1100, 560), samples=64)
    apply_textures()
    studio_floor()
    lights()
    # The detail shot used to be a hardcoded camera, and it stayed pointed at the old
    # position of the hands after they were rebuilt - the review sheet kept showing the
    # previous grip.  It is aimed at the support hand now, which is the part that had to
    # be proved: fingers wrapped on the fore-end.
    support = next((hand for hand in _meta.get("hands", [])
                    if hand.name.startswith("HandLeft")), None)
    if support is not None:
        points = [support.matrix_world @ Vector(corner) for corner in support.bound_box]
        low = Vector((min(p.x for p in points), min(p.y for p in points),
                      min(p.z for p in points)))
        high = Vector((max(p.x for p in points), max(p.y for p in points),
                       max(p.z for p in points)))
        centre = (low + high) * 0.5
        reach = max((high - low).length * 0.95, 0.10)
        camera(centre + Vector((reach * 0.55, -reach * 1.25, reach * 0.75)), centre,
               lens=80)
    else:
        camera((0.52, -0.50, 0.26), (0.00, 0.03, -0.015), lens=85)
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
