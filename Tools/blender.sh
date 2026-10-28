#!/usr/bin/env bash
# Headless Blender runner for this project.
#
# The official Blender 4.5 LTS binaries are used through the `bpy` PyPI wheel
# (the real Blender, imported as a Python module).  In a container without an
# X11/OpenGL stack the wheel needs the stub libraries produced by
# Tools/env/make_bpy_stubs.py; see Tools/env/README.md.
#
# Usage:
#   Tools/blender.sh Tools/blender/build_kit.py [args...]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STUBS="${BPY_STUB_DIR:-$HOME/.local/lib/bpystubs}"
if [ ! -f "$STUBS/libXrender.so.1" ]; then
  echo "[blender.sh] building loader stubs in $STUBS ..." >&2
  python3 "$HERE/env/make_bpy_stubs.py" --out "$STUBS" >&2
fi
if ! python3 -c "import bpy" 2>/dev/null; then
  echo "[blender.sh] installing bpy (official Blender 4.5 LTS python module) ..." >&2
  python3 -m pip install --user --break-system-packages -q "bpy==4.5.15" numpy pillow >&2
fi
export LD_LIBRARY_PATH="$STUBS:${LD_LIBRARY_PATH:-}"
export BLENDER_HEADLESS=1
exec python3 "$@"
