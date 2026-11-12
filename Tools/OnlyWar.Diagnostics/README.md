# OnlyWar Diagnostics

Opt-in balance and Battle Value calibration tools, kept apart from the correctness suite in
`OnlyWar.Tests`. They are xUnit facts so they share the test workflow (`dotnet test` with `-e`
settings), but every fact returns at once unless its `RUN_*` variable is set. The only tests that
run by default are the fitter's unit tests (`ProbitFitTests`, well under a second).

They share two files with the test suite by link rather than copy: the rules-database locator
(`OnlyWar.Tests/Fixtures/RulesDatabaseFixture.cs`) and the `SharedState` collection
(`OnlyWar.Tests/SharedStateCollection.cs`). Calibration method and the rules it follows:
`Design/Reference/BattleLogic.md` §4; open calibration work: `OnlyWar_PRD.md` §5.8.

Run from the repository root, for example:

```powershell
dotnet test Tools\OnlyWar.Diagnostics --no-build --nologo -v q --filter "FullyQualifiedName~SquadDuelDiagnostics" -e RUN_DUEL_DIAGNOSTIC=1 -e DUEL_SEEDS=25 -e "DUEL_SCENARIOS=1 vs 34,n=9,x=4 @ 200"
```

The tools fight squads from the rules database against each other over many seeds and report who
holds the field and the casualties, so Battle Value can be set from fights rather than from a formula.
Both sides are deployed by the game's own `AnnihilationPlacer`, exactly as a meeting engagement is.

- `SquadDuelDiagnostics` runs hand-picked scenarios. Set `RUN_DUEL_DIAGNOSTIC=1` and
  `DUEL_SCENARIOS` (for example `"1 vs 34,n=9,x=4 @ 200"`: template id, `n` trims a squad, `x`
  repeats it, `ws` hands pooled weapon sets to the best shooters, `@` is the opening range in yards).
  The table lands in `%TEMP%\GodotOnlyWar\squad-duel.txt`, or in the file `DUEL_OUT` names, and the
  battles per minute in the same name with `.throughput.txt`. `DUEL_ROLES` picks which side is the
  battle's Attacker: `a-attacks` (default), `b-attacks` or `alternate` (A attacks on odd seeds). The
  table is always from A's point of view. `DUEL_CLOSE_TRAVEL=0` prices the Close continuation over
  the plain horizon, without its travel turns, as before 2026-10-23. `DUEL_STANDOFF_CLOSE=0` and
  `DUEL_NO_WIN_WITHDRAW=0` turn off the two stalemate rules of 2026-11-06, which are on in the game
  (`DUEL_NO_WIN_PATIENCE` sets the second rule's patience, default 100). Three forecast changes of
  2026-11-08 are on in the game and turn off in duels: `DUEL_FIRE_ON_APPROACH=0` drops the Close
  approaches that fire at a jog or a walk, `DUEL_POTENTIAL_AMMO=0` makes rounds free in the potential,
  and `DUEL_BURST_BY_RANGE=0` makes the removal-rate table keep each shot's burst and round price
  from the range the table was built at. `DUEL_CLOSE_STOPS=2` tries the halfway stops (off in the
  game). For the first battle of a run, `DUEL_TRACE` writes the
  engine's planning log and `DUEL_WOUND_LOG` writes every wound side A takes (attack kind, weapon,
  damage, hit location, damage-to-Constitution ratio and wound level), a per-weapon summary, and the
  final wound totals of A's hit locations: the tool for "what actually killed it". `DUEL_SHOT_LOG`
  writes every volley of that battle, both sides (rounds planned and fired, aim multiplier, the
  turns the shooter had just spent aiming at that target, hits, range) and a per-weapon summary split
  by aimed and un-aimed: the tool for "did the aim reach the shot".
- All the tools run battles in parallel, `DUEL_WORKERS` at a time (default half the logical processors, 6 on a 12-thread machine; `1` runs them one after
  another). Each battle has its own seeded random streams and results are summed in seed order, so
  the worker count does not change the result.
- `SquadLadderDiagnostics` finds, for each pair of squads, the break-even force ratio, which is the
  count of one squad that makes the other hold the field half the time. Set `RUN_LADDER=1` and
  `LADDER_ENTRIES` (for example `"pdf=34|hyb=26|tac=1|ork=39"`; a squad with no `n=` is built at its
  template maximum, including templates that would otherwise roll a size); the other settings are
  documented on the class. Output goes to `%TEMP%\GodotOnlyWar\ladder`: `probes-TAG.tsv` caches every
  probe by rules-database hash only (delete it after a code change), `pairs-TAG.tsv` is the pair
  table, and `throughput-TAG.txt` reports battles per minute. Probes run one after another, because
  each one decides where the next goes; only the battles inside a probe run in parallel. The
  gauntlet below has superseded it.
- `SquadGauntletDiagnostics` is the fixed-design calibration. **v2 (2026-10-21,
  `Design/Active/GauntletV2.md`):** set `GAUNTLET_PANEL` instead of `GAUNTLET_REFS`, and every entry
  fights every panel squad of another side (`GAUNTLET_ALLIES`, default `0+1`: PDF with Marines; since
  2026-10-27 the Tyranids and the Cult are separate sides, as they are hostile in the game); battles open where a reciprocal assault would (`GAUNTLET_RANGE` defaults to
  `assault`: the two forces' preferred opening ranges, equipment reallocation, burrowers, both sides
  Attacker; give a number for the old fixed range); a one-sided cell's cap rises to `GAUNTLET_CAP_MAX`
  (default 1000); and the report leads with **single-scale values**, a least-squares fit over every
  "ok" cell weighted by 1 / (se² + τ²), where τ is the matchup standard deviation. The v1 design
  below still works with `GAUNTLET_REFS` and a numeric `GAUNTLET_RANGE`.
  v1: set `RUN_GAUNTLET=1`,
  `GAUNTLET_ENTRIES` (same `key=spec|...` syntax as the ladder) and `GAUNTLET_REFS` (comma-separated
  entry keys): every entry fights every reference over a spread of force ratios (stage 1: five ratios
  around the prior, 8 seeds each; stage 2, since 2026-10-25, in rounds of 4 seeds at three ratios per
  unresolved cell, where its own fit says 25%, 50% and 75%, or, for a one-sided cell, around the
  break-even the single scale predicts from both squads' other matchups; a cell stops when
  se(ln break-even) is under `GAUNTLET_TARGET_SE` (0.2) or its predicted break-even is more than ×2
  past the largest battle `GAUNTLET_CAP_MAX` allows, and then counts as a bound in the fit;
  `GAUNTLET_ROUNDS`, default 6), and a probit model is fitted to all the
  battles. All battles of a stage run in one pool, so long battles do not leave cores idle. Output in
  `%TEMP%\GodotOnlyWar\gauntlet` (or `GAUNTLET_OUT`): `battles-TAG.tsv` is one row per battle, keyed
  by rules-database hash, a code hash (`OnlyWar.Battles.dll` plus `SquadDuelDiagnostics.HarnessVersion`,
  which must be bumped when the harness's placement or squad building changes), range, aggression,
  squads, seed and, when A defends, a role suffix. Every `battles-*.tsv` in the folder is read back, so
  runs resume and a data or code change invalidates old battles. `GAUNTLET_ROLES` picks the battle
  roles: `alternate` (default), `a-attacks` or `b-attacks`; with both roles present the global fit adds
  one attacker-role coefficient, which should come out near zero. `fit-TAG.txt`, `cells-TAG.tsv` and
  `values-TAG.tsv` are the fit (per-model values with PDF = 5, the noise of a fight, per-cell
  break-evens, and a chi-square test of whether matchup structure exists beyond one value per entry);
  `throughput-TAG.txt` counts fresh battles, and `lsq-TAG.tsv` holds the single-scale values. The other
  settings (`GAUNTLET_PRIOR`, `_RANGE`, `_AGGR`, `_CAP`, `_CAP_MAX`, `_TAG`, `_MIRROR`, `_ALLIES`) are
  documented on the class. A battle row records the range it actually opened at (`openingRange`); an
  assault battle's configured range is stored as 0. To refit stored battles without fighting
  any, set `RUN_GAUNTLET_FIT=1` and `GAUNTLET_IN` (a battles file, or a folder meaning every
  `battles-*.tsv` in it).
- `HardnessDiagnostics.Hardness_FitsSofteningAcrossRanges` (`RUN_HARDNESS_FIT=1`, same
  `HARDNESS_ENTRIES`, `HARDNESS_RANGES` default `30|60|120|250|450`) sets the engine's hits to
  disable against the planner's over a grid of the combining exponent and a softening scale, overall
  and per range, and lists every cell. 1,089 cells for ten entries in about half an hour.
- `HardnessDiagnostics` measures how hard each target is for each attacker's weapons: it fires hits
  through the engine's own hit pipeline (hit location, damage roll, armor, `WoundResolver`) at fresh
  copies of the target until it is no longer combat-effective, and reports mean hits to disable,
  per attack and per squad, plus each squad's effectiveness relative to the first entry. Hits, not
  shots: to-hit chance is excluded. Set `RUN_HARDNESS=1` and `HARDNESS_ENTRIES` (`key=spec|...`); the
  other settings (`HARDNESS_TRIALS`, `_RANGE`, `_CAP`, `_OUT`) are documented on the class. No
  battles; a few minutes for seven entries. The report ends with the planner's own estimate of the
  same hits to disable (`RemovalMath`, averaged over the same targets) at several settings of its
  location-combining exponent, with the error per setting: the calibration of
  `RemovalMath.LocationFillCombineExponent`. `HARDNESS_EXPLAIN="attacker:Weapon Name:target"` adds,
  for one cell, why each simulated target left the fight, the counter value each location gained per
  hit, and the planner's per-location fill rates.
- `RateTableDiagnostics` prints the engagement planner's squad-pair removal-rate table for two squads
  deployed as the duel deploys them, factor by factor: un-aimed and one-aim hit chances, the bursts
  the shot planner chooses for each, take-out and softening per hit, hits per kill, and the un-aimed
  and sustained rates. It then prices one standing turn both ways for every squad, shooter by
  shooter: the table's sustained rate (what the exchange forecast credits) against the readiness of
  the Hold option's Aim (what the option's immediate term credits), with the shot planner's own aimed
  shots in between, gross and net of the ammunition charge, the aim length each prefers, and the
  gross removal of the plan the charge picks. Set `RUN_RATE_TABLE=1` and `RATE_TABLE_SCENARIO`
  (duel syntax, for example `"26 vs 4 @ 150"`); `RATE_TABLE_SEED` and `RATE_TABLE_OUT` are
  optional. No battles. The same report prints each squad's options and the curve its effective
  engagement range is the argmax of. Its second fact, `PhiSweep`, rebuilds the scenario at every
  opening range in `PHI_SWEEP` (`"from:to:step"`) and prints each side-A squad's root engagement
  potential and chosen option: the tool for "does the forecast jump". Set `RUN_PHI_SWEEP=1`
  (`PHI_LEVEL_BREAKS=1` prices the potential's breaks by level for that sweep).
- More planner switches for every tool that fights battles, all adopted in the game on 2026-11-09:
  `DUEL_FOLLOW_PLAN=0` turns off the rule that a Normal squad whose Close plan wins, while standing
  loses, moves at that plan's speed (`DUEL_FOLLOW_ANY_STAY=1` applies it whatever Stay is worth);
  `DUEL_ROUNDS_WITH_SOLDIERS=0` makes the forecast keep dead soldiers' rounds in the budget; and
  `DUEL_REMOVAL_NODES` sets the yards between the nodes of the removal fraction per hit (default 50 since 2026-11-10, interpolated linearly; `0` solves every range);
  `DUEL_CYCLE_GRID` sets the range step in yards on which the removal-rate table solves each soldier's fire cycle (default 1 since 2026-11-10; `0` solves at the exact range);
  `DUEL_GROUP_SHOOTERS=0` builds one removal-rate term per soldier instead of one per group of interchangeable shooters (grouping is the default since 2026-11-11);
  `DUEL_CYCLE_NODE_RATIO` sets the range ratio between the nodes at which the fire-cycle search runs, interpolated between them (default `0`, which searches at every yard of `DUEL_CYCLE_GRID`; 1.25 was tried on 2026-11-11 and moved the duels without a speed gain);
  `DUEL_SHOT_REMOVAL_TABLES=0` makes the live shot evaluator walk the target's hit locations at the exact range instead of reading shared tables (tables are the default since 2026-11-11), and `DUEL_SHOT_REMOVAL_NODES` sets those tables' node spacing in yards (default 10; the forecast's tables keep `DUEL_REMOVAL_NODES`);
  `DUEL_BURST_TAIL_TOLERANCE` sets the share of a burst's score its remaining rounds may leave out before the burst integral stops (default `1e-4` since 2026-11-12; `0` runs every round to the deep tail, as before);
  `DUEL_SHARE_REMOVAL_TABLES=0` makes every term and range curve build its own removal-fraction table instead of sharing one per weapon and target location vector (sharing is the default since 2026-11-11); and
  `DUEL_SCARCITY_MARGIN` sets the factor on the rounds a soldier needs before his rounds are priced
  (default 1.5; 1 is the rule before). The rate-table and opening-range tools do not read these
  switches.
- The duel's `DUEL_SHOT_LOG` also ends with `AIM` lines: aim turns spent and wasted, split into target
  gone, switched while it stood, and lapsed; the mean distances of switches with the first 20 as
  samples; and the planner's own reasons for dropping held aims (`RangedTargetSelector.DroppedAimSink`).
- `ApproachCurveDiagnostics.ApproachCurves_ForecastBesideMeasured` (`RUN_APPROACH_FORECAST`) prints the
  opening forecast beside a measured curve table; `APPROACH_FIRE` (`planner`, the default, `aimed` or
  `unaimed`) and `APPROACH_DISCOUNT` (default 1) pick the forecast settings to compare.
- `OpeningRangeDiagnostics` prints each squad's preferred opening range for a duel scenario, with the
  opening forecast's net value (expected and unlucky) at a ladder of openings and the assault
  opening (rule B: the longer preference). Set `RUN_OPENING_RANGE=1` and `OPENING_SCENARIO`. No
  battles.
- `ApproachCurveDiagnostics` (the opening-range refactor, `Design/Active/OpeningRangeRefactor.md`).
  `RUN_APPROACH_CURVES=1` fights a panel of matchups (`APPROACH_PANEL`) at a ladder of forced assault
  openings (`APPROACH_RANGES`, `APPROACH_SEEDS`) and writes casualties, holds, contact and rounds left
  at contact to `approach-curves.tsv`/`.txt` after every point (8.5 hours for the default panel; the
  phase 0 result is in `GauntletResults/approach-phase0`). `RUN_APPROACH_FORECAST=1` fights nothing:
  it prints the grid-free opening forecast beside a measured tsv (`APPROACH_MEASURED`, default the
  phase 0 one), with `APPROACH_FIRE=aimed|unaimed`, `APPROACH_AMMO=0` and `APPROACH_BREAKS=hazard`
  to try the variants, and after each matchup the measured best opening beside both models'
  preferences; about a second. `APPROACH_TRACE=matchup:range` (for example `pdf-v-gs:100`) also
  writes every squad's forecast at that point turn by turn to `approach-trace.txt`.
- `ShotAuditDiagnostics` sets the planner's removal model against the engine. `RUN_SHOT_AUDIT=1` with
  `SHOT_AUDIT_SCENARIO` (duel syntax) and `SHOT_AUDIT_SEEDS` fights the battles on one thread with
  `ShootAction.Observer` set and, per side and range band, compares actual hits with the engine's own
  expectation at the total it rolled, at a full aim, and at a full aim on a still target, beside the
  aim bonus the shots actually had. `RUN_HITS_TO_KILL=1` with `HITS_TO_KILL_SCENARIO`
  (`"shooter vs target @ range"`) fires hits through the engine's wound rules at fresh targets and
  sets hits per take-out against the planner's removal per hit. Output in `SHOT_AUDIT_OUT`.
  `HITS_TO_KILL_MELEE=1` uses the attacker's primary melee weapon instead (the range is ignored).
- `ForecastAgainstBattleDiagnostics` compares the engagement potential's root forecasts with what
  happens in the battle. Set `RUN_FORECAST_AUDIT=1`, `FORECAST_AUDIT_SCENARIO` (duel syntax) and
  `FORECAST_AUDIT_SEEDS`; it reports, per side and range band, the share of the enemy (and of
  itself) each side's forecast expects to lose in the next turn against the share actually lost, and
  the first planning turn's forecast followed over its horizon (cumulative losses, broken share)
  against the battle. A FRONTAGE table follows: for each turn a side lands melee strikes, counted from
  its first, the distinct strikers and defenders struck against both sides' able soldiers, and per
  striker the strikes planned, hits landed and moved-penalty share (the evidence for
  `MeleeStrikeEstimator.ContactStrikersPerDefender`). The FROM TURN 1 table also gives each side's own
  forecast break chance against the share of its starting value routed by then (rows past the
  forecast's horizon say nothing). MORALE CHECKS sets every live morale check
  (`BattleMoraleService.CheckObserver`) beside the forecast's `BreakHazard` at the same inputs;
  CADENCE AND LEADERS counts checks per squad and when leaders fall. CONTINUATIONS gives, per range
  band, the root states' Stay and Close values, what the Close forecast expects, and how often the
  squad advanced. `FORECAST_AUDIT_TRACE=1` writes the first battle's first squad's root forecasts,
  lane by lane and turn by turn, to `forecast-trace.txt`. Output in `FORECAST_AUDIT_OUT`.
- `MarineVersusPdfDuelDiagnostics` is the first one-marine-against-N duel, superseded by the duel tool
  and kept for reference.

A full gauntlet is several hours of battles; run it in a terminal while the machine is idle.

The standard core gauntlet (seven entries against five references, 200 yards, Aggressive, roles
alternating). Set `GAUNTLET_PRIOR` to the previous run's fitted values and give each run a new tag:

```powershell
dotnet test Tools\OnlyWar.Diagnostics --no-build --nologo -v q --filter "FullyQualifiedName~SquadGauntletDiagnostics.Gauntlet_FightsAndFits" --logger trx -e RUN_GAUNTLET=1 -e GAUNTLET_TAG=full9 -e "GAUNTLET_ENTRIES=pdf=34|tac=1|hyb=26|ork=39|gaunt=18|gs=16|carn=29" -e "GAUNTLET_REFS=pdf,ork,tac,gs,carn" -e "GAUNTLET_PRIOR=pdf=5|tac=84.2|hyb=7.33|ork=9.43|gaunt=11.08|gs=24.22|carn=577" -e GAUNTLET_REFINE=0 -e GAUNTLET_CAP=300 -e DUEL_WORKERS=8
```

New templates are measured in faction stages: each entry only against references from other
factions (the Cult and the Tyranids count as one side), so stages share battles through the cache.

## Placement

`SquadDuelDiagnostics.HarnessVersion` 1 rolled its own placement, which put the near side's squad
leader in the rear column and the far side's in the front, and stacked a side's squads in a column the
game never produces. Mirror battles (identical squads, 800 battles) gave the Attacker slot 56.5% of
the wins (z ≈ 3.7). Version 3 calls the game's `AnnihilationPlacer`; the same mirrors give 50.0%, and
the gauntlet's attacker-role coefficient has come out at zero in every run since. Results from before
version 3 carry about ×1.15 in force ratio toward side A.

## Measurement caveats

- The ladder's probe cache has no code key; delete `probes-*.tsv` after any battle-relevant code
  change. The gauntlet's code hash is the byte hash of `OnlyWar.Battles.dll`, which is too strict (an
  attribute changes it) and too loose (it ignores `Runtime` and `Domain`).
- v1 runs use one start range (200 yards); v2 runs open at the assault range. All use open ground and
  no line of sight, so terrain will need a rerun. v1 runs also deployed burrowers (today only the
  Ravener has the Burrow ability) normally, where the game has them erupt beside the enemy; v2's
  assault mode places them as the game does.
- Precision weights alone do not suit a single-scale fit: on the `*9` cells the precision-weighted
  chi-square was 168 times its degrees of freedom, so the matchups, not the battles, set the spread.
  Hence the τ in the single-scale weights.
- Small forces behave differently from large ones; measure with full-size squads. A side of one to
  three monsters is close to all-or-nothing (probit σ about 0.75 against 0.1–0.2 for infantry).

## Pursuit scale probes

`ActivePursuitScaleDiagnosticsTests` (moved from `OnlyWar.Tests/Battles` on 2026-10-04) runs two
large pursuits (315 marines against 20 routing orks, and 360 against 24) through the full resolver
and checks the active-pursuit invariant: contact is only maintained with closing, attack or
fire-cycle progress. `RUN_PURSUIT_SCALE_TRACE=1` enables them; each writes a summary and trace to
`%TEMP%\GodotOnlyWar\pursuit-trace`. Background: `Design/Reference/BattleLogic.md`,
"Complete-pursuit validation". They use `TestModelFactory` and `TestSkills`, linked from
`OnlyWar.Tests/Fixtures`.

## Campaign traces

`ScenarioTraceDiagnostics` (moved from `OnlyWar.Tests/Turns` on 2026-10-04) generates the opening
"Promised World" scenario across several seeds and runs each sector forward with an idle player.
`RUN_SCENARIO_TRACE=1` writes per-seed traces and a combined CSV to
`%TEMP%\GodotOnlyWar\scenario-trace`, or the folder `SCENARIO_TRACE_OUT` names;
`SCENARIO_TRACE_SEEDS=1` narrows the seeds (seed 1 alone is about 17 minutes).
`SCENARIO_TRACE_TURNS=0` stops after generation, so the trace shows only generation's battles, in
under 10 minutes a seed. `SCENARIO_TRACE_SMALL=1` uses the small sector of the save round-trip tests,
and `SCENARIO_TRACE_CHAPTER` sets the chapter name. `RUN_POCKET_DUMP=1` runs the Imperial-pocket probe. Both facts are also tagged
`Category=Slow`. They use `TestGeneration` and `TestPersonnelComposition`, linked from
`OnlyWar.Tests/Fixtures`.

## Linking test fixtures

Fixtures are linked from `OnlyWar.Tests` rather than copied. The modules grant this project the same
`InternalsVisibleTo` friend access as `OnlyWar.Tests`, and `ModuleBoundaryEnforcementTests` pins that
allowlist, so a linked fixture may use module internals as it does in the test suite.
