using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Tests.Fixtures;

internal static class TestExecutionContextFactory
{
    public static MissionExecutionContext CreateMission(
        MissionContext state,
        IRNG random = null)
    {
        random ??= new FixedRNG();
        GameRulesData rules = new(RulesDatabaseFixture.DatabasePath);
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext battle = new(rules, random, aftermath);
        return new MissionExecutionContext(
            state,
            new MissionRules(TestSkills.Stealth, TestSkills.Tactics),
            random,
            battle);
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
