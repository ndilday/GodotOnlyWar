using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The Phase 4 removal-rate table (Design/Reference/BattleLogic.md): for one
    /// shooter squad, the expected enemy battle value it removes per turn against each enemy squad,
    /// captured once at a reference posture and rescalable to any projected range in closed form.
    ///
    /// <para>WHY IT IS ITS OWN THING. The engagement potential (<see cref="EngagementPotential"/>)
    /// prices a posture by asking "what would both sides remove per turn at the range this option
    /// leads to?" (the bounded policy rollout this was first built for asked the same question N
    /// turns ahead; that rollout has since been removed). Answering that
    /// by re-running target selection at every projected range would be ruinous, so each shooter's
    /// best shot is evaluated ONCE at a stationary, un-aimed, no-bulk reference posture, and
    /// <see cref="PairRemovalTerm"/> carries enough of that shot (pre-roll to-hit total, shot count,
    /// reference range and target speed, the K_loc vector for a degrading weapon) to re-derive the
    /// rate at any other range analytically. This class builds those terms; SquadPairRemovalRate
    /// aggregates them.</para>
    ///
    /// <para>Scoring only -- services and ranged selection in, no <see cref="ActionSink"/>. Results
    /// are memoized per shooter squad in the shared <see cref="BattlePlanningContext"/>, which is
    /// what makes it affordable for every option, projected state and parallel squad job to
    /// consult.</para>
    /// </summary>
    internal sealed class PairRemovalRateTable
    {
        private static readonly IReadOnlyDictionary<int, SquadPairRemovalRate>
            EmptyPairRemovalRates = new Dictionary<int, SquadPairRemovalRate>();

        /// <summary>
        /// Stands in for "this weapon does not roll to hit". Far enough above
        /// <see cref="RemovalMath.HitRollMean"/> that the burst model's first-shot threshold is met with
        /// certainty at any range and under any bulk multiplier, but finite, so the shared
        /// rescaling arithmetic applies to it unchanged.
        /// </summary>
        private const float ConeCertainHitTotal = 1_000f;

        private readonly SquadPlanningServices _services;
        private readonly RangedTargetSelector _ranged;
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly BattlePlanningContext _context;

        internal PairRemovalRateTable(SquadPlanningServices services, RangedTargetSelector ranged)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
            _context = _services.Context;
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);

        /// <summary>
        /// Returns, for one shooter squad, the per-enemy-squad removal rates -- expected enemy
        /// battle value removed per turn, in the SAME currency as `outgoing`, rescalable to any
        /// projected range in closed form. Memoized for the turn in the shared
        /// <see cref="BattlePlanningContext"/>, so repeated requests across options, projected
        /// states and worker planners cost one build.
        ///
        /// <para>PHASE 5 WIRED THIS INTO PLANNING. <c>AggregateRemovalRate</c> is gone; the
        /// exchange-rate model (<see cref="EngagementExchangeModel"/>) reads this table for both
        /// the outgoing and incoming halves, and <see cref="EngagementPotential"/> evaluates those
        /// rates at the current and projected separations. That is what puts immediate fire and
        /// future position value in one currency. (Phase 5 originally fed the plies and depth-0
        /// terminal of a bounded policy rollout, since removed.) See
        /// <see cref="SquadPairRemovalRate"/> for the aggregation semantics.</para>
        /// </summary>
        internal IReadOnlyDictionary<int, SquadPairRemovalRate> GetPairRemovalRates(
            BattleSquad shooterSquad)
        {
            if (shooterSquad == null)
            {
                return EmptyPairRemovalRates;
            }
            if (_context.PairRemovalRates.TryGetValue(
                shooterSquad.Id,
                out IReadOnlyDictionary<int, SquadPairRemovalRate> cached))
            {
                return cached;
            }
            IReadOnlyDictionary<int, SquadPairRemovalRate> built =
                BuildPairRemovalRates(shooterSquad);
            // GetOrAdd rather than an indexer assignment: a concurrent miss must resolve to a
            // single shared instance so reference identity is stable, the way the other context
            // caches behave. The builder is pure, so the losing duplicate is discarded harmlessly.
            return _context.PairRemovalRates.GetOrAdd(shooterSquad.Id, built);
        }

        private IReadOnlyDictionary<int, SquadPairRemovalRate> BuildPairRemovalRates(
            BattleSquad shooterSquad)
        {
            Dictionary<int, List<PairRemovalTerm>> termsByTargetSquad = [];
            Dictionary<int, List<ShooterGroup>> groupsByTargetSquad = [];
            foreach (BattleSoldier soldier in shooterSquad.AbleSoldiers
                .OrderBy(member => member.Soldier.Id))
            {
                if (!IsPlaced(soldier) || soldier.EquippedRangedWeapons.Count == 0)
                {
                    continue;
                }
                // Stationary, un-aimed, no-bulk reference posture -- see SquadPairRemovalRate.
                // Every EvaluateRangedTarget this walks is already memoized in the shared context.
                RangedTargetEvaluation evaluation = _ranged.SelectBestRangedTarget(
                    soldier, bulkMultiplier: 0f)
                    // PHASE 5. SelectBestRangedTarget only considers enemies inside weapon reach,
                    // so a squad that is currently out of range would get an EMPTY row and the
                    // potential would price every future turn at 0 -- no reason to ever close, at
                    // any distance. The old capability proxy did not have that hole: it recomputed
                    // its range factor at the PROJECTED range and became positive as soon as the
                    // squads came inside reach. Capturing a term against the nearest enemy anyway
                    // restores exactly that gradient honestly -- PairRemovalTerm gates the rate to
                    // 0 beyond MaximumEffectiveRange, so this contributes nothing until the
                    // potential projects the squads into range, and then contributes the real
                    // hit x removal x BV at that projected range.
                    ?? EvaluateNearestOutOfReachTarget(soldier);
                // A CONE BEARER IS A SHOOTER. Both target selectors above skip
                // IsTemplateWeapon, so a soldier whose only weapon is a flamer used to
                // contribute NO term and his squad's whole row read rate 0 at every range --
                // the squad was modelled as unarmed. Everything downstream then followed from
                // that: the arrival-time term (EvaluateArrivalTimeValue, since removed with the
                // rollout) saw an outgoing rate of 0 where the squad
                // stood and paid it to run to contact, so a flamer bearer burning a target for
                // 0.775 battle value at 10 yards abandoned the burst to charge (both
                // template-weapon planner tests, 2026-08-07).
                //
                // Take whichever branch the live planner would take -- PlanRangedAction gives
                // the cone ties, so this does too -- and price the burst the way the cone
                // actually resolves: one application per victim, no to-hit roll.
                TemplateFiringLineEvaluation coneLine = HasReadyTemplateWeapon(soldier)
                    ? _ranged.SelectBestTemplateFiringLine(soldier)
                    : null;
                if (coneLine != null
                    && coneLine.Score >= (evaluation?.Score ?? float.MinValue))
                {
                    AddConeRemovalTerms(soldier, coneLine, termsByTargetSquad);
                    continue;
                }
                if (evaluation?.Target == null
                    || evaluation.Weapon == null
                    || evaluation.Target.BattleSquad == null)
                {
                    continue;
                }
                int targetSquadId = evaluation.Target.BattleSquad.Id;
                if (!groupsByTargetSquad.TryGetValue(targetSquadId, out List<ShooterGroup> groups))
                {
                    groups = [];
                    groupsByTargetSquad[targetSquadId] = groups;
                }
                ShooterGroup candidate = new(soldier, evaluation);
                ShooterGroup match = GroupShooters
                    ? groups.Find(group => group.Accepts(candidate))
                    : null;
                if (match == null)
                {
                    groups.Add(candidate);
                }
                else
                {
                    match.Add(candidate);
                }
            }

            foreach ((int targetSquadId, List<ShooterGroup> groups) in groupsByTargetSquad)
            {
                if (!termsByTargetSquad.TryGetValue(targetSquadId, out List<PairRemovalTerm> terms))
                {
                    terms = [];
                    termsByTargetSquad[targetSquadId] = terms;
                }
                foreach (ShooterGroup group in groups)
                {
                    terms.Add(BuildPairRemovalTerm(
                        group.Shooter,
                        group.Evaluation,
                        group.Count,
                        group.Count == 1 ? null : group.TargetShares()));
                }
            }

            Dictionary<int, SquadPairRemovalRate> rates = [];
            foreach ((int targetSquadId, List<PairRemovalTerm> terms) in termsByTargetSquad)
            {
                rates[targetSquadId] = new SquadPairRemovalRate(
                    shooterSquad.Id, targetSquadId, terms);
            }
            return rates;
        }

        /// <summary>
        /// PHASE 5. The reference shot a soldier with no enemy inside reach WOULD take against the
        /// nearest enemy, evaluated at the current (out-of-reach) separation. Its rate is 0 today
        /// -- <see cref="PairRemovalTerm.MaximumEffectiveRange"/> sees to that -- and becomes real
        /// the moment the potential projects the squads inside reach. Longest-reaching loaded
        /// weapon, because that is the one that decides when the squad can start shooting.
        /// </summary>
        private RangedTargetEvaluation EvaluateNearestOutOfReachTarget(BattleSoldier soldier)
        {
            RangedWeapon weapon = soldier.EquippedRangedWeapons
                .Where(candidate => candidate.LoadedAmmo > 0
                    && !candidate.Template.IsTemplateWeapon)
                .OrderByDescending(candidate => BattleModifiersUtil.GetEffectiveMaxRange(
                    soldier.Soldier, candidate.Template))
                .ThenBy(candidate => candidate.Template.Id)
                .FirstOrDefault();
            if (weapon == null)
            {
                return null;
            }

            BattleSoldier nearest = null;
            float nearestDistance = float.MaxValue;
            foreach ((int enemyId, float distance) in
                _grid.GetEnemyDistances(soldier.Soldier.Id))
            {
                if (!_soldierMap.TryGetValue(enemyId, out BattleSoldier enemy)
                    || !enemy.IsCombatEffective
                    || enemy.BattleSquad == null
                    || !IsPlaced(enemy))
                {
                    continue;
                }
                if (distance < nearestDistance
                    || (distance == nearestDistance
                        && (nearest == null || enemyId < nearest.Soldier.Id)))
                {
                    nearest = enemy;
                    nearestDistance = distance;
                }
            }
            return nearest == null
                ? null
                : _ranged.EvaluateRangedTarget(soldier, nearest, weapon, nearestDistance, 0f);
        }

        private static bool HasReadyTemplateWeapon(BattleSoldier soldier)
        {
            IReadOnlyList<RangedWeapon> equipped = soldier.EquippedRangedWeapons;
            for (int index = 0; index < equipped.Count; index++)
            {
                if (equipped[index].Template.IsConeWeapon && equipped[index].LoadedAmmo > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// One removal term per enemy the chosen firing line engulfs, filed under that victim's own
        /// squad -- a cone crossing two squads genuinely removes value from both, and the table is
        /// keyed by target squad.
        ///
        /// <para>Friendly victims are dropped rather than netted out. This table is the OUTGOING
        /// half only; the blue-on-blue cost of a burst already lives in the immediate term's
        /// <c>ExpectedFriendlyBattleValueLost</c>, and a negative entry in a cell that means "what I
        /// remove from THAT squad" would be read as enemy removal by every consumer.</para>
        /// </summary>
        private void AddConeRemovalTerms(
            BattleSoldier shooter,
            TemplateFiringLineEvaluation line,
            Dictionary<int, List<PairRemovalTerm>> termsByTargetSquad)
        {
            bool shooterSide = _grid.GetSoldierSide(shooter.Soldier.Id);
            foreach (int victimId in line.VictimIds)
            {
                if (!_soldierMap.TryGetValue(victimId, out BattleSoldier victim)
                    || !victim.IsCombatEffective
                    || victim.BattleSquad == null
                    || _grid.GetSoldierSide(victimId) == shooterSide)
                {
                    continue;
                }
                float victimRange = _grid.GetDistanceBetweenSoldiers(
                    shooter.Soldier.Id, victimId);
                int targetSquadId = victim.BattleSquad.Id;
                if (!termsByTargetSquad.TryGetValue(
                    targetSquadId, out List<PairRemovalTerm> terms))
                {
                    terms = [];
                    termsByTargetSquad[targetSquadId] = terms;
                }
                terms.Add(BuildConeRemovalTerm(shooter, line.Weapon, victim, victimRange));
            }
        }

        /// <summary>
        /// A cone's per-victim term. Structurally a <see cref="PairRemovalTerm"/> like any other so
        /// the whole rescaling path is shared, but with the to-hit half neutralised: a template
        /// weapon engulfs its area rather than rolling against a target, so the reference to-hit
        /// total is pinned above every threshold in the burst model and the shot count is one.
        /// <see cref="PairRemovalTerm.MaximumEffectiveRange"/> still gates the term to 0 beyond the
        /// weapon's reach, which is what keeps the closing gradient honest.
        /// </summary>
        private static PairRemovalTerm BuildConeRemovalTerm(
            BattleSoldier shooter,
            RangedWeapon weapon,
            BattleSoldier victim,
            float victimRange)
        {
            RangedWeaponTemplate template = weapon.Template;
            float armor = victim.Armor?.Template.ArmorProvided ?? 0f;
            IReadOnlyList<TakeOutLocationTerm> takeOutTerms =
                template.DoesDamageDegradeWithRange
                    ? RemovalMath.BuildTakeOutLocationTerms(
                        victim, armor, template.ArmorMultiplier, template.WoundMultiplier)
                    : null;
            (float takeOut, float progress) = RangedShotEvaluator.CalculateRangedHitRemoval(
                victim, weapon, victimRange, armor);
            return new PairRemovalTerm(
                shooter.Soldier.Id,
                victim.Soldier.Id,
                template,
                ConeCertainHitTotal,
                1,
                victimRange,
                // No to-hit roll means no speed penalty to capture, and a zero reference speed
                // keeps the range rescaling in HitTotalAt on the same footing as the capture.
                0f,
                Math.Clamp(takeOut, 0f, 1f),
                Math.Clamp(progress, 0f, 1f),
                GetBattleValue(victim),
                BattleModifiersUtil.GetEffectiveMaxRange(shooter.Soldier, template),
                takeOutTerms);
        }

        private PairRemovalTerm BuildPairRemovalTerm(
            BattleSoldier shooter,
            RangedTargetEvaluation evaluation,
            int count = 1,
            IReadOnlyList<(int TargetId, int Shooters)> targetShares = null)
        {
            RangedWeaponTemplate template = evaluation.Weapon.Template;
            // The aimed shots the shot planner weighs against firing now, through the same
            // evaluator and with the same bonus (RangedTargetSelector.EvaluateFireTiming), so each
            // carries the burst the planner would really choose for it. The term picks among them
            // with the planner's own rule at every range (PairRemovalTerm.SustainedRateAt).
            //
            // Every plan's burst is chosen at the one round price the rule charges them all
            // (RangedTargetSelector.StandingRoundPrice, 2026-10-11), and the term keeps the
            // scarcity behind it. The standing un-aimed shot is one of the plans; the reference
            // shot keeps the burst it was evaluated with, because RemovalAt (fire on the move,
            // where no aim is on offer) reads it.
            List<AimPlan> aimPlans = [];
            AimPlan? standingShot = null;
            float scarcity = 0f;
            if (!template.IsTemplateWeapon)
            {
                float referenceModifier = evaluation.AdditionalToHitModifier;
                float roundPrice = _ranged.StandingRoundPrice(
                    shooter, evaluation.Target, evaluation.Weapon, evaluation.Range,
                    referenceModifier, evaluation.TargetSpeed, out scarcity);
                RangedTargetEvaluation standing = _ranged.EvaluateRangedTarget(
                    shooter,
                    evaluation.Target,
                    evaluation.Weapon,
                    evaluation.Range,
                    referenceModifier,
                    evaluation.TargetSpeed,
                    roundPrice: roundPrice);
                standingShot = new AimPlan(standing.PreRollHitTotal, standing.ShotsToFire, 1);
                for (int bonus = 0; bonus <= RangedTargetSelector.FullAimBonusTurns; bonus++)
                {
                    RangedTargetEvaluation aimed = _ranged.EvaluateRangedTarget(
                        shooter,
                        evaluation.Target,
                        evaluation.Weapon,
                        evaluation.Range,
                        RangedTargetSelector.AimAccuracy(shooter, template) + bonus + 1f,
                        evaluation.TargetSpeed,
                        roundPrice: roundPrice);
                    aimPlans.Add(new AimPlan(aimed.PreRollHitTotal, aimed.ShotsToFire, bonus + 2));
                }
            }
            RangedWeapon weapon = evaluation.Weapon;
            int? fixedRounds = weapon.IsUnlimited || weapon.IsSelfRegenerating
                ? 0
                : weapon.IsConsumableItem ? 1 : null;
            float wornArmor = evaluation.Target.Armor?.Template.ArmorProvided ?? 0f;
            // Only a degrading weapon needs the K_loc vector. For a non-degrading weapon
            // CalculateDamageAtRange is a flat DamageMultiplier, so takeOut0 IS takeOut(r) for
            // every r -- exact, not an approximation.
            IReadOnlyList<TakeOutLocationTerm> takeOutTerms =
                template.DoesDamageDegradeWithRange
                    ? RemovalMath.BuildTakeOutLocationTerms(
                        evaluation.Target, wornArmor, template.ArmorMultiplier, template.WoundMultiplier)
                    : null;
            return new PairRemovalTerm(
                shooter.Soldier.Id,
                evaluation.Target.Soldier.Id,
                template,
                evaluation.PreRollHitTotal,
                evaluation.ShotsToFire,
                evaluation.Range,
                evaluation.TargetSpeed,
                Math.Clamp(evaluation.TakeOutProbabilityOnHit, 0f, 1f),
                Math.Clamp(evaluation.WoundProgressOnHit, 0f, 1f),
                GetBattleValue(evaluation.Target),
                BattleModifiersUtil.GetEffectiveMaxRange(shooter.Soldier, template),
                takeOutTerms,
                aimPlans,
                scarcity,
                fixedRounds,
                standingShot)
            {
                CountsRounds = !RangedTargetSelector.IsNeverScarce(weapon),
                // The inputs of the scarcity, so the term can price a round at other ranges.
                ScarcityRounds = template.IsTemplateWeapon || RangedTargetSelector.IsNeverScarce(weapon)
                    ? float.NaN
                    : RangedTargetSelector.RoundsCarried(weapon),
                ScarcityWork = template.IsTemplateWeapon || RangedTargetSelector.IsNeverScarce(weapon)
                    ? float.NaN
                    : _ranged.WorkPerShooter(shooter, weapon),
                Count = count,
                TargetShares = targetShares,
            };
        }

        /// <summary>
        /// Whether interchangeable shooters share one <see cref="PairRemovalTerm"/> (true since
        /// 2026-11-11, step 2 of Design/Active/CoarseForecastTables.md). Process-wide so the
        /// diagnostics can compare (DUEL_GROUP_SHOOTERS=0).
        /// </summary>
        internal static bool GroupShooters = true;

        /// <summary>
        /// Shooters of one squad whose best shots land in the same enemy squad and are close enough
        /// to price as copies of the first one's (soldier-id order, as
        /// <see cref="RangedEffectivenessCurve"/> buckets its shooters).
        ///
        /// <para>WHY (2026-11-11). Each term carries its own removal-fraction table and cycle memo,
        /// and the terms are built again every turn, so almost all the removal-fraction solves that
        /// remained after step 1 were the same nodes solved again for each of ten soldiers with the
        /// same lasgun aimed at the same kind of target. The forecast only ranks a few options, and
        /// a squad's rate is a sum, so pricing ten copies of one shot ten times buys nothing.</para>
        ///
        /// <para>TOLERANCES (judgment calls, 2026-11-11). Same weapon template and the same burst
        /// (the reference shot's <c>ShotsToFire</c>). Pre-roll to-hit totals within
        /// <see cref="HitTotalTolerance"/>: the CDF's slope peaks at 0.133 a point, so half a point
        /// moves a hit chance by at most 0.067, and the representative is the first member, so the
        /// error has no fixed sign. Ranges within <see cref="RangeTolerance"/> of each other, so
        /// the range rescaling, which works in ln(range), does not spread the members apart when
        /// the forecast projects them close. The same target profile: take-out and wound fill per
        /// hit each within 0.02, target battle value within 5%, the same reach to the yard, the same
        /// aim accuracy, and for weapons that run down, rounds carried within 25%.</para>
        /// </summary>
        private sealed class ShooterGroup
        {
            private const float HitTotalTolerance = 0.5f;
            private const float RangeTolerance = 0.1f;
            private const float MinimumRangeTolerance = 5f;
            private const float RemovalPerHitTolerance = 0.02f;
            private const float BattleValueTolerance = 0.05f;
            private const float ReachTolerance = 1f;
            private const float AimAccuracyTolerance = 0.25f;
            private const float RoundsTolerance = 0.25f;

            internal BattleSoldier Shooter { get; }
            internal RangedTargetEvaluation Evaluation { get; }
            internal int Count { get; private set; } = 1;

            private readonly float _targetBattleValue;
            private readonly float _reach;
            private readonly float _aimAccuracy;
            private readonly float _rounds;
            private readonly List<(int TargetId, int Shooters)> _targets;

            internal ShooterGroup(BattleSoldier shooter, RangedTargetEvaluation evaluation)
            {
                Shooter = shooter;
                Evaluation = evaluation;
                RangedWeaponTemplate template = evaluation.Weapon.Template;
                _targetBattleValue = GetBattleValue(evaluation.Target);
                _reach = BattleModifiersUtil.GetEffectiveMaxRange(shooter.Soldier, template);
                _aimAccuracy = template.IsTemplateWeapon
                    ? 0f
                    : RangedTargetSelector.AimAccuracy(shooter, template);
                _rounds = RangedTargetSelector.IsNeverScarce(evaluation.Weapon)
                    ? float.NaN
                    : RangedTargetSelector.RoundsCarried(evaluation.Weapon);
                _targets = [(evaluation.Target.Soldier.Id, 1)];
            }

            internal bool Accepts(ShooterGroup other)
            {
                RangedTargetEvaluation mine = Evaluation;
                RangedTargetEvaluation theirs = other.Evaluation;
                if (!ReferenceEquals(mine.Weapon.Template, theirs.Weapon.Template)
                    || mine.ShotsToFire != theirs.ShotsToFire
                    || RangedTargetSelector.IsNeverScarce(mine.Weapon)
                        != RangedTargetSelector.IsNeverScarce(theirs.Weapon)
                    || mine.Weapon.IsConsumableItem != theirs.Weapon.IsConsumableItem
                    || Math.Abs(mine.PreRollHitTotal - theirs.PreRollHitTotal) > HitTotalTolerance
                    || Math.Abs(mine.TargetSpeed - theirs.TargetSpeed) > HitTotalTolerance
                    || Math.Abs(mine.Range - theirs.Range)
                        > Math.Max(MinimumRangeTolerance, RangeTolerance * Math.Min(mine.Range, theirs.Range))
                    || Math.Abs(mine.TakeOutProbabilityOnHit - theirs.TakeOutProbabilityOnHit)
                        > RemovalPerHitTolerance
                    || Math.Abs(mine.WoundProgressOnHit - theirs.WoundProgressOnHit)
                        > RemovalPerHitTolerance
                    || Math.Abs(_targetBattleValue - other._targetBattleValue)
                        > BattleValueTolerance * Math.Max(_targetBattleValue, other._targetBattleValue)
                    || Math.Abs(_reach - other._reach) > ReachTolerance
                    || Math.Abs(_aimAccuracy - other._aimAccuracy) > AimAccuracyTolerance)
                {
                    return false;
                }
                if (float.IsNaN(_rounds))
                {
                    return true;
                }
                return Math.Abs(_rounds - other._rounds)
                    <= RoundsTolerance * Math.Max(_rounds, other._rounds);
            }

            internal void Add(ShooterGroup other)
            {
                Count++;
                int targetId = other.Evaluation.Target.Soldier.Id;
                int index = _targets.FindIndex(share => share.TargetId == targetId);
                if (index < 0)
                {
                    _targets.Add((targetId, 1));
                }
                else
                {
                    _targets[index] = (targetId, _targets[index].Shooters + 1);
                }
            }

            internal IReadOnlyList<(int TargetId, int Shooters)> TargetShares() => _targets;
        }
    }
}
