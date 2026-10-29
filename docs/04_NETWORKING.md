# KORDON — networking

## Transport

**Unity Netcode for GameObjects 2.2.0** (pinned in `Packages/manifest.json`,
verified compatible with Unity 6000.0; the official package index lists 2.2.0 with
`unity: 6000.0`, depending on transport 2.4.0). The Unity transport (UTP) is used
over UDP.

`Kordon.Net.KordonNetwork` builds the `NetworkManager` and the transport in code
and configures it from `StreamingAssets/Data/Config/net_config.json`:

```jsonc
{
  "serverPort": 27015,          // game traffic
  "queryPort": 27016,           // status/ping endpoint
  "maxPlayers": 20,
  "serverTickRate": 30,
  "clientTimeoutSeconds": 20,
  "reconnectGraceSeconds": 90,
  "maxReconnectAttempts": 5,
  "reconnectRetryDelaySeconds": 2.5,
  "defaultRegionId": "eu-central",
  "allowLoopbackForDevelopment": false,
  "regions": [
    { "id": "eu-central", "displayName": "EU Central (Frankfurt)", "address": "" }
  ]
}
```

**EU first**: `eu-central` is the default region and the first entry; fill in its
`address` to advertise a server, or leave it empty and let players type an
address. No address exists in client code — CI fails the build if a loopback
literal appears outside the Core address policy that refuses it.

## Authority model

| Thing | Owner |
|---|---|
| hits, damage, armour, penetration | server (`NetworkMatchDirector.ShotServerRpc` re-raycasts against server bodies) |
| ammunition, reload, inspect | server (`LoadoutKit` per player); the exact count leaves only on a personal inspect |
| respawn timing, spawn protection | server (`FfaMatchController`) |
| score, kills, match end | server (`FfaMatchController` + `ServerValidators`) |
| movement | client predicts, server validates (`ServerValidators.ValidateMovement`, teleport and speed checks) and replicates the accepted position |
| co-op mission, hostiles | server (`CoopMissionDirector` + Core `MissionController`) |

Clients send ~20 Hz frames (position, yaw, pitch, sprint, crouch) and one shot
claim per trigger pull. Everything is re-derived on the server; a rejected frame
leaves the server's accepted position in place, so a teleporting client is dragged
back on its next snapshot.

## Anti-cheat

`Kordon.Core.AntiCheat.ServerValidators` + `ViolationLog`:

* impossible fire rate (`ValidateFireRate`),
* impossible movement/teleport (`ValidateMovement`),
* impossible view angles (`ValidateViewAngles`),
* score spoofing (`ValidateScoreSubmission` — a client score is always refused),
* illegal state transitions (`ValidateStateTransition`),
* severity accumulation with a kick threshold (`ViolationLog.ShouldKick`).

## Connection lifecycle

* **Host**: `StartHost` (listen server) or `StartServer` (dedicated, headless).
* **Client**: address from the form or the region config, validated by
  `NetConfig.ValidateClientRequest` (unknown region, undeployed region, loopback,
  bad port are all refused with a readable reason).
* **Ping**: sampled once per second from the transport RTT and shown on the HUD.
* **Reconnect**: an unexpected client disconnect starts a retry loop
  (`MaxReconnectAttempts`, `ReconnectRetryDelaySeconds`); the server holds the slot
  for `ReconnectGraceSeconds` and `FfaMatchController.TryReconnect` restores score
  and slot on a valid session token.
* **Leave**: `LeaveServerRpc` + `TryLeave` frees the slot immediately; a silent
  disconnect keeps it for the grace window.

## Running a server

```
Kordon.exe -kordon-server -serverAddress 0.0.0.0 -serverPort 27015
```

The Linux build (`Kordon → Build → Linux 64-bit player`) is the same binary for
headless hosting. The server needs no account, no key and no paid infrastructure;
it listens on the configured port and accepts up to `maxPlayers` clients.

## Voice

Voice is an integration point, not a service: `Kordon.Core.Voice.IVoiceTransport`
plus `VoiceProximityModel` (proximity falloff to 28 m, occluded gain, squad radio
at full gain) and `Game.Audio.VoiceRuntime` driving the keys and the HUD. The
shipped transport is `UnconfiguredVoiceTransport`, which says so in the HUD
("VOICE not configured"). Connecting a real provider is one `Install(...)` call
and requires no change to the game; KORDON never asks for an account or a key.

## Thrown equipment on the wire

Grenades are the only physical projectiles, so they get their own small protocol:

* `GrenadeThrowServerRpc(origin, direction, fuseSeconds)` — the client only says
  what it did. The server checks the origin against its own copy of the operator
  (2.5 m), the direction, the 0.45 s throw interval, that the kit still holds a
  grenade and that the match is live; then it clamps the fuze to
  `[CookableMinFuse, FuseSeconds]`, decrements the sealed kit itself and owns the
  flight from that moment.
* The server steps the same Core `GrenadeTrajectory` in fixed 60 Hz steps, sweeps
  the map for collisions and resolves the blast with `BlastModel` against its own
  bodies. Clients never apply grenade damage.
* `GrenadeSpawnedClientRpc(id, position, velocity, fuse, grenadeId)` lets every
  client run a visual copy of the flight; the copy never decides anything.
* `GrenadeExplodedClientRpc(id, position, grenadeId)` retires that copy and plays
  the blast, the smoke cloud and the flash at the position the server decided.
  Because both messages carry the same id, the visual grenade and the authoritative
  explosion can never disagree about which throw they belong to.
* Kills go through the match controller exactly like gunfire
  (`RegisterKill`); your own grenade is an uncredited death, so it kills you,
  counts as a death and never scores a kill.
* Smoke adds a cloud to the shared `SmokeField` on the server, which is what the
  militia AI uses to decide whether it can see an operator.

## Dedicated server

`Kordon.Net.DedicatedServerBootstrap` (`[RuntimeInitializeOnLoadMethod]`) parses the
command line with `Kordon.Core.Config.ServerLaunchOptions` before anything else.
`-kordon-server` starts `DedicatedServerHost`, which:

1. loads `StreamingAssets/Data`, builds the map from the authored layout JSON - so
   the server has real colliders for hitscan, grenade bounces and nav,
2. starts the UTP transport on `-serverAddress` / `-serverPort`,
3. spawns the `NetworkMatchDirector` and configures it with the database and map,
4. logs a heartbeat and the match result, and never creates UI or a local operator
   (`GameBootstrap` returns early when the flag is present).

The flag parser is engine free and unit tested (`ServerLaunchTests`): unknown flags
are reported, an invalid port is refused instead of silently replaced, and the
default bind address is `0.0.0.0`, never loopback.

