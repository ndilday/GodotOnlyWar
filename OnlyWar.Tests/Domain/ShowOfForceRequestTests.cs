using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Supply;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Covers how an effort-based (ForceCommitment) governor request accrues progress. This rule
/// previously had no test at all: both existing lifecycle tests supply a threat faction and so
/// only exercise the outcome-based path. The untested rule was also wrong for the player - it
/// counted squads with NO orders anywhere on the planet, which the UI could not express and which
/// the end-of-turn preflight actively told players to stop doing (issue #3).
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ShowOfForceRequestTests
{
    [Fact]
    public void ProcessTurn_ShowOfForceInCapitalRegion_AccruesProgress()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Character governor = fixture.InstallGovernor(investigation: 0f, neediness: 0f, opinion: 1f);
        PresenceRequest request = CreateEffortRequest(fixture, governor);
        LandSquadWithOrder(fixture, CapitalRegion(fixture), MissionType.ShowOfForce);

        request.ProcessTurn(new Date(1, 1, 2));

        Assert.True(request.ProgressBattleValueTime > 0);
        Assert.Equal(RequestStatus.InProgress, request.Status);
    }

    [Fact]
    public void ProcessTurn_SquadWithNoOrders_AccruesNothing()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Character governor = fixture.InstallGovernor(investigation: 0f, neediness: 0f, opinion: 1f);
        PresenceRequest request = CreateEffortRequest(fixture, governor);
        // The rule this replaced would have counted exactly this squad.
        LandSquadWithOrder(fixture, CapitalRegion(fixture), missionType: null);

        request.ProcessTurn(new Date(1, 1, 2));

        Assert.Equal(0, request.ProgressBattleValueTime);
        Assert.Equal(RequestStatus.Open, request.Status);
    }

    [Fact]
    public void ProcessTurn_ShowOfForceOutsideCapitalRegion_AccruesNothing()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Character governor = fixture.InstallGovernor(investigation: 0f, neediness: 0f, opinion: 1f);
        PresenceRequest request = CreateEffortRequest(fixture, governor);
        Region elsewhere = fixture.Planet.Regions[
            CapitalRegion(fixture) == fixture.Planet.Regions[0] ? 1 : 0];
        LandSquadWithOrder(fixture, elsewhere, MissionType.ShowOfForce);

        request.ProcessTurn(new Date(1, 1, 2));

        Assert.Equal(0, request.ProgressBattleValueTime);
    }

    [Fact]
    public void ProcessTurn_SustainedShowOfForce_EventuallyFulfillsRequest()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Character governor = fixture.InstallGovernor(investigation: 0f, neediness: 0f, opinion: 1f);
        PresenceRequest request = CreateEffortRequest(fixture, governor, serviceWeeks: 3);
        LandSquadWithOrder(fixture, CapitalRegion(fixture), MissionType.ShowOfForce);

        request.ProcessTurn(new Date(1, 1, 2));
        Assert.Equal(RequestStatus.InProgress, request.Status);
        request.ProcessTurn(new Date(1, 1, 3));
        Assert.Equal(RequestStatus.InProgress, request.Status);
        request.ProcessTurn(new Date(1, 1, 4));

        Assert.Equal(RequestStatus.Fulfilled, request.Status);
        Assert.True(request.IsRequestCompleted());
    }

    private static Region CapitalRegion(SectorSimulationFixture fixture) =>
        System.Array.Find(fixture.Planet.Regions, r => r.Id == fixture.Planet.CapitalRegionId)
        ?? fixture.Planet.Regions[0];

    // A false alarm - no threat faction - is what makes this a ForceCommitment request.
    private static PresenceRequest CreateEffortRequest(
        SectorSimulationFixture fixture, Character governor, int serviceWeeks = 4)
    {
        // Weekly contribution is capped at ReferenceBattleValuePerPackage *
        // MaximumEffectivePackageCount, so with both at 1 a week of presence is worth exactly one
        // squad-week regardless of how strong the squad actually is. That keeps these assertions
        // about the qualifying rule rather than about battle-value arithmetic.
        ForceCommitmentPackage commitment = new(
            "show-of-force-test", "Astartes presence", "squad",
            packageCount: 1, serviceWeeks: serviceWeeks, completionDeadlineWeeks: 8,
            referenceBattleValuePerPackage: 1);
        PresenceRequest request = new(
            1,
            fixture.Planet,
            governor,
            threatFaction: null,
            new Date(1, 1, 1),
            new Date(1, 1, 9),
            commitment,
            offeredRequisition: 100);
        governor.ActiveRequest = request;
        Assert.Equal(RequestFulfillmentKind.ForceCommitment, request.FulfillmentKind);
        return request;
    }

    // Keyed on the RULES player faction, which is what PresenceRequest looks up - not the
    // fixture's own local player faction object.
    private static Squad LandSquadWithOrder(
        SectorSimulationFixture fixture, Region region, MissionType? missionType)
    {
        Faction playerFaction = GameDataSingleton.Instance.GameRulesData.PlayerFaction;
        if (!fixture.Planet.PlanetFactionMap.TryGetValue(
            playerFaction.Id, out PlanetFaction playerPlanetFaction))
        {
            playerPlanetFaction = new PlanetFaction(playerFaction) { IsPublic = true };
            fixture.Planet.PlanetFactionMap[playerFaction.Id] = playerPlanetFaction;
        }
        if (!region.RegionFactionMap.TryGetValue(
            playerFaction.Id, out RegionFaction playerRegionFaction))
        {
            playerRegionFaction = new RegionFaction(playerPlanetFaction, region) { IsPublic = true };
            region.RegionFactionMap[playerFaction.Id] = playerRegionFaction;
        }

        Squad squad = new("Test Squad", null, TestModelFactory.SquadTemplate)
        {
            CurrentRegion = region
        };
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: "Test Marine"));
        playerRegionFaction.LandedSquads.Add(squad);

        if (missionType.HasValue)
        {
            Mission mission = new(missionType.Value, playerRegionFaction, 1);
            _ = new Order([squad], Disposition.Mobile, true, false, Aggression.Normal, mission);
        }
        return squad;
    }
}
