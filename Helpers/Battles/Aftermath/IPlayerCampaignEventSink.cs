using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    // Optional extension of the legacy aftermath sink. Existing test and NPC sinks remain valid;
    // the production player sink alone opts into canonical campaign-event recording.
    internal interface IPlayerCampaignEventSink
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
