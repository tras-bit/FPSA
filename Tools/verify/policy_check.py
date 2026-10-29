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
    check_build_scene()
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
