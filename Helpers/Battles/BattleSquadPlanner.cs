using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquadPlanner
    {
        private const float TargetTakeOutConfidenceThreshold = MeleeMath.TakeOutConfidenceTarget;
        private const int RangedTargetSquadCandidateCount = 3;

        private readonly BattleGridManager _grid;
        private readonly ICollection<IAction> _shootActions;
        private readonly ICollection<IAction> _moveActions;
        private readonly ICollection<IAction> _meleeActions;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly IReadOnlyDictionary<int, MeleeWeaponTemplate> _meleeWeaponTemplates;
        private readonly IRNG _random;
        private readonly Action<string> _log;
        private readonly Dictionary<(int AttackerSquadId, int TargetSquadId), float> _squadImminenceCache = [];
        private readonly Dictionary<
            (int ShooterId,
             int TargetId,
             int WeaponId,
             int RangeBits,
             int ModifierBits,
             int TargetSpeedBits,
             int LoadedAmmo),
            RangedTargetEvaluation> _rangedEvaluationCache = [];

        internal sealed class RangedTargetEvaluation
        {
            public BattleSoldier Target { get; }
            public RangedWeapon Weapon { get; }
            public float Range { get; }
            public int ShotsToFire { get; }
            public float HitProbability { get; }
            public float ExpectedDamageRatio { get; }
            public float ExpectedEnemyBattleValueRemoved { get; }
            public float ExpectedFriendlyBattleValueLost { get; }
            public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

            public RangedTargetEvaluation(
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                int shotsToFire,
                float hitProbability,
                float expectedDamageRatio,
                float expectedEnemyBattleValueRemoved,
                float expectedFriendlyBattleValueLost)
            {
                Target = target;
                Weapon = weapon;
                Range = range;
                ShotsToFire = shotsToFire;
                HitProbability = hitProbability;
                ExpectedDamageRatio = expectedDamageRatio;
                ExpectedEnemyBattleValueRemoved = expectedEnemyBattleValueRemoved;
                ExpectedFriendlyBattleValueLost = expectedFriendlyBattleValueLost;
            }
        }

        internal sealed class TemplateFiringLineEvaluation
        {
            public BattleSoldier Target { get; }
            public RangedWeapon Weapon { get; }
            public float Range { get; }
            public IReadOnlyList<int> VictimIds { get; }
            public float ExpectedEnemyBattleValueRemoved { get; }
            public float ExpectedFriendlyBattleValueLost { get; }
            public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

            public TemplateFiringLineEvaluation(
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                IReadOnlyList<int> victimIds,
                float expectedEnemyBattleValueRemoved,
                float expectedFriendlyBattleValueLost)
            {
                Target = target;
                Weapon = weapon;
                Range = range;
                VictimIds = victimIds;
                ExpectedEnemyBattleValueRemoved = expectedEnemyBattleValueRemoved;
                ExpectedFriendlyBattleValueLost = expectedFriendlyBattleValueLost;
            }
        }

        private readonly struct RangedHitEstimateContext
        {
            private readonly float _weaponSkill;
            private readonly float _rangeModifier;
            private readonly float _sizeModifier;
            private readonly float _moveAndAimModifier;
            private readonly float _meleeModifier;
            private readonly float _targetEvasion;

            public RangedHitEstimateContext(
                BattleSoldier soldier,
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                float moveAndAimModifier,
                bool firingIntoMelee)
            {
                _weaponSkill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                _rangeModifier = BattleModifiersUtil.CalculateRangeModifier(range, target.CurrentSpeed);
                _sizeModifier = BattleModifiersUtil.CalculateSizeModifier(target.Soldier.Size);
                _moveAndAimModifier = moveAndAimModifier;
                _meleeModifier = firingIntoMelee
                    ? RangedFriendlyFireRules.FiringIntoMeleePenalty
                    : 0;
                _targetEvasion = target.Soldier.Template.Species.RangedEvasion;
            }

            public float CalculatePreRollHitTotal(int numberOfShots)
            {
                // Preserve the original left-to-right floating-point expression exactly. These
                // values guide target and ammunition decisions, so even rounding-level changes can
                // alter a seeded battle at a threshold.
                float rateOfFireModifier = BattleModifiersUtil.CalculateRateOfFireModifier(numberOfShots);
                return _weaponSkill
                    + rateOfFireModifier
                    + _rangeModifier
                    + _sizeModifier
                    + _moveAndAimModifier
                    + _meleeModifier
                    - _targetEvasion;
            }
        }

        internal int CachedSquadImminenceCount => _squadImminenceCache.Count;
        internal int CachedRangedEvaluationCount => _rangedEvaluationCache.Count;

        public BattleSquadPlanner(BattleGridManager grid, 
                                  IReadOnlyDictionary<int, BattleSoldier> soldiers,
                                  ICollection<IAction> shootActions,
                                  ICollection<IAction> moveActions,
                                  ICollection<IAction> meleeActions,
                                  Action<string> log,
                                  IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates,
                                  IRNG random)
        {
            _grid = grid;
            _shootActions = shootActions;
            _moveActions = moveActions;
            _meleeActions = meleeActions;
            _soldierMap = soldiers;
            _meleeWeaponTemplates = meleeWeaponTemplates
                ?? throw new ArgumentNullException(nameof(meleeWeaponTemplates));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _log = log;
        }

        public void PrepareActions(BattleSquad squad)
        {
            // If no living enemy remains on the grid, this squad has nothing to plan against: every
            // targeting helper below resolves the "nearest enemy" to -1 and then indexes
            // _soldierMap[-1], which throws. Enemy presence is global to a side, so one probe settles
            // it — if the opposing side still has anyone on the grid, every per-soldier lookup will
            // find them. The battle is effectively decided; hold and let the resolver's end-of-turn
            // check close it out. (Latent since forever; surfaced once fully-organized NPC factions
            // began actually joining tactical battles at scale.)
            BattleSoldier probe = squad.AbleSoldiers.FirstOrDefault();
            if (probe == null) return;
            _grid.GetNearestEnemy(probe.Soldier.Id, out int anyEnemyId);
            if (anyEnemyId == -1) return;

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
                    float targetEvasion = closestSquad.GetAverageRangedEvasion();
                    float preferredHitDistance = BattleModifiersUtil.CalculateOptimalDistance(soldier, targetSize, targetArmor, targetCon, targetEvasion);
                    float templateMaximumRange = soldier.RangedWeapons
                        .Where(weapon => weapon.Template.IsTemplateWeapon)
                        .Select(weapon => weapon.Template.MaximumRange)
                        .DefaultIfEmpty(0)
                        .Max();
                    if (templateMaximumRange > 0)
                    {
                        // Template weapons auto-hit once they are in reach, so their movement
                        // decision is about entering the template rather than finding a to-hit
                        // sweet spot derived from accuracy, target size, or evasion.
                        if (distance > templateMaximumRange)
                        {
                            advanceVotes++;
                        }
                        else
                        {
                            standVotes++;
                        }
                        continue;
                    }
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
                                                            BattleModifiersUtil.EstimateHitDistance(soldier.Soldier, soldier.EquippedRangedWeapons[0], targetSize, soldier.HandsFree, targetEvasion));
                            if (desperateHitDistance > 0)
                            {
                                float targetPreferredDistance = BattleModifiersUtil.CalculateOptimalDistance(closestSquad.GetRandomSquadMember(_random),
                                                                           soldier.Soldier.Size,
                                                                           soldier.Armor.Template.ArmorProvided,
                                                                           soldier.Soldier.Constitution,
                                                                           soldier.Soldier.Template.Species.RangedEvasion);

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
                            closestSquad.GetRandomSquadMember(_random),
                            soldier.Soldier.Size,
                            soldier.Armor.Template.ArmorProvided,
                            soldier.Soldier.Constitution,
                            soldier.Soldier.Template.Species.RangedEvasion);

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
            bool hasLoadedTemplateWeapon = soldier.EquippedRangedWeapons.Any(
                weapon => weapon.Template.IsTemplateWeapon && weapon.LoadedAmmo > 0);
            // see if the enemy is within charging range and the soldier doesn't already have a target lined up
            if (!hasLoadedTemplateWeapon
                && speed >= range
                && (soldier.Aim == null || soldier.RangedWeapons[0].LoadedAmmo == 0))
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
            // Keep an established aim only while its target and weapon remain the best ranged option.
            // This preserves the value already invested in aiming without allowing TargetId/Aim to pin
            // the squad to a now-inferior target after the battlefield changes.
            else if (soldier.Aim != null
                && _soldierMap.ContainsKey(soldier.Aim.Item1)
                && IsExistingAimStillBest(soldier))
            {
                BattleSoldier target = _soldierMap[soldier.Aim.Item1];
                range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, soldier.Aim.Item1);
                // if the aim cannot be improved, go ahead and shoot
                if (soldier.Aim.Item3 == 3)
                {
                    RangedTargetEvaluation effectEstimate = EvaluateRangedTarget(
                        soldier,
                        target,
                        soldier.Aim.Item2,
                        range,
                        soldier.Aim.Item2.Template.Accuracy + 4);
                    soldier.CurrentSpeed = 0;
                    _shootActions.Add(new ShootAction(soldier.Soldier.Id,
                        soldier.Aim.Item1,
                        soldier.Aim.Item2.Template.Id,
                        range,
                        effectEstimate.ShotsToFire,
                        false,
                        _grid,
                        _random));
                }
                else
                {
                    // the aim can be improved
                    // current aim bonus is 1 for all-out attack, plus weapon accuracy, plus aim
                    float currentModifiers = soldier.Aim.Item2.Template.Accuracy + soldier.Aim.Item3 + 1;
                    // item1 is the pre-roll to-hit total; item2 is the expected ratio of damage to con, so 1 is a potential killshot
                    RangedTargetEvaluation resultEstimate = EvaluateRangedTarget(
                        soldier,
                        target,
                        soldier.Aim.Item2,
                        range,
                        currentModifiers);
                    // it's about to attack, go ahead and shoot, you may not get another chance
                    if (target.GetMoveSpeed() > range
                        // there's a good chance of both hitting and killing, go ahead and shoot now
                        || (resultEstimate.ExpectedDamageRatio >= 1 && resultEstimate.HitProbability >= 0.33f))
                    {
                        soldier.CurrentSpeed = 0;
                        _shootActions.Add(new ShootAction(soldier.Soldier.Id,
                            soldier.Aim.Item1,
                            soldier.Aim.Item2.Template.Id,
                            range,
                            resultEstimate.ShotsToFire,
                            false,
                            _grid,
                            _random));
                    }
                    else
                    {
                        // keep aiming
                        soldier.CurrentSpeed = 0;
                        _shootActions.Add(new AimAction(soldier, target, soldier.Aim.Item2, _log));
                    }
                }
            }
            // need to aim or shoot at a new target
            else
            {
                soldier.Aim = null;
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
                _shootActions.Add(new ReadyRangedWeaponAction(soldier, soldier.RangedWeapons[0]));
            }
            else if (soldier.RangedWeapons.Count > 1 && handsFree >= 1)
            {
                // ugh, this is a decision with a lot of factors that will only come up rarely
                // for now, let's go with the longer ranged weapon
                soldier.CurrentSpeed = 0;
                _shootActions.Add(new ReadyRangedWeaponAction(soldier, soldier.RangedWeapons.OrderByDescending(w => w.Template.MaximumRange).First()));

            }
        }

        private void AddReloadRangedWeaponActionToBag(BattleSoldier soldier)
        {
            _shootActions.Add(new ReloadRangedWeaponAction(soldier, soldier.EquippedRangedWeapons[0]));
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

            int newY = (int)(_grid.GetSoldierSide(soldier.Soldier.Id) ? -moveSpeed : moveSpeed);
            AddMoveAction(soldier, moveSpeed, new Tuple<int, int>(0, newY));

            // determine if soldier will shoot as he falls back
            AddRangedActionToBag(soldier, true);
        }

        private void AddMeleeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            soldier.CurrentSpeed = 0;
            List<BattleSoldier> adjacentEnemies = _grid.GetAdjacentEnemies(soldier.Soldier.Id)
                .Select(enemyId => _soldierMap[enemyId])
                .Where(enemy => enemy.CanFight)
                .OrderBy(enemy => enemy.Soldier.Id)
                .ToList();
            if (adjacentEnemies.Count == 0)
            {
                throw new InvalidOperationException("Attempting to melee with no adjacent enemy");
            }

            IReadOnlyList<MeleeWeapon> projectedMeleeLoadout = GetProjectedMeleeLoadout(soldier);
            MeleeWeapon projectedPrimary = projectedMeleeLoadout.FirstOrDefault();
            MeleeWeapon projectedSecondary = GetSecondaryMeleeWeapon(projectedMeleeLoadout);
            List<MeleeWeapon> plannedMeleeWeapons = BuildPlannedWeaponSequence(
                soldier,
                projectedPrimary,
                projectedSecondary);
            List<PlannedMeleeStrike> projectedStrikePlans = BuildStrikePlan(
                soldier,
                adjacentEnemies,
                plannedMeleeWeapons,
                didMove: false);
            float meleeScore = EstimateProjectedMeleeBattleValue(
                soldier,
                projectedStrikePlans,
                plannedMeleeWeapons);

            RangedTargetEvaluation pointBlankShot = SelectBestPointBlankRangedTarget(
                soldier,
                adjacentEnemies);
            TemplateFiringLineEvaluation pointBlankTemplate = SelectBestTemplateFiringLine(
                soldier,
                adjacentEnemies);
            float bestRangedScore = Math.Max(
                pointBlankShot?.Score ?? float.MinValue,
                pointBlankTemplate?.Score ?? float.MinValue);
            float forfeitedParryRisk = pointBlankShot == null && pointBlankTemplate == null
                ? 0
                : EstimateForfeitedParryRisk(
                    soldier,
                    adjacentEnemies,
                    projectedMeleeLoadout);
            float pointBlankScore = bestRangedScore - forfeitedParryRisk;

            if (pointBlankTemplate != null
                && pointBlankTemplate.Score >= (pointBlankShot?.Score ?? float.MinValue)
                && pointBlankScore > meleeScore)
            {
                soldier.TargetId = pointBlankTemplate.Target.Soldier.Id;
                _shootActions.Add(new AreaAttackAction(
                    soldier.Soldier.Id,
                    pointBlankTemplate.Target.Soldier.Id,
                    pointBlankTemplate.Weapon.Template.Id,
                    _grid,
                    _random));
                return;
            }

            if (pointBlankShot != null && pointBlankScore > meleeScore)
            {
                soldier.TargetId = pointBlankShot.Target.Soldier.Id;
                _shootActions.Add(new ShootAction(
                    soldier.Soldier.Id,
                    pointBlankShot.Target.Soldier.Id,
                    pointBlankShot.Weapon.Template.Id,
                    pointBlankShot.Range,
                    pointBlankShot.ShotsToFire,
                    useBulk: true,
                    grid: _grid,
                    random: _random));
                return;
            }

            // Preserve the existing action economy: choosing a melee weapon that is not yet in
            // hand spends this turn readying it; an already-ready (or unarmed default) loadout
            // attacks using the exact strike plan that was scored above.
            if (soldier.EquippedMeleeWeapons.Count == 0 && soldier.MeleeWeapons.Count > 0)
            {
                _shootActions.Add(new ReadyMeleeWeaponAction(soldier, soldier.MeleeWeapons[0]));
            }
            else if (projectedStrikePlans.Count > 0)
            {
                _meleeActions.Add(new MeleeAttackAction(
                    soldier,
                    projectedStrikePlans,
                    didMove: false,
                    log: _log,
                    random: _random,
                    meleeWeaponTemplates: _meleeWeaponTemplates));
            }
        }

        private IReadOnlyList<MeleeWeapon> GetProjectedMeleeLoadout(BattleSoldier soldier)
        {
            if (soldier.EquippedMeleeWeapons.Count > 0)
            {
                return soldier.EquippedMeleeWeapons.ToList();
            }

            if (soldier.MeleeWeapons.Count > 0)
            {
                // ReadyMeleeWeaponAction currently draws the first owned weapon. Score that same
                // future state rather than treating a two-handed gunner's melee alternative as zero.
                return [soldier.MeleeWeapons[0]];
            }

            MeleeWeapon unarmedWeapon = MeleeAttackAction.GetUnarmedWeapon(soldier);
            return unarmedWeapon == null ? [] : [unarmedWeapon];
        }

        private static MeleeWeapon GetSecondaryMeleeWeapon(IReadOnlyList<MeleeWeapon> loadout)
        {
            return loadout.Count >= 2
                && loadout[0].Template.Location == EquipLocation.OneHand
                && loadout[1].Template.Location == EquipLocation.OneHand
                    ? loadout[1]
                    : null;
        }

        private RangedTargetEvaluation SelectBestPointBlankRangedTarget(
            BattleSoldier soldier,
            IReadOnlyList<BattleSoldier> adjacentEnemies)
        {
            RangedTargetEvaluation best = null;
            foreach (BattleSoldier target in adjacentEnemies.OrderBy(enemy => enemy.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                foreach (RangedWeapon weapon in soldier.EquippedRangedWeapons
                    .Where(candidate => candidate.LoadedAmmo > 0
                        && !candidate.Template.IsTemplateWeapon
                        && range <= candidate.Template.MaximumRange)
                    .OrderBy(candidate => candidate.Template.Id))
                {
                    RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                        soldier,
                        target,
                        weapon,
                        range,
                        additionalToHitModifier: -weapon.Template.Bulk);
                    if (best == null || evaluation.Score > best.Score)
                    {
                        best = evaluation;
                    }
                }
            }

            return best;
        }

        internal float EstimateProjectedMeleeBattleValue(
            BattleSoldier attacker,
            IReadOnlyList<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<MeleeWeapon> plannedWeapons)
        {
            Dictionary<int, float> targetSurvivalProbability = [];
            int strikeCount = Math.Min(strikePlans.Count, plannedWeapons.Count);
            for (int index = 0; index < strikeCount; index++)
            {
                PlannedMeleeStrike strike = strikePlans[index];
                if (!_soldierMap.TryGetValue(strike.TargetId, out BattleSoldier target))
                {
                    continue;
                }

                float strikeTakeOutProbability = EstimateTakeOutProbability(
                    attacker,
                    target,
                    plannedWeapons[index],
                    didMove: false);
                float survival = targetSurvivalProbability.TryGetValue(
                    strike.TargetId,
                    out float existingSurvival)
                        ? existingSurvival
                        : 1;
                targetSurvivalProbability[strike.TargetId] = survival * (1 - strikeTakeOutProbability);
            }

            return targetSurvivalProbability.Sum(entry =>
                (1 - entry.Value) * GetBattleValue(_soldierMap[entry.Key]));
        }

        internal float EstimateForfeitedParryRisk(
            BattleSoldier defender,
            IReadOnlyList<BattleSoldier> adjacentAttackers,
            IReadOnlyCollection<MeleeWeapon> projectedDefensiveWeapons)
        {
            float defenderBattleValue = GetBattleValue(defender);
            if (defenderBattleValue <= 0 || adjacentAttackers.Count == 0)
            {
                return 0;
            }

            float projectedParryModifier = MeleeAttackAction.GetDefenderDefenseModifier(
                defender,
                projectedDefensiveWeapons);
            float expectedBattleValueRisk = 0;
            foreach (BattleSoldier attacker in adjacentAttackers)
            {
                IReadOnlyList<MeleeWeapon> attackerLoadout = GetProjectedMeleeLoadout(attacker);
                MeleeWeapon primaryWeapon = attackerLoadout.FirstOrDefault();
                if (primaryWeapon == null)
                {
                    continue;
                }

                float primaryStrikeCount = MeleeMath.CalculateBaseAttackCount(
                    attacker.Soldier.AttackSpeed,
                    primaryWeapon.Template.AttackSpeedMultiplier);
                expectedBattleValueRisk += EstimateForfeitedParryRiskForStrikes(
                    defender,
                    attacker,
                    primaryWeapon,
                    primaryStrikeCount,
                    projectedDefensiveWeapons,
                    projectedParryModifier,
                    defenderBattleValue);

                MeleeWeapon secondaryWeapon = GetSecondaryMeleeWeapon(attackerLoadout);
                if (secondaryWeapon != null)
                {
                    expectedBattleValueRisk += EstimateForfeitedParryRiskForStrikes(
                        defender,
                        attacker,
                        secondaryWeapon,
                        1,
                        projectedDefensiveWeapons,
                        projectedParryModifier,
                        defenderBattleValue);
                }
            }

            return Math.Clamp(expectedBattleValueRisk, 0, defenderBattleValue);
        }

        private float EstimateForfeitedParryRiskForStrikes(
            BattleSoldier defender,
            BattleSoldier attacker,
            MeleeWeapon attackingWeapon,
            float strikeCount,
            IReadOnlyCollection<MeleeWeapon> projectedDefensiveWeapons,
            float projectedParryModifier,
            float defenderBattleValue)
        {
            if (strikeCount <= 0)
            {
                return 0;
            }

            float defenderSkill = projectedDefensiveWeapons.Count > 0
                ? projectedDefensiveWeapons.Max(weapon =>
                    defender.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill))
                : MeleeAttackAction.GetDefenderMeleeSkill(
                    defender,
                    attackingWeapon.Template.RelatedSkill);
            float attackerSkill = attacker.Soldier.GetTotalSkillValue(
                attackingWeapon.Template.RelatedSkill);
            float hitProbabilityWithParry = MeleeAttackAction.EstimateHitProbability(
                attackerSkill,
                attackingWeapon.Template.Accuracy,
                didMove: false,
                defenderSkill,
                defender.Soldier.Template.Species.MeleeEvasion,
                projectedParryModifier);
            float hitProbabilityWhileShooting = MeleeAttackAction.EstimateHitProbability(
                attackerSkill,
                attackingWeapon.Template.Accuracy,
                didMove: false,
                defenderSkill,
                defender.Soldier.Template.Species.MeleeEvasion,
                defenderDefenseModifier: 0);
            float increasedHitProbability = Math.Max(
                0,
                hitProbabilityWhileShooting - hitProbabilityWithParry);
            float woundRatio = EstimateTakeOutOnHit(defender, attacker, attackingWeapon);
            return strikeCount
                * increasedHitProbability
                * woundRatio
                * defenderBattleValue;
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
                _shootActions.Add(new ReadyMeleeWeaponAction(soldier, soldier.MeleeWeapons[0]));
            }
            else
            {
                //Debug.Log(soldier.Soldier.Name + " charging " + moveSpeed.ToString("F0"));
                soldier.CurrentSpeed = moveSpeed;
                _grid.ReserveSpace(newPos);
                ushort orientation = CalculateOrientationFromVector(move);
                _moveActions.Add(new MoveAction(soldier, _grid, currentPosition, newPos, orientation));
                BattleSoldier target = oppSquad.AbleSoldiers.Single(s => s.Soldier.Id == closestEnemyId);
                MeleeAttackAction action = CreateMeleeAttackAction(soldier, [target], distance >= 2);
                if (action != null)
                {
                    _meleeActions.Add(action);
                }
            }
        }

        private MeleeAttackAction CreateMeleeAttackAction(BattleSoldier soldier, IEnumerable<BattleSoldier> candidateTargets, bool didMove)
        {
            List<BattleSoldier> targets = candidateTargets
                .Where(target => target != null && target.CanFight)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id)
                .ToList();
            if (targets.Count == 0)
            {
                return null;
            }

            MeleeWeapon primaryWeapon = soldier.GetPrimaryMeleeWeapon(
                MeleeAttackAction.GetUnarmedWeapon(soldier));
            MeleeWeapon secondaryWeapon = soldier.GetSecondaryMeleeWeapon();
            List<MeleeWeapon> plannedWeapons = BuildPlannedWeaponSequence(soldier, primaryWeapon, secondaryWeapon);
            if (plannedWeapons.Count == 0)
            {
                return null;
            }

            List<PlannedMeleeStrike> strikePlans = BuildStrikePlan(soldier, targets, plannedWeapons, didMove);
            if (strikePlans.Count == 0)
            {
                return null;
            }

            return new MeleeAttackAction(
                soldier,
                strikePlans,
                didMove,
                _log,
                _random,
                _meleeWeaponTemplates);
        }

        private List<MeleeWeapon> BuildPlannedWeaponSequence(BattleSoldier soldier, MeleeWeapon primaryWeapon, MeleeWeapon secondaryWeapon)
        {
            int primaryAttackCount = DetermineAttackCount(soldier, primaryWeapon);
            List<MeleeWeapon> plannedWeapons = [];
            for (int i = 0; i < primaryAttackCount; i++)
            {
                plannedWeapons.Add(primaryWeapon);
            }

            if (secondaryWeapon != null)
            {
                plannedWeapons.Add(secondaryWeapon);
            }

            return plannedWeapons;
        }

        private int DetermineAttackCount(BattleSoldier soldier, MeleeWeapon weapon)
        {
            float attackCount = MeleeMath.CalculateBaseAttackCount(
                soldier.Soldier.AttackSpeed,
                weapon?.Template.AttackSpeedMultiplier
                    ?? MeleeWeaponTemplate.DefaultAttackSpeedMultiplier);
            int guaranteedAttacks = (int)Math.Floor(attackCount);
            float fractionalAttack = attackCount - guaranteedAttacks;
            if (_random.GetLinearDouble() < fractionalAttack)
            {
                guaranteedAttacks++;
            }

            return Math.Max(0, guaranteedAttacks);
        }

        private List<PlannedMeleeStrike> BuildStrikePlan(BattleSoldier attacker,
                                                         IReadOnlyList<BattleSoldier> targets,
                                                         IReadOnlyList<MeleeWeapon> plannedWeapons,
                                                         bool didMove)
        {
            List<BattleSoldier> untargetedEnemies = targets.ToList();
            List<PlannedMeleeStrike> strikePlans = [];
            BattleSoldier currentTarget = null;
            float cumulativeTakeOutConfidence = 0;

            foreach (MeleeWeapon weapon in plannedWeapons)
            {
                if (currentTarget == null)
                {
                    List<BattleSoldier> targetPool = untargetedEnemies.Count > 0 ? untargetedEnemies : targets.ToList();
                    currentTarget = SelectBestMeleeTarget(attacker, weapon, targetPool, didMove);
                    cumulativeTakeOutConfidence = 0;
                }

                if (currentTarget == null)
                {
                    break;
                }

                strikePlans.Add(new PlannedMeleeStrike(currentTarget.Soldier.Id,
                                                       weapon.Template.Id,
                                                       currentTarget.Soldier.Name,
                                                       weapon.Template.Name));

                float strikeTakeOutChance = EstimateTakeOutProbability(attacker, currentTarget, weapon, didMove);
                cumulativeTakeOutConfidence = 1 - ((1 - cumulativeTakeOutConfidence) * (1 - strikeTakeOutChance));
                if (cumulativeTakeOutConfidence >= TargetTakeOutConfidenceThreshold)
                {
                    untargetedEnemies.RemoveAll(target => target.Soldier.Id == currentTarget.Soldier.Id);
                    currentTarget = null;
                    cumulativeTakeOutConfidence = 0;
                }
            }

            return strikePlans;
        }

        private BattleSoldier SelectBestMeleeTarget(BattleSoldier attacker,
                                                    MeleeWeapon weapon,
                                                    IReadOnlyList<BattleSoldier> targets,
                                                    bool didMove)
        {
            BattleSoldier bestTarget = null;
            float bestTakeOutChance = float.MinValue;
            float bestHitChance = float.MinValue;

            foreach (BattleSoldier target in targets)
            {
                float hitChance = EstimateHitProbability(attacker, target, weapon, didMove);
                float takeOutChance = Math.Clamp(hitChance * EstimateTakeOutOnHit(target, attacker, weapon), 0, 1);
                if (takeOutChance > bestTakeOutChance
                    || (Math.Abs(takeOutChance - bestTakeOutChance) < 0.0001f && hitChance > bestHitChance)
                    || (Math.Abs(takeOutChance - bestTakeOutChance) < 0.0001f
                        && Math.Abs(hitChance - bestHitChance) < 0.0001f
                        && (bestTarget == null || target.Soldier.Id < bestTarget.Soldier.Id)))
                {
                    bestTarget = target;
                    bestTakeOutChance = takeOutChance;
                    bestHitChance = hitChance;
                }
            }

            return bestTarget;
        }

        private float EstimateTakeOutProbability(BattleSoldier attacker, BattleSoldier target, MeleeWeapon weapon, bool didMove)
        {
            float hitChance = EstimateHitProbability(attacker, target, weapon, didMove);
            return Math.Clamp(hitChance * EstimateTakeOutOnHit(target, attacker, weapon), 0, 1);
        }

        private float EstimateHitProbability(BattleSoldier attacker, BattleSoldier target, MeleeWeapon weapon, bool didMove)
        {
            float attackSkill = attacker.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
            float defenderSkill = MeleeAttackAction.GetDefenderMeleeSkill(target, weapon.Template.RelatedSkill);
            float defenderDefenseModifier = MeleeAttackAction.GetDefenderDefenseModifier(target);
            return MeleeAttackAction.EstimateHitProbability(attackSkill,
                                                            weapon.Template.Accuracy,
                                                            didMove,
                                                            defenderSkill,
                                                            target.Soldier.Template.Species.MeleeEvasion,
                                                            defenderDefenseModifier);
        }

        private float EstimateTakeOutOnHit(BattleSoldier target, BattleSoldier attacker, MeleeWeapon weapon)
        {
            float averageDamage = attacker.Soldier.Strength * weapon.Template.StrengthMultiplier * 3.5f;
            float effectiveArmor = (target.Armor?.Template.ArmorProvided ?? 0) * weapon.Template.ArmorMultiplier;
            float penetratingDamage = Math.Max(0, averageDamage - effectiveArmor);
            float woundRatio = (penetratingDamage * weapon.Template.WoundMultiplier) / target.Soldier.Constitution;
            return Math.Clamp(woundRatio, 0, 1);
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
            TemplateFiringLineEvaluation templateLine = SelectBestTemplateFiringLine(soldier);
            RangedTargetEvaluation targetEvaluation = SelectBestRangedTarget(soldier, isMoving);
            if (templateLine != null
                && templateLine.Score >= (targetEvaluation?.Score ?? float.MinValue))
            {
                soldier.TargetId = templateLine.Target.Soldier.Id;
                _shootActions.Add(new AreaAttackAction(
                    soldier.Soldier.Id,
                    templateLine.Target.Soldier.Id,
                    templateLine.Weapon.Template.Id,
                    _grid,
                    _random));
                return;
            }

            if (targetEvaluation == null)
            {
                if (!isMoving && soldier.EquippedRangedWeapons.Any(
                    weapon => weapon.Template.IsTemplateWeapon && weapon.LoadedAmmo > 0))
                {
                    AddClosingMoveToBag(soldier);
                }
                return;
            }

            BattleSoldier target = targetEvaluation.Target;
            soldier.TargetId = target.Soldier.Id;

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
            // decide whether to shoot or aim
            // calculate the expected number of hits if the soldier shoots now
            // calculate the expected number of hits if the soldier aims for a turn, then shoots
            // if aiming >= 2xshooting, aim
            RangedTargetEvaluation shootNow = GetBestWeaponForSituation(soldier, target, range, isMoving, false);
            RangedTargetEvaluation aimNow = GetBestWeaponForSituation(soldier, target, range, isMoving, true);
            if (shootNow != null && (aimNow == null || shootNow.HitProbability * 2 > aimNow.HitProbability))
            {
                _shootActions.Add(new ShootAction(soldier.Soldier.Id,
                    target.Soldier.Id,
                    shootNow.Weapon.Template.Id,
                    range,
                    shootNow.ShotsToFire,
                    isMoving,
                    _grid,
                    _random));
            }
            else if (!isMoving)
            {
                // aim with longest ranged weapon
                if (aimNow?.Weapon != null)
                {
                    _shootActions.Add(new AimAction(soldier, target, aimNow.Weapon, _log));
                }
                else
                {
                    RangedWeapon aimWeapon = soldier.EquippedRangedWeapons
                        .Where(weapon => !weapon.Template.IsTemplateWeapon)
                        .OrderByDescending(weapon => weapon.Template.MaximumRange)
                        .First();
                    _shootActions.Add(new AimAction(soldier, target, aimWeapon, _log));
                }
            }
        }

        private bool IsExistingAimStillBest(BattleSoldier soldier)
        {
            RangedTargetEvaluation bestTarget = SelectBestRangedTarget(
                soldier,
                useBulk: false,
                includeExistingAim: true);

            return bestTarget != null
                && bestTarget.Target.Soldier.Id == soldier.Aim.Item1
                && bestTarget.Weapon.Template.Id == soldier.Aim.Item2.Template.Id;
        }

        /// <summary>
        /// Scores every soldier in the three nearest in-range enemy squads and returns the
        /// target/weapon pair with the greatest expected battle-value swing.
        /// </summary>
        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            bool useBulk,
            bool includeExistingAim = false)
        {
            if (soldier?.EquippedRangedWeapons == null || soldier.EquippedRangedWeapons.Count == 0)
            {
                return null;
            }

            RangedTargetEvaluation best = null;
            foreach (BattleSquad candidateSquad in GetNearestInRangeEnemySquads(soldier))
            {
                foreach (BattleSoldier target in candidateSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
                    foreach (RangedWeapon weapon in soldier.EquippedRangedWeapons
                        .Where(candidate => candidate.LoadedAmmo > 0
                            && !candidate.Template.IsTemplateWeapon
                            && range <= candidate.Template.MaximumRange)
                        .OrderBy(candidate => candidate.Template.Id))
                    {
                        float toHitModifier = useBulk ? -weapon.Template.Bulk : 0;
                        if (includeExistingAim
                            && soldier.Aim?.Item1 == target.Soldier.Id
                            && soldier.Aim.Item2.Template.Id == weapon.Template.Id)
                        {
                            toHitModifier += weapon.Template.Accuracy + soldier.Aim.Item3 + 1;
                        }

                        RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                            soldier,
                            target,
                            weapon,
                            range,
                            toHitModifier);
                        // Candidate squads, soldiers, and weapons are ordered nearest-first and
                        // deterministically, so an exact tie naturally stays on the closer option.
                        if (best == null || evaluation.Score > best.Score)
                        {
                            best = evaluation;
                        }
                    }
                }
            }

            return best;
        }

        internal TemplateFiringLineEvaluation SelectBestTemplateFiringLine(
            BattleSoldier soldier,
            IEnumerable<BattleSoldier> candidateTargets = null)
        {
            if (soldier?.EquippedRangedWeapons == null
                || soldier.EquippedRangedWeapons.Count == 0
                || !IsPlaced(soldier))
            {
                return null;
            }

            IEnumerable<BattleSoldier> targets = candidateTargets
                ?? GetNearestInRangeEnemySquads(soldier)
                    .SelectMany(candidateSquad => candidateSquad.AbleSoldiers);
            bool shooterSide = _grid.GetSoldierSide(soldier.Soldier.Id);
            TemplateFiringLineEvaluation best = null;
            foreach (BattleSoldier target in targets
                .Where(target => target != null
                    && target.CanFight
                    && IsPlaced(target)
                    && _grid.GetSoldierSide(target.Soldier.Id) != shooterSide)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                foreach (RangedWeapon weapon in soldier.EquippedRangedWeapons
                    .Where(weapon => weapon.Template.IsTemplateWeapon
                        && weapon.LoadedAmmo > 0
                        && range <= weapon.Template.MaximumRange)
                    .OrderBy(weapon => weapon.Template.Id))
                {
                    IReadOnlyList<int> victimIds = ConeTemplate.GetVictimIds(
                        _grid,
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.MaximumRange,
                        weapon.Template.AreaRadius);
                    float expectedEnemyBattleValueRemoved = 0;
                    float expectedFriendlyBattleValueLost = 0;
                    foreach (int victimId in victimIds)
                    {
                        if (!_soldierMap.TryGetValue(victimId, out BattleSoldier victim))
                        {
                            continue;
                        }
                        if (!victim.CanFight)
                        {
                            // Incapacitated figures are still physically engulfed by the action,
                            // but their battle value has already been removed from the fight.
                            continue;
                        }

                        float victimRange = _grid.GetDistanceBetweenSoldiers(
                            soldier.Soldier.Id,
                            victimId);
                        float armor = victim.Armor?.Template.ArmorProvided ?? 0;
                        float woundRatio = Math.Clamp(
                            CalculateExpectedDamage(
                                weapon,
                                victimRange,
                                armor,
                                victim.Soldier.Constitution),
                            0,
                            1);
                        float expectedBattleValueRemoval = woundRatio * GetBattleValue(victim);
                        if (_grid.GetSoldierSide(victimId) == shooterSide)
                        {
                            expectedFriendlyBattleValueLost += expectedBattleValueRemoval;
                        }
                        else
                        {
                            expectedEnemyBattleValueRemoved += expectedBattleValueRemoval;
                        }
                    }

                    TemplateFiringLineEvaluation evaluation = new(
                        target,
                        weapon,
                        range,
                        victimIds,
                        expectedEnemyBattleValueRemoved,
                        expectedFriendlyBattleValueLost);
                    // A zero-value burst wastes fuel, and a negative one knowingly trades
                    // more friendly value than it removes. Neither is a viable firing line.
                    if (evaluation.Score > 0 && (best == null || evaluation.Score > best.Score))
                    {
                        best = evaluation;
                    }
                }
            }

            return best;
        }

        internal RangedTargetEvaluation EvaluateRangedTarget(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier)
        {
            var cacheKey = (
                soldier.Soldier.Id,
                target.Soldier.Id,
                weapon.Template.Id,
                BitConverter.SingleToInt32Bits(range),
                BitConverter.SingleToInt32Bits(additionalToHitModifier),
                BitConverter.SingleToInt32Bits(target.CurrentSpeed),
                (int)weapon.LoadedAmmo);
            if (_rangedEvaluationCache.TryGetValue(cacheKey, out RangedTargetEvaluation cached))
            {
                return cached;
            }

            Tuple<float, float, int> attackEstimate = EstimatePlannedRangedAttack(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier);
            float woundRatio = Math.Clamp(attackEstimate.Item2, 0, 1);
            float imminence = GetSquadImminence(soldier.BattleSquad, target.BattleSquad);
            float enemyBattleValueRemoved = imminence
                * attackEstimate.Item1
                * woundRatio
                * GetBattleValue(target);
            float friendlyBattleValueLost = CalculateExpectedFriendlyStrayCost(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                attackEstimate.Item3);

            RangedTargetEvaluation result = new RangedTargetEvaluation(
                target,
                weapon,
                range,
                attackEstimate.Item3,
                attackEstimate.Item1,
                attackEstimate.Item2,
                enemyBattleValueRemoved,
                friendlyBattleValueLost);
            _rangedEvaluationCache[cacheKey] = result;
            return result;
        }

        private IReadOnlyList<BattleSquad> GetNearestInRangeEnemySquads(BattleSoldier shooter)
        {
            float maximumRange = shooter.EquippedRangedWeapons
                .Where(weapon => weapon.LoadedAmmo > 0)
                .Select(weapon => weapon.Template.MaximumRange)
                .DefaultIfEmpty(0)
                .Max();
            if (maximumRange <= 0 || !IsPlaced(shooter)) return [];

            bool shooterSide = _grid.GetSoldierSide(shooter.Soldier.Id);
            return _grid.GetSoldierPositions().Keys
                .Where(id => id != shooter.Soldier.Id
                    && _soldierMap.ContainsKey(id)
                    && _grid.GetSoldierSide(id) != shooterSide
                    && _soldierMap[id].CanFight)
                .GroupBy(id => _soldierMap[id].BattleSquad)
                .Select(group => new
                {
                    Squad = group.Key,
                    Distance = group.Min(id => _grid.GetDistanceBetweenSoldiers(shooter.Soldier.Id, id))
                })
                .Where(candidate => candidate.Squad != null && candidate.Distance <= maximumRange)
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Squad.Id)
                .Take(RangedTargetSquadCandidateCount)
                .Select(candidate => candidate.Squad)
                .ToList();
        }

        private float CalculateExpectedFriendlyStrayCost(
            BattleSoldier shooter,
            BattleSoldier nominalTarget,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            int numberOfShots)
        {
            if (!_grid.IsTargetEngagedWithShootersAllies(
                shooter.Soldier.Id,
                nominalTarget.Soldier.Id))
            {
                return 0;
            }

            List<BattleSoldier> scrumParticipants = _grid
                .GetMeleeScrumParticipants(nominalTarget.Soldier.Id)
                .Where(_soldierMap.ContainsKey)
                .Select(id => _soldierMap[id])
                .ToList();
            bool shooterSide = _grid.GetSoldierSide(shooter.Soldier.Id);
            float expectedFriendlyLossOnStray = scrumParticipants
                .Where(participant => _grid.GetSoldierSide(participant.Soldier.Id) == shooterSide)
                .Sum(participant =>
                {
                    float victimProbability = RangedFriendlyFireRules.CalculateStrayTargetProbability(
                        participant,
                        scrumParticipants);
                    float armor = participant.Armor?.Template.ArmorProvided ?? 0;
                    float woundRatio = Math.Clamp(
                        CalculateExpectedDamage(
                            weapon,
                            range,
                            armor,
                            participant.Soldier.Constitution),
                        0,
                        1);
                    return victimProbability * woundRatio * GetBattleValue(participant);
                });

            float preRollHitTotal = CalculateRangedPreRollHitTotal(
                shooter,
                nominalTarget,
                weapon,
                range,
                additionalToHitModifier,
                numberOfShots,
                firingIntoMelee: true);
            return RangedFriendlyFireRules.CalculateNearMissProbability(preRollHitTotal)
                * expectedFriendlyLossOnStray;
        }

        internal float GetSquadImminence(BattleSquad attackerSquad, BattleSquad targetSquad)
        {
            if (attackerSquad == null || targetSquad == null) return 0;

            var cacheKey = (attackerSquad.Id, targetSquad.Id);
            if (_squadImminenceCache.TryGetValue(cacheKey, out float cached))
            {
                return cached;
            }

            float calculated = CalculateSquadImminence(attackerSquad, targetSquad);
            _squadImminenceCache[cacheKey] = calculated;
            return calculated;
        }

        private float CalculateSquadImminence(BattleSquad attackerSquad, BattleSquad targetSquad)
        {
            List<BattleSoldier> attackers = attackerSquad.AbleSoldiers.Where(IsPlaced).ToList();
            List<BattleSoldier> targets = targetSquad.AbleSoldiers.Where(IsPlaced).ToList();
            if (attackers.Count == 0 || targets.Count == 0) return 0;

            float distance = attackers
                .SelectMany(attacker => targets.Select(target =>
                    _grid.GetDistanceBetweenSoldiers(attacker.Soldier.Id, target.Soldier.Id)))
                .Min();
            float preferredRange = Math.Max(
                1,
                targetSquad.GetPreferredEngagementRange(
                    attackerSquad.GetAverageSize(),
                    attackerSquad.GetAverageArmor(),
                    attackerSquad.GetAverageConstitution(),
                    attackerSquad.GetAverageRangedEvasion()));
            float distanceToEngagement = Math.Max(0, distance - preferredRange);
            if (distanceToEngagement <= 0) return 1;

            float moveSpeed = targetSquad.GetSquadMove();
            if (moveSpeed <= 0 || float.IsInfinity(moveSpeed)) return 0;

            float turnsUntilEngagement = (float)Math.Ceiling(distanceToEngagement / moveSpeed);
            return 1f / (1f + turnsUntilEngagement);
        }

        private bool IsPlaced(BattleSoldier soldier)
        {
            return soldier != null
                && _grid.GetSoldierPositions().ContainsKey(soldier.Soldier.Id);
        }

        private static float GetBattleValue(BattleSoldier soldier)
        {
            return Math.Max(0, soldier?.Soldier?.Template?.BattleValue ?? 0);
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

        private RangedTargetEvaluation GetBestWeaponForSituation(BattleSoldier soldier, BattleSoldier target, float range, bool useBulk, bool useAccuracy)
        {
            RangedTargetEvaluation best = null;
            float bestScore = float.MinValue;
            foreach(RangedWeapon weapon in soldier.EquippedRangedWeapons.OrderByDescending(w => w.Template.DamageMultiplier))
            {
                if (weapon.Template.IsTemplateWeapon
                    || range > weapon.Template.MaximumRange
                    || weapon.LoadedAmmo <= 0)
                {
                    continue;
                }

                float bulkAndAccMod = 0;
                bulkAndAccMod -= useBulk ? weapon.Template.Bulk : 0;
                // base accuracy bonus is the weapon's accuracy plus 1 for aiming making it an all-out attack
                bulkAndAccMod += useAccuracy ? weapon.Template.Accuracy + 1 : 0;
                RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                    soldier,
                    target,
                    weapon,
                    range,
                    bulkAndAccMod);
                // if not likely to break through armor, there's little point
                if (evaluation.HitProbability > 0.1f && evaluation.Score > bestScore)
                {
                    // about a 1/10 chance of hitting
                    best = evaluation;
                    bestScore = evaluation.Score;
                }
            }
            return best;
        }

        private void AddClosingMoveToBag(BattleSoldier soldier)
        {
            float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            if (closestEnemyId == -1 || distance <= 0) return;

            Tuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
            Tuple<int, int> line = new(
                enemyPosition.Item1 - soldier.TopLeft.Item1,
                enemyPosition.Item2 - soldier.TopLeft.Item2);
            AddMoveAction(soldier, soldier.GetMoveSpeed(), line);
        }

        private Tuple<float, float, int> EstimatePlannedRangedAttack(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float moveAndAimMod)
        {
            int shotsToFire = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, (int)weapon.LoadedAmmo));
            float armor = target.Armor?.Template.ArmorProvided ?? 0;
            float con = target.Soldier.Constitution;
            float expectedDamage = CalculateExpectedDamage(weapon, range, armor, con);
            bool firingIntoMelee = _grid.IsTargetEngagedWithShootersAllies(
                soldier.Soldier.Id,
                target.Soldier.Id);
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                moveAndAimMod,
                firingIntoMelee);
            Tuple<float, float> estimate = null;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                estimate = EstimateHitAndDamage(
                    hitContext,
                    expectedDamage,
                    shotsToFire);
                int revisedShots = CalculateShotsToFire(
                    weapon,
                    estimate.Item1,
                    estimate.Item2);
                if (revisedShots == shotsToFire)
                {
                    return new Tuple<float, float, int>(
                        estimate.Item1,
                        estimate.Item2,
                        shotsToFire);
                }

                shotsToFire = revisedShots;
            }

            // Recalculate once with the final shot count so the returned probability is exactly
            // the one ShootAction will resolve, even if a future rule introduces oscillation.
            estimate = EstimateHitAndDamage(
                hitContext,
                expectedDamage,
                shotsToFire);
            return new Tuple<float, float, int>(estimate.Item1, estimate.Item2, shotsToFire);
        }

        private int CalculateShotsToFire(RangedWeapon weapon, float toHitAtPlannedRateOfFire, float damagePerShot)
        {
            int minRoF = 1;
            int maxRof = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, (int)weapon.LoadedAmmo));
            // assume all machine guns have to fire at at least 1/4 their max
            if(weapon.Template.RateOfFire > 10)
            {
                minRoF = Math.Min(weapon.Template.RateOfFire / 4, maxRof);
            }

            if (toHitAtPlannedRateOfFire < .1f)
            {
                // don't waste ammo on impossible shots
                return minRoF;
            }

            if (damagePerShot <= 0)
            {
                return minRoF;
            }

            int killRof = (int)Math.Round(1 / damagePerShot) + 1;

            return Math.Clamp(killRof, minRoF, maxRof);

        }

        private static Tuple<float, float> EstimateHitAndDamage(
            RangedHitEstimateContext hitContext,
            float expectedDamage,
            int numberOfShots)
        {
            float preRollHitTotal = hitContext.CalculatePreRollHitTotal(numberOfShots);
            float probability = GaussianCalculator.ApproximateNormalCDF(
                (preRollHitTotal - 10.5f) / 3f);
            return new Tuple<float, float>(probability, expectedDamage);
        }

        private static float CalculateRangedPreRollHitTotal(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float moveAndAimMod,
            int numberOfShots,
            bool firingIntoMelee)
        {
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                moveAndAimMod,
                firingIntoMelee);
            return hitContext.CalculatePreRollHitTotal(numberOfShots);
        }

        private void AddMoveAction(BattleSoldier soldier, float moveSpeed, Tuple<int, int> line)
        {
            Tuple<int, int> desiredMove = CalculateMovementAlongLine(line, moveSpeed);
            Tuple<int, int> newLocation = new Tuple<int, int>(soldier.TopLeft.Item1 + desiredMove.Item1, soldier.TopLeft.Item2 + desiredMove.Item2);
            newLocation = FindBestLocation(soldier.TopLeft, newLocation, moveSpeed);
            soldier.CurrentSpeed = moveSpeed;
            _grid.ReserveSpace(newLocation);
            ushort orientation = CalculateOrientationFromVector(line);
            _moveActions.Add(new MoveAction(soldier, _grid, soldier.TopLeft, newLocation, orientation));
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
            float penetratingDamage = Math.Max(0, (effectiveStrength * 4.25f) - effectiveArmor);
            return con <= 0
                ? 1
                : (penetratingDamage * weapon.Template.WoundMultiplier) / con;
        }
    }
}
