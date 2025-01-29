using OnlyWar.Models.Equippables;
using System;
using System.Collections.Concurrent;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class AimAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly BattleSoldier _target;
        private readonly RangedWeapon _weapon;
        private readonly ConcurrentQueue<string> _log;

        public AimAction(BattleSoldier soldier, BattleSoldier target, RangedWeapon weapon, ConcurrentQueue<string> log)
        {
            _soldier = soldier;
            _target = target;
            _weapon = weapon;
            _log = log;
        }
        public void Execute()
        {
            // check is this is maintaining aim or starting with a new target
            if(_soldier.Aim?.Item1 != _target)
            {
                // this is a new target
                _soldier.Aim = new Tuple<BattleSoldier, RangedWeapon, int>(_target, _weapon, 0);
            }
            else
            {
                // containing aim, increment the bonus
                int curAim = _soldier.Aim.Item3;
                _soldier.Aim = new Tuple<BattleSoldier, RangedWeapon, int>(_target, _weapon, curAim + 1);
            }
            _soldier.TurnsAiming++;
        }

        public string Description()
        {
            if(_soldier.Aim?.Item1 == _target && _soldier.Aim.Item3 > 0)
            {
                return _soldier.Soldier.Name + " continues aiming at " + _target.Soldier.Name;
            }
            else
            {
                return _soldier.Soldier.Name + " aims at " + _target.Soldier.Name;
            }
        }
}
