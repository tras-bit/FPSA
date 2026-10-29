# Map audit: coop_silent_kiln

Scene: `ArtSource/Blender/coop_silent_kiln.blend`  
Layout: `Assets/StreamingAssets/Data/Maps/coop_silent_kiln.json`  
Spawns: 6  Kit instances: 480  Shells: 2

**Verdict: PASS** (0 finding(s))

## Floor coverage

Ray cast a 2 m grid (1050 samples) downward from z=6; every sample must land on the `ground` shell.

* samples that hit nothing (holes): **0**
* samples stopped by something other than the ground shell: 271 (catwalk, roof, prop: legitimate cover above the floor)

## Spawns

All 6 spawns stand on the ground shell with 1.85 m of clear headroom.

## Shell contract

One walkable ground shell; every declared shell exists; the ground covers the playable bounds. `MapBuilder` gives it a collider because it is walkable.

## Review images

![coop_silent_kiln](../docs/art/coop_silent_kiln-iso.png)
![coop_silent_kiln](../docs/art/coop_silent_kiln-top.png)

