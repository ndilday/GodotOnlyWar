using System;
using OnlyWar.Models;
using OnlyWar.Models.Supply;

namespace OnlyWar.Helpers.Supply
{
    public readonly struct PledgeDeliveryResult
    {
        public Pledge Pledge { get; }
        public int DeliveredRequisition { get; }

        public PledgeDeliveryResult(Pledge pledge, int deliveredRequisition)
        {
            Pledge = pledge ?? throw new ArgumentNullException(nameof(pledge));
            DeliveredRequisition = deliveredRequisition;
        }
    }

    /// <summary>
    /// Advances a single pledge without accessing or mutating campaign global state.
    /// The caller supplies whether the source remains friendly and controlled, applies
    /// any returned Requisition, and replaces its stored pledge with the returned value.
    /// </summary>
    public static class PledgeDeliveryProcessor
    {
        public static PledgeDeliveryResult Process(
            Pledge pledge,
            Date currentDate,
            bool sourceIsFriendlyAndControlled)
        {
            if (pledge == null) throw new ArgumentNullException(nameof(pledge));
            if (currentDate == null) throw new ArgumentNullException(nameof(currentDate));

            if (pledge.Status is PledgeStatus.Completed or PledgeStatus.Defaulted)
            {
                return new PledgeDeliveryResult(pledge, 0);
            }

            if (!sourceIsFriendlyAndControlled)
            {
                PledgeStatus unavailableStatus = pledge.ScheduleKind == PledgeScheduleKind.OneOff
                    ? PledgeStatus.Defaulted
                    : PledgeStatus.Suspended;
                return new PledgeDeliveryResult(pledge.With(unavailableStatus), 0);
            }

            if (pledge.Status == PledgeStatus.Suspended)
            {
                // Missed standing deliveries are not accumulated. Restoration restarts the
                // cadence from the date the source becomes available again.
                Date resumedDelivery = PledgeDates.AddWeeks(currentDate, pledge.CadenceWeeks);
                return new PledgeDeliveryResult(pledge.With(PledgeStatus.Active, resumedDelivery), 0);
            }

            if (!currentDate.IsAfterOrEqual(pledge.NextDeliveryDate))
            {
                return new PledgeDeliveryResult(pledge, 0);
            }

            int delivered = pledge.Payload.Kind switch
            {
                PledgePayloadKind.Requisition => pledge.Payload.Amount,
                _ => throw new InvalidOperationException($"Unsupported pledge payload {pledge.Payload.Kind}.")
            };

            if (pledge.ScheduleKind == PledgeScheduleKind.OneOff)
            {
                return new PledgeDeliveryResult(pledge.With(PledgeStatus.Completed), delivered);
            }

            Date nextDelivery = PledgeDates.AddWeeks(currentDate, pledge.CadenceWeeks);
            return new PledgeDeliveryResult(pledge.With(PledgeStatus.Active, nextDelivery), delivered);
        }
    }
}
