using System;
using System.Collections.Concurrent;
using OnlyWar.Helpers.Battles.Resolutions;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class MoveAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly BattleGridManager _grid;
        private readonly Tuple<int, int> _newTopLeft, _currentTopLeft;
        private readonly ushort _orientation, _currentOrientation;
        private readonly ConcurrentBag<MoveResolution> _resultList;
        public MoveAction(BattleSoldier soldier, BattleGridManager grid, Tuple<int, int> currentTopLeft, ushort currentOrientation, Tuple<int, int> newTopLeft, ushort orientation, ConcurrentBag<MoveResolution> resultList)
        {
            _soldier = soldier;
            _grid = grid;
            _newTopLeft = newTopLeft;
            _orientation = orientation;
            _resultList = resultList;
            _currentOrientation = currentOrientation;
            _currentTopLeft = currentTopLeft;
        }

        public void Execute()
        {
            _resultList.Add(new MoveResolution(_soldier, _grid, _newTopLeft, _orientation));
            _soldier.TurnsRunning++;
        }

        public void Reverse()
        {
            _resultList.Add(new MoveResolution(_soldier, _grid, _currentTopLeft, _currentOrientation));
            _soldier.TurnsRunning--;
        }
    }
}
