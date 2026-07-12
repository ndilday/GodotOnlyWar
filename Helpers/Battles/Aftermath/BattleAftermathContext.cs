using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
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
        public int FirstSideStartingSoldierCount { get; }
        public int SecondSideStartingSoldierCount { get; }
        public Faction FirstSideFaction { get; }
        public Faction SecondSideFaction { get; }

        public BattleAftermathContext(
            IReadOnlyList<BattleSquad> firstSideSquads,
            IReadOnlyList<BattleSquad> secondSideSquads,
            Region region,
            BattleHistory battleHistory)
        {
            _firstSideSquads = firstSideSquads;
            _secondSideSquads = secondSideSquads;
            _firstSideSquadIds = firstSideSquads.Select(squad => squad.Id).ToHashSet();
            _secondSideSquadIds = secondSideSquads.Select(squad => squad.Id).ToHashSet();

            Region = region;
            BattleHistory = battleHistory;
            StartingSoldiers = firstSideSquads
                .Concat(secondSideSquads)
                .SelectMany(squad => squad.AbleSoldiers)
                .ToList();
            StartingPlayerSoldiers = StartingSoldiers
                .Where(soldier => soldier.Soldier is PlayerSoldier)
                .ToList();
            FirstSideStartingSoldierCount = firstSideSquads.SelectMany(squad => squad.AbleSoldiers).Count();
            SecondSideStartingSoldierCount = secondSideSquads.SelectMany(squad => squad.AbleSoldiers).Count();
            FirstSideFaction = firstSideSquads.FirstOrDefault()?.Squad?.Faction;
            SecondSideFaction = secondSideSquads.FirstOrDefault()?.Squad?.Faction;
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

            Faction ownFaction = soldier.BattleSquad?.Squad?.Faction;
            if (ownFaction == FirstSideFaction)
            {
                return SecondSideFaction ?? FirstSideFaction;
            }
            return FirstSideFaction ?? SecondSideFaction;
        }

        public bool IsSecondSide(BattleSoldier soldier) =>
            soldier?.BattleSquad != null && _secondSideSquadIds.Contains(soldier.BattleSquad.Id);
    }
}
