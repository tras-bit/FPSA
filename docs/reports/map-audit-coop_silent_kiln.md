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

## Side lanes: spawn to spawn

Eye height 1.62 m, every spawn pair whose distance is at least 8 m (0 pairs) is ray cast along the straight line between them. Neighbours inside one spawn cluster are exempt: five slots in a shelter are 2-4 m apart by design.

None of the 0 pairs has a clear line: 0 are broken by geometry.

## Sight out of each spawn

Every spawn has at least 6 m of clear sight in one of 16 sampled directions. A spawn walled in is as broken as a spawn that can be sniped from the other corner.

| spawn | clear directions | furthest clear |
| --- | --- | --- |
| `insert_1` | 15 / 16 | 92.2 m |
| `insert_2` | 16 / 16 | 92.2 m |
| `insert_3` | 13 / 16 | 92.2 m |
| `insert_4` | 16 / 16 | 92.2 m |
| `insert_5` | 12 / 16 | 92.2 m |
| `insert_6` | 15 / 16 | 92.2 m |

## Bomb sites and buy zones

0 bomb site(s) and 0 buy zone(s) are declared by the layout. A site must be on the floor inside the bounds, with a clear plant spot under 1.9 m of headroom and at least 20 m from the other site; every team spawn must sit inside one of its own side's buy zones.

Every site stands on the floor with a clear plant spot and a real distance from the other site, and every team spawn can buy.

## Shell contract

One walkable ground shell; every declared shell exists; the ground covers the playable bounds. `MapBuilder` gives it a collider because it is walkable.

