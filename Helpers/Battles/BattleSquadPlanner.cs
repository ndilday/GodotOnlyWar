﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquadPlanner
    {
        private readonly BattleGridManager _grid;
        private readonly ConcurrentBag<IAction> _shootActionBag;
        private readonly ConcurrentBag<IAction> _moveActionBag;
        private readonly ConcurrentBag<IAction> _meleeActionBag;
        private readonly MeleeWeapon _defaultMeleeWeapon;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly ConcurrentQueue<string> _log;

        public BattleSquadPlanner(BattleGridManager grid, 
                                  IReadOnlyDictionary<int, BattleSoldier> soldiers,
                                  ConcurrentBag<IAction> shootActionBag,
                                  ConcurrentBag<IAction> moveActionBag,
                                  ConcurrentBag<IAction> meleeActionBag,
                                  ConcurrentQueue<string> log,
                                  MeleeWeapon defaultMeleeWeapon)
        {
            _grid = grid;
            _shootActionBag = shootActionBag;
            _moveActionBag = moveActionBag;
            _meleeActionBag = meleeActionBag;
            _defaultMeleeWeapon = defaultMeleeWeapon;
            _soldierMap = soldiers;
            _log = log;
        }

        public void PrepareActions(BattleSquad squad)
        {
            if (squad.IsInMelee)
            {
                // it doesn't really matter what the soldiers want to do, it's time to flee or fight
                // TODO: evaluate running vs fighting
                foreach(BattleSoldier soldier in squad.AbleSoldiers)
                {
                    if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
                    {
                        AddMeleeActionsToBag(soldier);
                    }
                    else
                    {
                        AddChargeActionsToBag(soldier);
                    }
                }
            }
            else
            {
                // need some concept of squad disposition... stance, whether they're actively aiming
                // determine closest enemy
                // determine our optimal range
                // determine closest enemy optimal range
                // if the enemy wants to advance, we want to stay put, and vice versa
                // if we both want to get closer or both want to stay put, it's more interesting

                // retreatng is moving full tilt away from the enemy
                // TODO: this won't be a valid option when surrounded
                int retreatVotes = 0;
                // falling back is moving at 1/3 speed away from the enemy,
                // leaving the possibility of shooting
                //int fallbackVotes = 0;
                // advancing is sprinting toward the enemy
                int advanceVotes = 0;
                // standing is not moving
                int standVotes = 0;
                // charging is moving into hand-to-hand contact with the enemy
                int chargeVotes = 0;
                foreach (BattleSoldier soldier in squad.AbleSoldiers)
                {
                    float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestSoldierId);
                    BattleSquad closestSquad = _soldierMap[closestSoldierId].BattleSquad;
                    float targetSize = closestSquad.GetAverageSize();
                    float targetArmor = closestSquad.GetAverageArmor();
                    float targetCon = closestSquad.GetAverageConstitution();
                    float preferredHitDistance = BattleModifiersUtil.CalculateOptimalDistance(soldier, targetSize, targetArmor, targetCon);
                    if (preferredHitDistance == -1)
                    {
                        // this soldier wants to run
                        retreatVotes++;
                    }
                    else if (preferredHitDistance == 0)
                    {
                        if (soldier.EquippedRangedWeapons.Count >= 1)
                        {
                            float desperateHitDistance = EstimateArmorPenDistance(soldier.EquippedRangedWeapons[0], targetArmor);
                            desperateHitDistance = Math.Min(desperateHitDistance, 
                                                            BattleModifiersUtil.EstimateHitDistance(soldier.Soldier, soldier.EquippedRangedWeapons[0], targetSize, soldier.HandsFree));
                            if (desperateHitDistance > 0)
                            {
                                float targetPreferredDistance = BattleModifiersUtil.CalculateOptimalDistance(closestSquad.GetRandomSquadMember(),
                                                                           soldier.Soldier.Size,
                                                                           soldier.Armor.Template.ArmorProvided,
                                                                           soldier.Soldier.Constitution);

                                if (desperateHitDistance < targetPreferredDistance)
                                {
                                    // advance
                                    advanceVotes++;
                                }
                                else
                                {
                                    // don't advance
                                    standVotes++;
                                }
                            }
                            else
                            {
                                advanceVotes++;
                                chargeVotes++;
                            }
                        }
                        else
                        {
                            advanceVotes++;
                            chargeVotes++;
                        }
                    }
                    else if (distance > preferredHitDistance * 3)
                    {
                        advanceVotes++;
                    }
                    else
                    {
                        float targetPreferredDistance = BattleModifiersUtil.CalculateOptimalDistance(
                            closestSquad.GetRandomSquadMember(),
                            soldier.Soldier.Size, 
                            soldier.Armor.Template.ArmorProvided,
                            soldier.Soldier.Constitution);

                        if(preferredHitDistance < targetPreferredDistance)
                        {
                            // advance
                            advanceVotes++;
                        }
                        else
                        {
                            // don't advance
                            standVotes++;
                        }
                    }
                }

                if (advanceVotes > standVotes && advanceVotes > retreatVotes)
                {
                    _log.Enqueue(squad.Name + " advances");
                    if (chargeVotes >= advanceVotes / 2)
                    {
                        foreach (BattleSoldier soldier in squad.AbleSoldiers)
                        {
                            AddChargeActionsToBag(soldier);
                        }
                    }
                    else
                    {
                        foreach (BattleSoldier soldier in squad.AbleSoldiers)
                        {
                            AddAdvanceActionsToBag(soldier);
                        }
                    }
                }
                else if (retreatVotes > standVotes && retreatVotes > advanceVotes)
                {
                    foreach (BattleSoldier soldier in squad.AbleSoldiers)
                    {
                        AddRetreatingActionsToBag(soldier, squad);
                    }
                }
                else
                {
                    foreach (BattleSoldier soldier in squad.AbleSoldiers)
                    {
                        AddStandingActionsToBag(soldier);
                    }
                }
            }
        }

        private void AddStandingActionsToBag(BattleSoldier soldier)
        {
            float range = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            float speed = _soldierMap[closestEnemyId].BattleSquad.AbleSoldiers.First().GetMoveSpeed();
            // see if the enemy is within charging range and the soldier doesn't already have a target lined up
            if (speed >= range && (soldier.Aim == null || soldier.RangedWeapons[0].LoadedAmmo == 0))
            {
                AddChargeActionsToBag(soldier);
            }
            else if (soldier.RangedWeapons.Count == 0)
            {
                //Debug.Log("ISoldier with no ranged weapons just standing around");
            }
            // do we have a ranged weapon equipped
            else if (soldier.EquippedRangedWeapons.Count == 0 && soldier.RangedWeapons.Count > 0)
            {
                AddEquipRangedWeaponActionToBag(soldier);
            }
            else if (soldier.ReloadingPhase > 0 ||
                    (soldier.EquippedRangedWeapons.Count > 0 && soldier.RangedWeapons[0].LoadedAmmo == 0))
            {
                AddReloadRangedWeaponActionToBag(soldier);
            }
            // determine if soldier was already aiming and the target is still around and not in a melee
            else if (soldier.Aim != null && _soldierMap.ContainsKey(soldier.Aim.Item1))
            {
                BattleSoldier target = _soldierMap[soldier.Aim.Item1];
                range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, soldier.Aim.Item1);
                // if the aim cannot be improved, go ahead and shoot
                if (soldier.Aim.Item3 == 3)
                {
                    Tuple<float, float> effectEstimate = EstimateHitAndDamage(soldier, target, soldier.Aim.Item2, range, soldier.Aim.Item2.Template.Accuracy + 4);
                    int shotsToFire = CalculateShotsToFire(soldier.Aim.Item2, effectEstimate.Item1, effectEstimate.Item2);
                    soldier.CurrentSpeed = 0;
                    _shootActionBag.Add(new ShootAction(soldier.Soldier.Id,
                        soldier.Aim.Item1,
                        soldier.Aim.Item2.Template.Id,
                        range,
                        shotsToFire,
                        false));
                }
                else
                {
                    // the aim can be improved
                    // current aim bonus is 1 for all-out attack, plus weapon accuracy, plus aim
                    float currentModifiers = soldier.Aim.Item2.Template.Accuracy + soldier.Aim.Item3 + 1;
                    // item1 is the pre-roll to-hit total; item2 is the expected ratio of damage to con, so 1 is a potential killshot
                    Tuple<float, float> resultEstimate = EstimateHitAndDamage(soldier, target, soldier.Aim.Item2, range, currentModifiers);
                    // it's about to attack, go ahead and shoot, you may not get another chance
                    if (target.GetMoveSpeed() > range
                        // there's a good chance of both hitting and killing, go ahead and shoot now
                        || (resultEstimate.Item2 >= 1 && resultEstimate.Item1 >= 0.33f))
                    {
                        int shotsToFire = CalculateShotsToFire(soldier.Aim.Item2, resultEstimate.Item1, resultEstimate.Item2);
                        soldier.CurrentSpeed = 0;
                        _shootActionBag.Add(new ShootAction(soldier.Soldier.Id,
                            soldier.Aim.Item1,
                            soldier.Aim.Item2.Template.Id,
                            range,
                            shotsToFire,
                            false));
                    }
                    else
                    {
                        // keep aiming
                        soldier.CurrentSpeed = 0;
                        _shootActionBag.Add(new AimAction(soldier, target, soldier.Aim.Item2, _log));
                    }
                }
            }
            // need to aim or shoot at a new target
            else
            {
                soldier.CurrentSpeed = 0;
                AddRangedActionToBag(soldier, false);
            }
        }

        private void AddEquipRangedWeaponActionToBag(BattleSoldier soldier)
        {
            int handsFree = soldier.HandsFree;
            // we're standing here without a readied ranged weapon; we should do something about that
            if (soldier.RangedWeapons.Count == 1 && handsFree >= 1)
            {
                // the easiest case... ready our one ranged weapon
                soldier.CurrentSpeed = 0;
                _shootActionBag.Add(new ReadyRangedWeaponAction(soldier, soldier.RangedWeapons[0]));
            }
            else if (soldier.RangedWeapons.Count > 1 && handsFree >= 1)
            {
                // ugh, this is a decision with a lot of factors that will only come up rarely
                // for now, let's go with the longer ranged weapon
                soldier.CurrentSpeed = 0;
            _shootActionBag.Add(new ReadyRangedWeaponAction(soldier, soldier.RangedWeapons.OrderByDescending(w => w.Template.MaximumRange).First()));

            }
        }

        private void AddReloadRangedWeaponActionToBag(BattleSoldier soldier)
        {
            _shootActionBag.Add(new ReloadRangedWeaponAction(soldier, soldier.EquippedRangedWeapons[0]));
        }

        private void AddAdvanceActionsToBag(BattleSoldier soldier)
        {
            // for now advance toward closest enemy;
            // down the road, we may want to advance toward a rearward enemy, ignoring the closest enemy

            float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            float moveSpeed = soldier.GetMoveSpeed();
            if(distance < moveSpeed)
            {
                AddChargeActionsToBag(soldier);
            }
            else
            {
                Tuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
                Tuple<int, int> line = new Tuple<int, int>((short)(enemyPosition.Item1 - soldier.TopLeft.Item1), 
                                                                   (short)(enemyPosition.Item2 - soldier.TopLeft.Item2));
                // soldier can't get there in one move, advance as far as possible
                AddMoveAction(soldier, moveSpeed, line);

                // should the soldier shoot along the way?
                AddRangedActionToBag(soldier, true);
            }
        }

        private void AddRetreatingActionsToBag(BattleSoldier soldier, BattleSquad soldierSquad)
        {
            float moveSpeed = soldier.GetMoveSpeed();

            int newY = (int)(soldierSquad.IsPlayerSquad ? -moveSpeed : moveSpeed);
            AddMoveAction(soldier, moveSpeed, new Tuple<int, int>(0, newY));

            // determine if soldier will shoot as he falls back
            AddRangedActionToBag(soldier, true);
        }

        private void AddMeleeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            // for now just attack, don't worry about cooler moves
            // if we have melee weapons but none are equipped, we should change that
            if (soldier.EquippedMeleeWeapons.Count == 0 && soldier.MeleeWeapons.Count > 0)
            {
                soldier.CurrentSpeed = 0;
                _shootActionBag.Add(new ReadyMeleeWeaponAction(soldier, soldier.MeleeWeapons[0]));
            }
            else
            {
                float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
                if (distance != 1) throw new InvalidOperationException("Attempting to melee with no adjacent enemy");
                BattleSoldier enemy = _soldierMap[closestEnemyId];
                soldier.CurrentSpeed = 0;
                MeleeWeapon weapon = soldier.EquippedMeleeWeapons.Count == 0 ? 
                    _defaultMeleeWeapon : soldier.EquippedMeleeWeapons[0];
                _meleeActionBag.Add(new MeleeAttackAction(soldier, enemy, weapon, false, _log));
            }
        }

        private void AddChargeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            if (soldier.IsInMelee)
            {
                // determine what sort of manuver to make
                AddMeleeActionsToBag(soldier);
            }
            else
            {
                // get stuck in
                // move adjacent to nearest enemy
                // TODO: handle when someone else in the same squad wants to use the same spot
                // TODO: probably by letting the one with the lower id have it, and the higher id has to 
                float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
                float moveSpeed = soldier.GetMoveSpeed();
                Tuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
                if (distance > moveSpeed + 1)
                {
                    Tuple<int, int> moveVector = new Tuple<int, int>(enemyPosition.Item1 - soldier.TopLeft.Item1, enemyPosition.Item2 - soldier.TopLeft.Item2);
                    // we can't make it to an enemy in one move
                    // soldier can't get there in one move, advance as far as possible
                    AddMoveAction(soldier, moveSpeed, moveVector);

                    // should the soldier shoot along the way?
                    AddRangedActionToBag(soldier, true);
                }
                else
                {
                    Tuple<int, int> newPos = _grid.GetClosestOpenAdjacency(soldier.TopLeft, enemyPosition);
                    BattleSquad oppSquad = _soldierMap[closestEnemyId].BattleSquad;
                    if (newPos == null)
                    {
                        // find the next closest
                        // okay, this is one of those times where I made something because it made me feel smart,
                        // but it's probably unreadable so I should change it later
                        // basically, foreach soldier in the squad of the closest enemy, except the closest enemy (who we already checked)
                        // get their locations, and then sort it according to distance square
                        // PROTIP: SQRT is a relatively expensive operation, so sort by distance squares when it's about comparative, not absolute, distance
                        var map = oppSquad.AbleSoldiers
                            .Where(s => s.Soldier.Id != closestEnemyId)
                            .Select(s => new Tuple<int, Tuple<int, int>>(s.Soldier.Id, _grid.GetSoldierPosition(s.Soldier.Id)[0]))
                            .Select(t => new Tuple<int, Tuple<int, int>, Tuple<int, int>>(t.Item1, t.Item2, new Tuple<int, int>(t.Item2.Item1 - soldier.TopLeft.Item1, t.Item2.Item2 - soldier.TopLeft.Item2)))
                            .Select(u => new Tuple<int, Tuple<int, int>, int>(u.Item1, u.Item2, (u.Item3.Item1 * u.Item3.Item1 + u.Item3.Item2 * u.Item3.Item2)))
                            .OrderBy(u => u.Item3);
                        foreach (Tuple<int, Tuple<int, int>, int> soldierData in map)
                        {
                            newPos = _grid.GetClosestOpenAdjacency(soldier.TopLeft, soldierData.Item2);
                            if (newPos != null)
                            {
                                AddChargeActionsHelper(soldier, soldierData.Item1, soldier.TopLeft, (float)Math.Sqrt(soldierData.Item3), oppSquad, newPos);
                                break;
                            }
                        }
                        if (newPos == null)
                        {
                            // we weren't able to find an enemy to get near, guess we try to find someone to shoot, instead?
                            //Debug.Log("ISoldier in squad engaged in melee couldn't find anyone to attack");
                            Tuple<int, int> line = new Tuple<int, int>((short)(enemyPosition.Item1 - soldier.TopLeft.Item1),
                                                                               (short)(enemyPosition.Item2 - soldier.TopLeft.Item2));
                            // soldier can't get there in one move, advance as far as possible
                            AddMoveAction(soldier, moveSpeed, line);
                            //AddStandingActionsToBag(soldier);
                        }
                    }
                    else
                    {
                        AddChargeActionsHelper(soldier, closestEnemyId, soldier.TopLeft, distance, oppSquad, newPos);
                    }
                }
            }
        }

        private void AddChargeActionsHelper(BattleSoldier soldier, int closestEnemyId, Tuple<int, int> currentPosition, float distance, BattleSquad oppSquad, Tuple<int, int> newPos)
        {
            Tuple<int, int> move = new Tuple<int, int>(newPos.Item1 - currentPosition.Item1, newPos.Item2 - currentPosition.Item2);
            float distanceSq = ((move.Item1 * move.Item1) + (move.Item2 * move.Item2));
            float moveSpeed = soldier.GetMoveSpeed();
            if (distance > moveSpeed + 1)
            {
                // we can't make it to an enemy in one move
                // soldier can't get there in one move, advance as far as possible
                
                Tuple<int, int> realMove = CalculateMovementAlongLine(move, moveSpeed);
                AddMoveAction(soldier, moveSpeed, realMove);

                // should the soldier shoot along the way?
                AddRangedActionToBag(soldier, true);
            }
            else if (soldier.EquippedMeleeWeapons.Count == 0 && soldier.MeleeWeapons.Count > 0)
            {
                soldier.CurrentSpeed = 0;
                _shootActionBag.Add(new ReadyMeleeWeaponAction(soldier, soldier.MeleeWeapons[0]));
            }
            else
            {
                //Debug.Log(soldier.Soldier.Name + " charging " + moveSpeed.ToString("F0"));
                soldier.CurrentSpeed = moveSpeed;
                _grid.ReserveSpace(newPos);
                ushort orientation = CalculateOrientationFromVector(move);
                _moveActionBag.Add(new MoveAction(soldier, _grid, currentPosition, newPos, orientation));
                BattleSoldier target = oppSquad.AbleSoldiers.Single(s => s.Soldier.Id == closestEnemyId);
                _meleeActionBag.Add(new MeleeAttackAction(soldier, target, soldier.MeleeWeapons.Count == 0 ? _defaultMeleeWeapon : soldier.EquippedMeleeWeapons[0], distance >= 2, _log));
            }
        }

        private void AddRangedActionToBag(BattleSoldier soldier, bool isMoving)
        {
            if (soldier.RangedWeapons.Count == 0) return;
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                AddEquipRangedWeaponActionToBag(soldier);
            }
            else if (soldier.EquippedRangedWeapons[0].LoadedAmmo == 0)
            {
                AddReloadRangedWeaponActionToBag(soldier);
            }
            else
            {
                AddShootOrAimActionToBag(soldier, isMoving);
            }
        }

        private void AddShootOrAimActionToBag(BattleSoldier soldier, bool isMoving)
        {
            BattleSoldier target;
            if (soldier.TargetId != null && _soldierMap.ContainsKey((int)soldier.TargetId))
            {
                target = _soldierMap[(int)soldier.TargetId];
            }
            else
            {
                _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
                BattleSquad oppSquad = _soldierMap[closestEnemyId].BattleSquad;
                target = oppSquad.GetRandomSquadMember();
                soldier.TargetId = target.Soldier.Id;
            }

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
            // decide whether to shoot or aim
            // calculate the expected number of hits if the soldier shoots now
            // calculate the expected number of hits if the soldier aims for a turn, then shoots
            // if aiming >= 2xshooting, aim
            Tuple<float, float, RangedWeapon> shootNow = GetBestWeaponForSituation(soldier, target, range, isMoving, false);
            Tuple<float, float, RangedWeapon> aimNow = GetBestWeaponForSituation(soldier, target, range, isMoving, true);
            if (shootNow.Item1 * 2 > aimNow.Item1)
            {
                int shotsToFire =
                    CalculateShotsToFire(shootNow.Item3, shootNow.Item1, shootNow.Item2);
                _shootActionBag.Add(new ShootAction(soldier.Soldier.Id,
                    target.Soldier.Id,
                    shootNow.Item3.Template.Id,
                    range,
                    shotsToFire,
                    isMoving));
            }
            else if (!isMoving)
            {
                // aim with longest ranged weapon
                if (aimNow?.Item3 != null)
                {
                    _shootActionBag.Add(new AimAction(soldier, target, aimNow.Item3, _log));
                }
                else
                {
                    _shootActionBag.Add(new AimAction(soldier, target, soldier.EquippedRangedWeapons.OrderByDescending(w => w.Template.MaximumRange).First(), _log));
                }
            }
        }

        private float EstimateArmorPenDistance(RangedWeapon weapon, float targetArmor)
        {
            // if range doesn't matter for damage, we can just limit on hitting 
            if (!weapon.Template.DoesDamageDegradeWithRange) return weapon.Template.MaximumRange;
            float effectiveArmor = targetArmor * weapon.Template.ArmorMultiplier;

            // if there's no chance of doing a wound, maybe we should run?
            if (weapon.Template.DamageMultiplier * 6 < effectiveArmor) return -1;
            // find the range with a 1/3 chance of armor pen
            float distanceRatio = 1 - ( effectiveArmor / (4.25f * weapon.Template.DamageMultiplier));
            if (distanceRatio < 0) return 0;
            return weapon.Template.MaximumRange * distanceRatio;
        }

        private Tuple<float, float, RangedWeapon> GetBestWeaponForSituation(BattleSoldier soldier, BattleSoldier target, float range, bool useBulk, bool useAccuracy)
        {
            RangedWeapon bestWeapon = null;
            float bestAccuracy = 0;
            float bestDamage = -0;
            foreach(RangedWeapon weapon in soldier.EquippedRangedWeapons.OrderByDescending(w => w.Template.DamageMultiplier))
            {
                float bulkAndAccMod = 0;
                bulkAndAccMod -= useBulk ? weapon.Template.Bulk : 0;
                // base accuracy bonus is the weapon's accuracy plus 1 for aiming making it an all-out attack
                bulkAndAccMod += useAccuracy ? weapon.Template.Accuracy + 1 : 0;
                Tuple<float, float> hitAndDamage = EstimateHitAndDamage(soldier, target, weapon, range, bulkAndAccMod);
                // if not likely to break through armor, there's little point
                if (hitAndDamage.Item1 > 0.1f && hitAndDamage.Item2 > bestDamage)
                {
                    // about a 1/10 chance of hitting
                    bestAccuracy = hitAndDamage.Item1;
                    bestDamage = hitAndDamage.Item2;
                    bestWeapon = weapon;
                }
            }
            return new Tuple<float, float, RangedWeapon>(bestAccuracy, bestDamage, bestWeapon);
        }

        private int CalculateShotsToFire(RangedWeapon weapon, float toHitAtMaximumRateOfFire, float damagePerShot)
        {
            int minRoF = 1;
            int maxRof = weapon.Template.RateOfFire;
            // assume all machine guns have to fire at at least 1/4 their max
            if(weapon.Template.RateOfFire > 10)
            {
                minRoF = weapon.Template.RateOfFire / 4;
            }

            if (toHitAtMaximumRateOfFire < .1f)
            {
                // don't waste ammo on impossible shots
                return minRoF;
            }
            if(weapon.LoadedAmmo < maxRof)
            {
                maxRof = weapon.LoadedAmmo;
            }

            int killRof = (int)Math.Round(1 / damagePerShot) + 1;

            return Math.Clamp(killRof, minRoF, maxRof);

        }

        private Tuple<float, float> EstimateHitAndDamage(BattleSoldier soldier, BattleSoldier target, RangedWeapon weapon, float range, float moveAndAimMod)
        {
            float sizeMod = BattleModifiersUtil.CalculateSizeModifier(target.Soldier.Size);
            float armor = target.Armor.Template.ArmorProvided;
            float con = target.Soldier.Constitution;
            float expectedDamage = CalculateExpectedDamage(weapon, range, armor, con);
            float rangeMod = BattleModifiersUtil.CalculateRangeModifier(range, target.CurrentSpeed);
            float rofMod = BattleModifiersUtil.CalculateRateOfFireModifier(weapon.Template.RateOfFire);
            float weaponSkill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
            float total = weaponSkill + rofMod + rangeMod + sizeMod + moveAndAimMod - 10.5f;
            float probability = GaussianCalculator.ApproximateNormalCDF(total);
            return new Tuple<float, float>(probability, expectedDamage);
        }

        private void AddMoveAction(BattleSoldier soldier, float moveSpeed, Tuple<int, int> line)
        {
            Tuple<int, int> desiredMove = CalculateMovementAlongLine(line, moveSpeed);
            Tuple<int, int> newLocation = new Tuple<int, int>(soldier.TopLeft.Item1 + desiredMove.Item1, soldier.TopLeft.Item2 + desiredMove.Item2);
            newLocation = FindBestLocation(soldier.TopLeft, newLocation, moveSpeed);
            soldier.CurrentSpeed = moveSpeed;
            _grid.ReserveSpace(newLocation);
            ushort orientation = CalculateOrientationFromVector(line);
            _moveActionBag.Add(new MoveAction(soldier, _grid, soldier.TopLeft, newLocation, orientation));
        }

        private Tuple<int, int> CalculateMovementAlongLine(Tuple<int, int> line, float moveSpeed)
        {
            Tuple<int, int> targetLocation;
            if (moveSpeed <= 0) return new Tuple<int, int>(0, 0);   // this shouldn't happen
            else if(line.Item1 == 0)
            {
                targetLocation = new Tuple<int, int>(0, line.Item2 < 0 ? -(int)moveSpeed : (int)moveSpeed);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }
            else if(line.Item2 == 0)
            {
                targetLocation = new Tuple<int, int>(line.Item1 < 0 ? -(int)moveSpeed : (int)moveSpeed, 0);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }

            // multiply line by the square root of moveSpeed^2/line^2
            int lineLengthSq = (line.Item1 * line.Item1) + (line.Item2 * line.Item2);
            float speedSq = moveSpeed * moveSpeed;
            float multiplier = (float)Math.Sqrt(speedSq / lineLengthSq);

            // if we're fast enough to get to the destination, just go there
            if (multiplier >= 1.0f) return line;

            float xDistance = line.Item1 * multiplier;
            float yDistance = line.Item2 * multiplier;

            // should always move a minimum of one space
            if (xDistance == 0 && yDistance == 0)
            {
                if (line.Item1 > line.Item2)
                {
                    return new Tuple<int, int>(1, 0);
                }
                else
                {
                    return new Tuple<int, int>(0, 1);
                }
            }
            else
            {
                // if there's movement in both dimensions and "Wasted" movement in the longer direction
                // determine if the excess is enough to finish the movement along the smaller leg
                float xLeftover = xDistance % 1;
                float yLeftover = yDistance % 1;

                if (line.Item2 != 0 && xLeftover != 0 && Math.Abs(xDistance) > Math.Abs(yDistance))
                {
                    int x = (int)xDistance;
                    int y = yDistance < 0 ? (int)yDistance -1 : (int)yDistance + 1;
                    if((x * x) + (y * y) < speedSq)
                    {
                        return new Tuple<int, int>(x, y);
                    }
                }
                else if (line.Item2 != 0 && yLeftover != 0)
                {
                    int x = xDistance < 0 ? (int)xDistance - 1: (int)xDistance + 1;
                    int y = (int)yDistance;
                    if ((x * x) + (y * y) < speedSq)
                    {
                        return new Tuple<int, int>(x, y);
                    }
                }
            }
            return new Tuple<int, int> ((int)xDistance, (int)yDistance);
        }

        private ushort CalculateOrientationFromVector(Tuple<int, int> vector)
        {
            if (Math.Abs(vector.Item1) > Math.Abs(vector.Item2))
            {
                if (vector.Item1 > 0)
                {
                    return 1;
                }
                else
                {
                    return 3;
                }
            }
            else if (vector.Item2 < 0)
            {
                return 2;
            }
            return 0;
        }

        private Tuple<int, int> FindBestLocation(Tuple<int, int> startingPoint, Tuple<int, int> targetPoint, float speed)
        {
            float speedSq = speed * speed;
            int xMove = targetPoint.Item1 - startingPoint.Item1;
            int xMoveSq = xMove * xMove;
            int yMove = targetPoint.Item2 - startingPoint.Item2;
            int yMoveSq = yMove * yMove;
            // try shifting around the shorter axis first
            if (xMoveSq > yMoveSq)
            {

                while (xMoveSq > 0)
                {
                    int direction = yMove < 0 ? -1 : 1;
                    int i = 2;
                    int newY = yMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    while (newY * newY <= speedSq - xMoveSq)
                    {
                        Tuple<int, int> newTarget = new Tuple<int, int>(startingPoint.Item1 + xMove, startingPoint.Item2 + newY);
                        if (_grid.IsSpaceAvailable(newTarget))
                        {
                            return newTarget;
                        }
                        i++;
                        newY = yMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    }
                    // if we can't find a lateral move that works, start over with the main axis reduced by 1
                    xMove -= xMove > 0 ? 1 : -1;
                    xMoveSq = xMove * xMove;
                }
                //Debug.Log($"There is no place in the world for this move: {startingPoint.Item1}, {startingPoint.Item2}->{targetPoint.Item1},{targetPoint.Item2}, {speed}");
                return startingPoint;
            }
            else
            {
                while (yMoveSq > 0)
                {
                    int direction = xMove < 0 ? -1 : 1;
                    int i = 2;
                    int newX = xMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    while (newX * newX <= speedSq - yMoveSq)
                    {
                        Tuple<int, int> newTarget = new Tuple<int, int>(startingPoint.Item1 + newX, startingPoint.Item2 + yMove);
                        if (_grid.IsSpaceAvailable(newTarget))
                        {
                            return newTarget;
                        }
                        i++;
                        newX = xMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    }
                    // if we can't find a lateral move that works, start over with the main axis reduced by 1
                    yMove -= yMove > 0 ? 1 : -1;
                    yMoveSq = yMove * yMove;
                }
                //Debug.Log($"There is no place in the world for this move: {startingPoint.Item1}, {startingPoint.Item2}->{targetPoint.Item1},{targetPoint.Item2}, {speed}");
                return startingPoint;
            }
        }

        private float CalculateExpectedDamage(RangedWeapon weapon, float range, float armor, float con)
        {
            float effectiveStrength = BattleModifiersUtil.CalculateDamageAtRange(weapon, range);
            float effectiveArmor = armor * weapon.Template.ArmorMultiplier;
            return ((effectiveStrength * 4.25f) - effectiveArmor) / con;
        }
    }
}
