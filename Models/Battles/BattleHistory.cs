using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleHistory
    {
        public List<Squad> PlayerSquads { get; }
        public List<Squad> OpposingSquads { get; }

        public IReadOnlyDictionary<int, IList<Tuple<int, int>>> StartingSoldierLocations { get; }
        public List<BattleTurn> Turns { get; }

        public BattleHistory(List<Squad> playerSquads, List<Squad> opposingSquads, IReadOnlyDictionary<int, IList<Tuple<int, int>>> startingPositions)
        {
            PlayerSquads = playerSquads;
            OpposingSquads = opposingSquads;
            StartingSoldierLocations = startingPositions;
            Turns = new List<BattleTurn>();
        }
    }
}
