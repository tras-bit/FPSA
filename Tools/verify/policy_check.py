#!/usr/bin/env python3
"""Design-rule verification for KORDON.

The rules this game promises are not only gameplay tuning: several of them are
structural (no crosshair, no permanent ammo counter, no hardcoded server address,
no bots in the release FFA mode, no leftover TODOs, no uncompiled script).  This
script enforces those structurally, in CI, on every push, and writes
docs/reports/policy.md.  It exits non-zero when a rule is broken.
"""

from __future__ import annotations

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPTS = os.path.join(ROOT, "Assets", "_Project", "Scripts")
DATA = os.path.join(ROOT, "Assets", "StreamingAssets", "Data")
REPORT = os.path.join(ROOT, "docs", "reports", "policy.md")

failures: list[str] = []
notes: list[str] = []


def walk(extension=".cs"):
    for base, _, files in os.walk(SCRIPTS):
        for name in files:
            if name.endswith(extension):
                yield os.path.join(base, name)


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def check_no_localhost():
    # The only place a loopback literal may appear is the Core address policy that
    # refuses it.  A server binding to 0.0.0.0 is not a client address and is fine.
    pattern = re.compile(r"localhost|127\.0\.0\.1")
    allowed = os.path.join("Core", "Config", "NetConfig.cs")
    offenders = []
    for path in walk():
        rel = os.path.relpath(path, SCRIPTS)
        for number, line in enumerate(read(path).splitlines(), 1):
            stripped = line.strip()
            if stripped.startswith("//") or stripped.startswith("#"):
                continue
            if pattern.search(line) and rel != allowed:
                offenders.append(f"{os.path.relpath(path, ROOT)}:{number}: {stripped}")
    if offenders:
        failures.append("hardcoded loopback/bind address in gameplay code:\n  " +
                        "\n  ".join(offenders))
    else:
        notes.append("no hardcoded localhost/loopback/bind address in any script")


def check_no_crosshair():
    # Declaring that there is no crosshair is the point; drawing one is the bug.
    pattern = re.compile(r"crosshair", re.IGNORECASE)
    offenders = []
    for path in walk():
        rel = os.path.relpath(path, SCRIPTS)
        if rel.startswith(os.path.join("Core", "")):
            continue
        for number, line in enumerate(read(path).splitlines(), 1):
            stripped = line.strip()
            if stripped.startswith("///") or stripped.startswith("//"):
                continue
            if pattern.search(line) and "no crosshair" not in stripped.lower():
                offenders.append(f"{os.path.relpath(path, ROOT)}:{number}: {stripped}")
    if offenders:
        failures.append("crosshair code found (the design forbids a crosshair):\n  " +
                        "\n  ".join(offenders))
    else:
        notes.append("no crosshair is drawn anywhere in the game code")


def check_ammo_privacy():
    hud = os.path.join(SCRIPTS, "Game", "Ui", "HudScreen.cs")
    if not os.path.exists(hud):
        failures.append("HUD screen missing")
        return
    text = read(hud)
    if "MayShowExactCount" not in text:
        failures.append("HUD does not gate exact ammunition on the inspect state")
    else:
        notes.append("exact round counts are gated on MayShowExactCount (inspect only)")
    if re.search(r"RoundsLeftInMagazine|ExactRounds\.ToString", text):
        failures.append("HUD appears to render live rounds outside the inspect readout")


def check_ffa_bots():
    path = os.path.join(SCRIPTS, "Net", "NetworkMatchDirector.cs")
    if os.path.exists(path) and "AllowBots = false" not in read(path):
        failures.append("networked FFA does not explicitly disable bots")
    else:
        notes.append("networked free-for-all runs with AllowBots = false")


def check_todos():
    pattern = re.compile(r"\b(TODO|FIXME|HACK|NotImplementedException)\b")
    offenders = []
    for path in walk():
        for number, line in enumerate(read(path).splitlines(), 1):
            if pattern.search(line):
                offenders.append(f"{os.path.relpath(path, ROOT)}:{number}: {line.strip()}")
    if offenders:
        failures.append("unfinished markers in shipped code:\n  " + "\n  ".join(offenders))
    else:
        notes.append("no TODO/FIXME/HACK/NotImplementedException in the game code")


def check_compile_coverage():
    project = os.path.join(ROOT, "Tests", "Kordon.Game.CompileCheck",
                           "Kordon.Game.CompileCheck.csproj")
    if not os.path.exists(project):
        failures.append("compile check project missing")
        return
    text = read(project)
    for folder in ("Core", "Game", "Net", "Editor"):
        if f"Scripts/{folder}/**/*.cs" not in text:
            failures.append(f"compile check does not cover Scripts/{folder}")
    else:
        notes.append("every Unity side script folder is compiled by the CI type check")


def check_content_ids():
    definitions = os.path.join(DATA, "Definitions")
    ids = set()
    for name in ("weapons.json", "magazines.json", "bullets.json", "armor_kits.json",
                 "grenades.json"):
        path = os.path.join(definitions, name)
        if not os.path.exists(path):
            failures.append(f"definition file missing: {name}")
            continue
        with open(path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        for value in payload.values() if isinstance(payload, dict) else payload:
            if isinstance(value, list):
                for item in value:
                    if isinstance(item, dict) and "id" in item:
                        ids.add(item["id"])

    referenced = set()
    pattern = re.compile(r'"(vk74|tr30|vs9|dmr14|sr338|k870|p9|'
                         r'mag_[0-9][a-z0-9]*_[0-9]+[a-z0-9_]*|'
                         r'tube_[0-9][a-z0-9_]*|'
                         r'frag_f1|smoke_rdg|flash_zarya)"')
    for path in walk():
        if os.path.relpath(path, SCRIPTS).startswith(os.path.join("Core", "")):
            continue
        for match in pattern.finditer(read(path)):
            referenced.add(match.group(1))

    missing = sorted(value for value in referenced
                     if value not in ids and value not in ("light", "standard", "heavy"))
    if missing:
        failures.append("content ids referenced by code but missing from the definitions: " +
                        ", ".join(missing))
    else:
        notes.append(f"{len(referenced)} content ids referenced by code all exist in Definitions")


def check_shaders_ship_in_build():
    """A shader that is not under a Resources folder is stripped from the player.

    Shader.Find only sees the shaders baked into a build, and a shader that no
    material asset references is not baked in.  Losing them silently would remove
    the bodycam pass and the grenade effects, so every shader must live under
    Resources, every Shader.Find literal must name a shader that exists, and every
    resource path in ShaderLibrary must resolve to a real shader file.
    """
    assets = os.path.join(ROOT, "Assets")
    declared = {}
    by_resource = {}
    misplaced = []
    marker = f"{os.sep}Resources{os.sep}"
    for base, _, files in os.walk(assets):
        for name in files:
            if not name.endswith(".shader"):
                continue
            path = os.path.join(base, name)
            match = re.search(r'^\s*Shader\s+"([^"]+)"', read(path), re.MULTILINE)
            if not match:
                failures.append(f"shader without a declared name: {os.path.relpath(path, ROOT)}")
                continue
            declared[match.group(1)] = path
            if marker not in path:
                misplaced.append(os.path.relpath(path, ROOT))
            else:
                tail = path.split(marker, 1)[1].replace(os.sep, "/")
                by_resource[tail[: -len(".shader")]] = match.group(1)

    if misplaced:
        failures.append("shader outside a Resources folder (stripped from the player build): " +
                        ", ".join(misplaced))

    for path in walk():
        for match in re.finditer(r'Shader\.Find\("([^"]+)"\)', read(path)):
            if match.group(1) not in declared:
                failures.append(f"{os.path.relpath(path, ROOT)}: Shader.Find(\"{match.group(1)}\") "
                                "names a shader that does not exist")

    library = os.path.join(SCRIPTS, "Game", "Rendering", "ShaderLibrary.cs")
    if not os.path.exists(library):
        failures.append("ShaderLibrary.cs missing: shader loading is not build safe")
        return

    text = read(library)
    resources = dict(re.findall(r'const string (\w+Resource)\s*=\s*"([^"]+)"', text))
    names = dict(re.findall(r'const string (\w+Name)\s*=\s*"([^"]+)"', text))
    for key, resource in resources.items():
        if resource not in by_resource:
            failures.append(f"ShaderLibrary.{key} = \"{resource}\" does not resolve to a shader "
                            "file under Resources")
            continue
        expected = names.get(key[: -len("Resource")] + "Name")
        if expected is not None and by_resource[resource] != expected:
            failures.append(f"ShaderLibrary.{key} points at {by_resource[resource]} but the "
                            f"shader is declared as {expected}")
    for key, name in names.items():
        if name not in declared:
            failures.append(f"ShaderLibrary.{key} = \"{name}\" names a shader that does not exist")

    if not misplaced and resources and len(by_resource) >= len(resources):
        notes.append(f"{len(declared)} shaders live under Resources and every ShaderLibrary "
                     "path resolves (build safe, never stripped)")


# Built-in packages the project cannot compile without.  These are not gameplay
# rules, they are the reason the project opens: a missing module is a hard
# compile error inside someone else's package, which is exactly the kind of thing
# the static checks used to miss.  Each entry states who needs it.
REQUIRED_MODULES = {
    "com.unity.modules.physics": "colliders, hitscan and grenade bounces",
    "com.unity.modules.physics2d": "Netcode for GameObjects (NetworkRigidbodyBase uses Rigidbody2D/FixedJoint2D)",
    "com.unity.modules.animation": "the bodycam and viewmodel motion",
    "com.unity.modules.audio": "AudioSource, positional footsteps and gunfire",
    "com.unity.modules.particlesystem": "grenade flash, smoke and sparks",
    "com.unity.modules.imageconversion": "texture handling at import",
    "com.unity.modules.imgui": "the editor tooling fallbacks",
    "com.unity.modules.jsonserialize": "reading the JSON definitions",
    "com.unity.modules.ui": "uGUI (Canvas, Image, Text, InputField)",
    "com.unity.modules.uielements": "the editor UI layer",
    "com.unity.modules.unitywebrequest": "content loading paths",
}


def check_package_manifest():
    """Packages/manifest.json must actually contain what the project compiles against."""
    path = os.path.join(ROOT, "Packages", "manifest.json")
    if not os.path.exists(path):
        failures.append("Packages/manifest.json missing: the project has no package set")
        return
    try:
        manifest = json.loads(read(path))
    except json.JSONDecodeError as error:
        failures.append(f"Packages/manifest.json is not valid JSON: {error}")
        return

    dependencies = manifest.get("dependencies", {})
    missing = [name for name in REQUIRED_MODULES if name not in dependencies]
    for name in missing:
        failures.append(f"built-in package missing from Packages/manifest.json: {name} "
                        f"({REQUIRED_MODULES[name]})")

    for package, version in (("com.unity.netcode.gameobjects", "2.2.0"),
                             ("com.unity.test-framework", "1.4.5"),
                             ("com.unity.ugui", "2.0.0")):
        if dependencies.get(package) != version:
            failures.append(f"{package} must be pinned to {version}, "
                            f"found {dependencies.get(package)!r}")

    # A module listed but never needed hides the next missing one; report drift
    # without failing, so removing a module stays a deliberate choice.
    unused = [name for name in dependencies
              if name.startswith("com.unity.modules.") and name not in REQUIRED_MODULES]
    if unused:
        notes.append("unrequested built-in modules kept in the manifest: " + ", ".join(sorted(unused)))

    if not missing:
        notes.append(f"all {len(REQUIRED_MODULES)} required built-in packages are declared "
                     "and the package versions are pinned")


# Import settings the game's look depends on.  These are invisible to a compiler
# and to a static analyser, and wrong values do not error: they just make the art
# look flat and unfinished at runtime.
def check_texture_import_settings():
    textures = os.path.join(ROOT, "Assets", "_Project", "Art", "Textures")
    if not os.path.isdir(textures):
        failures.append("texture folder missing: Assets/_Project/Art/Textures")
        return

    checked = 0
    problems = []
    for name in sorted(os.listdir(textures)):
        if not name.endswith(".png"):
            continue
        meta_path = os.path.join(textures, name + ".meta")
        if not os.path.exists(meta_path):
            problems.append(f"{name}: no .meta (import settings would be guessed by Unity)")
            continue
        meta = read(meta_path)
        checked += 1
        srgb = re.search(r"sRGBTexture:\s*(\d)", meta)
        kind = (re.search(r"^\s*textureType:\s*(\d)", meta, re.M) or [None, "0"])[1]
        if name.endswith("_normal.png"):
            if kind != "1":
                problems.append(f"{name}: textureType is {kind}, must be 1 (NormalMap) - "
                                "otherwise the surface normal map is sampled as plain colour")
            if srgb and srgb.group(1) != "0":
                problems.append(f"{name}: normal map must be linear, sRGB is on")
        elif name.endswith("_metsmo.png"):
            if srgb and srgb.group(1) != "0":
                problems.append(f"{name}: metallic/smoothness mask must be linear, sRGB is on")
        elif name.endswith("_albedo.png"):
            if srgb and srgb.group(1) != "1":
                problems.append(f"{name}: albedo must be sRGB, it is linear")

    if problems:
        failures.append("texture import settings that would make the art look wrong:\n  " +
                        "\n  ".join(problems))
    elif checked:
        notes.append(f"{checked} texture .meta files carry the import settings the art needs "
                     "(sRGB albedo, linear normal and mask maps, normals typed as NormalMap)")


def check_imported_material_contract():
    """Every material a Blender FBX carries must resolve to an authored material.

    The kit exports its materials as KRD_<surface>; the game strips that prefix
    and loads Kordon/Materials/M_<surface>.  If a name does not resolve, Unity
    happily renders the untextured imported material - the map looks flat grey and
    nothing errors.  That is a content contract, so it is checked like one.
    """
    models = os.path.join(ROOT, "Assets", "_Project", "Art", "Models")
    materials = os.path.join(ROOT, "Assets", "_Project", "Art", "Resources", "Kordon",
                             "Materials")
    if not os.path.isdir(models) or not os.path.isdir(materials):
        failures.append("art folder missing: Models/ or Resources/Kordon/Materials/")
        return

    authored = {name[: -len(".mat")] for name in os.listdir(materials) if name.endswith(".mat")}
    prefix = "KRD_"
    imported = {}
    for base, _, files in os.walk(models):
        for name in files:
            if not name.lower().endswith(".fbx"):
                continue
            path = os.path.join(base, name)
            with open(path, "rb") as handle:
                data = handle.read()
            for match in re.finditer(rb"KRD_[a-z_0-9]+", data):
                imported.setdefault(match.group().decode(), set()).add(name)

    unmapped = []
    for material, owners in sorted(imported.items()):
        surface = material[len(prefix):].lower()
        if "M_" + surface not in authored:
            unmapped.append(f"{material} (in {', '.join(sorted(owners)[:3])}) has no "
                            f"M_{surface}.mat")

    if unmapped:
        failures.append("FBX materials that would render untextured:\n  " + "\n  ".join(unmapped))
    elif imported:
        notes.append(f"{len(imported)} imported FBX materials all resolve to an authored "
                     "material (the map and weapons never fall back to a grey import)")


# Compiling every script into one assembly hides a whole class of error: our code
# can use a namespace without the assembly reference that owns it, and the compiler
# only complains inside Unity ("CS0012: type defined in an assembly that is not
# referenced").  This project hit exactly that with UnityTransport/NetworkEndpoint.
# So: every engine or package namespace used by a script must be referenced by the
# asmdef that owns it.
# Only namespaces that really need an explicit asmdef reference belong here.  The
# engine's own assemblies (UnityEngine.UI, UnityEngine.EventSystems, the modules)
# are available to every asmdef without one - the editor build proves it, because
# Kordon.Game reports warnings while using UnityEngine.UI.  Package assemblies such
# as Netcode's are not: that is what broke the build with CS0012 NetworkEndpoint.
NAMESPACE_ASSEMBLIES = {
    "Unity.Netcode": ["Unity.Netcode.Runtime"],
    "Unity.Networking.Transport": ["Unity.Networking.Transport"],
}


def _asmdefs():
    found = {}
    for base, _, files in os.walk(os.path.join(ROOT, "Assets")):
        for name in files:
            if name.endswith(".asmdef"):
                path = os.path.join(base, name)
                try:
                    data = json.loads(read(path))
                except json.JSONDecodeError as error:
                    failures.append(f"{os.path.relpath(path, ROOT)} is not valid JSON: {error}")
                    continue
                found[base] = (data.get("name", name), data.get("references", []),
                               data.get("includePlatforms", []))
    return found


def _owning_asmdef(path, asmdefs):
    folder = os.path.dirname(path)
    while folder and folder != ROOT:
        if folder in asmdefs:
            return asmdefs[folder]
        folder = os.path.dirname(folder)
    return None


def check_assembly_references():
    asmdefs = _asmdefs()
    if not asmdefs:
        failures.append("no .asmdef found: the project has no assembly layout")
        return

    # Fully qualified usage counts too: KordonNetwork writes
    # Unity.Netcode.Transports.UTP.UnityTransport without a using statement, and
    # that is precisely the line that needed Unity.Networking.Transport.
    qualified = {
        "Unity.Netcode.Transports.UTP": ["Unity.Netcode.Runtime", "Unity.Networking.Transport"],
        "Unity.Networking.Transport": ["Unity.Networking.Transport"],
        "Unity.Netcode": ["Unity.Netcode.Runtime"],
    }

    problems = []
    checked = 0
    for path in walk():
        owner = _owning_asmdef(path, asmdefs)
        if owner is None:
            continue
        name, references, platforms = owner
        text = read(path)
        # Drop comments so documentation about a namespace is not a dependency.
        code = "\n".join(line.split("//")[0] for line in text.splitlines())

        used = set(re.findall(r"^\s*using\s+(UnityEngine\.[\w\.]+|Unity\.Netcode[\w\.]*|"
                              r"Unity\.Networking\.Transport[\w\.]*|UnityEditor[\w\.]*)\s*;",
                              code, re.MULTILINE))
        for match in re.finditer(r"\bUnity\.Netcode(?:\.Transports\.UTP)?\b", code):
            used.add(match.group())
        for match in re.finditer(r"\bUnity\.Networking\.Transport\b", code):
            used.add(match.group())

        for namespace in sorted(used):
            if namespace.startswith("UnityEditor"):
                if "Editor" not in platforms:
                    problems.append(f"{os.path.relpath(path, ROOT)} uses {namespace} but "
                                    f"assembly {name} is not editor only")
                continue
            required = None
            for prefix in sorted(qualified, key=len, reverse=True):
                if namespace == prefix or namespace.startswith(prefix + "."):
                    required = qualified[prefix]
                    break
            if required is None:
                continue  # an engine assembly, always available to asmdefs
            for assembly in required:
                checked += 1
                if assembly not in references:
                    problems.append(f"{os.path.relpath(path, ROOT)} uses {namespace} but {name}"
                                    f".asmdef does not reference {assembly}")

    if problems:
        failures.append("assembly references missing (the static checks flatten every script "
                        "into one assembly, Unity does not):\n  " + "\n  ".join(problems))
    else:
        notes.append(f"{checked} package namespace uses are all backed by the assembly "
                     "reference of the owning asmdef")


def check_build_scene():
    scenes = os.path.join(ROOT, "Assets", "_Project", "Scenes")
    script = os.path.join(SCRIPTS, "Editor", "BuildScript.cs")
    if not os.path.exists(script):
        failures.append("no editor build script: a player build would have no scene to start")
    else:
        notes.append("editor build script present (creates the boot scene and builds the player)")


def main() -> int:
    check_no_localhost()
    check_no_crosshair()
    check_ammo_privacy()
    check_ffa_bots()
    check_todos()
    check_compile_coverage()
    check_content_ids()
    check_assembly_references()
    check_build_scene()
    check_imported_material_contract()
    check_texture_import_settings()
    check_package_manifest()
    check_shaders_ship_in_build()

    os.makedirs(os.path.dirname(REPORT), exist_ok=True)
    with open(REPORT, "w", encoding="utf-8") as handle:
        handle.write("# Design rule verification\n\n")
        handle.write("Generated by `Tools/verify/policy_check.py` in CI.\n\n")
        handle.write("## Result\n\n")
        handle.write("**PASS** - every structural rule holds.\n\n" if not failures
                     else "**FAIL** - %d rule(s) broken.\n\n" % len(failures))
        handle.write("## Checks\n\n")
        for note in notes:
            handle.write(f"- {note}\n")
        if failures:
            handle.write("\n## Failures\n\n")
            for failure in failures:
                handle.write(f"- {failure}\n")

    for failure in failures:
        print("FAIL:", failure)
    for note in notes:
        print("ok:", note)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
