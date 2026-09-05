using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Adds the small equip/reload actions permitted while a squad is moving at a run posture.
    /// Kept separate from the melee builder so that its callback is a narrow operation rather
    /// than a callback into <see cref="BattleSquadPlanner"/>.
    /// </summary>
    internal sealed class SquadRunUtilityActionBuilder
    {
        private readonly ActionSink _actions;

        internal SquadRunUtilityActionBuilder(
            SquadPlanningServices services,
            ActionSink actions)
        {
            System.ArgumentNullException.ThrowIfNull(services);
            _actions = actions ?? throw new System.ArgumentNullException(nameof(actions));
        }

        internal void AddPermittedRunUtilityActionToBag(BattleSoldier soldier)
        {
            if (soldier.RangedWeapons.Count == 0)
            {
                return;
            }
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                AddEquipRangedWeaponActionToBag(soldier);
            }
            else if (soldier.EquippedRangedWeapons[0].CanReload
                && (soldier.EquippedRangedWeapons[0].ReloadProgress > 0
                    || soldier.EquippedRangedWeapons[0].LoadedAmmo == 0))
            {
                AddReloadRangedWeaponActionToBag(soldier);
            }
            else
            {
                RangedWeapon emptyBlastWeapon = soldier.RangedWeapons
                    .FirstOrDefault(weapon => weapon.Template.IsBlastWeapon
                        && weapon.LoadedAmmo == 0);
                if (emptyBlastWeapon != null && emptyBlastWeapon.CanReload
                    && emptyBlastWeapon.ReloadProgress == 0)
                {
                    _actions.Shoot.Add(new ReloadRangedWeaponAction(soldier, emptyBlastWeapon));
                }
            }
        }

        private void AddEquipRangedWeaponActionToBag(BattleSoldier soldier)
        {
            var usableWeapons = soldier.RangedWeapons
                .Where(weapon => (int)weapon.Template.Location <= soldier.FunctioningHands)
                .ToList();
            // we're standing here without a readied ranged weapon; we should do something about that
            if (usableWeapons.Count == 1)
            {
                _actions.Shoot.Add(new ReadyRangedWeaponAction(soldier, usableWeapons[0]));
            }
            else if (usableWeapons.Count > 1)
            {
                // Keep the existing deterministic choice for the uncommon multi-weapon case.
                _actions.Shoot.Add(new ReadyRangedWeaponAction(
                    soldier,
                    usableWeapons.OrderByDescending(w => w.Template.MaximumRange).First()));
            }
        }

        private void AddReloadRangedWeaponActionToBag(BattleSoldier soldier)
        {
            _actions.Shoot.Add(new ReloadRangedWeaponAction(
                soldier,
                soldier.EquippedRangedWeapons[0]));
        }
    }
}
