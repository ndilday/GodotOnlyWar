using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Battles.Abstractions
{
    /// <summary>
    /// Typed campaign-event boundary for player battle aftermath. The legacy aftermath sink stays
    /// intentionally small so battle tests and NPC simulations can continue to use it without a
    /// campaign ledger.
    /// </summary>
    public interface IPlayerNarrativeEventSink
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

        CampaignEvent RecordSquadLeaderUnavailable(
            PlayerSoldier soldier,
            int squadId,
            string squadName,
            bool wasActualLeader,
            BattleEventContextSnapshot context);

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
