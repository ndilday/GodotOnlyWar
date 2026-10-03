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
  The table lands in `%TEMP%\GodotOnlyWar\squad-duel.txt`.
- `SquadLadderDiagnostics` finds, for each pair of squads, the break-even force ratio, which is the
  count of one squad that makes the other hold the field half the time. Set `RUN_LADDER=1` and
  `LADDER_ENTRIES` (for example `"pdf=34|hyb=26|tac=1|ork=39"`; a squad with no `n=` is built at its
  template maximum, including templates that would otherwise roll a size); the other settings are
  documented on the class. Output goes to `%TEMP%\GodotOnlyWar\ladder`: `probes-TAG.tsv` caches every
  probe by rules-database hash, so an interrupted run resumes and a data change invalidates old
  results, and `pairs-TAG.tsv` is the pair table. Run several processes with different `LADDER_TAG`
  and `LADDER_PAIRS` to split a large matrix; do it while the machine is idle.

The slow persistence and generation tests can be run separately:

```powershell
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "Category=Slow"
```
