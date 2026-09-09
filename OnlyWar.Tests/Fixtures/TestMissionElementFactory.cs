using OnlyWar.Battles.Abstractions;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using System.Linq;

namespace OnlyWar.Tests.Fixtures;

internal static class TestMissionElementFactory
{
    public static OperationalMissionElement From(BattleSquad battleSquad)
    {
        return new OperationalMissionElement(
            battleSquad.Id,
            battleSquad.Name,
            battleSquad.Faction,
            battleSquad.Soldiers.Select(soldier => soldier.Soldier),
            battleSquad.IsPlayerSquad,
            new EngagementElementTraits(
                battleSquad.Traits.ProvidesCommandAura,
                battleSquad.Traits.ProvidesSynapse,
                battleSquad.Traits.IsHeadquarters),
            battleSquad.CampaignSquad,
            battleSquad.CampaignCharacter,
            new BattleEngagementResolver.BattleSquadEngagementState(battleSquad),
            battleSquad.EngagementParticipantIds,
            battleSquad.AbleSoldiers.Count);
    }

    public static EngagementResult ToEngagementResult(BattleHistory history)
    {
        EngagementOutcome outcome = history.Outcome == null
            ? null
            : new EngagementOutcome(
                history.Outcome.EndReason switch
                {
                    BattleEndReason.Annihilation => EngagementEndReason.Annihilation,
                    BattleEndReason.Withdrawal => EngagementEndReason.Withdrawal,
                    BattleEndReason.Rout => EngagementEndReason.Rout,
                    BattleEndReason.MutualDisengagement => EngagementEndReason.MutualDisengagement,
                    BattleEndReason.TurnCap => EngagementEndReason.TurnCap,
                    _ => throw new System.ArgumentOutOfRangeException()
                },
                history.Outcome.SideHoldingField switch
                {
                    BattleSide.Attacker => EngagementSide.First,
                    BattleSide.Opposing => EngagementSide.Second,
                    _ => null
                },
                history.Outcome.DisengagedSquadIds,
                history.Outcome.EliminatedSquadIds,
                history.Outcome.RoutingSquadIds,
                history.Outcome.RearGuardSquadIds);

        return new EngagementResult(
            outcome,
            Report: null,
            Summary: null,
            Replay: history,
            history.FirstSideEnemyDeaths,
            history.FirstSideEnemiesKilled,
            history.FirstSideEnemyDeaths,
            SecondSideEnemyDeaths: 0,
            history.KilledSoldierIds.ToArray(),
            history.IncapacitatedSoldierIds.ToArray(),
            history.DamagedSoldierIds.ToArray(),
            history.ClosingSummary);
    }
}
