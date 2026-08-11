# Opening Scenario — “Promised World”

**Status:** Implemented end-to-end (July 2026). The TDD is the architectural source of truth; this
reference retains scenario-specific sequencing, balance anchors, and product decisions.

## Locked player-facing decisions

- The Sector Lord promises the Chapter an invaded Imperial world as its future Chapter World.
- The Chapter begins in orbit with its squads embarked; landing is the first player action.
- Liberating the world grants it to the Chapter. If it is overrun, the promise lapses, the Sector
  Lord’s opinion falls, and the campaign continues as a sandbox.
- The opening is a real campaign state, not a scripted tutorial instance. Ordinary factions,
  missions, population, combat, and turn processing determine the outcome.

## Persistent scenario state

`Sector.Scenario` is nullable so plain-sandbox and legacy sectors remain valid. A `PromisedWorld`
`CampaignScenario` stores:

- promised planet id;
- objective state (`Pending`, `Won`, or `Lapsed`);
- briefing text composed once at generation;
- whether the one-shot briefing was acknowledged; and
- the original promising authority id for narrative continuity.

The mechanically relevant authority is always resolved from the current governance hierarchy. A
governor’s death or succession therefore does not orphan the objective.

## Governance hierarchy

`SectorBuilder.AssignGovernance` deterministically derives governance after sector generation and
load:

- the highest-importance Imperial-controlled world with a governor is the sector capital;
- each subsector similarly receives an Imperial governance seat when one exists;
- `Sector.GetSectorLord` and `GetSubsectorGovernor` resolve the characters occupying those seats.

The designations are recomputed rather than persisted. Governor characters already persist, so the
identity of the current office-holder round-trips without duplicating hierarchy state.

## Generation and temporal sequencing

`ScenarioBuilder.StampPromisedWorld` is an override layer after ordinary sector generation:

1. Select a suitable Imperial world deterministically.
2. Establish the Sector Lord’s promise and attach `CampaignScenario` state.
3. Stamp Tyranid and Genestealer Cult pressure onto the objective world.
4. Place the Chapter fleet in orbit, not on the surface.
5. Compose and store the briefing.
6. Simulate the objective world forward through the shared turn processors so the invasion has
   history and a non-static starting position.

The pre-landing simulation uses the same faction strategy, strategic/tactical mission, aftermath,
planet, and intelligence processors as normal play. It intentionally omits campaign-date advancement,
Chapter upkeep, fleet travel, other planets, and objective resolution.

## Turn-loop resolution

`ScenarioTurnProcessor` runs after missions, aftermath, upkeep, fleet movement, and planetary
simulation. This ordering ensures the objective reads the settled world state for the week.

- **Win:** the promised world has no public hostile planetary presence and remains recoverable;
  Chapter control is installed and the Sector Lord’s opinion improves.
- **Lapse:** the world becomes irrecoverably overrun; the promise is withdrawn and opinion falls.
- **Pending:** neither condition is satisfied.

The processor emits a typed scenario notification for UI presentation. The briefing uses its persisted
acknowledgement flag, so loading a campaign does not replay it accidentally.

## Balance anchors

Scenario tuning lives in `ScenarioRules`, not in this document. The intended balance shape is:

- the invasion must already be in motion when the player arrives;
- the world remains plausibly winnable if the player commits promptly;
- ordinary strategic NPC combat, rather than thousands of generated tactical actors, resolves
  army-scale PDF/Tyranid fighting;
- the stranded Tyranid biomass/consumption budget is the main pacing lever; and
- deterministic scenario tests and optional trace diagnostics validate survival and completion bands.

Two decisions behind the current knobs are worth retaining, because both replaced an approach that
did not survive contact:

- **Tyranid strength is relative, not absolute.** `TyranidGarrisonStrengthMultiple = 1.0` sizes the
  swarm against the promised world's own PDF — the planet's whole pre-stamp garrison, split across
  the stamped regions. Absolute constants were tried first and a headless diagnostic showed them
  roughly three orders of magnitude too small. The basis was the average region's *civilian
  population* until 2026-08-02; because a Tyranid faction's Population is its MilitaryStrength, that
  compared the swarm's army against a headcount ~33x the PDF it actually fights.
- **Winnability is structural, not a growth throttle.** The original plan capped Tyranid *logistic*
  growth below the default curve (`GrowthMultiplier = 0.4`). That knob was **removed** when the
  faction was flipped to `GrowthType.Consumption` (rules-DB migration `migrate-tyranid-consumption`),
  which consumption ignores entirely. The swarm is now held in check by a finite stranded biomass
  budget plus the pre/post-landing delays — so a dawdling player faces a spreading swarm rather than
  a soft-lock, without a throttle to tune.

Opinion swings on win/lapse are `±0.5`. The win window and idle-player lapse behavior have been
validated in-engine; current values are tuned, subject to ordinary balance adjustment.

Detailed army-scale formulas are retained in `Design/Reference/BattleLogic.md`.

## Deliberately deferred

- A full authored narrative system beyond `BriefingComposer`.
- Global Chapter reputation and Inquisition consequence systems.
- Rich governance titles and capital markers on every strategic screen.
- Replacement-world offers after failure.
- Reduced starting Chapter strength if later playtesting shows the objective is trivial.
- Explicit air, void-support, blockade, and Guard-quality modifiers for strategic combat.
