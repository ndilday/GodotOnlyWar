using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
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

        // Example method to move a soldier (update other methods accordingly)
        public void MoveSoldier(int soldierId, Tuple<int, int> newTopLeft, ushort newOrientation)
        {
            if (_soldiers.ContainsKey(soldierId))
            {
                BattleSoldier soldier = _soldiers[soldierId];
                _grid.MoveSoldier(soldier, newTopLeft, newOrientation);
                soldier.TopLeft = newTopLeft;
                soldier.Orientation = newOrientation;
            }
        }

        // Example method to apply a wound (you'll need to flesh this out)
        public void ApplyWound(BattleSoldier inflicter,
                               WeaponTemplate weapon,
                               BattleSoldier sufferer,
                               float damage,
                               HitLocation hitLocation)
        {
            // Find the soldier in the state and apply the wound
            WoundResolver resolver = new WoundResolver(false);
            resolver.WoundQueue.Add(new WoundResolution(inflicter, weapon, sufferer, damage, hitLocation));
            resolver.Resolve();
        }

        // Add other methods you need to modify the state, such as:
        // - Applying damage
        // - Updating ammo counts
        // - Setting flags (e.g., IsInMelee)
        // - etc.

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

    // Example of a BattleTurn class that wraps BattleState
    public class BattleTurn
    {
        public int TurnNumber { get; }
        public BattleState State { get; }
        public List<IAction> Actions { get; }

        public BattleTurn(int turnNumber, BattleState state)
        {
            TurnNumber = turnNumber;
            State = state;
            Actions = [];
        }
    }
}
