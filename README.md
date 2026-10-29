# KORDON

A tactical first person shooter set in the fictional Veransk corridor, built in
Unity 6 for Windows. Free-for-All is the release mode: 20 operators, no teams,
first one to 50 personal kills wins. A two-to-four player co-op operation,
*Silent Kiln*, ships alongside it.

Everything in this repository is original: the setting, the map, the art, the
audio (synthesised from noise in `Tools/audio`), the UI (built in code) and the
networking layer glue. No asset, map, sound or piece of code was copied from any
existing game.

**Release 1.0.7.** This repository is the release: open it in Unity 6 and press
Play (three steps, below), or build the player from the `Kordon` menu. The release
does **not** contain a prebuilt `.exe` - no Windows machine and no Unity licence
were available in the build environment, so the binary could not be produced here.
See [Known limitations](#known-limitations) and `docs/06_VERIFICATION.md`.

---

## Requirements

| | |
|---|---|
| Unity | **6000.0.32f1** (Unity 6 LTS), installed through Unity Hub |
| Packages | resolved automatically from `Packages/manifest.json`: Netcode for GameObjects **2.2.0**, Test Framework 1.4.5, uGUI 2.0.0 |
| Platform | Windows 10/11 x64 (the project also builds a Linux headless server) |
| Optional | Blender 4.5 LTS, only if you want to re-export the art from `ArtSource/Blender` |

No paid service, account, key or asset store purchase is required to build, run
or play the game.

## Open and run in the editor

1. Unity Hub → *Add project from disk* → this folder → open with 6000.0.32f1.
   The first import resolves the packages and imports the FBX/texture assets
   (a few minutes, once).
2. Menu **Kordon → Open boot scene (press Play)**. This creates the empty boot
   scene the game boots from (`Assets/_Project/Scenes/Boot.unity`).
3. Press **Play**. The game builds its content, UI and world from code at start
   (`GameBootstrap`), so there is nothing to wire in the scene view.

Optional, to bake a map into a scene asset instead of building it at runtime:
**Kordon → Bake map → ffa_zavod7 (Zavod-7)**.

## If the first open reports errors

Two things can go wrong on a fresh clone, both recoverable in a minute:

| What you see | Why | Fix |
|---|---|---|
| `Don't know how to serialize System.String[]` / `PostProcessing failed: Unity.ILPP.Runner.ILPostProcessorDiagnosticsException` | Netcode's ILPP code generator rejects array-of-string RPC parameters; fixed in 1.0.4 (names travel as one wire string). Your clone predates it | update to 1.0.4 or newer |
| `CS0012: The type 'NetworkEndpoint' is defined in an assembly that is not referenced ... 'Unity.Networking.Transport'` | `UnityTransport` needs the transport assembly; fixed in 1.0.3 (asmdef reference + `com.unity.transport` in the manifest). Your clone predates it | update to 1.0.3 or newer |
| `CS1069: The type name 'Rigidbody2D' could not be found ... forwarded to assembly 'UnityEngine.Physics2DModule'` (inside `com.unity.netcode.gameobjects`) | the built-in **Physics 2D** package was not enabled, and Netcode for GameObjects needs it for its 2D rigidbody components | already fixed in `Packages/manifest.json` since 1.0.1 (`com.unity.modules.physics2d`). If you edit the manifest, keep it - `Tools/verify/policy_check.py` fails without it |
| `Assembly 'Library/ScriptAssemblies/Unity.Collections.CodeGen.dll' will not be loaded ... Unable to resolve reference 'Unity.Collections'` | a half-written `Library/` left behind by the failed package resolve | quit Unity, delete `Library/ScriptAssemblies` (or the whole `Library` folder), reopen - packages resolve again and it compiles |

If the editor offers to upgrade the project to a newer Unity version, decline:
this release is pinned to **6000.0.32f1**.

## Build a player

**Kordon → Build → Windows 64-bit player** produces `Builds/Windows/Kordon.exe`.
The build script creates the boot scene, sets the player settings and builds.
A Linux build (**Kordon → Build → Linux 64-bit player**) is the dedicated server
binary.

## Running a match

### Free-for-All (the release mode)

1. One machine hosts: main menu → **PLAY - FREE-FOR-ALL** → **HOST**.
   The host binds the address from `StreamingAssets/Data/Config/net_config.json`
   (or `0.0.0.0` when the configured EU region address is empty).
2. Everybody else: main menu → **PLAY - FREE-FOR-ALL** → type the host's address
   (or DNS name) and port → **JOIN**.
3. Each player picks a kit on the prep screen. The kit **locks** when the round
   starts; magazines cannot be changed afterwards.
4. First operator to 50 personal kills wins. The results screen shows the
   authoritative standings, then offers a rematch.

### Dedicated server

The same binary can run as a server with no local player and no menu:

```
Kordon.exe -kordon-server -serverAddress 0.0.0.0 -serverPort 27015 -map ffa_zavod7
```

* `Kordon.Net.DedicatedServerBootstrap` sees `-kordon-server` before anything else
  and starts `DedicatedServerHost`; `GameBootstrap` deliberately skips the menu
  flow in that process, so a server never builds UI or a local operator.
* The server builds the map itself (colliders included, which is what hitscan and
  grenade bounces are resolved against), starts the transport on the given
  interface and port, spawns the authoritative `NetworkMatchDirector` and logs a
  heartbeat every 15 seconds (phase, operators, target, uptime).
* Flags: `-serverAddress` (default `0.0.0.0`), `-serverPort` (default from
  `net_config.json`, 27015), `-map` (default `ffa_zavod7`), `-maxplayers`.
  Anything it does not recognise is printed, never silently ignored.
* Clients join it exactly like a player host: **JOIN** with the server's address
  and port.

### Co-op: Operation Silent Kiln

Main menu → **CO-OP OPERATION (2-4)** → **HOST**, then 1–3 more players join the
same way. Every operator reads the briefing and presses **F** to acknowledge;
infiltration begins when all present operators are ready. The chain is: reach the
rail spur → breach the yard gate → sabotage two fuel lines → recover intel →
hold the kiln hall for 45 s → reach the extraction pad → hold it until the
transport boards and leaves.

### Basic training

Main menu → **BASIC TRAINING** walks the 13 steps of the tutorial script with your
own current bindings, and can drop you straight into practice. The prep screen
also offers three ready-made kits (assault, breacher, marksman) and a control
profile switch; every weapon and plate in them is available from the first match.

### Practice

**PRACTICE (OFFLINE, NO BOTS)** runs the same Free-for-All rules locally against
no opponents: it exists to learn the map, the recoil and the magazine system.
Practice is labelled as practice everywhere and never stands in for networked
play.

## Controls

| Action | Default key |
|---|---|
| Move | W A S D |
| Sprint (stamina-limited) | Left Shift |
| Crouch / prone | Left Ctrl / Z |
| Lean left / right | Q / E |
| Jump | Space |
| Fire / aim | Left mouse / right mouse |
| **Tap R** | reload |
| **Hold R** | extract and inspect the magazine (exact round count, cannot fire) |
| Weapon slots | 1 primary, 2 sidearm, 3 grenade |
| Throw a grenade | left mouse while slot 3 is out — **hold to cook a frag**, release to throw |
| Use / interact / acknowledge briefing | F |
| Scoreboard (hold) | Tab |
| Pause | Esc |
| Push to talk (voice, when a transport is installed) | V |

Every one of these is rebindable in *Settings → Controls*: click the row, press
the new key.

## What the HUD shows, and what it does not

* Bottom centre: the magazine strip. **White = full, yellow = partially used,
  red = empty**, with a tick on the magazine in the weapon.
* Bottom right: the carried grenade and how many are left. While a frag is cooked
  the line turns amber and then red and shows the **burning fuze** in seconds.
* The **only** place an exact round count ever appears is the inspect readout,
  which you get by holding R.
* There is **no crosshair** and **no permanent ammo counter**. Both exist nowhere
  in the code, and CI checks that they stay that way.
* The bodycam feed (inertia, sway, grain, chromatic aberration, vignette, exposure
  flutter) is **always on** and has no toggle, by design.

## Thrown equipment

Grenades are the only physical projectiles in the game: an arc, a bounce and a
timed fuze cannot be a hitscan ray.

* **Select** slot 3. The weapon goes down and your hand comes up.
* **Hold** the fire button to cook a fragmentation grenade (the fuze burns down
  to the grenade's own safe minimum and stops there — you cannot hold it until it
  goes off in your hand), then **release** to throw. Smoke and flashbang leave the
  hand immediately.
* The flight is a deterministic arc integrated at a fixed 60 Hz step
  (`GrenadeTrajectory`), it bounces off the map with real energy loss and it keeps
  burning on the ground.
* The blast is resolved by `BlastModel`, the same rules offline and on the server:
  quadratic fragmentation falloff from the lethal radius to the damage radius,
  hard cover saves you completely (you still get suppressed), plates soften
  fragments by up to 35 %, and a flashbang only blinds what it can see.
* In an online match the client sends only the throw intent (origin, direction,
  fuze left). The server checks the origin against its own copy of the operator,
  owns the flight, resolves the blast, and credits kills through the match
  controller. Your own grenade can kill you and never scores.
* Respawning reissues exactly the grenades the sealed kit was built with; there is
  no mid-round resupply and no ground pickup.

## Network settings

Everything network related lives in
`Assets/StreamingAssets/Data/Config/net_config.json`:

```jsonc
{
  "serverPort": 27015,
  "queryPort": 27016,
  "maxPlayers": 20,
  "serverTickRate": 30,
  "reconnectGraceSeconds": 90,
  "maxReconnectAttempts": 5,
  "reconnectRetryDelaySeconds": 2.5,
  "defaultRegionId": "eu-central",
  "allowLoopbackForDevelopment": false,
  "regions": [ { "id": "eu-central", "displayName": "EU Central (Frankfurt)", "address": "" } ]
}
```

* Set a region's `address` to your server's public hostname to advertise it; the
  connect screen fills that in. Leave it empty and players type an address.
* `allowLoopbackForDevelopment` is `false` in the shipped file: a release client
  refuses to connect to `localhost` at all. Turn it on only for local testing,
  and never ship a build with it on.
* No address is hardcoded anywhere in client code - CI fails the build if one
  appears.

## Repository layout

```
Assets/_Project/
  Art/            models (FBX), textures, materials, shaders, generated audio;
                  Resources/ holds everything loaded by path (build safe)
  Scenes/         boot scene (created by the build script)
  Scripts/Core/   engine free deterministic rules: match, ammo, damage, movement,
                  nav, co-op mission, anti-cheat, content loading  (Kordon.Core)
  Scripts/Game/   Unity side: map builder, player, bodycam, weapons, UI, audio, AI
  Scripts/Net/    Netcode for GameObjects: transport, authoritative directors,
                  client sessions                                        (Kordon.Net)
  Scripts/Editor/ map baker, build script
Assets/StreamingAssets/Data/
  Definitions/    weapons, magazines, bullets, armour, grenades, missions, subtitles
  Maps/           authored layout for both maps (Blender written, single source of truth)
  Nav/            nav graph per map
  Art/            generated kit, weapon and grenade manifests
ArtSource/Blender/  Blender sources: modular kit, both maps, the weapon set
Tools/            Blender and texture/audio generators, verification scripts
Tests/            Core unit tests, syntax check, Unity side compile check
docs/             design and network documents, verification reports, art previews
```

The map, its colliders, its spawns and its materials are built at runtime from
`StreamingAssets/Data/Maps/*.json` plus the kit manifest, using the same code
path as the editor bake tool - a baked scene and a runtime map cannot drift.

## Tests and verification

* `Tests/Kordon.Core.Tests` - 225 tests over the rules that matter: the 50 kill
  match end, magazine handling (tap vs hold, colour states, no exact ammo in
  snapshots), damage and penetration, grenade flight/blast/smoke, movement and
  stamina, anti-cheat validators, mission chain, net config and content integrity.
* `Tests/Kordon.SyntaxCheck` - forbidden construct check for the Core assembly.
* `Tests/Kordon.Game.CompileCheck` - compiles every Unity side script against
  hand written UnityEngine/UnityEditor/NGO stubs with real Unity 6000.0
  signatures. Unity cannot run in CI, so this is how the Unity code is type
  checked on every push.
* `Tools/verify/policy_check.py` - structural design rules (no crosshair, ammo
  privacy, no loopback literal, no bots in networked FFA, no TODOs, content ids
  resolve).

Reports land in `docs/reports/`. See `docs/06_VERIFICATION.md` for the current
state of every check.

## Known limitations

See `docs/06_VERIFICATION.md`. The short version: the project was developed in a
Linux container with no Unity Editor and no Windows machine, so the player binary
was **not** produced here; everything that can be compiled, unit tested and
statically verified was, and the exact reason the build could not be run is
documented there rather than hidden.
