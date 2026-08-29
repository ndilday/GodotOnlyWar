using System;
using OnlyWar.Models.Orders;

namespace OnlyWar.Models.Soldiers
{
    public enum IndividualPostingPurpose
    {
        Independent = 0,
        Medical = 1
    }

    // Format-13 compatibility vocabulary. Operational order membership is no longer encoded in
    // physical posting state; new code uses IndividualPostingPurpose plus PlayerSoldier.CurrentOrder.
    public enum IndividualPostingKind
    {
        OperationalAttachment = 0,
        IndependentDeployment = 1,
        MedicalDetachment = 2,
        AwaitingReunion = 3
    }

    /// <summary>
    /// The save-owned physical commitment of a soldier who is away from his organizational
    /// home squad. Mutation belongs to IndividualPostingService.
    /// </summary>
    public sealed class IndividualPosting
    {
        public IndividualPostingPurpose Purpose { get; internal set; }
        public CampaignLocation Location { get; internal set; }
        public Date StartedDate { get; }

        // Compatibility projection for the retired format-13 shape. It is not a source of truth
        // and is never written by the format-14 persistence path.
        private bool _awaitingReunion;
        [Obsolete("Use Purpose and PlayerSoldier.CurrentOrder.")]
        public IndividualPostingKind Kind
        {
            get
            {
                if (_awaitingReunion) return IndividualPostingKind.AwaitingReunion;
                if (Order != null) return IndividualPostingKind.OperationalAttachment;
                return Purpose == IndividualPostingPurpose.Medical
                    ? IndividualPostingKind.MedicalDetachment
                    : IndividualPostingKind.IndependentDeployment;
            }
            internal set
            {
                _awaitingReunion = value == IndividualPostingKind.AwaitingReunion;
                Purpose = value == IndividualPostingKind.MedicalDetachment
                    ? IndividualPostingPurpose.Medical
                    : IndividualPostingPurpose.Independent;
            }
        }

        [Obsolete("Order membership belongs to PlayerSoldier.CurrentOrder.")]
        public Order Order { get; internal set; }

        internal IndividualPosting(
            IndividualPostingPurpose purpose,
            CampaignLocation location,
            Date startedDate)
        {
            Purpose = purpose;
            Location = location;
            StartedDate = startedDate;
        }

        internal IndividualPosting(
            IndividualPostingKind kind,
            CampaignLocation location,
            Date startedDate,
            Order order = null)
        {
            Purpose = kind == IndividualPostingKind.MedicalDetachment
                ? IndividualPostingPurpose.Medical
                : IndividualPostingPurpose.Independent;
            _awaitingReunion = kind == IndividualPostingKind.AwaitingReunion;
            Location = location;
            StartedDate = startedDate;
            Order = order;
        }
    }
}
