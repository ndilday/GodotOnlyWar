using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Battles;
using OnlyWar.Domain;
using OnlyWar.Generation.World;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in diagnostic: why a reciprocal assault opens where it does. For each squad of a duel
/// scenario it prints its preferred opening range against the other side's whole force and the
/// opening forecast's net value, expected and unlucky, at a ladder of openings
/// (BattleEngagementFrameBuilder.CalculateForecastOpeningRange takes the shortest opening within the
/// indifference band of the best on both). Then the outgoing and incoming curves at the same ladder,
/// and the assault opening the gauntlet would use. No battle is fought.
///
/// Gated by RUN_OPENING_RANGE. Settings: OPENING_SCENARIO (duel sides, "A vs B", for example
/// "17,x=3 vs 34,x=2"), OPENING_SEED (default 1, the duel's first seed), OPENING_OUT (default
/// %TEMP%\GodotOnlyWar\opening-range.txt).
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OpeningRangeDiagnostics
{
    private static readonly float[] Ladder = [1, 2, 5, 10, 20, 40, 80, 160, 320, 640, 1000];

    [Fact]
    public void OpeningRange_ReportsItsTerms()
    {
        string scenario = Environment.GetEnvironmentVariable("OPENING_SCENARIO");
        if (Environment.GetEnvironmentVariable("RUN_OPENING_RANGE") == null || string.IsNullOrWhiteSpace(scenario))
        {
            return;
        }

        SquadDuelDiagnostics.ApplyPlannerSwitches();
        int seed = SquadDuelDiagnostics.ReadInt("OPENING_SEED", 1);
        string output = Environment.GetEnvironmentVariable("OPENING_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "opening-range.txt");
        StringBuilder report = new();
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        foreach (string one in scenario.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Report(report, blob, one, seed);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, report.ToString());
    }

    private static void Report(StringBuilder report, GameRulesBlob blob, string scenario, int seed)
    {
        string[] sides = scenario.Split('@')[0].Split(" vs ", StringSplitOptions.TrimEntries);
        // Built as SquadDuelDiagnostics.RunBattle builds them for A attacking.
        OnlyWar.Abstractions.IEntityIdAllocator ids = new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        SeededRNG buildRandom = new(80_000 + seed);
        List<BattleSquad> sideA = SquadDuelDiagnostics.BuildSquads(blob, SquadDuelDiagnostics.ParseSide(sides[0]), ids, "A", buildRandom);
        List<BattleSquad> sideB = SquadDuelDiagnostics.BuildSquads(blob, SquadDuelDiagnostics.ParseSide(sides[1]), ids, "B", buildRandom);
        // OPENING_TACTICAL=1: price soldiers at their tactical values, as a battle does, instead of
        // the database's strategic values.
        if (Environment.GetEnvironmentVariable("OPENING_TACTICAL") == "1")
        {
            TacticalValueTable.EnsureAssigned(sideA, sideB);
            report.AppendLine("  (tactical values: " + string.Join(", ", sideA.Concat(sideB).Select(squad =>
                $"{squad.Name} {F(squad.AbleSoldiers.Sum(soldier => soldier.PlanningValue))}")) + ")");
        }
        // OPENING_SEPARATIONS (for example "1,100,449"): place side A on x = 0 and side B on x = the
        // separation, build the tactical value table there (a few turns of its fixed point), and print
        // each squad's mean offense, durability, melee share and value. Positions are cleared after.
        string separations = Environment.GetEnvironmentVariable("OPENING_SEPARATIONS");
        if (!string.IsNullOrWhiteSpace(separations))
        {
            foreach (string text in separations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separation = int.Parse(text, CultureInfo.InvariantCulture);
                int row = 0;
                foreach (BattleSoldier soldier in sideA.SelectMany(squad => squad.Soldiers))
                {
                    soldier.TopLeft = (0, row++ * 2);
                }
                row = 0;
                foreach (BattleSoldier soldier in sideB.SelectMany(squad => squad.Soldiers))
                {
                    soldier.TopLeft = (separation, row++ * 2);
                }
                TacticalValueTable table = null;
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    table = TacticalValueTable.Build(sideA, sideB, table);
                }
                report.AppendLine($"  tactical values at {separation} yd: " + string.Join("; ", sideA.Concat(sideB).Select(squad =>
                {
                    List<SoldierTacticalValue> values = squad.AbleSoldiers
                        .Select(soldier => table.TryGet(soldier.Soldier.Id, out SoldierTacticalValue value) ? value : default)
                        .ToList();
                    return $"{squad.Name}: a {F(values.Average(v => v.Offense))}, d {F(values.Average(v => v.Durability))}, "
                        + $"melee {F(values.Average(v => v.MeleeShare))}, value {F(values.Sum(v => v.Value))}";
                })));
                foreach (BattleSoldier soldier in sideA.Concat(sideB).SelectMany(squad => squad.Soldiers))
                {
                    soldier.TopLeft = null;
                }
            }
        }
        // OPENING_VALUE_A / OPENING_VALUE_B: every soldier of that side at this tactical value, to
        // reproduce the values a battle reached at its separation.
        foreach ((string name, List<BattleSquad> side) in new[] { ("OPENING_VALUE_A", sideA), ("OPENING_VALUE_B", sideB) })
        {
            if (float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                foreach (BattleSoldier soldier in side.SelectMany(squad => squad.Soldiers))
                {
                    soldier.TacticalValue = value;
                }
                report.AppendLine($"  ({name} = {F(value)} a soldier)");
            }
        }

        (ushort range, double preferenceA, double preferenceB) = SquadDuelDiagnostics.AssaultOpening(sideA, sideB);
        report.AppendLine($"=== {scenario} (seed {seed}): assault opening {range} yd; A prefers {F(preferenceA)}, B prefers {F(preferenceB)}");
        foreach ((string label, List<BattleSquad> own, List<BattleSquad> enemy) in
            new[] { ("A", sideA, sideB), ("B", sideB, sideA) })
        {
            foreach (BattleSquad squad in own)
            {
                float? forecastRange = BattleEngagementFrameBuilder.CalculateForecastOpeningRange(squad, enemy, own);
                report.AppendLine($"  {label} {squad.Name} ({squad.AbleSoldiers.Count} able): prefers "
                    + (forecastRange.HasValue ? F(forecastRange.Value) : "none (no ranged weapon)"));
                // Squads of one template differ by their rolled special and heavy weapons, and the
                // forecast's candidate openings run out to the squad's reach.
                report.AppendLine("      weapons: " + string.Join(", ", squad.Soldiers
                    .SelectMany(soldier => soldier.RangedWeapons)
                    .GroupBy(weapon => weapon.Template.Name)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Count()} {group.Key}")));
                OpeningExchangeForecast forecast = OpeningExchangeForecast.Build(squad, enemy, own);
                if (forecast != null)
                {
                    report.AppendLine($"      reach {F(forecast.Reach)}, own pool {F(forecast.OwnPool)}");
                    // The in-battle standoff (the Close continuation's destination), unplaced.
                    float band = BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange(
                        squad, enemy, forecast.Reach, out float useful, out float peak,
                        requirePlacement: false, friendlySquads: own);
                    report.AppendLine($"      effective engagement range {F(band)}, useful fire {F(useful)}, peak removal {F(peak)}");
                    report.AppendLine("      forecast net, expected (unlucky) by opening: " + string.Join("  ", Ladder.Select(opening =>
                        $"{F(opening)}: {F(OpeningExchangeForecast.Net(forecast.Evaluate(opening)))}"
                        + $" ({F(OpeningExchangeForecast.Net(forecast.EvaluateUnlucky(opening)))})")));
                }
                var curve = BattleEngagementFrameBuilder.DescribeEngagementCurve(squad, enemy, 2000f, Ladder, own);
                if (curve is null)
                {
                    report.AppendLine("      (no curve)");
                    continue;
                }
                report.AppendLine("      range:    " + string.Join(" ", curve.Value.Samples.Select(s => $"{F(s.Range),8}")));
                report.AppendLine("      outgoing: " + string.Join(" ", curve.Value.Samples.Select(s => $"{F(s.Outgoing),8}")));
                report.AppendLine("      incoming: " + string.Join(" ", curve.Value.Samples.Select(s => $"{F(s.Incoming),8}")));
                report.AppendLine("      melee:    " + string.Join(" ", curve.Value.Samples.Select(s => $"{F(s.MeleeThreat),8}")));
            }
        }
        report.AppendLine();
    }

    private static string F(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.###", CultureInfo.InvariantCulture);
}
