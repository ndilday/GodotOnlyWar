using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using Xunit;

namespace OnlyWar.Tests.Missions;

// Guards that the mission steps set MissionContext's structured outcome signals at the points they
// resolve (rather than relying on Log wording). Only the step branches reachable without the full
// game-data/battle machinery are exercised here; the classifier's interpretation of the signals is
// covered by MissionOutcomeClassifierTests.
public class MissionStepOutcomeSignalTests
{
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
        new MeetingEngagementMissionStep().ExecuteMissionStep(context, 0f, null);

        Assert.True(context.NoViableTarget);
    }

    [Fact]
    public void Ambushed_NoForcesToEngage_SetsNoViableTarget()
    {
        MissionContext context = CreateContext(MissionType.Ambush);

        new AmbushedMissionStep().ExecuteMissionStep(context, 0f, null);

        Assert.True(context.NoViableTarget);
    }

    [Fact]
    public void PerformAmbush_NoForcesToEngage_SetsNoViableTarget()
    {
        MissionContext context = CreateContext(MissionType.Ambush);

        new PerformAmbushMissionStep().ExecuteMissionStep(context, 0f, null);

        Assert.True(context.NoViableTarget);
    }

    private static MissionContext CreateContext(MissionType missionType)
    {
        Mission mission = new(missionType, CreateRegionFaction(), 0);
        Order order = new(new List<Squad>(), Disposition.Raiding, true, false,
            Aggression.Cautious, mission);
        return new MissionContext(order, new List<BattleSquad>(), new List<BattleSquad>());
    }

    private static RegionFaction CreateRegionFaction()
    {
        Faction faction = new(1, "Enemy", Color.Red, isPlayerFaction: false, isDefaultFaction: false,
            canInfiltrate: false, GrowthType.None,
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
