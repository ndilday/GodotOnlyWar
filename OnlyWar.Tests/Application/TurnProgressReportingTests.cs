using System.Collections.Generic;
using System.Linq;
using OnlyWar.Application.Battles;
using OnlyWar.Battles;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Runtime;
using OnlyWar.Runtime.Factories;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

/// <summary>
/// The End Turn overlay polls TurnProgress while the turn resolves on a worker thread, so a long
/// battle has to name itself there. The overlay only ever sees the latest line, which is what
/// these tests read back.
/// </summary>
public class TurnProgressReportingTests
{
    // Battle-ready non-player forces (as in BattleAbandonedWoundedTests), so the battle ends
    // on its own in a few turns rather than hanging on a tie.
    private const int TyranidFactionId = 2;
    private const int TermagauntSquadTemplateId = 18;
    private const int PdfInfantrySquadTemplateId = 34;

    [Fact]
    public void Resolve_ReportsTheBattleRegionAndItsLastTurn()
    {
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        Faction imperial = rules.Factions.Single(faction => faction.IsDefaultFaction);
        Faction tyranids = rules.Factions.Single(faction => faction.Id == TyranidFactionId);
        Region region = new(1, null, 0, "Hive Tertius", new RegionCoordinate(0, 0), 0);
        SeededRNG random = new(7);
        TurnProgress progress = new();
        BattleEngagementResolver adapter = new(
            new BattleExecutionContext(
                rules,
                random,
                new BattleAftermathDependencies(new Date(1, 1, 1), random, NoOpAftermathSink.Instance),
                throwOnInertBattle: true),
            regionResolver: regionId => regionId == region.Id ? region : null,
            progress: progress);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        Squad pdf = SquadFactory.GenerateSquad(
            imperial.SquadTemplates[PdfInfantrySquadTemplateId], random, entityIds, "PDF Platoon");
        Squad brood = SquadFactory.GenerateSquadWithinBudget(
            tyranids.SquadTemplates[TermagauntSquadTemplateId], 90L, random, entityIds, "Brood");
        Assert.NotNull(pdf);
        Assert.NotNull(brood);

        adapter.Resolve(new EngagementInput(
            [adapter.CreateSquad(false, pdf).ToEngagementParticipant()],
            [adapter.CreateSquad(false, brood).ToEngagementParticipant()],
            new EngagementLocation(region.Id, region.Name, "Test World"),
            OpeningRange: 30,
            new EngagementSideProfile(Aggression.Normal, EngagementRole.Attacker),
            new EngagementSideProfile(Aggression.Normal, EngagementRole.Defender)));

        const string prefix = "Resolving Battle in Hive Tertius: Turn ";
        Assert.StartsWith(prefix, progress.Current);
        Assert.True(
            int.TryParse(progress.Current[prefix.Length..], out int lastTurn) && lastTurn >= 1,
            $"expected a battle turn number, got \"{progress.Current}\"");
    }

    [Fact]
    public void Report_NullClearsTheLine()
    {
        TurnProgress progress = new();
        progress.Report("Processing Faction Plans");
        progress.Report(null);

        Assert.Equal(string.Empty, progress.Current);
    }

    private sealed class NoOpAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
