# Forecast Calibration

**Status: implemented reference record (2026-11-18).** Distilled from `OpeningRangeRefactor.md`,
`BreakCliffSmoothing.md`, `SmoothFireAllocation.md`, `ContactRateCalibration.md`, `MobBreakCalibration.md`,
`HorizonSideClock.md` and `CoarseForecastTables.md`, all deleted on 2026-11-18 under the promotion rule in
`Design/README.md`. The mechanisms are in `OnlyWar_TDD.md` §6.6 and `BattleLogic.md` §5; this file keeps the
measurements that justify each constant and the changes that were tried and removed. Value calibration (the
gauntlet, strategic against tactical value, the stalemate rules) is in `BattleValueCalibration.md`.

October to November 2026 was one long loop: measure the planner's forecast against what battles do, find the
term that disagrees, fix that term. **The lesson repeated four times: the first suspect was wrong.** The
Kommando break model was blamed and the horizon was at fault; the PDF fire rate was blamed and the fire
cadence was right; a 1.9x removal gap was a 500-hit cap in the audit tool; a "wrong-sign" forecast trace had
the sides swapped. Check the measuring tool and the trace orientation before changing the planner.

The tools are in `Tools/OnlyWar.Diagnostics/README.md`: `ApproachCurveDiagnostics`,
`ForecastAgainstBattleDiagnostics`, `ShotAuditDiagnostics`, `HardnessDiagnostics`, `RateTableDiagnostics`.

## 1. Opening range from the forecast (phases 0-5, 2026-10-22 to 10-24)

The old preferred opening was the shortest range at which expected fire removes the enemy's break share
before contact, floored by a "useful band". Measured against battles it was wrong three ways:

- **The useful band is the wrong quantity.** What matters against a closing enemy is cumulative removal over
  the whole approach. Casualties the PDF inflicts rise to about 300 yd against melee and then flatten
  (90 Hormagaunts v 40 PDF: 7% at 12-30 yd, 21% at 50-75, 32% at 100, 37% at 200, 45% at 300 and 450, 36% at
  600). The model put the PDF's band at 37-78 yd. In a traced 600-yd battle the PDF hit 1.2% of 1,700 rounds
  fired beyond 330 yd and 10% of 500 inside 100.
- **The point-blank volley was far too optimistic.** The model expected one volley to remove 75% of
  Genestealers; the PDF removed 8% at 1 yd and 18% at 12.
- **A contact seeker's preference of 0 is not a preference.** It halved every gunline's opening once averaged.
  A squad with no ranged weapon now has NO preference (`CalculatePreferredOpeningRangeOrNone` is null).

The answer is the argmax of the opening forecast (`BattleLogic.md` §5.3). Phase 0 measured 8 matchups at 12-600
yd (`GauntletResults/approach-phase0`): the opening changes the winner in 6 of 8 (only Genestealers and the
Carnifex are insensitive); 600 yd is often worse than 300; ammunition does run short before contact (Shoota
Boyz 7-23% of rounds left at contact at 300-600 yd).

Calibrated best opening against measured (yd): PDF v Hormagaunts 450 / 314, PDF v Genestealers 300 / 299-354,
PDF v Kommandos 50 / 41-51, Tactical v PDF 450 / 333, Tactical v Shoota Boyz 600 (ladder end) / 1,128,
Tactical v Mob 450 / 268, Tactical v Hormagaunts 450 / 350, Orks 12-25 / 2. The Carnifex curve is flat (it holds
76-94% at every opening), so its 1,277 is not a failure. Acceptance criteria and their fates: best opening
within one ladder step (holds, bar the Carnifex); no decisive Genestealer volley (holds: 25% against 19% at
12 yd); inflicted casualties within x1.5 (**cannot hold where battles end in a break**: a broken squad keeps
losing men in battle while the forecast counts its value as gone at the break).

Two changes to `OpeningForecastOptions` came from the curves: **no turn discount** (with 0.97 a kill 60 turns
away counted a sixth, and every gunline's best opening stopped at about a third of the measured one) and
**planner-priced fire** (`CurveFireModel.PlannerPriced`: each round charged at the shot planner's scarcity
price and the planner's own held-aim cycle; with rounds free, Tactical squads ran dry by 200 yd where battles
showed 83% left at contact). The argmax must hold on the unlucky path too
(`OpeningExchangeForecast.EvaluateUnlucky`), because the shortest point of a flat plateau is its edge.

**Assault rule.** Options: A keep the midpoint (a gunline against melee opens near 150 yd); **B, a reciprocal
assault opens at the longer preference (adopted, product owner 2026-10-22)**; C open at sighting range and let
preferences only shape behaviour (right physically, needs terrain and line of sight; B is its open-ground
version). B makes melee forces weaker in reciprocal assaults and changes every campaign assault between a
gunline and a melee force. Consequence found later: an outnumbered side asks for its farthest opening and
rule B grants it (`BattleValueCalibration.md` §5).

**Tests rewritten with the sufficiency sweep** (each states its reason in place): the Xibarrus Zeta hand-built
guard became `PreferredOpeningRange_AgainstMeleeOnlyMonsters_StandsWellOffContact` on real templates (the
hand-built monsters died before arriving); `PreferredOpeningRange_RisesWithOpposingConstitution` and the Grist
Nine Epsilon break-limit test were removed with the sweep they pinned;
`OpeningRangeForceShareTests` now asserts the forecast, not the chosen opening, because force share flattens
the ladder into a plateau where the unlucky tie-break decides (230 yd in force, 144 alone).

## 2. Fire as soldiers really deliver it (2026-10-23)

- **Rounds were spent three times too fast** in the forecast (PDF v Hormagaunts at 300 yd: 8% left at
  contact against 66% measured). The aimed shot's accuracy and +1 had been priced into the un-aimed shot; fixed.
- **Point blank was too strong** because the curve used one synthetic location; the forecast now prices a hit
  on a representative soldier's real locations (`RangedEffectivenessCurve.Build`'s `representativeTarget`).
- **Removal per hit is right.** Autogun on fresh Genestealers through the engine: 8.2 hits per take-out
  against 7.7 in the planner.
- **The gap was shot timing.** PDF v Genestealers from 100 yd: 99% of volleys followed an aim, but the bonus
  at the shot averaged 0.2-0.7 and under 1% had the full bonus of 3; soldiers fire after one to three aim
  turns while the table priced the full-aim cycle.
- **Held-aim rule built, then reverted** (`value(b) >= value(b+j) - g*j`). Each shot got better but a quarter
  of the PDF's aim turns were wasted (128 of 526 at 150 yd), so Hormagaunts held 81/88/75/25% at 100/150/200/
  300 yd against 69/56/56/12 before. Early firing had been hiding that loss.
- **Why aims are wasted (traced):** 160 of 503 aim turns (32%) never reached a shot; **113 had a target that
  fell to a squadmate's shot** (up to 15 PDF soldiers held aims on one Hormagaunt), 23 were switches after
  `RunToward` cleared aims, 24 lapsed.
- **Target-loss in the fire timing, built and reverted.** A per-target "chance it is gone" from the squad's
  average rate changed almost nothing, because the waste is focus fire, which an average cannot see.
- **Adopted instead: price the rate table at the fire soldiers deliver.** `PairRemovalTerm.SustainedRateAt`
  replays the planner's own cycle (`RangedTargetSelector.PlannerCycle`) and charges focus-fire waste
  (`SquadPairRemovalRate.SustainedRateAtRange` groups terms by target; shared targets get `q` = other shooters'
  removal over his value, cap 0.9). Decisions do not change; only what a turn of standing fire is worth.
- **Mob support and coercion in the break profile**, found from a Kommando trace: at 25 yd every squad held
  rather than charge because the forecast broke the Kommandos on turn 3 of the run, where the live check gives
  Orks mob support and a leader who cancels a rout. `ProfileOf` now takes the faction morale rules. (§5 shows
  the horizon, not the break model, was the larger cause of that Kommando case; the change stays.)

## 3. The break cliff (2026-10-23)

Phi jumped 20-30 battle value across a few yards (PDF v 3 Hormagaunt broods: smooth 80-112 yd, then +21 between
112 and 116 and a sawtooth after), and a squad compares options by differences of Phi, so a jump reads as a
real gain: a PDF squad ran toward a charging horde. Cause: the forecast follows ONE mean path and decides on it
a race between two events that each move most of the value at once, the enemy breaking (hazard 0 to 1 within a
turn or two, removing the lane's value at once) and contact (a fixed turn, then 100 BV a turn). A few yards
move the break one turn earlier or contact one turn later. Battles are not like that (the same opening gives
each outcome some of the time). Level breaks did not help: they add the scatter of WHICH soldiers are hit,
`sqrt(L(1-L)/n)`, far narrower than the scatter of HOW MUCH fire lands.

- **Option B, expected value over the exchange's luck, built** (`EngagementExchangeForecast.RunExpected`,
  `LuckSpread` 0.5): three forecasts at luck `-s, 0, +s` (own fire x e^x, theirs x e^-x), weights 1/6, 2/3, 1/6
  (three-point Gauss-Hermite); side points run only where a break or contact is in reach. `s = 0` reproduces
  `Run` to the bit. **It shrank the jumps and did not remove them**; from s = 0.5 a sawtooth appeared at 36-72 yd.
  Rejected options: widening the hazard (smooths the break only), spreading a break over turns (softens size,
  not position, no battle meaning). The opening forecast keeps the luck average; the potential does not.
- **The main cause was fire allocation:** the rate table gave each soldier one term against his single best
  target, so a few yards of geometry moved whole soldiers' fire from one brood to another, and a brood that
  drew most of it broke early. Soft allocation (`PairRemovalRateTable.FireShares`, temperature 0.25) plus
  reallocating fire from broken lanes (`ShiftFireFromBrokenLanes`) was **built and reverted the same day**: Phi
  went to about +150 everywhere (the PDF expected all three broods to break before contact) and spot duels got
  worse for the PDF (Hormagaunts hold 81/69/25% against 69/56/12). The fire pricing of §2 stays.
- Neither closed the jumps. After the contact frontage of §4 the 112-116 yd step became two smaller steps and
  the 76-80 yd step (+10) remained, from fire allocation. That item was not passed in full and nobody
  re-measured it after the later forecast changes.

## 4. The contact rate (2026-10-23)

At contact the PDF's forecast put its own loss at 90% in one turn; in battle it built over five turns (23, 65,
78, 83, 84%). The old rate summed every attacker's strikes against his four nearest targets, capped only by the
target squad's value.

Measured (`ForecastAgainstBattleDiagnostics` FRONTAGE table, 6 battles each): an outnumbering side strikes with
**about 1.5 soldiers per defender** (Hormagaunts on PDF 1.22-1.42 once formed, Ork Mob on Tactical 1.23-1.70,
Genestealers and Kommandos 0.27-0.47); and **a striker reaches fewer than one defender a turn**, however many
attacks he has (Genestealers land 2.9 hits a turn but on 0.76 defenders). Removal per hit is right
(`HITS_TO_KILL_MELEE`: 1.2 hits per take-out for Rending Claws on PDF in engine and planner).

Built (`MeleeStrikeEstimator.EstimateContactStrikes`, `ContactStrikes.RemovalRate`): each attacker's turn is
priced against ONE defender as a take-out probability t; the strongest `min(attackers x share, 1.5 x defenders x
frontage share)` engage, each meeting a defender at random, so the share removed is `1 - exp(-sum t / D)`
(predicts defenders struck: Genestealers 9.5 against 8.2, Hormagaunts 21.6 against 20.8). In
`EngagementPotential` the enemy squads that reach this squad share its frontage by number and each fights it
with its force share, with the loss priced once and shared out (a defender met by two broods is taken out
once). Before this each lane priced both squads' whole melee as if they fought alone: three broods struck one
PDF squad in full. Checks: PDF loss over the first three contact turns forecast 25/66/66% against battle
19/59/72%. **Rejected:** a ramp (two constants, no physical meaning, does not scale with squad size); spreading
each attacker's hazard over the defenders (credits a near-certain kill's overkill to others, changed almost
nothing). The Kommando 25 yd regression that followed was not the melee rate: do not raise it to correct it.

## 5. Horizon, mob breaks and the side clock (2026-10-23/24)

- **The hypothesis "the forecast breaks a mob too early" was wrong.** The 43% plateau of the Kommandos' own loss
  was the end of the forecast's HORIZON (4.4 turns at 25 yd), not a break; the audit table that read it follows
  a forecast past its horizon. Within the horizon the forecast's break chance (2.3% by turn 4-5) matched the
  routed share (4.2%). The break formula is right at the live inputs (PDF rout results 17/45% live against
  18/48% forecast). Not built: check cadence, leader survival, support as a function of losses (the known gaps
  if mob breaks need work).
- **The cause: the horizon cut off the charge.** At 25 yd the horizon was 4.4 turns and the Close needed 4.4 turns
  to arrive, so it spent the whole horizon running under fire (1.4 removed against 26.6 lost) and the melee was
  never priced. The old melee rate, three times too high, had paid off inside the last fraction of a turn and
  hidden it. Fix: the Close continuation gets the horizon at its destination PLUS the turns to get there
  (`EngagementPotential.CloseHorizonAddsTravel`). Kommandos v PDF at 12/25/50 yd: hold 94/6/0% became 100/69/6%;
  Hormagaunt, Genestealer and Tactical controls unchanged.
- **Routing friends.** With the leader as he was, the forecast's check still routed less than the live one;
  adding the live check's routing-friends shock closed it (PDF v Warriors at 50-75% losses: 39.5% live, 25.9%
  forecast, 37.5% with it). Built as `EngagementExchangeForecast.RoutingFriends`/`PriceRoutingFriends` (those
  routing at the start plus, since the forecast assumes the side fights as the squad does, the squad's own
  chance to have broken by then). Leaders: PDF sergeants die in 13 of 36 squads at 57% cumulative loss, Ork Nobs
  in 5 of 48 at 88%; "leader lost with the share lost" is pessimistic for Nobs.
- **A side-level own clock was tested and rejected.** The horizon's own clock is per squad
  (`T = min(enemy clock, own clock)`, TDD §6.6), but a squad's own withdrawal point does not exist in battle
  (`BattleSquad.ShouldContinueMission` is called only between battles; withdrawal is side-level in
  `BattleForceEvaluator`). Four-arm cross test (32 battles an arm, the opponent held fixed): the side clock cost
  the Kommandos 12-15 points (59 to 44% and 53 to 41%), gunlines gained 0-6 within noise, Tactical lost 3-6.
  Likely reason: for a contact seeker the short per-squad horizon of a battered squad stands in for the cost of
  its coming rout, which the forecast prices only partly. The per-squad clock stays. **The question of whether
  the own clock should follow side-level withdrawal is closed.**
- **"Withdraw when the planner expects to lose"** was proposed for the PDF-under-fire case (2026-10-26) and
  became the no-win withdrawal of `BattleValueCalibration.md` §5.

## 6. Checks of the planner's fire model against the engine (2026-10-26)

- **Softening fit** (`HardnessDiagnostics.Hardness_FitsSofteningAcrossRanges`: 1,089 cells, every attack against
  ten entries at 30/60/120/250/450 yd and melee, 60 targets a cell). Planner over engine, removal per hit = take-
  out + s x softening, q = 2, s = 1:

  | Range | Cells | Bias | rms factor |
  |---|---|---|---|
  | melee | 110 | 0.93 | 1.16 |
  | 30 / 60 / 120 yd | 200 each | 0.92 | 1.22 |
  | 250 yd | 186 | 0.91 | 1.27 |
  | 450 yd | 173 | 0.95 | 1.43 |

  Take-out alone is far worse (rms 2.3-3.6), and no (q, s) on the grid beats the present one by much. Outliers
  too deadly: frag grenades against Warriors (x0.67 at every range) and the Big Shoota against the Carnifex.
  About 20 cells at 250-450 yd are ones the planner prices as impossible and the engine kills with rare rolls.
  The softening is calibrated and unchanged.
- **The 1.9x gap was a measuring error.** `ShotAuditDiagnostics`' hits-to-kill stopped each target at 500 hits;
  at 250 yd 59% of Warriors were still standing, and since hits grow deadlier as wounds build, the cut string
  overstated hits per take-out. Run to the end (cap 20,000) it reads 664 (`HardnessDiagnostics` 608) against the
  planner's 515: x1.2-1.3, not x1.9.
- **Fire cadence is right.** Volleys forecast against volleys fired, per root forecast (10 PDF squads v 1
  Warrior brood): PDF 300-450 yd 10.1% against 9.9%, 450+ yd 18.3% against 17.6%; Warriors 44.4% against 40.4%
  and 37.9% against 37.5%. The low firing rate is the aim cycle and half the PDF having no target in reach, and
  the forecast already has both. This closed the PDF-fire question; no fire fix is due.
- **Who moves under fire.** The 70 PDF squad-turns at 200-300 yd chose RunToward 25, routing RunToward 22, Hold
  19, Jog/StepForward 3, StepBack 1. The 28 real advances were three out-ranged squads with outgoing 0 and
  incoming 0.2-0.4 a turn, where each step to a nearer separation raised Phi slightly (RunToward -25.2 against
  Hold -26.1 at 297 yd) until a local maximum at 284 yd. An out-ranged Normal squad has no fast way out of
  range (StepBack is a walk), and the gain a step (0.1-1 BV against Phi of -26 to -63) is small. This is the
  stand-off of `BattleValueCalibration.md` §5.

## 7. Planner speed: coarse forecast tables (2026-11-10 to 11-12)

The forecast only ranks a few options and the doctrine mask removes most of them, but computed at a precision the
ranking cannot use. `Governance_IsDeterministicForSeed` took 13 min on 2026-11-05 and 45-52 min beside another
run; battle LENGTH did not grow, the planner's cost per turn did (8-10 squads a side cost 1-3.4 s a turn on 12
threads, about half the test time). The 8 v 3 Neophyte v PDF duel made 166,000 removal-fraction calls a turn
(63% from the un-aimed rate, 31% from the range curve), each walking every hit location with up to nine normal
CDFs. Principle: coarse where the value changes slowly or the decision cannot see the difference, exact where
it changes quickly, and ONE currency so the planner, forecast and gauntlet stay comparable.

**Step 1, removal fraction on 50-yard nodes, linear interpolation (`RemovalFractionTable`, 2026-11-10).**
Interpolation, never steps: steps make Phi flat inside a band and jump at its edge, and Close and Stay differ by
one move. The to-hit side stays exact (it changes fast at short range). 166,000 calls a turn became 18,700.

**Step 2, one term per shooter group, and shared tables (2026-11-11).** `PairRemovalRateTable.ShooterGroup`
merges shooters whose best shots land in the same squad with the same weapon and burst, to-hit within 0.5,
ranges within 10% (at least 5 yd), take-out and wound fill each within 0.02, target value within 5%, reach
within 1 yd, aim accuracy within 0.25 and rounds within 25%. `RemovalFractionTable.For` shares one table per
weapon and location vector (process-wide, cleared at 4,096). **Grouping alone left the solves where they were
(20,900 a turn); sharing took them to 12 and the 8 v 3 duel from 8m33s to 1m15s alone, same 439-turn battle to
the last digit.** Most of the gain was the shared tables, not the grouping. The tolerances are judgment calls
and the duels did not move.

**Step 3, burst length per range band: tried 2026-11-11, OFF by default** (`PairRemovalTerm.CycleNodeRatio`,
`DUEL_CYCLE_NODE_RATIO`). Doctrine could not supply the burst: `Faction.FireDiscipline` only scales the lane-spread
preference, and a doctrine burst would forecast fire the soldiers do not fire (the 2026-11-08 error). Built
instead: search at nodes by RATIO (to-hit moves with 2.47 ln(range), so 1.25 gives nodes 0.55 points apart) and
interpolate. No speed gain, and it moved outcomes (10 Mobs v Tactical 100/0 became 75/25), so the cycle search
is no longer the main cost.

**Step 4, fewer aim plans: not built.** It cuts the same search step 3 did, and one aim plan broke the forecast
in October 2026 (`PairRemovalTerm.SustainedRateAt`).

Later, all exact (same numbers to the digit): `RangeRates.Average`/`Interpolate` quadratic to binary search,
then `AverageAll`/`AtAll` reading all four arrays in one pass; the live shot evaluator reads shared tables on
**10-yard** nodes (50 put the planner's take-out count 12-13% high at 80 yd for a degrading rifle); the bounded
caches kept their own counter because `ConcurrentDictionary.Count` takes every lock (5.9% self time);
`ScoreBursts` memoized per shot; the cycle search keeps one slot per yard (256-slot hash collided: 4,820
searches a turn against 1,917); prebuilt `Regex` in `SquadDesignationFormatter`; the burst tail cutoff
(`RemovalMath.BurstTailTolerance` 1e-4, only 12% fewer CDF steps because most bursts were short). 8 v 8 duel:
123 to 217 turns/min with the shot tables; CDF steps 7.0 billion to 4.9 billion.

**Profile shares, the evidence to compare against** (first 6 min of `Governance_IsDeterministicForSeed`, Total
CPU): `EngagementExchangeForecast.Run` 17.9-24.8%, `ExpectedBurstRemovalFraction` 15.7-17.6%, the cycle search
13.6-14.2%, `ApproximateNormalCDF` 10-11% self (already a 4,097-entry table: its cost is the call count).
`Governance_IsDeterministicForSeed` alone spread 11m42s to 16m7s across changes that each made the 8 v 8 duel
faster, so **its wall clock is load, not code; compare by profile shares or counts.**

Key duels (24-72 seeds, within noise through steps 1-2): Tactical v Sluggas 60/40, PDF v Hormagaunts 29-33/67-71,
10 Mobs v Tactical 100/0, Tactical v 3 PDF 97/0/3, 4 Brood Brother squads v Warriors at 195 yd 41-50/41-56/0-9
(one standard error is 9 points at 32 seeds). At 8 and 24 seeds two of these first looked biased against the
shooter and both differences went away with more seeds: **do not conclude from 8 seeds.**

## 8. Open

Tracked in `OnlyWar_PRD.md` §5.8: whether 0.5 is the right to-hit tolerance for grouping shooters (or only
identical soldiers should group); the remaining steps in §7 if speed matters again; the ammunition and
enemy-rounds limits of the forecast (a broken share's rounds stay with the unbroken share, and a squad's rounds
are one pool so a heavy weapon's count toward the lasguns' fire).
