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
/// every cell at five force ratios, the prior ratio times 1/4, 1/2, 1, 2 and 4, 8 seeds each. Stage 2
/// runs in rounds (2026-10-25): each round refits every cell and the single scale on all the battles so
/// far, and gives every unresolved cell 4 more seeds at three sizes, where its own probit puts A's
/// result at 25%, 50% and 75%, or, for a cell with no usable probit (one-sided, extrapolated), around
/// the break-even the single scale predicts from both squads' other matchups. A cell is resolved when
/// its break-even's standard error falls under GAUNTLET_TARGET_SE, or when the prediction lies more
/// than a factor of two beyond the largest battle the cap allows; the fit then uses its battles as a
/// bound (censored least squares). Ratios are realized as whole squads (a of A against b of
/// B), at most GAUNTLET_CAP models a side, preferring small battles. All the battles of a stage run
/// in one pool, largest first, so a long battle never leaves cores idle behind it.
///
/// Settings (environment):
///   GAUNTLET_ENTRIES  '|'-separated "key=spec", one squad each in SquadDuelDiagnostics syntax (for
///                     example "pdf=34|hyb=26|tac=1|ork=39"). A squad with no n= is built at its
///                     template maximum. Do not use x= here.
///   GAUNTLET_PANEL    comma-separated entry keys forming the panel (Design/Active/GauntletV2.md section 1):
///                     every entry fights every panel squad of ANOTHER side, so panel squads also fight
///                     each other. Takes precedence over GAUNTLET_REFS.
///   GAUNTLET_ALLIES   faction ids that share a side, '|'-separated groups of '+'-joined ids (default
///                     "0+1": PDF with Space Marines; every other faction is its own side). Used by
///                     the panel design only.
///   GAUNTLET_REFS     the v1 design: comma-separated entry keys that every entry fights, whatever its
///                     side (for example "pdf,hyb").
///   GAUNTLET_PRIOR    optional "key=value|..." prior per-model value of an entry; the default is the
///                     summed template Battle Value per model. It only places the stage-1 ratios.
///   GAUNTLET_ROUNDS   most stage-2 rounds (default 6); each gives an unresolved cell 4 seeds at
///                     three sizes.
///   GAUNTLET_TARGET_SE standard error of ln(break-even) at which a cell needs no more rounds
///                     (default 0.2; the matchup sd tau was 0.58 on the *9 data).
///   GAUNTLET_MIRROR   1 (default) fights both orientations of a pair of references, so A-versus-B and
///                     B-versus-A are separate cells; 0 keeps only the first.
///   GAUNTLET_RANGE    "assault" (default): each battle opens where a reciprocal assault between the two
///                     forces would, with that setup's reallocation, burrowers and Attacker profiles
///                     (SquadDuelDiagnostics.AssaultOpeningRange); or a fixed range in yards, the v1
///                     design (the *9 runs used 200).
///   GAUNTLET_AGGR     Aggression for both sides (default Aggressive).
///   GAUNTLET_ROLES    which side is the battle's Attacker (BattleRole.Attacker, placed at x = 0): "alternate"
///                     (default; A attacks on odd seeds, B on even seeds, so each cell and ratio is balanced),
///                     "a-attacks" (A always, as before roles were recorded) or "b-attacks". The two sides'
///                     squads are built in slot order, so a swapped battle is the same battle with the
///                     labels exchanged. The fit reports the attacker role as a coefficient (see below).
///   GAUNTLET_CAP      most models allowed on one side (default 200).
///   GAUNTLET_CAP_MAX  the cap a cell gets once its stage-1 battles come back one-sided (default 1000), so a
///                     single monster can be fought against enough cheap squads to find its break-even.
///   GAUNTLET_TAG      name for this run's output files (default "run").
///   GAUNTLET_OUT      output folder (default %TEMP%\GodotOnlyWar\gauntlet).
///   DUEL_WORKERS      battles run at once (default half the logical processors).
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
    // Stage 2 rounds (2026-10-25): seeds a size gets per round (even, so the roles stay balanced), the
    // most rounds, and the standard error of ln(break-even) at which a cell is resolved. tau, the
    // matchup sd, was 0.58 on the *9 data, so a cell sharper than 0.2 adds little to the fit.
    private const int RoundSeeds = 4;
    private const int DefaultRounds = 6;
    private const double DefaultTargetSe = 0.2;
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
            + $"range {SquadDuelDiagnostics.RangeText(first.Range)}, aggression {first.Aggression}.{Environment.NewLine}{note}";
        GauntletAnalysis.WriteReport(GauntletAnalysis.Analyze(rows), directory, tag, header);
    }

    private sealed record Entry(string Key, string Spec, int Models, double Prior, int Side);

    private sealed class Cell
    {
        public int Index;
        public Entry A;
        public Entry B;

        /// <summary>Most models a side may field in this cell; raised for one-sided cells (GAUNTLET_CAP_MAX).</summary>
        public int Cap;

        /// <summary>The (a, b) squad counts to fight and how many seeds each gets.</summary>
        public List<(int A, int B, int Seeds)> Points = [];

        /// <summary>No more stage-2 rounds: its break-even is sharp enough, or beyond any allowed size.</summary>
        public bool Resolved;

        /// <summary>Why the cell was resolved, for the report.</summary>
        public string ResolvedBy;

        /// <summary>Adds <paramref name="seeds"/> more seeds at (a, b), after any it already has.</summary>
        public void AddSeeds(int a, int b, int seeds)
        {
            int at = Points.FindIndex(point => point.A == a && point.B == b);
            if (at < 0)
            {
                Points.Add((a, b, seeds));
            }
            else
            {
                Points[at] = (a, b, Points[at].Seeds + seeds);
            }
        }

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
        private readonly int _capMax;
        private readonly double _targetSe;
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
            _range = SquadDuelDiagnostics.ParseRange(
                Environment.GetEnvironmentVariable("GAUNTLET_RANGE") ?? SquadDuelDiagnostics.AssaultRangeText);
            _cap = SquadDuelDiagnostics.ReadInt("GAUNTLET_CAP", 200);
            _capMax = System.Math.Max(_cap, SquadDuelDiagnostics.ReadInt("GAUNTLET_CAP_MAX", 1000));
            _targetSe = double.TryParse(Environment.GetEnvironmentVariable("GAUNTLET_TARGET_SE"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double targetSe) ? targetSe : DefaultTargetSe;
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

            Dictionary<int, int> sideOfFaction = ReadAllies(
                Environment.GetEnvironmentVariable("GAUNTLET_ALLIES") ?? DefaultAllies);
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
                List<SquadSpec> squads = SquadDuelDiagnostics.ParseSide(spec);
                (int models, int value) = SquadDuelDiagnostics.Measure(_blob, squads);
                double prior = priors.TryGetValue(key, out double given) ? given : (double)value / models;
                int faction = _blob.Factions.First(f => f.SquadTemplates.ContainsKey(squads[0].TemplateId)).Id;
                _entries.Add(new Entry(key, spec, models, prior, sideOfFaction.GetValueOrDefault(faction, faction)));
            }
            foreach (string key in priors.Keys.Where(key => _entries.All(entry => entry.Key != key)))
            {
                throw new ArgumentException($"GAUNTLET_PRIOR names '{key}', which is not an entry.");
            }

            // PANEL design (v2): every entry fights every panel squad of another side, so the panel
            // squads also fight one another. REFS design (v1): every entry fights every reference,
            // whatever its side.
            string panelText = Environment.GetEnvironmentVariable("GAUNTLET_PANEL");
            bool panel = !string.IsNullOrWhiteSpace(panelText);
            string refText = panel ? panelText : Environment.GetEnvironmentVariable("GAUNTLET_REFS") ?? "";
            _refs = refText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (_refs.Count == 0)
            {
                throw new ArgumentException("GAUNTLET_PANEL (or GAUNTLET_REFS) must name at least one entry key.");
            }
            foreach (string key in _refs.Where(key => _entries.All(entry => entry.Key != key)))
            {
                throw new ArgumentException($"{(panel ? "GAUNTLET_PANEL" : "GAUNTLET_REFS")} names '{key}', which is not an entry.");
            }
            _design = panel ? "panel" : "references";

            bool mirror = SquadDuelDiagnostics.ReadInt("GAUNTLET_MIRROR", 1) != 0;
            foreach (Entry a in _entries)
            {
                foreach (string refKey in _refs)
                {
                    Entry b = _entries.First(entry => entry.Key == refKey);
                    if (a.Key == b.Key || (panel && a.Side == b.Side))
                    {
                        continue;
                    }
                    if (!mirror && _cells.Any(cell => cell.A.Key == b.Key && cell.B.Key == a.Key))
                    {
                        continue;
                    }
                    _cells.Add(new Cell { Index = _cells.Count, A = a, B = b, Cap = _cap });
                }
            }
            foreach (Entry orphan in _entries.Where(entry => _cells.All(cell => cell.A != entry && cell.B != entry)))
            {
                throw new ArgumentException($"Entry '{orphan.Key}' has no opponent: no panel squad is on another side.");
            }
        }

        /// <summary>
        /// Factions that fight on one side and never against each other: the Imperium's PDF (0) and
        /// Space Marines (1), allied in the game (Sector sets the player and default faction Allied).
        /// The Tyranids (2) and the Genestealer Cult (3) are hostile to each other in the game, so
        /// since 2026-10-27 they are separate sides (Design/Active/GauntletV2.md section 1).
        /// </summary>
        private const string DefaultAllies = "0+1";

        private readonly string _design;

        /// <summary>Parses GAUNTLET_ALLIES ("0+1|2+3"): each group's factions share the group's first id as their side.</summary>
        private static Dictionary<int, int> ReadAllies(string text)
        {
            Dictionary<int, int> side = [];
            foreach (string group in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int[] ids = group.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToArray();
                foreach (int id in ids)
                {
                    side[id] = ids[0];
                }
            }
            return side;
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
                RunStage("stage 1", Tasks(_cells));

                // Stage 2, in rounds (2026-10-25). Each round refits every cell and the single scale on
                // all the battles so far, gives every unresolved cell RoundSeeds more seeds at the sizes
                // its estimate points to, and runs all of them in one pool. A cell is resolved when its
                // break-even's standard error is under the target, or when the scale puts its break-even
                // beyond any size the cap allows (it then stays a bound for the fit). Before, stage 2 gave
                // every cell three sizes at 16 seeds at once, and a one-sided cell stepped blindly outward
                // by factors of four up to 1000 models a side: 744 Gretchin against 16 Brood Brothers,
                // hours a battle, still lost.
                int rounds = System.Math.Max(0, SquadDuelDiagnostics.ReadInt("GAUNTLET_ROUNDS", DefaultRounds));
                for (int round = 1; round <= rounds; round++)
                {
                    List<BattleRow> all = _cells.SelectMany(cell => RowsOf(cell, cell.Points)).ToList();
                    Dictionary<string, double> scale = GauntletAnalysis
                        .Analyze(all, _entries.Select(entry => entry.Key).ToList())
                        .LeastSquares.Values.ToDictionary(value => value.Key, value => value.U);
                    int planned = 0;
                    foreach (Cell cell in _cells.Where(cell => !cell.Resolved))
                    {
                        List<(int A, int B)> next = PlanRound(cell, RowsOf(cell, cell.Points), scale);
                        foreach ((int a, int b) in next)
                        {
                            cell.AddSeeds(a, b, RoundSeeds);
                            planned++;
                        }
                    }
                    if (planned == 0)
                    {
                        break;
                    }
                    RunStage($"stage 2 round {round}", Tasks(_cells));
                }

                (long battlesAfter, long turnsAfter) = SquadDuelDiagnostics.Throughput;
                List<BattleRow> design = _cells.SelectMany(cell => RowsOf(cell, cell.Points)).ToList();
                GauntletFit fit = GauntletAnalysis.Analyze(design, _entries.Select(entry => entry.Key).ToList());
                string rangeText = _range <= SquadDuelDiagnostics.AssaultRange
                    ? $"assault opening (mean {design.Average(row => row.OpeningRange):N0} yd, "
                        + $"{design.Min(row => row.OpeningRange)}-{design.Max(row => row.OpeningRange)})"
                    : $"range {_range} yd";
                string resolved = string.Join(", ", _cells
                    .GroupBy(cell => cell.ResolvedBy ?? "unresolved after the last round")
                    .Select(group => $"{group.Key} {group.Count()}"));
                string header = $"Gauntlet '{_tag}': db {_db}, code {_code}, {rangeText}, aggression {_aggression}, roles {_roles}, cap {_cap} (raised to {_capMax} for one-sided cells) models.{Environment.NewLine}"
                    + $"Stage 2 rounds: target se(ln break-even) {_targetSe}; cells {resolved}.{Environment.NewLine}"
                    + $"Entries: {string.Join(", ", _entries.Select(e => $"{e.Key}={e.Spec} ({e.Models} models, side {e.Side})"))}{Environment.NewLine}"
                    + $"{(_design == "panel" ? "Panel" : "References")}: {string.Join(", ", _refs)}.";
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
            int maxA = System.Math.Max(1, cell.Cap / cell.A.Models);
            int maxB = System.Math.Max(1, cell.Cap / cell.B.Models);
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

        /// <summary>
        /// The sizes one stage-2 round fights for a cell, or none when the cell is resolved.
        ///
        /// <para>A cell with a usable probit gets the sizes where it puts A's result at 25%, 50% and
        /// 75%, as stage 2 always did. A cell without one (one-sided, extrapolated, bracketed, too few
        /// sizes) is aimed by the single scale fitted to every cell so far: its break-even is predicted
        /// as u_A - u_B (<paramref name="scale"/>), which the other matchups of both squads pin down,
        /// kept at least a factor of two past any bound its own battles set. A prediction more than a
        /// factor of two beyond the largest battle the cap allows resolves the cell as a bound, which
        /// the fit still uses (GauntletAnalysis.FitLeastSquares).</para>
        /// </summary>
        private List<(int A, int B)> PlanRound(Cell cell, List<BattleRow> rows, IReadOnlyDictionary<string, double> scale)
        {
            CellFit fit = GauntletAnalysis.FitCell(cell.A.Key, cell.B.Key, rows);
            if ((fit.Status == "ok" || fit.Status == "bracketed") && fit.SeX0 <= _targetSe)
            {
                Resolve(cell, "sharp");
                return [];
            }

            double half = System.Math.Log(2.0);
            double slack = System.Math.Log(StepFactor);
            List<double> targets = [];
            if (fit.Status == "ok" && fit.Beta < -0.1)
            {
                // The fit says P(A) = Phi(alpha + beta x), so the score p sits at x = (z_p - alpha) / beta.
                foreach (double z in new[] { -ProbeZ, 0.0, ProbeZ })
                {
                    targets.Add(System.Math.Clamp((z - fit.Alpha) / fit.Beta, fit.MinX - slack, fit.MaxX + slack));
                }
            }
            else
            {
                double? predicted = scale.TryGetValue(cell.A.Key, out double uA) && scale.TryGetValue(cell.B.Key, out double uB)
                    ? uA - uB
                    : null;
                double centre;
                if (fit.Status == "bracketed")
                {
                    centre = fit.X0;
                }
                else if (double.IsFinite(fit.LowerX))
                {
                    centre = System.Math.Max(predicted ?? fit.LowerX + slack, fit.LowerX + half);
                }
                else if (double.IsFinite(fit.UpperX))
                {
                    centre = System.Math.Min(predicted ?? fit.UpperX - slack, fit.UpperX - half);
                }
                else
                {
                    centre = predicted ?? System.Math.Log(cell.A.Prior / cell.B.Prior);
                }

                // A break-even past the base cap needs bigger battles; one far past the largest the cap
                // allows is left as a bound.
                if (centre > Reach(cell, cell.Cap).Max || centre < Reach(cell, cell.Cap).Min)
                {
                    cell.Cap = _capMax;
                }
                (double minReach, double maxReach) = Reach(cell, cell.Cap);
                if (centre > maxReach + half || centre < minReach - half)
                {
                    Resolve(cell, "beyond reach");
                    return [];
                }
                targets.AddRange([centre - half, centre, centre + half]);
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

        /// <summary>The smallest and largest ln(B models per A model) a cap allows in this cell.</summary>
        private static (double Min, double Max) Reach(Cell cell, int cap)
        {
            int maxA = System.Math.Max(1, cap / cell.A.Models);
            int maxB = System.Math.Max(1, cap / cell.B.Models);
            return (
                System.Math.Log((double)cell.B.Models / (maxA * cell.A.Models)),
                System.Math.Log((double)(maxB * cell.B.Models) / cell.A.Models));
        }

        private static void Resolve(Cell cell, string why)
        {
            cell.Resolved = true;
            cell.ResolvedBy = why;
        }

        // ----- Running battles -----

        /// <summary>
        /// A battle with A attacking keeps the key it had before roles were recorded, so older rows (all
        /// of them A-attacks) are reused under "a-attacks" and for the odd seeds of "alternate". A battle
        /// with A defending carries a "|bAtt" suffix, so it can never be confused with one of them.
        /// </summary>
        private string KeyOf(Cell cell, int a, int b, int seed) =>
            $"{_db}|{_code}|{SquadDuelDiagnostics.RangeText(_range)}|{_aggression}|{cell.A.Spec},x={a}|{cell.B.Spec},x={b}|s{seed}"
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
                    double aLeft = (double)record.ALeft / record.ModelsA;
                    double bLeft = (double)record.BLeft / record.ModelsB;
                    double score = BattleRow.ScoreOf(
                        record.Reason,
                        record.Holder == BattleSide.Attacker ? 1.0 : record.Holder == BattleSide.Opposing ? 0.0 : 0.5,
                        aLeft, bLeft);
                    BattleRow row = new(
                        task.Key, task.Cell.A.Key, task.Cell.B.Key, task.Cell.A.Spec, task.Cell.B.Spec,
                        task.A, task.B, record.ModelsA, record.ModelsB, task.Seed,
                        score, aLeft, bLeft,
                        record.Turns, record.Reason, _db, _code, _range, _aggression.ToString(),
                        aAttacks ? BattleRow.Attacker : BattleRow.Defender, record.OpeningRange);
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
                $"{stage}: {done} of {total} fresh battles done after {SquadDuelDiagnostics.FormatElapsed(_clock.Elapsed)}{Environment.NewLine}");
    }
}
