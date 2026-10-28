# Blender in a headless container

The desktop Blender installer (`download.blender.org`) and the Debian archives
(`deb.debian.org`) are unreachable from the sandbox this project was authored
in, so the standalone editor could not be installed.  Instead the project uses
the **official Blender 4.5 LTS binaries shipped as the `bpy` Python module**
(PyPI package `bpy`, built by the Blender Foundation).  This is the same
Blender — same kernel, same operators, same `.blend` format, same FBX
exporter — just driven from Python instead of a window.

`bpy` still links against desktop-only shared objects (`libXrender`,
`libXfixes`, `libXi`, `libXt`, `libSM`, `libICE`, `libxkbcommon`, `libGL`,
`libcuda`, `libze_loader`, `libextern_draco`).  None of them are ever called
for headless geometry authoring or FBX export, but the loader must resolve
every reference (including *versioned* ones such as
`xkb_state_update_mask@V_0.5.0`).

`make_bpy_stubs.py` generates exactly those SONAMEs:

1. walks the `DT_NEEDED` graph of the wheel and lists unresolvable SONAMEs;
2. attributes every `UNDEF` symbol to the library + symbol version named by
   `.gnu.version_r`;
3. skips symbols already provided by a resolvable library or by the Python
   interpreter (stubbing those corrupts ld.so version matching);
4. compiles one stub `.so` per missing SONAME, with a linker version script so
   versioned references resolve.

```bash
python3 Tools/env/make_bpy_stubs.py --out ~/.local/lib/bpystubs
export LD_LIBRARY_PATH=~/.local/lib/bpystubs:$LD_LIBRARY_PATH
python3 -c "import bpy; print(bpy.app.version_string)"   # -> 4.5.15 LTS
```

`Tools/blender.sh` does all of the above automatically and then runs any
project script:

```bash
Tools/blender.sh Tools/blender/build_kit.py
```

On a normal desktop machine none of this is required — install Blender 4.5 LTS
from blender.org and run the same scripts with
`blender --background --python Tools/blender/build_kit.py`; every project
script detects both entry points.
