# Map audit: ffa_zavod7

Scene: `ArtSource/Blender/ffa_zavod7.blend`  
Layout: `Assets/StreamingAssets/Data/Maps/ffa_zavod7.json`  
Spawns: 20  Kit instances: 716  Shells: 2

**Verdict: PASS** (0 finding(s))

## Floor coverage

Ray cast a 2 m grid (1496 samples) downward from z=6; every sample must land on the `ground` shell.

* samples that hit nothing (holes): **0**
* samples stopped by something other than the ground shell: 309 (catwalk, roof, prop: legitimate cover above the floor)

## Spawns

All 20 spawns stand on the ground shell with 1.85 m of clear headroom.

## Shell contract

One walkable ground shell; every declared shell exists; the ground covers the playable bounds. `MapBuilder` gives it a collider because it is walkable.

