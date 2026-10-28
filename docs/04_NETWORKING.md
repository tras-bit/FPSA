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
