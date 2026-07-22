using System.Collections.Generic;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class ReconOperationReportBuilderTests
{
    [Fact]
    public void Build_SummarizesIndependentSquadOutcomesAsOneMixedOperation()
    {
        MissionContext successful = CreateContext("Cassiel", impact: 2f);
        MissionContext lost = CreateContext("Gideon", impact: -1f);
        lost.ForceLostContact = true;

        ReconOperationReport report = ReconOperationReportBuilder.Build(
            [successful, lost],
            "Ashfields, Gehenna");

        Assert.Equal("MIXED RESULTS", report.OutcomeStatus);
        Assert.Contains("2 reconnaissance squads", report.Summary);
        Assert.Contains("1 operated undetected", report.Summary);
        Assert.Contains("1 lost contact with base", report.Summary);
    }

    [Fact]
    public void Build_AllElementsShareOutcome_UsesAllInsteadOfRepeatingCount()
    {
        MissionContext first = CreateContext("Faustus Squad", impact: 1f);
        MissionContext second = CreateContext("Thawn Squad", impact: 1f);
        first.ForceBrokeContact = true;
        second.ForceBrokeContact = true;

        ReconOperationReport report = ReconOperationReportBuilder.Build(
            [first, second],
            "Terra Delta");

        Assert.Contains("All broke contact after detection", report.Summary);
        Assert.DoesNotContain("2 broke contact after detection", report.Summary);
    }

    [Fact]
    public void MissionContext_DebriefLinesCarryElementSquadAndDay()
    {
        MissionContext context = CreateContext("Cassiel", impact: 0f);
        context.DaysElapsed = 2;

        context.AddLog("Completed the northern sweep.");

        MissionDebriefLine line = Assert.Single(context.DebriefLines);
        Assert.Equal((ushort)2, line.Day);
        Assert.Equal("Cassiel", line.SquadName);
    }

    [Fact]
    public void DebriefLineGrouper_CombinesSquadActivitiesIntoOneEntryPerDay()
    {
        MissionDebriefLine[] lines =
        [
            new("Day 1: First activity", day: 1, squadName: "Faustus Squad"),
            new("Day 1: Second activity", day: 1, squadName: "Thawn Squad"),
            new("Day 2: Follow-up activity", day: 2, squadName: "Faustus Squad")
        ];

        IReadOnlyList<MissionDebriefLineGroup> groups = MissionDebriefLineGrouper.GroupByDay(lines);

        Assert.Equal(2, groups.Count);
        Assert.Equal((ushort)1, groups[0].Day);
        Assert.Equal(2, groups[0].Lines.Count);
        Assert.Equal((ushort)2, groups[1].Day);
        Assert.Single(groups[1].Lines);
    }

    private static MissionContext CreateContext(string squadName, float impact)
    {
        BattleSquad squad = new(true, TestModelFactory.CreateSquad(
            squadName,
            TestModelFactory.CreateSoldier(name: $"{squadName} Scout")));
        Mission mission = new(MissionType.Recon, regionFaction: null, missionSize: 0);
        Order order = new(
            [squad.Squad],
            Disposition.Raiding,
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Cautious,
            mission: mission);
        MissionContext context = new(order, [squad], []);
        context.Impact = impact;
        return context;
    }
}
