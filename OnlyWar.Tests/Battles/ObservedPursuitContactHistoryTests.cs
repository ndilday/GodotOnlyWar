using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class ObservedPursuitContactHistoryTests
{
    [Fact]
    public void SteadyProgress_UsesGeometryWhenDeclaredSpeedsOscillate()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        int[,] positions =
        {
            { 0, 30, 4, 32 },
            { 4, 32, 8, 34 },
            { 8, 34, 12, 36 },
            { 12, 36, 16, 38 }
        };
        (float Pursuer, float Quarry)[] declaredSpeeds =
        [
            (5, 9),
            (10, 1),
            (4, 8),
            (4, 8)
        ];

        for (int turn = 0; turn < positions.GetLength(0); turn++)
        {
            ObserveMovement(
                fixture,
                fixture.Quarry,
                positions[turn, 0],
                positions[turn, 1],
                positions[turn, 2],
                positions[turn, 3],
                declaredSpeeds[turn].Pursuer,
                declaredSpeeds[turn].Quarry);
        }

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.True(activity.HasObservedClosingProgress);
        Assert.True(activity.ObservedSeparationGain > PursuitProgressPolicy.ProgressTolerance);
        Assert.True(activity.ProgressHistorySamples >= PursuitProgressPolicy.HistoryLength);
        Assert.True(activity.ClosingSpeed < PursuitContactTolerance());
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void FasterDeclaredPursuerMovingAway_DoesNotCountAsProgress()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);

        for (int turn = 0; turn < PursuitProgressPolicy.StartupGraceTurns + 1; turn++)
        {
            ObserveMovement(
                fixture,
                fixture.Quarry,
                turn * 6,
                30 + (turn * 8),
                (turn + 1) * 6,
                30 + ((turn + 1) * 8),
                pursuerSpeed: 12,
                quarrySpeed: 2);
        }

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.True(activity.ClosingSpeed > BattleContactRules.PursuitSpeedAdvantageTolerance);
        Assert.False(activity.HasObservedClosingProgress);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void BriefBlocking_RetainsTheRecentProductiveWindowAndReportsFailedMove()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        ObserveMovement(fixture, fixture.Quarry, 0, 30, 4, 32, 8, 6);

        fixture.Pursuer.Soldiers[0].CurrentSpeed = 8;
        fixture.Quarry.Soldiers[0].CurrentSpeed = 6;
        Assign(fixture, fixture.Quarry);
        MoveAction blocked = new(
            fixture.Pursuer.Soldiers[0],
            fixture.Grid,
            (4, 0),
            (32, 0),
            fixture.Pursuer.Soldiers[0].Orientation,
            8);
        blocked.Execute(fixture.State);
        Assert.False(blocked.Succeeded);
        fixture.Service.LogPursuitProgress([blocked]);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.True(activity.HasObservedClosingProgress);
        Assert.Equal(1, activity.FailedMoveCount);
        Assert.Equal(0, activity.ActualPursuerDisplacement);
        Assert.True(activity.PlannedPursuerDisplacement > 0);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void SustainedBlocking_AgesTheProductiveSampleOutOfTheWindow()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        ObserveMovement(fixture, fixture.Quarry, 0, 30, 4, 32, 8, 6);

        for (int turn = 0; turn < PursuitProgressPolicy.HistoryLength; turn++)
        {
            fixture.Pursuer.Soldiers[0].CurrentSpeed = 8;
            fixture.Quarry.Soldiers[0].CurrentSpeed = 6;
            Assign(fixture, fixture.Quarry);
            MoveAction blocked = new(
                fixture.Pursuer.Soldiers[0],
                fixture.Grid,
                (4, 0),
                (32, 0),
                fixture.Pursuer.Soldiers[0].Orientation,
                8);
            blocked.Execute(fixture.State);
            Assert.False(blocked.Succeeded);
            fixture.Service.LogPursuitProgress([blocked]);
        }

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.Equal(PursuitProgressPolicy.HistoryLength, activity.ProgressHistorySamples);
        Assert.False(activity.HasObservedClosingProgress);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void EqualSpeedChasing_WithNoGeometricGain_EndsAfterStartupGrace()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);

        for (int turn = 0; turn < PursuitProgressPolicy.StartupGraceTurns + 1; turn++)
        {
            ObserveMovement(
                fixture,
                fixture.Quarry,
                turn * 5,
                30 + (turn * 5),
                (turn + 1) * 5,
                30 + ((turn + 1) * 5),
                pursuerSpeed: 8,
                quarrySpeed: 8);
        }

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.False(activity.HasObservedClosingProgress);
        Assert.False(activity.HasStartupGrace);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void Reassignment_DropsOldProgressAndRepeatedSwitchesCannotRefreshGraceForever()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        ObserveMovement(fixture, fixture.Quarry, 0, 30, 4, 32, 8, 6);

        Assign(fixture, fixture.OtherQuarry);
        PursuitPairActivity reassigned = Assert.Single(fixture.BuildActivities());
        Assert.Equal(fixture.OtherQuarry.Id, reassigned.QuarrySquadId);
        Assert.False(reassigned.HasObservedClosingProgress);
        Assert.Equal(0, reassigned.ProgressHistorySamples);
        Assert.True(reassigned.HasStartupGrace);

        // Fill the first pair's bounded grace window, then rotate targets. The assignment state
        // counts unproductive switches across pair identities, so the fourth new assignment has
        // no fresh startup exemption.
        Assign(fixture, fixture.Quarry);
        Assign(fixture, fixture.OtherQuarry);
        Assign(fixture, fixture.Quarry);
        Assign(fixture, fixture.OtherQuarry);
        Assign(fixture, fixture.Quarry);

        PursuitPairActivity final = Assert.Single(fixture.BuildActivities());
        Assert.False(final.HasObservedClosingProgress);
        Assert.False(final.HasStartupGrace);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(final).Decision);
    }

    [Fact]
    public void MembershipReplacementWithTheSameCount_InvalidatesGeometryHistory()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        ObserveMovement(fixture, fixture.Quarry, 0, 30, 4, 32, 8, 6);

        BattleSoldier oldSoldier = fixture.Pursuer.Soldiers[0];
        fixture.Grid.RemoveSoldier(oldSoldier.Soldier.Id);
        fixture.Pursuer.RemoveSoldier(oldSoldier);
        Soldier replacementSeed = TestModelFactory.CreateSoldier(
            name: "Replacement",
            dexterity: 18,
            skills: [new Skill(TestSkills.Ranged, 12)]);
        replacementSeed.Id = 96_004;
        BattleSoldier replacement = new(replacementSeed, fixture.Pursuer)
        {
            TopLeft = (4, 0),
            CurrentSpeed = 8
        };
        fixture.Pursuer.Soldiers.Add(replacement);
        fixture.Grid.PlaceSoldier(replacement, true, [replacement.TopLeft.Value]);

        Assign(fixture, fixture.Quarry);
        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.Single(fixture.Pursuer.AbleSoldiers);
        Assert.False(activity.HasObservedClosingProgress);
        Assert.Equal(0, activity.ProgressHistorySamples);
        Assert.True(activity.HasStartupGrace);
    }

    [Fact]
    public void InvalidMembershipSample_LogsValidityAndResetReason()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        List<string> log = [];
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = log.Add;
            ObserveMovement(fixture, fixture.Quarry, 0, 30, 4, 32, 8, 6);
            log.Clear();
            Assign(fixture, fixture.Quarry);

            BattleSoldier oldSoldier = fixture.Pursuer.Soldiers[0];
            fixture.Grid.RemoveSoldier(oldSoldier.Soldier.Id);
            fixture.Pursuer.RemoveSoldier(oldSoldier);
            Soldier replacementSeed = TestModelFactory.CreateSoldier(
                name: "Logged replacement",
                dexterity: 18,
                skills: [new Skill(TestSkills.Ranged, 12)]);
            replacementSeed.Id = 96_005;
            BattleSoldier replacement = new(replacementSeed, fixture.Pursuer)
            {
                TopLeft = (4, 0),
                CurrentSpeed = 8
            };
            fixture.Pursuer.Soldiers.Add(replacement);
            fixture.Grid.PlaceSoldier(replacement, true, [replacement.TopLeft.Value]);

            fixture.Service.LogPursuitProgress();

            string trace = Assert.Single(
                log,
                line => line.StartsWith("PURSUIT_PROGRESS ", StringComparison.Ordinal));
            Assert.Contains("sample_validity_reason=membership_changed ", trace);
            Assert.Contains("history_validity_reason=membership_changed ", trace);
            Assert.Contains("history_reset_reason=membership_changed ", trace);
            Assert.Contains("history_window=0/4 ", trace);
            Assert.Contains("observed_progress_contact_evidence=false ", trace);
        }
        finally
        {
            BattleLog.Sink = previous;
        }
    }

    [Fact]
    public void NewlyAssignedPairGetsBoundedStartupThenStallsWithoutEvidence()
    {
        Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
        Assign(fixture, fixture.Quarry);
        PursuitPairActivity initial = Assert.Single(fixture.BuildActivities());
        Assert.True(initial.HasStartupGrace);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(initial).Decision);

        for (int turn = 0; turn < PursuitProgressPolicy.StartupGraceTurns; turn++)
        {
            fixture.Service.LogPursuitProgress();
            Assign(fixture, fixture.Quarry);
        }

        PursuitPairActivity stalled = Assert.Single(fixture.BuildActivities());
        Assert.False(stalled.HasStartupGrace);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(stalled).Decision);
    }

    [Fact]
    public void ObservedProgressExistsWhenDiagnosticsAreDisabled()
    {
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = null;
            Fixture fixture = CreateFixture(quarryX: 30, otherQuarryX: 70);
            ObserveMovement(fixture, fixture.Quarry, 0, 30, 4, 32, 1, 9);

            PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

            Assert.True(activity.HasObservedClosingProgress);
            Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
        }
        finally
        {
            BattleLog.Sink = previous;
        }
    }

    private static float PursuitContactTolerance() =>
        BattleContactRules.PursuitSpeedAdvantageTolerance;

    private static void ObserveMovement(
        Fixture fixture,
        BattleSquad quarry,
        int pursuerStart,
        int quarryStart,
        int pursuerEnd,
        int quarryEnd,
        float pursuerSpeed,
        float quarrySpeed)
    {
        Assign(fixture, quarry);
        BattleSoldier pursuer = fixture.Pursuer.Soldiers[0];
        BattleSoldier quarrySoldier = quarry.Soldiers[0];
        pursuer.CurrentSpeed = pursuerSpeed;
        quarrySoldier.CurrentSpeed = quarrySpeed;
        MoveAction pursuerMove = new(
            pursuer,
            fixture.Grid,
            (pursuerStart, 0),
            (pursuerEnd, 0),
            pursuer.Orientation,
            MathF.Abs(pursuerEnd - pursuerStart));
        MoveAction quarryMove = new(
            quarrySoldier,
            fixture.Grid,
            (quarryStart, 0),
            (quarryEnd, 0),
            quarrySoldier.Orientation,
            MathF.Abs(quarryEnd - quarryStart));
        pursuerMove.Execute(fixture.State);
        quarryMove.Execute(fixture.State);
        Assert.True(pursuerMove.Succeeded, pursuerMove.Description());
        Assert.True(quarryMove.Succeeded, quarryMove.Description());
        fixture.Service.LogPursuitProgress([pursuerMove, quarryMove]);
    }

    private static void Assign(Fixture fixture, BattleSquad quarry) =>
        fixture.Service.ReplaceCurrentTurnPursuitPairings([
            new KeyValuePair<int, int>(fixture.Pursuer.Id, quarry.Id)]);

    private static BattleContactRules.Result EvaluateContact(PursuitPairActivity activity) =>
        BattleContactRules.Evaluate(new(
            Turn: 1,
            IsFirstSide: false,
            ActivePursuerCount: 1,
            AllPursuersBreakOff: false,
            EnemyAlsoWithdrawing: false,
            PursuitPairs: [activity],
            RearGuardActive: false,
            MaskedDepartureProgress: 0,
            WithdrawingSquadRunAllowance: 6));

    private static Fixture CreateFixture(int quarryX, int otherQuarryX)
    {
        BattleSquad pursuerSeed = CreateSquad("Observed Pursuer", 96_001);
        BattleSquad quarrySeed = CreateSquad("Observed Quarry", 96_002);
        BattleSquad otherQuarrySeed = CreateSquad("Observed Other Quarry", 96_003);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [pursuerSeed.Id] = pursuerSeed },
            new Dictionary<int, BattleSquad>
            {
                [quarrySeed.Id] = quarrySeed,
                [otherQuarrySeed.Id] = otherQuarrySeed
            });
        BattleSquad pursuer = state.GetSquad(pursuerSeed.Id);
        BattleSquad quarry = state.GetSquad(quarrySeed.Id);
        BattleSquad otherQuarry = state.GetSquad(otherQuarrySeed.Id);
        BattleGridManager grid = new();
        Place(grid, pursuer.Soldiers[0], true, 0);
        Place(grid, quarry.Soldiers[0], false, quarryX);
        Place(grid, otherQuarry.Soldiers[0], false, otherQuarryX);
        BattleRoundMetrics metrics = new(state);
        BattleWithdrawalService service = CreateService(state, grid, metrics);
        return new(state, grid, service, pursuer, quarry, otherQuarry);
    }

    private static BattleWithdrawalService CreateService(
        BattleState state,
        BattleGridManager grid,
        BattleRoundMetrics metrics)
    {
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        FixedRNG random = new();
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        BattleMoraleService morale = new(
            state,
            grid,
            execution,
            state.AllAttackerSquads.Values.Concat(state.AllOpposingSquads.Values));
        return new(state, grid, rules, metrics, morale);
    }

    private static BattleSquad CreateSquad(string name, int soldierId)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(
            name: name,
            dexterity: 18,
            skills: [new Skill(TestSkills.Ranged, 12)]);
        soldier.Id = soldierId;
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldier));
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool side, int x)
    {
        soldier.TopLeft = (x, 0);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private sealed record Fixture(
        BattleState State,
        BattleGridManager Grid,
        BattleWithdrawalService Service,
        BattleSquad Pursuer,
        BattleSquad Quarry,
        BattleSquad OtherQuarry)
    {
        public IReadOnlyList<PursuitPairActivity> BuildActivities() =>
            Service.BuildPursuitPairActivities(BattleSide.Attacker, BattleSide.Opposing);
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
