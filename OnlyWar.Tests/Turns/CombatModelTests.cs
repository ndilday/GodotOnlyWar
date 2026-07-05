using OnlyWar.Helpers;
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
    public void FactionDefaults_PopulationIsMilitary_OnlyForNpcHordes()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
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
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
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
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction horde = fixture.AddConsumptionFaction(0, population: 10_000, organization: 100);
        horde.Garrison = 500;
        RegionFaction civilian = fixture.DefaultRegionFaction(0);
        civilian.Population = 20_000;
        civilian.Garrison = 1_000;

        TurnController.ApplyMilitaryCasualties(horde, 3_000);
        TurnController.ApplyMilitaryCasualties(civilian, 400);

        Assert.Equal(7_000, horde.Population);     // a horde bleeds from its population
        Assert.Equal(500, horde.Garrison);         // its garrison is untouched
        Assert.Equal(20_000, civilian.Population); // civilians are spared
        Assert.Equal(600, civilian.Garrison);      // the military pool takes the loss
    }

    [Fact]
    public void ApplyMilitaryCasualties_ClampsAtZero()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction horde = fixture.AddConsumptionFaction(0, population: 1_000, organization: 100);

        TurnController.ApplyMilitaryCasualties(horde, 5_000);

        Assert.Equal(0, horde.Population);
    }

    [Fact]
    public void EstablishInvaderPresence_SeedsANewPublicFootholdFromSurvivors()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Faction tyranids = fixture.AddConsumptionFaction(0, population: 1_000, organization: 100)
            .PlanetFaction.Faction;
        Region target = fixture.Planet.Regions[5];
        Assert.False(target.RegionFactionMap.ContainsKey(tyranids.Id));

        TurnController.EstablishInvaderPresence(tyranids, target, survivors: 250);

        RegionFaction foothold = target.RegionFactionMap[tyranids.Id];
        Assert.True(foothold.IsPublic);
        Assert.Equal(250, foothold.Population); // a horde's survivors become population
        Assert.Equal(0, foothold.Garrison);
    }

    [Fact]
    public void EstablishInvaderPresence_ReinforcesAnExistingFoothold()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction existing = fixture.AddConsumptionFaction(3, population: 1_000, organization: 100);
        Faction tyranids = existing.PlanetFaction.Faction;

        TurnController.EstablishInvaderPresence(tyranids, fixture.Planet.Regions[3], survivors: 400);

        Assert.Equal(1_400, existing.Population);
        Assert.Single(fixture.Planet.Regions[3].RegionFactionMap.Values,
            rf => rf.PlanetFaction.Faction.Id == tyranids.Id);
    }
}
