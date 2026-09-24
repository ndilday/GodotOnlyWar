using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Squads;
using OnlyWar.Generation.World;
using OnlyWar.Persistence.Database.GameRules;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// What the Battle Review shows for an encounter with the Insurrectionist irregulars (PRD §4.20,
/// §5.4). Revolts are hard to provoke on demand in play, so this stands in for a hand check in
/// Godot: it fights a few turns against the real rules-database formations and reads the replay
/// display the Battle Review scene renders. It does not look at the scene itself.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class InsurrectionistEncounterPresentationTests
{
    private const int SpaceMarineFactionId = 1;
    private const int ScoutSquadTemplateId = 4;
    private const string MobName = "Insurrectionist Mob";
    private const string WeaponTeamName = "Insurrectionist Weapon Team";
    private const string FirebrandName = "Insurrectionist Firebrand";
    private const int TurnsToFight = 4;

    [Fact]
    public void BattleReview_PresentsTheIrregularsAsTheirOwnFormations()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction insurrectionists = blob.Factions.Single(faction => faction.Name == "Insurrectionists");
        Faction marines = blob.Factions.Single(faction => faction.Id == SpaceMarineFactionId);

        RNG.Reset(78_000);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        BattleSquad mob = CreateSquad(insurrectionists, MobName, entityIds, isPlayer: false);
        BattleSquad team = CreateSquad(insurrectionists, WeaponTeamName, entityIds, isPlayer: false);
        BattleSquad firebrand = CreateSquad(insurrectionists, FirebrandName, entityIds, isPlayer: false);
        BattleSquad scouts = CreateSquad(
            marines, marines.SquadTemplates[ScoutSquadTemplateId].Name, entityIds, isPlayer: true);
        int mobStrength = mob.Soldiers.Count;

        BattleGridManager grid = new();
        PlaceBlock(grid, mob, side: false, x: 120, y: 0);
        PlaceBlock(grid, team, side: false, x: 130, y: 20);
        PlaceBlock(grid, firebrand, side: false, x: 130, y: 25);
        PlaceBlock(grid, scouts, side: true, x: 0, y: 0);

        BattleTurnResolver resolver = CreateResolver(grid, [scouts], [mob, team, firebrand]);
        for (int turn = 0; turn < TurnsToFight; turn++)
        {
            resolver.ProcessNextTurn();
        }
        BattleHistory history = resolver.BattleHistory;
        BattleReplaySummaryBuilder builder = new();
        BattleReplayDisplay opening = builder.Build(history, 0, mob.Id);

        // Force hierarchy: one enemy root holding exactly the three irregular formations, each
        // titled and typed from its own template, the mob at the strength it actually rolled.
        BattleForceHierarchyNode enemyRoot = Assert.Single(
            opening.ForceHierarchy, node => !node.IsPlayerForce);
        Assert.Equal(
            [FirebrandName, MobName, WeaponTeamName],
            enemyRoot.Children.Select(node => node.Title).OrderBy(title => title));
        foreach (BattleForceHierarchyNode node in enemyRoot.Children)
        {
            Assert.StartsWith(node.Title, node.Subtitle, StringComparison.Ordinal);
            Assert.NotNull(node.SquadRow);
            Assert.Equal(node.Title, node.SquadRow.Type);
        }
        BattleForceHierarchyNode mobNode = enemyRoot.Children.Single(node => node.Title == MobName);
        Assert.Equal(mobStrength, mobNode.StartingStrength);
        Assert.Equal("scout", mobNode.IconKey);
        Assert.Equal("hq", enemyRoot.Children.Single(node => node.Title == FirebrandName).IconKey);

        // Formation panel: led by the ringleader, carrying the faction's own autogun set.
        BattleFormationSummary mobSummary = opening.SelectedFormation;
        Assert.Equal(MobName, mobSummary.FormationType);
        Assert.Equal("Insurrectionist Ringleader", mobSummary.CommanderName);
        Assert.Equal(mobStrength, mobSummary.StartingStrength);
        BattleWeaponSetSummary autoguns = Assert.Single(mobSummary.ActiveWeaponSets);
        Assert.Equal("Autogun (Insurrectionist)", autoguns.Name);
        Assert.Equal(mobStrength, autoguns.Count);

        BattleFormationSummary teamSummary = builder.Build(history, 0, team.Id).SelectedFormation;
        Assert.Equal(
            ["Autogun (Insurrectionist) x1", "Heavy Stubber (Insurrectionist) x1"],
            teamSummary.ActiveWeaponSets
                .Select(set => $"{set.Name} x{set.Count}")
                .OrderBy(label => label));

        // Chronicle: every entry the irregulars act in is attributed to one of their formations,
        // and nothing anywhere on the review still reads as the PDF they replaced.
        List<BattleEventEntry> entries = Enumerable.Range(0, history.Turns.Count)
            .SelectMany(index => builder.Build(history, index).CurrentTurnEvents)
            .ToList();
        HashSet<int> irregularIds = [mob.Id, team.Id, firebrand.Id];
        List<BattleEventEntry> irregularEntries = entries
            .Where(entry => entry.FormationId.HasValue && irregularIds.Contains(entry.FormationId.Value))
            .ToList();
        Assert.NotEmpty(irregularEntries);
        Assert.All(irregularEntries, entry =>
            Assert.Contains(entry.FormationName, new[] { MobName, WeaponTeamName, FirebrandName }));

        IEnumerable<string> visibleText = enemyRoot.Children
            .SelectMany(node => new[] { node.Title, node.Subtitle, node.SquadRow.Name, node.SquadRow.Type })
            .Append(enemyRoot.Title)
            .Concat(mobSummary.ActiveWeaponSets.Select(set => set.Name))
            .Concat(teamSummary.ActiveWeaponSets.Select(set => set.Name))
            .Concat(irregularEntries.SelectMany(entry => new[] { entry.ActorName, entry.Text }));
        Assert.All(visibleText, text =>
            Assert.DoesNotContain("PDF", text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private static BattleSquad CreateSquad(
        Faction faction,
        string templateName,
        OnlyWar.Abstractions.IEntityIdAllocator entityIds,
        bool isPlayer)
    {
        SquadTemplate template = faction.SquadTemplates.Values.Single(candidate => candidate.Name == templateName);
        Squad squad = SquadFactory.GenerateSquad(template, new StaticRNG(), entityIds, template.Name);
        return new BattleSquad(isPlayer, squad);
    }

    private static void PlaceBlock(BattleGridManager grid, BattleSquad squad, bool side, int x, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            BattleSoldier soldier = squad.Soldiers[i];
            soldier.TopLeft = (x + (i % 5), y + (i / 5));
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders)
    {
        GameRulesData rules = GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
        RNG.Reset(78_100);
        StaticRNG random = new();
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(Aggression.Normal, BattleRole.Attacker),
            new BattleSideProfile(Aggression.Normal, BattleRole.Defender));
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(OnlyWar.Domain.Soldiers.PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
