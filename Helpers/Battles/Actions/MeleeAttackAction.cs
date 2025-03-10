﻿using System.Collections.Concurrent;

using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class MeleeAttackAction : IAction
    {
        private readonly BattleSoldier _attacker;
        private readonly BattleSoldier _target;
        private readonly MeleeWeapon _weapon;
        private readonly ConcurrentQueue<string> _log;
        private readonly bool _didMove;

        public List<WoundResolution> WoundResolutions { get; }
        public int ActorId => _attacker.Soldier.Id;
        public MeleeAttackAction(BattleSoldier attacker, BattleSoldier target,
                                 MeleeWeapon weapon, bool didMove,
                                 ConcurrentQueue<string> log)
        {
            _attacker = attacker;
            _target = target;
            _weapon = weapon;
            _didMove = didMove;
            _log = log;
            WoundResolutions = new List<WoundResolution>();
        }
        public void Execute(BattleState state)
        {
            if (IsAdjacentToTarget())
            {
                for (int i = 0; i <= _weapon.Template.ExtraAttacks; i++)
                {
                    _attacker.IsInMelee = true;
                    _target.IsInMelee = true;
                    _attacker.BattleSquad.IsInMelee = true;
                    _target.BattleSquad.IsInMelee = true;
                    float modifier = _weapon.Template.Accuracy + (_didMove ? -2 : 0);
                    float skill = _attacker.Soldier.GetTotalSkillValue(_weapon.Template.RelatedSkill);
                    float roll = 10.5f + (3.0f * (float)RNG.NextRandomZValue());
                    float total = skill + modifier - roll;
                    _log.Enqueue(_attacker.Soldier.Name + " swings at " + _target.Soldier.ToString());
                    if (total > 0)
                    {
                        _log.Enqueue(_attacker.Soldier.Name + " strikes " + _target.Soldier.ToString());
                        HandleHit();
                    }
                }
                _attacker.TurnsSwinging++;
            }
            else
            {
                _log.Enqueue("<color=orange>" + _attacker.Soldier.Name + " did not get close enough to attack</color>");
            }
        }

        private bool IsAdjacentToTarget()
        {
            int topLimit, bottomLimit, leftLimit, rightLimit;
            topLimit = _attacker.TopLeft.Item2 + 1;
            leftLimit = _attacker.TopLeft.Item1 - 1;
            bottomLimit = _attacker.BottomRight.Item2 - 1;
            rightLimit = _attacker.BottomRight.Item1 + 1;

            // the target is adjacent if it's not any of
            // above, below, left, or right of the limit grid
            bool targetIsAbove = _target.BottomRight.Item2 > topLimit;
            bool targetIsBelow = _target.TopLeft.Item2 < bottomLimit;
            bool targetIsLeft = _target.BottomRight.Item1 < leftLimit;
            bool targetIsRight = _target.TopLeft.Item1 > rightLimit;

            return !targetIsAbove && !targetIsBelow && !targetIsLeft && !targetIsRight;
        }

        private void HandleHit()
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(_target);
            // make sure this body part hasn't already been severed
            if (!hitLocation.IsSevered)
            {
                // calculate damage based on attacker's strength, weapon multiplier, and 
                float damage = _attacker.Soldier.Strength * _weapon.Template.StrengthMultiplier * (3.5f + ((float)RNG.NextRandomZValue() * 1.75f));
                // determine armor effectiveness based on penetration of the weapon
                float effectiveArmor = _target.Armor.Template.ArmorProvided * _weapon.Template.ArmorMultiplier;
                // determine how much damage penetrates the armor
                float penDamage = damage - effectiveArmor;
                if (penDamage > 0)
                {
                    // determine size of wound
                    float totalDamage = penDamage * _weapon.Template.WoundMultiplier;
                    WoundResolutions.Add(new WoundResolution(_attacker, _weapon.Template, _target, totalDamage, hitLocation));
                }
            }
        }

        public string Description()
        {
            string desc = $"{_attacker.Soldier.Name} attacks {_target.Soldier.Name} with {_weapon.Template.Name}, hitting {WoundResolutions.Count} times.\n";
            foreach(WoundResolution wound in WoundResolutions)
            {
                desc += wound.Description;
            }
            return desc;
        }
    }
}
