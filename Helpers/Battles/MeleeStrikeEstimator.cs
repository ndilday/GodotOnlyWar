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

        // Keep the engagement profile's definition of usable melee capability: an explicitly
        // armed melee soldier contributes, and an unarmed soldier contributes only when he has no
        // ranged loadout. A ranged-only soldier can still punch after contact, but that is not a
        // melee capability the formation is priced around by BattleEngagementFrameBuilder.
        private static bool IsEngagementMeleeCapable(BattleSoldier soldier) =>
            soldier?.FunctioningHands > 0
                && (soldier.MeleeWeapons.Count > 0 || soldier.RangedWeapons.Count == 0);

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

        /// <summary>
        /// Expected enemy battle value removed by one stationary melee exchange. This is the
        /// melee counterpart to <see cref="SquadPairRemovalRate.RateAtRange"/>: each attacker
        /// plans its projected strikes against the nearest contact sample, and each strike is
        /// credited with the same take-out/removal fraction used by melee action planning.
        /// </summary>
        internal float EstimateContactRemovalRate(
            BattleSquad attacker,
            BattleSquad target)
        {
            if (attacker == null || target == null)
            {
                return 0f;
            }

            return EstimateContactRemovalRate(
                attacker.AbleSoldiers
                    .Where(IsPlaced)
                    .Where(IsEngagementMeleeCapable)
                    .OrderBy(soldier => soldier.Soldier.Id)
                    .ToList(),
                target.AbleSoldiers.Where(IsPlaced).OrderBy(soldier => soldier.Soldier.Id).ToList());
        }

        /// <summary>
        /// Grid-free form used while building engagement profiles. The inputs are already the
        /// placed members of the two sides, so the same scoring calculation can be shared with
        /// the planner without giving the frame builder a targeting context or an action sink.
        /// </summary>
        internal static float EstimateContactRemovalRate(
            IReadOnlyCollection<BattleSoldier> attackers,
            IReadOnlyCollection<BattleSoldier> targets)
        {
            List<BattleSoldier> attackerList = attackers
                ?.Where(IsEngagementMeleeCapable)
                .ToList()
                ?? [];
            List<BattleSoldier> targetList = targets?.Where(soldier => soldier != null).ToList()
                ?? [];
            if (attackerList.Count == 0 || targetList.Count == 0)
            {
                return 0f;
            }

            Dictionary<int, BattleSoldier> targetMap = targetList
                .GroupBy(soldier => soldier.Soldier.Id)
                .ToDictionary(group => group.Key, group => group.First());
            float total = 0f;
            foreach (BattleSoldier attacker in attackerList.OrderBy(soldier => soldier.Soldier.Id))
            {
                List<BattleSoldier> contactTargets = targetList
                    .OrderBy(target => DistanceBetween(attacker, target))
                    .ThenBy(target => target.Soldier.Id)
                    .Take(EngagementMeleeTargetSampleCount)
                    .ToList();
                IReadOnlyList<MeleeWeapon> loadout = GetProjectedMeleeLoadoutCore(attacker);
                if (contactTargets.Count == 0 || loadout.Count == 0)
                {
                    continue;
                }

                MeleeWeapon primary = loadout[0];
                MeleeWeapon secondary = GetSecondaryMeleeWeapon(loadout);
                List<MeleeWeapon> plannedWeapons = BuildProjectedWeaponSequence(
                    attacker, primary, secondary);
                List<PlannedMeleeStrike> strikePlan = BuildStrikePlanCore(
                    attacker, contactTargets, plannedWeapons, didMove: false);
                total += EstimateProjectedMeleeBattleValueCore(
                    attacker,
                    strikePlan,
                    plannedWeapons,
                    targetMap,
                    didMove: false);
            }

            return Math.Min(total, targetList.Sum(GetBattleValue));
        }

        private static float DistanceBetween(BattleSoldier first, BattleSoldier second)
        {
            if (!first.TopLeft.HasValue || !second.TopLeft.HasValue)
            {
                return float.MaxValue;
            }

            float dx = first.TopLeft.Value.Item1 - second.TopLeft.Value.Item1;
            float dy = first.TopLeft.Value.Item2 - second.TopLeft.Value.Item2;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
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
            => GetProjectedMeleeLoadoutCore(soldier);

        private static IReadOnlyList<MeleeWeapon> GetProjectedMeleeLoadoutCore(BattleSoldier soldier)
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
            => EstimateProjectedMeleeBattleValueCore(
                attacker, strikePlans, plannedWeapons, _soldierMap, didMove);

        private static float EstimateProjectedMeleeBattleValueCore(
            BattleSoldier attacker,
            IReadOnlyList<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<MeleeWeapon> plannedWeapons,
            IReadOnlyDictionary<int, BattleSoldier> soldierMap,
            bool didMove)
        {
            Dictionary<int, float> targetSurvivalProbability = [];
            int strikeCount = Math.Min(strikePlans.Count, plannedWeapons.Count);
            for (int index = 0; index < strikeCount; index++)
            {
                PlannedMeleeStrike strike = strikePlans[index];
                if (!soldierMap.TryGetValue(strike.TargetId, out BattleSoldier target))
                {
                    continue;
                }

                float strikeTakeOutProbability = EstimateTakeOutProbabilityCore(
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
                (1 - entry.Value) * GetBattleValue(soldierMap[entry.Key]));
        }

        /// <summary>
        /// Whether a soldier caught in melee should break contact or stand and fight, for a squad
        /// under an ordered withdrawal (not a rout -- a routing soldier is pinned rather than given
        /// the choice).
        ///
        /// <para>Running is not free: he turns his back, so his guard is forfeited and he defends
        /// with foot speed alone. This weighs the worst incoming hit probability standing (parry
        /// intact) against the same standing running, alongside his own best offense, and hands the
        /// three numbers plus the squad's morale state to
        /// <see cref="MeleeDisengagementPolicy"/>. Both defensive terms are read as they would be
        /// if he STOPPED, not from his currently-flagged running state -- standing is what restores
        /// the guard his squad's declared Run took away.</para>
        /// </summary>
        internal MeleeDisengagementPolicy.Result DecideMeleeDisengagement(BattleSoldier soldier)
        {
            List<BattleSoldier> adjacentEnemies = _grid.GetAdjacentEnemies(soldier.Soldier.Id)
                .Select(enemyId => _soldierMap[enemyId])
                .Where(enemy => enemy.IsCombatEffective)
                .OrderBy(enemy => enemy.Soldier.Id)
                .ToList();
            if (adjacentEnemies.Count == 0)
            {
                return MeleeDisengagementPolicy.Evaluate(new(
                    0, 0, 0, 0, soldier.BattleSquad.MoraleState));
            }

            MeleeWeapon myWeapon = GetProjectedMeleeLoadout(soldier).FirstOrDefault()
                ?? MeleeAttackAction.GetUnarmedWeapon(soldier);
            float mySkill = myWeapon == null
                ? 0
                : soldier.Soldier.GetTotalSkillValue(myWeapon.Template.RelatedSkill);
            float myEvasion = soldier.Soldier.Template.Species.MeleeEvasion;
            // Standing restores the guard the squad's declared Run took away, so both defensive
            // terms are read as they would be if he stopped — not from his current flagged state.
            float myParryIfStanding = MeleeAttackAction.GetDefenderDefenseModifier(
                soldier,
                soldier.EquippedMeleeWeapons,
                forfeitsWeaponParry: false);
            float mySkillIfRunning = MeleeAttackAction.GetRunningDefenderMeleeSkill(soldier);

            float worstStanding = 0;
            float worstRunning = 0;
            float bestOffense = 0;
            foreach (BattleSoldier enemy in adjacentEnemies)
            {
                MeleeWeapon enemyWeapon = enemy.GetPrimaryMeleeWeapon(
                    MeleeAttackAction.GetUnarmedWeapon(enemy));
                if (enemyWeapon == null) continue;
                float enemySkill = enemy.Soldier.GetTotalSkillValue(
                    enemyWeapon.Template.RelatedSkill);
                float standing = MeleeAttackAction.EstimateHitProbability(
                    enemySkill,
                    enemyWeapon.Template.Accuracy,
                    didMove: false,
                    mySkill,
                    myEvasion,
                    myParryIfStanding);
                float running = MeleeAttackAction.EstimateHitProbability(
                    enemySkill,
                    enemyWeapon.Template.Accuracy,
                    didMove: false,
                    mySkillIfRunning,
                    myEvasion,
                    defenderDefenseModifier: 0);
                if (standing > worstStanding)
                {
                    worstStanding = standing;
                    worstRunning = running;
                }

                if (myWeapon != null)
                {
                    float offense = MeleeAttackAction.EstimateHitProbability(
                        mySkill,
                        myWeapon.Template.Accuracy,
                        didMove: false,
                        MeleeAttackAction.GetDefenderMeleeSkill(
                            enemy,
                            myWeapon.Template.RelatedSkill),
                        enemy.Soldier.Template.Species.MeleeEvasion,
                        MeleeAttackAction.GetDefenderDefenseModifier(enemy));
                    if (offense > bestOffense) bestOffense = offense;
                }
            }

            return MeleeDisengagementPolicy.Evaluate(new(
                bestOffense,
                worstStanding,
                worstRunning,
                adjacentEnemies.Count,
                soldier.BattleSquad.MoraleState));
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
            => BuildStrikePlanCore(attacker, targets, plannedWeapons, didMove);

        private static List<PlannedMeleeStrike> BuildStrikePlanCore(
            BattleSoldier attacker,
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
                    currentTarget = SelectBestMeleeTargetCore(
                        attacker, weapon, targetPool, didMove);
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

                float strikeTakeOutChance = EstimateTakeOutProbabilityCore(
                    attacker, currentTarget, weapon, didMove);
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

        private static BattleSoldier SelectBestMeleeTargetCore(
            BattleSoldier attacker,
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
                float takeOutChance = Math.Clamp(
                    hitChance * EstimateTakeOutOnHit(target, attacker, weapon), 0, 1);
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
            => EstimateTakeOutProbabilityCore(attacker, target, weapon, didMove);

        private static float EstimateTakeOutProbabilityCore(
            BattleSoldier attacker,
            BattleSoldier target,
            MeleeWeapon weapon,
            bool didMove)
        {
            float hitChance = EstimateHitProbability(attacker, target, weapon, didMove);
            return Math.Clamp(hitChance * EstimateTakeOutOnHit(target, attacker, weapon), 0, 1);
        }

        private static float EstimateHitProbability(
            BattleSoldier attacker,
            BattleSoldier target,
            MeleeWeapon weapon,
            bool didMove)
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
        private static float EstimateTakeOutOnHit(
            BattleSoldier target,
            BattleSoldier attacker,
            MeleeWeapon weapon)
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
