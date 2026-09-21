using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles.Actions
{
    public class ReadyRangedWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;
        private readonly IReadOnlyCollection<int> _handGroupIds;

        public int ActorId => _soldier.Soldier.Id;
        public RangedWeapon Weapon => _weapon;
        public bool Succeeded { get; private set; }

        public ReadyRangedWeaponAction(BattleSoldier soldier, RangedWeapon weapon,
                                       IReadOnlyCollection<int> handGroupIds = null)
        {
            _soldier = soldier;
            _weapon = weapon;
            _handGroupIds = handGroupIds;
        }

        public void Execute(BattleState state)
        {
            // Re-readying an already equipped weapon is equipment bookkeeping, not progress
            // toward a fire cycle. The planner normally never emits that action, but keeping the
            // action's success fact explicit prevents a repeated no-op from preserving pursuit.
            Succeeded = false;
            if (_soldier.EquippedRangedWeapons.Contains(_weapon))
            {
                return;
            }

            Succeeded = _soldier.ReadyWeapon(_weapon, _handGroupIds);
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} readies {_weapon.Template.Name}\n";
        }
    }
}
