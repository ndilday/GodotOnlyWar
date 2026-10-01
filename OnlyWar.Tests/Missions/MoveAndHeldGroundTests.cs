using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Missions.Recon;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

// Move is an Infiltrate mission anchored on the Chapter's own presence in the target region: it
// crosses by stealth, arrives, and the squads take up residence there. Advance (Attack) shares the
// same end-of-mission rule for moving the force into the ground it reached. Before this, neither
// order moved a squad at all - a won assault "held" its target from the staging region.
public class MoveAndHeldGroundTests
{
    [Fact]
    public void Move_IsOfferedIntoAdjacentRegion_WhetherOrNotItHoldsEnemies()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        fixture.AddPublicCult(region: 1, population: 2_000, organization: 100);
        Region origin = fixture.Planet.Regions[0];
        Region contested = fixture.Planet.Regions[1];
        Region empty = fixture.Planet.Regions[2];

        IReadOnlyList<AvailableMission> intoContested =
            MissionAvailability.GetAvailableMissions(origin, contested);
        IReadOnlyList<AvailableMission> intoEmpty =
            MissionAvailability.GetAvailableMissions(origin, empty);

        Assert.Contains(intoContested, option => option.Kind == MissionAvailabilityKind.Move);
        Assert.Contains(intoContested, option => option.Kind == MissionAvailabilityKind.Attack);
        Assert.Contains(intoEmpty, option => option.Kind == MissionAvailabilityKind.Move);
        Assert.DoesNotContain(
            MissionAvailability.GetAvailableMissions(origin, origin),
            option => option.Kind == MissionAvailabilityKind.Move);
    }

    [Fact]
    public void MoveOrder_IsRepresentedAndLabelledAsMove_AttackIsNot()
    {
        MoveScenario scenario = MoveScenario.Create();
        AvailableMission move = new("Move", MissionAvailabilityKind.Move);

        Assert.True(move.RepresentsOrder(scenario.Order));
        Assert.Equal("Move", MissionAvailability.GetOrderLabel(scenario.Order.Mission));
        Order attack = new([], true, true, Aggression.Normal,
            new Mission(MissionType.Advance, scenario.Enemy, 0));
        Assert.False(move.RepresentsOrder(attack));
    }

    [Fact]
    public void Move_StartsWithInfiltration_AndMainStepIsArrival()
    {
        MoveScenario scenario = MoveScenario.Create();
        MissionExecutionContext execution =
            TestExecutionContextFactory.CreateMission(scenario.Context, new FixedRNG());

        Assert.IsType<InfiltrateMissionStep>(MissionStepOrchestrator.GetStartingStep(execution));
        Assert.IsType<ArriveMissionStep>(MissionStepOrchestrator.GetMainInitialStep(execution));
        Assert.Equal(MissionReturnPolicy.Hold, MissionReturnPolicies.GetPolicy(MissionType.Infiltrate));
    }

    [Fact]
    public void SuccessfulMove_ArrivesInOneDay_AndMovesTheSquadIntoTheRegion()
    {
        MoveScenario scenario = MoveScenario.Create(stealth: 256);
        MissionExecutionContext execution =
            TestExecutionContextFactory.CreateMission(scenario.Context, new FixedRNG());

        new MissionStepDriver(execution, MissionStepOrchestrator.GetStartingStep(execution))
            .RunToCompletion();
        bool moved = MissionForceRelocation.ResolveHeldGround(
            scenario.Context, new Date(1, 1, 1), null);

        Assert.Equal(1, scenario.Context.DaysElapsed);
        Assert.True(scenario.Context.ForceEnteredTargetRegion);
        Assert.True(moved);
        Assert.Same(scenario.Target, scenario.Squad.CurrentRegion);
        Assert.Contains(scenario.Squad, scenario.PlayerTargetPresence.LandedSquads);
        Assert.DoesNotContain(scenario.Squad, scenario.PlayerStagingPresence.LandedSquads);
        MissionOutcomeClassification outcome = MissionOutcomeClassifier.Classify(scenario.Context);
        Assert.True(outcome.HeldTargetRegion);
        Assert.False(outcome.WasDetected);
    }

    // The Move's anchor faction is the Chapter's own presence. Before the spotter was drawn from the
    // region's enemies, a detected Move was "intercepted" by the Chapter's own patrols there.
    [Fact]
    public void DetectedMove_IsSpottedByTheEnemy_NotByTheChaptersOwnPresence()
    {
        MoveScenario scenario = MoveScenario.Create(stealth: 0);
        MissionExecutionContext execution =
            TestExecutionContextFactory.CreateMission(scenario.Context, new BadLuckRNG());

        MissionStepResult result = new InfiltrateMissionStep()
            .ExecuteMissionStep(execution, 0f, null);

        Assert.IsType<DetectedMissionStep>(result.Next);
        Assert.Same(scenario.Enemy, scenario.Context.Spotter);
        Assert.False(scenario.Context.ForceEnteredTargetRegion);
    }

    [Fact]
    public void FailedCheck_IntoRegionWithNoEnemies_StillArrives()
    {
        MoveScenario scenario = MoveScenario.Create(stealth: 0, withEnemy: false);
        MissionExecutionContext execution =
            TestExecutionContextFactory.CreateMission(scenario.Context, new BadLuckRNG());

        MissionStepResult result = new InfiltrateMissionStep()
            .ExecuteMissionStep(execution, 0f, null);

        Assert.IsType<ArriveMissionStep>(result.Next);
        Assert.Null(scenario.Context.Spotter);
        Assert.True(scenario.Context.ForceEnteredTargetRegion);
    }

    [Fact]
    public void AdvanceThatEnteredTheRegion_MovesTheSquadIn()
    {
        MoveScenario scenario = MoveScenario.Create(missionType: MissionType.Advance);
        scenario.Context.ForceEnteredTargetRegion = true;

        bool moved = MissionForceRelocation.ResolveHeldGround(
            scenario.Context, new Date(1, 1, 1), null);

        Assert.True(moved);
        Assert.Same(scenario.Target, scenario.Squad.CurrentRegion);
        Assert.Contains(scenario.Squad, scenario.PlayerTargetPresence.LandedSquads);
    }

    [Theory]
    [InlineData("entered-false")]
    [InlineData("withdrew")]
    [InlineData("broke-off")]
    [InlineData("lost")]
    [InlineData("returned")]
    [InlineData("reciprocal-defeat")]
    public void AdvanceThatDidNotHoldTheRegion_StaysWhereItStarted(string outcome)
    {
        MoveScenario scenario = MoveScenario.Create(missionType: MissionType.Advance);
        MissionContext context = scenario.Context;
        context.ForceEnteredTargetRegion = outcome != "entered-false";
        context.ForceWithdrewUnderFire = outcome == "withdrew";
        context.AssaultBrokenOff = outcome == "broke-off";
        context.ForceLostContact = outcome == "lost";
        context.ForceReturnedToBase = outcome == "returned";
        context.ReciprocalAssaultDefeated = outcome == "reciprocal-defeat";

        bool moved = MissionForceRelocation.ResolveHeldGround(context, new Date(1, 1, 1), null);

        Assert.False(moved);
        Assert.False(context.ForceHeldTargetRegion);
        Assert.Same(scenario.Staging, scenario.Squad.CurrentRegion);
        Assert.Contains(scenario.Squad, scenario.PlayerStagingPresence.LandedSquads);
    }

    [Fact]
    public void ReturnPolicyMissions_AreNeverMovedByHeldGround()
    {
        MoveScenario scenario = MoveScenario.Create(missionType: MissionType.Recon);
        scenario.Context.ForceEnteredTargetRegion = true;

        Assert.False(MissionForceRelocation.ResolveHeldGround(
            scenario.Context, new Date(1, 1, 1), null));
        Assert.Same(scenario.Staging, scenario.Squad.CurrentRegion);
    }

    // The roll is subtracted from the skill advantage (GaussianCalculator), so a roll this high fails
    // every check whatever the skill, and the detection branch is taken.
    private sealed class BadLuckRNG : IRNG
    {
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
        public double GetLinearDouble() => 0.0;
        public int GetIntBelowMax(int min, int max) => min;
        public double NextRandomZValue() => 10.0;
    }

    private sealed class MoveScenario
    {
        public Region Staging { get; private init; }
        public Region Target { get; private init; }
        public RegionFaction Enemy { get; private init; }
        public RegionFaction PlayerStagingPresence { get; private init; }
        public RegionFaction PlayerTargetPresence { get; private init; }
        public Squad Squad { get; private init; }
        public Order Order { get; private init; }
        public MissionContext Context { get; private init; }

        // A Chapter squad in the staging region, ordered into an adjacent target region. The target
        // holds an enemy (unless withEnemy is false) and a Chapter presence, which is the Move's
        // anchor and where an Advance's squads land. An Advance targets the enemy instead.
        public static MoveScenario Create(
            int stealth = 256,
            bool withEnemy = true,
            MissionType missionType = MissionType.Infiltrate)
        {
            Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
            Region target = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
            Region staging = new(2, planet, 0, "Staging Region", new RegionCoordinate(1, 0), 0);
            planet.Regions[0] = target;

            RegionFaction enemy = null;
            if (withEnemy)
            {
                Faction enemyFaction = CreateFaction(20, "Orks", isPlayerFaction: false);
                // Population must be set before Garrison; RegionFaction clamps Garrison to Population.
                enemy = new RegionFaction(new PlanetFaction(enemyFaction), target)
                {
                    Population = 1000,
                    Garrison = 100
                };
                target.RegionFactionMap[enemyFaction.Id] = enemy;
            }

            Faction chapter = CreateFaction(10, "Chapter", isPlayerFaction: true);
            PlanetFaction chapterOnPlanet = new(chapter) { IsPublic = true };
            planet.PlanetFactionMap[chapter.Id] = chapterOnPlanet;
            RegionFaction stagingPresence = new(chapterOnPlanet, staging);
            staging.RegionFactionMap[chapter.Id] = stagingPresence;
            RegionFaction targetPresence = new(chapterOnPlanet, target);
            target.RegionFactionMap[chapter.Id] = targetPresence;

            SquadTemplate template = new(
                10,
                "Tactical Squad",
                TestModelFactory.DefaultWeapons,
                [],
                TestModelFactory.TestArmor,
                [
                    new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
                    new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)
                ],
                SquadTypes.None)
            {
                Faction = chapter
            };
            Squad squad = new("Tactical Squad", null, template);
            squad.AddSquadMember(CreateMarine(TestModelFactory.SergeantTemplate, "Sergeant", stealth));
            squad.AddSquadMember(CreateMarine(TestModelFactory.MarineTemplate, "Marine", stealth));
            squad.CurrentRegion = staging;
            stagingPresence.LandedSquads.Add(squad);

            RegionFaction anchor = missionType == MissionType.Infiltrate
                ? targetPresence
                : enemy ?? targetPresence;
            Order order = new(
                [squad],
                isQuiet: true,
                isActivelyEngaging: false,
                levelOfAggression: Aggression.Normal,
                mission: new Mission(missionType, anchor, missionSize: 0));
            MissionContext context = new(order,
                [TestMissionElementFactory.From(new BattleSquad(true, squad))], []);

            return new MoveScenario
            {
                Staging = staging,
                Target = target,
                Enemy = enemy,
                PlayerStagingPresence = stagingPresence,
                PlayerTargetPresence = targetPresence,
                Squad = squad,
                Order = order,
                Context = context
            };
        }

        private static Soldier CreateMarine(SoldierTemplate template, string name, int stealth) =>
            TestModelFactory.CreateSoldier(
                template,
                name,
                skills: [new Skill(TestSkills.Stealth, stealth), new Skill(TestSkills.Tactics, 256)]);

        private static Faction CreateFaction(int id, string name, bool isPlayerFaction) =>
            new(
                id,
                name,
                Color.Red,
                isPlayerFaction: isPlayerFaction,
                isDefaultFaction: false,
                behavior: FactionBehavior.None,
                GrowthType.Conversion,
                new Dictionary<int, Species>(),
                new Dictionary<int, SoldierTemplate>(),
                new Dictionary<int, SquadTemplate>(),
                new Dictionary<int, UnitTemplate>(),
                new Dictionary<int, BoatTemplate>(),
                new Dictionary<int, ShipTemplate>(),
                new Dictionary<int, FleetTemplate>());
    }
}
