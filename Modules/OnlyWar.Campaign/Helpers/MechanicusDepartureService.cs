using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Medical.Readiness;
using OnlyWar.Operations.Abstractions;
using System;
using System.Collections.Generic;

namespace OnlyWar.Campaign
{
    public enum MechanicusDepartureReasonCode
    {
        None = 0,
        MissingSoldier,
        AlreadyPosted,
        NotLineBrother,
        ScoutMarine,
        BelowTechmarineRequirement,
        CommittedToOperation,
        NotFit,
        InWarp
    }

    public sealed record MechanicusDepartureEvaluation(
        bool IsAllowed,
        MechanicusDepartureReasonCode ReasonCode,
        string Reason)
    {
        public static MechanicusDepartureEvaluation Allowed { get; } =
            new(true, MechanicusDepartureReasonCode.None, null);
    }

    /// <summary>
    /// Sends a Battle Brother to Mars to be trained as a Techmarine by the Adeptus Mechanicus
    /// (TDD §6.14, "Departure"). The one operation for both chapter founding
    /// and the Armory screen: he moves to the Armory, takes the Techmarine template now rather
    /// than on return, and is posted off-sector until his expected return date.
    ///
    /// Eligibility is the general career-track rule (a line brother,
    /// <see cref="SoldierTransferService.IsLineBrother"/>) with Scout Marines excluded, plus the
    /// Techmarine template requirement, fitness, and no operational commitment. At founding only
    /// the template requirement and the posting check apply: founders hold no line template yet,
    /// and nothing at founding can be unfit or committed.
    /// </summary>
    public sealed class MechanicusDepartureService
    {
        // Static 20-year period from departure. Everyone sent together returns together.
        public const int TrainingWeeks = 1040;

        private readonly IndividualPostingService _postings;
        private readonly SoldierTemplateEligibilityService _eligibility = new();
        private readonly SoldierTemplate _techmarine;
        private readonly SoldierTemplate _scoutMarine;

        public MechanicusDepartureService(
            IOrderCommitmentSurface commitments,
            SoldierTemplate techmarineTemplate,
            SoldierTemplate scoutMarineTemplate)
        {
            _postings = new IndividualPostingService(
                commitments ?? throw new ArgumentNullException(nameof(commitments)));
            _techmarine = techmarineTemplate
                ?? throw new ArgumentNullException(nameof(techmarineTemplate));
            _scoutMarine = scoutMarineTemplate;
        }

        // FromTotalWeeks, not new Date(int): that constructor takes a zero-based offset and
        // would put the return one week late.
        public static Date ExpectedReturnDate(Date departureDate) =>
            Date.FromTotalWeeks(departureDate.GetTotalWeeks() + TrainingWeeks);

        public MechanicusDepartureEvaluation Evaluate(
            PlayerSoldier soldier,
            RecruitmentProgram recruitmentProgram = null,
            bool atFounding = false)
        {
            if (soldier == null)
            {
                return Reject(MechanicusDepartureReasonCode.MissingSoldier, "No soldier selected.");
            }
            if (soldier.IndividualPosting != null)
            {
                return Reject(MechanicusDepartureReasonCode.AlreadyPosted,
                    $"{soldier.Name} is already posted away from his formation.");
            }
            if (!atFounding)
            {
                if (!SoldierTransferService.IsLineBrother(soldier.Template))
                {
                    return Reject(MechanicusDepartureReasonCode.NotLineBrother,
                        $"Only line Battle Brothers can be sent to Mars; {soldier.Name} is a "
                        + $"{soldier.Template?.Name}.");
                }
                if (_scoutMarine != null && soldier.Template.Id == _scoutMarine.Id)
                {
                    return Reject(MechanicusDepartureReasonCode.ScoutMarine,
                        $"{soldier.Name} is a Scout and not yet a full Battle Brother.");
                }
            }
            if (!_eligibility.IsEligible(soldier, _techmarine))
            {
                return Reject(MechanicusDepartureReasonCode.BelowTechmarineRequirement,
                    $"{soldier.Name} does not meet the {_techmarine.Name} requirements.");
            }
            if (atFounding)
            {
                return MechanicusDepartureEvaluation.Allowed;
            }
            if (soldier.CurrentOrder != null || soldier.AssignedSquad?.CurrentOrders != null)
            {
                return Reject(MechanicusDepartureReasonCode.CommittedToOperation,
                    $"{soldier.Name} is committed to an operation.");
            }
            DutyReadinessEvaluation duty = DutyReadinessService.Evaluate(
                soldier, recruitmentProgram: recruitmentProgram);
            if (!duty.IsDutyReady)
            {
                return Reject(MechanicusDepartureReasonCode.NotFit,
                    duty.Reason ?? $"{soldier.Name} is not fit to travel.");
            }
            if (CampaignLocationService.ForSoldier(soldier)?.Ship?.Fleet?.TravelPhase
                == FleetTravelPhase.InWarp)
            {
                return Reject(MechanicusDepartureReasonCode.InWarp,
                    $"{soldier.Name} is aboard a fleet in the Warp.");
            }
            return MechanicusDepartureEvaluation.Allowed;
        }

        public IndividualPosting Depart(
            PlayerSoldier soldier,
            Squad armory,
            Date departureDate,
            RecruitmentProgram recruitmentProgram = null,
            bool atFounding = false,
            IDictionary<int, Squad> squadMap = null)
        {
            ArgumentNullException.ThrowIfNull(armory);
            ArgumentNullException.ThrowIfNull(departureDate);
            MechanicusDepartureEvaluation evaluation =
                Evaluate(soldier, recruitmentProgram, atFounding);
            if (!evaluation.IsAllowed)
            {
                throw new InvalidOperationException(evaluation.Reason);
            }

            Squad previous = soldier.AssignedSquad;
            if (!ReferenceEquals(previous, armory))
            {
                previous?.RemoveSquadMember(soldier);
                armory.AddSquadMember(soldier);
                if (previous != null && previous.Members.Count == 0)
                {
                    new SquadLifecycleService(squadMap: squadMap).HandleEmptySquad(previous);
                }
            }
            soldier.Template = _techmarine;
            Date returnDate = ExpectedReturnDate(departureDate);
            IndividualPosting posting = _postings.Create(
                soldier,
                IndividualPostingPurpose.Mechanicus,
                CampaignLocation.OffSector,
                departureDate,
                returnDate);
            soldier.AddEvent(new SoldierEvent(departureDate, SoldierEventType.Transfer,
                $"sent to Mars for training by the Adeptus Mechanicus, expected to return {returnDate}"));
            return posting;
        }

        private static MechanicusDepartureEvaluation Reject(
            MechanicusDepartureReasonCode code, string reason) => new(false, code, reason);
    }
}
