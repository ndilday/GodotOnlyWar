using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Opt-in scale probes for the active-pursuit invariant. These use the same direct
/// <see cref="BattleTurnResolver"/> construction path as the withdrawal regressions, rather than
/// changing the tactical actor cap or adding a special terminal rule for diagnostics.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class ActivePursuitScaleDiagnosticsTests
{
    private static readonly string TraceDirectory = Path.Combine(
        Path.GetTempPath(),
        "GodotOnlyWar",
        "pursuit-trace");

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("Category", "Slow")]
    public void GristNineStylePursuit_315MarinesAgainst20RoutingOrks_ReportsInvariantEvidence()
    {
        if (Environment.GetEnvironmentVariable("RUN_PURSUIT_SCALE_TRACE") == null) return;

        RunScenario(
            name: "grist-nine-style-315-vs-20",
            marineCount: 315,
            quarryCount: 20,
            marineSquadCount: 3,
            idBase: 97_000);
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("Category", "Slow")]
    public void LargerPursuit_360MarinesAgainst24RoutingOrks_ReportsInvariantEvidence()
    {
        if (Environment.GetEnvironmentVariable("RUN_PURSUIT_SCALE_TRACE") == null) return;

        RunScenario(
            name: "larger-than-grist-nine-360-vs-24",
            marineCount: 360,
            quarryCount: 24,
            marineSquadCount: 4,
            idBase: 98_000);
    }

    private static void RunScenario(
        string name,
        int marineCount,
        int quarryCount,
        int marineSquadCount,
        int idBase)
    {
        ScaleScenario scenario = BuildScenario(
            marineCount,
            quarryCount,
            marineSquadCount,
            idBase);
        List<string> contactTrace = [];
        List<string> engagementTrace = [];
        List<string> actionTrace = [];
        List<string> diagnosticTrace = [];
        int resolvingTurn = 0;
        int? firstShotTurn = null;
        int shotsFired = 0;
        Action<string> previousSink = BattleLog.Sink;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            BattleLog.Sink = line =>
            {
                if (line.StartsWith("PRESS_CONTACT_PROJECTION ", StringComparison.Ordinal)
                    || line.StartsWith("FOLLOW_SHOT_EVAL ", StringComparison.Ordinal)
                    || line.StartsWith("ESCAPE_EVAL ", StringComparison.Ordinal)
                    || line.StartsWith("PURSUIT_PROGRESS ", StringComparison.Ordinal))
                {
                    diagnosticTrace.Add(line);
                }
                else if (line.StartsWith("CONTACT_EVAL ", StringComparison.Ordinal))
                {
                    contactTrace.Add(line);
                }
                else if (line.StartsWith("ENGAGE_EVAL ", StringComparison.Ordinal))
                {
                    engagementTrace.Add(line);
                }
                else if (line.StartsWith("ACTION ", StringComparison.Ordinal))
                {
                    actionTrace.Add(line);
                    if (Field(line, "action") == "Shoot")
                    {
                        firstShotTurn ??= resolvingTurn;
                        shotsFired += int.Parse(Field(line, "shots"), CultureInfo.InvariantCulture);
                    }
                }
            };

            while (scenario.Resolver.BattleHistory.Outcome == null
                && scenario.Resolver.BattleHistory.Turns.Count - 1 < 1_000)
            {
                resolvingTurn = scenario.Resolver.BattleHistory.Turns.Count;
                scenario.Resolver.ProcessNextTurn();
            }
        }
        finally
        {
            stopwatch.Stop();
            BattleLog.Sink = previousSink;
        }

        BattleOutcome outcome = scenario.Resolver.BattleHistory.Outcome;
        int turns = scenario.Resolver.BattleHistory.Turns.Count - 1;

        List<string> maintained = contactTrace
            .Where(line => Field(line, "decision") == "RemainInContact")
            .ToList();
        string finalMaintained = maintained.Count == 0 ? "none" : maintained[^1];
        string terminalDiagnostic = outcome?.EndReason == BattleEndReason.TurnCap
            ? turns < 1_000
                ? "inert_watchdog_no_casualty_or_separation_change"
                : "1000_turn_cap"
            : "none";
        List<string> chosen = engagementTrace
            .Where(line => bool.Parse(Field(line, "chosen")))
            .OrderBy(line => int.Parse(Field(line, "turn"), CultureInfo.InvariantCulture))
            .ThenBy(line => int.Parse(Field(line, "squad"), CultureInfo.InvariantCulture))
            .ToList();
        int holdAdvanceSwitches = chosen
            .GroupBy(line => Field(line, "squad"))
            .Sum(group => group.Zip(group.Skip(1), (before, after) =>
                    IsHold(Field(before, "kind")) != IsHold(Field(after, "kind")) ? 1 : 0)
                .Sum());
        int maximumWorthwhileFireChase = MaximumWorthwhileFireChase(engagementTrace);
        string summary =
            $"PURSUIT_SCALE name={name} soldiers={marineCount + quarryCount} "
            + $"turns={turns} runtime_ms={stopwatch.Elapsed.TotalMilliseconds:F0} "
            + $"outcome={outcome?.EndReason.ToString() ?? "none"} "
            + $"killed={scenario.Resolver.BattleHistory.KilledSoldierIds.Count} "
            + $"damaged={scenario.Resolver.BattleHistory.DamagedSoldierIds.Count} "
            + $"incapacitated={scenario.Resolver.BattleHistory.IncapacitatedSoldierIds.Count} "
            + $"first_shot_turn={firstShotTurn?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
            + $"shots_fired={shotsFired} "
            + $"max_worthwhile_fire_chase={maximumWorthwhileFireChase} "
            + $"hold_advance_switches={holdAdvanceSwitches} "
            + $"final_evidence={FieldOrDefault(finalMaintained, "maintenance_evidence", "none")} "
            + $"final_reason={FieldOrDefault(finalMaintained, "reason", "none")} "
            + $"terminal_diagnostic={terminalDiagnostic} "
            + $"trace_lines={contactTrace.Count} "
            + $"diagnostic_trace_lines={diagnosticTrace.Count}";
        Directory.CreateDirectory(TraceDirectory);
        string reportPath = Path.Combine(TraceDirectory, name + ".log");
        File.WriteAllLines(reportPath, new[] { summary }
            .Concat(contactTrace)
            .Concat(engagementTrace)
            .Concat(actionTrace)
            .Concat(diagnosticTrace));
        Console.WriteLine(summary + $" trace={reportPath}");

        Assert.NotNull(outcome);
        Assert.NotEqual(BattleEndReason.TurnCap, outcome.EndReason);
        Assert.True(turns < 1_000, $"{name} reached {turns} turns; trace={reportPath}");
        Assert.NotEmpty(contactTrace);
        Assert.NotEmpty(maintained);
        foreach (string line in maintained)
        {
            Assert.True(
                int.Parse(Field(line, "pursuit_pairs")) > 0,
                $"contact was maintained without an assigned pair: {line}");
            Assert.DoesNotContain("pursuers_reasonable_shot", line);

            int activeEvidence = int.Parse(Field(line, "positive_closing_pairs"))
                + int.Parse(Field(line, "attacked_recently_pairs"))
                + int.Parse(Field(line, "viable_fire_cycle_pairs"));
            Assert.True(
                activeEvidence > 0,
                $"contact was maintained without closing, attack, or fire-cycle progress: {line}");
        }
    }

    private static int MaximumWorthwhileFireChase(IReadOnlyCollection<string> engagementTrace)
    {
        var turns = engagementTrace
            .GroupBy(line => (Turn: Field(line, "turn"), Squad: Field(line, "squad")))
            .Select(group => new
            {
                Turn = int.Parse(group.Key.Turn, CultureInfo.InvariantCulture),
                Squad = group.Key.Squad,
                Chosen = group.Single(line => bool.Parse(Field(line, "chosen"))),
                Hold = group.FirstOrDefault(line => Field(line, "kind") == "Hold")
            })
            .OrderBy(entry => entry.Squad)
            .ThenBy(entry => entry.Turn);
        int maximum = 0;
        foreach (var squad in turns.GroupBy(entry => entry.Squad))
        {
            int consecutive = 0;
            foreach (var turn in squad)
            {
                bool worthwhileHold = turn.Hold != null
                    && float.Parse(Field(turn.Hold, "outgoing"), CultureInfo.InvariantCulture) > 0;
                consecutive = worthwhileHold && !IsHold(Field(turn.Chosen, "kind"))
                    ? consecutive + 1
                    : 0;
                maximum = System.Math.Max(maximum, consecutive);
            }
        }
        return maximum;
    }

    private static bool IsHold(string kind) => kind == "Hold";

    private static ScaleScenario BuildScenario(
        int marineCount,
        int quarryCount,
        int marineSquadCount,
        int idBase)
    {
        SoldierTemplate routingTemplate = new(
            idBase,
            TestModelFactory.HumanSpecies,
            "Routing Ork",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 1);
        // Keep this a normal, damageable routing force. An earlier probe used Constitution 5,000
        // and BattleValue 0, which intentionally made the quarry almost impossible to finish and
        // tested the inert watchdog more than it tested pursuit resolution.
        BattleSquad quarry = CreateSquad(
            "Routing Orks",
            idBase + 100,
            routingTemplate,
            quarryCount,
            isPlayerSquad: false,
            removeWeapons: true);
        foreach (BattleSoldier soldier in quarry.Soldiers)
        {
            ((Soldier)soldier.Soldier).MoveSpeed = 6f;
        }

        List<BattleSquad> marines = [];
        int remaining = marineCount;
        for (int squadIndex = 0; squadIndex < marineSquadCount; squadIndex++)
        {
            int count = remaining / (marineSquadCount - squadIndex);
            remaining -= count;
            BattleSquad squad = CreateSquad(
                $"Marine Pursuit {squadIndex + 1}",
                idBase + 1_000 + (squadIndex * 300),
                TestModelFactory.MarineTemplate,
                count,
                isPlayerSquad: true,
                removeWeapons: false);
            foreach (BattleSoldier soldier in squad.Soldiers)
            {
                ((Soldier)soldier.Soldier).MoveSpeed = 8f;
            }
            marines.Add(squad);
        }

        BattleGridManager grid = new();
        PlaceRectangle(grid, quarry, side: true, x: 0, y: 0, width: 5);
        for (int index = 0; index < marines.Count; index++)
        {
            PlaceRectangle(
                grid,
                marines[index],
                side: false,
                x: 60,
                y: index * 12,
                width: 21);
        }

        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        Date date = new(1, 1, 1);
        RNG.Reset(idBase);
        StaticRNG random = new();
        BattleAftermathDependencies aftermath = new(
            date,
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        BattleTurnResolver resolver = new(
            grid,
            [quarry],
            marines,
            region: null,
            execution,
            new BattleSideProfile(Aggression.Normal, BattleRole.Attacker),
            new BattleSideProfile(Aggression.Aggressive, BattleRole.Defender));
        return new ScaleScenario(resolver);
    }

    private static BattleSquad CreateSquad(
        string name,
        int firstSoldierId,
        SoldierTemplate soldierTemplate,
        int count,
        bool isPlayerSquad,
        bool removeWeapons)
    {
        Faction faction = new(
            firstSoldierId + 10_000,
            name,
            Color.Red,
            isPlayerFaction: isPlayerSquad,
            isDefaultFaction: false,
            behavior: FactionBehavior.None,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [soldierTemplate.Id] = soldierTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
        SquadTemplate squadTemplate = new(
            firstSoldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(soldierTemplate, 0, (byte)count)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(firstSoldierId, name, null, squadTemplate);
        for (int index = 0; index < count; index++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(
                soldierTemplate,
                $"{name} {index + 1}");
            soldier.Id = firstSoldierId + index;
            squad.AddSquadMember(soldier);
        }

        BattleSquad result = new(isPlayerSquad, squad);
        if (removeWeapons)
        {
            foreach (BattleSoldier soldier in result.Soldiers)
            {
                soldier.ClearWeapons();
            }
        }
        return result;
    }

    private static void PlaceRectangle(
        BattleGridManager grid,
        BattleSquad squad,
        bool side,
        int x,
        int y,
        int width)
    {
        for (int index = 0; index < squad.Soldiers.Count; index++)
        {
            int column = index % width;
            int row = index / width;
            BattleSoldier soldier = squad.Soldiers[index];
            soldier.TopLeft = (x + column, y + row);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }
    }

    private static string Field(string line, string name)
    {
        string prefix = name + "=";
        string field = line
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Single(part => part.StartsWith(prefix, StringComparison.Ordinal));
        return field[prefix.Length..];
    }

    private static string FieldOrDefault(string line, string name, string fallback)
    {
        return line == "none" || !line.Contains(name + "=", StringComparison.Ordinal)
            ? fallback
            : Field(line, name);
    }

    private sealed record ScaleScenario(BattleTurnResolver Resolver);

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
