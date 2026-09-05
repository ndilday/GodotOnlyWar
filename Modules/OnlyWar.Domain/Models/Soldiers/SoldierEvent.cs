using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    // The kinds of events recorded in a soldier's structured history log. One value
    // exists per site that currently writes soldier history; the reserved values are
    // emitted by later narrative passes (PRD 0.8 steps 2-4) and are listed here so the
    // enum and save schema stay stable as those events come online.
    public enum SoldierEventType
    {
        Founding = 0,
        AcceptedToTraining = 1,
        PsychicDetected = 2,
        Promotion = 3,
        Transfer = 4,
        RatingFlag = 5,
        AwardReceived = 6,
        BattleParticipation = 7,
        Death = 8,
        // Whether a fallen brother's gene-seed was recovered, destroyed, or immature
        // (PRD 4.8 / 4.12). Recorded at roster removal, distinct from the Death note.
        GeneseedRecovery = 9,
        // Taken out of a battle alive -- motive capability gone, a weapon hand ruined, or a vital
        // location crippled short of severed -- and recovered from the field
        // (Design/Reference/CasualtyRealism.md §2.3). Distinct from Death: he is still on the roster.
        Incapacitated = 10,

        // Reserved for planned notable career events:
        FirstBlood = 100,
        KillMilestone = 101,
        LastSurvivor = 102,
        MentorAssigned = 103,
        Oath = 104,
        NearDeathRecovery = 105,

        // Recorded when a player-run non-battle mission finishes.
        MissionOutcome = 106,
        SquadHeldAgainstOdds = 107,
        BodyPartReplacement = 108
    }

    // A single, queryable entry in a soldier's history. Date, type, faction, weapon,
    // magnitude, location, and related soldiers support event-based career queries, while
    // Detail supplies the authored one-line summary shown in the service record.
    public class SoldierEvent
    {
        private readonly List<int> _relatedSoldierIds;

        public Date Date { get; }
        // Canonical event id for format-8 Service Record projections. A value of zero is
        // retained only for direct compatibility/test entries that have not yet crossed the
        // campaign recorder boundary.
        public long CampaignEventId { get; }
        public SoldierEventType Type { get; }
        // The human-readable body of the event, without any leading date stamp.
        public string Detail { get; }
        public int? FactionId { get; }
        public int? WeaponTemplateId { get; }
        public int? Magnitude { get; }
        public string LocationName { get; }
        public IReadOnlyList<int> RelatedSoldierIds => _relatedSoldierIds;

        public SoldierEvent(Date date, SoldierEventType type, string detail,
                            int? factionId = null, int? weaponTemplateId = null,
                            int? magnitude = null, string locationName = null,
                            IEnumerable<int> relatedSoldierIds = null,
                            long campaignEventId = 0)
        {
            // Date is the mutable campaign clock. History entries must retain the date
            // on which they occurred rather than following that clock as turns advance.
            Date = CopyDate(date);
            if (campaignEventId < 0) throw new System.ArgumentOutOfRangeException(nameof(campaignEventId));
            CampaignEventId = campaignEventId;
            Type = type;
            Detail = detail;
            FactionId = factionId;
            WeaponTemplateId = weaponTemplateId;
            Magnitude = magnitude;
            LocationName = locationName;
            _relatedSoldierIds = relatedSoldierIds?.ToList() ?? [];
        }

        private static Date CopyDate(Date date) =>
            date == null ? null : new Date(date.Millenium, date.Year, date.Week);

        // Formats the service-record line. Career events are date-stamped; death notes are
        // kept as standalone epitaphs because their detail is already a complete record.
        public string Render() =>
            Type == SoldierEventType.Death ? Detail : $"{Date}: {Detail}";
    }
}
