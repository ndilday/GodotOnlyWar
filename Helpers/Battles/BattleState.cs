using OnlyWar.Helpers.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Battles
{
    public class BattleState
    {
        private readonly Dictionary<int, IReadOnlyList<Tuple<int, int>>> _soldierPositionsMap;
        private readonly Dictionary<int, BattleSoldier> _soldiers;

        public int TurnNumber { get; private set; }
        public IReadOnlyDictionary<int, BattleSoldier> Soldiers { get => _soldiers; }
        public IReadOnlyDictionary<int, IReadOnlyList<Tuple<int, int>>> SoldierPositionsMap { get => _soldierPositionsMap; }
        public IReadOnlyDictionary<int, BattleSquad> PlayerSquads { get; }
        public IReadOnlyDictionary<int, BattleSquad> OpposingSquads { get; }

        // Constructor for creating the initial state
        public BattleState(IReadOnlyDictionary<int, BattleSquad> playerSquads,
                           IReadOnlyDictionary<int, BattleSquad> opposingSquads)
        {
            _soldiers = new Dictionary<int, BattleSoldier>();
            PlayerSquads = playerSquads;
            OpposingSquads = opposingSquads;
            TurnNumber = 0;
            // add the soldiers to the soldier map from the squads
            foreach (BattleSquad squad in playerSquads.Values.Concat(opposingSquads.Values))
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldiers[soldier.Soldier.Id] = (BattleSoldier)soldier.Clone();
                    _soldierPositionsMap[soldier.Soldier.Id] = soldier.PositionList;
                }
            }
        }

        // Constructor for creating a new state based on a previous state (deep copy)
        public BattleState(BattleState original)
        {
            // deep copy of the squads, replacing soldier references with their copies
            PlayerSquads = original.PlayerSquads.ToDictionary(
                kvp => kvp.Key,
                kvp => (BattleSquad)kvp.Value.Clone());
            OpposingSquads = original.OpposingSquads.ToDictionary(
                kvp => kvp.Key,
                kvp => (BattleSquad)kvp.Value.Clone());
            // create the soldier dictionaries from these squads
            _soldiers = new Dictionary<int, BattleSoldier>();
            _soldierPositionsMap = new Dictionary<int, IReadOnlyList<Tuple<int, int>>>();
            foreach (BattleSquad squad in PlayerSquads.Values.Concat(OpposingSquads.Values))
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldiers[soldier.Soldier.Id] = (BattleSoldier)soldier.Clone();
                    _soldierPositionsMap[soldier.Soldier.Id] = soldier.PositionList;
                }
            }

            TurnNumber = original.TurnNumber + 1;
        }

        // Helper method to get a soldier by ID
        public BattleSoldier GetSoldier(int soldierId)
        {
            return _soldiers[soldierId];
        }

        // Helper method to get a squad by ID
        public BattleSquad GetSquad(int squadId)
        {
            // You might need to search both PlayerSquads and OpposingSquads
            if (PlayerSquads.ContainsKey(squadId))
            {
                return PlayerSquads[squadId];
            }
            else
            {
                return OpposingSquads[squadId];
            }
        }
    }
}