﻿using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ReloadRangedWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;
        public ReloadRangedWeaponAction(BattleSoldier soldier, RangedWeapon weapon)
        {
            _soldier = soldier;
            _weapon = weapon;
        }

        public void Execute()
        {
            _soldier.ReloadingPhase++;
            if(_soldier.ReloadingPhase == _weapon.Template.ReloadTime)
            {
                _weapon.LoadedAmmo = _weapon.Template.AmmoCapacity;
                _soldier.ReloadingPhase = 0;
            }
        }
    }
}
