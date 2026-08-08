using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// What a melee is WORTH: the strikes a soldier would land, the battle value they would remove,
    /// what a run-in costs under fire, and what a defender gives up by shooting instead of parrying.
    ///
    /// <para>Scoring only -- it takes a <see cref="SquadPlanningServices"/> and no
    /// <see cref="ActionSink"/>, so it cannot emit an action, and it never draws from the battle
    /// RNG (see <see cref="BuildProjectedWeaponSequence"/>). Building the actual melee and charge
    /// actions is a separate concern; this is the half the engagement evaluator consults when
    /// deciding whether to close.</para>
    /// </summary>
    internal sealed class MeleeStrikeEstimator
    {
        // Shot/strike planning targets this take-out confidence before moving to the next enemy.
        private const float TargetTakeOutConfidenceThreshold = MeleeMath.TakeOutConfidenceTarget;
        // How many of the engaged squad's nearest members a would-be charger projects strikes
        // against when estimating a melee's value. A charger reaches only the front of a squad;
        // this geometry/sample bound is independent of the score currency.
        private const int EngagementMeleeTargetSampleCount = 4;
        // Cap on the number of turns of incoming fire charged against a run-in. Raised from four
        // after adding the charge-arrival discount (see EstimateChargeNet) so long charges no
        // longer get both an undiscounted payoff and an aggressively capped cost.
        private const int EngagementMaxExposureTurns = 8;
        // Enemies more than this far beyond the target contribute negligible fire during a run-in;
        // the nearest-first distance scan stops there to stay bounded in large battles. This is a
        // spatial scan bound, so the score-currency conversion does not change it.
        private const float EngagementRearThreatCutoff = 30f;

        private readonly SquadPlanningServices _services;
        // Closing cost is priced from the ENEMY's perspective -- what their guns expect to remove
        // from our charger -- so this estimator reads the same ranged scoring the shooters use.
        private readonly RangedTargetSelector _ranged;
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;

        internal MeleeStrikeEstimator(SquadPlanningServices services, RangedTargetSelector ranged)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);

        // Net outcome of a soldier charging the engaged enemy squad: the battle value his strikes
        // would remove on contact, and the friendly battle value expected to be lost crossing the
        // gap under fire. NetValue < 0 means the run-in costs more than the melee gains.
        internal readonly struct ChargeAssessment
        {
            public float MeleeBattleValue { get; }
            public float ClosingCost { get; }
            public bool ReachesContactThisTurn { get; }
            public float NetValue => MeleeBattleValue - ClosingCost;

            public ChargeAssessment(
                float meleeBattleValue,
                float closingCost,
                bool reachesContactThisTurn)
            {
                MeleeBattleValue = meleeBattleValue;
                ClosingCost = closingCost;
                ReachesContactThisTurn = reachesContactThisTurn;
            }
        }

        internal ChargeAssessment EstimateChargeNet(
            BattleSoldier soldier,
            BattleSquad targetSquad,
            float distance)
        {
            IReadOnlyList<MeleeWeapon> loadout = GetProjectedMeleeLoadout(soldier);
            if (loadout.Count == 0)
            {
                return new ChargeAssessment(0f, 0f, false);
            }

            List<BattleSoldier> reachableEnemies = targetSquad.AbleSoldiers
                .Where(IsPlaced)
                .OrderBy(enemy => _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id, enemy.Soldier.Id))
                .ThenBy(enemy => enemy.Soldier.Id)
                .Take(EngagementMeleeTargetSampleCount)
                .ToList();
            if (reachableEnemies.Count == 0)
            {
                return new ChargeAssessment(0f, 0f, false);
            }

            MeleeWeapon primary = loadout.FirstOrDefault();
            MeleeWeapon secondary = GetSecondaryMeleeWeapon(loadout);
            List<MeleeWeapon> plannedWeapons = BuildProjectedWeaponSequence(
                soldier, primary, secondary);
            List<PlannedMeleeStrike> strikePlan = BuildStrikePlan(
                soldier, reachableEnemies, plannedWeapons, didMove: true);
            float meleeBattleValue = EstimateProjectedMeleeBattleValue(
                soldier, strikePlan, plannedWeapons, didMove: true);

            float moveSpeed = soldier.GetMoveSpeed();
            // TurnsUntilWeReachTarget (attacker's own speed, distance less the 1-cell contact
            // allowance) -- see Design/Reference/EngagementScoringOverhaul.md Phase 0. This is the ONE
            // arrival discount Phase 3 kept: a charge's payoff genuinely does not exist until the
            // charger arrives, unlike a bolt, which lands the turn it is fired.
            int turnsToContact = moveSpeed <= 0
                ? int.MaxValue
                : (int)Math.Ceiling(Math.Max(0f, distance - 1f) / moveSpeed);
            // Quote future melee in the same present-value currency as ranged targeting. Contact
            // already made has full value; every turn spent closing discounts the payoff.
            float chargeArrivalDiscount = turnsToContact == int.MaxValue
                ? 0f
                : 1f / (1f + turnsToContact);
            meleeBattleValue *= chargeArrivalDiscount;
            bool reachesThisTurn = turnsToContact <= 1;
            float closingCost = EstimateClosingCost(soldier, distance, turnsToContact);
            return new ChargeAssessment(meleeBattleValue, closingCost, reachesThisTurn);
        }

        // Expected friendly battle value lost while this soldier crosses to melee: the incoming
        // ranged removal against him per turn, integrated over the (capped) number of turns the
        // run-in is exposed. Threat is evaluated at the midpoint of the approach to each shooter,
        // modeling the fact that fire grows more accurate as he closes.
        private float EstimateClosingCost(
            BattleSoldier soldier,
            float distance,
            int turnsToContact)
        {
            if (turnsToContact <= 0)
            {
                return 0f;
            }
            int exposedTurns = Math.Min(turnsToContact, EngagementMaxExposureTurns);
            float perTurnLoss = 0f;
            foreach ((int enemyId, float enemyDistance) in
                _grid.GetEnemyDistances(soldier.Soldier.Id))
            {
                if (enemyDistance > distance + EngagementRearThreatCutoff)
                {
                    // GetEnemyDistances is nearest-first; everything past here is rear-area.
                    break;
                }
                if (!_soldierMap.TryGetValue(enemyId, out BattleSoldier enemy)
                    || !enemy.IsCombatEffective)
                {
                    continue;
                }
                float threatRange = Math.Max(1f, enemyDistance * 0.5f);
                float best = 0f;
                foreach (RangedWeapon weapon in enemy.EquippedRangedWeapons)
                {
                    if (weapon.LoadedAmmo <= 0
                        || weapon.Template.IsTemplateWeapon
                        || threatRange > weapon.Template.MaximumRange)
                    {
                        continue;
                    }
                    // Enemy-perspective evaluation: ExpectedEnemyBattleValueRemoved is the battle
                    // value of *our* soldier the enemy expects to remove — exactly the run-in cost.
                    RangedTargetEvaluation eval = _ranged.EvaluateRangedTarget(
                        enemy, soldier, weapon, threatRange, -weapon.Template.Bulk);
                    if (eval.ExpectedEnemyBattleValueRemoved > best)
                    {
                        best = eval.ExpectedEnemyBattleValueRemoved;
                    }
                }
                perTurnLoss += best;
            }
            return perTurnLoss * exposedTurns;
        }

        // Deterministic sibling of BuildPlannedWeaponSequence for pure pre-move estimates: rounds
        // the fractional attack instead of drawing from the battle RNG, so assessing a hypothetical
        // charge never perturbs the seeded stream (see BattlePlanningContext's frozen-state invariant).
        internal static List<MeleeWeapon> BuildProjectedWeaponSequence(
            BattleSoldier soldier,
            MeleeWeapon primary,
            MeleeWeapon secondary)
        {
            int primaryAttackCount = (int)Math.Round(MeleeMath.CalculateBaseAttackCount(
                soldier.Soldier.AttackSpeed,
                primary?.Template.AttackSpeedMultiplier
                    ?? MeleeWeaponTemplate.DefaultAttackSpeedMultiplier));
            List<MeleeWeapon> plannedWeapons = [];
            for (int i = 0; i < primaryAttackCount; i++)
            {
                plannedWeapons.Add(primary);
            }
            if (secondary != null)
            {
                plannedWeapons.Add(secondary);
            }
            return plannedWeapons;
        }

        internal IReadOnlyList<MeleeWeapon> GetProjectedMeleeLoadout(BattleSoldier soldier)
        {
            if (soldier.EquippedMeleeWeapons.Count > 0)
            {
                return soldier.EquippedMeleeWeapons.ToList();
            }

            MeleeWeapon usableWeapon = GetFirstUsableMeleeWeapon(soldier);
            if (usableWeapon != null)
            {
                // ReadyMeleeWeaponAction currently draws the first owned weapon. Score that same
                // future state rather than treating a two-handed gunner's melee alternative as zero.
                return [usableWeapon];
            }

            MeleeWeapon unarmedWeapon = MeleeAttackAction.GetUnarmedWeapon(soldier);
            return unarmedWeapon == null ? [] : [unarmedWeapon];
        }

        internal static MeleeWeapon GetSecondaryMeleeWeapon(IReadOnlyList<MeleeWeapon> loadout)
        {
            return loadout.Count >= 2
                && loadout[0].Template.Location == EquipLocation.OneHand
                && loadout[1].Template.Location == EquipLocation.OneHand
                    ? loadout[1]
                    : null;
        }

        internal static MeleeWeapon GetFirstUsableMeleeWeapon(BattleSoldier soldier)
        {
            return soldier.MeleeWeapons.FirstOrDefault(
                weapon => (int)weapon.Template.Location <= soldier.FunctioningHands);
        }

        internal float EstimateProjectedMeleeBattleValue(
            BattleSoldier attacker,
            IReadOnlyList<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<MeleeWeapon> plannedWeapons,
            bool didMove = false)
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
                    didMove);
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
            float takeOutProbability = EstimateTakeOutOnHit(
                defender, attacker, attackingWeapon);
            return strikeCount
                * increasedHitProbability
                * takeOutProbability
                * defenderBattleValue;
        }

        internal List<PlannedMeleeStrike> BuildStrikePlan(BattleSoldier attacker,
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

        internal float EstimateTakeOutProbability(BattleSoldier attacker, BattleSoldier target, MeleeWeapon weapon, bool didMove)
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

        // PHASE 5. The graded fraction, exactly as on the ranged side. Every caller multiplies this
        // by a battle value, so leaving melee on bare take-out probability while ranged fire was
        // credited for wounding would have rigged every ranged-versus-melee comparison the planner
        // makes -- including the Hold-versus-CloseToContact decision this phase is calibrated
        // against. The two must be quoted in one currency or neither is trustworthy.
        private float EstimateTakeOutOnHit(BattleSoldier target, BattleSoldier attacker, MeleeWeapon weapon)
        {
            return RemovalMath.CalculateRemovalFractionOnHit(
                target,
                attacker.Soldier.Strength * weapon.Template.StrengthMultiplier,
                (target.Armor?.Template.ArmorProvided ?? 0)
                    * weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier);
        }
    }
}
