# KORDON — rules

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

## Fairness rules (enforced structurally, see `docs/06_VERIFICATION.md`)

* No crosshair, ever: aim through iron sights or an optic.
* No permanent ammunition counter; exact counts exist only during inspection.
* No paid advantage and no gameplay stats on cosmetics
  (`CosmeticCatalog.ValidateNoGameplayEffects`).
* All kits are available from the first match; unlocks are cosmetic variants.
