using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ShootAction : IAction
    {
        private string _soldierName, _targetName, _weaponName;

        public int ActorId => ShooterId;

        public int ShooterId { get; }
        public int TargetId { get; }
        public int WeaponId { get; }
        public float Range { get; }
        public int NumberOfShots { get; }
        public bool UseBulk { get; }
        public List<WoundResolution> WoundResolutions { get; }

        public ShootAction(int shooterId, int targetId, int weaponId, float range, int numberOfShots, bool useBulk)
        {
            ShooterId = shooterId;
            TargetId = targetId;
            WeaponId = weaponId;
            Range = range;
            NumberOfShots = numberOfShots;
            UseBulk = useBulk;
            WoundResolutions = new List<WoundResolution>();
        }

        public void Execute(BattleState state)
        {
            // we don't want to resolve hit locations during replays, 
            // so we need to resolve them before calling Execute
            if (WoundResolutions.Count == 0)
            {
                var shooter = state.GetSoldier(ShooterId);
                var target = state.GetSoldier(TargetId);
                var weapon = shooter.EquippedRangedWeapons.First(w => w.Template.Id == WeaponId);
                _soldierName = shooter.Soldier.Name;
                _targetName = target.Soldier.Name;
                _weaponName = weapon.Template.Name;

                var skill = shooter.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                var modifier = CalculateToHitModifiers(shooter, target, weapon, skill);
                var roll = 10.5f + (3.0f * (float)RNG.NextGaussianDouble());
                var total = skill + modifier - roll;
                shooter.Aim = null;
                if (total > 0)
                {
                    // there were hits, determine how many
                    int numberOfShots = NumberOfShots;
                    do
                    {
                        WoundResolution woundResolution = HandleHit(shooter, weapon, target);
                        if(woundResolution != null)
                        {
                            WoundResolutions.Add(woundResolution);
                        }
                        total -= weapon.Template.Recoil;
                        numberOfShots--;
                    } while (total > 1 && numberOfShots > 0);
                }
                shooter.TurnsShooting++;
            }
        }

        private float CalculateToHitModifiers(BattleSoldier shooter, BattleSoldier target, RangedWeapon weapon, float soldierSkill)
        {
            float totalModifier = 0;
            // the bulky weapon penalty is usually added when the weapon is fired while moving
            if (UseBulk)
            {
                totalModifier -= weapon.Template.Bulk;
            }
            // if the soldier is aiming at the current target with the current weapon, add the accuracy bonus
            if (shooter.Aim?.Item1 == target.Soldier.Id && shooter.Aim?.Item2 == weapon)
            {
                // accuracy of the weapon is limited by the soldier skill
                // TODO: take this into account with enemies, rather than using high attribute, low skill
                totalModifier += shooter.Aim.Item3 + Math.Min(weapon.Template.Accuracy, soldierSkill) + 1;
            }
            // apply modifiers for rate of fire, taget size, and range
            totalModifier += BattleModifiersUtil.CalculateRateOfFireModifier(NumberOfShots);
            totalModifier += BattleModifiersUtil.CalculateSizeModifier(target.Soldier.Size);
            totalModifier += BattleModifiersUtil.CalculateRangeModifier(Range, target.CurrentSpeed);

            return totalModifier;
        }

        private WoundResolution HandleHit(BattleSoldier shooter, RangedWeapon weapon, BattleSoldier target)
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(target);
            // make sure this body part hasn't already been shot off
            if (!hitLocation.IsSevered)
            {
                float damage = BattleModifiersUtil.CalculateDamageAtRange(weapon, Range) * (3.5f + ((float)RNG.NextGaussianDouble() * 1.75f));
                float effectiveArmor = target.Armor.Template.ArmorProvided * weapon.Template.ArmorMultiplier;
                float penDamage = damage - effectiveArmor;
                if (penDamage > 0)
                {
                    float totalDamage = penDamage * weapon.Template.WoundMultiplier;
                    return new WoundResolution(shooter, weapon.Template, target, totalDamage, hitLocation);
                }
            }
            return null;
        }

        public string Description()
        {
            return $"{_soldierName} fires a {_weaponName} {NumberOfShots} times at {_targetName}, hitting {WoundResolutions.Count} times";
        }
    }
}
