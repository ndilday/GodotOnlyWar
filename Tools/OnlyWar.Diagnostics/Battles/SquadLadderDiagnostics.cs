using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in calibration tool, not a correctness test. For each pair of squads it finds the break-even
/// force ratio: the number of B squads that makes A hold the field about half the time. The pair
/// table is the input to the Battle Value fit. Gated by RUN_LADDER.
///
/// Search. The first probe is placed at the ratio the current template Battle Values predict (the
/// summed value of an A squad over a B squad), corrected by per-template offsets learned from the
/// pairs already finished in this run. Each result then moves the next probe: a one-sided result
/// steps by its logit, a bracketed result interpolates in log-ratio against the logit of A's score.
/// The search stops when the bracket is narrower than 20% or repeats a probe it already ran, and a
/// pair that reaches the model cap without flipping is reported as censored.
///
/// Settings (environment):
///   LADDER_ENTRIES  '|'-separated "key=spec", where spec is one squad in SquadDuelDiagnostics syntax
///                   (for example "pdf=34|hyb=26|tac=1|ork=39"). A squad with no n= is built at its
///                   template maximum. Do not use x= here.
///   LADDER_PAIRS    "all" (every unordered pair, the default) or comma-separated "keyA:keyB".
///   LADDER_SEEDS    battles for a probe near even (default 24).
///   LADDER_SEEDS_FIRST  battles for every probe's first batch (default 8); a probe whose first
///                   batch scores outside 0.2 to 0.8 is not run any further.
///   LADDER_RANGE    starting range in yards (default 200).
///   LADDER_CAP      most models allowed on one side (default 200).
///   LADDER_AGGR     Aggression for both sides (default Aggressive).
///   LADDER_TAG      name for this run's output files, so several processes can split the pairs.
///   LADDER_OUT      output folder (default %TEMP%\GodotOnlyWar\ladder).
///   DUEL_WORKERS    battles of one probe run at once (default 8). Probes still run one after
///                   another, because each one decides where the next goes.
///
/// throughput-TAG.txt reports the battles and turns per minute so far, counting fresh battles only.
///
/// Every probe is cached in probes-TAG.tsv, keyed by the rules database hash and the scenario, and
/// every probes-*.tsv in the folder is read back, so an interrupted run resumes and a rules change
/// invalidates old results by itself. The pair table is pairs-TAG.tsv. The search is deterministic
/// for a given entry and pair order, so a rerun in the same order hits the cache for every probe.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SquadLadderDiagnostics
{
    private const int MaximumProbes = 8;
    private const double SearchSlope = 2.5;
    private const double MinimumStep = 0.4;
    private const double ConvergedWidth = 0.2;
    private const double OffsetLearningRate = 0.35;
    private const double NearEvenLow = 0.2;
    private const double NearEvenHigh = 0.8;

    private sealed record Probe(int A, int B, ProbeResult Result)
    {
        /// <summary>Log of B squads per A squad. A weakens as this grows.</summary>
        public double X => System.Math.Log((double)B / A);
        public double Score => Result.AScore;
    }

    private sealed record PairResult(
        string Status, double SquadRatio, Probe Low, Probe High, List<Probe> Probes, Probe Nearest);

    [Fact]
    public void Ladder_BuildsPairTable()
    {
        string entriesText = Environment.GetEnvironmentVariable("LADDER_ENTRIES");
        if (Environment.GetEnvironmentVariable("RUN_LADDER") == null || string.IsNullOrWhiteSpace(entriesText))
        {
            return;
        }

        int seeds = SquadDuelDiagnostics.ReadInt("LADDER_SEEDS", 24);
        int firstSeeds = System.Math.Min(seeds, SquadDuelDiagnostics.ReadInt("LADDER_SEEDS_FIRST", 8));
        int range = SquadDuelDiagnostics.ReadInt("LADDER_RANGE", 200);
        int cap = SquadDuelDiagnostics.ReadInt("LADDER_CAP", 200);
        Aggression aggression = SquadDuelDiagnostics.ReadAggression("LADDER_AGGR");
        string tag = Environment.GetEnvironmentVariable("LADDER_TAG") ?? "run";
        string directory = Environment.GetEnvironmentVariable("LADDER_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "ladder");
        Directory.CreateDirectory(directory);

        List<(string Key, string Spec)> entries = entriesText
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => text.Split('=', 2))
            .Select(parts => (parts[0].Trim(), parts[1].Trim()))
            .ToList();
        Dictionary<string, string> specByKey = entries.ToDictionary(entry => entry.Key, entry => entry.Spec);
        List<(string A, string B)> pairs = ReadPairs(entries.Select(entry => entry.Key).ToList());

        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        string dbHash;
        using (FileStream dbStream = new(
            RulesDatabaseFixture.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            dbHash = Convert.ToHexString(SHA256.HashData(dbStream))[..12];
        }
        ProbeCache cache = new(directory, tag);
        string prefix = $"{dbHash}|{range}|{aggression}|";

        // What a squad is worth, as the search sees it: its summed template Battle Value, nudged by
        // whatever the finished pairs have taught us about that template. Starting guesses only.
        Dictionary<string, (int Models, int Value)> measures = entries.ToDictionary(
            entry => entry.Key,
            entry => SquadDuelDiagnostics.Measure(blob, SquadDuelDiagnostics.ParseSide(entry.Spec)));
        Dictionary<string, double> offsets = entries.ToDictionary(entry => entry.Key, _ => 0.0);

        string pairsPath = Path.Combine(directory, $"pairs-{tag}.tsv");
        string throughputPath = Path.Combine(directory, $"throughput-{tag}.txt");
        (long battlesBefore, long turnsBefore) = SquadDuelDiagnostics.Throughput;
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        StringBuilder table = new();
        table.AppendLine(string.Join('\t',
            "a", "b", "status", "squads_b_per_a", "models_b_per_a", "models_per_squad_a", "models_per_squad_b",
            "prior_squads_b_per_a", "low_a", "low_b", "low_score", "high_a", "high_b", "high_score", "probes",
            "near_a_hold", "near_a_lost", "near_b_lost", "near_turns",
            "near_a_move", "near_a_fire", "near_a_swings", "near_b_move", "near_b_fire", "near_b_swings",
            "db", "seeds", "range", "aggression"));

        foreach ((string keyA, string keyB) in pairs)
        {
            string specA = specByKey[keyA];
            string specB = specByKey[keyB];
            (int modelsA, int valueA) = measures[keyA];
            (int modelsB, int valueB) = measures[keyB];

            ProbeResult Batch(int a, int b, int firstSeed, int count)
            {
                string key = $"{prefix}{specA},x={a}|{specB},x={b}|s{firstSeed}+{count}";
                if (cache.TryGet(key, out ProbeResult hit))
                {
                    return hit;
                }

                ProbeResult fresh = SquadDuelDiagnostics.RunProbe(
                    blob,
                    SquadDuelDiagnostics.ParseSide($"{specA},x={a}"),
                    SquadDuelDiagnostics.ParseSide($"{specB},x={b}"),
                    range, count, aggression, firstSeed);
                cache.Add(key, fresh);
                return fresh;
            }

            // A cheap first batch settles every probe that is clearly one-sided. Only a probe near
            // even gets the rest of the seeds, since only those decide where the break-even sits.
            ProbeResult Run(int a, int b)
            {
                ProbeResult first = Batch(a, b, 1, firstSeeds);
                if (seeds <= firstSeeds || first.AScore < NearEvenLow || first.AScore > NearEvenHigh)
                {
                    return first;
                }
                return ProbeResult.Merge(first, Batch(a, b, firstSeeds + 1, seeds - firstSeeds));
            }

            double prior = valueA > 0 && valueB > 0
                ? System.Math.Log((double)valueA / valueB) + offsets[keyA] - offsets[keyB]
                : 0;
            PairResult result = FindBreakEven(Run, modelsA, modelsB, cap, prior);
            if (result.Status.StartsWith("bracketed", StringComparison.Ordinal))
            {
                double error = System.Math.Log(result.SquadRatio) - prior;
                offsets[keyA] += OffsetLearningRate * error;
                offsets[keyB] -= OffsetLearningRate * error;
            }

            Probe near = result.Nearest;
            table.AppendLine(string.Join('\t',
                keyA, keyB, result.Status,
                F(result.SquadRatio), F(result.SquadRatio * modelsB / modelsA), modelsA, modelsB,
                F(System.Math.Exp(prior)),
                result.Low?.A, result.Low?.B, F(result.Low?.Score), result.High?.A, result.High?.B, F(result.High?.Score),
                result.Probes.Count,
                F(near.Result.AHold), F(near.Result.ALost), F(near.Result.BLost), F(near.Result.Turns),
                F(near.Result.A.Move), F(near.Result.A.Fire), F(near.Result.A.SwingsPerBattle),
                F(near.Result.B.Move), F(near.Result.B.Fire), F(near.Result.B.SwingsPerBattle),
                dbHash, seeds, range, aggression));
            File.WriteAllText(pairsPath, table.ToString());

            // Fresh battles only: a probe served from the cache costs nothing and is not counted.
            (long battlesNow, long turnsNow) = SquadDuelDiagnostics.Throughput;
            File.WriteAllText(throughputPath, SquadDuelDiagnostics.DescribeThroughput(
                battlesNow - battlesBefore, turnsNow - turnsBefore, clock.Elapsed) + Environment.NewLine);
        }
    }

    private static List<(string A, string B)> ReadPairs(List<string> keys)
    {
        string text = Environment.GetEnvironmentVariable("LADDER_PAIRS");
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return keys.SelectMany((a, i) => keys.Skip(i + 1).Select(b => (a, b))).ToList();
        }

        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split(':'))
            .Select(parts => (parts[0], parts[1]))
            .ToList();
    }

    /// <summary>
    /// Searches the log of the squad ratio for the point where A's score crosses one half, starting
    /// at <paramref name="priorX"/>. Counts are whole squads, so each target is rounded to the
    /// nearest (a, b) that fits under the cap, preferring small battles when two fit equally well.
    /// </summary>
    private static PairResult FindBreakEven(
        Func<int, int, ProbeResult> run, int modelsA, int modelsB, int cap, double priorX)
    {
        int maxA = System.Math.Max(1, cap / modelsA);
        int maxB = System.Math.Max(1, cap / modelsB);
        double xMin = -System.Math.Log(maxA);
        double xMax = System.Math.Log(maxB);

        List<Probe> probes = [];
        double target = System.Math.Clamp(priorX, xMin, xMax);
        for (int iteration = 0; iteration < MaximumProbes; iteration++)
        {
            (int a, int b) = ChooseCounts(target, maxA, maxB, modelsA, modelsB, cap);
            if (probes.Any(existing => existing.A == a && existing.B == b))
            {
                break;
            }

            Probe probe = new(a, b, run(a, b));
            probes.Add(probe);

            Probe strong = probes.Where(p => p.Score >= 0.5).OrderByDescending(p => p.X).FirstOrDefault();
            Probe weak = probes.Where(p => p.Score < 0.5).OrderBy(p => p.X).FirstOrDefault();
            if (strong != null && weak != null)
            {
                if (strong.X >= weak.X || weak.X - strong.X < ConvergedWidth)
                {
                    break;
                }
                target = Interpolate(strong, weak);
            }
            else
            {
                double step = Logit(probe.Score) / SearchSlope;
                if (System.Math.Abs(step) < MinimumStep)
                {
                    step = probe.Score >= 0.5 ? MinimumStep : -MinimumStep;
                }
                target = System.Math.Clamp(probe.X + step, xMin, xMax);
            }
        }

        Probe nearest = probes.OrderBy(p => System.Math.Abs(p.Score - 0.5)).First();
        Probe low = probes.Where(p => p.Score >= 0.5).OrderByDescending(p => p.X).FirstOrDefault();
        Probe high = probes.Where(p => p.Score < 0.5).OrderBy(p => p.X).FirstOrDefault();
        if (low == null)
        {
            // A never held half the time, even against the fewest B squads the cap allows.
            return new PairResult("censored_b_stronger", System.Math.Exp(high.X), null, high, probes, nearest);
        }
        if (high == null)
        {
            return new PairResult("censored_a_stronger", System.Math.Exp(low.X), low, null, probes, nearest);
        }
        if (low.X >= high.X)
        {
            return new PairResult(
                "bracketed_noisy", System.Math.Exp((low.X + high.X) / 2), low, high, probes, nearest);
        }
        return new PairResult("bracketed", System.Math.Exp(Interpolate(low, high)), low, high, probes, nearest);
    }

    private static (int A, int B) ChooseCounts(double x, int maxA, int maxB, int modelsA, int modelsB, int cap)
    {
        (int A, int B) best = (1, 1);
        double bestCost = double.MaxValue;
        for (int a = 1; a <= System.Math.Min(maxA, 12); a++)
        {
            int centre = (int)System.Math.Round(a * System.Math.Exp(x));
            for (int b = centre - 1; b <= centre + 1; b++)
            {
                if (b < 1 || b > maxB)
                {
                    continue;
                }

                double error = System.Math.Abs(System.Math.Log((double)b / a) - x);
                double cost = error + 0.0004 * (a * modelsA + b * modelsB);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = (a, b);
                }
            }
        }
        return best;
    }

    /// <summary>The log-ratio where the logit of A's score reaches zero, between a strong and a weak probe.</summary>
    private static double Interpolate(Probe strong, Probe weak)
    {
        double strongLogit = Logit(strong.Score);
        double weakLogit = Logit(weak.Score);
        return System.Math.Abs(strongLogit - weakLogit) < 1e-9
            ? (strong.X + weak.X) / 2
            : strong.X + (0 - strongLogit) * (weak.X - strong.X) / (weakLogit - strongLogit);
    }

    private static double Logit(double score)
    {
        double clamped = System.Math.Clamp(score, 0.03, 0.97);
        return System.Math.Log(clamped / (1 - clamped));
    }

    private static string F(double? value) =>
        value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";

    /// <summary>Probe results keyed by scenario, loaded from and appended to tab-separated files.</summary>
    private sealed class ProbeCache
    {
        private readonly Dictionary<string, ProbeResult> _results = [];
        private readonly string _path;

        public ProbeCache(string directory, string tag)
        {
            _path = Path.Combine(directory, $"probes-{tag}.tsv");
            foreach (string file in Directory.GetFiles(directory, "probes-*.tsv"))
            {
                foreach (string line in File.ReadLines(file))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length == 28 && !_results.ContainsKey(parts[0]))
                    {
                        _results[parts[0]] = Parse(parts);
                    }
                }
            }
        }

        public bool TryGet(string key, out ProbeResult result) => _results.TryGetValue(key, out result);

        public void Add(string key, ProbeResult result)
        {
            _results[key] = result;
            File.AppendAllText(_path, Format(key, result) + Environment.NewLine);
        }

        private static string Format(string key, ProbeResult r) => string.Join('\t',
            new string[]
            {
                key, N(r.AHold), N(r.BHold), N(r.Undecided), N(r.ALost), N(r.BLost), N(r.Turns),
                r.MaxTurns.ToString(CultureInfo.InvariantCulture), r.ModelsA.ToString(CultureInfo.InvariantCulture),
                r.ModelsB.ToString(CultureInfo.InvariantCulture), r.Reasons.Replace('\t', ' ')
            }
            .Concat(Behavior(r.A)).Concat(Behavior(r.B)).Append(r.Seeds.ToString(CultureInfo.InvariantCulture)));

        private static IEnumerable<string> Behavior(BehaviorSummary b) =>
            [N(b.Fire), N(b.Move), N(b.Aim), N(b.Reload), N(b.HitRate), N(b.ShotsPerVolley), N(b.MeanRange), N(b.SwingsPerBattle)];

        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static double D(string text) => double.Parse(text, CultureInfo.InvariantCulture);

        private static BehaviorSummary ParseBehavior(string[] p, int at) => new()
        {
            Fire = D(p[at]), Move = D(p[at + 1]), Aim = D(p[at + 2]), Reload = D(p[at + 3]),
            HitRate = D(p[at + 4]), ShotsPerVolley = D(p[at + 5]), MeanRange = D(p[at + 6]), SwingsPerBattle = D(p[at + 7])
        };

        private static ProbeResult Parse(string[] p) => new()
        {
            AHold = D(p[1]), BHold = D(p[2]), Undecided = D(p[3]), ALost = D(p[4]), BLost = D(p[5]),
            Turns = D(p[6]), MaxTurns = int.Parse(p[7], CultureInfo.InvariantCulture),
            ModelsA = int.Parse(p[8], CultureInfo.InvariantCulture), ModelsB = int.Parse(p[9], CultureInfo.InvariantCulture),
            Reasons = p[10], A = ParseBehavior(p, 11), B = ParseBehavior(p, 19),
            Seeds = int.Parse(p[27], CultureInfo.InvariantCulture)
        };
    }
}
