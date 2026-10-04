# OnlyWar Test Project

The test project contains fast unit tests, application-level turn and mission tests, and a smaller
set of persistence and generation integration tests. The main areas covered are:

- domain rules: wounds, healing, training, recruitment, factions, supplies, transfers, and medical care
- battle rules: actions, placement, morale, withdrawal, engagement planning, damage, casualties, and replay data
- mission and order processing, including stealth, detection, continuation, outcomes, and reporting
- campaign generation, governance, force composition, and deterministic seeded behavior
- turn processing, strategic combat, reports, intelligence, construction, and multi-turn invariants
- save/load, deployment storage, rules-database validation, and atomic save recovery
- controller-facing projections and labels for fleet, planet, chapter, region, battle review, and training screens

The default verification command excludes the deliberately slow diagnostics:

```powershell
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "Category!=Slow"
```

The `ScenarioTraceDiagnostics` tests are opt-in balance tools rather than correctness tests. They are
gated by `RUN_SCENARIO_TRACE` or `RUN_POCKET_DUMP` and write human-readable traces under the temporary
directory. Run them only when investigating campaign behavior.

`SquadDuelDiagnostics` and `SquadLadderDiagnostics` are opt-in Battle Value calibration tools. They
fight squads from the rules database against each other over many seeds and report who holds the
field and the casualties, so Battle Value can be set from fights rather than from a formula.

- `SquadDuelDiagnostics` runs hand-picked scenarios. Set `RUN_DUEL_DIAGNOSTIC=1` and
  `DUEL_SCENARIOS` (for example `"1 vs 34,n=9,x=4 @ 200"`: template id, `n` trims a squad, `x`
  repeats it, `ws` hands pooled weapon sets to the best shooters, `@` is the start range in yards).
  The table lands in `%TEMP%\GodotOnlyWar\squad-duel.txt`, or in the file `DUEL_OUT` names, and the
  battles per minute in the same name with `.throughput.txt`. `DUEL_ROLES` picks which side is the
  battle's Attacker: `a-attacks` (default, so older numbers stay comparable), `b-attacks` or
  `alternate` (A attacks on odd seeds). The table is always from A's point of view.
- Both tools run the seeds of a scenario in parallel, `DUEL_WORKERS` at a time (default 8; `1` runs
  them one after another). Each battle has its own seeded random streams and the seeds are summed in
  order, so the worker count does not change the result.
- `SquadLadderDiagnostics` finds, for each pair of squads, the break-even force ratio, which is the
  count of one squad that makes the other hold the field half the time. Set `RUN_LADDER=1` and
  `LADDER_ENTRIES` (for example `"pdf=34|hyb=26|tac=1|ork=39"`; a squad with no `n=` is built at its
  template maximum, including templates that would otherwise roll a size); the other settings are
  documented on the class. Output goes to `%TEMP%\GodotOnlyWar\ladder`: `probes-TAG.tsv` caches every
  probe by rules-database hash, so an interrupted run resumes and a data change invalidates old
  results, `pairs-TAG.tsv` is the pair table, and `throughput-TAG.txt` reports battles per minute.
  Probes run one after another, because each one decides where the next goes; only the battles
  inside a probe run in parallel. Run the ladder while the machine is idle.
- `SquadGauntletDiagnostics` is the fixed-design alternative to the ladder. Set `RUN_GAUNTLET=1`,
  `GAUNTLET_ENTRIES` (same `key=spec|...` syntax as the ladder) and `GAUNTLET_REFS` (comma-separated
  entry keys): every entry fights every reference over a spread of force ratios (stage 1: five ratios
  around the prior, 8 seeds each; stage 2: three ratios where the stage-1 fit says 25%, 50% and 75%,
  16 seeds each), and a probit model is fitted to all the battles. All battles of a stage run in one
  pool, `DUEL_WORKERS` at a time, so long battles do not leave cores idle. Output in
  `%TEMP%\GodotOnlyWar\gauntlet` (or `GAUNTLET_OUT`): `battles-TAG.tsv` is one row per battle, keyed
  by rules-database hash, a code hash (`OnlyWar.Battles.dll` plus `SquadDuelDiagnostics.HarnessVersion`,
  which must be bumped when the harness's placement or squad building changes), range, aggression, squads, seed and, when A
  defends, a role suffix, and every `battles-*.tsv` in the folder is read back, so runs resume and a
  data or code change invalidates old battles. `GAUNTLET_ROLES` picks the battle roles: `alternate`
  (default; A attacks on odd seeds, B on even), `a-attacks` (the old behavior; older rows are all
  A-attacks, parse without the `aRole` column and are reused under it) or `b-attacks`. When both roles
  are present the global fit adds one attacker-role coefficient (with its standard error and the factor
  `exp(rho/kappa)` in models) and the per-model values become role-neutral. `fit-TAG.txt`, `cells-TAG.tsv` and `values-TAG.tsv` are the fit (per-model values with
  PDF = 5, the noise of a fight, per-cell break-evens, and a chi-square test of whether matchup
  structure exists beyond one value per entry); `throughput-TAG.txt` counts fresh battles. The other
  settings (`GAUNTLET_PRIOR`, `_RANGE`, `_AGGR`, `_CAP`, `_TAG`, `_MIRROR`, `_REFINE`) are documented
  on the class. To refit stored battles without fighting any, set `RUN_GAUNTLET_FIT=1` and
  `GAUNTLET_IN` (a battles file, or a folder meaning every `battles-*.tsv` in it). The fitter
  (`Battles/Calibration/ProbitFit.cs`) has always-on unit tests in `ProbitFitTests`.

The slow persistence and generation tests can be run separately:

```powershell
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "Category=Slow"
```
