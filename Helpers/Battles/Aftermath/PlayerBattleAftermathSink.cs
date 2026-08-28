using OnlyWar.Models;
using OnlyWar.Models.Events;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal sealed class PlayerBattleAftermathSink :
        IPlayerBattleAftermathSink,
        IPlayerCampaignEventSink,
        IPlayerNarrativeEventSink
    {
        private readonly PlayerForce _playerForce;
        private int _battleOrdinal;
        private int _activeBattleOrdinal = -1;
        private string _activeBattleCorrelation;
        private BattleEventContextSnapshot _activeBattleContext;
        private bool _battleHistoryRecorded;

        public PlayerBattleAftermathSink(PlayerForce playerForce)
        {
            // NPC-only and reduced simulation sessions may legitimately have no player force.
            // Keep the boundary available for those sessions, but fail explicitly if a player
            // aftermath policy ever tries to apply a campaign effect without player state.
            _playerForce = playerForce;
        }

        public void MoveToFallenBrothers(PlayerSoldier soldier)
        {
            if (soldier == null)
            {
                throw new ArgumentNullException(nameof(soldier));
            }
            PlayerForce playerForce = RequirePlayerForce();
            if (playerForce.Army == null)
            {
                throw new InvalidOperationException(
                    "A player army is required to record a fallen battle participant.");
            }

            // Only his own death ends an attachment; releasing it here keeps a dead man out
            // of an order's AttachedSoldiers (and out of the OrderSoldier save table).
            Orders.OrderAttachment.Detach(soldier);
            Squad formerSquad = soldier.AssignedSquad;
            formerSquad?.RemoveSquadMember(soldier);
            if (formerSquad?.Members.Count == 0)
            {
                new SquadLifecycleService(playerForce).HandleEmptySquad(formerSquad);
            }
            soldier.AssignedSquad = null;
            playerForce.Army.PlayerSoldierMap.Remove(soldier.Id);
            playerForce.Army.FallenBrothers[soldier.Id] = soldier;
        }

        public void AddRecoveredGeneseed(float purity) =>
            RequirePlayerForce().AddRecoveredGeneseed(purity);

        public void BeginBattle(BattleEventContextSnapshot context)
        {
            _activeBattleContext = context ?? throw new ArgumentNullException(nameof(context));
            _activeBattleCorrelation = context.CorrelationKey;
            _activeBattleOrdinal = _battleOrdinal++;
            _battleHistoryRecorded = false;
        }

        public CampaignEvent RecordBattleParticipation(
            Date date,
            PlayerSoldier soldier,
            BattleEventContextSnapshot context,
            Faction opposingFaction,
            int enemiesTakenDown,
            int woundsReceived)
        {
            PlayerForce force = RequirePlayerForce();
            return force.CampaignEventRecorder.RecordBattleParticipation(
                soldier,
                context,
                enemiesTakenDown,
                woundsReceived,
                opposingFaction?.Id,
                opposingFaction?.Name,
                BuildSquadEntity(soldier));
        }

        public CampaignEvent RecordIncapacitation(
            Date date,
            PlayerSoldier soldier,
            BattleEventContextSnapshot context,
            HitLocation definingLocation,
            WeaponTemplate causingWeapon,
            bool qualifiesAsNearDeath)
        {
            PlayerForce force = RequirePlayerForce();
            return force.CampaignEventRecorder.RecordIncapacitated(
                soldier,
                context,
                definingLocation,
                causingWeapon,
                qualifiesAsNearDeath,
                soldier.Template?.Id,
                soldier.Template?.Name,
                soldier.Template?.Rank,
                BuildSquadEntity(soldier));
        }

        public CampaignEvent RecordSquadLeaderUnavailable(
            PlayerSoldier soldier,
            int squadId,
            string squadName,
            bool wasActualLeader,
            BattleEventContextSnapshot context) =>
            RequirePlayerForce().CampaignEventRecorder.RecordSquadLeaderUnavailable(
                soldier, squadId, squadName, wasActualLeader, context);

        public CampaignEvent RecordDeath(Date date, PlayerSoldier soldier, DeathPayload payload)
        {
            PlayerForce force = RequirePlayerForce();
            return force.CampaignEventRecorder.RecordDeath(
                soldier,
                date,
                payload,
                BuildSquadEntity(soldier));
        }

        public CampaignEvent RecordGeneseedRecovery(
            Date date,
            PlayerSoldier soldier,
            GeneseedRecoveryPayload payload)
        {
            PlayerForce force = RequirePlayerForce();
            return force.CampaignEventRecorder.RecordGeneseedRecovery(
                soldier,
                date,
                payload,
                BuildSquadEntity(soldier));
        }

        public CampaignEvent RecordLastSurvivor(
            PlayerSoldier soldier,
            LastSurvivorPayload payload)
        {
            PlayerForce force = RequirePlayerForce();
            return force.CampaignEventRecorder.RecordLastSurvivor(
                soldier,
                payload,
                BuildSquadEntity(soldier));
        }

        public CampaignEvent RecordSquadHeldAgainstOdds(
            int squadId,
            string squadName,
            SquadHeldAgainstOddsPayload payload,
            IReadOnlyList<PlayerSoldier> participants)
        {
            PlayerForce force = RequirePlayerForce();
            return force.CampaignEventRecorder.RecordSquadHeldAgainstOdds(
                squadId,
                squadName,
                payload,
                participants);
        }

        public void RecordCreditedKill(
            Date date,
            PlayerSoldier soldier,
            Faction opposingFaction,
            WeaponTemplate weapon,
            Region region,
            string victimDisplayName)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            PlayerForce force = RequirePlayerForce();
            int newTotal = soldier.FactionCasualtyCountMap.Values.Sum(value => value);
            int previousTotal = Math.Max(0, newTotal - 1);
            int? opposingFactionId = opposingFaction?.Id;
            int? weaponId = weapon?.Id;
            List<CampaignEventEntityRef> location = new();
            if (opposingFaction != null)
            {
                location.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Faction,
                    opposingFaction.Id,
                    CampaignEventEntityRole.Opponent,
                    opposingFaction.Name));
            }
            if (region != null)
            {
                location.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Region,
                    region.Id,
                    CampaignEventEntityRole.Location,
                    region.Name));
                if (region.Planet != null)
                {
                    location.Add(new CampaignEventEntityRef(
                        CampaignEntityKind.Planet,
                        region.Planet.Id,
                        CampaignEventEntityRole.Location,
                        region.Planet.Name));
                }
            }
            force.CampaignEventRecorder.RecordKillMilestones(
                soldier,
                previousTotal,
                newTotal,
                opposingFactionId,
                weaponId,
                date.GetTotalWeeks(),
                GetActiveBattleCorrelation(date),
                victimDisplayName,
                location);
        }

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents)
        {
            PlayerForce force = RequirePlayerForce();
            List<string> entries = subEvents?.ToList() ?? [];
            if (!_battleHistoryRecorded)
            {
                force.AddToBattleHistory(date, title, entries);
                _battleHistoryRecorded = true;
            }
            string correlation = GetActiveBattleCorrelation(date);
            int ordinal = _activeBattleOrdinal;
            force.RecordBattleResolved(
                date,
                title,
                entries,
                correlation,
                $"battle/resolved/{date.GetTotalWeeks()}/{ordinal}");
            // Keep the completed context until the next BeginBattle call.  The aftermath
            // policy is deliberately retryable, so a second completion must reuse the
            // same correlation and resolved-event dedupe key instead of opening a new
            // synthetic battle.
        }

        private string GetActiveBattleCorrelation(Date date)
        {
            if (_activeBattleContext != null)
            {
                return _activeBattleContext.CorrelationKey;
            }
            if (_activeBattleOrdinal < 0)
            {
                _activeBattleOrdinal = _battleOrdinal++;
                _activeBattleCorrelation = $"battle/{date.GetTotalWeeks()}/{_activeBattleOrdinal}";
            }
            return _activeBattleCorrelation;
        }

        private static IEnumerable<CampaignEventEntityRef> BuildSquadEntity(PlayerSoldier soldier)
        {
            Squad squad = soldier?.AssignedSquad;
            return squad == null
                ? Enumerable.Empty<CampaignEventEntityRef>()
                : new[]
                {
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Squad,
                        squad.Id,
                        CampaignEventEntityRole.Related,
                        squad.Name)
                };
        }

        private PlayerForce RequirePlayerForce()
        {
            if (_playerForce == null)
            {
                throw new InvalidOperationException(
                    "Player battle aftermath requires a player force in the current game session.");
            }

            return _playerForce;
        }
    }
}
