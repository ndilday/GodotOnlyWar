- Python is not available on this machine; do not attempt to use Python in project work.

## Test Running Guidance

- Prefer targeted `dotnet test` runs while iterating. The full `OnlyWar.Tests` suite is slow and should be expected to take at least 5 minutes.
- Use `OnlyWar.Tests\OnlyWar.Tests.csproj` directly and filter by namespace, class, or test name for the area touched. Useful folder/namespace filters include:
  - `FullyQualifiedName~OnlyWar.Tests.Domain`
  - `FullyQualifiedName~OnlyWar.Tests.Turns`
  - `FullyQualifiedName~OnlyWar.Tests.Generation`
  - `FullyQualifiedName~OnlyWar.Tests.Data`
  - `FullyQualifiedName~OnlyWar.Tests.Battles`
  - `FullyQualifiedName~OnlyWar.Tests.UI`
- Exclude opt-in long diagnostics during default verification with `Category!=Slow` unless the task explicitly asks for balance/diagnostic coverage.
- Run `ScenarioTraceDiagnostics` only when specifically needed, with `RUN_SCENARIO_TRACE=1`; it writes trace files under the temp directory.
- For broad confidence without paying the full-suite cost repeatedly, run the relevant namespace/class first, then one broader adjacent namespace if the change crosses boundaries. Save the full suite for final validation or when the user asks for it.

## SQLite Database Access

- For ad hoc SQLite database inspection, queries, schema changes, and data fixes, prefer:
  `C:\Projects\SQLite\Tools\sqlite3.exe`
- Do not create one-off C# programs for database operations that the SQLite CLI can perform.
- Use C# only when database access is part of the application’s production implementation or automated tests.