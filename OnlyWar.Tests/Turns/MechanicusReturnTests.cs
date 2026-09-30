using System.Linq;
using OnlyWar.Application;
using OnlyWar.Campaign;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Operations.Orders;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

/// <summary>
/// Mars pipeline (TDD §6.14): brothers come home from Mars on their
/// expected return date, to the standing override, else the Home World's capital, else the
/// flagship, else the Armory's duty station; and the Mechanicus loan ends with the first cohort.
/// Postings are created with past start dates rather than simulating twenty years.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MechanicusReturnTests
{
    private static readonly Date FoundingDeparture = Date.FromTotalWeeks(1000);
    private static readonly Date FoundingReturn = MechanicusDepartureService.ExpectedReturnDate(FoundingDeparture);

    private readonly SectorSimulationFixture _fixture = SectorSimulationFixture.Create();
    private readonly Squad _armory;
    private readonly Ship _flagship;

    public MechanicusReturnTests()
    {
        _armory = new Squad("Armory", null, Doctrine.Armory);
        TaskForce taskForce = new(Force.Faction) { Planet = _fixture.Planet, Position = _fixture.Planet.Position };
        _flagship = new Ship(1, "Emperor's Wrath", new ShipTemplate(1, "Strike Cruiser", 200, 0, 0))
        {
            IsFlagship = true
        };
        taskForce.Ships.Add(_flagship);
        _flagship.Fleet = taskForce;
        Force.Fleet.TaskForces.Add(taskForce);
        // Away from every default destination unless a test says otherwise.
        _armory.DutyStation = CampaignLocation.Landed(_fixture.Planet.Regions[9]);
        Force.IsMechanicusLoanActive = true;
    }

    private PlayerForce Force => _fixture.Sector.PlayerForce;
    private ChapterGenerationDoctrine Doctrine => _fixture.Rules.ChapterDoctrine;

    [Fact]
    public void BeforeTheReturnDate_NobodyComesHome()
    {
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        MechanicusReturnReport report = Returns().ProcessReturns(
            _fixture.Sector, Date.FromTotalWeeks(FoundingReturn.GetTotalWeeks() - 1));

        Assert.Null(report);
        Assert.True(MechanicusTrainingService.IsOnMars(adept));
        Assert.True(Force.IsMechanicusLoanActive);
    }

    [Fact]
    public void WithAHomeWorld_TheReturneeReportsToItsCapital()
    {
        OnlyWar.Campaign.Turns.ChapterHomeworldService.ReplaceChapterPlanetFaction(_fixture.Planet, Force.Faction);
        Force.HomeWorldPlanetId = _fixture.Planet.Id;
        Region capital = OnlyWar.Domain.Extensions.PlanetExtensions.GetCapitalRegion(_fixture.Planet);
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        MechanicusReturnReport report = Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        AssertPostedAt(adept, CampaignLocation.Landed(capital));
        MechanicusReturnEntry entry = Assert.Single(report.Returns);
        Assert.Contains(capital.Name, entry.Destination);
        Assert.Contains(adept.SoldierEvents, e => e.Detail.StartsWith("returned from Mars"));
    }

    [Fact]
    public void AHomeWorldTheChapterNoLongerHolds_IsPassedOverForTheFlagship()
    {
        Force.HomeWorldPlanetId = _fixture.Planet.Id; // still held by the Imperium, not the chapter
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        AssertPostedAt(adept, CampaignLocation.Aboard(_flagship));
    }

    [Fact]
    public void WithNoHomeWorld_TheReturneeReportsToTheFlagship()
    {
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        MechanicusReturnReport report = Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        AssertPostedAt(adept, CampaignLocation.Aboard(_flagship));
        Assert.Contains(adept, _flagship.IndividuallyBoardedSoldiers);
        Assert.Equal($"the {_flagship.Name}", Assert.Single(report.Returns).Destination);
    }

    [Fact]
    public void TheStandingOverride_WinsOverTheDefault()
    {
        Region chosen = _fixture.Planet.Regions[3];
        Force.MarsReturnDestination = CampaignLocation.Landed(chosen);
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        AssertPostedAt(adept, CampaignLocation.Landed(chosen));
    }

    [Fact]
    public void AnOverrideThatNoLongerExists_SendsHimToTheArmorysDutyStation()
    {
        Ship lost = new(2, "Lost Vessel", new ShipTemplate(1, "Strike Cruiser", 200, 0, 0));
        Force.MarsReturnDestination = CampaignLocation.Aboard(lost);
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        MechanicusReturnReport report = Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        AssertAtDutyStation(adept);
        Assert.Contains(_fixture.Planet.Regions[9].Name, Assert.Single(report.Returns).Destination);
    }

    [Fact]
    public void ADestinationThatIsTheDutyStation_ClearsThePosting()
    {
        _armory.DutyStation = CampaignLocation.Aboard(_flagship);
        PlayerSoldier adept = SendToMars(FoundingDeparture);

        Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        AssertAtDutyStation(adept);
    }

    // The loan ends when the founding cohort is home, not when an earlier or partial return
    // happens, and a later departure still on Mars does not hold it open.
    [Fact]
    public void TheLoanEndsWithTheFoundingCohort_AndNotBefore()
    {
        PlayerSoldier first = SendToMars(FoundingDeparture);
        PlayerSoldier second = SendToMars(FoundingDeparture);
        PlayerSoldier later = SendToMars(Date.FromTotalWeeks(FoundingDeparture.GetTotalWeeks() + 52));

        Assert.Null(Returns().ProcessReturns(
            _fixture.Sector, Date.FromTotalWeeks(FoundingReturn.GetTotalWeeks() - 1)));
        Assert.True(Force.IsMechanicusLoanActive);

        MechanicusReturnReport report = Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        Assert.True(report.LoanEnded);
        Assert.False(Force.IsMechanicusLoanActive);
        Assert.Equal(new[] { first.Id, second.Id }, report.Returns.Select(r => r.SoldierId).OrderBy(id => id));
        Assert.True(MechanicusTrainingService.IsOnMars(later));
        Assert.False(TechmarineSupport.IsOnLoan(Force));
    }

    // With the founding cohort split across two departure dates, the loan waits for the last.
    [Fact]
    public void TheLoanWaitsForEveryoneWhoLeftNoLaterThanTheReturningGroup()
    {
        SendToMars(FoundingDeparture);
        PlayerSoldier earlier = SendToMars(Date.FromTotalWeeks(FoundingDeparture.GetTotalWeeks() - 1));
        // An individual return date, later than the static period: he left first but is not home.
        earlier.IndividualPosting = new IndividualPosting(
            IndividualPostingPurpose.Mechanicus,
            CampaignLocation.OffSector,
            earlier.IndividualPosting.StartedDate,
            Date.FromTotalWeeks(FoundingReturn.GetTotalWeeks() + 10));

        MechanicusReturnReport report = Returns().ProcessReturns(_fixture.Sector, FoundingReturn);

        Assert.Single(report.Returns);
        Assert.False(report.LoanEnded);
        Assert.True(Force.IsMechanicusLoanActive);
    }

    [Fact]
    public void ProcessTurn_BringsHimHome_AndTheReportSaysSo()
    {
        PlayerSoldier adept = SendToMars(
            FoundingDeparture,
            returnDate: Date.FromTotalWeeks(_fixture.CurrentDate.GetTotalWeeks() + 1));

        TurnResolutionResult result = _fixture.ProcessTurn();

        Assert.False(MechanicusTrainingService.IsOnMars(adept));
        Assert.True(result.MechanicusReturns.LoanEnded);
        Assert.False(Force.IsMechanicusLoanActive);
        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            Date.FromTotalWeeks(_fixture.CurrentDate.GetTotalWeeks() + 1), result);
        Assert.Contains(build.Snapshot.Entries, entry =>
            entry.Title == "Returned from Mars" && entry.Summary.Contains(adept.Name));
        Assert.Contains(build.Snapshot.Entries, entry => entry.Title == "Mechanicus Loan Ended");
    }

    private PlayerSoldier SendToMars(Date departure, Date returnDate = null)
    {
        string name = $"Brother Adept {Force.Army.PlayerSoldierMap.Count + 1}";
        PlayerSoldier adept = new(TestModelFactory.CreateSoldier(Doctrine.Techmarine, name), name);
        _armory.AddSquadMember(adept);
        Force.Army.PlayerSoldierMap[adept.Id] = adept;
        new IndividualPostingService(new OrderCommitmentSurface()).Create(
            adept,
            IndividualPostingPurpose.Mechanicus,
            CampaignLocation.OffSector,
            departure,
            returnDate ?? MechanicusDepartureService.ExpectedReturnDate(departure));
        return adept;
    }

    private static MechanicusReturnService Returns() => new(new OrderCommitmentSurface());

    private void AssertPostedAt(PlayerSoldier soldier, CampaignLocation expected)
    {
        Assert.False(MechanicusTrainingService.IsOnMars(soldier));
        Assert.Same(_armory, soldier.AssignedSquad);
        Assert.Equal(IndividualPostingPurpose.Independent, soldier.IndividualPosting.Purpose);
        Assert.True(soldier.IndividualPosting.Location.IsSamePlace(expected));
        Assert.True(CampaignLocationService.ForSoldier(soldier).IsSamePlace(expected));
    }

    private void AssertAtDutyStation(PlayerSoldier soldier)
    {
        Assert.Null(soldier.IndividualPosting);
        Assert.Same(_armory, soldier.AssignedSquad);
        Assert.False(MechanicusTrainingService.IsOnMars(soldier));
    }
}
