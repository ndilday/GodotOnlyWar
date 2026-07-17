using OnlyWar.Models;
using OnlyWar.Models.Supply;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SupplyRequestLifecycleTests
{
    [Fact]
    public void ProcessTurn_AfterDeadline_FailsBeforeLateOutcomeCanFulfill()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Character governor = fixture.InstallGovernor(investigation: 0f, neediness: 0f, opinion: 1f);
        Faction threat = fixture.AddControllingFaction(1, "Threat", 100).PlanetFaction.Faction;
        ForceCommitmentPackage commitment = new(
            "deadline-test", "Deadline test", "squad", 1, 1, 1, 100);
        PresenceRequest request = new(
            1,
            fixture.Planet,
            governor,
            threat,
            new Date(1, 1, 1),
            new Date(1, 1, 2),
            commitment,
            100,
            hasPlayerResponded: true);

        // The threat disappears, but only after the agreed deadline.
        fixture.Planet.PlanetFactionMap.Remove(threat.Id);
        request.ProcessTurn(new Date(1, 1, 3));

        Assert.Equal(RequestStatus.Failed, request.Status);
        Assert.False(request.IsRequestCompleted());
        Assert.Equal(new Date(1, 1, 3), request.DateRequestResolved);
    }

    [Fact]
    public void DateTotalWeeks_RoundTripsCanonicalMillenniumBoundary()
    {
        Date boundary = new(42, 0, 1);

        Date restored = Date.FromTotalWeeks(boundary.GetTotalWeeks());

        Assert.Equal(boundary, restored);
        restored.IncrementWeek();
        Assert.Equal(new Date(42, 0, 2), restored);
    }
}
