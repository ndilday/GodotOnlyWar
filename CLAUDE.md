# OnlyWar — working agreements

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
- Full suite, idle machine, 2026-08-08: **1709 tests in 10m54s**. For reference the same
  filter at commit `0dcc7d2` took 22m4s for 1679 tests, so the early-August work roughly
  halved it.
- Roughly nine of those eleven minutes are ~10 tests, nearly all full sector generation:
  `ScenarioBuilderTests` (3 tests, 1m17s run alone), `SaveLoadRoundTripTests` (3, they
  generate a sector before round-tripping), `NewChapterBuilderTests`,
  `GovernanceHierarchyTests`. (Two untagged Missions tests used to be on this list; see the
  `Category!=Slow` bullet below for why they no longer are.)
- Per-test durations from a TRX of a PARALLEL run are inflated by contention between the
  tests themselves — those three `ScenarioBuilderTests` report 1m53s+1m50s+22s inside the
  full run and total 1m17s alone. Rank with them; never size with them.
- `--filter "Category!=Slow"` is **1700 tests in ~1m7s** (2026-08-09), and is the practical
  default. The two Missions tests that used to dominate it —
  `MissionTargetStrengthTests.AssassinateStealth_UntrainedForceAgainstASearchedRegion_IsDetected`
  and `MissionStealthDifficultyTests.SabotageStealth_UntrainedForceAgainstASearchedRegion_IsDetected`
  — were about a minute each because each one fought seven full battles that had nothing to do
  with the detection they assert. They now drive two mission steps instead of `RunToCompletion`
  and finish in milliseconds; the whole `OnlyWar.Tests.Missions` area is 170 tests in ~0.4s.
  Ignore older notes about this filter blowing past a 600s timeout.
- While iterating, `--filter "FullyQualifiedName~OnlyWar.Tests.Battles"` (546 tests, ~7s)
  is still the fastest useful signal.
- Filter by area while iterating, e.g. `--filter "FullyQualifiedName~OnlyWar.Tests.Turns"`
  (also `.Domain`, `.Generation`, `.Data`, `.Battles`, `.UI`).
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
