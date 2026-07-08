using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class StrategicCombatResolverTests
{
    [Fact]
    public void CalculateDefenderBattleValue_UsesMilitaryStrengthForPopulationIsMilitaryDefenders()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction horde = fixture.AddConsumptionFaction(1, population: 1_000, organization: 100);
        horde.Garrison = 200;

        long battleValue = StrategicCombatResolver.CalculateDefenderBattleValue(horde);

        Assert.Equal(horde.MilitaryStrength, battleValue);
        Assert.Equal(1_000, battleValue);
    }

    [Fact]
    public void Resolve_InvadingVictoryEstablishesFootholdFromSurvivors()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 10_000, organization: 100);
        Faction attacker = staging.PlanetFaction.Faction;
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Garrison = 100;
        long committed = 5_000;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatResult result = Resolve(staging, target, committed, Aggression.Normal, invades: true);

        Assert.Equal(StrategicCombatOutcome.InvaderFoothold, result.Outcome);
        Assert.True(result.AttackerWon);
        Assert.True(target.Region.RegionFactionMap.TryGetValue(attacker.Id, out RegionFaction foothold));
        Assert.True(foothold.IsPublic);
        Assert.Equal(result.AttackerSurvivors, foothold.Population);
        Assert.True(result.AttackerSurvivors < committed);
    }

    [Fact]
    public void Resolve_RaidingVictoryReturnsSurvivorsToStaging()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddPublicCult(0, population: 10_000, organization: 100);
        Faction attacker = staging.PlanetFaction.Faction;
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Garrison = 100;
        long committed = 5_000;
        long originalStaging = staging.MilitaryStrength;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatResult result = Resolve(staging, target, committed, Aggression.Normal, invades: false);

        Assert.Equal(StrategicCombatOutcome.Raided, result.Outcome);
        Assert.True(result.AttackerWon);
        Assert.False(target.Region.RegionFactionMap.ContainsKey(attacker.Id));
        Assert.Equal(originalStaging - result.AttackerLosses, staging.MilitaryStrength);
    }

    [Fact]
    public void Resolve_LightningRaidVictoryNeverEstablishesFoothold()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 10_000, organization: 100);
        Faction attacker = staging.PlanetFaction.Faction;
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Garrison = 100;
        long committed = 5_000;
        long originalStaging = staging.MilitaryStrength;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatMission mission = new(
            target,
            attacker,
            committed,
            [new StrategicCombatContribution(staging, committed)],
            Aggression.Cautious,
            invadesOnVictory: true,
            missionType: MissionType.LightningRaid);

        StrategicCombatResult result = new StrategicCombatResolver(new FixedRNG()).Resolve(mission);

        Assert.Equal(MissionType.LightningRaid, mission.MissionType);
        Assert.Equal(StrategicCombatOutcome.Raided, result.Outcome);
        Assert.True(result.AttackerWon);
        Assert.False(result.ControlChanged);
        Assert.False(target.Region.RegionFactionMap.ContainsKey(attacker.Id));
        Assert.True(target.MilitaryStrength < 100);
        Assert.Equal(originalStaging - result.AttackerLosses, staging.MilitaryStrength);
    }

    [Fact]
    public void Resolve_DefenderHoldReturnsSurvivorsToStaging()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 10_000, organization: 100);
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Garrison = 10_000;
        long committed = 1_000;
        long originalStaging = staging.MilitaryStrength;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatResult result = Resolve(staging, target, committed, Aggression.Normal, invades: true);

        Assert.Equal(StrategicCombatOutcome.DefenderHeld, result.Outcome);
        Assert.False(result.AttackerWon);
        Assert.Equal(originalStaging - result.AttackerLosses, staging.MilitaryStrength);
        Assert.True(staging.MilitaryStrength > originalStaging - committed);
    }

    [Fact]
    public void Resolve_DefenderHoldReturnsSurvivorsWithoutCreatingStrengthAcrossStagingRegions()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction stagingA = fixture.AddConsumptionFaction(0, population: 10, organization: 100);
        RegionFaction stagingB = AddSameFactionPresence(fixture, stagingA, 2, population: 10, organization: 100);
        RegionFaction stagingC = AddSameFactionPresence(fixture, stagingA, 3, population: 10, organization: 100);
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Population = 100;
        target.Garrison = 31;

        List<StrategicCombatContribution> contributions =
        [
            new StrategicCombatContribution(stagingA, 1),
            new StrategicCombatContribution(stagingB, 1),
            new StrategicCombatContribution(stagingC, 1)
        ];
        long originalStaging = contributions.Sum(c => c.StagingFaction.MilitaryStrength);
        foreach (StrategicCombatContribution contribution in contributions)
        {
            contribution.StagingFaction.RemoveMilitaryStrength(contribution.BattleValue);
        }

        StrategicCombatMission mission = new(
            target,
            stagingA.PlanetFaction.Faction,
            committedBattleValue: 3,
            contributions,
            Aggression.Normal,
            invadesOnVictory: true);

        StrategicCombatResult result = new StrategicCombatResolver(new FixedRNG()).Resolve(mission);

        Assert.Equal(StrategicCombatOutcome.DefenderHeld, result.Outcome);
        Assert.Equal(1, result.AttackerLosses);
        Assert.Equal(originalStaging - result.AttackerLosses,
            contributions.Sum(c => c.StagingFaction.MilitaryStrength));
    }

    [Fact]
    public void Resolve_NearPeerMassCombatAttritsBothSidesWithoutInstantCapture()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create(defaultRegionPopulation: 200_000);
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Population = 120_000;
        target.Garrison = 80_000;
        long committed = 70_000;
        long originalStaging = staging.MilitaryStrength;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatResult result = Resolve(staging, target, committed, Aggression.Normal, invades: true);

        Assert.Equal(StrategicCombatOutcome.DefenderHeld, result.Outcome);
        Assert.False(result.AttackerWon);
        Assert.InRange(result.AttackerLosses, 3_000, 10_000);
        Assert.InRange(result.DefenderLosses, 3_000, 10_000);
        Assert.True(target.MilitaryStrength > 0);
        Assert.Equal(originalStaging - result.AttackerLosses, staging.MilitaryStrength);
    }

    [Fact]
    public void Resolve_OverwhelmingInvaderEstablishesFootholdWithoutDeletingDefender()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create(defaultRegionPopulation: 100_000);
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 250_000, organization: 100);
        Faction attacker = staging.PlanetFaction.Faction;
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Population = 40_000;
        target.Garrison = 40_000;
        long committed = 200_000;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatResult result = Resolve(staging, target, committed, Aggression.Normal, invades: true);

        Assert.Equal(StrategicCombatOutcome.InvaderFoothold, result.Outcome);
        Assert.True(result.AttackerWon);
        Assert.InRange(result.AttackerSurvivors, committed * 9 / 10, committed - 1);
        Assert.InRange(result.DefenderLosses, 5_000, 39_999);
        Assert.True(target.MilitaryStrength > 0);
        RegionFaction foothold = target.Region.RegionFactionMap[attacker.Id];
        Assert.Equal(result.AttackerSurvivors, foothold.Population);
    }

    [Fact]
    public void Resolve_EntrenchmentIncreasesDefenderEffectiveStrengthAndReducesDefenderLosses()
    {
        StrategicCombatResult open = ResolveAgainstDefaultDefender(entrenchment: 0);
        StrategicCombatResult entrenched = ResolveAgainstDefaultDefender(entrenchment: 8);

        Assert.True(entrenched.DefenderEffectiveStrength > open.DefenderEffectiveStrength);
        Assert.True(entrenched.DefenderLosses < open.DefenderLosses);
    }

    [Fact]
    public void Resolve_CasualtiesAreClampedToAvailableStrength()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Population = 10;
        target.Garrison = 10;
        long committed = 50_000;
        staging.RemoveMilitaryStrength(committed);

        StrategicCombatResult result = Resolve(staging, target, committed, Aggression.Aggressive, invades: true);

        Assert.InRange(result.DefenderLosses, 0, 10);
        Assert.InRange(result.AttackerLosses, 0, committed);
        Assert.InRange(target.Garrison, 0, 10);
    }

    [Fact]
    public void ProcessStrategicCombatMissions_RecordsResultWithoutTacticalContext()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 10_000, organization: 100);
        Faction attacker = staging.PlanetFaction.Faction;
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Garrison = 100;
        long committed = 5_000;
        staging.RemoveMilitaryStrength(committed);
        StrategicCombatMission mission = new(
            target,
            attacker,
            committed,
            [new StrategicCombatContribution(staging, committed)],
            Aggression.Normal,
            invadesOnVictory: true);
        Order order = new(new List<Squad>(), Disposition.Mobile, false, true, Aggression.Normal, mission);
        TurnController controller = new();

        controller.ProcessStrategicCombatMissions([order]);

        Assert.Empty(controller.MissionContexts);
        StrategicCombatResult result = Assert.Single(controller.StrategicCombatResults);
        Assert.Equal(StrategicCombatOutcome.InvaderFoothold, result.Outcome);
    }

    private static StrategicCombatResult ResolveAgainstDefaultDefender(int entrenchment)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction staging = fixture.AddConsumptionFaction(0, population: 20_000, organization: 100);
        RegionFaction target = fixture.DefaultRegionFaction(1);
        target.Garrison = 2_000;
        target.Entrenchment = entrenchment;
        long committed = 5_000;
        staging.RemoveMilitaryStrength(committed);

        return Resolve(staging, target, committed, Aggression.Normal, invades: true);
    }

    private static StrategicCombatResult Resolve(
        RegionFaction staging,
        RegionFaction target,
        long committed,
        Aggression aggression,
        bool invades)
    {
        StrategicCombatMission mission = new(
            target,
            staging.PlanetFaction.Faction,
            committed,
            [new StrategicCombatContribution(staging, committed)],
            aggression,
            invades);
        return new StrategicCombatResolver(new FixedRNG()).Resolve(mission);
    }

    private static RegionFaction AddSameFactionPresence(
        SectorSimulationFixture fixture,
        RegionFaction source,
        int region,
        long population,
        int organization)
    {
        RegionFaction regionFaction = new(source.PlanetFaction, fixture.Planet.Regions[region])
        {
            Population = population,
            IsPublic = true,
            Garrison = 0,
            Organization = organization
        };
        fixture.Planet.Regions[region].RegionFactionMap[source.PlanetFaction.Faction.Id] = regionFaction;
        return regionFaction;
    }
}
