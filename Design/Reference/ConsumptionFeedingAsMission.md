# Consumption Feeding as a Planned Mission

Status: **implemented 2026-08-07.** Written and built the same day. Retained in `Reference/` because
the reasoning behind the two non-obvious choices — one mission type rather than two, and expansion
sharing the budget without sharing the offensive code path — is not recoverable from the code.
The mechanism itself is summarized in `OnlyWar_TDD.md` §6.2 and `OnlyWar_PRD.md` §4.24.

What shipped, against the plan below:

- `MissionType.Feed` + `FeedMission` (committed battle value), appended to the enum (§1, §3).
- `FactionConsumptionPlanner` (composed by `FactionStrategyController`) owns PRIORITY 5/6:
  `PlanConsumptionExpansionOnPlanet` then `PlanFeedMissionsOnPlanet`, both after patrols and both
  drawing down the same `SpareTroops` (§2).
- `MissionTurnProcessor.ProcessFeedOrders`, dispatched beside squad-less construction from both
  `TurnController.ProcessTurn` and `PlanetForwardSimulator.Simulate` (§3).
- Consumption expansion and feeding dropped from `UpdatePlanet` in favour of hidden-consumer
  fallbacks (§4 resolved, §5 resolved — see those sections).
- References below use the current policy owners where this design discusses code ownership; the
  historical reasoning remains here, while live formulas stay in the implementation and TDD.

One thing the change surfaced rather than caused: `ClearStalePatrolSquads` swept only Patrol squads,
so every NPC recon party that survived its week — landed home by `ExfiltrateMissionStep` — stayed in
`LandedSquads` forever, inflating the region's search difficulty. It is now
`FactionReconPatrolPlanner.ClearStaleTransientSquads` (Patrol **and** Recon), and
`PlanetForwardSimulator` runs the same sweep after its last week, since generation has no following
planning pass to do it.

## Why

Biomass feeding was intended to be an activity that only the portion of a swarm *not* committed to
other operations performs — like any other military tasking. It is not implemented that way. Today
the entire deployed swarm eats every turn, and the same troops are simultaneously counted as
defending, patrolling, and attacking.

The Tyranids *do* run the normal AI planning pass. `TurnOrderPlanner.AppendNpcOrders` sweeps every
non-Imperial faction through `FactionStrategyController.GenerateFactionOrders`, and that pass
allocates force exactly as expected by the facade's per-planet state construction:

```
organizedTroops           = regionFaction.GetDeployedStrength()
requiredDefensiveBattleValue = CalculateRequiredDefensiveBattleValue(...)   // >= 20% floor
spareTroops               = max(0, organizedTroops - requiredDefensiveBattleValue)
```

`spareTroops` is then drawn down by offensives in `FactionOffensiveOrderBuilder`, and
`PlanPatrolMissionsOnPlanet` takes another
`PatrolForceFraction = 0.1` of the remainder.

Two things break the link to feeding:

1. **The allocation is transient.** `SpareTroops` lives on `RegionForceState`
   (`Helpers/Strategy/FactionPlanningModels.cs`), a local built at the top of planning and discarded
   when the method returns. The only field mirrored back onto the `RegionFaction` is
   `AssignedDefensiveBattleValue` (set during the facade's state construction) — which exists
   *precisely because* a downstream consumer was re-deriving force instead of reading the
   commitment. Same class of bug, different consumer.
2. **Feeding recomputed from scratch.** The former planet-turn feeding path used
   `Population × Organization/100`, which is the
   *same quantity* as `GetDeployedStrength()` — not the residual after commitments.

Timing is not the obstacle: planning is Phase 1 of `TurnController.ProcessTurn`
(`Helpers/TurnController.cs:108`) and `UpdatePlanets` is Phase 3 (`:129`), so the turn's assignments
are current and persisted when feeding runs. There is simply no field carrying the leftover.

There was a **third** independent accounting of the same troops: consumption expansion
computed its own `organized = Population ×
Organization/100` and is equally blind to the planner. It runs *before* consumption specifically so
movers are not double-counted — phase ordering standing in for a shared budget.

**Chosen approach (user decision, 2026-08-07):** make feeding a real mission the planner allocates
against, competing with defence and offence on the same budget — rather than the narrower fix of
persisting a spare-troops field for the existing special-case function to read. Rationale: "it makes
the two types of feeding occur like all other military operations, rather than requiring a special
case function."

## Useful property

For a `PopulationIsMilitary` faction, `MilitaryStrength == Population`
(`Models/Planets/RegionFaction.cs:87`), so the swarm's BV pool and its headcount are the same number.
The planner's units and the feeding pass's units already agree — no conversion needed.

## Design

### 1. `MissionType.Feed`

Append to `Models/Missions/MissionType.cs`. **Append only** — the enum persists as an int ordinal
(`PlanetDataAccess.SaveMission`) and the existing `ShowOfForce` comment records that inserting above
it corrupts saves.

One mission type, not two. Keep the yield math exactly as it is: the chunk-by-chunk interleave of
`PredationMarginalYield` (eating prey) vs `ConsumptionMarginalYield` (stripping carrying capacity)
across `BiomassAllocationSteps = 128` is what makes returns diminish correctly *within* a turn as
each pool depletes. Splitting predation and devouring into separately-planned mission types would
discard that for no gain. The allocator decides the prey/land split internally; the planner only
decides how many troops to hand it.

A `FeedMission : Mission` subclass carrying the committed battle value is the likely shape (compare
`ConstructionMission.BuildAmount` in `Models/Missions/Mission.cs:70`).

### 2. Planning

New step in `GeneratePlanetOrders`, running **after** `PlanPatrolMissionsOnPlanet` (the facade's
patrol phase) so feeding receives the true residual: what
survives the defensive reserve, offensives, development, and the patrol screen.

- Gate to `faction.GrowthType == GrowthType.Consumption`.
- Per `RegionForceState`, commit whatever `SpareTroops` remains; skip if `<= 0`.
- No `ForceGenerator.GenerateForce` call. Feeding is squad-less — materialising squads for a
  million-strong swarm would be absurd, and unlike a patrol screen there is nothing for them to do
  tactically.

### 3. Execution

Squad-less, on the `ConstructionMission` precedent
(`MissionTurnProcessor.ProcessConstructionOrders`, `Helpers/Turns/MissionTurnProcessor.cs:360`) —
those orders resolve instantly and create no `MissionContext`. Feed orders are dispatched the same
way from Phase 2 of `TurnController.ProcessTurn` (they have no `AssignedSquads`, so note the
existing Phase 2 filter at `Helpers/TurnController.cs:118` selects squad-less
`ConstructionMission`s — the Feed filter goes alongside it).

The execution body is the existing `ResolveBiomassConsumption` loop with one substitution: `troops`
comes from the mission's committed BV instead of `consumer.Population * (consumer.Organization /
100.0)`. Everything downstream (`ApplyPredationKills`, the carrying-capacity strip,
`RecordScenarioBlighting`, `BiomassFeedEfficiency = 0.5` conversion, the `GameLog.Debug` line) is
unchanged.

Then **delete** the former feeding call from `UpdatePlanet`. This is the "no special-case function" part of the
change: feeding stops being a planet-update side effect and becomes a mission executed in the
mission phase.

### 4. Expansion — RESOLVED: shares the budget, keeps its own path

Consumption expansion is the third accounting. If feeding draws from `SpareTroops` but expansion
still helps itself to the full pool, the double-count is fixed in one place and left in the other.

Expansion is conceptually already an `Advance`: the strategy controller has offensive machinery that
moves force into an adjacent region, and it already carries Consumption-specific reward logic —
`FactionOffensiveEvaluator.CalculateOffensiveReward` adds the target region's
carrying capacity to the reward for a Consumption attacker.

**Resolved: kept, and taught to draw from the shared budget.** Deleting it in favour of the normal
offensive path was the direction the conversation was heading, and it is wrong on inspection.
Expansion's target is chosen by *biomass*, and the richest neighbour is frequently empty ground with
a high carrying capacity and **no enemy `RegionFaction` in it at all** — nothing
`IdentifyPotentialOffensivesOnPlanet` could ever take as a target, since it enumerates enemy region
factions. Routing expansion through the offensive path would have silently deleted exactly the moves
that make the tide spread. The double-count needed a shared *budget*, not a shared *code path*.

So the move now happens in `PlanConsumptionExpansionOnPlanet`, at PRIORITY 5, sized from `SpareTroops`
rather than from the whole deployed strength, and applied directly rather than issued as an order —
the same shape `PlanGarrisonReinforcement` and `PlanFrontReinforcement` already use for relocating
strength between regions. Spreading precedes feeding in the planner because a swarm on the move is
not grazing. The behaviours that survived unchanged:

- Move target is the adjacent region of highest `RegionBiomass` (prey population + carrying
  capacity), and only when strictly richer than home, as implemented by
  `FactionConsumptionPlanner`.
- Movers scale by `RegionDepletion(region)` — home gets emptier as it is stripped — times
  `ConsumptionExpansionShare = 0.5`. The base is now `SpareTroops` instead of `organized`.
- Movers arrive via `EstablishInvaderPresence`. They no longer feed the destination the same turn:
  the budget is committed before they leave, and an advancing force is not eating.

The former phase-ordering comment ("spreading precedes consumption so departing force is not
counted twice at home") is gone, replaced by a note recording
why the ordering no longer does that job. Ordering only ever de-duplicated those two functions
against each other while leaving both blind to everything else the swarm was tasked with; the shared
budget covers all of it.

### 5. Edge case to preserve deliberately — RESOLVED: explicit fallback

Planning only sees `IsPublic` region-factions during the facade's state construction, whereas
feeding today runs for any Consumption faction regardless of visibility. Irrelevant on the promised
world (the opening stamp sets `IsPublic = true`), but a hidden swarm would silently stop eating.

**Resolved with a fallback, not a behaviour change.** `UpdatePlanet` now calls
`ConsumptionTurnProcessor.ResolveHiddenExpansion` / `ResolveHiddenFeeding`, which run the old
whole-strength logic filtered to `!IsPublic`. A consumer nothing planned for genuinely has its whole strength
available, so full strength is the right budget for it — the fallback is correct on its own terms
rather than merely conservative. The unfiltered `ConsumptionTurnProcessor.ResolveExpansion(Planet)` /
`ResolveFeeding(Region)` entry points remain for the direct-arithmetic tests.

## Expected effect

Swarm growth drops by whatever fraction is committed elsewhere — at minimum the 20%
`MinimumDefensiveReserveFraction`, more once patrols and offensives draw. Since growth is
exponential, this compounds hard over the post-landing feeding window and is a far larger lever on
the opening scenario than either scenario tunable.

**This will shift every seeded outcome.** That is fine and expected — rebaseline without ceremony.

## Related work in flight

- **Already applied this session:** `ScenarioProfile.PromisedWorldInfiltratorStrengthFraction` is
  authored as `0.05` in the shipped `ScenarioProfile` rules row. Motivation: the
  seed-1 "invaded but not conquered" invariant was failing. **Not yet verified** — the test had not
  been re-run after the edit.
- **Failing test this is aimed at:**
  `OnlyWar.Tests.Generation.ScenarioBuilderTests.GenerateSector_Seed1ProducesPlayablePromisedWorldInvariants`
  (`OnlyWar.Tests/Generation/ScenarioBuilderTests.cs:79`), failing with
  `expected Imperial population 753958 to exceed the largest invader's 1027513`. Roughly 2 min for
  that test alone; ~8.5 min for the whole `ScenarioBuilderTests` class. The other two tests in the
  class pass, including the determinism test.
- **Caveat on the cult cut:** `RegionBiomass` and the predation pool count *all* non-Consumption
  population, cult included. Shrinking the cult does not reduce what the swarm can eat — the 5%
  moves back to the Imperial column and the swarm eats it either way. It should still help the
  invariant (which compares Imperial population against the largest single invader), but it will not
  slow the swarm.
- **Unrelated, being handled in a separate conversation:** two failing flamer cone tests,
  `BattleSquadPlannerTests.TemplateWeaponBearer_EmitsAreaAttackWithoutAimingOrShooting`
  (`OnlyWar.Tests/Battles/BattleSquadPlannerTests.cs:1651`) and
  `GrenadePlannerTests.FlamerBearerWithABeltGrenade_StillFiresTheConeOnAnEvenTrade`
  (`OnlyWar.Tests/Battles/GrenadePlannerTests.cs:354`), both `Assert.Single() Failure: The collection
  was empty`. Not related to this plan.

## Other levers on the same problem, for reference

If the opening scenario still hands off badly after this change, in rough order of strength:

1. `BiomassAppetitePerTroop = 0.5` (`Helpers/Turns/ConsumptionTurnProcessor.cs`) — the base of the
   growth exponential.
2. `ScenarioProfile.PostLandingTurnsMean = 4.0` — weeks the swarm feeds unopposed before the player
   arrives. Drawn as `max(0, round(mean + z))`, `z ~ N(0,1)`, deterministic per seed
   (`Builders/ScenarioBuilder.cs:81`). Worth checking what `z` seed 1 actually rolled before
   assuming a typical 4 weeks.
3. `ScenarioProfile.InvaderGarrisonStrengthMultiple = 1.0f` — the landing stamp, sized as the planet's
   whole pre-stamp Imperial *garrison* split across 2–3 regions. A linear knob sitting under an
   exponential; its own comment records that on seed 1 the post-landing window multiplied it ~6.8x,
   so halving it buys well under a week of grace.

## Verification

Per `CLAUDE.md`: `dotnet` through the PowerShell tool, one foreground invocation at a time, never
overlapped. Build once with `--nologo -v q`, then `dotnet test --no-build`.

- Fast iteration: `--filter "FullyQualifiedName~OnlyWar.Tests.Battles"` (~1s) — but that lane will
  not exercise this change. The relevant lanes are `.Turns`, `.Domain` (includes
  `FactionStrategyControllerTests`), and `.Generation` (slow: ~13 min).
- Target test: `--filter "FullyQualifiedName~ScenarioBuilderTests"`.
- Do **not** run Godot or drive the Godot runtime — the user verifies that side manually.
