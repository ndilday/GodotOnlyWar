# OnlyWar — working agreements
## Basic rules
In communication with the user, use ASD-STE100 Simplified Technical English.

## Tool discipline (applies from your first tool call)

Permission rules are **prefix matches**, and a compound command matches only if *every*
part of it matches. Command shape does matter — but far less than earlier versions of this
file claimed. Check the verified list below before contorting a command to dodge a prompt.

**Where a rule lives matters more than how the command is shaped.**
`.claude/settings.local.json`'s `permissions.allow` **replaces** the one in
`.claude/settings.json` — it does not merge with it (verified 2026-07-30, by moving rules
between the files one at a time). Keep the single canonical allow list in
`settings.local.json`; `settings.json` carries `hooks` only. A permission rule added to
`settings.json` silently does nothing. `/.claude` is git-ignored in full, so neither file
is shared — there is no sharing argument for splitting the list.

What was actually measured on 2026-07-30, re-running each command once the rules were live:

- **Multi-word prefixes work in both matchers.** `PowerShell(dotnet --version:*)` and
  `Bash(dotnet build:*)` both fire. The old "PowerShell matches the first token only"
  claim was an artifact of the replace-not-merge behaviour above: no rule in
  `settings.json` was ever live, so nothing PowerShell-specific was being observed.
- **Compound commands are fine when every part is allowlisted.**
  `dotnet --version; dotnet --list-runtimes` runs silently. Mix in one unallowlisted part
  and it prompts — the matcher checks all of them, so a compound command can't smuggle
  anything past the allowlist.
- **A `;` INSIDE A QUOTED ARGUMENT still splits the command for the matcher.** This is the
  compound-command rule biting somewhere it does not look like a compound command:
  `dotnet test … -l "console;verbosity=detailed"` prompts every time, because the matcher
  sees `dotnet test … -l "console` and `verbosity=detailed"` and the second part matches no
  rule. Quoting does not protect it. To get failure detail from `dotnet test`, use
  `--logger trx` (no semicolon) and `Read` the `.trx` under `TestResults/`, or raise
  verbosity with `-v n`. Same trap for any other `key;value` argument.
- **Redirections do not break matching.** `dotnet --version 2>$null` runs silently. Still
  don't suppress stderr — you want to read it — but not for permission reasons.
- **The call operator `&` does break matching.** `&` becomes the first token and the exe
  path stops matching. Never prefix a PowerShell command with `&` when the path has no
  spaces.
- **A leading `cd` prompts** — but because `cd` itself isn't allowlisted (the all-parts
  rule), not because it breaks the match. Use absolute paths regardless: the working
  directory is already the repo root.
- **Route git through the Bash tool.** Not a matcher limitation — the granular
  per-subcommand rules simply exist as `Bash(git …)` and have no PowerShell counterparts.
- **Prefer the dedicated tools over their shell equivalents:** `Read` over `cat`/`head`/
  `tail`, `Grep` over `grep`/`rg`/`Select-String`, `Glob` over `find`/`ls -R`,
  `Edit`/`Write` over `sed -i`. These never prompt. They are deliberately *not*
  allowlisted as shell commands, so the friction steers you to the right tool.
- **Read-only cmdlets run without a prompt whether or not they're allowlisted.**
  `Get-ChildItem` is absent from the allow list and still runs silently — Claude Code
  classifies safe reads on its own. So the allowlist is *not* what steers you here: prefer
  `Glob`/`Grep` because they give better output and read outside the repo, not because
  `Get-ChildItem` costs a prompt. This auto-approval is what confounded the earlier
  experiments — a command running silently proves nothing about whether a rule matched.
- **Don't use `echo` to label or separate output.** Put the explanation in your response.
- **`grep`/`sed`/`find`/`cat` and `2>` redirections are blocked by a hook**, not merely
  discouraged — `.claude/hooks/no-shell-search.sh`, wired as `PreToolUse` on
  `Bash|PowerShell`. A deny is the rule working, not a malfunction: switch to the tool the
  message names. `#allow-shell` anywhere in the command overrides it; that override is for
  the case where no dedicated tool can do the job at all, not for the case where the
  dedicated tool lacks a flag you wanted.
- Parallelise by issuing several independent tool calls in one block. Welding them into
  one shell line is no longer a permission problem, but separate calls keep failures
  attributable and let the allowed ones through when one part isn't covered.

## SQLite

`Database/OnlyWar.s3db` is the rules database and the source of truth for soldier/squad/
unit templates, skills, ratings, training profiles, and factions. Player saves and user
diagnostic bundles are separate `.s3db` files.

Query and modify them with the SQLite CLI. It is not on PATH. Do not write one-off C#
programs for database work the CLI can do; use C# only where DB access is part of the
shipping application or its automated tests.

- **Bash tool:** `/c/Projects/SQLite/Tools/sqlite3.exe <db> "<SQL>"` — POSIX path form.
  The quoted Windows path does **not** match the allow rule from Bash.
- **PowerShell tool:** `C:\Projects\SQLite\Tools\sqlite3.exe <db> "<SQL>"` — bare path, no
  `&` and no quotes around it. The path has no spaces, so the call operator is unnecessary,
  and it would push the exe out of first-token position and break the match.
- **No trailing `;`** inside the quoted SQL — the CLI doesn't need it and it breaks the
  prefix match.
- One invocation per tool call. For multi-statement work, write a `.sql` file and `.read`
  it. Keep schema/data edits idempotent (`INSERT OR REPLACE`, guarded `UPDATE`,
  `CREATE TABLE IF NOT EXISTS`) and verify with a follow-up `SELECT`.
- **Get new primary keys from `SELECT MAX(Id)`, never by eyeballing a filtered query.**
  `INSERT OR REPLACE` silently overwrites an existing row, and the damage surfaces much
  later as an unrelated test failure. This cost a full detour on 2026-08-08: element id 70
  was picked from a `WHERE Min <> Max` listing and clobbered the Scout Company HQ's Scout
  Sergeant slot.
- After a data migration, diff the working DB against the committed one before trusting it:
  `git show HEAD:Database/OnlyWar.s3db > <scratchpad>/head.s3db`, then `ATTACH` it and ask
  two *separate* questions per table — rows in HEAD missing from main (must be zero unless
  you meant to delete them) and rows in main missing from HEAD. Don't fuse them into one
  `EXCEPT ... UNION ALL ... EXCEPT` chain; SQLite associates compound operators left to
  right and the result is not the symmetric difference you wanted.
- `git checkout -- Database/OnlyWar.s3db` can fail with "unable to unlink old ... Invalid
  argument" while a stale `vstest.console`/`testhost` holds the file. Kill it first, or
  repair the rows with targeted SQL instead.
- `-header -column` for readable output.

## Bulk data work

Generating, cross-checking, or transforming data at volume — hundreds of records, set
comparison, dedup — is a scripting job. Don't grind it out across dozens of inline tool
calls, and don't add a throwaway C# program to the project for it (same rule as SQLite
above).

Write a PowerShell script into the session scratchpad and invoke it **through the
PowerShell tool** by bare absolute path: `<scratchpad>\<script>.ps1`, one invocation per
tool call. No `&`, no surrounding quotes — the scratchpad path has no spaces, and anything
ahead of the path breaks first-token matching.

- The scratchpad root `C:\Users\nadil\AppData\Local\Temp\claude\` is allowlisted as a
  prefix in **`.claude/settings.local.json`** (not `settings.json` — a rule there is inert,
  see above). As of 2026-08-07 the prefix rules were still producing prompts and the user
  was hand-approving every scratchpad script; extra anchor variants were added that day and
  are **unverified**. Assume a scratchpad script may still prompt until proven otherwise. Scripts
  written anywhere else will prompt on every run.
- The allow rule is what actually suppresses the prompt — *this file cannot grant
  permissions*, it only shapes the commands you write. If a scratchpad script prompts
  anyway, the rule is missing or misshapen; say so rather than working around it.
- Keep generation separate from verification — one script that emits, one that checks —
  so a failed check doesn't force regenerating the data to re-test it.
- Keep both idempotent and re-runnable, so iterating on the input is cheap.
- Have the checker print counts and violations, not full result sets. You are the one
  reading the output; keep it to what decides the next step.
- Put source data in plain `.txt`/`.json` files beside the script rather than inlining it,
  so the data can be edited without touching the logic.

## Build & test

- `dotnet` runs through the **PowerShell** tool. One invocation at a time, foreground,
  allowed to block until it returns. Never overlap invocations and never
  background-and-poll them — concurrent runs lock `bin/obj` artifacts and testhost, which
  reads as a hang.
- Keep output small: `--nologo -v q`. Build once, then `dotnet test --no-build`.
- **Pass environment variables with `dotnet test -e NAME=value`, never a PowerShell
  `$env:NAME = "…";` prefix.** The prefix takes first-token position and breaks the allow
  rule, forcing a manual permission prompt; `-e` keeps `dotnet test` in front. Same reason
  `if ($?) { … }` chaining is out — use one tool call per command instead.
- **Every timing number below assumes an IDLE machine, and that assumption is load-bearing.**
  Measured 2026-08-08: the identical build ran the full suite in **21m28s** while the user
  was working and **10m54s** once they stepped away. Contention is a 2x factor — bigger
  than most optimizations — so a wall-clock comparison taken while the user is in Godot,
  Visual Studio, or a build is not evidence of anything. Confirm the machine is quiet
  before believing a timing, and prefer call counts, which are contention-immune.
- **Every count below is from 2026-09-19 unless dated otherwise. The suite has roughly
  doubled since the August figures, so treat any number older than that as gone, not
  merely drifted.** The `Category!=Slow` bullet was still claiming 1700 tests when the
  filter actually ran 3185.
- There are **two test projects**, and both run on an unfiltered `dotnet test`:
  `OnlyWar.Tests` (the suite proper) and `OnlyWar.HeadlessTests` (14 tests, ~0.2s warm and
  ~8s on the session's first run, which is start-up and not the tests). Every
  filter below is written against `OnlyWar.Tests` and the headless project runs alongside
  it regardless, which is why two "Passed!" lines come back per invocation.
- The suite is **3183 tests, and it splits cleanly in two**: `Category!=Slow` is 3171 in
  ~3m41s and `Category=Slow` is **12 tests in 8m33s** (measured once, under the same load
  that doubled the readings below it, so treat it as an upper bound). Nobody has timed one
  unfiltered run since 2026-08-08 (1709 tests, 10m54s idle; 22m4s for 1679 at commit
  `0dcc7d2`). Run the two halves separately — you almost never want the slow half.
- Those 12 slow tests, from a TRX on 2026-09-19, are all full sector generation or a save
  round-trip of one:
  `ScenarioBuilderTests` (4: 2m39s on seed 3, 1m34s, 36.9s, 22.2s), `SaveLoadRoundTripTests`
  (3, about 51s each), `NewChapterBuilderTests` (25.8s), `OrkSaveLoadRegressionTests`
  (16.9s), `ChapterMusterPerformanceTests` (0.5s — tagged `Slow`, and no longer is), and the
  two `ScenarioTraceDiagnostics` entries, which return in microseconds unless
  `RUN_SCENARIO_TRACE=1` is set.
- Per-test durations from a TRX of a PARALLEL run are inflated by contention between the
  tests themselves — the three `ScenarioBuilderTests` of 2026-08-08 reported 1m53s+1m50s+22s
  inside the full run and totalled 1m17s alone. Rank with them; never size with them.
- `--filter "Category!=Slow"` is 3171 tests in **~3m41s on a quiet machine**, and it is
  still the practical default. Ignore older notes about it blowing past a 600s timeout.
  **Three runs of the identical binary on 2026-09-19 gave 7m52s, 7m24s and 3m41s.** The
  first two were taken while the user was working. Do not repeat the mistake made that day:
  two consistent slow readings were written down here as "the cost and not a load spike",
  and a TRX of the third run then showed 215s of summed test time against a 3m41s wall
  clock — nothing was missing, the machine was simply busy. **Two agreeing measurements
  under load agree about the load.**
- **61% of that run is ONE test.**
  `Generation.GovernanceHierarchyTests.Governance_IsDeterministicForSeed` is **131s of the
  215s**, because it generates two full sectors on seed 4 and loads the rules database twice
  to do it. It is not tagged `Slow`. The rest of the top five are the same shape — sector
  generation in an untagged test: `Generation.SectorBuilderTests` (2 tests, 19s),
  `Domain.FactionCapabilityStateTests` (2 tests, 17s),
  `Battles.BattleMoraleResolverTests.HighEgoSquadsFightingToAnnihilation_NeverRout` (5.1s),
  `Data.NewGameSaveTests` (2 tests, 4.9s). Those nine tests are 84% of the suite; the other
  3,162 share about 35 seconds between them. **If this filter feels slow, suspect one of
  those nine or a busy machine — not general growth.**
- Every area below was measured separately on 2026-09-19, so the times include process
  start-up and exclude contention with the other areas.

  | Filter (`FullyQualifiedName~OnlyWar.Tests.` + …) | Tests | Time |
  |---|---|---|
  | `Battles` | 1682 | ~4s |
  | `Domain` | 559 | ~18s |
  | `Turns` | 338 | ~1s |
  | `Missions` | 168 | ~0.8s |
  | `UI` | 132 | ~1s |
  | `Application` | 82 | ~1s |
  | `Generation` | 68 | **~2m** |
  | `Data` | 55 | **~31s** |
  | `Orders` | 44 | ~0.8s |
  | `Narrative`, `Math`, `Architecture`, `Diagnostics` together | 45 | ~0.1s |

  Every area except `Generation` and `Data` answers in about a second, so there is no
  reason to widen a filter to save a call. `Generation` and `Data` are the two to avoid
  unless the change touches them, and both rows above already exclude their `Slow` tests.
  `Generation`'s two minutes are almost entirely `GovernanceHierarchyTests`.
- The Missions area is 168 tests in ~0.8s, and it is the worked example of why an untagged
  test is worse than a slow one. Two of them —
  `MissionTargetStrengthTests.AssassinateStealth_UntrainedForceAgainstASearchedRegion_IsDetected`
  and `MissionStealthDifficultyTests.SabotageStealth_UntrainedForceAgainstASearchedRegion_IsDetected`
  — took about a minute each, because each fought seven full battles that had nothing to do
  with the detection it asserts. They now drive two mission steps instead of
  `RunToCompletion`. **Fix the test rather than tag it `Slow`** where the cost is incidental
  to what it proves.
- While iterating, `--filter "FullyQualifiedName~OnlyWar.Tests.Battles"` (1682 tests, ~4s)
  is still the fastest useful signal — it tripled in count and got faster.
- Add `&Category!=Slow` to an area filter. `Generation` (6), `Data` (4) and `Turns` (2)
  carry every `Slow` test between them, and an area name alone will pull them in.
- Run `ScenarioTraceDiagnostics` only when asked, with `-e RUN_SCENARIO_TRACE=1`. Narrow it
  with `-e SCENARIO_TRACE_SEEDS=1` — seed 1 alone is ~17.5 min (2026-08-02), all eight
  seeds far longer. Traces land in `%TEMP%\GodotOnlyWar\scenario-trace\seed-<n>.txt` at
  `GameLogLevel.Debug`; read the trace rather than trusting the pass/fail, and grep
  `Battle end` for per-battle turn counts and planning ms.
- If a run wedges, kill stale `testhost`/`vstest.console`/`dotnet` processes to release
  the locks.

## Godot

**Never run Godot tests or drive the Godot runtime yourself.** The user verifies the
Godot side manually because doing it here burns credits. Make the change, say what needs
verifying, and hand it off.

## Environment

- Windows. **Python is not available** — don't reach for it.
- Both a Bash (Git Bash, POSIX sh) and a PowerShell tool are available; each takes its
  own syntax. Pick one per command and don't mix idioms.
