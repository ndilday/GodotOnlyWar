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

The slow persistence and generation tests can be run separately:

```powershell
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "Category=Slow"
```
