using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Events;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    /// <summary>
    /// Typed campaign-event boundary for player battle aftermath. The legacy aftermath sink stays
    /// intentionally small so battle tests and NPC simulations can continue to use it without a
    /// campaign ledger.
    /// </summary>
    internal interface IPlayerNarrativeEventSink
    {
        void BeginBattle(BattleEventContextSnapshot context);

        CampaignEvent RecordBattleParticipation(
            Date date,
            PlayerSoldier soldier,
            BattleEventContextSnapshot context,
            Faction opposingFaction,
            int enemiesTakenDown,
            int woundsReceived);

        CampaignEvent RecordIncapacitation(
            Date date,
            PlayerSoldier soldier,
            BattleEventContextSnapshot context,
            HitLocation definingLocation,
            WeaponTemplate causingWeapon,
            bool qualifiesAsNearDeath);

        CampaignEvent RecordDeath(
            Date date,
            PlayerSoldier soldier,
            DeathPayload payload);

        CampaignEvent RecordGeneseedRecovery(
            Date date,
            PlayerSoldier soldier,
            GeneseedRecoveryPayload payload);

        CampaignEvent RecordLastSurvivor(
            PlayerSoldier soldier,
            LastSurvivorPayload payload);

        CampaignEvent RecordSquadHeldAgainstOdds(
            int squadId,
            string squadName,
            SquadHeldAgainstOddsPayload payload,
            IReadOnlyList<PlayerSoldier> participants);
    }
}
