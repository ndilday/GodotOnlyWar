# Engagement Scoring Overhaul

**Status: implemented, moved to `Reference/` on 2026-08-08.** All seven phases are in the code. It is
retained rather than deleted because nothing else explains the math it introduced — the λ sweep table,
the `K_loc`/wound-progress derivations, the Phase 6 range-model collapse, and the per-site audits that
record *why* each site kept or dropped a discount.

**Two things a reader must know before trusting this document:**

1. **Phase 5d's terminal formula is superseded.** `Design/Active/EngagementScoringRepair.md` (D1)
   replaced the geometric `discount^turnsToAct / (1 − discount)` with a hyperbolic arrival discount
   and an explicit `ExpectedRemainingTurns = 20f` horizon, because the geometric form multiplied a
   melee squad's entire reason for existing by ~1e-13 across a long approach. The Phase 5d section
   below is annotated in place; take the formula from the repair plan, not from here.
2. **The residual open items moved with the status, not with the document.** Godot verification, the
   provisional λ = 0.5, and the un-swept `SaturationFraction` / `NegligibleRemovalFraction` are
   tracked in the repair plan's §7. Do not re-derive them from this file's "Outstanding" section,
   which is preserved as the record of what Phase 7 handed over.

Squad posture selection (`BattleSquadPlanner.EvaluateEngagementOption`) adds two terms that are
nominally the same currency — battle value — but differ by roughly four orders of magnitude. The
immediate-fire term cannot influence the decision, so squads choose movement on the fourth
significant figure of a capability proxy.

## Outstanding

**All seven phases are complete.** Suites green: **484 Battles / 305 Turns / 163 Missions.**

Phase 7's agent was terminated mid-phase by a spend limit, but it died during *validation*, not
implementation — all three parts were already in the tree. Completing it consisted of removing its
scratch harness, adding the regression guard it was building
(`MissionOpeningRangeTests.PreferredOpeningRange_IsTheDerivedBandNotWeaponReach`), and running the
suites it never reached.

Phase 7 Part 2 resolved the opening-range gap by **option (b): pricing the approach.** Opening at
range `r` commits a force to `(r − band)/speed` turns of closing at strictly less removal per turn,
so un-opposed the discounted total is maximized at `r = band` and the approach cost falls to zero by
construction. `RangedEffectivenessCurve.SaturationFraction` was therefore left at 0.5 — the option
(a) tuning was not needed, and that constant still serves the two questions where the un-opposed
form is genuinely right (`WorthwhileRangedReach` and pursuit, both asking "can I hurt them from here
at all"). `MissionOpeningRange.Interpolate` keeps its two now-unused `GetRandomSquadMember` draws as
deliberate RNG-stream anchors, so seeded mission divergence stays attributable to the range change
rather than to a re-baseline.

**Requires the user's manual Godot verification** (never run by any phase, per project convention) —
every behavioural phase: 1, 2, 3, 5, 6, 7.

**Provisional values, cheap to revisit:**

- `WoundProgressCreditWeight` (λ) = **0.5**, chosen from a recorded sweep against a *reconstruction*
  of the reference battle, not the traced battle itself. The reconstruction reproduces the pathology
  (outgoing 0.009 vs future 22.6, and the λ=0 collapse) but not the trace's 0.018 closing margin.
- `RangedEffectivenessCurve.SaturationFraction` = **0.5**. See Phase 7 Part 2.

**Defects found in passing, not fixed:**

- ~~**Placer coordinate truncation.**~~ **FIXED at the root.** `BattleSquadPlacer.PlaceSquadHorizontally/Vertically`
  narrowed every cell coordinate through `(short)` before widening it straight back into the
  `ValueTuple<int, int>` the grid stores, so a large standoff wrapped to cells beside the origin and
  threw on the collision. `BattleGridManager` keys cells with a sparse `Dictionary<(int X, int Y), int>`
  and has no bounded extent, so the casts were pure loss. Removing them fixed `AnnihilationPlacer`'s
  unguarded exposure and let `AmbushPlacer.FormationStandoff` revert to its pre-2026-07-08
  `Math.Max(range, 1)` — the `short.MaxValue / 2` ceiling Phase 7 added is gone. Both placers now have
  huge-range tests that assert the forces really stand off, rather than only that every squad was
  mapped: `AmbushPlacerTests.PlaceSquads_HugeEngagementRange_DoesNotWrapCoordinates` (strengthened —
  it was named for an assertion it never made) and `AnnihilationPlacer_HugeRange_DoesNotWrapCoordinates`
  (new).
- **`WITHDRAW_EVAL.friendly_viable_damage` cannot distinguish "shooting" from "shooting effectively."**
  It is a 2-deep bool queue of "did any soldier execute an attack action this turn", not a viability
  estimate. The plan's premise that it derived from the removal estimate was wrong. Untouched.
- **Melee removal in the lookahead is still a capability proxy.** Only the ranged half was converted
  to the Phase 4 table.
- **`woundProgress` sums across hit locations, but a disable needs concentration in one**, so it
  over-states. Handled by a scalar (λ) rather than by modelling where damage lands; a real fix needs
  the battle-damage accumulator deferred elsewhere for the same reason.

**Unverified inferences** — recorded so they are re-checked rather than inherited:

- The 60× gap between Rostzin Squad's `outgoing` (0.063) and Rostadi/Scharel's (0.001) is *inferred*
  to be target assignment — Rostzin drawing the Lictor while the others drew Carnifexes. Back-solved
  from the trace; targets are not logged at that level.
- The traced 200-yard standoff is *inferred* to be the clamp binding. `Math.Clamp` emits exactly 200
  for any input ≥ 200, and three of four representative draws give 1000, but a Lictor draw would give
  ~206 unclamped. Not provable from the log.
- Marine BVs and squad composition were reconstructed from `Battle XP` rosters and
  `WITHDRAW_EVAL start_bv`, not read from the save.

## Evidence

Xibarrus Zeta ambush, `game-trace-20260804-124243.log:39224` — 30 Space Marines vs 4 Tyranids,
183 turns, 3 kills. Turn 1, Rostadi Squad (id 16):

| option | outgoing | future | score |
|---|---|---|---|
| Hold | 0.001 | 22.848 | 14.852 |
| CloseToContact | 0 | 22.877 | **14.870** ✔ |

`score = outgoing + 0.65 × future` reproduces both lines exactly. The decision margin is 0.018;
Hold's entire immediate-fire term is 0.001, so `outgoing` would need to be 19× larger to matter.

`future` reconstructs exactly from a tactical squad's own battle value (9× Tactical Marine BV 9 +
1× Sergeant BV 11 × 0.65 ranged share = `UsableRangedBattleValue` 88.15):

| ply | computation | value |
|---|---|---|
| depth 0 (terminal) | `88.15 × 0.25 / (1 + 0)` | 22.04 |
| depth 1 | `8.198 + 0.65 × 22.04` | 22.52 |
| depth 2 | `8.198 + 0.65 × 22.52` | **22.84** |

The per-ply exchange comes from `AggregateRemovalRate`, which reads the defender only as a cap:

```csharp
float ranged = attacker.UsableRangedBattleValue * 0.10f * rangedRangeFactor;
return Math.Min(defender.TotalAbleBattleValue, Math.Max(ranged, melee));
```

No hit probability, no take-out probability, no armor, no constitution. It asserts that a shooting
squad removes 10% of *its own* battle value per turn. For this squad that is 8.198 BV/turn — 4.5% of
the entire enemy force — against `outgoing`'s calibrated 0.001 BV/turn. **The two halves of one score
disagree about the same squad's shooting by a factor of ~8,000.**

Contributing defects found alongside it:

- `PolicyRangeDelta` and the depth-0 terminal both use `desired = PreferredBandUpper`, which
  `BattleEngagementFrameBuilder` sets to the weapon's **maximum** range (1000 for a boltgun). At range
  200 that makes `turnsToAct = 0` and own-motion `= 0` for every policy, so the lookahead cannot see
  its own movement. The entire 0.029 spread between Hold and CloseToContact comes from
  `rangedRangeFactor` responding to ~6 yards of projected motion (0.930 → 0.932).
- `CalculateSquadImminence` discounts *ranged removal* by *when the enemy will reach us*
  (`1/(1+turnsUntilEngagement)` ≈ 1/26 here). Arrival time does not affect whether a bolt lands; this
  is a category error, and it double-discounts against `EngagementFutureDiscount`. Against a
  withdrawing enemy it drives ranged value toward zero. **Fixed in Phase 3.**
- `BaselineRangeDelta` has no withdrawing branch. A melee-only profile is `IsContactSeeking`, so it is
  always projected as charging. The Tyranids in this trace chose `StepBack` on most turns.
- `MaxFormationStandoff = 200` (`AmbushPlacer`) was added 2026-07-08 in commit `4d6182c` as a `ushort`
  overflow guard; the only accompanying test asserts that coordinates do not wrap. It has acted as an
  unreviewed balance constant since.

## Invariants

Every phase must preserve:

- Planning/resolution parity and deterministic battle replay.
- Friendly-fire attribution.
- The shared Battle Value currency used to compare ranged, melee, and movement.
- **Squads must not fire at targets they cannot damage.** This is why take-out probability replaced
  raw to-hit scoring; any replacement metric must go to ~0 when penetration is impossible.

## Phases

### Phase 0 — Naming and semantics (behaviour-neutral)

Six quantities are called "imminence" or serve as one, with four incompatible definitions:

| site | formula | whose speed | whose range | shape |
|---|---|---|---|---|
| `CalculateSquadImminence` :4527 | `1/(1+ceil((d − targetPreferred)/spd))` | target's | target's | 0–1 discount |
| `contactImminence` :2010 | `1/(1+turnsToContact)`, `d−1` | attacker's | fixed 1 | 0–1 discount |
| `intercept` :1336 | `1/(1+d/spd)`, no ceiling | threat's | none | 0–1 discount |
| terminal :1211 | `0.25/(1+max(0,d−desired)/spd)` | own | own | 0–1 discount × 0.25 |
| pair weights `FrameBuilder:157` | `1/max(1,d)` | — | — | inverse distance |
| screen assignment `FrameBuilder:253` | `spd/max(1,d)` | threat's | — | **rate**, vs 0.015 threshold |

Split into named concepts — `TurnsUntilTargetReachesUs`, `TurnsUntilWeReachTarget`, `ClosingRate`,
`ProximityWeight` — and apply the `1/(1+turns)` discount at call sites rather than baking it into six
near-duplicates. `MinimumScreenImminence = 0.015` becomes checkable once its units are stated.

Document `AggregateRemovalRate`'s actual contract in place: it is a capability proxy, not a kill
estimate, and is not commensurable with `outgoing`.

**Validation:** zero seeded divergence. Any divergence is a defect in the pass.

### Phase 1 — Target motion honesty

- Add a withdrawing branch to `BaselineRangeDelta` so retreating targets project as opening range.
- Give `CalculateSquadImminence` a **signed** closing rate instead of raw `GetSquadMove()`.

Small, self-contained, and independent of the currency work.

**Validation:** re-baseline seeded battles. Divergence should be attributable to withdrawal cases.

### Phase 2 — Range-model conflation

Separate "weapon maximum range" from "preferred engagement range" at the two sites that conflate them
(`PolicyRangeDelta`, depth-0 terminal). This is what makes movement visible to the lookahead and is
the precondition for the shots-per-horizon model in Phase 5.

**Done.** `BattleSquadCapabilityProfile.EffectiveEngagementRange` added alongside — not replacing —
`PreferredBandUpper`, derived in the single method
`BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange` (the Phase 6 seam) against the
able-soldier-weighted mean of the opposing side, matching
`CalculateTurnsUntilTargetReachesUs`'s averaged-opponent pattern (that method was itself deleted in
Phase 3; the averaged-opponent derivation it modelled lives on). Only `PolicyRangeDelta` and the
depth-0 terminal consume it; `AggregateRemovalRate`, `BattleEngagementFrameBuilder.Baseline`,
`BaselineRangeDelta` and `EvaluatePursuitContactProgress` legitimately mean reach and were left
alone with comments saying so.

**Caveat that raises Phase 6's priority.** `EstimateKillDistance` returns the weapon's `MaximumRange`
outright for a non-degrading weapon, so `CalculateOptimalDistance` degenerates to reach whenever
accuracy is not the binding constraint — precisely the bolter-vs-Carnifex case in the trace above
(hit-limited 2417 vs `MaximumRange` 1000). Phase 2 therefore separates the two quantities *in the
plumbing* but does not move that scenario at all. The separation bites where accuracy or penetration
actually binds. See the characterization test
`CapabilityProfile_NonDegradingWeaponEffectiveRangeStillCollapsesOntoReach`, which Phase 6 should
flip.

### Phase 3 — Remove arrival time from ranged removal

Stop discounting ranged removal by `turnsUntilEngagement`. Retain the discount for `contactImminence`
(melee charge payoff), where it is correct.

This is the 26× crush. Expect a large, deliberate behavioural change.

**Watch:** `WITHDRAW_EVAL.friendly_viable_damage` is computed off the same optimism. Honest removal
values will start firing disengagement decisions in fights the player currently expects to be fought.
That is arguably correct, but it lands in a different subsystem — verify before proceeding.

**Done.** `GetTargetArrivalDiscount`, `CalculateTurnsUntilTargetReachesUs` and
`BattlePlanningContext.TargetArrivalDiscounts` are deleted. Ranged removal is now
`hit × takeOut × targetBV`, undiscounted, at all three former consumers.

Per-site audit — the plan said "roughly six" consumers; there were **three**, all immediate:

| site | quantity | decision |
|---|---|---|
| `EvaluateRangedTarget` | this turn's aimed/unaimed shot | **removed** |
| `SelectBestTemplateFiringLine` per-victim loop | this turn's cone/flamer burst | **removed** |
| `EvaluateBlastThrow` (via `BlastNearbySoldier.TargetArrivalDiscount`) | this turn's grenade detonation | **removed** — friendly value was already undiscounted here, so the discount was a pure enemy/friendly accounting asymmetry |
| `EstimateChargeNet.chargeArrivalDiscount` | melee payoff deferred until contact | **kept** — never used this function; it computes `TurnsUntilWeReachTarget` from the charger's own speed |

The other "imminence" sites from the Phase 0 table (depth-0 terminal, screen `intercept`, `PairWeights`,
screen assignment) are independent quantities that never consumed this function. They were left alone;
only their cross-reference comments were updated.

**Target selection.** The discount varied by target squad, so removing it does change *which* enemy a
soldier shoots. It is not a loss. Distance is already encoded in the shot's value through
`CalculateRangeModifier` = `2.4663·ln(2/(r+v))`, which is strongly monotone in range — doubling the
distance costs ~1.71 to-hit points against a roll with σ = 3 — and it enters *before* take-out
probability, so a distant target's `hit × takeOut × BV` falls off on its own. On top of that,
`SelectBestRangedTarget` iterates `GetNearestInRangeEnemySquads` nearest-first with an exact-tie
tie-break that stays on the closer option, and subtracts `LaneSpreadPenalty`. No gap found; nothing
was propping up near-target preference except the defect.

**A weaker "will this enemy ever engage" factor.** Argued **against implementing now**, and it is not
in this phase. The honest version of that idea is not a discount on the shot — it is a *mission* term:
an enemy that will never reach us also never damages us, so the value of removing it is lower for
reasons of objective, not ballistics. Folding it back into per-shot removal would rebuild exactly the
category error just deleted, and it would again zero out fire on a retreating enemy. If it is wanted,
it belongs alongside `CanAnySquadProsecuteMission` in the force-level layer, after Phase 5 gives
`outgoing` and `future` a common currency. Noted as a follow-up; not built.

**`friendly_viable_damage` — the plan's premise was wrong.** It does *not* derive from
`AggregateRemovalRate` or from any removal estimate. `BattleTurnResolver.BuildMetrics` reads
`_damageActionHistory[side]`, a 2-deep queue of one bool per turn set at
`BattleTurnResolver.cs:765`: "did any soldier on this side *execute* a `ShootAction`,
`AreaAttackAction`, `BlastAttackAction` or `MeleeAttackAction` this turn". It is an action-occurred
flag, not a viability estimate. In the reference trace it read `true` while the marines could not
meaningfully hurt the Tyranids because they were *firing* — accurately, if uselessly.

The scoring change therefore pushes it the **opposite** way from the plan's fear. `SelectBestRangedTarget`
only returns a target when `best.Score > 0`; the old discount could drive removal to 0 (exactly 0 for a
withdrawing target, whose `turnsUntilTargetReachesUs` was infinite), suppressing the shot entirely and
making the flag *false*. Undiscounted removal makes shots more likely to be planned, so the flag
trends *more* `true`, and `VoluntaryWithdrawalReason.EligibleAndUnableToDamage` fires *less* often.
No new disengagements are introduced by this phase. The real defect the trace exposed — that the flag
cannot distinguish "shooting effectively" from "shooting" — is untouched and remains open.

**Validation.** `OnlyWar.Tests.Battles` 470 passed, `.Turns` 305 passed, `.Missions` 162 passed.
Two tests changed expectations, both because they encoded the defect:

- `GrenadePlannerTests.GrenadeBearer_RefusesADangerCloseThrowThatCostsMoreThanItRemoves` — the refusal
  came partly from enemy value being discounted while the thrower's own expected loss was not. With
  both priced in the same currency, a BV-20 thrower's 0.591 self-cost no longer exceeds the 0.711 it
  removes. The property is intact; the crossover moved, so the thrower is now BV 40.
- `BattleAbandonedWoundedTests.BattleEnd_SideHoldingFieldFinishesOffTheWoundedTheLoserLeftBehind` —
  seeded full battle whose winner flipped. The test's own comment already prescribes the fix
  (it is an aftermath test, not a balance baseline): `PdfPlatoonCount` 5 → 6.

Phase 1's `TargetArrivalDiscount_*` tests were deleted with the function they measured; the withdrawal
case is re-expressed as `RangedRemoval_AgainstWithdrawingTargetIsNotZeroed`, which now asserts the
opposite conclusion for the same scenario.

**Reference scenario** (`ReferenceScenario_BolterSquadAt200YardsHasNonTrivialImmediateFireValue`):
a fully-aimed bolter 200 yards from a size-8 melee enemy moving 4/turn. Hold's `outgoing` was
**0.4411** before and is **22.497** after — a factor of 51, exactly `1 + ceil(199/4)`. Since `future`
in the traced battle was 22.85, `outgoing` and `future` are now the same order of magnitude and the
immediate term can finally move the decision.

### Phase 4 — Removal-rate infrastructure (behaviour-neutral)

Build the machinery without switching `AggregateRemovalRate` onto it.

The target is already available: `EvaluateImmediateActionValue` returns `rootActions` carrying
`TargetId`, and `_context.RangedEvaluations` already caches the full evaluation. Add a per-(shooter
squad, target squad) removal-rate table to `BattlePlanningContext`, memoized per turn alongside
`SquadImminence`.

Range rescaling stays closed-form — the lookahead must not call the targeting stack:

```
hit(r) = Φ( (total₀ + 2.4663·ln((r₀+v₀)/(r+v)) − 10.5) / 3 )
```

One `ln`, one `Φ`. Cache `(total₀, r₀, v₀, takeOut₀, targetBV)` per pair.

For non-degrading weapons this is **exact**, not an approximation: `CalculateDamageAtRange` returns a
flat `DamageMultiplier` when `DoesDamageDegradeWithRange` is false, so take-out is genuinely
range-independent. For degrading weapons, cache the per-location vector `K_loc = effectiveArmor +
requiredPenetratingDamage` (range-independent) and evaluate

```
takeOut(r) = Σ_loc w_loc · Φ((DamageRollMean − K_loc/damageCoefficient(r)) / DamageRollStdDev)
```

a fixed-size sum of normal CDFs with no hit-location or wound traversal.

**Constraint:** planning is already the bottleneck — 429ms of this battle's 504ms. The recursion must
stay closed-form arithmetic over cached values.

**Validation:** zero seeded divergence (nothing consumes the table yet); planning-time regression check.

**Done.** `Helpers/Battles/SquadPairRemovalRate.cs` adds `TakeOutLocationTerm`, `PairRemovalTerm` and
`SquadPairRemovalRate`; `BattlePlanningContext.PairRemovalRates` memoizes the table per turn;
`BattleSquadPlanner.GetPairRemovalRates(shooterSquad)` builds it.

**Shape and semantics.** The table is stored shooter-squad-major — `ShooterSquadId → TargetSquadId →
SquadPairRemovalRate` — because one pass over the shooter squad's soldiers produces the whole row, so
building a row lazily costs no more than building one cell. A cell holds one `PairRemovalTerm` per
contributing shooter: `(total₀, r₀, v₀, takeOut₀, targetBV, weapon template, K_loc vector)`.

*Aggregation: sum of per-soldier argmax.* Each able, placed, ranged-armed soldier contributes its
single best target's removal, and lands in exactly one cell — the one holding that target's squad. The
cell's rate is the **sum**, not the mean, because `AggregateRemovalRate`'s consumers want a squad-level
battle value per turn, and because summing per-soldier argmax is exactly what `outgoing` does in
`EvaluateImmediateActionValue`. That makes the two commensurable, which is the whole point of the
phase. An absent cell means no soldier is aimed into that enemy squad — rate 0. No cap is applied;
`AggregateRemovalRate` already clamps to the defender's `TotalAbleBattleValue` and `outgoing` caps per
target soldier, so the choice stays with the consumer.

*Reference posture.* Captured stationary, un-aimed, no-bulk. That is the natural reference for
`AggregateRemovalRate`, whose lookahead consumer already applies an explicit per-policy
`outgoingRetention` (Hold 1, Jog 0.65, Run 0).

*`ReferencePairRange`* is the mean of the terms' own reference ranges; `RateAtRange(r)` shifts every
term by `r − ReferencePairRange`, so per-soldier geometry offsets survive and
`RateAtRange(ReferencePairRange) == ReferenceRate` exactly.

**Non-degrading exactness — confirmed in code.** `BattleModifiersUtil.CalculateDamageAtRange` branches
on `DoesDamageDegradeWithRange` and returns a bare `DamageMultiplier` when it is false, with no range
term anywhere in that branch. `takeOut₀` therefore *is* `takeOut(r)` for every `r`, and those terms
carry no `K_loc` vector at all (`TakeOutTerms` is null). The claim is asserted directly by
`RescaledHitProbability_MatchesDirectEvaluationForNonDegradingWeapon`.

**Drift between `CalculateTakeOutProbabilityOnHit` and the `K_loc` path is structurally impossible.**
There is now exactly one hit-location walk, `AccumulateTakeOutTerms`, with an optional damage
coefficient and an optional collector. `CalculateTakeOutProbabilityOnHit` passes a coefficient and no
collector (so the hot path allocates nothing); `BuildTakeOutLocationTerms` passes a collector and no
coefficient. Both then reach the same per-location tail, `EvaluateTakeOutLocationTail`. Accumulation
order and expression shape are unchanged, so the two agree bitwise —
`KLocVector_ReproducesTakeOutProbabilityExactlyAtEveryRange` asserts exact float equality, not a
tolerance.

**Plumbing added.** `RangedTargetEvaluation` now carries `PreRollHitTotal` and `TargetSpeed`;
`EstimateHitAndDamage`/`EstimatePlannedRangedAttack` return the total rather than only the CDF of it.
Inverting `ApproximateNormalCDF` would have been lossy and recomputing the total would have duplicated
`RangedHitEstimateContext`'s assembly order. `BattleModifiersUtil.RangeModifierCoefficient` names the
`2.4663f` both the forward and the rescaling path read.

**Documented approximation.** The shot count baked into `total₀` (through the rate-of-fire modifier) is
held fixed under rescaling. The live path re-derives shots from the hit probability via
`CalculateShotsToFire`, so a rescaled rate can differ from a full re-evaluation for a burst weapon
whose chosen rate of fire would change with range. Re-running that fixed point means calling the
targeting stack, which is precisely what the lookahead must not do.

**Validation.** `OnlyWar.Tests.Battles` **477 passed, 0 failed** — the 470 baseline plus seven new
tests in `OnlyWar.Tests/Battles/RemovalRateTableTests.cs`. `.Turns` 305 passed and `.Missions` 162
passed, both unchanged: zero seeded divergence, as required for a behaviour-neutral phase.

**Performance.** Nothing on the hot path calls `GetPairRemovalRates`, so planning cost is unchanged by
construction — wiring a build-and-discard would itself have been the regression. Measured (Debug
build, 5-shooter squad vs a 3-soldier enemy squad): table build 0.59 ms non-degrading / 2.17 ms
degrading, once per shooter squad per turn; `RateAtRange` 560 ns/call non-degrading, 2725 ns/call
degrading. The degrading path costs ~5× more because it re-sums the hit-location CDF vector, so Phase
5 should expect the graded term to be cheap for marine small arms and the dominant cost against
degrading weapons.

**Left open for Phase 5 — deliberately.**

- **Pair weights vs best target.** `future` allocates across enemy squads by `PairWeights` (inverse
  distance); this table is already target-selected by per-soldier argmax. Multiplying a cell rate by
  `PairWeights` would double-count the allocation; ignoring `PairWeights` blinds the lookahead to
  flank threats it currently sees. Phase 4 makes both representable and picks neither.
- **`woundProgress`.** `TakeOutLocationTerm.RequiredRatio` is captured but unread. It is the input
  Phase 5's `E[woundProgress | no takeout]` needs, so the graded term is added inside
  `PairRemovalTerm.RemovalAt` without re-plumbing the table.
- **λ calibration** and the depth-0 terminal's recalibration are untouched.

### Phase 5 — Graded damage metric

`CalculateTakeOutProbabilityOnHit` is **already wound-state-aware** — `FindMinimumDisablingWoundRatio`
reads `location.Wounds.WoundTotal`, so take-out rises as a target accumulates damage. The graded state
exists; what is missing is credit for *creating* it. The planner scores only the finishing blow, never
the twenty hits that made it possible. This is a credit-assignment fix, not a new accumulator.

```
removal = BV × [ P(takeout) + λ · E[woundProgress | no takeout] ]
```

`woundProgress` is the expected advance toward the disable threshold normalized by the remaining gap,
computable from the per-location loop that already produces `requiredRatio` and `K_loc`.

Properties:

- Preserves the invariant: if penetration is impossible, `requiredRoll` sits beyond the damage roll's
  tail and both terms are ~0.
- Supplies the missing gradient: a penetrable-but-not-one-shottable target scores positively, and
  scores higher as it softens.
- Reinforces sticky targeting at no cost — squads finish what they started.
- **`λ = 0` reproduces current behaviour exactly.**

Sequencing inside this phase matters. `outgoing` is currently too pessimistic and `future` too
optimistic; the optimism is the only thing making squads act at all. Converging them at λ = 0 sets
both to ~0 and every option ties, handing the battle to `ChooseEngagementOption`'s tie-break.

1. Introduce `woundProgress` in `outgoing` only, λ behind a constant. Sweep λ where it is cheap to
   observe.
2. Only once λ is tuned, switch `AggregateRemovalRate` onto the Phase 4 table.
3. Recalibrate the depth-0 terminal as a geometric continuation of the same per-turn rate rather than
   `attainable × 0.25`. It is currently 41% of `future` (9.312 of 22.84) with no per-turn semantics.

The result should express the intended decision rule directly: *holding buys three low-impact shots,
moving buys two slightly-less-low-impact shots; close only when the marginal gain beats the shot lost.*

**Validation:** full re-baseline. Godot verification handoff.

**Done.** `OnlyWar.Tests.Battles` **484 passed** (477 baseline + 4 `GradedRemovalTests` + 3
`GradedRemovalCalibrationTests`), `.Turns` **305 passed**, `.Missions` **162 passed**. Two existing
battle tests changed; both are analysed below. **No seeded battle flipped a winner** —
`BattleAbandonedWoundedTests` passes unchanged, which was the phase's biggest downside risk.

**5a — `woundProgress`.** `TakeOutLocationTerm` gains `ZeroProgressThreshold`
(`K_zero = effectiveArmor + naturalArmor / weaponWoundMultiplier`), the damage at which a location
first takes any wound. The resolver's damage-to-wound-ratio map is affine, so progress toward the
disable threshold is linear in the damage roll between `K_zero` and `K_loc`, and its partial
expectation has a closed form:

```
E[progress; no takeout] = [ phi(A) - phi(B) - A*(Phi(B) - Phi(A)) ] / (B - A)
```

one exp and two CDFs per location, `A` and `B` being the standardized wound-onset and disabling
rolls. `AccumulateTakeOutTerms` accumulates it alongside take-out in the same walk.

*The design doc's notation was wrong and the code deliberately deviates.* `E[woundProgress | no
takeout]` — a **conditional** expectation — diverges as `P(takeout) -> 1`, so a target certain to die
would be scored as worth more than its battle value. The **partial** expectation is used instead. It
makes the formula an exact decomposition, `E[progress] = P(takeout)*1 + E[progress; no takeout]`, so
lambda interpolates between "only kills count" and "all expected progress counts" and the bracket is
bounded by 1 for lambda in [0, 1]. Asserted by
`GradedRemoval_NeverExceedsTheTargetsWholeBattleValue`.

*λ = 0 checkpoint: `OnlyWar.Tests.Battles` 477 passed, 0 failed* — behaviour-neutral, as required,
before anything else was touched.

*Scope.* The graded fraction replaced the bare take-out probability at **every** site that turns a
landed hit into expected battle value: the conventional ranged shot, the cone burst, both halves of a
blast, the friendly-stray cost, **and melee** (`EstimateTakeOutOnHit`). Melee was included after
initially being left out. Leaving it out was not conservative — it silently rigged every
ranged-versus-melee comparison the planner makes, including the Hold-versus-CloseToContact decision
this phase is calibrated against, in exactly the direction the calibration wanted. `CalculateShotsToFire`
and the table's `ReferenceTakeOut` keep reading the raw take-out probability: a shot count is a
question about kills, not about accumulated wounds.

**5b — the sweep, and a deviation from the plan's ordering.** λ was swept **after** 5c/5d rather than
before. Sweeping first measures a structure that step 5c then replaces: with `future` still on the
capability proxy, `future` is a constant 22.648 at every λ and Hold already wins by 2.3, so the sweep
cannot discriminate. Both sweeps are recorded; the second is the one λ was chosen from and the one
reproduced in `BattleSquadPlanner`'s constant comment.

Reference scenario (`GradedRemovalCalibrationTests`, which regenerates the table): 30 bolter marines
(Dex 15.4, Gun skill bonus 1.4, BV 9/11) at 200 yards from 1 Hive Tyrant (BV 84), 1 Lictor (BV 37)
and 2 melee Carnifexes (BV 30), all melee-only. Species attributes, the Boltgun and the battle values
are read from `Database/OnlyWar.s3db`; the 20mm/10mm chitin assignment is the one number that is not.

| λ | chosen (post-5c/5d) | outgoing | future | Hold − Close | (pre-5c `future`) |
|---|---|---|---|---|---|
| 0.00 | **StepForward** | 0.009 | 1.840 | 2.334 | 22.648 |
| 0.05 | Hold | 0.170 | 3.742 | 2.471 | 22.648 |
| 0.10 | Hold | 0.330 | 5.643 | 2.608 | 22.648 |
| 0.15 | Hold | 0.491 | 7.544 | 2.746 | 22.648 |
| 0.20 | Hold | 0.652 | 9.445 | 2.883 | 22.648 |
| 0.25 | Hold | 0.812 | 11.346 | 3.020 | 22.648 |
| 0.35 | Hold | 1.133 | 15.148 | 3.295 | 22.648 |
| **0.50** | **Hold** | **1.615** | **20.851** | **3.706** | 22.648 |
| 0.75 | Hold | 1.935 | 33.626 | 4.304 | 22.648 |
| 1.00 | Hold | 2.577 | 44.102 | 4.813 | 22.648 |

**λ = 0.5, provisional.** Reasoning, in order of weight:

1. **λ = 0 collapses.** The plan predicted this and the sweep confirms it: with `future` built from
   the same honest rate, a squad that cannot one-shot anything scores ~0 on both halves and the
   decision goes to `ChooseEngagementOption`'s tie-break, which picks StepForward. Every positive λ
   fixes the reported behaviour, so the sweep is choosing a magnitude, not a direction.
2. **Rate of resolution.** 30 marines remove `3 × outgoing` BV/turn against 181 BV of Tyranids:
   ~75 turns at 0.25, ~38 at 0.5, ~23 at 1.0. The stated calibration target is "tens of turns, not
   183".
3. **Physics, and the only argument here that is not tuning.** `woundProgress` SUMS across hit
   locations, but a disable requires the damage to concentrate in ONE of them. The summed figure
   therefore systematically over-states real progress toward a kill, by roughly the number of
   independent disabling locations. That argues for a value clearly below 1.
4. It leaves `future` (20.9) at nearly the magnitude the surrounding score terms were tuned against
   pre-Phase-5 (22.6), so the phase does not silently re-scale commitment, role and readiness costs
   along with it.

The value lives in one commented constant with the sweep beside it, and is settable in-process so the
calibration test can re-sweep without one rebuild per point.

**The invariant holds.** `GradedRemoval_IsZeroAgainstATargetThatCannotBePenetrated` proves it on the
wound model, and `ShippedLambda_StillRefusesToPlinkAtAnImpenetrableTarget` proves it through the whole
scoring stack on the reference geometry: outgoing 1.6148 against penetrable chitin, **exactly 0**
against the same force at armour 255. When penetration is impossible both the onset and the disable
thresholds sit far out in the damage roll's tail and the Gaussian mass between them vanishes, so λ has
nothing to multiply. It cannot buy value against an impenetrable target at any setting.

**5c — pair weights vs argmax, resolved ASYMMETRICALLY.** The two halves ask different questions.

- **Outgoing: argmax table, no `PairWeights`.** The table is already target-selected — each soldier
  contributes its best target's removal to exactly one enemy squad's cell — so summing cells over
  enemies reconstructs the squad's true whole-squad removal per turn, computed the same way as
  `outgoing`. `PairWeights` is a normalized allocation summing to 1; multiplying an already-allocated
  rate by it divides the squad's fire twice and understates every shooting option.
- **Incoming: `PairWeights` retained**, applied to the enemy's WHOLE-squad rate at our projected
  separation. There the allocation is the real question — what share of that squad's fire lands on us
  rather than on our neighbours — and its argmax cell cannot answer it: that cell is a single frozen
  choice against this turn's geometry, so reading it directly would swing projected incoming between
  "all of it" and "none of it" as the enemy's best target flickered between our squads.

This does not blind the lookahead to flank threats, which was the stated risk of choosing argmax: a
distant enemy squad still appears in the incoming half, which is where it actually costs us something.

Melee is untouched by the table (which is ranged-only) and keeps its capability proxy — 13% of the
attacker's usable melee battle value inside 1.5 — with its `PairWeights` allocation on the outgoing
side. Dropping it would make melee-only enemies read as harmless. Phase 6 is where that becomes a real
estimate too.

**A hole the plan did not anticipate: out-of-reach squads.** `SelectBestRangedTarget` only considers
enemies inside weapon reach, so a squad currently out of range gets an EMPTY table row and would price
every future turn at 0 — no reason to ever close, at any distance. The old capability proxy did not
have that hole: it recomputed its range factor at the PROJECTED range and became positive as soon as
the squads came inside reach. Fixed by capturing a reference term against the nearest enemy anyway
(`EvaluateNearestOutOfReachTarget`) and gating `PairRemovalTerm.RemovalAt` to 0 beyond the shooter's
`MaximumEffectiveRange`. The term contributes nothing until the lookahead projects the squads into
range and then contributes the real `hit × removal × BV` at that range — the same gradient, honestly.

**5d — the depth-0 terminal.** Was `attainable × 0.25 / (1 + turnsToAct)`: 41% of `future` built from
the squad's own battle value, with no per-turn semantics and no reference to what it was shooting at.
Now a geometric continuation of exactly the per-turn net exchange the plies compute:

```
terminal = exchange(rangeWhenActing) * discount^turnsToAct / (1 - discount)
```

> **SUPERSEDED — do not implement this formula.** `EngagementScoringRepair.md` D1/Step 3 replaced the
> geometric factor with `ExpectedRemainingTurns / (1 + turnsToAct)`. Reusing `EngagementFutureDiscount
> = 0.65` as the discount on the *geometric tail* asserted that nothing past turn ~10 exists; at the
> reference battle's 69.5 turns to contact that is 1e-13, so a melee-only squad's entire payoff was
> multiplied by zero and `future` reduced to `−incoming`, making retreat strictly optimal. The two
> other arrival discounts in the codebase were already hyperbolic; this phase unilaterally made the
> third geometric. Everything else in 5d — evaluating at `rangeWhenActing`, pricing the terminal at
> Hold retention, and the "once I am standing where I want to stand" reading — survives unchanged.

Read literally: *once I am standing where I want to stand, this is what each further turn is worth; it
starts `turnsToAct` turns from now and I discount it the way I discount every other future turn.*
Evaluating at `rangeWhenActing` rather than the current range is what preserves the closing gradient
for a squad that is out of reach today. The terminal is priced at Hold retention, because a squad that
has taken position stands and shoots.

**Test changes — two, both justified individually.**

- `BattleSquadPlannerTests.ChooseEngagementOption_LookaheadSeesOwnMovementInsideWeaponReach` — its
  first assertion (Hold's `future` SHRINKS under the honest engagement range, 0.38882 → 0.23541) was a
  property of the OLD terminal's formula, which penalized Hold for standing far from a range Hold by
  definition never closes to. Under 5d's shape the honest range makes Hold's terminal slightly LARGER
  here (1.28e-8 → 2.12e-8) — standing 200 yards off is worth almost nothing, and thirty-odd discounted
  turns of grinding at the effective range is worth almost nothing plus a little. Asserting the
  direction would now pin the shape 5d deliberately removed. The Phase 2 property itself (the
  lookahead can SEE its own movement) is carried entirely by the two following assertions, which are
  UNCHANGED and still discriminate. Replaced with an inequality that the arms differ at all.
- `BattleSquadPlannerTests.EngagedShooter_ShootsAgainstOneAttacker_ButReadiesMeleeAgainstThree` — a
  fixture retune, precedent exactly as in Phase 3's grenade test. Its own comment always called it a
  crossover scenario, and the graded metric moved the whole curve: both the shot AND the forfeited
  parry are now credited for the wounding they do, and melee at point blank hits far more often than a
  bulky rifle, so parry risk grew faster than shot value. At the old 2.5 damage the shooter drops the
  rifle even against a SINGLE attacker and the test stops discriminating at all. Compact Rifle damage
  2.5 → 4 restores the straddle at the ORIGINAL 1-versus-3 counts, so the property under test and the
  test's name are unchanged; only the point on the damage axis where the crossover lives moved.
- `RemovalRateTableTests.PairRemovalRate_RescalingIsCheapEnoughForTheLookahead` — guard raised
  20us → 50us, see Performance.

**Performance.** The recursion stayed closed-form; nothing in it touches the grid, the targeting stack
or wound state. `RateAtRange` costs (isolated, Debug, 5 terms): **non-degrading 560 → 680 ns/call,
degrading 2725 → 5458 ns/call**. The degrading path roughly doubled because the graded term adds two
normal CDFs and an exp per hit location; take-out and wound-progress were fused into ONE pass over the
location vector after a two-pass version measured 8278 ns. Marine small arms are non-degrading, so the
common case pays ~20%. Table build is unchanged in structure and now amortizes properly, since the
lookahead actually consumes the rows the immediate-fire pass already warmed. The full seeded battle in
`BattleAbandonedWoundedTests` runs in ~1.0 s.

**This benchmark is badly contended and should not be read as a latency budget.** The same build
measured 5458 ns/call alone and 22569 ns/call while the rest of the suite ran, in back-to-back runs.
The guard exists to catch the recursion ceasing to be closed-form, so it was raised to clear that noise.

**Left open, stated rather than worked around.**

- The reference scenario is a RECONSTRUCTION, not the traced battle. In it Hold beats CloseToContact by
  2.33 even at λ = 0, so it does not reproduce the trace's 0.018 margin in favour of closing. What it
  does reproduce is the pathology that matters — `outgoing` 0.009 against `future` 22.6 — and the λ = 0
  collapse. The Godot verification is what will confirm the real battle moves.
- `TotalRangedRemovalRate` re-sums the enemy's table row on every call; the range varies continuously
  so it is not memoizable by key without quantizing. Left alone: it is a handful of cells.
- Melee removal is still a capability proxy, so the ranged and melee halves of the lookahead are still
  quoted in different currencies even though the IMMEDIATE terms are now consistent. That is Phase 6.
- The `woundProgress` over-statement described in point 3 of the λ reasoning is handled by a scalar,
  not by modelling location-wise accumulation. A real fix would track which location the damage lands
  in, which is a battle-damage accumulator — the same thing Phase 3 of the battle-planning
  optimization plan deferred for the same reason.

### Phase 6 — Derived preferred band

With a smooth removal function, preferred range falls out of `removal(r) − incoming(r)` instead of
being authored. For a non-degrading weapon against a penetrable target, closing always improves
removal, so standoff is set by return fire — which is the correct physics.

Collapses `EstimateHitDistance`, `EstimateKillDistance`, `CalculateOptimalDistance`, and
`CalculateOpeningDistance` — four parallel approximations of the same quantity — onto one function.

Phase 2 already left the seam: replace the body of
`BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange` and no call site changes. Until it
is replaced, `EffectiveEngagementRange` equals reach for every non-degrading weapon against a target
that accuracy can already reach — which is most marine small arms.

**Done.** `OnlyWar.Tests.Battles` **484 passed**, `.Turns` **305 passed**, `.Missions` **162 passed**.
The seam held: no call site of `CalculateEffectiveEngagementRange` changed, exactly as the plan
predicted. **No seeded battle flipped a winner** — `BattleAbandonedWoundedTests` passes unchanged.

**The one model.** `Helpers/Battles/RangedEffectivenessCurve.cs`. Expected battle value removed per
turn as a smooth function of range, for a set of shooters against a scalar representative target:

```
removal(r) = Σ_shooter max_weapon  Phi((total0 + k·ln(2/r) − 10.5)/3) · removalFraction(r) · targetBV
```

gated to 0 beyond each shooter's own reach. The removal fraction is evaluated by
`BattleSquadPlanner.EvaluateRemovalFraction` itself, over a **single synthetic
`TakeOutLocationTerm`** built from the representative's scalar armour and constitution
(`K = effArmor + con/woundMultiplier`, `K_zero = effArmor`). Reusing that evaluator rather than
writing a second one is what makes the invariant hold here by construction and keeps λ shared.

*Why a second range model exists alongside Phase 4's table.* `SquadPairRemovalRate` is built from
REAL targets through the targeting stack and the grid, so it only exists inside `BattleSquadPlanner`
mid-battle. `BattleEngagementFrameBuilder` is static, RNG-free and grid-free, and mission setup runs
before a grid exists. The curve is the same shape and the same currency, computable where the range
question is actually asked. **The plan's "use `SquadPairRemovalRate.RateAtRange`" was not literally
possible at the seam** — that is the main thing Phase 6 found the plan got wrong.

**Sampling: 50 evaluations, 33 coarse + 17 refine.** The coarse pass spans `[1, reach]` and is a
GLOBAL sweep, not a hill climb, because `removal(r) − incoming(r)` is not guaranteed unimodal once a
degrading weapon's damage falloff and a melee arrival term are both present. The refine pass
subdivides the bracketing coarse interval, giving reach/512 resolution (~2 yards at a bolter's
1000). Each evaluation is one `ln`, one normal CDF, and — degrading weapons only — one more CDF pair;
no hit-location walk per sample, since the location vector is a single term built once at
construction. Built once per squad per turn, as Phase 4's constraint requires.

**A. The derived band.** `CalculateEffectiveEngagementRange` is now
`argmax over r of [ outgoing(r) − incomingRanged(r) − meleeThreat/(1 + turnsUntilContact(r)) ]`.

- `outgoing` is our curve against the able-soldier-weighted mean opponent, as before.
- `incomingRanged` is the opposing force's curve fired at a representative member of us — symmetric.
- The melee term is `meleeBV · MeleeContactRemovalFraction / (1 + max(0, r−1.5)/closingSpeed)`. It
  equals `BattleSquadPlanner.MeleeRemovalRate` exactly at contact and decays with approach time.
  This is the one place a turns-based discount is legitimate (Phase 3): a charge really does pay off
  later. The `0.13` coefficient moved to `BattleModifiersUtil.MeleeContactRemovalFraction` so the
  two sites cannot disagree.
- **Our own melee is deliberately absent from the outgoing half.** The question is where a squad
  wants to stand and shoot; a contact spike would drag every mixed squad to contact. A squad that
  would rather fight in melee is routed by `IsContactSeeking` in `Baseline`, which does not read
  this quantity.

Degenerate cases, all returning 0 ("nothing to stand off for, close"): no usable ranged weapon; no
opposing force; and **impenetrable target, checked EXPLICITLY** rather than left to the argmax —
against an impenetrable target the net score is just `−incoming(r)`, whose argmax is maximum range,
i.e. a "preferred range" that implies plinking from standoff is worthwhile. The invariant says it is
not.

*The invariant needed a floor, which the plan did not anticipate.* The old `EstimateKillDistance`
got a crisp −1 from `DamageMultiplier·6 < effectiveArmor`, a top-of-the-die cutoff. A Gaussian curve
has no such cutoff: armour it cannot beat still yields a ~1e-4 tail. `NegligibleRemovalFraction =
0.001` — a thousandth of the target's battle value per turn, i.e. a thousand-turn fight — is where
that tail stops counting as a reason to choose a range. Expressed as a fraction of target BV, not an
absolute, because the invariant is about rate.

**B. What collapsed onto what.**

| was | now |
|---|---|
| `EstimateHitDistance` | deleted; its to-hit assembly is the curve's `BaseHitTotal`, minus the inversion |
| `EstimateKillDistance` | deleted; take-out is the curve's removal fraction, from the real evaluator |
| `CalculateOptimalDistance` | thin derivation: `curve.SaturationRange(0.5)`. Signature and return conventions (−1 no hands, 0 no standoff) unchanged, so no caller moved |
| `CalculateOpeningDistance` | deleted |
| `BattleSquad.GetPreferredOpeningRange` | **kept as a named seam**, now delegating to `GetPreferredEngagementRange` |

*The stale-constraint finding, and it is stale twice over.* The doc comment said
`CalculateOpeningDistance` was "kept separate from `CalculateOptimalDistance` because squad
imminence depends on that function's meaning" — that wording still survives in `OnlyWar_TDD.md:781`
and has been corrected there. In the code, Phase 2 had already silently re-pointed it at
`BattleSquadCapabilityProfile.EffectiveEngagementRange (via GetPreferredEngagementRange)`.
**Both are moot.** `CalculateSquadImminence` was deleted in Phase 3 — `grep -i imminence Helpers/`
returns only cross-reference comments — and Phase 6 severed `EffectiveEngagementRange`'s dependency
on `GetPreferredEngagementRange` entirely. Neither constraint constrains anything.

The real reason the fourth function existed was a **discontinuity**: `EstimateHitDistance` returned a
hard 0 whenever the to-hit total failed to clear 10.5, so a "no standoff" answer had to be
disambiguated by cause — hit-limited weapons wanted to open far and plink, wound-limited weapons
wanted to open close. A curve has no cliff: a 20%-at-400-yards hit chance scores 20%, and a 0 can
now only mean "nothing removable at any range", which is exactly the case that wanted to open close
anyway. The disambiguation was a workaround for the artifact Phase 6 removed.

*What was deliberately KEPT separate, and why.* `GetPreferredOpeningRange` survives because the
question is real, but the distinction that survives is about RETURN FIRE, not about weapons. Standoff
mid-fight maximizes `removal − incoming` and needs to know what the enemy is and how fast it closes;
at the opening of an engagement that posture does not exist yet, so opening range asks the same curve
the un-opposed question, "where is this force still effective". Same model, one term fewer.

**`SaturationFraction = 0.5`, and it is a knob.** "Still at least half as effective here as at its
best." 0.9 was tried first and pulled degrading weapons in to near contact — their damage falloff is
linear and 90% of peak is a narrow window — which understates reach for exactly the force-level gates
(`BattleTurnResolver.WorthwhileRangedReach`) that exist to stop a force believing it can shoot from
anywhere. At 0.5 the derivation nearly reproduces the old numbers wherever the old model was not
degenerate (bolter vs light infantry 235.5 → 235; autocannon vs light infantry 278.6 → 275) and
fixes them where it was.

**C. Consumers.**

- **`EvaluatePursuitContactProgress` — kept on reach, and Phase 6 strengthens that case.** The band
  is `removal − incoming`, and a withdrawing quarry's incoming is precisely what a pursuer has
  already decided to accept; pursuing to the standoff band would halt the chase at a distance chosen
  by a threat model that does not apply. The band can also legitimately be 0, or several hundred
  yards, so pursuit progress would mean something different per matchup. Reach is the one threshold
  with a fixed meaning for a pursuit: past it there is no shot at all.
- **`Baseline` — kept on reach.** It answers "am I roughly in the fight", a wide-hysteresis
  containment question, and it is the fallback the scored options are compared against. Putting the
  derived band in the FALLBACK and then scoring movement relative to it double-counts the same
  judgement. `AggregateRemovalRate` no longer exists (Phase 5), so that half of the Phase 2 note is
  obsolete.

**Before/after `EffectiveEngagementRange`** (10 marines, Dexterity 15.4, Gun bonus 1.4; enemies
melee-only. BEFORE column re-runs the deleted `min(EstimateHitDistance, EstimateKillDistance)` on the
identical fixture):

| force | reach | BEFORE | AFTER | opening range |
|---|---|---|---|---|
| bolter vs 2 Carnifexes (armour 20, con 224, size 8) | 1000 | **1000** | **172.7** | 1000 |
| bolter vs 2 light infantry (armour 3, con 12, size 1) | 1000 | 235.5 | 8.8 | 235 |
| autocannon (degrading) vs Carnifexes | 1600 | 1349 | 26.0 | 553 |
| autocannon vs light infantry | 1600 | 278.6 | 10.4 | 275 |

The bolter-vs-Carnifex row is the reference case: it moved from **reach** to a real derived standoff,
which is the flip Phase 2 asked for. The light-infantry rows are near contact honestly — a size-1
target at 200 yards is a poor shot and two guardsmen threaten almost nothing on arrival. The
autocannon rows are the degrading-damage case: its damage falls linearly to nothing at 1600, so
against 224-constitution chitin it can only hurt a Carnifex from very close, and the old 1349 was the
one-third-quantile approximation flattering it.

**Opening ranges for Phase 7** — `MissionOpeningRange.Interpolate` averages
`GetPreferredOpeningRange`, the fourth column above. For the reference case (bolter marines vs
melee-only Tyranids) it is **1000 yards, reach-capped**, which matches this plan's own Phase 7
prediction ("500–980 yards; bolter `MaximumRange` 1000 caps it, not accuracy"). **Two things Phase 7
must weigh:**

1. Removing `MaxFormationStandoff = 200` will open marine-vs-Tyranid ambushes at ~1000 yards while
   the same marines' derived mid-fight standoff is ~173. That is ~140 turns of approach before the
   fight starts. Phase 3 removed the imminence defect that made this catastrophic, but the approach
   is still long.
2. `SaturationFraction` is a single named constant with this trade-off documented beside it. At 0.9
   the same reference case opens at **395** yards instead of 1000, much closer to the mid-fight band.
   If Phase 7 wants the clamp gone without a 1000-yard walk, that constant — not a new clamp — is the
   place to spend the tuning.

**Test changes — five, each justified individually. No bulk update to green.**

- `CapabilityProfile_NonDegradingWeaponEffectiveRangeStillCollapsesOntoReach` → renamed
  `...EffectiveEngagementRangeIsDerivedNotReach` and **flipped**, as this plan instructed. Same
  fixture, opposite assertion: the band is now strictly inside reach.
- `EstimateKillDistance_MultiHitWeaponRetainsStandoffRange` → `OptimalDistance_MultiHitDegrading
  WeaponRetainsStandoffRange`. Property unchanged and still worth pinning; only the function that
  answers it moved.
- `EstimateKillDistance_OneShotCaseKeepsExistingQuantileRange` → **deleted**, replaced by
  `OptimalDistance_DegradingWeaponStandsCloserAgainstAToughterTarget`. It asserted 1040–1060 yards, a
  number produced entirely by the `4.25f` one-third-quantile divisor. That divisor IS the
  approximation Phase 6 removed; there is no behaviour under the assertion to re-express, only the
  constant it pinned. The surviving property — a degrading weapon's standoff shrinks against a
  tougher target — is asserted directly.
- `EstimateKillDistance_WeaponThatCannotPenetrateReturnsMinusOne` →
  `OptimalDistance_WeaponThatCannotPenetrateHasNoStandoffRange`. The old magic −1 was swallowed by
  `min()`; the invariant is now stated directly as 0.
- `CalculateOpeningDistance_HitLimitedHeavyWeaponOpensFarWhileOptimalIsZero` →
  `OpeningDistance_HitLimitedHeavyWeaponStillOpensFar`. Its `optimal == 0` half was the artifact
  itself. The surviving half — this weapon opens far rather than being dragged to a close start — is
  asserted, and now `optimal` is nonzero too.
- `CalculateOpeningDistance_WoundLimitedWeaponStaysCloseLikeOptimal` → renamed only; both values are
  still 0.
- `CapabilityProfile_EffectiveEngagementRangeIsDistinctFromWeaponReach` — its last assertion required
  `EffectiveEngagementRange == GetPreferredEngagementRange(...)`, true only because the Phase 2 seam
  DELEGATED to that method. Pinning it would pin the delegation, i.e. the thing that had to go. The
  property the test is named for is asserted directly instead: make the opposition tougher and
  require the standoff to grow.
- `ChooseEngagementOption_LookaheadSeesOwnMovementInsideWeaponReach` — **one of three arms dropped.**
  `Future(after, CloseToContact) > Future(after, Hold)` no longer holds, for a fixture-specific
  reason. The Long Reach Rifle has accuracy 6 against a SIZE 1 target, so at the fixture's 200 yards
  its hit probability is ~0.0005 and the derived band is 1 (contact) — the honest answer. But
  CloseToContact is scored at zero outgoing retention against melee incoming that switches on inside
  1.5, and both arms are now ~1e-7 because the terminal is discounted across ~50 approach turns.
  Asserting the sign of a difference between two effectively-zero numbers pins fixture noise. The
  named property — the lookahead can SEE its own movement — is carried by the two surviving
  assertions, which still discriminate sharply: the spread widened 2.15e-9 → 1.22e-7, a factor of 57.
- `MissionOpeningRangeTests` fixture retune, precedent as in Phases 3 and 5: the ranged squad's
  soldiers get Dexterity 20. The default test soldier has Dexterity 10 and no skill points, and the
  test rifle has accuracy 0, RoF 1 and damage degrading linearly to nothing over 100 yards — its
  honest standoff is **1.6 yards**, so both sides' preferences collapsed to 0 and there was nothing
  to interpolate between. That is the model no longer inheriting the one-third-quantile
  approximation, which had handed that rifle ~29 yards it had not earned. A shooter who can shoot
  gets a real ~24-yard band from the same rifle and the property under test is untouched.

**Left open.**

- The scalar representative target is an approximation the old primitives also made
  (`EstimateKillDistance` used the same `con/woundMultiplier + effectiveArmor`), but it can disagree
  with `SquadPairRemovalRate`'s real per-hit-location vector on how tough a specific body is. Both
  models are honest about range; they are not guaranteed to agree on magnitude.
- `SaturationFraction` and `NegligibleRemovalFraction` are un-swept. See the Phase 7 note above.
- Godot verification is still outstanding from Phase 5 and now also covers this phase.

### Phase 7 — Remove `MaxFormationStandoff`

Last, and only after Phase 3.

Uncapped, the marines' preferred opening range here is 500–980 yards (bolter `MaximumRange` 1000 caps
it, not accuracy — the hit-limited distance against a Carnifex computes to 1887 yards). At 1000 yards
today's imminence is `1/126` — five times worse than at 200. Removing the clamp before Phase 3 gives
the same walk-forward decision with shooting valued 5× lower and ~125 turns of approach instead of 25.

The clamp is currently masking the imminence defect.

## Open questions

- ~~**Best-target definition.**~~ **Resolved in Phase 5.** `argmax removal` is the rule, and it does
  not regress the case take-out probability was introduced to fix: the graded term goes to ~0 exactly
  where take-out does, proven both on the wound model and end-to-end through the reference geometry.
- ~~**Pair weights vs best target.**~~ **Resolved in Phase 5, asymmetrically** — argmax for outgoing,
  `PairWeights` for incoming. See that section for why the flank-threat concern does not bite.
- ~~**λ calibration target.**~~ **Used.** "Tens of turns" was one of the four inputs to λ = 0.5
  (~38 turns for the reference force). It was not the decisive one; the λ = 0 collapse was.

## Unverified inferences

Recorded so they are re-checked rather than inherited:

- The 60× gap between Rostzin Squad's `outgoing` (0.063) and Rostadi/Scharel's (0.001) is *inferred*
  to be target assignment — Rostzin drawing the Lictor (BV 37, size 3.06) while the other two drew
  Carnifexes (BV 30, size 8.0, far tougher). Back-solved from the trace; targets are not logged at
  this level. Confirm before relying on it.
- The 200-yard standoff is *inferred* to be the clamp binding. `Math.Clamp` emits exactly 200 for any
  input ≥ 200, and three of the four possible representative draws give 1000, but a Lictor draw would
  produce ~206 unclamped. Not provable from the log.
- Marine BV values and squad composition are reconstructed from `Battle XP` rosters and
  `WITHDRAW_EVAL start_bv`, not read from the save.
