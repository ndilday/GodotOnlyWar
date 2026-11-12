using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// What a melee is WORTH: the strikes a soldier would land, the battle value they would remove,
    /// and what a defender gives up by shooting instead of parrying. What a run-in costs under fire
    /// is not priced here: the exchange forecast's Close continuation in
    /// <see cref="EngagementPotential"/> prices the crossing, with the enemy's fire shared across
    /// our squads.
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
        // Contact frontage, measured 2026-10-23 with ForecastAgainstBattleDiagnostics (FRONTAGE
        // table, Design/Active/ContactRateCalibration.md): an outnumbering side strikes with about
        // 1.2-1.7 soldiers per defender standing (Hormagaunts and an Ork Mob alike), and a striker
        // reaches fewer than one distinct defender a turn (0.4-0.8), however many attacks he has.
        internal const float ContactStrikersPerDefender = 1.5f;

        private readonly SquadPlanningServices _services;
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;

        internal MeleeStrikeEstimator(SquadPlanningServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
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

        /// <summary>
        /// Expected enemy battle value removed by one stationary melee exchange. This is the
        /// melee counterpart to <see cref="SquadPairRemovalRate.RateAtRange"/>: each attacker
        /// plans its projected strikes against the nearest contact sample, and each strike is
        /// credited with the same take-out/removal fraction used by melee action planning.
        /// </summary>
        internal ContactStrikes EstimateContactStrikes(
            BattleSquad attacker,
            BattleSquad target)
        {
            if (attacker == null || target == null)
            {
                return ContactStrikes.None;
            }

            return EstimateContactStrikes(
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
        ///
        /// <para>Frontage: a melee is not every attacker striking at once. Each attacker's turn of
        /// strikes is priced against one defender, his nearest (a striker reaches fewer than one
        /// distinct defender a turn in battle), as a take-out probability <c>t</c>. Only the
        /// strongest <see cref="ContactStrikersPerDefender"/> times the defenders' number engage,
        /// each against a defender met at random, so a defender survives the strikers that met him
        /// and the share removed is <c>1 - exp(-sum(t) / D)</c>. Measured 2026-10-23: that random
        /// contact predicts the defenders struck a turn (9.5 against 8.2 for Genestealers on PDF,
        /// 21.6 against 20.8 for Hormagaunts), and it is smooth in both sides' numbers. The same rule
        /// prices a defending line's strikes back.</para>
        /// </summary>
        internal static float EstimateContactRemovalRate(
            IReadOnlyCollection<BattleSoldier> attackers,
            IReadOnlyCollection<BattleSoldier> targets) =>
            EstimateContactStrikes(attackers, targets).RemovalRate();

        /// <summary>
        /// The parts of <see cref="EstimateContactRemovalRate(IReadOnlyCollection{BattleSoldier}, IReadOnlyCollection{BattleSoldier})"/>
        /// that do not depend on how the two sides' squads share the melee: each attacker's take-out
        /// probability against one defender in a turn, strongest first, and the defenders' number
        /// and value.
        /// </summary>
        internal static ContactStrikes EstimateContactStrikes(
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
                return ContactStrikes.None;
            }

            Dictionary<int, BattleSoldier> targetMap = targetList
                .GroupBy(soldier => soldier.Soldier.Id)
                .ToDictionary(group => group.Key, group => group.First());
            List<float> takeOuts = [];
            foreach (BattleSoldier attacker in attackerList.OrderBy(soldier => soldier.Soldier.Id))
            {
                BattleSoldier contactTarget = targetList
                    .OrderBy(target => DistanceBetween(attacker, target))
                    .ThenBy(target => target.Soldier.Id)
                    .First();
                IReadOnlyList<MeleeWeapon> loadout = GetProjectedMeleeLoadoutCore(attacker);
                float targetValue = GetBattleValue(contactTarget);
                if (loadout.Count == 0 || targetValue <= 0)
                {
                    continue;
                }

                MeleeWeapon primary = loadout[0];
                MeleeWeapon secondary = GetSecondaryMeleeWeapon(loadout);
                List<MeleeWeapon> plannedWeapons = BuildProjectedWeaponSequence(
                    attacker, primary, secondary);
                List<PlannedMeleeStrike> strikePlan = BuildStrikePlanCore(
                    attacker, [contactTarget], plannedWeapons, didMove: false);
                float takeOut = EstimateProjectedMeleeBattleValueCore(
                    attacker,
                    strikePlan,
                    plannedWeapons,
                    targetMap,
                    didMove: false) / targetValue;
                takeOuts.Add(Math.Clamp(takeOut, 0f, 1f));
            }

            return new ContactStrikes(
                takeOuts.OrderByDescending(value => value).ToArray(),
                targetList.Count,
                targetList.Sum(GetBattleValue));
        }

        /// <summary>
        /// Chance that one turn of this soldier's strikes, all at <paramref name="target"/>, takes
        /// him out. No battle value goes in: the tactical value table
        /// (<see cref="TacticalValueTable"/>) reads it. Any soldier with a functioning hand
        /// strikes, with his weapons or unarmed.
        /// </summary>
        internal static float EstimateTurnTakeOutProbability(
            BattleSoldier attacker,
            BattleSoldier target)
        {
            if (attacker?.FunctioningHands is not > 0 || target == null)
            {
                return 0f;
            }
            IReadOnlyList<MeleeWeapon> loadout = GetProjectedMeleeLoadoutCore(attacker);
            if (loadout.Count == 0)
            {
                return 0f;
            }
            List<MeleeWeapon> plannedWeapons = BuildProjectedWeaponSequence(
                attacker, loadout[0], GetSecondaryMeleeWeapon(loadout));
            float survival = 1f;
            foreach (MeleeWeapon weapon in plannedWeapons)
            {
                survival *= 1f - EstimateTakeOutProbabilityCore(
                    attacker, target, weapon, didMove: false);
            }
            return Math.Clamp(1f - survival, 0f, 1f);
        }

        /// <summary>
        /// One squad's melee against another, before the frontage is applied.
        /// </summary>
        internal readonly record struct ContactStrikes(
            float[] TakeOuts,
            int Defenders,
            float DefenderValue)
        {
            internal static ContactStrikes None { get; } = new([], 0, 0f);

            internal int Attackers => TakeOuts?.Length ?? 0;

            /// <summary>
            /// Battle value removed a turn. <paramref name="attackerShare"/> is the share of the
            /// attacking squad that fights these defenders, <paramref name="frontageShare"/> the
            /// share of the defenders' frontage left to it when other squads strike the same
            /// defenders, and <paramref name="defenderShare"/> the share of the defending squad it
            /// fights (a force share: its friends fight the rest).
            ///
            /// <para><paramref name="otherTakeOuts"/> is the <see cref="EngagedTakeOuts"/> of the
            /// other squads striking the same defenders: a defender met by strikers of two squads
            /// is taken out once, so the defenders' loss is priced for all of them together and this
            /// squad is credited its share of it.</para>
            /// </summary>
            internal float RemovalRate(
                float attackerShare = 1f,
                float frontageShare = 1f,
                float defenderShare = 1f,
                float otherTakeOuts = 0f)
            {
                float defenders = Defenders * defenderShare;
                float takeOuts = EngagedTakeOuts(attackerShare, frontageShare, defenderShare);
                if (takeOuts <= 0 || defenders <= 0)
                {
                    return 0f;
                }
                float allTakeOuts = takeOuts + Math.Max(0f, otherTakeOuts);
                return DefenderValue * defenderShare * (1 - (float)Math.Exp(-allTakeOuts / defenders))
                    * (takeOuts / allTakeOuts);
            }

            /// <summary>
            /// The summed take-out probabilities of the attackers that engage: the strongest first,
            /// up to the frontage, a share of a soldier for that share.
            /// </summary>
            internal float EngagedTakeOuts(
                float attackerShare = 1f,
                float frontageShare = 1f,
                float defenderShare = 1f)
            {
                float defenders = Defenders * defenderShare;
                if (Attackers == 0 || defenders <= 0)
                {
                    return 0f;
                }
                float engaging = Math.Min(
                    attackerShare * Attackers,
                    ContactStrikersPerDefender * defenders * frontageShare);
                float takeOuts = 0f;
                foreach (float takeOut in TakeOuts)
                {
                    if (engaging <= 0)
                    {
                        break;
                    }
                    takeOuts += takeOut * Math.Min(1f, engaging);
                    engaging -= 1f;
                }
                return takeOuts;
            }
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

        /// <summary>
        /// The battle value this soldier's strikes would remove from <paramref name="targetSquad"/>
        /// in one exchange, undiscounted. The caller credits it only when the squad is already in
        /// contact at turn start: attacks resolve from turn-start geometry and movement after them
        /// (TDD §6.6), so a soldier who arrives in contact this turn strikes next turn, and that
        /// future melee is priced by the potential's contact rates, not here.
        /// </summary>
        internal float EstimateMeleeInContact(BattleSoldier soldier, BattleSquad targetSquad)
        {
            IReadOnlyList<MeleeWeapon> loadout = GetProjectedMeleeLoadout(soldier);
            if (loadout.Count == 0)
            {
                return 0f;
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
                return 0f;
            }

            MeleeWeapon primary = loadout.FirstOrDefault();
            MeleeWeapon secondary = GetSecondaryMeleeWeapon(loadout);
            List<MeleeWeapon> plannedWeapons = BuildProjectedWeaponSequence(
                soldier, primary, secondary);
            List<PlannedMeleeStrike> strikePlan = BuildStrikePlan(
                soldier, reachableEnemies, plannedWeapons, didMove: true);
            return EstimateProjectedMeleeBattleValue(
                soldier, strikePlan, plannedWeapons, didMove: true);
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
                target.Armor?.Template.ArmorProvided ?? 0,
                weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier);
        }
    }
}
