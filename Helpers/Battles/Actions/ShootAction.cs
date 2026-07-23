using System;
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
        private readonly BattleGridManager _grid;
        private readonly IRNG _random;
        private bool _isResolved;
        private bool _strayHitWasFriendly;
        private string _strayTargetName;

        public int ActorId => ShooterId;

        public int ShooterId { get; }
        public int TargetId { get; }
        public int WeaponId { get; }
        public float Range { get; }
        public int NumberOfShots { get; }
        public bool UseBulk => BulkMultiplier > 0;
        public float BulkMultiplier { get; }
        public float AimMultiplier { get; }
        public int? StrayTargetId { get; private set; }
        public bool IsFriendlyFire => StrayTargetId.HasValue && _strayHitWasFriendly;
        public int HitCount { get; private set; }
        public List<WoundResolution> WoundResolutions { get; }

        public ShootAction(
            int shooterId,
            int targetId,
            int weaponId,
            float range,
            int numberOfShots,
            bool useBulk,
            BattleGridManager grid,
            IRNG random)
            : this(
                shooterId,
                targetId,
                weaponId,
                range,
                numberOfShots,
                useBulk ? 1f : 0f,
                useBulk ? 0f : 1f,
                grid,
                random)
        {
        }

        public ShootAction(
            int shooterId,
            int targetId,
            int weaponId,
            float range,
            int numberOfShots,
            float bulkMultiplier,
            float aimMultiplier,
            BattleGridManager grid,
            IRNG random)
        {
            ShooterId = shooterId;
            TargetId = targetId;
            WeaponId = weaponId;
            Range = range;
            NumberOfShots = numberOfShots;
            BulkMultiplier = Math.Max(0, bulkMultiplier);
            AimMultiplier = Math.Clamp(aimMultiplier, 0, 1);
            _grid = grid;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            WoundResolutions = new List<WoundResolution>();
        }

        public void Execute(BattleState state)
        {
            // we don't want to resolve hit locations during replays, 
            // so we need to resolve them before calling Execute
            if (!_isResolved)
            {
                var shooter = state.GetSoldier(ShooterId);
                var target = state.GetSoldier(TargetId);
                var weapon = shooter.EquippedRangedWeapons.First(w => w.Template.Id == WeaponId);
                _soldierName = shooter.Soldier.Name;
                _targetName = target.Soldier.Name;
                _weaponName = weapon.Template.Name;

                var skill = shooter.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                bool firingIntoMelee = _grid?.IsTargetEngagedWithShootersAllies(ShooterId, TargetId) == true;
                var modifier = CalculateToHitModifiers(shooter, target, weapon, skill, firingIntoMelee);
                var roll = 10.5f + (3.0f * (float)_random.NextRandomZValue());
                var total = skill + modifier - roll;
                shooter.Aim = null;
                if (total > 0)
                {
                    // there were hits, determine how many
                    int numberOfShots = NumberOfShots;
                    do
                    {
                        HitCount++;
                        WoundResolution woundResolution = HandleHit(shooter, weapon, target);
                        if(woundResolution != null)
                        {
                            WoundResolutions.Add(woundResolution);
                        }
                        total -= weapon.Template.Recoil;
                        numberOfShots--;
                    } while (total > 1 && numberOfShots > 0);
                }
                else if (firingIntoMelee && RangedFriendlyFireRules.IsNearMiss(total))
                {
                    IReadOnlyList<BattleSoldier> participants = _grid
                        .GetMeleeScrumParticipants(TargetId)
                        .Select(state.GetSoldier)
                        .ToList();
                    BattleSoldier strayTarget = RangedFriendlyFireRules.SelectStrayTarget(
                        participants,
                        _random.GetLinearDouble());
                    StrayTargetId = strayTarget.Soldier.Id;
                    _strayHitWasFriendly = _grid.GetSoldierSide(ShooterId)
                        == _grid.GetSoldierSide(strayTarget.Soldier.Id);
                    _strayTargetName = strayTarget.Soldier.Name;

                    HitCount++;
                    WoundResolution woundResolution = HandleHit(shooter, weapon, strayTarget);
                    if (woundResolution != null)
                    {
                        WoundResolutions.Add(woundResolution);
                    }
                }
                shooter.TurnsShooting++;
                _isResolved = true;
            }
        }

        public float CalculateToHitModifiers(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float soldierSkill,
            bool firingIntoMelee)
        {
            float totalModifier = 0;
            // the bulky weapon penalty is usually added when the weapon is fired while moving
            if (BulkMultiplier > 0)
            {
                totalModifier -= weapon.Template.Bulk * BulkMultiplier;
            }
            // if the soldier is aiming at the current target with the current weapon, add the accuracy bonus
            if (shooter.Aim?.Item1 == target.Soldier.Id && shooter.Aim?.Item2 == weapon)
            {
                // accuracy of the weapon is limited by the soldier skill
                // TODO: take this into account with enemies, rather than using high attribute, low skill
                float fullAimBonus = shooter.Aim.Value.Item3
                    + Math.Min(weapon.Template.Accuracy, soldierSkill)
                    + 1;
                totalModifier += fullAimBonus * AimMultiplier;
            }
            // apply modifiers for rate of fire, taget size, and range
            totalModifier += BattleModifiersUtil.CalculateRateOfFireModifier(NumberOfShots);
            totalModifier += BattleModifiersUtil.CalculateSizeModifier(target.Soldier.Size);
            totalModifier += BattleModifiersUtil.CalculateRangeModifier(Range, target.CurrentSpeed);
            // elusive targets (serpentine Raveners, weaving Genestealers, camo-caped
            // Scouts) are flatly harder to hit — see Design/EvasionBurrowAndAmbush.md
            totalModifier -= target.Soldier.Template.Species.RangedEvasion;
            // a Shaken squad's fire is degraded (Design/Active/MoraleAndRout.md §6)
            if (shooter.BattleSquad?.MoraleState == MoraleState.Shaken)
            {
                totalModifier -= MoraleConstants.ShakenRangedAccuracyPenalty;
            }
            if (firingIntoMelee)
            {
                totalModifier += RangedFriendlyFireRules.FiringIntoMeleePenalty;
            }

            return totalModifier;
        }

        private WoundResolution HandleHit(BattleSoldier shooter, RangedWeapon weapon, BattleSoldier target)
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(target, _random);
            // make sure this body part hasn't already been shot off
            if (!hitLocation.IsSevered)
            {
                float damage = BattleModifiersUtil.CalculateDamageAtRange(weapon, Range) * (3.5f + ((float)_random.NextRandomZValue() * 1.75f));
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
            string desc = $"{_soldierName} fires a {_weaponName} {NumberOfShots} {TimeWord(NumberOfShots)} at {_targetName}\n";
            if (StrayTargetId.HasValue)
            {
                string allegiance = IsFriendlyFire ? "friendly fire" : "a stray hit";
                desc += $"The shot misses its mark and strikes {_strayTargetName} ({allegiance})\n";
            }

            desc += DescribeHits(HitCount, WoundResolutions.Count);
            foreach (WoundResolution wound in WoundResolutions)
            {
                desc += wound.Description;
            }
            return desc;
        }

        internal static string DescribeHits(int hitCount, int damagingHitCount)
        {
            int noDamageHitCount = Math.Max(0, hitCount - damagingHitCount);
            if (hitCount > 0 && damagingHitCount == 0)
            {
                return $"Hitting {hitCount} {TimeWord(hitCount)}, but doing no damage\n";
            }

            if (noDamageHitCount > 0)
            {
                return $"Hitting {hitCount} {TimeWord(hitCount)}, with {noDamageHitCount} {HitWord(noDamageHitCount)} doing no damage\n";
            }

            return $"Hitting {hitCount} {TimeWord(hitCount)}\n";
        }

        internal static string TimeWord(int count) => count == 1 ? "time" : "times";

        private static string HitWord(int count) => count == 1 ? "hit" : "hits";
    }
}
