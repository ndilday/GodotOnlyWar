using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using System;
using System.Collections.Concurrent;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class AimAction : IAction
    {
        private readonly int _soldierId;
        private readonly int _targetId;
        private readonly RangedWeapon _weapon;
        private readonly ConcurrentQueue<string> _log;
        private string _soldierName, _targetName;
        private bool _isNew;

        public AimAction(BattleSoldier soldier, BattleSoldier target, RangedWeapon weapon, ConcurrentQueue<string> log)
        {
            _soldierId = soldier.Soldier.Id;
            _soldierName = soldier.Soldier.Name;
            _targetId = target.Soldier.Id;
            _targetName = target.Soldier.Name;
            _weapon = weapon;
            _log = log;
        }
        public void Execute(BattleState state)
        {
            BattleSoldier soldier = state.GetSoldier(_soldierId);
            // check is this is maintaining aim or starting with a new target
            if (soldier.Aim?.Item1 != _targetId)
            {
                // this is a new target
                soldier.Aim = new Tuple<int, RangedWeapon, int>(_targetId, _weapon, 0);
                _isNew = true;
            }
            else
            {
                _isNew = false;
                // containing aim, increment the bonus
                int curAim = soldier.Aim.Item3;
                soldier.Aim = new Tuple<int, RangedWeapon, int>(_targetId, _weapon, curAim + 1);
            }
            soldier.TurnsAiming++;
        }

        public string Description()
        {
            if (!_isNew)
            {
                return $"{_soldierName} continues aiming at {_targetName}";
            }
            else
            {
                return $"{_soldierName} aims at {_targetName}";
            }
        }
    }
}
