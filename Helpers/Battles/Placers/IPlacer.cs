using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Placers
{
    interface IArmyPlacer
    {
        Dictionary<BattleSquad, Tuple<ushort, ushort>> PlaceSquads(IEnumerable<BattleSquad> squads);
    }
}
