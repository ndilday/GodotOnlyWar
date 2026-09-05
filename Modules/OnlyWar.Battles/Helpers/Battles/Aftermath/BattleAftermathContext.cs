using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Events;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal sealed class BattleAftermathContext
    {
        private readonly IReadOnlyList<BattleSquad> _firstSideSquads;
        private readonly IReadOnlyList<BattleSquad> _secondSideSquads;
        private readonly HashSet<int> _firstSideSquadIds;
        private readonly HashSet<int> _secondSideSquadIds;

        public Region Region { get; }
        public BattleHistory BattleHistory { get; }
        public IReadOnlyList<BattleSoldier> StartingSoldiers { get; }
        public IReadOnlyList<BattleSoldier> StartingPlayerSoldiers { get; }
        public IReadOnlyList<BattleSquad> ParticipatingSquads { get; }
        public IReadOnlyList<StartingPlayerSquad> StartingPlayerSquads { get; }
        public int FirstSideStartingSoldierCount { get; }
        public int SecondSideStartingSoldierCount { get; }
        public Faction FirstSideFaction { get; }
        public Faction SecondSideFaction { get; }
        public BattleAftermathDependencies Dependencies { get; }
        public BattleEventContextSnapshot BattleEventContext { get; }

        public sealed record StartingPlayerSquad(
            int Id,
            string Name,
            IReadOnlyList<PlayerSoldier> Participants,
            int? MissionId,
            string MissionName,
            MissionType? MissionType,
            int? OrderId,
            string OrderName,
            Aggression? Aggression);

        public BattleAftermathContext(
            IReadOnlyList<BattleSquad> firstSideSquads,
            IReadOnlyList<BattleSquad> secondSideSquads,
            Region region,
            BattleHistory battleHistory,
            BattleAftermathDependencies dependencies)
        {
            _firstSideSquads = firstSideSquads;
            _secondSideSquads = secondSideSquads;
            _firstSideSquadIds = firstSideSquads.Select(squad => squad.Id).ToHashSet();
            _secondSideSquadIds = secondSideSquads.Select(squad => squad.Id).ToHashSet();
            ParticipatingSquads = firstSideSquads.Concat(secondSideSquads).ToList();

            Region = region;
            BattleHistory = battleHistory;
            Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            StartingSoldiers = firstSideSquads
                .Concat(secondSideSquads)
                .SelectMany(squad => squad.AbleSoldiers)
                .ToList();
            StartingPlayerSoldiers = StartingSoldiers
                .Where(soldier => soldier.Soldier is PlayerSoldier)
                .ToList();
            StartingPlayerSquads = firstSideSquads
                .Concat(secondSideSquads)
                .Select(squad =>
                {
                    List<PlayerSoldier> participants = squad.AbleSoldiers
                        .Where(soldier => soldier.Soldier is PlayerSoldier)
                        .Select(soldier => (PlayerSoldier)soldier.Soldier)
                        .ToList();
                    Order order = participants
                        .Select(soldier => soldier.CurrentOrder ?? soldier.AssignedSquad?.CurrentOrders)
                        .FirstOrDefault(candidate => candidate != null);
                    Mission mission = order?.Mission;
                    return new StartingPlayerSquad(
                        squad.Id,
                        squad.Squad?.Name ?? $"Squad {squad.Id}",
                        participants,
                        mission?.Id,
                        mission?.MissionType.ToString(),
                        mission?.MissionType,
                        order?.Id,
                        order == null ? null : $"Order {order.Id}",
                        order?.LevelOfAggression);
                })
                .Where(squad => squad.Participants.Count > 0)
                .ToList();
            FirstSideStartingSoldierCount = firstSideSquads.SelectMany(squad => squad.AbleSoldiers).Count();
            SecondSideStartingSoldierCount = secondSideSquads.SelectMany(squad => squad.AbleSoldiers).Count();
            FirstSideFaction = firstSideSquads.FirstOrDefault()?.Faction;
            SecondSideFaction = secondSideSquads.FirstOrDefault()?.Faction;
            Order order = StartingPlayerSoldiers
                .Select(soldier => soldier.Soldier is PlayerSoldier player
                    ? player.CurrentOrder ?? player.AssignedSquad?.CurrentOrders
                    : null)
                .FirstOrDefault(candidate => candidate != null);
            BattleEventContext = Dependencies.CreateBattleEventContext(region, order);
        }

        public BattleEventContextSnapshot GetPlayerEventContext(
            BattleSoldier soldier,
            bool? playerHeldField = null)
        {
            Faction opposingFaction = GetOpposingFaction(soldier);
            return BattleEventContext.ForOpposingFaction(
                opposingFaction?.Id,
                opposingFaction?.Name,
                playerHeldField);
        }

        public BattleEventContextSnapshot GetPlayerEventContext(
            StartingPlayerSquad squad,
            bool playerHeldField)
        {
            if (squad == null) throw new ArgumentNullException(nameof(squad));
            BattleSoldier anchor = StartingPlayerSoldiers
                .First(soldier => squad.Participants.Any(participant => participant.Id == soldier.Soldier.Id));
            return GetPlayerEventContext(anchor, playerHeldField) with
            {
                MissionId = squad.MissionId,
                MissionName = squad.MissionName,
                MissionType = squad.MissionType,
                OrderId = squad.OrderId,
                OrderName = squad.OrderName,
                Aggression = squad.Aggression
            };
        }

        public Faction GetOpposingFaction(BattleSoldier soldier)
        {
            if (soldier == null)
            {
                return SecondSideFaction ?? FirstSideFaction;
            }

            int squadId = soldier.BattleSquad.Id;
            if (_firstSideSquadIds.Contains(squadId))
            {
                return SecondSideFaction ?? FirstSideFaction;
            }
            if (_secondSideSquadIds.Contains(squadId))
            {
                return FirstSideFaction ?? SecondSideFaction;
            }

            Faction ownFaction = soldier.BattleSquad?.Faction;
            if (ownFaction == FirstSideFaction)
            {
                return SecondSideFaction ?? FirstSideFaction;
            }
            return FirstSideFaction ?? SecondSideFaction;
        }

        public bool IsSecondSide(BattleSoldier soldier) =>
            soldier?.BattleSquad != null && _secondSideSquadIds.Contains(soldier.BattleSquad.Id);

        public bool IsFirstSide(BattleSoldier soldier) =>
            soldier?.BattleSquad != null && _firstSideSquadIds.Contains(soldier.BattleSquad.Id);

        public bool AreOpposingSides(BattleSoldier first, BattleSoldier second) =>
            (IsFirstSide(first) && IsSecondSide(second))
            || (IsSecondSide(first) && IsFirstSide(second));
    }
}
