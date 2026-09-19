# Force Allocation — Calibration and Rejected Alternatives

Appendix to `OnlyWar_TDD.md` §6.2. The mechanism lives there; this holds the measured figures it was
tuned against, the facts about the world that are not derivable from the code, and the alternatives
that were tried and rejected. Nothing here restates a formula that lives in source.

## Reconnaissance, measured

Per-squad weekly observation margin, measured across Grist Nine runs on 2026-09-17:

| Recon aggression | Per-squad mean | Negative sweeps |
| --- | --- | --- |
| Cautious | 0.96 | 26% |
| Aggressive | 2.77 | 10% |

Measured slope is **0.60 per aggression step** against 0.70 predicted by `DifficultyStepPerLevel`.
`ExpectedSweepMarginAtNormal` is 1.55 because Normal sits one step above Cautious.

**The pooled margin is a signed sum, not a best-of-n.** A squad that rolls badly cancels one that
rolled well, so adding squads raises the mean and the variance together and reliability cannot be
bought with squad count: at a per-squad mean of 0.96 and sd 2.1, eighty percent confidence would take
about twelve squads on one region. Negative results are deliberate — squads split up across a large
region and one can come back with a wrong picture of its patch.

**Aggression is the lever that works.** `MissionAggressionModifiers.EffectDifficulty` is the exact
inverse of `ExposureDifficulty`, so caution is paid for in intelligence. A single flat expected margin
starves a cautious cult or wastes half a bold WAAAGH, which is why sweep sizing reads the faction's
doctrine rather than a constant.

## Awareness, and why a listening post cannot substitute for scouting

Two ongoing sources only: recon sweeps, through `ResolveReconResult`; and listening posts, at
`0.2 × level` per turn. Combat contact and patrols do **not** feed it — they move *beliefs*, a different
quantity on a different path. Awareness decays 25% per turn.

`ApplyAwareness` decays first and then adds, so awareness settles at

```
A = A × 0.75 + 0.2 × level    →    A = 0.8 × level
```

**A level-1 listening post therefore tops out at 0.8 — permanently below the 1.0 threshold** that buys
the first significant figure of estimate precision. Crossing it needs level 1.25, which is into the
2,000-point cost band.

Measured on Grist Nine, 2026-09-18, before occupied regions could be scouted:

| | Listening post | Awareness |
| --- | --- | --- |
| Mu, Nu, Xi (occupied) | 1.00, 1.00, 1.16 | 0.20, 0.38, 0.49 |
| Pi, Zeta, Epsilon (scouted, unoccupied) | 0.0 | 1.22, 1.06, 6.67 |

Pi holds no sensor at all and sits above the threshold on sweeps alone. The stored beliefs corroborate
it from the other side: regions with no awareness row carry estimates of exactly 100 or 1000 — powers
of ten, which is `CoarsenEstimate` at zero significant figures — while Epsilon at 6.67 awareness
carries 513.

**Awareness is the scarce quantity, not belief.** `ObservePublicActivity` confirms every public
presence on a planet for free each turn for any observer holding ground anywhere on it; distance and
awareness do not enter. "We do not know who is there" is therefore almost never the constraint.

## Beliefs must keep updating after they are confirmed

`ObservePublicActivity` computed `delta = ConfirmedThreshold - previousEvidence` and then
`if (delta <= 0f) continue;`, which discarded the whole **sighting** rather than just the evidence
increment. A region observed well enough to confirm had its believed strength frozen at whatever it was
that week, permanently. Battles, withdrawals and reinforcement all went unnoticed. On Monody Prime the
Orks believed 10,000 in a region holding 218, sized an assault at twice the belief, and committed
20,000 to kill 164.

Two changes, and the distinction between them is the point:

- A repeat sighting of confirmed ground contributes `RepeatSightingEvidence` (0.25) instead of being
  dropped. Small, because it adds little certainty — but non-zero, because `IntelObservation`'s
  contract is that an observation moves evidence, and that invariant is worth keeping.
- `BlendEstimate` caps **both** weights at `ConfirmedThreshold`. Previously the prior carried
  accumulated evidence up to 12 against a sighting weighted 0.75, so the region watched most closely
  was the one whose numbers were most out of date.

**"How sure am I that they are there" and "how strong are they" are different questions, and only the
first one saturates.** A current sighting is worth a confirmed measurement, so they meet at equal weight
and a belief halves its error each time it is looked at.

A faction standing in a region reads the current strength coarsened at its own real awareness
(`FactionIntelligenceRules.CoarsenEstimate`). It notices a garrison leaving even if it cannot count what
is left, and scouting its own streets sharpens the figure — which is what makes that worth doing. The
intel floor applied for occupied ground decides which *model* applies (no population-anchored prior when
you are standing there); it does not buy precision.

## Invasions land where they can win, and next to each other

`AllocateLanding` allocated `Math.Min(remaining, desired)`, so once the force ran low the next region
was landed on anyway with whatever was left — a beachhead deliberately sized under
`DefendedLandingRatio`, which is to say one that loses. The leftover now reinforces the first beachhead
instead.

Landing sites also prefer ground adjacent to ground already chosen. Scattering across a planet gave
every beachhead hostile neighbours on all sides, which then charged each landing an enormous defensive
requirement for enemies it had invited on itself.

Carrying capacity is **not** involved in landing allocation and never was; the spread came from handing
a flat `UndefendedLandingBattleValue` to region after region ordered by population.

## Strategic combat thresholds, and what "contested" means

A region reads as contested whenever two or more mutually hostile **public** factions are present;
`Region.ControllingFaction` returns null in that case regardless of who won the fighting. A presence
leaves public view by one of two routes, and both require **exactly zero** military strength:
`StrategicCombatResolver.HideBrokenCivilianDefender` on a won strategic assault, or
`RegionControlTurnProcessor.UpdateImperialRemnantState` when the default faction's garrison is gone and
a public enemy is present.

Strategic combat clamps defender losses at 75% per battle, so **it can never reach that zero by
itself**. Tactical resolution can. That is why the handoff floor matters more than it looks: 20,000
battle value committed against a defender of 218 killed 164 and left 54 standing.

**So over-commitment was what trapped mop-up fights in the one resolver that cannot finish them**, and
the contested-region count was measuring civilian presence rather than military success. Several rounds
of tuning were spent against that number before the mechanism was traced.

Observed, Monody Prime 2026-09-18 — the trap in the act:

```
defenderBV=426  estimatedDefenderBV=1000  committed=2040  strategic
defenderBV=506  estimatedDefenderBV=1000  committed=2160  strategic
```

Both defenders sit under the crossover and should have resolved tactically. The estimate read 1000 — a
power of ten, so zero significant figures, so awareness below 1.0 on ground the faction occupied and
could not then scout.

## Construction economics

`DefenseBuildCost` is `2 × 10^band`, charged at ×100, so a first level costs 200 battle value and a
second 2,000. Enumeration offers only the remainder of the **current** band, so the tenfold escalation
is invisible inside a single turn and construction is capped at one band per region per defence type
per turn.

Measured on Grist Nine Mu's listening post:

| Week | Battle value | Level gained |
| --- | --- | --- |
| 1 | 200 | 0 → 1.00 |
| 2 | 660 | 1.00 → 1.33 |
| 3 | 1,340 | 1.33 → 2.00 |

**Construction self-limits only trivially** — the weekly ceiling is the number of slots, not an
economic brake. Within a turn the value per point is identical for the first point and the last,
because the shape is linear across a band, so once a build leads it takes its whole level without
tapering.

One level of works is worth about a sixth of a defence, read off the combat rules rather than chosen:
about 8% through `StrategicCombatRules.DefenderProtection`, about 10% through `EntrenchmentMultiplier`,
and about 20% off tactical casualties in `MissionAftermathProcessor`.

## Rejected alternatives

**Concave as the default curve.** A square root has unbounded slope at zero, so the first sliver of a
concave task is worth an arbitrarily large amount per battle value — and since that is exactly how bids
rank, a concave task outbids every threshold-shaped one however doctrine weights them. Feeding and
patrolling starved every assault until these became linear. Linear is the default; a curve is
non-linear only where the resolver genuinely has a threshold. Reconnaissance keeps `Concave` because
pooled margins really do go under a square root, and the spike is bounded there because its saturation
is only a few squads.

**Flooring failed sweeps** so bad squads cannot cancel good ones — rejected. Bad intelligence is
deliberate.

**Narrowing the mission-check sigma** — rejected. It is shared by every mission type.

**Normalising a family's importance against the other candidates** — rejected after three separate
scale errors in one expression, each of which hid the next:

1. `unansweredThreat / largestUnansweredThreat` alongside a saturation that *is* the unanswered threat.
   The two cancelled: the rate came out as `doctrine / maxNeed`, so every threatened region bid at an
   identical rate whether it faced a hundred or ten thousand.
2. `population / largestPopulation`, which made one big region devalue every other — and which read
   `RegionFaction.Population`, meaning civilians for the Imperials but **warband size** for a
   `PopulationIsMilitary` faction, so ground was "worth holding" in proportion to how many of our own
   troops already stood on it.
3. `min(1.0, hostileFronts × 0.35)` as the replacement for (1), which saturated instantly — every
   region on a contested planet borders three or more enemy-held regions, so the term capped at 1.0 and
   population never entered the decision at all.

The invariant that came out of it: **a family may normalise only against something intrinsic to
itself.** A preference between otherwise equal tasks belongs in `ForceTask.TieBreakValue`.

**Discounting threats from factions that never attack** — rejected by removing the asymmetry instead.
Posture is an *intention*, not a strength or a position, so nothing observable could support it. The
default faction now plans offensively like everyone else; a PDF still almost never attacks, but the
force ratio stops it rather than a rule.

**Scouting past the border, and recon spill** — both rejected. The player cannot target beyond
adjacent-or-own, a region is roughly a day's travel, and missions already model that: a force not in the
target region runs `InfiltrateMissionStep` first while the pooled margin is a per-day sum, so a two-hop
sweep would spend most of the week walking and observe far less. Spill describes a sightline that does
not exist at this scale. Reopen only if region size changes.

**Symmetric republish for `Indelible` factions** (hide at zero population, unhide above it) — rejected.
It would delete the feral-ork cycle: the invasion could then only end by reducing every Ork region to
exactly zero, which also removes the fungal seed that is supposed to regrow. Hidden-with-population is
the state the fiction needs.

**Giving anti-air a combat effect** — rejected as out of scope. It defends against attacks that cannot
happen: `DeepStrike`, `EstablishAirhead` and `CloseAirSupport` exist as mission types with return
policies and display names, and nothing issues any of them. The order was withdrawn from the player
instead; the stored level, decay, sabotage, ally transfer, overlay and watch sums are all intact, so
re-offering it is one line once an air track exists.

## Timing

Runtime is dominated by **what the AI does**, not by how long the planner takes to decide it — these
tests simulate turns until a condition. Wall clock cannot separate "the planner is slow" from "the AI
finally does something". One run took 1h55m and was read as evidence that a change had exploded the
combat volume; the same code later ran in five minutes, and the difference was contention from games
running on the same machine.

If cost is suspected: confirm the machine is idle (CLAUDE.md puts contention alone at a 2x factor),
measure **call counts** rather than seconds, and only then profile. The candidate generator allocates a
`SortedSet` per bid evaluation and is the obvious first suspect if the planner really is implicated.

Sector generation measured 2m50s against 2m57s across the floor change, which is noise, and those two
runs were not single-variable.
