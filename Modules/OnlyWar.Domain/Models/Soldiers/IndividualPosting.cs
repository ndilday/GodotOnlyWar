using System;
namespace OnlyWar.Domain.Soldiers
{
    public enum IndividualPostingPurpose
    {
        Independent = 0,
        Medical = 1,
        // Training by the Adeptus Mechanicus on Mars. Always off-sector; a soldier with this
        // posting is not available for any duty (DutyReadinessReasonCode.OffSector).
        Mechanicus = 2
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
        // When the posting is expected to end, or null when it is open-ended. Stored rather than
        // derived from StartedDate so that individual return dates need no save change.
        public Date ExpectedReturnDate { get; }

        public IndividualPosting(
            IndividualPostingPurpose purpose,
            CampaignLocation location,
            Date startedDate,
            Date expectedReturnDate = null)
        {
            Purpose = purpose;
            Location = location;
            StartedDate = startedDate;
            ExpectedReturnDate = expectedReturnDate;
        }

    }
}
