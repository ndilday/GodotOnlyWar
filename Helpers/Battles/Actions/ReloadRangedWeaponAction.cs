using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ReloadRangedWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;
        private readonly ushort _ammoLeft;
        public ReloadRangedWeaponAction(BattleSoldier soldier, RangedWeapon weapon)
        {
            _soldier = soldier;
            _weapon = weapon;
            _ammoLeft = weapon.LoadedAmmo;
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

        public void Reverse()
        {
            if(_soldier.ReloadingPhase == 0)
            {
                _weapon.LoadedAmmo = _ammoLeft;
                _soldier.ReloadingPhase = (ushort)(_weapon.Template.ReloadTime - 1);
            }
            else
            {
                _soldier.ReloadingPhase--;
            }
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} reloads {_weapon.Template.Name}";
        }
    }
}
