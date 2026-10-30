# Map audit: ffa_zavod7

Scene: `ArtSource/Blender/ffa_zavod7.blend`  
Layout: `Assets/StreamingAssets/Data/Maps/ffa_zavod7.json`  
Spawns: 20  Kit instances: 720  Shells: 2

**Verdict: PASS** (0 finding(s))

## Floor coverage

Ray cast a 2 m grid (1496 samples) downward from z=6; every sample must land on the `ground` shell.

* samples that hit nothing (holes): **0**
* samples stopped by something other than the ground shell: 321 (catwalk, roof, prop: legitimate cover above the floor)

## Spawns

All 20 spawns stand on the ground shell with 1.85 m of clear headroom.

## Side lanes: spawn to spawn

Eye height 1.62 m, every spawn pair whose distance is at least 8 m (150 pairs) is ray cast along the straight line between them. Neighbours inside one spawn cluster are exempt: five slots in a shelter are 2-4 m apart by design.

None of the 150 pairs has a clear line: 150 are broken by geometry.

## Sight out of each spawn

Every spawn has at least 6 m of clear sight in one of 16 sampled directions. A spawn walled in is as broken as a spawn that can be sniped from the other corner.

| spawn | clear directions | furthest clear |
| --- | --- | --- |
| `spawn_a1` | 11 / 16 | 111.2 m |
| `spawn_a2` | 12 / 16 | 111.2 m |
| `spawn_a3` | 12 / 16 | 111.2 m |
| `spawn_a4` | 12 / 16 | 111.2 m |
| `spawn_a5` | 13 / 16 | 111.2 m |
| `spawn_b1` | 11 / 16 | 111.2 m |
| `spawn_b2` | 12 / 16 | 111.2 m |
| `spawn_b3` | 12 / 16 | 111.2 m |
| `spawn_b4` | 13 / 16 | 111.2 m |
| `spawn_b5` | 13 / 16 | 111.2 m |
| `spawn_c1` | 8 / 16 | 111.2 m |
| `spawn_c2` | 9 / 16 | 111.2 m |
| `spawn_c3` | 11 / 16 | 111.2 m |
| `spawn_c4` | 10 / 16 | 111.2 m |
| `spawn_c5` | 10 / 16 | 111.2 m |
| `spawn_d1` | 11 / 16 | 111.2 m |
| `spawn_d2` | 12 / 16 | 111.2 m |
| `spawn_d3` | 13 / 16 | 111.2 m |
| `spawn_d4` | 12 / 16 | 111.2 m |
| `spawn_d5` | 12 / 16 | 111.2 m |

## Shell contract

One walkable ground shell; every declared shell exists; the ground covers the playable bounds. `MapBuilder` gives it a collider because it is walkable.

## Review images

![ffa_zavod7](../docs/art/ffa_zavod7-iso.png)
![ffa_zavod7](../docs/art/ffa_zavod7-top.png)
![ffa_zavod7](../docs/art/ffa_zavod7-lanes.png)

