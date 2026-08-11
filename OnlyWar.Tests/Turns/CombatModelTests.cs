using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Coverage for the combat-model corrections (PRD §4.24): casualties reduce a faction's fighting
// strength from the correct pool (Population for a horde, Garrison for a faction with civilians),
// and a victorious invader establishes a foothold from its survivors rather than dissolving.
public class CombatModelTests
{
    [Fact]
    public void SquadBattleValue_IsDerivedFromMemberBattleValues()
    {
        // The test squad is one Sergeant (BV 2) plus up to four Marines (BV 2 each) = 10, computed
        // from the roster rather than stored, so it can never drift from its members (PRD §4.24).
        Assert.Equal(10, TestModelFactory.SquadTemplate.BattleValue);
    }

    [Fact]
    public void FactionDefaults_PopulationIsMilitary_OnlyForNpcHordes()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Faction tyranids = fixture.AddConsumptionFaction(0, population: 1_000, organization: 100)
            .PlanetFaction.Faction;
        Faction cult = fixture.AddHiddenFaction(1, GrowthType.Logistic, population: 1_000)
            .PlanetFaction.Faction;

        Assert.False(fixture.Default.PopulationIsMilitary); // the Imperium has a civilian base
        Assert.True(tyranids.PopulationIsMilitary);         // the swarm's numbers are its army
        Assert.True(cult.PopulationIsMilitary);
    }

    [Fact]
    public void FactionDefaults_InvadesOnVictory_OnlyForConsumptionFactions()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Faction tyranids = fixture.AddConsumptionFaction(0, population: 1_000, organization: 100)
            .PlanetFaction.Faction;
        Faction cult = fixture.AddHiddenFaction(1, GrowthType.Logistic, population: 1_000)
            .PlanetFaction.Faction;

        Assert.True(tyranids.InvadesOnVictory);   // the tide seizes ground it takes
        Assert.False(cult.InvadesOnVictory);      // others raid and withdraw
        Assert.False(fixture.Default.InvadesOnVictory);
    }

    [Fact]
    public void ApplyMilitaryCasualties_HordeLosesPopulation_CivilianLosesGarrison()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(0, population: 10_000, organization: 100);
        horde.Garrison = 500;
        RegionFaction civilian = fixture.DefaultRegionFaction(0);
        civilian.Population = 20_000;
        civilian.Garrison = 1_000;

        horde.RemoveMilitaryStrength(3_000);
        civilian.RemoveMilitaryStrength(400);

        Assert.Equal(7_000, horde.Population);      // a horde bleeds from its population
        Assert.Equal(500, horde.Garrison);          // its garrison is untouched
        // A civilian-base faction loses the fallen from BOTH its garrison and the population they
        // were drawn from (garrison is a sub-value of population); the civilian remainder
        // (Population - Garrison = 19,000) is unchanged, so civilians are still spared.
        Assert.Equal(19_600, civilian.Population);
        Assert.Equal(600, civilian.Garrison);
    }

    [Fact]
    public void ApplyMilitaryCasualties_ClampsAtZero()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(0, population: 1_000, organization: 100);

        horde.RemoveMilitaryStrength(5_000);

        Assert.Equal(0, horde.Population);
    }

    [Fact]
    public void OrganizedAndDisorganizedStrength_AreConcretePoolsAcrossCasualties()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(
            0, population: 10_000, organization: 40);

        Assert.Equal(4_000, horde.OrganizedMilitaryStrength);
        Assert.Equal(6_000, horde.DisorganizedMilitaryStrength);

        horde.RemoveOrganizedMilitaryStrength(1_000);

        Assert.Equal(9_000, horde.MilitaryStrength);
        Assert.Equal(3_000, horde.GetDeployedStrength());
        Assert.Equal(6_000, horde.DisorganizedMilitaryStrength);
        Assert.Equal(33, horde.Organization);
    }

    [Fact]
    public void RaisedTroopsEnterOrganizedPool_AndReorganizationMovesExistingStrength()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(
            0, population: 1_000, organization: 0);

        horde.AddMilitaryStrength(100);
        long moved = horde.ReorganizeMilitaryStrength(
            StrategicCombatRules.ReorganizationBattleValuePerEffort);

        Assert.Equal(1_100, horde.MilitaryStrength);
        Assert.Equal(150, horde.OrganizedMilitaryStrength);
        Assert.Equal(950, horde.DisorganizedMilitaryStrength);
        Assert.Equal(StrategicCombatRules.ReorganizationBattleValuePerEffort, moved);
    }

    [Fact]
    public void UndefendedLossesRemoveOnlyDisorganizedStrength()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(
            0, population: 1_000, organization: 25);

        long destroyed = horde.RemoveDisorganizedMilitaryStrength(300);

        Assert.Equal(300, destroyed);
        Assert.Equal(700, horde.MilitaryStrength);
        Assert.Equal(250, horde.OrganizedMilitaryStrength);
        Assert.Equal(450, horde.DisorganizedMilitaryStrength);
    }

    [Fact]
    public void ReorganizationConstruction_ConvertsAFixedBattleValuePerEffort()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(
            0, population: 10_000, organization: 0);
        ConstructionMission mission = new(DefenseType.Organization, 1, horde);

        MissionTurnProcessor.ApplyConstruction(mission, amount: 2);

        Assert.Equal(
            2 * StrategicCombatRules.ReorganizationBattleValuePerEffort,
            horde.OrganizedMilitaryStrength);
        Assert.Equal(
            10_000 - 2 * StrategicCombatRules.ReorganizationBattleValuePerEffort,
            horde.DisorganizedMilitaryStrength);
    }

    [Fact]
    public void AmbushCasualtiesSampleOrganizedAndDisorganizedPoolsProportionally()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction horde = fixture.AddConsumptionFaction(
            0, population: 1_000, organization: 40);

        MissionAftermathProcessor.RemoveProportionalAmbushLosses(horde, 100);

        Assert.Equal(900, horde.MilitaryStrength);
        Assert.Equal(360, horde.OrganizedMilitaryStrength);
        Assert.Equal(540, horde.DisorganizedMilitaryStrength);
    }

    [Fact]
    public void EstablishInvaderPresence_SeedsANewPublicFootholdFromSurvivors()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Faction tyranids = fixture.AddConsumptionFaction(0, population: 1_000, organization: 100)
            .PlanetFaction.Faction;
        Region target = fixture.Planet.Regions[5];
        Assert.False(target.RegionFactionMap.ContainsKey(tyranids.Id));

        InvaderPresenceService.Establish(tyranids, target, survivors: 250);

        RegionFaction foothold = target.RegionFactionMap[tyranids.Id];
        Assert.True(foothold.IsPublic);
        Assert.Equal(250, foothold.Population); // a horde's survivors become population
        Assert.Equal(0, foothold.Garrison);
    }

    [Fact]
    public void EstablishInvaderPresence_ReinforcesAnExistingFoothold()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction existing = fixture.AddConsumptionFaction(3, population: 1_000, organization: 100);
        Faction tyranids = existing.PlanetFaction.Faction;

        InvaderPresenceService.Establish(tyranids, fixture.Planet.Regions[3], survivors: 400);

        Assert.Equal(1_400, existing.Population);
        Assert.Single(fixture.Planet.Regions[3].RegionFactionMap.Values,
            rf => rf.PlanetFaction.Faction.Id == tyranids.Id);
    }
}
