﻿using System;
using System.Collections.Concurrent;

using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class ShootAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;
        private readonly BattleSoldier _target;
        private readonly float _range;
        private int _numberOfShots;
        private readonly bool _useBulk;
        private readonly ConcurrentBag<WoundResolution> _resultList;
        private readonly ConcurrentQueue<string> _log;

        public ShootAction(BattleSoldier shooter, RangedWeapon weapon, BattleSoldier target, float range, int numberOfShots, bool useBulk, ConcurrentBag<WoundResolution> resultList, ConcurrentQueue<string> log)
        {
            _soldier = shooter;
            _weapon = weapon;
            _target = target;
            _range = range;
            _numberOfShots = numberOfShots;
            _useBulk = useBulk;
            _resultList = resultList;
            _log = log;
        }

        public void Execute()
        {
            float skill = _soldier.Soldier.GetTotalSkillValue(_weapon.Template.RelatedSkill);
            float modifier = CalculateToHitModifiers(skill);
            float roll = 10.5f + (3.0f * (float)RNG.NextGaussianDouble());
            float total = skill + modifier - roll;
            _soldier.Aim = null;
            _log.Enqueue($"{_soldier.Soldier.Name} fires a {_weapon.Template.Name} at {_target.Soldier.Name}");
            if(total > 0)
            {
                _log.Enqueue("<color=red>" + _soldier.Soldier.Name + " hits " + _target.Soldier.Name + " " + Math.Min((int)(total/_weapon.Template.Recoil) + 1, _numberOfShots) + " times</color>");
                // there were hits, determine how many
                do
                {
                    HandleHit();
                    total -= _weapon.Template.Recoil;
                    _numberOfShots--;
                } while (total > 1 && _numberOfShots > 0);
            }
            _soldier.TurnsShooting++;
        }

        private float CalculateToHitModifiers(float soldierSkill)
        {
            float totalModifier = 0;
            // the bulky weapon penalty is usually added when the weapon is fired while moving
            if (_useBulk)
            {
                totalModifier -= _weapon.Template.Bulk;
            }
            // if the soldier is aiming at the current target with the current weapon, add the accuracy bonus
            if(_soldier.Aim?.Item1 == _target && _soldier.Aim?.Item2 == _weapon)
            {
                // accuracy of the weapon is limited by the soldier skill
                // TODO: take this into account with enemies, rather than using high attribute, low skill
                totalModifier += _soldier.Aim.Item3 + Math.Min(_weapon.Template.Accuracy, soldierSkill) + 1;
            }
            // apply modifiers for rate of fire, taget size, and range
            totalModifier += BattleModifiersUtil.CalculateRateOfFireModifier(_numberOfShots);
            totalModifier += BattleModifiersUtil.CalculateSizeModifier(_target.Soldier.Size);
            totalModifier += BattleModifiersUtil.CalculateRangeModifier(_range, _target.CurrentSpeed);

            return totalModifier;
        }
        
        private void HandleHit()
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(_target);
            // make sure this body part hasn't already been shot off
            if(!hitLocation.IsSevered)
            {
                float damage = BattleModifiersUtil.CalculateDamageAtRange(_weapon, _range) * (3.5f + ((float)RNG.NextGaussianDouble() * 1.75f));
                float effectiveArmor = _target.Armor.Template.ArmorProvided * _weapon.Template.ArmorMultiplier;
                float penDamage = damage - effectiveArmor;
                if (penDamage > 0)
                {
                    float totalDamage = penDamage * _weapon.Template.WoundMultiplier;
                    _resultList.Add(new WoundResolution(_soldier, _weapon.Template, _target, totalDamage, hitLocation));
                }
            }
        }
    }
}
