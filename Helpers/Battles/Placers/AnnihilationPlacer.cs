using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Placers
{
    class AnnihilationPlacer
    {
        private readonly BattleGridManager _grid;

        public AnnihilationPlacer(BattleGridManager grid)
        {
            _grid = grid;
        }
        public Dictionary<BattleSquad, Tuple<ushort, ushort>> PlaceSquads(IEnumerable<BattleSquad> bottomSquads, 
                                                            IEnumerable<BattleSquad> topSquads)
        {
            Dictionary<BattleSquad, Tuple<ushort, ushort>> result = [];

            ArmyLayout bottomLayout = ArmyLayoutHelper.Instance.LayoutArmyLine(bottomSquads, true);
            ArmyLayout topLayout = ArmyLayoutHelper.Instance.LayoutArmyLine(topSquads, true);

            // TODO: determine distance between forces
            // we should probably base this on weapon ranges of the respective armies
            // for now, we'll just go with 500 yards
            // TODO: exclude crippled soldiers from being deployed
            foreach (KeyValuePair<int, BattleSquadLayout> squadLayoutMapItem in topLayout.SquadLayoutMap)
            {
            }

            foreach (KeyValuePair<int, BattleSquadLayout> squadLayoutMapItem in bottomLayout.SquadLayoutMap)
            {
            }
            // TODO: place armies based on distance calculation
            //
            return result;
        }
    }
}
