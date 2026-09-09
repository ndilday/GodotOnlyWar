using System;
namespace OnlyWar.Domain.Soldiers
{
    public enum IndividualPostingPurpose
    {
        Independent = 0,
        Medical = 1
    }

    /// <summary>
    /// The save-owned physical commitment of a soldier who is away from his organizational
    /// home squad. Mutation belongs to IndividualPostingService.
    /// </summary>
    public sealed class IndividualPosting
    {
        public IndividualPostingPurpose Purpose { get; set; }
        public CampaignLocation Location { get; set; }
        public Date StartedDate { get; }

        public IndividualPosting(
            IndividualPostingPurpose purpose,
            CampaignLocation location,
            Date startedDate)
        {
            Purpose = purpose;
            Location = location;
            StartedDate = startedDate;
        }

    }
}
