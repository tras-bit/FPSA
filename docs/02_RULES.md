# KORDON — rules

## Demolition (second PvP mode)

Two squads of five, one charge, round based scoring - the objective shooter half of the
game, on the same map as the free-for-all mode. It has its own rules object
(`BombRules`), its own economy (`EconomyRules`) and its own server controller
(`BombMatchController`); the free-for-all controller is not touched by any of it.

* **Sides.** Attackers carry the charge and win a round by detonating it or by
  eliminating the defenders. Defenders win by defusing, by eliminating the attackers
  before the plant, or by running the round clock out. Sides **swap after 12 rounds**;
  the score belongs to the squad, not to the side, so a team never loses its rounds by
  switching ends.
* **Rounds.** First squad to **13** takes the match (MR12, 24 rounds maximum). Freeze
  time is **15 s**, the live round is **115 s**, the armed charge runs for **40 s**.
* **The charge.** Planting holds an interact key for **3.2 s** inside a site. Death
  cancels it and the progress is lost - progress is computed from held time on the
  server, never reported by the client. Once armed, the round is not over until the
  charge is defused or it goes off: killing every attacker after the plant wins nothing.
* **Defusing.** **10 s** without a kit, **5 s** with one. A defuse can still fail if it
  starts too late, which is what makes the retake a decision rather than a formality.
* **Buying.** Money is the mode's progression. Everyone starts a match with **800**,
  the ceiling is **16000**. Buying is legal during freeze time and for the first **20 s**
  of the live round, inside the operator's own **buy zone** (13 m around their shelter).
  Kills pay **300**, a round win pays **3250** (+800 for a detonation), a plant and a
  defuse pay **300** each. The losing side is paid on a ladder - **1400, 1900, 2400,
  2900, 3400** for consecutive losses - so a lost round is recoverable instead of
  terminal.
* **Shop.** A fixed, validated catalogue (`BuyCatalog`): two sidearms, SMG, shotgun, two
  rifles, a marksman rifle and a sniper; armour vest or vest and helmet; a defuse kit for
  defenders only; frag, smoke and flash, at most four grenades. Slots prevent duplicates,
  prices are data, and the catalogue is validated against the weapons the build actually
  ships so it can never sell a weapon that does not exist.
* **Bots.** Practice only, exactly as in the free-for-all rules: `BombRules.AllowBots`
  is false for networked play and the controller refuses a bot join. The offline mode in
  the main menu fills both sides with labelled bots that shoot, plant and defuse through
  the same controller, so the rules themselves are exercised, but they are practice
  opponents and the scoreboard marks every one of them `[bot]`.
* **Playing it here.** Main menu -> `DEMOLITION (OFFLINE, VS BOTS)`. `B` opens the shop
  during buy time, `E` plants/defuses on a site, hold Tab for the two-sided board. The
  shop screen and the bots are single-player only: there is no demolition network session
  in this build.
* **Map.** Zavod-7 carries both layouts. The demolition block is `bombSites`,
  `teamSpawns` and `buyZones` in `Assets/StreamingAssets/Data/Maps/ffa_zavod7.json`;
  site A is beside the kiln in the furnace hall, site B is in the pipe yard, 33 m away.
  `docs/art/ffa_zavod7-sites.png` draws them from the same numbers the game loads.

## Free-for-All (the release mode)

* 20 player slots, every operator for themselves: there are no teams and no team
  score anywhere in the code.
* First player to **personally** reach **50 eliminations** wins. 49 does not end
  anything (`Tests/Kordon.Core.Tests/MatchRulesTests.FortyNineKills_DoesNotWinTheMatch`).
* Phases: **Lobby → Loadout (45 s) → Countdown (8 s) → Live → Finished**.
* The kit is chosen in the loadout phase and **locked** when the countdown starts.
  There is no weapon swap and no new magazine mid-round.
* Respawn: **5 s** after death, **2.5 s** of spawn protection which is cancelled
  the moment the protected player fires.
* No resupply between respawns inside a round; `ResupplyRule.OnRespawnOnly` is the
  shipped rule and the magazine types/counts never change. Rounds come back only
  on respawn (`AmmoKit.ReissueFullMagazines`).
* Disconnect: the slot and the score are held for **90 s**; a reconnect with the
  session token resumes them. After the grace window the slot is free.
* Match end: kill target, time limit (if configured), server abort, or everybody
  leaving. Results come from the server's standings, never from a client claim.

Server authority (see `docs/04_NETWORKING.md`): hits, health, ammunition,
respawn, score and match end are decided on the server. The client sends intents.

## Operation Silent Kiln (co-op)

2–4 operators, AI hostiles, one continuous mission:

1. **Briefing** — every operator must acknowledge (F) before infiltration.
2. **Reach the rail spur** — movement objective.
3. **Breach the yard gate** — sabotage with a charge.
4. **Sabotage both fuel lines** — two charges, either order.
5. **Recover the intel** — carry objective in the control room.
6. **Hold the kiln hall for 45 s** — the timer only runs while an operator is
   inside; it resets nothing but it does not progress when the hall is empty.
7. **Reach the extraction pad**, call the transport, **hold the pad until it
   boards** (75 s inbound, 40 s boarding window).

Fail states: all operators down and bleeding out (45 s bleed-out), a critical
objective failed, or the mission time limit. Extraction is critical: an operation
cannot succeed without evacuating. Hostile budget: 34 total, at most 8 alive at
once. Co-op rules never modify the FFA rule set.

## Practice

`MatchRulesConfig.PracticeRange()` allows bots and is the only mode that does. It
is offline, is labelled "PRACTICE" in the HUD, and bots are named as bots in the
scoreboard. Networked FFA sets `AllowBots = false` and the controller refuses a
bot join with `JoinOutcome.BotsNotAllowed`.

## Thrown equipment

* Three types: `frag_f1` (fragmentation), `smoke_rdg` (smoke), `flash_zarya`
  (flashbang). Every operator can carry up to two, chosen before the match and
  locked with the rest of the kit.
* Flight: fixed-step deterministic integration of gravity, drag, bounce and fuze
  (`Kordon.Core.Combat.GrenadeTrajectory`). The server runs the same code, clamps
  the fuze to `[CookableMinFuse, FuseSeconds]` and rejects an origin further than
  2.5 m from where it believes the operator is, or two throws inside 0.45 s.
* Blast: `Kordon.Core.Combat.BlastModel`. Damage saturates inside the lethal
  radius, falls off quadratically to the damage radius, is blocked entirely by
  cover when the grenade requires line of sight, is scaled by partial cover and
  reduced by at most 35 % by plates. Suppression survives cover at half strength.
* A frag never leaves the operator's name off the kill feed: the server credits
  the thrower through `RegisterKill`; your own grenade is an uncredited death
  (`RegisterUncreditedDeath`) that still starts the normal respawn timer.
* Smoke creates a vision volume for its full duration; flash blinds only inside
  its effect radius with line of sight, for a duration that shrinks with distance.

## Fairness rules (enforced structurally, see `docs/06_VERIFICATION.md`)

* No crosshair, ever: aim through iron sights or an optic.
* No permanent ammunition counter; exact counts exist only during inspection.
* No paid advantage and no gameplay stats on cosmetics
  (`CosmeticCatalog.ValidateNoGameplayEffects`).
* All kits are available from the first match; unlocks are cosmetic variants.
