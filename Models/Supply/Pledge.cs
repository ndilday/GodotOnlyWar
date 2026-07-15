using System;

namespace OnlyWar.Models.Supply
{
    public enum PledgePayloadKind
    {
        Requisition = 0
    }

    public enum PledgeScheduleKind
    {
        OneOff = 0,
        Standing = 1
    }

    public enum PledgeStatus
    {
        Active = 0,
        Suspended = 1,
        Completed = 2,
        Defaulted = 3
    }

    public sealed class PledgePayload
    {
        public PledgePayloadKind Kind { get; }
        public int Amount { get; }

        public PledgePayload(PledgePayloadKind kind, int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "A pledge payload must be positive.");
            }

            Kind = kind;
            Amount = amount;
        }

        public static PledgePayload Requisition(int amount) =>
            new(PledgePayloadKind.Requisition, amount);
    }

    /// <summary>
    /// A snapshotted institutional promise. GrantingAuthorityId identifies who made the
    /// promise for history and presentation; pledge validity is deliberately not tied to
    /// that character continuing to govern the source world.
    /// </summary>
    public sealed class Pledge
    {
        private readonly Date _nextDeliveryDate;

        public int Id { get; }
        public int SourcePlanetId { get; }
        public int GrantingAuthorityId { get; }
        public PledgePayload Payload { get; }
        public PledgeScheduleKind ScheduleKind { get; }
        public int CadenceWeeks { get; }
        public PledgeStatus Status { get; }
        public Date NextDeliveryDate => PledgeDates.Copy(_nextDeliveryDate);

        public Pledge(
            int id,
            int sourcePlanetId,
            int grantingAuthorityId,
            PledgePayload payload,
            PledgeScheduleKind scheduleKind,
            Date nextDeliveryDate,
            int cadenceWeeks = 0,
            PledgeStatus status = PledgeStatus.Active)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (sourcePlanetId < 0) throw new ArgumentOutOfRangeException(nameof(sourcePlanetId));
            if (grantingAuthorityId < 0) throw new ArgumentOutOfRangeException(nameof(grantingAuthorityId));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            _nextDeliveryDate = PledgeDates.Copy(nextDeliveryDate ??
                throw new ArgumentNullException(nameof(nextDeliveryDate)));

            if (scheduleKind == PledgeScheduleKind.Standing && cadenceWeeks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cadenceWeeks),
                    "A standing pledge must have a positive cadence.");
            }

            if (scheduleKind == PledgeScheduleKind.OneOff && cadenceWeeks != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cadenceWeeks),
                    "A one-off pledge cannot have a cadence.");
            }

            Id = id;
            SourcePlanetId = sourcePlanetId;
            GrantingAuthorityId = grantingAuthorityId;
            ScheduleKind = scheduleKind;
            CadenceWeeks = cadenceWeeks;
            Status = status;
        }

        internal Pledge With(PledgeStatus status, Date nextDeliveryDate = null) =>
            new(Id, SourcePlanetId, GrantingAuthorityId, Payload, ScheduleKind,
                nextDeliveryDate ?? _nextDeliveryDate, CadenceWeeks, status);
    }

    internal static class PledgeDates
    {
        internal static Date Copy(Date date) => new(date.Millenium, date.Year, date.Week);

        internal static Date AddWeeks(Date date, int weeks)
        {
            if (weeks < 0) throw new ArgumentOutOfRangeException(nameof(weeks));
            Date result = Copy(date);
            for (int week = 0; week < weeks; week++) result.IncrementWeek();
            return result;
        }
    }
}
