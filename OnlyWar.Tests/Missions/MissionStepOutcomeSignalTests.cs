using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Missions;
using OnlyWar.Contracts.Battles;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

// Guards that the mission steps set MissionContext's structured outcome signals at the points they
// resolve (rather than relying on Log wording). Only the step branches reachable without the full
// game-data/battle machinery are exercised here; the classifier's interpretation of the signals is
// covered by MissionOutcomeClassifierTests.
public class MissionStepOutcomeSignalTests
{
    [Fact]
    public void RecordBattleOutcome_TargetCasualty_SetsTargetEliminated()
    {
        MissionContext context = CreateContext(MissionType.Assassination);
        context.AssassinationTargetSoldierId = 42;
        BattleHistory history = new() { FirstSideEnemiesKilled = 2, FirstSideEnemyDeaths = 1 };
        history.KilledSoldierIds.Add(42);

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.True(context.TargetEliminated);
        Assert.Equal(1, context.EnemiesKilled);
        Assert.Equal(2, context.EnemyKillCredits);
    }

    [Fact]
    public void RecordBattleOutcome_OnlyBodyguardCasualty_DoesNotSetTargetEliminated()
    {
        MissionContext context = CreateContext(MissionType.Assassination);
        context.AssassinationTargetSoldierId = 42;
        BattleHistory history = new() { FirstSideEnemiesKilled = 1, FirstSideEnemyDeaths = 1 };
        history.KilledSoldierIds.Add(99);

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.False(context.TargetEliminated);
        Assert.Equal(1, context.EnemiesKilled);
        Assert.Equal(1, context.EnemyKillCredits);
    }

    [Fact]
    public void RecordBattleOutcome_TracksKillCreditsSeparatelyFromUniqueDeaths()
    {
        MissionContext context = CreateContext(MissionType.Ambush);
        BattleHistory history = new() { FirstSideEnemiesKilled = 3, FirstSideEnemyDeaths = 2 };

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.Equal(2, context.EnemiesKilled);
        Assert.Equal(3, context.EnemyKillCredits);
    }

    [Theory]
    [InlineData(BattleEndReason.Withdrawal)]
    [InlineData(BattleEndReason.Rout)]
    public void RecordBattleOutcome_MissionSideWithdrawalOrRout_SetsUnderFire(
        BattleEndReason reason)
    {
        MissionContext context = CreateContext(MissionType.Advance);
        BattleHistory history = new()
        {
            Outcome = new BattleOutcome(reason, BattleSide.Opposing)
        };

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.True(context.ForceWithdrewUnderFire);
    }

    [Fact]
    public void RecordBattleOutcome_OpposingSideWithdrawal_DoesNotSetUnderFire()
    {
        MissionContext context = CreateContext(MissionType.Advance);
        BattleHistory history = new()
        {
            Outcome = new BattleOutcome(BattleEndReason.Withdrawal, BattleSide.Attacker)
        };

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.False(context.ForceWithdrewUnderFire);
    }

    [Fact]
    public void RecordBattleOutcome_MissionAnnihilatesOpposition_OverridesHistoricalRout()
    {
        BattleSquad missionSquad = CreateOrderedBattleSquad(Aggression.Cautious);
        Mission mission = new(MissionType.Advance, CreateRegionFaction(), 0);
        Order order = new([missionSquad.Squad], true, false, Aggression.Cautious, mission);
        MissionContext context = new(
            order,
            [TestMissionElementFactory.From(missionSquad)],
            []);
        BattleHistory history = new()
        {
            FirstSideEnemyDeaths = 1,
            Outcome = new BattleOutcome(
                BattleEndReason.Annihilation,
                BattleSide.Attacker,
                routingSquadIds: [missionSquad.Id])
        };

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.Equal(1, context.EnemiesKilled);
        Assert.False(context.ForceWithdrewUnderFire);
    }

    [Fact]
    public void RecordBattleOutcome_MutualDisengagement_RecordsMissionWithdrawal()
    {
        MissionContext context = CreateContext(MissionType.Advance);
        BattleHistory history = new()
        {
            Outcome = new BattleOutcome(BattleEndReason.MutualDisengagement, null)
        };

        context.RecordBattleOutcome(TestMissionElementFactory.ToEngagementResult(history));

        Assert.True(context.ForceWithdrewUnderFire);
    }

    [Fact]
    public void BattleProfiles_UseMissionAggressionAndCommonOpposingOrders()
    {
        MissionContext context = CreateContext(MissionType.Advance);
        OperationalMissionElement first = TestMissionElementFactory.From(
            CreateOrderedBattleSquad(Aggression.Aggressive));
        OperationalMissionElement second = TestMissionElementFactory.From(
            CreateOrderedBattleSquad(Aggression.Aggressive));

        EngagementSideProfile mission = context.CreateMissionEngagementProfile(EngagementRole.Ambusher);
        EngagementSideProfile opposing = MissionContext.CreateOpposingEngagementProfile(
            [second, first], EngagementRole.Ambushed);

        Assert.Equal(Aggression.Cautious, mission.Aggression);
        Assert.Equal(EngagementRole.Ambusher, mission.Role);
        Assert.Equal(Aggression.Aggressive, opposing.Aggression);
        Assert.Equal(EngagementRole.Ambushed, opposing.Role);
    }

    [Fact]
    public void OpposingProfile_MixedOrMissingOrders_FallsBackToNormal()
    {
        OperationalMissionElement cautious = TestMissionElementFactory.From(
            CreateOrderedBattleSquad(Aggression.Cautious));
        OperationalMissionElement aggressive = TestMissionElementFactory.From(
            CreateOrderedBattleSquad(Aggression.Aggressive));

        Assert.Equal(Aggression.Normal,
            MissionContext.CreateOpposingEngagementProfile([], EngagementRole.Defender).Aggression);
        Assert.Equal(Aggression.Normal,
            MissionContext.CreateOpposingEngagementProfile(
                [cautious, aggressive], EngagementRole.Defender).Aggression);
    }

    [Fact]
    public void InfiltrateShouldContinue_WeekElapsed_SetsObjectiveAborted()
    {
        MissionContext context = CreateContext(MissionType.Infiltrate);
        context.DaysElapsed = 6;

        bool shouldContinue = new InfiltrateMissionStep().ShouldContinue(context);

        Assert.False(shouldContinue);
        Assert.True(context.ObjectiveAborted);
    }

    [Fact]
    public void InfiltrateShouldContinue_NoCombatCapableSquads_SetsObjectiveAborted()
    {
        // DaysElapsed under the week cap, but no squads able to continue -> the casualties abort branch.
        MissionContext context = CreateContext(MissionType.Infiltrate);
        context.DaysElapsed = 1;

        bool shouldContinue = new InfiltrateMissionStep().ShouldContinue(context);

        Assert.False(shouldContinue);
        Assert.True(context.ObjectiveAborted);
    }

    [Fact]
    public void MeetingEngagement_NoForcesToEngage_SetsNoViableTarget()
    {
        MissionContext context = CreateContext(MissionType.Advance);

        // Empty mission and opposing squad lists hit the guard before any battle setup.
        new MissionStepDriver(CreateExecution(context), new MeetingEngagementMissionStep()).RunToCompletion();

        Assert.True(context.NoViableTarget);
    }

    [Fact]
    public void Ambushed_NoForcesToEngage_SetsNoViableTarget()
    {
        MissionContext context = CreateContext(MissionType.Ambush);

        new MissionStepDriver(CreateExecution(context), new AmbushedMissionStep()).RunToCompletion();

        Assert.True(context.NoViableTarget);
    }

    [Fact]
    public void PerformAmbush_NoForcesToEngage_SetsNoViableTarget()
    {
        MissionContext context = CreateContext(MissionType.Ambush);

        new MissionStepDriver(CreateExecution(context), new PerformAmbushMissionStep()).RunToCompletion();

        Assert.True(context.NoViableTarget);
    }

    private static MissionContext CreateContext(MissionType missionType)
    {
        Mission mission = new(missionType, CreateRegionFaction(), 0);
        Order order = new(new List<Squad>(), true, false,
            Aggression.Cautious, mission);
        return new MissionContext(
            order,
            new List<OperationalMissionElement>(),
            new List<OperationalMissionElement>());
    }

    private static MissionExecutionContext CreateExecution(MissionContext context) =>
        TestExecutionContextFactory.CreateMission(context, new SeededRNG(1));

    private static BattleSquad CreateOrderedBattleSquad(Aggression aggression)
    {
        Squad squad = TestModelFactory.CreateSquad(
            $"{aggression} Squad", TestModelFactory.CreateSoldier(name: $"{aggression} Soldier"));
        Mission mission = new(MissionType.Advance, CreateRegionFaction(), 0);
        _ = new Order([squad], false, true, aggression, mission);
        return new BattleSquad(false, squad);
    }

    private static RegionFaction CreateRegionFaction()
    {
        Faction faction = new(1, "Enemy", Color.Red, isPlayerFaction: false, isDefaultFaction: false,
            behavior: FactionBehavior.None, GrowthType.None,
            new Dictionary<int, Species>(), new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(), new Dictionary<int, OnlyWar.Models.Units.UnitTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.BoatTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.ShipTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.FleetTemplate>());
        Planet planet = new(1, "Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        return new RegionFaction(new PlanetFaction(faction), region);
    }
}
