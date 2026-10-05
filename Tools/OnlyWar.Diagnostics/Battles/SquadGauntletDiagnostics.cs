using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Diagnostics.Battles.Calibration;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in calibration tool, not a correctness test. The fixed-design counterpart of the ladder: every
/// ENTRY squad fights each REFERENCE squad over a spread of force ratios, and one probit model is
/// fitted to all the battles. The fit gives a per-model value for every entry (anchored so a PDF
/// trooper is worth 5), the noise of a fight, and a test of whether matchups (rock, paper, scissors)
/// matter beyond what one number per entry explains. Gated by RUN_GAUNTLET.
///
/// Design. A cell is one entry against one reference (never an entry against itself). Stage 1 fights
/// every cell at five force ratios, the prior ratio times 1/4, 1/2, 1, 2 and 4, 8 seeds each. Each
/// cell's probit is then fitted on those battles, and stage 2 fights three more ratios per cell, where
/// the fit predicts that A holds the field 25%, 50% and 75% of the time, 16 seeds each. A cell whose
/// stage-1 battles were all one-sided has no such fit, so stage 2 steps outward from the grid by
/// factors of 4 instead, up to the model cap. Ratios are realized as whole squads (a of A against b of
/// B), at most GAUNTLET_CAP models a side, preferring small battles. All the battles of a stage run
/// in one pool, largest first, so a long battle never leaves cores idle behind it.
///
/// Settings (environment):
///   GAUNTLET_ENTRIES  '|'-separated "key=spec", one squad each in SquadDuelDiagnostics syntax (for
///                     example "pdf=34|hyb=26|tac=1|ork=39"). A squad with no n= is built at its
///                     template maximum. Do not use x= here.
///   GAUNTLET_REFS     comma-separated entry keys that act as references (for example "pdf,hyb").
///   GAUNTLET_PRIOR    optional "key=value|..." prior per-model value of an entry; the default is the
///                     summed template Battle Value per model. It only places the stage-1 ratios.
///   GAUNTLET_REFINE   extra rounds of stage 2's placement on all battles so far, 16 seeds a ratio
///                     (default 0, the fixed design). Use it when cells come back separated.
///   GAUNTLET_MIRROR   1 (default) fights both orientations of a pair of references, so A-versus-B and
///                     B-versus-A are separate cells; 0 keeps only the first.
///   GAUNTLET_RANGE    starting range in yards (default 200).
///   GAUNTLET_AGGR     Aggression for both sides (default Aggressive).
///   GAUNTLET_ROLES    which side is the battle's Attacker (BattleRole.Attacker, placed at x = 0): "alternate"
///                     (default; A attacks on odd seeds, B on even seeds, so each cell and ratio is balanced),
///                     "a-attacks" (A always, as before roles were recorded) or "b-attacks". The two sides'
///                     squads are built in slot order, so a swapped battle is the same battle with the
///                     labels exchanged. The fit reports the attacker role as a coefficient (see below).
///   GAUNTLET_CAP      most models allowed on one side (default 200).
///   GAUNTLET_TAG      name for this run's output files (default "run").
///   GAUNTLET_OUT      output folder (default %TEMP%\GodotOnlyWar\gauntlet).
///   DUEL_WORKERS      battles run at once (default 8).
///
/// Every battle is one row of battles-TAG.tsv, appended as it finishes and keyed by the rules
/// database hash, a code hash (OnlyWar.Battles.dll plus SquadDuelDiagnostics.HarnessVersion, so a
/// change to the harness's placement or squad building also invalidates old battles), the range, the aggression, both squads' specs with
/// their counts, the seed, and, for a battle in which A defends, a "|bAtt" suffix. The aRole column
/// (Attacker or Defender, A's role) records the role. Rows from before the column have 19 columns,
/// were all A-attacks, parse as Attacker, and keep their old keys, so "a-attacks" and the odd seeds
/// of "alternate" reuse them. When both roles are present the fit adds one coefficient rho for "A
/// attacks" (+1) against "A defends" (-1) to the global model, with its standard error and the factor
/// exp(rho / kappa) in models; values and kappa are then role-neutral. The per-cell lines and the
/// matchup test are unchanged (each cell holds both roles in balance under "alternate").
/// Every battles-*.tsv in the folder is read back at the start, so an
/// interrupted run resumes, another tag's battles are reused, and a change to the rules or the battle
/// code invalidates old battles by itself. A battle's result does not depend on the worker count or
/// the order the battles run in. throughput-TAG.txt reports the battles and turns per minute, counting
/// fresh battles only, and fit-TAG.txt, cells-TAG.tsv and values-TAG.tsv are the fit.
///
/// RUN_GAUNTLET_FIT=1 with GAUNTLET_IN (a battles file, or a folder meaning every battles-*.tsv in it)
/// refits stored battles without fighting any, so analysis never needs a rerun. It uses the most
/// common rules-database and code version in the input; GAUNTLET_DB, GAUNTLET_CODE, GAUNTLET_RANGE and
/// GAUNTLET_AGGR narrow the choice, and the report goes to GAUNTLET_OUT as fit-TAG.txt (default tag "refit").
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SquadGauntletDiagnostics
{
    private const int Stage1Seeds = 8;
    private const int Stage2Seeds = 16;
    private const double OneSidedScore = 0.85;
    private const double StepFactor = 4.0;
    private const double ProbeZ = 0.6744897501960817;
    private static readonly double[] Stage1Multipliers = [0.25, 0.5, 1.0, 2.0, 4.0];

    [Fact]
    public void Gauntlet_FightsAndFits()
    {
        string entriesText = Environment.GetEnvironmentVariable("GAUNTLET_ENTRIES");
        if (Environment.GetEnvironmentVariable("RUN_GAUNTLET") == null || string.IsNullOrWhiteSpace(entriesText))
        {
            return;
        }

        new GauntletRun(entriesText).Execute();
    }

    [Fact]
    public void Gauntlet_RefitsStoredBattles()
    {
        string input = Environment.GetEnvironmentVariable("GAUNTLET_IN");
        if (Environment.GetEnvironmentVariable("RUN_GAUNTLET_FIT") == null || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        string tag = Environment.GetEnvironmentVariable("GAUNTLET_TAG") ?? "refit";
        string directory = Environment.GetEnvironmentVariable("GAUNTLET_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "gauntlet");
        List<BattleRow> all = GauntletAnalysis.ReadRows(input);
        (List<BattleRow> rows, string note) = GauntletAnalysis.SelectDataset(all);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException($"No usable battles in '{input}'. {note}");
        }

        BattleRow first = rows[0];
        string header = $"Refit of {input}: {rows.Count} of {all.Count} stored battles; db {first.Db}, code {first.Code}, "
            + $"range {first.Range} yd, aggression {first.Aggression}.{Environment.NewLine}{note}";
        GauntletAnalysis.WriteReport(GauntletAnalysis.Analyze(rows), directory, tag, header);
    }

    private sealed record Entry(string Key, string Spec, int Models, double Prior);

    private sealed class Cell
    {
        public int Index;
        public Entry A;
        public Entry B;

        /// <summary>The (a, b) squad counts to fight and how many seeds each gets.</summary>
        public List<(int A, int B, int Seeds)> Points = [];

        public void AddPoint(int a, int b, int seeds)
        {
            int at = Points.FindIndex(point => point.A == a && point.B == b);
            if (at < 0)
            {
                Points.Add((a, b, seeds));
            }
            else if (Points[at].Seeds < seeds)
            {
                Points[at] = (a, b, seeds);
            }
        }
    }

    private sealed record BattleTask(Cell Cell, int A, int B, int Seed, string Key, int Models);

    /// <summary>One gauntlet: its settings, the stores it reads and writes, and the two stages.</summary>
    private sealed class GauntletRun
    {
        private readonly List<Entry> _entries;
        private readonly List<Cell> _cells = [];
        private readonly GameRulesBlob _blob;
        private readonly string _db;
        private readonly string _code;
        private readonly int _range;
        private readonly int _cap;
        private readonly Aggression _aggression;
        private readonly string _tag;
        private readonly string _directory;
        private readonly int _workers;
        private readonly List<string> _refs;
        private readonly RoleMode _roles;
        private readonly ConcurrentDictionary<string, BattleRow> _store = new();
        private readonly object _gate = new();
        private StreamWriter _writer;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly HashSet<string> _loaded = [];
        private int _done;

        public GauntletRun(string entriesText)
        {
            _range = SquadDuelDiagnostics.ReadInt("GAUNTLET_RANGE", 200);
            _cap = SquadDuelDiagnostics.ReadInt("GAUNTLET_CAP", 200);
            _aggression = SquadDuelDiagnostics.ReadAggression("GAUNTLET_AGGR");
            _roles = RoleModes.Read("GAUNTLET_ROLES", RoleMode.Alternate);
            _tag = Environment.GetEnvironmentVariable("GAUNTLET_TAG") ?? "run";
            _directory = Environment.GetEnvironmentVariable("GAUNTLET_OUT")
                ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "gauntlet");
            _workers = System.Math.Max(1, SquadDuelDiagnostics.ReadInt("DUEL_WORKERS", SquadDuelDiagnostics.DefaultWorkers));
            Directory.CreateDirectory(_directory);

            _blob = RulesDatabaseFixture.LoadRules();
            using (FileStream stream = new(
                RulesDatabaseFixture.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _db = Convert.ToHexString(SHA256.HashData(stream))[..12];
            }
            // The code version covers the battle engine AND the harness's battle setup (placement,
            // squad building), which lives in this test assembly and does not change the engine dll.
            string battlesDll = typeof(BattleTurnResolver).Assembly.Location;
            string engineHash;
            using (FileStream stream = new(battlesDll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                engineHash = Convert.ToHexString(SHA256.HashData(stream));
            }
            _code = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"{engineHash}|harness={SquadDuelDiagnostics.HarnessVersion}")))[..12];

            Dictionary<string, double> priors = [];
            string priorText = Environment.GetEnvironmentVariable("GAUNTLET_PRIOR");
            if (!string.IsNullOrWhiteSpace(priorText))
            {
                foreach (string pair in priorText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] parts = pair.Split('=', 2);
                    priors[parts[0].Trim()] = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }
            }

            _entries = [];
            foreach (string text in entriesText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = text.Split('=', 2);
                string key = parts[0].Trim();
                string spec = parts[1].Trim();
                if (spec.Contains("x=", StringComparison.Ordinal) || spec.Contains(';'))
                {
                    throw new ArgumentException($"Entry '{key}' must be one squad without x= ('{spec}').");
                }
                (int models, int value) = SquadDuelDiagnostics.Measure(_blob, SquadDuelDiagnostics.ParseSide(spec));
                double prior = priors.TryGetValue(key, out double given) ? given : (double)value / models;
                _entries.Add(new Entry(key, spec, models, prior));
            }
            foreach (string key in priors.Keys.Where(key => _entries.All(entry => entry.Key != key)))
            {
                throw new ArgumentException($"GAUNTLET_PRIOR names '{key}', which is not an entry.");
            }

            string refText = Environment.GetEnvironmentVariable("GAUNTLET_REFS") ?? "";
            _refs = refText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (_refs.Count == 0)
            {
                throw new ArgumentException("GAUNTLET_REFS must name at least one entry key.");
            }
            foreach (string key in _refs.Where(key => _entries.All(entry => entry.Key != key)))
            {
                throw new ArgumentException($"GAUNTLET_REFS names '{key}', which is not an entry.");
            }

            bool mirror = SquadDuelDiagnostics.ReadInt("GAUNTLET_MIRROR", 1) != 0;
            foreach (Entry a in _entries)
            {
                foreach (string refKey in _refs)
                {
                    Entry b = _entries.First(entry => entry.Key == refKey);
                    if (a.Key == b.Key)
                    {
                        continue;
                    }
                    if (!mirror && _cells.Any(cell => cell.A.Key == b.Key && cell.B.Key == a.Key))
                    {
                        continue;
                    }
                    _cells.Add(new Cell { Index = _cells.Count, A = a, B = b });
                }
            }
        }

        public void Execute()
        {
            LoadStore();
            (long battlesBefore, long turnsBefore) = SquadDuelDiagnostics.Throughput;
            string path = Path.Combine(_directory, $"battles-{_tag}.tsv");
            using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using (_writer = new StreamWriter(stream) { AutoFlush = true })
            {
                if (stream.Length == 0)
                {
                    _writer.WriteLine(BattleRow.Header);
                }

                // Stage 1: five ratios around the prior, 8 seeds each.
                foreach (Cell cell in _cells)
                {
                    double centre = System.Math.Log(cell.A.Prior / cell.B.Prior);
                    foreach (double multiplier in Stage1Multipliers)
                    {
                        (int a, int b) = Choose(cell, centre + System.Math.Log(multiplier));
                        cell.AddPoint(a, b, Stage1Seeds);
                    }
                }
                List<(int A, int B, int Seeds)>[] stage1 = _cells.Select(cell => cell.Points.ToList()).ToArray();
                RunStage("stage 1", Tasks(_cells));

                // Stage 2: where each cell's stage-1 fit says the fight is 25%, 50% and 75% for A.
                foreach (Cell cell in _cells)
                {
                    List<BattleRow> rows = RowsOf(cell, stage1[cell.Index]);
                    foreach ((int a, int b) in PlanStage2(cell, rows, stage1[cell.Index]))
                    {
                        cell.AddPoint(a, b, Stage2Seeds);
                    }
                }
                RunStage("stage 2", Tasks(_cells));

                // Optional further rounds of the same placement, on all the battles so far. A cell whose
                // break-even sits far from the prior is one-sided or only bracketed coarsely after
                // stage 2, and each round narrows it.
                int rounds = System.Math.Max(0, SquadDuelDiagnostics.ReadInt("GAUNTLET_REFINE", 0));
                for (int round = 1; round <= rounds; round++)
                {
                    foreach (Cell cell in _cells)
                    {
                        List<(int A, int B, int Seeds)> existing = cell.Points.ToList();
                        foreach ((int a, int b) in PlanStage2(cell, RowsOf(cell, existing), existing))
                        {
                            cell.AddPoint(a, b, Stage2Seeds);
                        }
                    }
                    RunStage($"refine {round}", Tasks(_cells));
                }

                (long battlesAfter, long turnsAfter) = SquadDuelDiagnostics.Throughput;
                List<BattleRow> design = _cells.SelectMany(cell => RowsOf(cell, cell.Points)).ToList();
                GauntletFit fit = GauntletAnalysis.Analyze(design, _entries.Select(entry => entry.Key).ToList());
                string header = $"Gauntlet '{_tag}': db {_db}, code {_code}, range {_range} yd, aggression {_aggression}, roles {_roles}, cap {_cap} models.{Environment.NewLine}"
                    + $"Entries: {string.Join(", ", _entries.Select(e => $"{e.Key}={e.Spec} ({e.Models} models)"))}{Environment.NewLine}"
                    + $"References: {string.Join(", ", _refs)}.";
                GauntletAnalysis.WriteReport(fit, _directory, _tag, header);

                _clock.Stop();
                File.WriteAllText(
                    Path.Combine(_directory, $"throughput-{_tag}.txt"),
                    SquadDuelDiagnostics.DescribeThroughput(battlesAfter - battlesBefore, turnsAfter - turnsBefore, _clock.Elapsed)
                    + Environment.NewLine
                    + $"{battlesAfter - battlesBefore} fresh battles; {design.Count(row => _loaded.Contains(row.Key))} of the "
                    + $"{design.Count} battles in the design came from the cache." + Environment.NewLine);
            }
        }

        // ----- Design -----

        /// <summary>
        /// Chooses whole-squad counts (a of A, b of B) for a target log of B models per A model: the pair
        /// within the cap whose ratio is nearest, preferring small battles when two fit about equally.
        /// </summary>
        private (int A, int B) Choose(Cell cell, double modelsX)
        {
            int maxA = System.Math.Max(1, _cap / cell.A.Models);
            int maxB = System.Math.Max(1, _cap / cell.B.Models);
            double target = System.Math.Clamp(
                modelsX + System.Math.Log((double)cell.A.Models / cell.B.Models), -System.Math.Log(maxA), System.Math.Log(maxB));
            (int A, int B) best = (1, 1);
            double bestCost = double.MaxValue;
            for (int a = 1; a <= maxA; a++)
            {
                for (int b = 1; b <= maxB; b++)
                {
                    double error = System.Math.Abs(System.Math.Log((double)b / a) - target);
                    double cost = error + 0.0004 * (a * cell.A.Models + b * cell.B.Models);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = (a, b);
                    }
                }
            }
            return best;
        }

        private List<(int A, int B)> PlanStage2(Cell cell, List<BattleRow> rows, List<(int A, int B, int Seeds)> stage1)
        {
            CellFit fit = GauntletAnalysis.FitCell(cell.A.Key, cell.B.Key, rows);
            double minX = fit.MinX;
            double maxX = fit.MaxX;
            List<double> targets = [];

            bool allA = fit.Points.All(point => point.MeanScore >= OneSidedScore);
            bool allB = fit.Points.All(point => point.MeanScore <= 1 - OneSidedScore);
            if (allA || allB)
            {
                // No crossing yet: step outward from the grid edge, toward more B if A won everywhere.
                double edge = allA ? maxX : minX;
                double direction = allA ? 1 : -1;
                for (int step = 1; step <= 3; step++)
                {
                    targets.Add(edge + direction * step * System.Math.Log(StepFactor));
                }
                List<(int A, int B)> outward = [];
                foreach (double x in targets)
                {
                    (int a, int b) = Choose(cell, x);
                    // At the cap the step lands back on a stage-1 point, which has nothing new to learn.
                    if (!stage1.Any(point => point.A == a && point.B == b) && !outward.Contains((a, b)))
                    {
                        outward.Add((a, b));
                    }
                }
                return outward;
            }

            double centre = System.Math.Log(cell.A.Prior / cell.B.Prior);
            double[] z = [-ProbeZ, 0.0, ProbeZ];
            double slack = System.Math.Log(StepFactor);
            foreach (double zp in z)
            {
                // The fit says P(A) = Phi(alpha + beta x), so the score p sits at x = (z_p - alpha) / beta.
                double x = fit.Fit != null && fit.Beta < -0.1
                    ? (zp - fit.Alpha) / fit.Beta
                    : centre - zp / ProbeZ * System.Math.Log(2.0);
                targets.Add(System.Math.Clamp(x, minX - slack, maxX + slack));
            }

            List<(int A, int B)> chosen = [];
            foreach (double x in targets)
            {
                (int a, int b) = Choose(cell, x);
                if (!chosen.Contains((a, b)))
                {
                    chosen.Add((a, b));
                }
            }
            return chosen;
        }

        // ----- Running battles -----

        /// <summary>
        /// A battle with A attacking keeps the key it had before roles were recorded, so older rows (all
        /// of them A-attacks) are reused under "a-attacks" and for the odd seeds of "alternate". A battle
        /// with A defending carries a "|bAtt" suffix, so it can never be confused with one of them.
        /// </summary>
        private string KeyOf(Cell cell, int a, int b, int seed) =>
            $"{_db}|{_code}|{_range}|{_aggression}|{cell.A.Spec},x={a}|{cell.B.Spec},x={b}|s{seed}"
            + (_roles.AAttacksOn(seed) ? "" : "|bAtt");

        private List<BattleTask> Tasks(List<Cell> cells)
        {
            List<BattleTask> tasks = [];
            HashSet<string> seen = [];
            foreach (Cell cell in cells)
            {
                foreach ((int a, int b, int seeds) in cell.Points)
                {
                    for (int seed = 1; seed <= seeds; seed++)
                    {
                        string key = KeyOf(cell, a, b, seed);
                        if (seen.Add(key))
                        {
                            tasks.Add(new BattleTask(cell, a, b, seed, key, a * cell.A.Models + b * cell.B.Models));
                        }
                    }
                }
            }
            return tasks;
        }

        private List<BattleRow> RowsOf(Cell cell, IEnumerable<(int A, int B, int Seeds)> points)
        {
            List<BattleRow> rows = [];
            foreach ((int a, int b, int seeds) in points)
            {
                for (int seed = 1; seed <= seeds; seed++)
                {
                    BattleRow row = _store[KeyOf(cell, a, b, seed)];
                    rows.Add(row with { EntryA = cell.A.Key, EntryB = cell.B.Key });
                }
            }
            return rows;
        }

        private void LoadStore()
        {
            foreach (BattleRow row in GauntletAnalysis.ReadRows(_directory))
            {
                _store.TryAdd(row.Key, row);
                _loaded.Add(row.Key);
            }
        }

        private void RunStage(string name, List<BattleTask> tasks)
        {
            List<BattleTask> pending = tasks.Where(task => !_store.ContainsKey(task.Key)).ToList();
            // Largest first, so the long battles start early and the short ones fill the tail.
            pending = pending
                .OrderByDescending(task => task.Models)
                .ThenBy(task => task.Cell.Index).ThenBy(task => task.A).ThenBy(task => task.B).ThenBy(task => task.Seed)
                .ToList();
            _done = 0;
            WriteProgress(name, 0, pending.Count);

            int planningWorkers = _workers > 1 ? 1 : 0;
            Parallel.ForEach(
                Partitioner.Create(pending, EnumerablePartitionerOptions.NoBuffering),
                new ParallelOptions { MaxDegreeOfParallelism = _workers },
                task =>
                {
                    bool aAttacks = _roles.AAttacksOn(task.Seed);
                    SquadDuelDiagnostics.BattleRecord record = SquadDuelDiagnostics.RunBattle(
                        _blob,
                        SquadDuelDiagnostics.ParseSide($"{task.Cell.A.Spec},x={task.A}"),
                        SquadDuelDiagnostics.ParseSide($"{task.Cell.B.Spec},x={task.B}"),
                        _range, task.Seed, _aggression, planningWorkers, aAttacks);
                    double score = record.Holder == BattleSide.Attacker ? 1.0 : record.Holder == BattleSide.Opposing ? 0.0 : 0.5;
                    BattleRow row = new(
                        task.Key, task.Cell.A.Key, task.Cell.B.Key, task.Cell.A.Spec, task.Cell.B.Spec,
                        task.A, task.B, record.ModelsA, record.ModelsB, task.Seed,
                        score, (double)record.ALeft / record.ModelsA, (double)record.BLeft / record.ModelsB,
                        record.Turns, record.Reason, _db, _code, _range, _aggression.ToString(),
                        aAttacks ? BattleRow.Attacker : BattleRow.Defender);
                    _store[task.Key] = row;
                    lock (_gate)
                    {
                        _writer.WriteLine(row.ToLine());
                        int done = ++_done;
                        if (done % 10 == 0 || done == pending.Count)
                        {
                            WriteProgress(name, done, pending.Count);
                        }
                    }
                });
        }

        private void WriteProgress(string stage, int done, int total) =>
            File.WriteAllText(
                Path.Combine(_directory, $"progress-{_tag}.txt"),
                $"{stage}: {done} of {total} fresh battles done after {_clock.Elapsed:hh\\:mm\\:ss}{Environment.NewLine}");
    }
}
