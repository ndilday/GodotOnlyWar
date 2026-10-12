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
  table is always from A's point of view. For the first battle of a run, `DUEL_TRACE` writes the
  engine's planning log and `DUEL_WOUND_LOG` writes every wound side A takes (attack kind, weapon,
  damage, hit location, damage-to-Constitution ratio and wound level), a per-weapon summary, and the
  final wound totals of A's hit locations: the tool for "what actually killed it". `DUEL_SHOT_LOG`
  writes every volley of that battle, both sides (rounds planned and fired, aim multiplier, the
  turns the shooter had just spent aiming at that target, hits, range) and a per-weapon summary split
  by aimed and un-aimed: the tool for "did the aim reach the shot".
- All the tools run battles in parallel, `DUEL_WORKERS` at a time (default 8; `1` runs them one after
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
- `SquadGauntletDiagnostics` is the fixed-design calibration. Set `RUN_GAUNTLET=1`,
  `GAUNTLET_ENTRIES` (same `key=spec|...` syntax as the ladder) and `GAUNTLET_REFS` (comma-separated
  entry keys): every entry fights every reference over a spread of force ratios (stage 1: five ratios
  around the prior, 8 seeds each; stage 2: three ratios where the stage-1 fit says 25%, 50% and 75%,
  16 seeds each; `GAUNTLET_REFINE=n` repeats stage 2), and a probit model is fitted to all the
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
  `throughput-TAG.txt` counts fresh battles. The other settings (`GAUNTLET_PRIOR`, `_RANGE`, `_AGGR`,
  `_CAP`, `_TAG`, `_MIRROR`) are documented on the class. To refit stored battles without fighting
  any, set `RUN_GAUNTLET_FIT=1` and `GAUNTLET_IN` (a battles file, or a folder meaning every
  `battles-*.tsv` in it).
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
  potential and chosen option: the tool for "does the forecast jump". Set `RUN_PHI_SWEEP=1`.
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
- All calibration runs use one start range (200 yards), open ground and no line of sight. Terrain
  will need a rerun.
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
`%TEMP%\GodotOnlyWar\scenario-trace`; `SCENARIO_TRACE_SEEDS=1` narrows the seeds (seed 1 alone is
about 17 minutes). `RUN_POCKET_DUMP=1` runs the Imperial-pocket probe. Both facts are also tagged
`Category=Slow`. They use `TestGeneration` and `TestPersonnelComposition`, linked from
`OnlyWar.Tests/Fixtures`.

## Linking test fixtures

Fixtures are linked from `OnlyWar.Tests` rather than copied. The modules grant this project the same
`InternalsVisibleTo` friend access as `OnlyWar.Tests`, and `ModuleBoundaryEnforcementTests` pins that
allowlist, so a linked fixture may use module internals as it does in the test suite.
