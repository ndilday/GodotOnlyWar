# Battle Value Calibration

**Status: implemented reference record (2026-11-18).** Distilled from `StrategicAndTacticalValue.md` and
`GauntletV2.md`, both deleted on 2026-11-18 under the promotion rule in `Design/README.md`. The architecture is in
`OnlyWar_TDD.md` §6.6; the player-facing rule is in `OnlyWar_PRD.md` §4.14; open work is in
`OnlyWar_PRD.md` §5.8; the commands are in `Tools/OnlyWar.Diagnostics/README.md`. This file keeps what
those cannot carry: why the value was split, the measurement method and its pitfalls, the findings that
shaped the planner, and the alternatives that failed. The stored values are in the rules database and are
deliberately not copied here. Planner-side calibration (opening range, break model, contact rate, speed) is
in `ForecastCalibration.md`.

## 1. One value became two (product owner, 2026-10-31)

1. **Strategic value** is stored (`SquadTemplateElement.StrategicValuePerSoldier`, read as
   `ISoldier.StrategicValue`). It sets what a unit costs to field and how the strategic layer judges balance:
   force generation, military-strength pools and allocation, strategic combat, mission sizing, post-battle
   losses, threat assessment. The gauntlet measures it from battles, in one pass.
2. **Tactical value** is computed in the battle from the battle state (`TacticalValueTable`, held in
   `BattlePlanningContext.TacticalValues`, read as `BattleSoldier.PlanningValue`). It drives target
   selection, charge or shoot, opening range and withdrawal. **The planner reads no strategic value.**
   `BattleState` and `BattleRoundMetrics` read the strategic value, because they score the battle and do not
   plan it. Fixtures built without a coordinator fall back to the strategic value.

Why one value failed. Each of these five faults came from a planner priced in the database value:

- **The measurement was circular.** The planner priced every trade in database value and the gauntlet
  measured the database value from planner battles. The Brood Brother Weapon Squad (database 16, measured
  about 105) never closed on Termagaunts because its own low value made its losses look cheap and the
  Termagaunts' Close look bad: at 105 in a scratch copy the same duel decided in 88% of battles, against 0%.
  The fix attempted first was a damped fixed-point iteration (about 2.5 days a pass, started and stopped on
  2026-10-31). Splitting the value removed the loop and made one pass enough.
- **Armour and weapon additions double counted.** `EffectiveBattleValueCalculator` added armour/2 and a
  per-weapon bonus to a value the gauntlet had measured with that kit already. The additions were fixed
  amounts, so cheap troops were priced 2-3 times their value and elite troops about 1.2 times. A Tactical
  marine was 7.6 PDF troopers to the planner and 17.8 in the database.
- **The gauntlet calibrated a different planner.** The additions applied only to a squad with an itemized
  equipment source, which a live campaign has and the harness did not.
- **The exchange rate was a leftover.** `EngagementCurrency.EnemyValueRate` converted enemy value into ours,
  needed while values were not on one scale (headcount parity until 2026-10-12). Once the gauntlet put them
  on one scale it was 1.
- **The engagement mode was a fixed squad property.** Contact seeking came from `SoldierTemplate.MeleeFraction`
  against a 0.55 threshold, and every Ork Boy squad shared one template at 0.5. Slugga Boyz were never contact
  seekers, so 150 Sluggas opened at 854 yards against 120 Neophytes and took autogun fire for 427 turns before
  the first melee.

Built in six phases, 2026-11-01 to 11-02: baseline duels, `TacticalValueTable`, switch the planner's currency
and delete `EffectiveBattleValueCalculator` and `EngagementCurrency`, armament-and-target engagement mode,
remove `MeleeFraction` (template column dropped), rename `BvPerSoldier` to `StrategicValuePerSoldier`
(about 1,100 renames in 122 files, by script; save-file columns renamed too, backward compatibility waived).
In the battle module "battle value" now means the tactical value. `BattleValueCalculator` keeps its name: it is
the offline Lanchester estimate, not the stored value.

## 2. The tactical value

For soldier j against the force opposing him, at the start of each turn:

- **a_j** is expected take-outs a turn: the better of the sustained aimed ranged cycle
  (`RangedEffectivenessCurve`, against the target's real hit locations, at the current separation or the
  shooter's reach) and one turn of melee at contact (`MeleeStrikeEstimator.EstimateTurnTakeOutProbability`,
  no value in it).
- **d_j** is the turns he survives under ONE average enemy soldier's fire or melee. Not under the whole
  force: that made the value depend on headcount.
- **u_j = sqrt(a_j x d_j)**, then the **side factor** `v_j = u_j x (s_enemy / s_own)^(1/4)`, s the side's mean
  u. Measured against the real opponent, u is in units of the enemy's average, so the ratio of the two sides'
  u is the SQUARE of the true ratio. With the quarter power, uniform sides give the square law's
  `sqrt(alpha_m / alpha_b)` Mob models per marine, whatever either side's numbers (1 Mob squad and 6 Mob
  squads gave ratios within 25%). Scale is unit: the geometric mean of the side means is 1.
- **Self-reference.** a counts the enemy models j removes, and a take-out of a valuable model counts more.
  Each enemy is weighted by his previous-turn value over the enemy mean (the first turn uniform), so each
  turn is one step of a fixed-point iteration that runs with the battle. Three turns settle within 10%.
  `OpeningExchangeForecast.Build` assigns values before turn 1 through `TacticalValueTable.EnsureAssigned`.
- **Melee share** (`TacticalMeleeShare`, read by `BattleEngagementFrameBuilder.SoldierCombatShares`) is melee
  over melee plus the BAND-MEAN ranged rate (`SustainedAt` at 1/8, 3/8, 5/8 and 7/8 of reach). Point-blank
  ranged made an Ork Slugga 0.49, though a Slugga does not fight at five yards with it; the band mean gives
  0.98. Gate: usable melee means a melee weapon, or no gun at all. Thresholds stay 0.55 (contact seeking) and
  0.35 (fire support). The share is force-level, the value-weighted mean over enemy groups, so a squad has
  one mode a turn. **The original design offered two continuations (charge, hold) per target squad. That was
  not built:** per-target modes need per-target continuations in the potential.
- **Not built:** a leader term (morale and command value of a soldier's loss). Leaders get almost no value
  from a and d. Revisit if the planner stops shooting leaders (PRD §5.8).
- **Cost:** 258 rate evaluations for 1 Tactical squad v 4 Mob squads. Counters:
  `TacticalValueTable.TotalRateEvaluations` and `TotalBuildTicks`.

**Three rules adopted 2026-11-15** (product owner), after Tactical marines and Warriors stood off for the whole
battle. The value mixed two geometries: each rate was the better of the ranged cycle at the present
separation and melee at contact, so at range a Warrior was priced as if he reached contact and was hard to
hurt because the marines were still far. In that currency his own losses were dear and the marines' cheap, so
his effective engagement range went to maximum reach (750 yd with the battle's values, 1 yd with the
opening's). Each rule has a duel switch (`Tools/OnlyWar.Diagnostics/README.md`):

1. **Contact durability** (`TacticalValueTable.MeleeDurabilityAtContact`). A soldier whose melee is at least
   his ranged cycle over its band takes his durability against that enemy at the enemy's contact rate, not
   at the separation. **One-sided on purpose:** a version that also read the gunner side at contact moved
   the marine-to-Mob ratio to 3.2 (the gauntlet says about 24).
2. **No fist credit for gunners** (`UnarmedMeleeWithGun = false`). A soldier with a gun and no melee weapon
   is credited no unarmed melee, as `MeleeShares` and `BattleEngagementFrameBuilder` already ruled. Rule 1
   alone did not end the stall, because the marines' fists beat their Boltguns at range. A Warrior is now
   valued by his gun only, though in the engine he still strikes with his talons.
3. **Indifference floor in the cheaper side's soldiers** (`SquadEngagementPolicy.IndifferenceInCheaperSoldiers`).
   Rule 2 made Tactical v 3 PDF a stand-off: the autoguns cannot hurt a marine at 800 yd, so his durability sat
   at the 1,000-turn cap and he was worth 10-100 PDF troopers. A floor of 5% of the squad's own mean value
   made every PDF kill "about nothing", so `RunToward` scored best on every sampled turn (0.065-0.13 over
   `Hold`) and the squad held. The floor is now quoted per soldier of the cheaper side. Two experiments that
   did not help were removed: the strategic value when either side has no fire at the separation, and
   durability read at no less than the enemy's band-mean fire.

Decided shares, A holds / B holds / undecided (Warriors 8 seeds, controls 24, alternating roles):

| Duel | Old | Rule 1 | Rules 1+2 | Rules 1+2+3 |
|---|---|---|---|---|
| Tactical v 2 Warriors | 0/17/83 | 0/12/88 | 0/100/0 | 0/100/0 |
| Scouts v 2 Warriors | 33/17/50 | 38/38/25 | 38/62/0 | 38/62/0 |
| PDF v Hormagaunts | 33/67/0 | | 38/62/0 | 29/71/0 |
| Tactical v 2 Slugga Mobs | 42/58/0 | | 42/58/0 | 54/46/0 |
| 10 Mobs v Tactical | 100/0/0 | | 75/25/0 | 62/38/0 |
| Tactical v 3 PDF | 96/0/4 | | 54/0/46 | 100/0/0 |

**Phase checks that mattered.** Phase 3 decided the Neophyte-Slugga stalemate (94% undecided with contact at
turn 459 became 0% undecided with contact at turn 16) and kept PDF standing against Hormagaunts. Phase 2 did
NOT fix Weapon Squad v Termagaunts (100% undecided, before and after): the Termagaunts' only weapon is a
Devourer whose take-out per hit is 0.0000 at 610 yd, the squad's preferred 608 yd is where it can hurt them
and they cannot hurt it, and the walk to contact is about 100 turns against a 0.97 discount. That is
approach economics, not currency; it is §5 below.

## 3. The gauntlet (measurement method)

`Tools/OnlyWar.Diagnostics` fights squads at several force ratios and fits a probit on every battle,
`P(A holds) = Phi(kappa x (u_A - u_B - ln(models B / models A)))`, the lognormal-ratio model the strategic
resolver uses. Method as it stands after v2:

- **One scale from all matchups.** Every entry fights a panel of squads from the other sides, and one
  least-squares fit sets every value: `ln b(A,B) = u(B) - u(A) + e(A,B)`, b the cell's break-even, PDF fixed
  at 5. No squad is a reference whose value must absorb the others' matchups. A non-transitive triangle shows
  as residuals across its three cells instead of moving a reference. The fitter checks that the cells connect
  every squad to the PDF anchor.
- **Weights are 1 / (se^2 + tau^2).** tau is the matchup standard deviation, estimated so weighted residuals
  match their degrees of freedom (Paule-Mandel). Precision weights alone gave a dispersion of 168 on the
  `*9` cells and pulled Tactical to 55, because a few very sharp cells outweighed the rest.
- **Bounds and brackets.** A one-sided or extrapolated cell enters as a censored bound (active set: held at its
  bound only while the values would break it); a separated cell with wins and losses is a bracket at the
  gap's midpoint. A cell with no usable probit is aimed at the break-even the scale predicts from both
  squads' other cells; a prediction more than x2 past the largest battle the cap allows is left as a bound,
  unfought. The first Ork-stage run spent hours a battle on one-sided cells stepped out to 1,000 models
  (744 Gretchin against 16 Brood Brothers, still lost); that is why.
- **Stages.** Stage 1 fights five ratios at 8 seeds around the prior; stage 2 rounds of 4 seeds at three sizes
  per unresolved cell, stopping at se(ln break-even) under 0.2, at most 6 rounds. `GAUNTLET_CAP_MAX` 400.
- **Opening range is the game's.** Each battle opens where a reciprocal assault would (the longer of the two
  preferences, `ForecastCalibration.md` §1), with equipment reallocation, burrowers placed as the game does,
  and both sides Attacker. Fixed 200 yards was the v1 design.
- **Turn-cap battles score by casualties**: A's share of the casualties as a fraction of each side lost, with
  0.05 added to both so an uneventful battle stays near 0.5 (`BattleRow.ScoreOf`; stored rows are rescored on
  read). Before this a capped battle counted 0.5 and flattened the cell.
- **Panel (12 squads, product owner 2026-10-27).** Per side a gunline, a soft melee squad and a hard target:
  Imperial PDF Infantry / Assault / Tactical; Tyranids Termagaunts / Hormagaunts / Warriors; Cult Brood
  Brothers / Acolyte Hybrids / Aberrants; Orks Shoota Boyz / Slugga Boyz / Meganobz. PDF and Marines share a
  side (`GAUNTLET_ALLIES`); the Tyranids and the Cult are hostile in the game, so separate sides.
- **Joint refit, never per-stage values.** The scale LEVEL moves with which entries are in the fit: adding the
  eight other Ork squads moved every panel value down 20-35% with identical panel battles, and the Tyranid
  stage moved them 40% up. The PDF anchor does 5-7 times better than the scale predicts against the Cult's
  melee squads, so its worth depends on whom the other entries fight. Write values once, from one refit
  over every battles file of the same code key. An open idea, not built: anchor on the mean of several
  gunlines.
- **Do not give panel squads new priors in a faction stage.** A prior sets the stage-1 sizes and a battle is
  cached only at the same sizes; new priors planned 5,532 fresh battles against 2,568.
- **Writing values.** One factor per squad so the full-size squad's mean equals the fitted value, applied to
  every element, rounded to integers (minimum 1); PDF stays as the anchor. A diff against a backup must show
  no other row or column changed. The value belongs on the squad ELEMENT, not the soldier template
  (decision 2026-10-27): an Ork Nob is 10 in Nobz and 131 in Meganobz. Splitting soldier templates was
  rejected, because it means one template per element even where two are identical, or a change of armament
  silently needing a new template.
- **Cache key.** Battles are keyed by rules-database hash and a code hash. `GAUNTLET_DB_KEY` keeps the old
  database key and is safe only for a write that cannot alter a battle (`StrategicValuePerSoldier`: the engine
  reads it only for within-side weights, which are equal inside a cell).

### Runs

| Run | Date | Battles (fresh) | Time | Result |
|---|---|---|---|---|
| `v2-panel12` | 10-27 | 5,396 | 32.7 h | tau 0.76, 944 stalemates (17%) in four pairs |
| Ork, Tyranid, Cult, Insurrectionist stages | 10-29 to 10-31 | 3,564 / 2,096 / 1,756 / 1,456 | 8.6 / 3.2 / 12.6 / 27.9 h | tau 0.70-0.81; levels disagreed (above) |
| `v2-joint0` refit | 10-31 | 14,268 | | tau 0.75; largest residuals the stalemate cells bbw-gaunt, bbw-war |
| `v2-it1` | 10-31 | stopped after 3 h | | superseded by the value split |
| `v2-p6` | 11-02 to 11-04 | 14,048 | 47.4 h | 11% at the turn limit, tau 0.70; written 11-05 |
| `v2-p6b` | to 11-08 | 13,644 | 41.4 h | turn limit 6.0%, withdrawal 12.7%, tau 0.80 |
| `v2-p7` | to 11-13 | 12,752 | 27.1 h | turn limit 4.6%, kappa 0.916, tau 0.68; written 11-13 |
| `v2-p8` | to 11-15 | 7,308 of 19,540 | 26.5 h | adds 16 entries (46 total); chapter-service squads left at old values |
| `v2-p9` | 11-15 | 1,100 of 19,608 | 1.3 h | refights the two Warbosses after a data fix; written 11-15 |
| **`v2-p10`** | to 11-18 | 19,056 (all fresh) | over a day | kappa 0.973, tau 0.653, 27 cells unresolved; **written 11-18** |

`v2-p10` is the accepted run, with the three rules of §2 on. Its files are kept in `GauntletResults/v2-p10`;
`battles-v2-p10.tsv` is the cache for the next run and is valid only for this engine. The values are in the
rules database: 72 statements, 61 element values changed, no other column or row.

## 4. Findings that shaped the planner or the values

- **Matchup structure is real and one value cannot hold it.** One value per squad misses a typical matchup
  by x1.9 to x2.1 (tau 0.63-0.81). The largest residuals are rock-paper-scissors: Meganobz against Brood
  Brothers x17 (lasguns against mega armour), the PDF 5-7 times better than the scale against Aberrants and
  Acolytes. Strategic code keeps the scalar; matchup work is PRD §5.8.
- **Stalemate pairs are a gunline against a squad that will not close** (Brood Brothers-Warriors,
  Tactical-Warriors, Termagaunts-PDF, PDF-Warriors, the two Weapon-Squad cells). Their cells are flat, so
  their break-evens say little, and a value resting on them is not trustworthy. A refit without those pairs
  moved most values down about 13% together and Tactical 24%. Always show a refit with and without
  turn-limit battles before writing a value.
- **Weapon Squad.** Two heavy weapons that out-range the enemy kill slowly, score well on casualty share and
  never lose the field; against marines and Meganobz it loses in under 20 turns. Value written from the
  no-stale refit (36) on 2026-11-05, then from the joint fit on 2026-11-13, after the stalemate rules.
- **Scouts beat Tactical because of Sniper Rifles.** A player squad in the gauntlet rolls its quotas from
  minimum to maximum, so a Scout Squad takes 0-8 Optional Armament (Sniper Rifle or Shotgun) and a Tactical
  Squad 0-1 special and 0-1 heavy. With four Sniper Rifles the Scouts held 75% against two Warrior squads;
  on the base kit both squads stood off. The Sniper Rifle (accuracy 9 against the Boltgun's 3, armour x0.5,
  wound x3, reach 2,000) decides it. Stealth is not read in battle. Scouts are now level with Tactical.
- **Ork boss data error (found from `v2-p8`).** The Warboss wore Mega Armor and the 'Eavy Warboss 'Eavy Armor
  with a Choppa, so the 'Eavy Warboss came out below the Warboss in every cell. Corrected to Orkish Skin and
  Big Choppa. The Megaboss below the Warboss was not an error: the Power Klaw's half attack speed loses to
  swarms (Hormagaunts 61 against 139) and wins against marines.
- **Leaders are lower limits.** HQ squads, bosses and synapse creatures measured alone get no value for their
  effect on nearby squads. The HQ-and-bodyguard design (§6) was not built.
- **Ork Nob and Boy values spread x13 and x3.9** across the squads that share a template (10 in Nobz, 131 in
  Meganobz, 103 in Flash Gitz), which is the argument for element-level storage.
- **Gauntlet v1 had a placement bias.** Mirror battles gave the Attacker slot 56.5% of wins until the harness
  called the game's `AnnihilationPlacer` (`Tools/OnlyWar.Diagnostics/README.md`, Placement).
- **Effect on generation (2026-11-05).** The new values changed the battles that sector generation fights,
  unevenly: the full sector on seed 3 fought 124 then 99 battles but 4,236 then 5,065 turns, and the small
  save-test sector 79 then 108 battles and 2,101 then 5,862 turns (one save test 1m28s to 7m41s). Battles of
  20 soldiers or fewer a side last longer. The values exposed two latent crashes on seed 7 (HQ squads placed
  in the same cells behind a short kill zone, and a population conversion that required an Imperial entry in
  a region that had lost it), both fixed with tests.

## 5. Stalemates: evidence, rules and rejected alternatives (2026-11-06 to 11-09)

Evidence from `v2-p6`: the turn limit hit 115 of 9,053 battles that opened under 300 yd and 898 of 1,615 that
opened at 600-899 yd; 875 of the 1,598 turn-limit battles stopped on the inert rule. Three behaviours made them:

1. **Nobody can hurt anybody and nobody closes.** Long-reach weapons run out of rounds, Stay is worth 0 and
   Close is below 0, so every squad holds until the inert rule.
2. **A squad that expects a net loss delays it.** Phi is negative whichever way it goes, and a discounted loss
   is smaller when it is later, so a step back scores above a hold. Without the discount the Termagaunts do
   walk in and do rout, so refusing to close is correct.
3. **The losing side cannot leave.** Every gauntlet battle was Aggressive, which has no withdrawal threshold.

**The outnumbered side asks for the farthest opening.** When every opening is a loss, the smallest loss is
where nobody can hurt anybody, and the reciprocal-assault rule opens at the longer preference. The forecast
was largely right: at 450+ yd one turn ahead, forecast over actual loss was 0.95 (Tactical), 1.19 (Weapon
Squad), 1.41 (Flash Gitz). The Termagaunts expecting a quarter of the loss they took (0.27) was not traced.

**Adopted** (all on in the game; switches in the Tools README):

- Stand-off close (`ApplyStandoffCommitment`) and no-win withdrawal (patience 100,
  `VoluntaryWithdrawalReason.NoWayToWin`), 2026-11-06. Rule text is in `BattleLogic.md` §6. The first
  withdrawal version had no loss test and failed: Mobs left after killing 82% of the marines, Termagaunts
  left at turn 10 with no losses. Patience 30 and 100 decide the same share; 100 loses less. Limit: the
  forecast does not know ammunition, so Warriors leave the Flash Gitz before the Snazzguns run dry.
- **Forecast fixes, 2026-11-08/09.** Fire on the approach and ammunition in the potential
  (`FireOnApproach`, `AmmunitionInPotential`). `PairRemovalTerm.RechooseBurstByRange`: each term had kept the
  burst and round scarcity chosen at the range the table was built at, so at 732 yd a lasgun hit that removes
  almost nothing (4.5 x 10^10 hits a kill) set scarcity to 1 and the table credited one round at 183 yd too
  (0.13 a turn against 1.76 correctly). Halfway-stop bug fixed (once the half stop beat the run, the quarter
  stop was cut from it and never tried). **`RoundsLeaveWithSoldiers`:** a dead soldier's rounds had stayed in
  the budget, so survivors inherited them; 4 Brood Brothers v Warriors at 195 yd lost because 46 of them had
  each fired exactly 320 rounds, their whole load, by turn 85 (not morale).
- **Scarcity margin 1.5** (`RangedTargetSelector.ScarcityMargin`): the rounds a soldier needs are multiplied
  before the price is set. 320 rounds against a need of about 315 is a knife edge with no margin for losses.
  At 1.5 the Brood Brothers fired in 27% of soldier-turns against 12% and finished the Warriors before they
  ran dry; 4 v 1 at 195 yd went from 12/50/38 to 62/38/0.
- **Narrowed follow-the-plan** (`FollowClosePlan` with `FollowPlanOnlyWhenStayLoses`): when Stay itself loses
  and the root Close beats it, only the option that moves as the Close moves is offered. It repaired the Mobs
  (100/0 against 12/75/12 with the margin alone) without driving the PDF into the Hormagaunts' charge.

**Not adopted, with the reason:**

- **Halfway stops** (`DUEL_CLOSE_STOPS=2`, off in the game). Alone they changed nothing, because the stop's
  payoff came after a 90-turn run.
- **Forecast discount 1 in the potential.** It makes Close win, but a step costs nothing in Phi, so the option
  with the best immediate fire wins, the walk at about 1 yd a turn, which bleeds under fire on a 430-yard
  crossing and routs. The 0.97 discount stays in the potential; the opening forecast has none.
- **Follow-the-plan, first version** (applied whatever Stay was worth). It won 9 v 1 in every battle but
  flipped PDF v Hormagaunts from 62/38 to 12/88, because the PDF followed their Close toward the charging
  Hormagaunts (moved 46% of turns, mean range 69 yd against 141).
- **Either stalemate rule alone.** The close rule alone decided 75% of Tactical v Warriors and 0% of Flash
  Gitz v Warriors; with the withdrawal rule both decide 100% (off: 50% and 0%). Brood Brothers v Warriors
  (12 v 1) stayed at 38% in every arm.
- **The 4 Brood Brothers v 1 Warriors stall is correct.** Even opened at 195 yd, 4 lose to 1 Warrior squad
  (12/50/38); the forecast's Phi +22 there was optimistic, and the ammunition fix closed it.

Still open after adoption: 9 Brood Brothers v 1 Warriors from 750 yd stalls on a slow win (BB lost 0%, Warriors
4% at the cap): the Brood Brothers' Stay is a slow win, not a loss, so the narrowed rule does not apply.

## 6. Designed, not built

- **HQ squads and bodyguards by marginal effect.** Fight a fixed troop group of three ordinary squads in four
  configurations on paired seeds (troops; + HQ; + guard; + both), and split each leader's effect by averaging
  the two orders it can be added in (a Shapley split): `V(HQ) = 1/2 [(T+HQ - T) + (T+HQ+G - T+G)]`, the
  interaction reported, an effect below the measurement's resolution reported as "below resolution". The
  group stays fixed and only the panel's count varies, because copying the group would copy the HQ.
- **Marine option variants.** One entry per option in each weapon group at its maximum count, averaged with
  equal weight (30 entries, about 12,000 battles, 19-29 h). The Command Weapon group gets no variants.
- **Encounter-frequency weighting** of cells: start unweighted, show both if it moves values.
- **Shared soldier templates:** weight by how often the generator fields each squad, and split a template only
  when its squads disagree a lot (element storage made this moot for the Orks).
- **Rejected for this study, kept for the next one:** a fixed share of BV for an HQ troop group (the Tyranid
  HQs are large next to their troops) and a fixed reference pair (no panel).
