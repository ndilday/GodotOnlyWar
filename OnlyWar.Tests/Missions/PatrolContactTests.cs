using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class PatrolContactTests
{
    // A routine patrol reports only factions operating openly. Leechwater, 2026-09-24: PDF patrols
    // recorded contact with every hostile presence in the region, hidden ones included, which let
    // the beat confirm a hidden cult and turn it into intelligence-led ambush offers.
    [Fact]
    public void Patrol_ReportsPublicHostilesButNotHiddenOnes()
    {
        Faction pdf = CreateFaction(1, "Imperial", isDefault: true);
        Faction publicCult = CreateFaction(2, "Open Cult");
        Faction hiddenCult = CreateFaction(3, "Hidden Cult");
        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        FactionRelationshipLedger relationships = new();
        planet.AttachRelationshipLedger(relationships);
        relationships.SetStance(pdf, publicCult, FactionStance.Hostile);
        relationships.SetStance(pdf, hiddenCult, FactionStance.Hostile);

        RegionFaction patrolPresence = AddPresence(planet, region, pdf, isPublic: true);
        AddPresence(planet, region, publicCult, isPublic: true);
        AddPresence(planet, region, hiddenCult, isPublic: false);

        Squad squad = TestModelFactory.CreateSquad("Screen", TestModelFactory.CreateSoldier());
        squad.CurrentRegion = region;
        Order order = new([squad], true, false, Aggression.Normal,
            new Mission(MissionType.Patrol, patrolPresence, 0));
        MissionContext context = new(
            order,
            [TestMissionElementFactory.From(new BattleSquad(false, squad))],
            [])
        {
            Impact = 1f
        };

        List<IntelObservation> observations = [];
        new MissionAftermathProcessor(null, null, observations.Add)
            .ApplyMissionResults([context]);

        IntelObservation contact = Assert.Single(observations);
        Assert.Equal(publicCult.Id, contact.BelievedTarget.Id);
        Assert.Equal(IntelObservationSource.PatrolContact, contact.Source);
    }

    private static RegionFaction AddPresence(Planet planet, Region region, Faction faction, bool isPublic)
    {
        PlanetFaction planetFaction = new(faction) { IsPublic = isPublic };
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction presence = new(planetFaction, region) { IsPublic = isPublic };
        region.RegionFactionMap[faction.Id] = presence;
        return presence;
    }

    private static Faction CreateFaction(int id, string name, bool isDefault = false) =>
        new(id, name, Color.White, false, isDefault, FactionBehavior.None, GrowthType.Logistic,
            new Dictionary<int, OnlyWar.Domain.Soldiers.Species>(),
            new Dictionary<int, OnlyWar.Domain.Soldiers.SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Units.UnitTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Fleets.BoatTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Fleets.ShipTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Fleets.FleetTemplate>());
}
