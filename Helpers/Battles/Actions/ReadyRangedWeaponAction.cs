using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ReadyRangedWeaponAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;

        public int ActorId => _soldier.Soldier.Id;

        public ReadyRangedWeaponAction(BattleSoldier soldier, RangedWeapon weapon)
        {
            _soldier = soldier;
            _weapon = weapon;
        }

        public void Execute(BattleState state)
        {
            int handsFree = _soldier.HandsFree;

            // handle two-handed weapons
            if(_weapon.Template.Location == EquipLocation.TwoHand && handsFree < 2)
            {
                // not enough hands free, unequip any equipped weapons
                _soldier.EquippedRangedWeapons.Clear();
                _soldier.EquippedMeleeWeapons.Clear();
            }
            // handle one-handed weapons
            else if(_weapon.Template.Location == EquipLocation.OneHand && handsFree < 1)
            {
                // not enough hands free, unequip ranged weapons if possible
                if(_soldier.EquippedRangedWeapons.Count > 0)
                {
                    _soldier.EquippedRangedWeapons.Clear();
                }
                // if no ranged weapons equipped, unequip any melee weapons
                else
                {
                    _soldier.EquippedMeleeWeapons.Clear();
                }
            }
            // equip the new weapon
            _soldier.EquippedRangedWeapons.Add(_weapon);
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} readies {_weapon.Template.Name}\n";
        }
    }
}
