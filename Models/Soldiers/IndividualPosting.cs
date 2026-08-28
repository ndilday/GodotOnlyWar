using OnlyWar.Models.Orders;

namespace OnlyWar.Models.Soldiers
{
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
        public IndividualPostingKind Kind { get; internal set; }
        public Order Order { get; internal set; }
        public CampaignLocation Location { get; internal set; }
        public Date StartedDate { get; }

        internal IndividualPosting(
            IndividualPostingKind kind,
            CampaignLocation location,
            Date startedDate,
            Order order = null)
        {
            Kind = kind;
            Location = location;
            StartedDate = startedDate;
            Order = order;
        }
    }
}
