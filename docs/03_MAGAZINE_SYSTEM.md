# KORDON — magazine system

The magazine system is the identity of the game. It is implemented in
`Kordon.Core.Ammo` (`AmmoKit`, `MagazineInstance`, `MagazineState`) and rendered
by `HudScreen` and `WeaponViewmodel`.

## Before the round

On the prep screen you choose, per weapon: the **magazine type** (box, drum,
tube, AP/PS load) and the **number of magazines**. That plan is validated by
`LoadoutValidator` (mass, fit, counts) and sealed by `LoadoutKit.Seal()` when the
countdown starts. The hash of the sealed selection is kept so tampering is
visible to the server.

## During the round: two meanings for one key

`R` is a tap-or-hold key, resolved by `AmmoKit.PressReloadKey` /
`ReleaseReloadKey`:

* **Tap R (released before 0.28 s) → reload.** Tactical reload if the magazine
  still has rounds (magazine goes back into the pouch as *partial*), dry reload
  if it was empty (ends with a bolt release). Times come from the weapon
  definition, multiplied by the magazine's `ReloadTimeMultiplier`.
* **Hold R → extract and inspect the magazine.** The magazine leaves the weapon,
  you can read the exact round count and the state of your reserve, and **you
  cannot fire** while inspecting. Releasing R returns the magazine.

Handling mistakes are *visible*: a fumble has a start event, a recovery
animation and a sound, never a silent misfire and never a random lost shot.

## What the HUD shows

* A strip of magazine slots at the bottom of the screen:
  **white = full, yellow = partially used, red = empty**.
* Nothing else. There is no permanent round count and no reserve counter.
* The only exact numbers appear in the **inspect readout**, which is gated in the
  snapshot type by `AmmoHudSnapshot.MayShowExactCount` (`IsInspecting &&
  InspectedRounds.HasValue`).
* CI enforces this structurally (`Tools/verify/policy_check.py`).

## Resupply

Respawn restores the **same** magazine types and counts with full rounds; nothing
new is added and nothing is taken away. `ResupplyRule.NeverWithinRound` forbids
resupply during a life entirely. There is no resupply box, no pickup and no
between-respawn refill of new magazines.

## Server authority

The server owns each player's `LoadoutKit`. A client "hold R" is answered with a
targeted RPC carrying that player's exact count and nothing else
(`NetworkMatchDirector.InspectServerRpc`). The client's local kit is for
prediction and presentation only.
