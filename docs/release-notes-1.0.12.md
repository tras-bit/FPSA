# KORDON 1.0.12 - the map is a rotation, and the UI survives Unity 6

This release exists because of the first run in a real Unity editor: the game died on
the first screen it built, and the assembled level did not match the authored one.

## The crash

`UiBuilder.Font` asked for `Arial.ttf`. Unity 6000 rejects that name with
`ArgumentException: Arial.ttf is no longer a valid built in font`, and because the
property was read while `GameFlow.Awake` was building the boot screens, the exception
took the whole game down before the menu existed.

The font is `LegacyRuntime.ttf` now, resolved once, cached, with an installed OS font
as the fallback and an error in the log instead of a throw.

## The map

Every map is assembled at runtime from layout JSON and kit FBX files. The conversion
from authored space (Blender, Z up) to the engine (Unity, Y up) was written as
`(x, z, y)` with the yaw negated - a mirror, not a rotation.

Positions and yaws were mirrored together, but module geometry comes out of the FBX
unmirrored, so every asymmetric module - stairs, doors, gates, conveyors, gantries -
was turned the wrong way relative to the gap it was built to fill. The result is a
yard of pieces that do not close up: walls facing away from their openings, catwalks
arriving at nothing, a level that reads as collapsed.

The conversion is `(x, z, -y)` now, the yaw keeps its sign, and both live in
`Kordon.Core.Maps.MapOrientation` with four tests: up becomes up, front becomes front,
the conversion preserves handedness, and yaw round trips.

## Verification

`core-tests` on this branch: 300 core tests passed, every script parsed with 0 syntax
errors, the Unity side type checked against the API stubs, design rules passed. The
map audit still passes in authored space (`docs/reports/map-audit-ffa_zavod7.md`).

## Limits

This environment has no Unity editor, so the map fix is verified by maths and tests,
not by a screenshot from inside the engine: the convention is derived from the export
settings and from the fact that the weapons and the operator bodies are authored facing
Blender -Y and must face the camera in Unity. If the editor still shows pieces that do
not line up, the next thing to check is the module FBXs themselves.
