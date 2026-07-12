using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using Xunit;

namespace OnlyWar.Tests.Missions;

// MissionOutcomeClassifier is the single place non-battle mission outcomes are determined, from the
// structured signals the mission steps set on MissionContext (no longer from Log-string matching).
// Both the career-log recorder and the end-of-turn report consume its MissionOutcomeClassification.
public class MissionOutcomeClassifierTests
{
    [Fact]
    public void Classify_MissionTypeAndCountsFlowThrough()
    {
        MissionContext context = CreateContext(MissionType.Sabotage);
        context.EnemiesKilled = 5;
        context.Impact = 2.5f;

        MissionOutcomeClassification result = MissionOutcomeClassifier.Classify(context);

        Assert.Equal(MissionType.Sabotage, result.MissionType);
        Assert.Equal(5, result.EnemiesKilled);
        Assert.Equal(2.5f, result.Impact);
    }

    [Fact]
    public void Classify_NoSignals_IsNominalAndUndetected()
    {
        MissionOutcomeClassification result = MissionOutcomeClassifier.Classify(CreateContext(MissionType.Recon));

        Assert.False(result.WasDetected);
        Assert.Equal(MissionForceDisposition.Nominal, result.Disposition);
        Assert.False(result.NoViableTarget);
        Assert.False(result.TargetLocated);
        Assert.False(result.TargetEliminated);
    }

    [Fact]
    public void Classify_SpotterSet_IsDetected()
    {
        MissionContext context = CreateContext(MissionType.Recon);
        context.Spotter = CreateRegionFaction();

        Assert.True(MissionOutcomeClassifier.Classify(context).WasDetected);
    }

    [Fact]
    public void Classify_BrokeContact_MapsToBrokeContactDisposition()
    {
        MissionContext context = CreateContext(MissionType.Recon);
        context.ForceBrokeContact = true;

        Assert.Equal(MissionForceDisposition.BrokeContact,
            MissionOutcomeClassifier.Classify(context).Disposition);
    }

    [Fact]
    public void Classify_LostContact_MapsToLostContactDisposition()
    {
        MissionContext context = CreateContext(MissionType.Recon);
        context.ForceLostContact = true;

        Assert.Equal(MissionForceDisposition.LostContact,
            MissionOutcomeClassifier.Classify(context).Disposition);
    }

    [Fact]
    public void Classify_WithdrewUnderFire_MapsToWithdrewUnderFireDisposition()
    {
        MissionContext context = CreateContext(MissionType.Advance);
        context.ForceWithdrewUnderFire = true;

        Assert.Equal(MissionForceDisposition.WithdrewUnderFire,
            MissionOutcomeClassifier.Classify(context).Disposition);
    }

    [Fact]
    public void Classify_ObjectiveAborted_MapsToAbortedDisposition()
    {
        MissionContext context = CreateContext(MissionType.Infiltrate);
        context.ObjectiveAborted = true;

        Assert.Equal(MissionForceDisposition.AbortedBeforeObjective,
            MissionOutcomeClassifier.Classify(context).Disposition);
    }

    [Fact]
    public void Classify_LostContactWinsOverBrokeContact()
    {
        // A force can break contact on one exfil attempt and be lost on a later one; the worse
        // terminal fate must win so the outcome isn't reported as a clean withdrawal.
        MissionContext context = CreateContext(MissionType.Recon);
        context.ForceBrokeContact = true;
        context.ForceLostContact = true;

        Assert.Equal(MissionForceDisposition.LostContact,
            MissionOutcomeClassifier.Classify(context).Disposition);
    }

    [Fact]
    public void Classify_NoViableTargetFlows()
    {
        MissionContext context = CreateContext(MissionType.LightningRaid);
        context.NoViableTarget = true;

        Assert.True(MissionOutcomeClassifier.Classify(context).NoViableTarget);
    }

    [Fact]
    public void Classify_TargetLocatedWithKills_IsEliminated()
    {
        MissionContext context = CreateContext(MissionType.Assassination);
        context.TargetLocated = true;
        context.EnemiesKilled = 1;

        MissionOutcomeClassification result = MissionOutcomeClassifier.Classify(context);
        Assert.True(result.TargetLocated);
        Assert.True(result.TargetEliminated);
    }

    [Fact]
    public void Classify_TargetLocatedWithoutKills_IsNotEliminated()
    {
        MissionContext context = CreateContext(MissionType.Assassination);
        context.TargetLocated = true;

        Assert.False(MissionOutcomeClassifier.Classify(context).TargetEliminated);
    }

    [Fact]
    public void Classify_KillsWithoutLocatingTarget_IsNotEliminated()
    {
        // A detected assassination attempt can rack up interceptor kills without ever reaching the
        // target; those must not read as a successful hit.
        MissionContext context = CreateContext(MissionType.Assassination);
        context.EnemiesKilled = 3;

        Assert.False(MissionOutcomeClassifier.Classify(context).TargetEliminated);
    }

    [Fact]
    public void Classify_NullMission_DefaultsToPatrol()
    {
        MissionContext context = new(order: null, playerSquads: new List<BattleSquad>(),
            opposingForces: new List<BattleSquad>());

        Assert.Equal(MissionType.Patrol, MissionOutcomeClassifier.Classify(context).MissionType);
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
