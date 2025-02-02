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
                           IReadOnlyDictionary<int, BattleSquad> opposingSquads) : this(0, playerSquads, opposingSquads) { }

        // Constructor for creating a new state based on a previous state (deep copy)
        public BattleState(BattleState original) : this(original.TurnNumber + 1, original.PlayerSquads, original.OpposingSquads) { }

        private BattleState(int turnNumber, IReadOnlyDictionary<int, BattleSquad> playerSquads, IReadOnlyDictionary<int, BattleSquad> opposingSquads)
        {
            TurnNumber = turnNumber;

            // deep copy the squads, which will also deep copy the soldiers in the squads
            PlayerSquads = playerSquads.ToDictionary(
                kvp => kvp.Key,
                kvp => (BattleSquad)kvp.Value.Clone());
            OpposingSquads = opposingSquads.ToDictionary(
                kvp => kvp.Key,
                kvp => (BattleSquad)kvp.Value.Clone());

            // add the soldiers to the soldier map from the squads
            _soldiers = new Dictionary<int, BattleSoldier>();
            _soldierPositionsMap = new Dictionary<int, IReadOnlyList<Tuple<int, int>>>();
            foreach (BattleSquad squad in playerSquads.Values.Concat(opposingSquads.Values))
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldiers[soldier.Soldier.Id] = (BattleSoldier)soldier.Clone();
                    _soldierPositionsMap[soldier.Soldier.Id] = soldier.PositionList;
                }
            }
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