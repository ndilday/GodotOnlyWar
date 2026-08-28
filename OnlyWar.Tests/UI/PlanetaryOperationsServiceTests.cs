using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using OnlyWar.Helpers.UI;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.UI;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class PlanetaryOperationsServiceTests
{
    [Fact]
    public void Eligibility_NoMission_ContainsOnlyUnassignedTargetAndAdjacentSquads()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region target = fixture.Planet.Regions[7];
        Region adjacent = target.GetAdjacentRegions().First();
        Region distant = fixture.Planet.Regions.First(region =>
            region != target && !target.GetAdjacentRegions().Contains(region));
        Squad targetSquad = AddPlayerSquad(fixture, target, "Target Squad");
        Squad adjacentSquad = AddPlayerSquad(fixture, adjacent, "Adjacent Squad");
        Squad distantSquad = AddPlayerSquad(fixture, distant, "Distant Squad");
        Squad assigned = AddPlayerSquad(fixture, target, "Assigned Elsewhere");
        Order otherOrder = new(
            [assigned], true, false, Aggression.Normal,
            new Mission(MissionType.Recon, fixture.DefaultRegionFaction(0), 0));
        fixture.Sector.AddNewOrder(otherOrder);

        RegionalEligibilityResult result = RegionalOrderEligibilityService.Build(
            fixture.Sector, target);

        Assert.Contains(result.Candidates, item => item.Squad == targetSquad);
        Assert.Contains(result.Candidates, item => item.Squad == adjacentSquad);
        Assert.DoesNotContain(result.Candidates, item => item.Squad == distantSquad);
        Assert.DoesNotContain(result.Candidates, item => item.Squad == assigned);
        Assert.Contains(result.Excluded, item =>
            item.Squad == assigned
            && item.Exclusion == SquadEligibilityExclusion.AssignedElsewhere);
    }

    [Theory]
    [InlineData(MissionAvailabilityKind.Defend)]
    [InlineData(MissionAvailabilityKind.Patrol)]
    [InlineData(MissionAvailabilityKind.FortifyEntrenchment)]
    [InlineData(MissionAvailabilityKind.BuildListeningPost)]
    [InlineData(MissionAvailabilityKind.BuildAntiAir)]
    public void Eligibility_StaticMissionKinds_ExcludeAdjacentOrigin(
        MissionAvailabilityKind kind)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region target = fixture.Planet.Regions[7];
        Region adjacent = target.GetAdjacentRegions().First();
        Squad targetSquad = AddPlayerSquad(fixture, target, "Target Squad");
        Squad adjacentSquad = AddPlayerSquad(fixture, adjacent, "Adjacent Squad");
        AvailableMission mission = MissionAvailability.GetAvailableMissions(target, target)
            .Single(option => option.Kind == kind);

        RegionalEligibilityResult result = RegionalOrderEligibilityService.Build(
            fixture.Sector, target, mission);

        Assert.Contains(result.Candidates, item => item.Squad == targetSquad);
        Assert.DoesNotContain(result.Candidates, item => item.Squad == adjacentSquad);
        Assert.Contains(result.Excluded, item =>
            item.Squad == adjacentSquad
            && item.Exclusion == SquadEligibilityExclusion.MissionUnavailable);
    }

    [Fact]
    public void ExistingOrder_KeepsItsAssignedSquadVisibleButNotAddSelectable()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region target = fixture.Planet.Regions[7];
        Squad assigned = AddPlayerSquad(fixture, target, "Assigned Squad");
        AvailableMission recon = MissionAvailability.GetAvailableMissions(target, target)
            .Single(option => option.Kind == MissionAvailabilityKind.Recon);
        Order order = OrderAssignment.AssignSquadsToMission(
            [assigned], target, recon, -1, Aggression.Normal);

        RegionalSquadCandidate candidate = RegionalOrderEligibilityService.Build(
                fixture.Sector, target, recon, order)
            .Candidates.Single(item => item.Squad == assigned);

        Assert.True(candidate.IsAssignedToContext);
        Assert.False(candidate.IsSelectable);
    }

    [Fact]
    public void OrderMutation_MultiOriginReconCreatesOneOrderAndReusesIt()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region target = fixture.Planet.Regions[7];
        Region adjacent = target.GetAdjacentRegions().First();
        Squad first = AddPlayerSquad(fixture, target, "First Squad");
        Squad second = AddPlayerSquad(fixture, adjacent, "Second Squad");
        AvailableMission recon = MissionAvailability.GetAvailableMissions(target, target)
            .Single(option => option.Kind == MissionAvailabilityKind.Recon);

        OrderMutationResult created = OrderMutationService.CreateOrAdd(
            fixture.Sector, target, recon, [first, second], -1, Aggression.Normal);
        Squad third = AddPlayerSquad(fixture, adjacent, "Third Squad");
        OrderMutationResult reinforced = OrderMutationService.CreateOrAdd(
            fixture.Sector, target, recon, [third], -1, Aggression.Cautious);

        Assert.True(created.Succeeded);
        Assert.Equal(OrderMutationKind.Created, created.Kind);
        Assert.True(reinforced.Succeeded);
        Assert.Equal(OrderMutationKind.Reinforced, reinforced.Kind);
        Assert.Same(created.Order, reinforced.Order);
        Assert.Equal(3, created.Order.AssignedSquads.Count);
        Assert.Single(fixture.Sector.Orders.Values, order =>
            order.Mission.RegionFaction.Region == target
            && order.Mission.MissionType == MissionType.Recon);
    }

    [Fact]
    public void Embark_InsufficientCapacityChangesNeitherLocationNorOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region source = fixture.Planet.Regions[0];
        Squad squad = AddPlayerSquad(fixture, source, "Surface Squad", members: 2);
        AvailableMission defend = MissionAvailability.GetAvailableMissions(source, source)
            .Single(option => option.Kind == MissionAvailabilityKind.Defend);
        Order order = OrderAssignment.AssignSquadsToMission(
            [squad], source, defend, -1, Aggression.Normal);
        Ship ship = AddOrbitingShip(fixture, capacity: 1);

        ForceMovementResult result = PlanetForceMovementService.Embark(
            fixture.Sector, fixture.Planet, source, ship, [squad]);

        Assert.False(result.Succeeded);
        Assert.Same(source, squad.CurrentRegion);
        Assert.Null(squad.BoardedLocation);
        Assert.Same(order, squad.CurrentOrders);
        Assert.Contains(squad, GetPlayerPresence(fixture, source).LandedSquads);
        Assert.Empty(ship.LoadedSquads);
    }

    [Fact]
    public void Embark_AssignedLastSquadEndsOrderThenBoardsWholeSquad()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region source = fixture.Planet.Regions[0];
        Squad squad = AddPlayerSquad(fixture, source, "Surface Squad", members: 2);
        AvailableMission defend = MissionAvailability.GetAvailableMissions(source, source)
            .Single(option => option.Kind == MissionAvailabilityKind.Defend);
        Order order = OrderAssignment.AssignSquadsToMission(
            [squad], source, defend, -1, Aggression.Normal);
        Ship ship = AddOrbitingShip(fixture, capacity: 10);

        ForceMovementResult result = PlanetForceMovementService.Embark(
            fixture.Sector, fixture.Planet, source, ship, [squad]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.OrdersEnded);
        Assert.Null(squad.CurrentRegion);
        Assert.Same(ship, squad.BoardedLocation);
        Assert.Null(squad.CurrentOrders);
        Assert.Contains(squad, ship.LoadedSquads);
        Assert.DoesNotContain(order, fixture.Sector.Orders.Values);
    }

    [Fact]
    public void Land_RemovesShipRelationshipAndCreatesNoOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region destination = fixture.Planet.Regions[3];
        Ship ship = AddOrbitingShip(fixture, capacity: 10);
        Squad squad = CreatePlayerSquad(fixture, "Orbiting Squad", 2);
        ship.LoadSquad(squad);
        squad.BoardedLocation = ship;

        ForceMovementResult result = PlanetForceMovementService.Land(
            fixture.Sector, fixture.Planet, destination, [squad]);

        Assert.True(result.Succeeded);
        Assert.Same(destination, squad.CurrentRegion);
        Assert.Null(squad.BoardedLocation);
        Assert.Null(squad.CurrentOrders);
        Assert.DoesNotContain(squad, ship.LoadedSquads);
        Assert.Contains(squad, GetPlayerPresence(fixture, destination).LandedSquads);

        IReadOnlyList<DossierCardData> cards = PlanetaryOperationsViewModelBuilder.BuildRegionCards(
            destination, fixture.Sector);
        DossierCardData defenses = Assert.Single(cards,
            card => card.Title == "Imperial Defenses");
        Assert.Equal("2", defenses.Rows.Single(row => row.Item1 == "Forces").Item2);
    }

    [Fact]
    public void MapBuilder_AlwaysUsesExactDiamondAndStableTerrain()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        PlanetRegionMapViewModel first = PlanetRegionMapViewModelBuilder.Build(
            fixture.Sector, fixture.Planet, PlanetMapOverlay.Control, fixture.Default.Id);
        PlanetRegionMapViewModel second = PlanetRegionMapViewModelBuilder.Build(
            fixture.Sector, fixture.Planet, PlanetMapOverlay.Control, fixture.Default.Id);

        Assert.Equal(new[] { 1, 2, 3, 4, 3, 2, 1 }, first.Rows.Select(row => row.Count));
        Assert.Equal(16, first.Rows.Sum(row => row.Count));
        Assert.Equal(
            first.Rows.SelectMany(row => row).Select(card => card.TerrainVariant),
            second.Rows.SelectMany(row => row).Select(card => card.TerrainVariant));
    }

    [Fact]
    public void MapBuilder_RotatesRowsToMatchEncodedHexProjection()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        PlanetRegionMapViewModel map = PlanetRegionMapViewModelBuilder.Build(
            fixture.Sector, fixture.Planet, PlanetMapOverlay.Control, fixture.Default.Id);

        Assert.Equal(
            new[] { "9", "5,12", "2,8,14", "0,4,11,15", "1,7,13", "3,10", "6" },
            map.Rows.Select(row => string.Join(",", row.Select(card =>
                System.Array.IndexOf(fixture.Planet.Regions, card.Region)))));
    }

    [Fact]
    public void Eligibility_UsesCanonicalTopologyForBetaAndGamma()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region beta = fixture.Planet.Regions[1];
        Region gamma = fixture.Planet.Regions[2];
        Squad squad = AddPlayerSquad(fixture, beta, "Beta Squad");

        RegionalEligibilityResult result = RegionalOrderEligibilityService.Build(
            fixture.Sector, gamma);

        Assert.DoesNotContain(result.Candidates, candidate => candidate.Squad == squad);
        Assert.DoesNotContain(result.Groups, group => group.Origin == beta);
    }

    [Fact]
    public void MapBuilder_UsesControllingEnemyFactionColorForRegionBorder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction enemy = fixture.AddControllingFaction(0, "Red Corsairs", population: 1000);

        PlanetRegionMapViewModel map = PlanetRegionMapViewModelBuilder.Build(
            fixture.Sector, fixture.Planet, PlanetMapOverlay.Control, fixture.Default.Id);
        RegionMapCardViewModel card = map.Rows.SelectMany(row => row)
            .Single(item => item.Region == fixture.Planet.Regions[0]);

        Assert.Equal(RegionControlState.Enemy, card.Control);
        Assert.Equal(enemy.PlanetFaction.Faction.Id, card.ControlFactionId);
        Assert.Equal(enemy.PlanetFaction.Faction.Name, card.ControlFactionName);
        Assert.Equal(enemy.PlanetFaction.Faction.Color.ToGodotColor(), card.ControlBorderColor);
        Assert.NotEqual(OnlyWarStyle.OpposingAccent, card.ControlBorderColor);
        Assert.Contains("Control: Red Corsairs", RegionMapCardView.BuildTooltip(card));
    }

    [Fact]
    public void MapBuilder_UsesBrightOrangeForContestedRegionBorder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        fixture.AddControllingFaction(0, "Red Corsairs", population: 1000);
        fixture.AddPublicCult(0, population: 1000, organization: 100);

        PlanetRegionMapViewModel map = PlanetRegionMapViewModelBuilder.Build(
            fixture.Sector, fixture.Planet, PlanetMapOverlay.Control, fixture.Default.Id);
        RegionMapCardViewModel card = map.Rows.SelectMany(row => row)
            .Single(item => item.Region == fixture.Planet.Regions[0]);

        Assert.Equal(RegionControlState.Contested, card.Control);
        Assert.Equal(OnlyWarStyle.MapContested, card.ControlBorderColor);
        Assert.NotEqual(OnlyWarStyle.Gold, card.ControlBorderColor);
    }

    [Fact]
    public void MapBuilder_BuildsUnifiedRegionHoverSummary()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        RegionFaction enemy = fixture.AddPublicCult(0, population: 5_000, organization: 100);
        fixture.DefaultPlanetFaction.SeedTargetBelief(
            region,
            enemy.PlanetFaction.Faction,
            evidence: 6f,
            estimatedPopulation: 5_000,
            estimatedMilitaryStrength: 5_000,
            evidenceWeek: 0);

        Squad unassigned = AddPlayerSquad(fixture, region, "Unassigned Squad");
        Squad assigned = AddPlayerSquad(fixture, region, "Assigned Squad");
        Mission assignedMission = new(MissionType.Ambush, enemy, 1);
        Mission openMission = new(MissionType.Sabotage, enemy, 1);
        region.SpecialMissions.Add(assignedMission);
        region.SpecialMissions.Add(openMission);
        fixture.Sector.AddNewOrder(new Order(
            [assigned], true, false, Aggression.Normal, assignedMission));

        RegionMapCardViewModel card = PlanetRegionMapViewModelBuilder.Build(
                fixture.Sector, fixture.Planet, PlanetMapOverlay.Control, fixture.Default.Id)
            .Rows.SelectMany(row => row)
            .Single(item => item.Region == region);

        Assert.Equal(1, card.UnassignedSquads);
        Assert.Equal(1, card.MissionOpportunities);
        RegionEnemyForceEstimate estimate = Assert.Single(card.PublicEnemyForces);
        Assert.Equal("Genestealer Cult", estimate.FactionName);
        Assert.Equal("Thousands", estimate.ForceEstimate);
        Assert.Equal(
            "Region 0\n"
            + "Control: Contested\n"
            + "Unassigned Squads: 1\n"
            + "Mission Opportunities: 1\n"
            + "Genestealer Cult: Thousands",
            RegionMapCardView.BuildTooltip(card));
        Assert.Null(unassigned.CurrentOrders);
    }

    [Fact]
    public void ControlPresentation_HiddenFactionDoesNotCreatePresenceOrContest()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        fixture.AddHiddenFaction(0, GrowthType.Conversion, 1_000);

        RegionControlPresentationModel presentation = RegionControlPresentation.Build(region);

        Assert.Equal(RegionControlState.Imperial, presentation.State);
        Assert.Single(presentation.Presences);
        Assert.Equal(fixture.Default.Id, presentation.Presences[0].FactionId);
    }

    [Fact]
    public void RegionCards_OrderImperialDefensesBeforeHostileForcesAndSortHostilesByFactionName()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        AddPlayerSquad(fixture, region, "Chapter Squad");
        fixture.AddConsumptionFaction(0, population: 100, organization: 100);
        fixture.AddPublicCult(0, population: 100, organization: 100);

        IReadOnlyList<DossierCardData> cards = PlanetaryOperationsViewModelBuilder.BuildRegionCards(
            region, fixture.Sector);

        Assert.Equal(
            new[]
            {
                ("Selected Region", "Region 0"),
                ("Imperial Defenses", "Imperium"),
                ("Hostile Force", "Genestealer Cult"),
                ("Hostile Force", "Tyranids")
            },
            cards.Select(card => (card.Title, card.Subtitle)));

        Assert.Equal("1", cards[1].Rows.Single(row => row.Item1 == "Forces").Item2);
    }

    [Fact]
    public void RegionCards_UsesChapterCardWhenNoDefaultImperialPresenceExists()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        fixture.AddControllingFaction(0, "Red Corsairs", population: 100);
        AddPlayerSquad(fixture, region, "Chapter Squad", members: 2);

        IReadOnlyList<DossierCardData> cards = PlanetaryOperationsViewModelBuilder.BuildRegionCards(
            region, fixture.Sector);

        DossierCardData defenses = Assert.Single(cards,
            card => card.Title == "Imperial Defenses");
        Assert.Equal("Test Chapter", defenses.Subtitle);
        Assert.Equal("2", defenses.Rows.Single(row => row.Item1 == "Forces").Item2);
    }

    [Fact]
    public void WorldDossier_UsesLargestFactionForceBand()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction first = fixture.AddPublicCult(0, population: 5, organization: 100);
        Faction cult = first.PlanetFaction.Faction;

        foreach ((int regionIndex, long estimate) in new[]
        {
            (1, 50L),
            (2, 5_000L)
        })
        {
            Region region = fixture.Planet.Regions[regionIndex];
            region.RegionFactionMap[cult.Id] = new RegionFaction(first.PlanetFaction, region)
            {
                Population = estimate,
                IsPublic = true,
                Organization = 100
            };
            fixture.DefaultPlanetFaction.SeedTargetBelief(
                region,
                cult,
                evidence: 6f,
                estimatedPopulation: estimate,
                estimatedMilitaryStrength: estimate,
                evidenceWeek: 1);
        }

        fixture.DefaultPlanetFaction.SeedTargetBelief(
            fixture.Planet.Regions[0],
            cult,
            evidence: 6f,
            estimatedPopulation: 5,
            estimatedMilitaryStrength: 5,
            evidenceWeek: 1);

        WorldDossierViewModel dossier = PlanetaryOperationsViewModelBuilder.BuildWorld(
            fixture.Sector,
            fixture.Planet,
            fixture.Planet.Regions[0]);
        DossierCardData cultCard = dossier.StrengthCards.Single(card => card.Subtitle == "Genestealer Cult");

        Assert.Equal("Thousands", cultCard.Rows.Single(row => row.Item1 == "Force Estimate").Item2);
    }

    [Fact]
    public void IntelPresentation_SuspectedUsesBandAndShowsAgeConfidenceAndDecay()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction cult = fixture.AddPublicCult(0, population: 500, organization: 100);
        FactionIntelBelief belief = new(
            fixture.Planet.Regions[0], cult.PlanetFaction.Faction,
            evidence: 2f, estimatedPopulation: 500,
            estimatedMilitaryStrength: 500, lastEvidenceWeek: 7);

        IntelEstimatePresentation presentation = IntelEstimatePresentationBuilder.Build(belief, 10);

        Assert.Equal(IntelLevel.Suspected, presentation.Level);
        Assert.Equal("Dozens–Thousands", presentation.Value);
        Assert.Contains("●●○○", presentation.Confidence);
        Assert.Equal("Evidence age: 3 wk", presentation.EvidenceAge);
        Assert.Contains("Drops to Rumor", presentation.DecayNotice);
    }

    [Fact]
    public void WorldIntelPresentation_UsesWeakestRegionalConfidence()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction first = fixture.AddPublicCult(0, population: 5000, organization: 100);
        Faction cult = first.PlanetFaction.Faction;
        Region secondRegion = fixture.Planet.Regions[1];
        RegionFaction second = new(first.PlanetFaction, secondRegion)
        {
            Population = 100,
            IsPublic = true,
            Organization = 100
        };
        secondRegion.RegionFactionMap[cult.Id] = second;
        fixture.DefaultPlanetFaction.SeedTargetBelief(
            first.Region, cult, 6f, 5000, 5000, 10);
        fixture.DefaultPlanetFaction.SeedTargetBelief(
            second.Region, cult, 0.5f, 100, 100, 8);

        IntelEstimatePresentation presentation = IntelEstimatePresentationBuilder.BuildWorld(
            [first, second], 10);

        Assert.Equal(IntelLevel.Rumor, presentation.Level);
        Assert.Equal("Strength undisclosed", presentation.Value);
        Assert.Contains("●○○○", presentation.Confidence);
    }

    [Fact]
    public void ActiveOrderTooltip_MatchesOrdinaryAvailableMissionTooltip()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        Squad squad = AddPlayerSquad(fixture, region, "Recon Squad");
        AvailableMission available = MissionAvailability.GetAvailableMissions(region, region)
            .Single(option => option.Kind == MissionAvailabilityKind.Recon);
        Order active = new(
            [squad], true, false, Aggression.Normal,
            new Mission(MissionType.Recon, fixture.DefaultRegionFaction(0), 0));

        Assert.Equal(
            PlanetaryOperationsScreenView.BuildMissionTooltip(available),
            PlanetaryOperationsScreenView.BuildMissionTooltip(active, [available]));
    }

    [Fact]
    public void ActiveOrderTooltip_MatchesSpecialMissionTooltip()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        RegionFaction target = fixture.AddPublicCult(0, population: 2_000, organization: 100);
        Squad squad = AddPlayerSquad(fixture, region, "Ambush Squad");
        Mission special = new(MissionType.Ambush, target, missionSize: 1);
        region.SpecialMissions.Add(special);
        AvailableMission available = MissionAvailability.GetAvailableMissions(region, region)
            .Single(option => option.SpecialMission?.Id == special.Id);
        Order active = new([squad], true, false, Aggression.Normal, special);

        Assert.Equal(
            PlanetaryOperationsScreenView.BuildMissionTooltip(available),
            PlanetaryOperationsScreenView.BuildMissionTooltip(active, [available]));
    }

    [Fact]
    public void OrderMutation_LiveAggressionAndSpecialistAttachmentRoundTrip()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        Squad line = AddPlayerSquad(fixture, region, "Line Squad");
        AvailableMission recon = MissionAvailability.GetAvailableMissions(region, region)
            .Single(option => option.Kind == MissionAvailabilityKind.Recon);
        Order order = OrderMutationService.CreateOrAdd(
            fixture.Sector, region, recon, [line], -1, Aggression.Normal).Order;
        Squad pool = CreatePlayerSquad(fixture, "Apothecarion", 0,
            SquadTypes.PermitsIndividualDetachment);
        PlayerSoldier specialist = new(TestModelFactory.CreateSoldier(), "Brother Medicus");
        pool.AddSquadMember(specialist);
        GetPlayerPresence(fixture, region).LandedSquads.Add(pool);
        pool.CurrentRegion = region;
        fixture.Sector.PlayerForce.Army.PlayerSoldierMap[specialist.Id] = specialist;

        OrderMutationResult aggression = OrderMutationService.SetAggression(
            fixture.Sector, order, Aggression.Aggressive);
        OrderMutationResult attached = OrderMutationService.AttachSpecialist(
            fixture.Sector, order, specialist);
        OrderMutationResult detached = OrderMutationService.DetachSpecialist(
            fixture.Sector, order, specialist);

        Assert.True(aggression.Succeeded);
        Assert.Equal(Aggression.Aggressive, order.LevelOfAggression);
        Assert.True(attached.Succeeded);
        Assert.True(detached.Succeeded);
        Assert.Null(specialist.AttachedOrder);
        Assert.DoesNotContain(specialist, order.AttachedSoldiers);
    }

    [Fact]
    public void ForceTree_GroupsAtCompanyLevelAndKeepsExcludedReasonVisible()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Squad eligible = CreatePlayerSquad(fixture, "Eligible", 2);
        Squad excluded = CreatePlayerSquad(fixture, "Committed", 1);
        List<ForceTreeSquad> roster =
        [
            new(eligible, "Bastion"),
            new(excluded, "Bastion", Exclusion: SquadEligibilityExclusion.AssignedElsewhere)
        ];

        IReadOnlyList<HierarchyTreeItem> tree = PlanetaryForceTreeBuilder.Build(
            roster, ForceTreeGrouping.Company, "", new HashSet<int>());
        HierarchyTreeItem group = Assert.Single(tree);

        Assert.True(group.CollapsedByDefault);
        Assert.Equal(2, group.Children.Count);
        Assert.Contains("COMMITTED TO ANOTHER ORDER", group.Children
            .Single(item => item.Key == $"squad:{excluded.Id}").Badge);
        Assert.Contains("1/1", group.Children
            .Single(item => item.Key == $"squad:{excluded.Id}").Badge);
        Assert.Equal(new[] { eligible, excluded },
            PlanetaryForceTreeBuilder.ResolveSelection(roster, group.Key));
    }

    [Fact]
    public void OrdersTree_OmitsHqAdministrativeAndPersonnelPoolFormations()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        Squad line = AddPlayerSquad(fixture, region, "Line Squad");
        Squad hq = AddPlayerSquad(fixture, region, "Company HQ", squadTypes: SquadTypes.HQ);
        Squad administrative = AddPlayerSquad(
            fixture, region, "Apothecarion", squadTypes: SquadTypes.Administrative);
        Squad personnelPool = AddPlayerSquad(
            fixture, region, "Librarius", squadTypes: SquadTypes.PermitsIndividualDetachment);

        RegionalEligibilityResult eligibility = RegionalOrderEligibilityService.Build(
            fixture.Sector, region);
        List<ForceTreeSquad> roster = PlanetaryOperationsScreenController.BuildOrderTreeRoster(
            eligibility);

        Assert.Contains(roster, item => item.Squad == line);
        Assert.DoesNotContain(roster, item => item.Squad == hq);
        Assert.DoesNotContain(roster, item => item.Squad == administrative);
        Assert.DoesNotContain(roster, item => item.Squad == personnelPool);
    }

    [Fact]
    public void ForceTree_CompanyRowStaysNeutralWhenAllSquadsAreSelected()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Squad first = CreatePlayerSquad(fixture, "First", 1);
        Squad second = CreatePlayerSquad(fixture, "Second", 1);
        List<ForceTreeSquad> roster =
        [
            new(first, "Bastion"),
            new(second, "Bastion")
        ];

        HierarchyTreeItem partial = Assert.Single(PlanetaryForceTreeBuilder.Build(
            roster, ForceTreeGrouping.Company, "", new HashSet<int> { first.Id }));
        Assert.False(partial.IsSelected);

        HierarchyTreeItem complete = Assert.Single(PlanetaryForceTreeBuilder.Build(
            roster, ForceTreeGrouping.Company, "", new HashSet<int> { first.Id, second.Id }));
        Assert.False(complete.IsSelected);
        Assert.All(complete.Children, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public void ForceTree_SquadUsesUnifiedTooltipAndNoDetailChild()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Squad squad = CreatePlayerSquad(fixture, "Eligible", 2);
        squad.CurrentRegion = fixture.Planet.Regions[0];
        ForceTreeSquad entry = new(squad, squad.CurrentRegion.Name);

        HierarchyTreeItem squadItem = Assert.Single(
            Assert.Single(PlanetaryForceTreeBuilder.Build(
                [entry], ForceTreeGrouping.Company, "", new HashSet<int>())).Children);

        Assert.Empty(squadItem.Children);
        Assert.Equal(
            "Leader: None\n"
            + "Squad Size: 2/2\n"
            + "Commitment: Unassigned\n"
            + "Location: Region 0",
            squadItem.Tooltip);
    }

    [Fact]
    public void ForceTree_AssignedSquadShowsOrderLabelBesideName()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Squad squad = CreatePlayerSquad(fixture, "Assigned", 2);
        squad.CurrentRegion = fixture.Planet.Regions[0];
        _ = new Order(
            [squad],
            isQuiet: false,
            isActivelyEngaging: true,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(MissionType.Patrol, regionFaction: null, missionSize: 1));

        ForceTreeSquad entry = new(squad, squad.CurrentRegion.Name, Assigned: true);
        HierarchyTreeItem squadItem = Assert.Single(
            Assert.Single(PlanetaryForceTreeBuilder.Build(
                [entry], ForceTreeGrouping.Company, "", new HashSet<int>())).Children);

        Assert.Equal("Assigned", squadItem.Text);
        Assert.Equal("Patrol, Region 0", squadItem.Badge);
    }

    [Fact]
    public void ForceTree_AssignedElsewhereSquadShowsOrderAndRegionBesideName()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Squad squad = CreatePlayerSquad(fixture, "Committed", 2);
        squad.CurrentRegion = fixture.Planet.Regions[0];
        _ = new Order(
            [squad],
            isQuiet: false,
            isActivelyEngaging: true,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(MissionType.Recon, regionFaction: null, missionSize: 1));

        ForceTreeSquad entry = new(
            squad,
            squad.CurrentRegion.Name,
            Exclusion: SquadEligibilityExclusion.AssignedElsewhere);
        HierarchyTreeItem squadItem = Assert.Single(
            Assert.Single(PlanetaryForceTreeBuilder.Build(
                [entry], ForceTreeGrouping.Company, "", new HashSet<int>())).Children);

        Assert.Equal("Committed", squadItem.Text);
        Assert.Equal("Recon, Region 0", squadItem.Badge);
    }

    [Fact]
    public void MedicalDetach_MovesOnlyCasualtyToOrbitAndLeavesSquadLanded()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        Squad squad = AddPlayerSquad(fixture, region, "Wounded Squad", 1);
        PlayerSoldier casualty = new(TestModelFactory.CreateSoldier(), "Brother Patient");
        casualty.Body.HitLocations.First().Wounds.AddWound(WoundLevel.Minor);
        squad.AddSquadMember(casualty);
        fixture.Sector.PlayerForce.Army.PlayerSoldierMap[casualty.Id] = casualty;
        Ship ship = AddOrbitingShip(fixture, capacity: 10);

        MedicalDetachmentResult result = new MedicalDetachmentService().DetachToOrbit(
            fixture.Sector, fixture.Planet, region, ship, [casualty], new Date(1));

        Assert.True(result.Succeeded);
        Assert.Equal(IndividualPostingKind.MedicalDetachment, casualty.IndividualPosting.Kind);
        Assert.Same(ship, casualty.IndividualPosting.Location.Ship);
        Assert.Contains(casualty, ship.IndividuallyBoardedSoldiers);
        Assert.Same(region, squad.CurrentRegion);
        Assert.Contains(squad, GetPlayerPresence(fixture, region).LandedSquads);
    }

    private static Squad AddPlayerSquad(
        SectorSimulationFixture fixture,
        Region region,
        string name,
        int members = 1,
        SquadTypes squadTypes = SquadTypes.None)
    {
        Squad squad = CreatePlayerSquad(fixture, name, members, squadTypes);
        PlanetFaction planetPresence = EnsurePlayerPlanetPresence(fixture);
        if (!region.RegionFactionMap.TryGetValue(
                fixture.Sector.PlayerForce.Faction.Id, out RegionFaction presence))
        {
            presence = new RegionFaction(planetPresence, region) { IsPublic = true };
            region.RegionFactionMap[fixture.Sector.PlayerForce.Faction.Id] = presence;
        }
        presence.LandedSquads.Add(squad);
        squad.CurrentRegion = region;
        return squad;
    }

    private static Squad CreatePlayerSquad(
        SectorSimulationFixture fixture,
        string name,
        int members,
        SquadTypes squadTypes = SquadTypes.None)
    {
        SquadTemplate template = new(
            id: 900,
            name: "Operations Test Squad",
            TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(),
            TestModelFactory.TestArmor,
            TestModelFactory.SquadTemplate.Elements.ToList(),
            squadTypes)
        {
            Faction = fixture.Sector.PlayerForce.Faction
        };
        Squad squad = new(name, null, template);
        for (int index = 0; index < members; index++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(
                name: $"{name} Marine {index + 1}"));
        }
        return squad;
    }

    private static PlanetFaction EnsurePlayerPlanetPresence(
        SectorSimulationFixture fixture)
    {
        Faction player = fixture.Sector.PlayerForce.Faction;
        if (!fixture.Planet.PlanetFactionMap.TryGetValue(
                player.Id, out PlanetFaction presence))
        {
            presence = new PlanetFaction(player) { IsPublic = true };
            fixture.Planet.PlanetFactionMap[player.Id] = presence;
        }
        return presence;
    }

    private static RegionFaction GetPlayerPresence(
        SectorSimulationFixture fixture,
        Region region)
    {
        region.RegionFactionMap.TryGetValue(
            fixture.Sector.PlayerForce.Faction.Id, out RegionFaction presence);
        return presence;
    }

    private static Ship AddOrbitingShip(
        SectorSimulationFixture fixture,
        int capacity)
    {
        Ship ship = new(
            701,
            "Test Transport",
            new ShipTemplate(701, "Test Transport", (ushort)capacity, 0, 0));
        TaskForce fleet = new(
            700,
            fixture.Sector.PlayerForce.Faction,
            fixture.Planet.Position,
            fixture.Planet,
            null,
            [ship]);
        fixture.Sector.AddNewFleet(fleet);
        return ship;
    }
}
