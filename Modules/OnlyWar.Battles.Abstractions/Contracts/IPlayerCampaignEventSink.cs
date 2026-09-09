using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles.Abstractions
{
    // Optional extension of the legacy aftermath sink. Existing test and NPC sinks remain valid;
    // the production player sink alone opts into canonical campaign-event recording.
    public interface IPlayerCampaignEventSink
    {
        void RecordCreditedKill(
            Date date,
            PlayerSoldier soldier,
            Faction opposingFaction,
            WeaponTemplate weapon,
            Region region,
            string victimDisplayName);
    }
}
