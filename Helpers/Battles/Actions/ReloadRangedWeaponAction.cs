using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ReloadRangedWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;

        public int ActorId => _soldier.Soldier.Id;

        public ReloadRangedWeaponAction(BattleSoldier soldier, RangedWeapon weapon)
        {
            _soldier = soldier;
            _weapon = weapon;
        }

        public void Execute(BattleState state)
        {
            _soldier.ReloadingPhase++;
            if(_soldier.ReloadingPhase == _weapon.Template.ReloadTime)
            {
                _weapon.LoadedAmmo = _weapon.Template.AmmoCapacity;
                _soldier.ReloadingPhase = 0;
            }
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} reloads {_weapon.Template.Name}\n";
        }
    }
}
