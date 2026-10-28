#!/usr/bin/env python3
"""Build minimal stub shared libraries so the official Blender-as-a-Python-module
(`bpy` wheel from PyPI = real Blender 4.5 LTS) can run headless inside a
container that has no desktop X11/OpenGL stack installed.

Why this is needed
------------------
`bpy/__init__.so` has DT_NEEDED entries for desktop-only SONAMEs
(libXrender, libXfixes, libXi, libXt, libSM, libICE, libxkbcommon, libGL,
libcuda, libze_loader, libextern_draco).  Headless geometry authoring and FBX
export never call into them, but the loader still has to resolve every
referenced symbol (some references are non-lazy), including *versioned*
references such as `xkb_state_update_mask@V_0.5.0`.

What this script does
---------------------
1. walks the DT_NEEDED graph of the wheel and lists SONAMEs that cannot be
   resolved on this machine;
2. collects every UNDEF symbol of the wheel's libraries, attributing each one
   to the library (and symbol version) that `.gnu.version_r` says provides it;
3. skips symbols already provided by a resolvable library or by the Python
   interpreter itself (stubbing those breaks ld.so version matching);
4. emits one version script + assembly stub per missing SONAME, compiles it and
   installs the result into a directory that can be put on LD_LIBRARY_PATH.

Usage
-----
    python3 Tools/env/make_bpy_stubs.py [--out ~/.local/lib/bpystubs]
    export LD_LIBRARY_PATH=~/.local/lib/bpystubs:$LD_LIBRARY_PATH
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

from elftools.elf.elffile import ELFFile
from elftools.elf.dynamic import DynamicSection
from elftools.elf.sections import SymbolTableSection

SYSTEM_DIRS = [
    "/lib/x86_64-linux-gnu",
    "/usr/lib/x86_64-linux-gnu",
    "/lib",
    "/usr/lib",
    "/usr/local/lib",
]

SKIP_SYMBOLS = {
    "__gmon_start__",
    "_ITM_deregisterTMCloneTable",
    "_ITM_registerTMCloneTable",
}

VALID_SYM = re.compile(r"^[_A-Za-z][A-Za-z0-9_]*$")

# Heuristic attribution for *unversioned* references.  Any placement works at
# runtime (unversioned lookups have no file constraint); this only keeps the
# generated stubs readable.
PREFIX_RULES = [
    ("libxkbcommon.so.0", ("xkb_", "xkbcommon_")),
    ("libXfixes.so.3", ("XFixes",)),
    ("libXrender.so.1", ("XRender",)),
    ("libXt.so.6", ("Xt",)),
    ("libXi.so.6", ("XOpenDevice", "XCloseDevice", "XListInputDevices",
                    "XFreeDeviceList", "XFreeDeviceState", "XQueryDeviceState",
                    "XSelectExtensionEvent", "XGetExtensionVersion", "XI")),
    ("libGL.so.1", ("gl", "egl", "glX", "GL")),
    ("libcuda.so.1", ("cu", "cuda")),
    ("libze_loader.so.1", ("ze", "zel")),
    ("libextern_draco.so", ("draco",)),
    ("libSM.so.6", ("Sm",)),
    ("libICE.so.6", ("Ice",)),
]


def read_dynamic(path: Path):
    needed, rpaths = [], []
    try:
        with open(path, "rb") as fh:
            elf = ELFFile(fh)
            for section in elf.iter_sections():
                if not isinstance(section, DynamicSection):
                    continue
                for tag in section.iter_tags():
                    if tag.entry.d_tag == "DT_NEEDED":
                        needed.append(tag.needed)
                    elif tag.entry.d_tag in ("DT_RPATH", "DT_RUNPATH"):
                        rpaths += [p for p in tag.rpath.split(":") if p]
    except Exception:
        pass
    return needed, rpaths


def verneed_map(path: Path) -> dict[int, tuple[str, str]]:
    """versym index -> (providing file, version name)."""
    out: dict[int, tuple[str, str]] = {}
    try:
        with open(path, "rb") as fh:
            elf = ELFFile(fh)
            for section in elf.iter_sections():
                if section.header.get("sh_type") != "SHT_GNU_versym":
                    continue
            for section in elf.iter_sections():
                if section.header.get("sh_type") != "SHT_GNU_verneed":
                    continue
                for need, auxes in section.iter_versions():
                    fname = need.name if hasattr(need, "name") else need["vn_file"]
                    for aux in auxes:
                        vname = aux.name if hasattr(aux, "name") else aux["vna_name"]
                        index = aux["vna_other"] if hasattr(aux, "__getitem__") else None
                        if index is None:
                            index = getattr(aux, "vna_other", None)
                        if index is not None:
                            out[int(index)] = (str(fname), str(vname))
    except Exception:
        pass
    return out


def scan_symbols(path: Path):
    """-> (undef: {base: versym_index}, defined: set[base])."""
    undef: dict[str, int] = {}
    defined: set[str] = set()
    try:
        with open(path, "rb") as fh:
            elf = ELFFile(fh)
            versym: list[int] = []
            for section in elf.iter_sections():
                if section.header.get("sh_type") == "SHT_GNU_versym":
                    data = section.data()
                    versym = [int.from_bytes(data[i:i + 2], "little")
                              for i in range(0, len(data) - 1, 2)]
                    break
            for section in elf.iter_sections():
                if not isinstance(section, SymbolTableSection):
                    continue
                if section.header.get("sh_entsize", 0) == 0:
                    continue
                is_dyn = section.header.get("sh_type") == "SHT_DYNSYM"
                for idx, sym in enumerate(section.iter_symbols()):
                    name = sym.name
                    if not name:
                        continue
                    base = name.split("@")[0]
                    if sym["st_shndx"] == "SHN_UNDEF":
                        if is_dyn and sym["st_info"]["bind"] == "STB_GLOBAL":
                            vi = versym[idx] if (is_dyn and idx < len(versym)) else 1
                            if base not in undef or vi > undef[base]:
                                undef[base] = vi
                    else:
                        defined.add(base)
    except Exception:
        pass
    return undef, defined


def find_lib(soname: str, extra_dirs) -> Path | None:
    for d in list(dict.fromkeys(extra_dirs)) + SYSTEM_DIRS:
        cand = Path(d) / soname
        if cand.exists():
            return cand
    return None


def interpreter_symbols() -> set[str]:
    """Symbols exported by the Python interpreter binary (they win over libs)."""
    out: set[str] = set()
    candidates = [Path(sys.executable)]
    for libdir in (Path(sys.base_prefix) / "lib", Path("/usr/lib/x86_64-linux-gnu")):
        if libdir.exists():
            candidates += sorted(libdir.glob("libpython3*.so*"))
    for c in candidates:
        if not c.exists():
            continue
        try:
            res = subprocess.run(["nm", "-D", "--defined-only", str(c)],
                                 capture_output=True, text=True, timeout=180)
        except Exception:
            continue
        for line in res.stdout.splitlines():
            parts = line.split()
            if len(parts) == 3:
                out.add(parts[2].split("@")[0])
    return out


def attribute(name: str, missing: set[str]) -> str:
    for lib, prefixes in PREFIX_RULES:
        if lib in missing and any(name.startswith(p) for p in prefixes):
            return lib
    return sorted(missing)[0]


def build(bpy_dir: Path, out_dir: Path) -> int:
    bpy_libs = sorted(p for p in bpy_dir.rglob("*.so*")
                      if p.is_file() and not p.is_symlink())
    if not bpy_libs:
        print(f"no libraries found under {bpy_dir}", file=sys.stderr)
        return 2
    print(f"scanning {len(bpy_libs)} bpy libraries in {bpy_dir}")

    extra_dirs = [str(bpy_dir / "lib"), str(bpy_dir)]
    seen: set[str] = set()
    queue = [p.name for p in bpy_libs]
    resolvable: list[Path] = list(bpy_libs)
    missing: set[str] = set()
    while queue:
        soname = queue.pop()
        if soname in seen:
            continue
        seen.add(soname)
        path = find_lib(soname, extra_dirs)
        if path is None:
            missing.add(soname)
            continue
        if path not in resolvable:
            resolvable.append(path)
        needed, rpaths = read_dynamic(path)
        extra_dirs += [r.replace("$ORIGIN", str(path.parent)) for r in rpaths]
        queue += needed
    missing = {m for m in missing if m.startswith("lib")}
    print("missing SONAMEs:", ", ".join(sorted(missing)) or "(none)")
    if not missing:
        return 0

    # ---- symbol delta ---------------------------------------------------
    # versym indices are per-object, so attribution must happen per library.
    undef: dict[str, int] = {}
    # name -> (owner_lib_or_None, version_or_None)
    attribution: dict[str, tuple[str | None, str | None]] = {}
    for lib_path in bpy_libs:
        u, _ = scan_symbols(lib_path)
        vmap = verneed_map(lib_path)
        for name, vi in u.items():
            owner = version = None
            if vi > 1 and vi in vmap:
                owner, version = vmap[vi]
            prev = attribution.get(name)
            if prev is None or (owner is not None and prev[0] is None):
                attribution[name] = (owner, version)
            if name not in undef or vi > undef[name]:
                undef[name] = vi

    defined: set[str] = set()
    for i, p in enumerate(resolvable, 1):
        _, d = scan_symbols(p)
        defined |= d
        if i % 40 == 0:
            print(f"  scanned {i}/{len(resolvable)} resolvable libs ...", flush=True)

    py_syms = interpreter_symbols()

    # (lib, version) -> symbols ; (lib, None) -> unversioned symbols
    buckets: dict[tuple[str, str | None], set[str]] = defaultdict(set)
    for name, vi in undef.items():
        if name in defined or name in py_syms or name in SKIP_SYMBOLS:
            continue
        if not VALID_SYM.match(name):
            continue
        owner, version = attribution.get(name, (None, None))
        if owner is None or owner not in missing:
            owner = attribute(name, missing)
            version = None
        buckets[(owner, version)].add(name)

    print(f"undef={len(undef)} defined={len(defined)} python={len(py_syms)} "
          f"-> stubbing {sum(len(v) for v in buckets.values())} symbols "
          f"in {len({k[0] for k in buckets})} libraries")

    # ---- emit + compile -------------------------------------------------
    out_dir.mkdir(parents=True, exist_ok=True)
    per_lib: dict[str, dict[str | None, set[str]]] = defaultdict(dict)
    for (lib, version), names in buckets.items():
        per_lib[lib][version] = names

    for lib in sorted(missing):
        versions = per_lib.get(lib, {})
        plain = sorted(versions.get(None, set()))
        versioned = sorted(n for ver, names in versions.items() if ver for n in names)
        stub_c = out_dir / f"{lib}.stub.c"
        lines = ["/* Auto-generated by Tools/env/make_bpy_stubs.py - do not edit. */"]
        for name in plain + versioned:
            lines += [f"__attribute__((visibility(\"default\"))) long {name}(void) {{ return 0; }}"]
        stub_c.write_text("\n".join(lines) + "\n")

        vscript = None
        if any(v for v in versions):
            vscript = out_dir / f"{lib}.ver"
            vl = []
            for version, names in sorted((k, v) for k, v in versions.items() if k):
                vl.append(f"{version} {{")
                vl.append("    global:")
                for n in sorted(names):
                    vl.append(f"        {n};")
                vl.append("};")
            vscript.write_text("\n".join(vl) + "\n")

        so = out_dir / lib
        cmd = ["gcc", "-shared", "-fPIC", "-Wl,--allow-shlib-undefined",
               "-o", str(so), str(stub_c)]
        if vscript:
            cmd += [f"-Wl,--version-script={vscript}"]
        res = subprocess.run(cmd, capture_output=True, text=True)
        if res.returncode != 0:
            print(f"gcc failed for {lib}:\n{res.stderr[:1500]}", file=sys.stderr)
            return 1
        total = sum(len(v) for v in versions.values())
        print(f"  built {lib}  ({total} symbols, "
              f"{len([v for v in versions if v])} versioned groups)")

    print(f"\nstubs ready in {out_dir}")
    print(f"activate with:  export LD_LIBRARY_PATH={out_dir}:$LD_LIBRARY_PATH")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--bpy", default=None, help="path to the bpy package dir")
    ap.add_argument("--out", default=os.path.expanduser("~/.local/lib/bpystubs"))
    args = ap.parse_args()

    import site

    bpy_dir = Path(args.bpy) if args.bpy else None
    if bpy_dir is None:
        candidates = [site.getusersitepackages()]
        try:
            candidates += list(site.getsitepackages())
        except AttributeError:
            pass
        for sp in candidates:
            cand = Path(sp) / "bpy"
            if cand.is_dir():
                bpy_dir = cand
                break
    if bpy_dir is None:
        print("bpy package directory not found (pip install bpy first)", file=sys.stderr)
        return 2
    return build(bpy_dir, Path(args.out))


if __name__ == "__main__":
    sys.exit(main())
