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

        // Reserved for step 2 (not yet emitted):
        FirstBlood = 100,
        KillMilestone = 101,
        LastSurvivor = 102,
        MentorAssigned = 103,
        Oath = 104,
        NearDeathRecovery = 105,
        MissionOutcome = 106
    }

    // A single, queryable entry in a soldier's history. Carries the structured fields the
    // notability classifier and narrator (PRD 0.8 steps 3-4) query — date, faction,
    // weapon, magnitude, location, related soldiers — alongside a rendered Detail clause
    // that reproduces the legacy free-text line. Render() is the display source of truth
    // for this pass; later passes narrate from the structured fields instead.
    public class SoldierEvent
    {
        private readonly List<int> _relatedSoldierIds;

        public Date Date { get; }
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
                            IEnumerable<int> relatedSoldierIds = null)
        {
            Date = date;
            Type = type;
            Detail = detail;
            FactionId = factionId;
            WeaponTemplateId = weaponTemplateId;
            Magnitude = magnitude;
            LocationName = locationName;
            _relatedSoldierIds = relatedSoldierIds?.ToList() ?? [];
        }

        // Reproduces the legacy history line. Every event is stamped with its date except
        // the death note, which historically carried none.
        public string Render() =>
            Type == SoldierEventType.Death ? Detail : $"{Date}: {Detail}";
    }
}
