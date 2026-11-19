using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Battles;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Generation.World;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in calibration tool for the opening-range refactor (Design/Reference/ForecastCalibration.md section 1,
/// section 4): for a panel of matchups and a ladder of opening ranges, what each side inflicts and
/// suffers when a reciprocal assault is forced to open at that range, with melee contact and the
/// rounds each side has left at contact and at the end. Phase 0 measures the current code; later
/// phases print the forecast beside it.
///
/// Gated by RUN_APPROACH_CURVES. Settings:
///   APPROACH_PANEL   '#'-separated "label=A vs B" in duel side syntax (default: the design's panel).
///   APPROACH_RANGES  comma-separated opening ranges in yards (default 12,25,50,100,150,200,300,450,600).
///   APPROACH_SEEDS   battles per point (default 16), roles alternating.
///   APPROACH_OUT     output folder (default %TEMP%\GodotOnlyWar\approach); approach-curves.tsv is one row
///                    per point and approach-curves.txt the readable table.
/// DUEL_WORKERS and DUEL_AGGR apply as in the duel tool.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ApproachCurveDiagnostics
{
    private const string DefaultPanel =
        "pdf-v-horm=17,x=3 vs 34,x=2"
        + "#pdf-v-gs=16 vs 34,x=2"
        + "#pdf-v-kom=44,x=4 vs 34,x=3"
        + "#pdf-v-carn=29 vs 34,x=10"
        + "#tac-v-pdf=1 vs 34,x=9"
        + "#shoota-v-tac=39,x=3 vs 1"
        + "#mob-v-tac=36,x=10 vs 1"
        + "#tac-v-horm=1 vs 17,x=4";

    private const string DefaultRanges = "12,25,50,100,150,200,300,450,600";

    [Fact]
    public void ApproachCurves_ReportCasualtiesByOpeningRange()
    {
        if (Environment.GetEnvironmentVariable("RUN_APPROACH_CURVES") == null)
        {
            return;
        }

        string panelText = Environment.GetEnvironmentVariable("APPROACH_PANEL") ?? DefaultPanel;
        int[] ranges = (Environment.GetEnvironmentVariable("APPROACH_RANGES") ?? DefaultRanges)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => int.Parse(text, CultureInfo.InvariantCulture))
            .ToArray();
        int seeds = SquadDuelDiagnostics.ReadInt("APPROACH_SEEDS", 16);
        Aggression aggression = SquadDuelDiagnostics.ReadAggression("DUEL_AGGR");
        string directory = Environment.GetEnvironmentVariable("APPROACH_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "approach");
        Directory.CreateDirectory(directory);
        string tsvPath = Path.Combine(directory, "approach-curves.tsv");
        string textPath = Path.Combine(directory, "approach-curves.txt");

        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        StringBuilder tsv = new();
        tsv.AppendLine(string.Join('\t', "matchup", "scenario", "range", "seeds", "aHolds", "bHolds", "undecided",
            "aLost", "bLost", "turns", "maxTurns", "contactShare", "contactTurn",
            "roundsA", "roundsLeftAtContactA", "roundsLeftAtEndA",
            "roundsB", "roundsLeftAtContactB", "roundsLeftAtEndB", "preferenceA", "preferenceB"));
        StringBuilder text = new();
        text.AppendLine("Casualties by forced assault opening range. inflicted by A = B's losses; by B = A's losses. "
            + "Rounds: share of the rounds carried at the start still left at first melee contact / at the end.");

        foreach (string item in panelText.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = item.Split('=', 2);
            string label = parts.Length == 2 ? parts[0].Trim() : item;
            string scenario = parts.Length == 2 ? parts[1].Trim() : item;
            string[] sides = scenario.Split(" vs ", StringSplitOptions.TrimEntries);
            text.AppendLine();
            text.AppendLine($"{label}: {scenario}");
            text.AppendLine($"  {"range",6} {"A holds",7} {"B holds",7} {"by A",6} {"by B",6} {"turns",6} {"contact",8} {"A rounds@contact",17} {"B rounds@contact",17} {"A end",6} {"B end",6}");
            foreach (int range in ranges)
            {
                ProbeResult result = SquadDuelDiagnostics.RunProbe(
                    blob,
                    SquadDuelDiagnostics.ParseSide(sides[0]),
                    SquadDuelDiagnostics.ParseSide(sides[1]),
                    -range,
                    seeds,
                    aggression,
                    roles: RoleMode.Alternate);
                tsv.AppendLine(string.Join('\t', label, scenario, range, seeds,
                    N(result.AHold), N(result.BHold), N(result.Undecided), N(result.ALost), N(result.BLost),
                    N(result.Turns), result.MaxTurns, N(result.ContactShare), N(result.MeanContactTurn),
                    N(result.RoundsStartA), N(result.RoundsLeftAtContactA), N(result.RoundsLeftAtEndA),
                    N(result.RoundsStartB), N(result.RoundsLeftAtContactB), N(result.RoundsLeftAtEndB),
                    N(result.MeanPreferenceA), N(result.MeanPreferenceB)));
                text.AppendLine($"  {range,6} {result.AHold,7:P0} {result.BHold,7:P0} {result.BLost,6:P0} {result.ALost,6:P0} "
                    + $"{result.Turns,6:N0} {result.ContactShare,8:P0} {P(result.RoundsLeftAtContactA),17} {P(result.RoundsLeftAtContactB),17} "
                    + $"{P(result.RoundsLeftAtEndA),6} {P(result.RoundsLeftAtEndB),6}");
                // Written after every point, so a stopped run keeps what it measured.
                File.WriteAllText(tsvPath, tsv.ToString());
                File.WriteAllText(textPath, text.ToString());
            }
        }
    }

    /// <summary>
    /// The grid-free opening forecast (OpeningExchangeForecast, refactor phase 1) beside the measured
    /// curves, for calibration. No battle is fought: it reads a measured approach-curves.tsv and, for
    /// every point, forecasts each squad of each side against the other side's whole force (the
    /// sides built as OpeningRangeDiagnostics builds them, seed 1), summed over the side.
    ///
    /// Gated by RUN_APPROACH_FORECAST. Settings:
    ///   APPROACH_MEASURED  the measured tsv (default GauntletResults\approach-phase0\approach-curves.tsv).
    ///   APPROACH_FIRE      planner (default), aimed or unaimed: the curve variant feeding the lanes.
    ///   APPROACH_DISCOUNT  the per-turn discount (default 1, the opening forecast's own).
    ///   APPROACH_AMMO      0 to stop counting ammunition (default counted).
    ///   APPROACH_OUT       output folder as above; writes approach-forecast.txt.
    /// </summary>
    [Fact]
    public void ApproachCurves_ForecastBesideMeasured()
    {
        if (Environment.GetEnvironmentVariable("RUN_APPROACH_FORECAST") == null)
        {
            return;
        }

        string measuredPath = Environment.GetEnvironmentVariable("APPROACH_MEASURED")
            ?? Path.Combine(RepositoryRoot(), "GauntletResults", "approach-phase0", "approach-curves.tsv");
        OpeningForecastOptions options = new(
            FireModel: (Environment.GetEnvironmentVariable("APPROACH_FIRE") ?? "").ToLowerInvariant() switch
            {
                "unaimed" => CurveFireModel.Unaimed,
                "aimed" => CurveFireModel.SustainedAimed,
                _ => CurveFireModel.PlannerPriced,
            },
            CountAmmunition: Environment.GetEnvironmentVariable("APPROACH_AMMO") != "0",
            LevelBreaks: Environment.GetEnvironmentVariable("APPROACH_BREAKS") != "hazard",
            LuckSpread: float.TryParse(Environment.GetEnvironmentVariable("APPROACH_LUCK"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float luck) ? luck : null,
            MoraleRules: RulesDatabaseFixture.LoadRules().FactionBehaviorRulesProfiles?.FirstOrDefault());
        // APPROACH_DISCOUNT: the forecast's per-turn discount (default the options' own).
        if (float.TryParse(Environment.GetEnvironmentVariable("APPROACH_DISCOUNT"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float discount))
        {
            options = options with { TurnDiscount = discount };
        }
        string directory = Environment.GetEnvironmentVariable("APPROACH_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "approach");
        Directory.CreateDirectory(directory);

        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        StringBuilder text = new();
        text.AppendLine($"Forecast ({options.FireModel}, ammunition {(options.CountAmmunition ? "counted" : "free")}, "
            + $"breaks {(options.LevelBreaks ? "by level" : "per-turn hazard")}, "
            + $"luck spread {options.LuckSpread ?? EngagementExchangeForecast.LuckSpread:0.##}) "
            + $"beside {measuredPath}.");
        text.AppendLine("by A / by B: casualties inflicted as a share of the other side (m = measured, f = forecast, "
            + "from the inflicting side's own view). brk: forecast break chance. holds: measured share held. "
            + "rnd@c: share of rounds left at first contact. net: forecast net value of the side.");

        string[] lines = File.ReadAllLines(measuredPath);
        string[] header = lines[0].Split('\t');
        int Column(string name) => Array.IndexOf(header, name);
        string currentMatchup = null;
        List<BattleSquad> sideA = null;
        List<BattleSquad> sideB = null;
        Dictionary<int, OpeningExchangeForecast> forecasts = null;
        List<(float Range, float ALost, float BLost)> measured = [];
        void Summarize()
        {
            if (sideA == null)
            {
                return;
            }
            float valueA = Value(sideA);
            float valueB = Value(sideB);
            float bestA = measured.OrderByDescending(row => (row.BLost * valueB) - (row.ALost * valueA)).First().Range;
            float bestB = measured.OrderByDescending(row => (row.ALost * valueA) - (row.BLost * valueB)).First().Range;
            text.AppendLine($"  best opening by measured net casualties: A {bestA:0}, B {bestB:0}. "
                + $"Forecast preferences: A {Preferences(sideA, sideB)}; B {Preferences(sideB, sideA)}");
            measured.Clear();
        }
        string Preferences(List<BattleSquad> own, List<BattleSquad> enemy) => string.Join(", ", own.Select(squad =>
        {
            float? forecast = BattleEngagementFrameBuilder.CalculateForecastOpeningRange(squad, enemy, own, options);
            return forecast.HasValue ? forecast.Value.ToString("0", CultureInfo.InvariantCulture) : "none";
        }).Distinct());
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split('\t');
            string matchup = cells[Column("matchup")];
            string scenario = cells[Column("scenario")];
            if (matchup != currentMatchup)
            {
                Summarize();
                currentMatchup = matchup;
                string[] sides = scenario.Split(" vs ", StringSplitOptions.TrimEntries);
                OnlyWar.Abstractions.IEntityIdAllocator ids = new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
                SeededRNG buildRandom = new(80_001);
                sideA = SquadDuelDiagnostics.BuildSquads(blob, SquadDuelDiagnostics.ParseSide(sides[0]), ids, "A", buildRandom);
                sideB = SquadDuelDiagnostics.BuildSquads(blob, SquadDuelDiagnostics.ParseSide(sides[1]), ids, "B", buildRandom);
                forecasts = sideA.Select(squad => (squad, OpeningExchangeForecast.Build(squad, sideB, sideA, options)))
                    .Concat(sideB.Select(squad => (squad, OpeningExchangeForecast.Build(squad, sideA, sideB, options))))
                    .ToDictionary(pair => pair.squad.Id, pair => pair.Item2);
                text.AppendLine();
                text.AppendLine($"{matchup}: {scenario}   A seeks contact: {Seeks(sideA, forecasts)}, B: {Seeks(sideB, forecasts)}");
                text.AppendLine($"  {"range",5} | {"by A m",6} {"f",5} | {"by B m",6} {"f",5} | {"brk A",5} {"brk B",5} {"holds A",7} | "
                    + $"{"A rnd@c m",9} {"f",5} | {"B rnd@c m",9} {"f",5} | {"net A",7} {"net B",7}");
            }

            float range = float.Parse(cells[Column("range")], CultureInfo.InvariantCulture);
            if (Environment.GetEnvironmentVariable("APPROACH_TRACE") == $"{matchup}:{range:0}")
            {
                TraceForecasts(directory, matchup, range, sideA, sideB, forecasts);
            }
            SideForecast a = Forecast(sideA, forecasts, range);
            SideForecast b = Forecast(sideB, forecasts, range);
            float byAForecast = Share(a.Inflicted, Value(sideB));
            float byBForecast = Share(b.Inflicted, Value(sideA));
            text.AppendLine($"  {range,5:0} | {Cell(cells, Column("bLost")),6} {P(byAForecast),5} | {Cell(cells, Column("aLost")),6} {P(byBForecast),5} | "
                + $"{P(a.OwnBreak),5} {P(b.OwnBreak),5} {Cell(cells, Column("aHolds")),7} | "
                + $"{Cell(cells, Column("roundsLeftAtContactA")),9} {P(a.RoundsAtContact),5} | "
                + $"{Cell(cells, Column("roundsLeftAtContactB")),9} {P(b.RoundsAtContact),5} | "
                + $"{a.Net,7:0.0} {b.Net,7:0.0}");
            measured.Add((
                range,
                float.Parse(cells[Column("aLost")], CultureInfo.InvariantCulture),
                float.Parse(cells[Column("bLost")], CultureInfo.InvariantCulture)));
        }
        Summarize();
        File.WriteAllText(Path.Combine(directory, "approach-forecast.txt"), text.ToString());
    }

    /// <summary>
    /// APPROACH_TRACE="matchup:range": every squad's forecast at that point, turn by turn, to
    /// approach-trace.txt.
    /// </summary>
    private static void TraceForecasts(
        string directory,
        string matchup,
        float range,
        List<BattleSquad> sideA,
        List<BattleSquad> sideB,
        Dictionary<int, OpeningExchangeForecast> forecasts)
    {
        StringBuilder trace = new();
        foreach ((string label, BattleSquad squad) in sideA.Select(squad => ("A", squad))
            .Concat(sideB.Select(squad => ("B", squad))))
        {
            OpeningExchangeForecast forecast = forecasts[squad.Id];
            if (forecast == null)
            {
                continue;
            }
            trace.AppendLine($"{matchup} @ {range:0}: {label} {squad.Name}, own pool {forecast.OwnPool:0.0}, "
                + $"enemy pool {forecast.EnemyPool:0.0}, rounds {forecast.OwnRounds:0} / enemy {forecast.EnemyRounds:0}");
            foreach (ExchangeLane lane in forecast.Lanes)
            {
                trace.AppendLine($"  lane: pool {lane.EnemyPool:0.0} moves {lane.Moves} closing {lane.MovingClosing:0.0}/{lane.StandingClosing:0.0} "
                    + $"contact {lane.ContactPossible} out {lane.ContactOutgoing:0.00} in {lane.ContactIncoming:0.00} "
                    + $"standing@{range:0} {lane.Standing.At(range)} rounds {lane.Standing.RoundsAt(range)}");
            }
            EngagementExchangeForecast.Result result = forecast.Evaluate(range, turn =>
            {
                if (turn.Turn < 40 || turn.Turn % 10 == 0)
                {
                    trace.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"    t={turn.Turn,3} r={turn.Range,6:F1} outRate={turn.OutRate,7:F2} inRate={turn.InRate,7:F2} "
                        + $"out={turn.Outgoing,6:F2} in={turn.Incoming,6:F2} own={turn.OwnRemaining,6:F1} "
                        + $"ownSurv={turn.OwnSurvival:F3} ownHaz={turn.OwnHazard:F3} enemy={turn.EnemyRemaining,6:F1} "
                        + $"enemySurv={turn.EnemySurvival:F3} enemyHaz={turn.EnemyHazard:F3} "
                        + $"rounds={turn.OwnRounds:0}/{turn.EnemyRounds:0}"));
                }
            });
            trace.AppendLine($"  => {result}");
        }
        File.WriteAllText(Path.Combine(directory, "approach-trace.txt"), trace.ToString());
    }

    private readonly record struct SideForecast(float Inflicted, float OwnBreak, float RoundsAtContact, float Net);

    private static SideForecast Forecast(
        List<BattleSquad> side, Dictionary<int, OpeningExchangeForecast> forecasts, float range)
    {
        float inflicted = 0;
        float breakWeighted = 0;
        float value = 0;
        float roundsAtContact = 0;
        float roundsCarried = 0;
        float net = 0;
        foreach (BattleSquad squad in side)
        {
            OpeningExchangeForecast forecast = forecasts[squad.Id];
            if (forecast == null)
            {
                continue;
            }
            EngagementExchangeForecast.Result result = forecast.Evaluate(range);
            inflicted += result.EnemyCasualties;
            breakWeighted += result.OwnBreakChance * forecast.OwnPool;
            value += forecast.OwnPool;
            net += OpeningExchangeForecast.Net(result);
            if (float.IsFinite(forecast.OwnRounds) && forecast.OwnRounds > 0)
            {
                roundsCarried += forecast.OwnRounds;
                // No contact in the forecast: what is left at the end is what contact would find.
                roundsAtContact += result.ContactTurn >= 0 ? result.OwnRoundsAtContact : result.OwnRoundsLeft;
            }
        }
        return new SideForecast(
            inflicted,
            value > 0 ? breakWeighted / value : float.NaN,
            roundsCarried > 0 ? roundsAtContact / roundsCarried : float.NaN,
            net);
    }

    private static string Seeks(List<BattleSquad> side, Dictionary<int, OpeningExchangeForecast> forecasts) =>
        string.Join("/", side.Select(squad => forecasts[squad.Id]?.IsContactSeeking == true ? "yes" : "no").Distinct());

    private static float Value(List<BattleSquad> side) =>
        side.SelectMany(squad => squad.AbleSoldiers).Sum(soldier => Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue));

    private static float Share(float part, float whole) => whole > 0 ? part / whole : float.NaN;

    private static string Cell(string[] cells, int column) =>
        column < 0 || column >= cells.Length || string.IsNullOrEmpty(cells[column])
            ? "n/a"
            : P(double.Parse(cells[column], CultureInfo.InvariantCulture));

    private static string RepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string N(double value) =>
        double.IsNaN(value) ? "" : value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string P(double value) =>
        double.IsNaN(value) ? "n/a" : (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
