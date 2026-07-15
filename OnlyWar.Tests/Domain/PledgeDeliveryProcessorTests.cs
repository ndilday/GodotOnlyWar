using OnlyWar.Helpers.Supply;
using OnlyWar.Models;
using OnlyWar.Models.Supply;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class PledgeDeliveryProcessorTests
{
    [Fact]
    public void OneOff_DeliversOnDueDateAndCompletes()
    {
        Pledge pledge = OneOff(new Date(41, 1000, 4));

        PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
            pledge, new Date(41, 1000, 4), sourceIsFriendlyAndControlled: true);

        Assert.Equal(175, result.DeliveredRequisition);
        Assert.Equal(PledgeStatus.Completed, result.Pledge.Status);
    }

    [Fact]
    public void OneOff_DoesNotDeliverBeforeDueDate()
    {
        Pledge pledge = OneOff(new Date(41, 1000, 4));

        PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
            pledge, new Date(41, 1000, 3), sourceIsFriendlyAndControlled: true);

        Assert.Equal(0, result.DeliveredRequisition);
        Assert.Equal(PledgeStatus.Active, result.Pledge.Status);
    }

    [Fact]
    public void UndeliveredOneOff_DefaultsWhenSourceIsLost()
    {
        Pledge pledge = OneOff(new Date(41, 1000, 4));

        PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
            pledge, new Date(41, 1000, 2), sourceIsFriendlyAndControlled: false);

        Assert.Equal(0, result.DeliveredRequisition);
        Assert.Equal(PledgeStatus.Defaulted, result.Pledge.Status);
    }

    [Fact]
    public void StandingPledge_SuspendsAndRestartsCadenceWhenSourceReturns()
    {
        Pledge pledge = Standing(new Date(41, 1000, 4), cadenceWeeks: 4);
        PledgeDeliveryResult suspended = PledgeDeliveryProcessor.Process(
            pledge, new Date(41, 1000, 3), sourceIsFriendlyAndControlled: false);

        PledgeDeliveryResult resumed = PledgeDeliveryProcessor.Process(
            suspended.Pledge, new Date(41, 1000, 8), sourceIsFriendlyAndControlled: true);

        Assert.Equal(PledgeStatus.Suspended, suspended.Pledge.Status);
        Assert.Equal(PledgeStatus.Active, resumed.Pledge.Status);
        Assert.Equal(new Date(41, 1000, 12), resumed.Pledge.NextDeliveryDate);
        Assert.Equal(0, resumed.DeliveredRequisition);
    }

    [Fact]
    public void StandingPledge_DeliversOnceAndSchedulesFromProcessingDate()
    {
        Pledge pledge = Standing(new Date(41, 999, 52), cadenceWeeks: 4);

        PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
            pledge, new Date(42, 0, 1), sourceIsFriendlyAndControlled: true);

        Assert.Equal(175, result.DeliveredRequisition);
        Assert.Equal(PledgeStatus.Active, result.Pledge.Status);
        Assert.Equal(new Date(42, 0, 5), result.Pledge.NextDeliveryDate);
    }

    [Fact]
    public void GovernorSuccessionDoesNotAffectInstitutionalPledge()
    {
        Pledge pledge = Standing(new Date(41, 1000, 4), cadenceWeeks: 4);
        Assert.Equal(29, pledge.GrantingAuthorityId);

        // Processing intentionally accepts no current-governor identity.
        PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
            pledge, new Date(41, 1000, 4), sourceIsFriendlyAndControlled: true);

        Assert.Equal(175, result.DeliveredRequisition);
        Assert.Equal(PledgeStatus.Active, result.Pledge.Status);
    }

    [Fact]
    public void DatesAreSnapshottedAndCannotBeMutatedThroughProperty()
    {
        Date dueDate = new(41, 1000, 4);
        Pledge pledge = OneOff(dueDate);
        dueDate.IncrementWeek();
        Date exposedDate = pledge.NextDeliveryDate;
        exposedDate.IncrementWeek();

        Assert.Equal(new Date(41, 1000, 4), pledge.NextDeliveryDate);
    }

    private static Pledge OneOff(Date dueDate) =>
        new(7, 13, 29, PledgePayload.Requisition(175),
            PledgeScheduleKind.OneOff, dueDate);

    private static Pledge Standing(Date dueDate, int cadenceWeeks) =>
        new(7, 13, 29, PledgePayload.Requisition(175),
            PledgeScheduleKind.Standing, dueDate, cadenceWeeks);
}
