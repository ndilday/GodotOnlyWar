using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles.Actions
{
    public class ReloadRangedWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;

        public int ActorId => _soldier.Soldier.Id;
        public RangedWeapon Weapon => _weapon;
        public bool Succeeded { get; private set; }

        public ReloadRangedWeaponAction(BattleSoldier soldier, RangedWeapon weapon)
        {
            _soldier = soldier;
            _weapon = weapon;
        }

        public void Execute(BattleState state)
        {
            Succeeded = false;
            if (!_weapon.HasSoldierReload || !_weapon.CanReload)
            {
                return;
            }

            ushort previousLoadedAmmo = _weapon.LoadedAmmo;
            ushort previousReloadProgress = _weapon.ReloadProgress;
            _weapon.AdvanceReload();
            _soldier.ReloadingPhase = _weapon.ReloadProgress;
            Succeeded = previousLoadedAmmo != _weapon.LoadedAmmo
                || previousReloadProgress != _weapon.ReloadProgress;
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} reloads {_weapon.Template.Name}\n";
        }
    }
}
