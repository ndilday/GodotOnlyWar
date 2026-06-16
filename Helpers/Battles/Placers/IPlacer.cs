using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Placers
{
    interface IArmyPlacer
    {
        Dictionary<BattleSquad, Coordinate> PlaceSquads(IEnumerable<BattleSquad> squads);
    }
}
