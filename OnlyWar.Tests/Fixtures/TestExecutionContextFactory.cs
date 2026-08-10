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
        // CAUTION (2026-08-09). FixedRNG's NextRandomZValue is a constant 0.0, so a contested check
        // is decided entirely by the two sides' MEANS -- there is no variance to break a tie. A
        // fixture whose opposing forces are exactly matched therefore produces the same result on
        // every turn forever, and a battle between them can never resolve. Keep the mean margin of
        // any contested roll a test relies on strictly off zero; see the accuracy comment on
        // TestModelFactory's Test Knife for the case that hung a battle for 1000 turns.
        random ??= new FixedRNG();
        GameRulesData rules = new(RulesDatabaseFixture.DatabasePath);
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        // throwOnInertBattle: a battle that stops progressing is an engine bug the game survives
        // and a test must not. See BattleExecutionContext.ThrowOnInertBattle.
        BattleExecutionContext battle = new(
            rules, random, aftermath, throwOnInertBattle: true);
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
