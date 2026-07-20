using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ReadyMeleeWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly MeleeWeapon _weapon;
        private readonly IReadOnlyCollection<int> _handGroupIds;

        public int ActorId => _soldier.Soldier.Id;

        public ReadyMeleeWeaponAction(BattleSoldier soldier, MeleeWeapon weapon,
                                      IReadOnlyCollection<int> handGroupIds = null)
        {
            _soldier = soldier;
            _weapon = weapon;
            _handGroupIds = handGroupIds;
        }

        public void Execute(BattleState state)
        {
            _soldier.ReadyWeapon(_weapon, _handGroupIds);
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} readies {_weapon.Template.Name}\n";
        }
    }
}
