using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Application.Adapters.Operations;
using OnlyWar.Operations.Contracts;
using OnlyWar.Builders;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Tests.Fixtures;

internal static class TestExecutionContextFactory
{
    public static BattleEngagementResolver CreateEngagementResolver(
        IRNG random = null,
        Region region = null)
    {
        random ??= new FixedRNG();
        GameRulesData rules = OnlyWar.Helpers.Database.GameRules.GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext battle = new(
            rules, random, aftermath, throwOnInertBattle: true);
        return new BattleEngagementResolver(
            battle,
            regionResolver: regionId => region?.Id == regionId ? region : null);
    }

    public static MissionExecutionContext CreateMission(
        MissionContext state,
        IRNG random = null)
    {
        // CAUTION (2026-08-09). FixedRNG's NextRandomZValue is a constant 0.0, so a contested check
        // is decided entirely by the two sides' MEANS -- there is no variance to break a tie. A
        // fixture whose opposing forces are exactly matched therefore produces the same result on
        // every turn forever, and a battle between them can never resolve. Keep the mean margin of
        // any contested roll a test relies on strictly off zero; see the accuracy comment on
        // TestModelFactory's Test Knife for the case that hung a battle for 1000 turns.
        random ??= new FixedRNG();
        // throwOnInertBattle: a battle that stops progressing is an engine bug the game survives
        // and a test must not. See BattleExecutionContext.ThrowOnInertBattle.
        Region missionRegion = state?.Order?.Mission?.RegionFaction?.Region;
        BattleEngagementResolver engagement = CreateEngagementResolver(random, missionRegion);
        return new MissionExecutionContext(
            state,
            new MissionRules(TestSkills.Stealth, TestSkills.Tactics),
            random,
            engagement,
            new TacticalEntityIdAllocator(),
            new MissionCampaignInputs(
                new Date(1, 1, 1),
                Personnel: TestPersonnelComposition.CreatePersonnel()),
            // Mission steps that raise an interception or an assault screen create neutral elements
            // through the same Application adapter as production.
            engagement);
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
