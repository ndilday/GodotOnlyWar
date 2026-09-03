using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FactionCapabilitiesTests
{
    [Fact]
    public void CapabilityQueriesRemainIndependent()
    {
        Faction ghostPlanetFaction = CreateFaction(
            FactionBehavior.HasGhostPlanets);
        Faction dormantPopulationFaction = CreateFaction(
            FactionBehavior.HasDormantPopulations);
        Faction invasionFaction = CreateFaction(
            FactionBehavior.GeneratesInvasions);
        Faction mobFaction = CreateFaction(
            FactionBehavior.MobMentality);

        Assert.True(FactionCapabilities.HasGhostPlanets(ghostPlanetFaction));
        Assert.False(FactionCapabilities.HasDormantPopulations(ghostPlanetFaction));
        Assert.False(FactionCapabilities.GeneratesInvasions(ghostPlanetFaction));

        Assert.True(FactionCapabilities.HasDormantPopulations(dormantPopulationFaction));
        Assert.False(FactionCapabilities.HasGhostPlanets(dormantPopulationFaction));
        Assert.False(FactionCapabilities.HasMobMentality(dormantPopulationFaction));

        Assert.True(FactionCapabilities.GeneratesInvasions(invasionFaction));
        Assert.False(FactionCapabilities.HasGhostPlanets(invasionFaction));
        Assert.False(FactionCapabilities.HasDormantPopulations(invasionFaction));

        Assert.True(FactionCapabilities.HasMobMentality(mobFaction));
        Assert.False(FactionCapabilities.GeneratesInvasions(mobFaction));
    }

    [Fact]
    public void WithCapabilityFiltersWithoutInferringFactionIdentity()
    {
        Faction ghostPlanetFaction = CreateFaction(FactionBehavior.HasGhostPlanets);
        Faction invasionFaction = CreateFaction(FactionBehavior.GeneratesInvasions);
        Faction combinedFaction = CreateFaction(
            FactionBehavior.HasGhostPlanets | FactionBehavior.GeneratesInvasions);

        List<Faction> result = FactionCapabilities
            .WithCapability(
                new[] { ghostPlanetFaction, invasionFaction, combinedFaction },
                FactionBehavior.GeneratesInvasions)
            .ToList();

        Assert.Equal(new[] { invasionFaction, combinedFaction }, result);
    }

    private static Faction CreateFaction(FactionBehavior behavior) => new(
        id: 1,
        name: "Capability Test Faction",
        color: Color.Gray,
        isPlayerFaction: false,
        isDefaultFaction: false,
        behavior,
        growthType: GrowthType.None,
        species: new Dictionary<int, Species>(),
        soldierTemplates: new Dictionary<int, SoldierTemplate>(),
        squadTemplates: new Dictionary<int, SquadTemplate>(),
        unitTemplates: new Dictionary<int, UnitTemplate>(),
        boatTemplates: new Dictionary<int, BoatTemplate>(),
        shipTemplates: new Dictionary<int, ShipTemplate>(),
        fleetTemplates: new Dictionary<int, FleetTemplate>());
}
