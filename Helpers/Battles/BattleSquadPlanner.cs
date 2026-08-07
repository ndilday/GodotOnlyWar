using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquadPlanner
    {
        private const float TargetTakeOutConfidenceThreshold = MeleeMath.TakeOutConfidenceTarget;
        private const int RangedTargetSquadCandidateCount = 3;
        // TUNABLE: the grenade is a sidearm, not the main gun. A blast throw must beat the
        // soldier's best conventional action (rifle shot or cone burst) by more than this
        // expected-battle-value margin before it is chosen. Retained after the take-out-
        // probability conversion: focused tests still separate lone targets, clusters, and
        // danger-close throws at this margin.
        private const float BlastOverConventionalScoreMargin = 0.25f;
        private const float WalkSpeedMultiplier = 0.2f;
        // Internal so the resolver's pursuit projections use the same jog speed the planner will
        // actually move at. They disagreed (0.66 vs 0.5) until 2026-07-30, so the posture decision
        // was predicting a jog a third faster than the one it got.
        internal const float JogSpeedMultiplier = 0.5f;
        private const float WalkBulkMultiplier = 0.5f;
        private const float FullBulkMultiplier = 1f;
        // Length the squad rout heading is normalized to. Long enough that no rout is ever capped
        // by the line itself (CalculateMovementAlongLine treats a line shorter than the move budget
        // as a destination), short enough that its squared length stays well inside int range.
        private const int RoutLineLength = 1_000;
        private const float WalkAimMultiplier = 0.5f;
        // Blast planning integrates enemy AND friendly value over the delivery scatter
        // distribution (not just the on-target impact), so a throw that only frags the
        // squad when it misses is no longer scored as free. See EvaluateBlastThrow and
        // OnlyWar_TDD.md §6.6.
        private const float BlastDeliveryRollMean = 10.5f;
        private const float BlastDeliveryRollStdDev = 3.0f;
        // The execution-time damage roll, shared by every attack resolver in the engine
        // (ShootAction, AreaAttackAction, MeleeAttackAction, BlastAttackAction): the weapon's
        // damage coefficient is scaled by (mean + z * stdDev). The planner's wound estimates
        // integrate over this roll so armored figures carry their real armor-penetrating tail
        // instead of being scored invulnerable at the mean.
        internal const float DamageRollMean = 3.5f;
        internal const float DamageRollStdDev = 1.75f;
        // The success roll every to-hit estimate is measured against: hit = Phi((total - 10.5)/3).
        // Named so the Phase 4 closed-form range rescaling reads the same numbers the direct
        // estimate does. See Design/Active/EngagementScoringOverhaul.md.
        internal const float HitRollMean = 10.5f;
        internal const float HitRollStdDev = 3f;
        // Deterministic quadrature nodes over the delivery roll's standard normal, and the
        // number of angular samples a scattered node spreads across. Fixed at compile time,
        // so blast scoring stays reproducible without drawing from the battle RNG.
        private const int BlastScatterAngleSamples = 8;
        // Soldiers farther than AreaRadius + this many cells from the aim point cannot be
        // caught by any scatter node we integrate, so the gather stops there.
        private const int BlastScatterMaxGatherCells = 12;
        // Shared ranged-candidate cap: rifle, cone, and blast all score against the same top
        // handful of acquired targets (committed target first, then nearest) instead of each
        // rescanning the field independently.
        private const int RangedCandidateEvaluationCount = 6;
        private static readonly (float Z, float Weight)[] BlastDeliveryQuadrature =
            BuildStandardNormalQuadrature();
        // TUNABLE (Phase 2 sticky targeting): a soldier keeps engaging the target it already
        // committed to (soldier.TargetId / soldier.Aim) across turns rather than rescanning the whole
        // field every turn, re-acquiring only when that target stops being a viable, worthwhile shot
        // or an un-engaged enemy is about to reach melee. "Worthwhile" reuses the planner's existing
        // floor: positive expected value and better than a one-in-ten chance to hit. Raising this
        // makes soldiers abandon marginal targets (and rescan) sooner.
        private const float StickyMinimumHitProbability = 0.1f;
        // TUNABLE (Phase 3 fire distribution): base strength of the firing-lane preference that
        // spreads a squad's fire across the enemy frontage instead of piling every rifle onto the
        // single highest-value target. Each candidate target is penalized by this coefficient times
        // the lateral gap (in grid cells, perpendicular to the squad's engagement axis) between the
        // shooter's place in its own line and the target's place in the enemy line, then scaled by
        // the shooter faction's FireDiscipline. 0 disables the lane term and restores pre-Phase-3
        // targeting exactly. Retained after the take-out-probability conversion because it biases
        // target selection only and never changes the returned expected-value score.
        private const float BaseLaneSpreadCoefficient = 1.0f;
        // Fire discipline used when a squad has no faction (test fixtures, stray battle squads).
        private const float DefaultFireDiscipline = 0.5f;
        // Aim bonus a pre-sprung ambusher opens with. Matches the planner's own "aim can no
        // longer be improved" ceiling (the >= 3 checks in the standing/forced-shot paths), so a
        // seeded ambusher is indistinguishable from a soldier who spent three turns lining up the
        // shot. See SeedAmbushAim and OnlyWar_TDD.md §6.6.
        private const int FullAimBonusTurns = 3;
        // A fresh stationary aim starts at bonus 0, takes four Aim actions to reach the planner's
        // full-aim threshold (3), and fires on the fifth turn. Pursuit uses this same cycle when
        // deciding how far a squad must run before it can safely stop and complete a shot.
        private const int PursuitFireWindowTurns = FullAimBonusTurns + 2;
        // ===================================================================================
        // TUNABLE -- lambda, the graded-damage credit weight (Phase 5,
        // Design/Active/EngagementScoringOverhaul.md).
        //
        //   removal = BV * [ P(takeout) + lambda * E[woundProgress; no takeout] ]
        //
        // 0 reproduces pre-Phase-5 behaviour exactly (only the finishing blow scores). 1 credits
        // every hit with the full fraction of the disable threshold it closes. It cannot conjure
        // value against an impenetrable target at ANY setting -- see CalculateRemovalFractionOnHit.
        //
        // SWEEP, reference scenario (GradedRemovalCalibrationTests, which regenerates this table):
        // 30 bolter marines (Gun skill bonus ~1.4, Dex 15.4, BV 9/11) at 200 yards from 1 Hive
        // Tyrant (BV 84), 1 Lictor (BV 37) and 2 melee Carnifexes (BV 30), all melee-only. Reported
        // for the lead marine squad; margin = Hold score - CloseToContact score, so positive means
        // "stand and shoot". Measured with the Phase 5c/5d lookahead in place.
        //
        //   lambda | chosen      | outgoing | future | Hold - Close
        //   -------+-------------+----------+--------+--------------
        //     0.00 | StepForward |    0.009 |   1.84 |        2.334
        //     0.05 | Hold        |    0.170 |  3.742 |        2.471
        //     0.10 | Hold        |    0.330 |  5.643 |        2.608
        //     0.15 | Hold        |    0.491 |  7.544 |        2.746
        //     0.20 | Hold        |    0.652 |  9.445 |        2.883
        //     0.25 | Hold        |    0.812 | 11.346 |        3.020
        //     0.35 | Hold        |    1.133 | 15.148 |        3.295
        //     0.50 | Hold        |    1.615 | 20.851 |        3.706
        //     0.75 | Hold        |    1.935 | 33.626 |        4.304
        //     1.00 | Hold        |    2.577 | 44.102 |        4.813
        //
        // WHY 0.5, in order of weight:
        //  1. lambda = 0 COLLAPSES. With `future` now built from the same honest rate, a squad that
        //     cannot one-shot anything scores ~0 on both halves and the decision falls to
        //     ChooseEngagementOption's tie-break -- StepForward, above. Every positive lambda fixes
        //     the reported behaviour; the sweep is choosing a magnitude, not a direction.
        //  2. Rate of resolution. 30 marines remove 3 * outgoing BV/turn against 181 BV of
        //     Tyranids: ~75 turns at 0.25, ~38 at 0.5, ~23 at 1.0. The design doc's stated
        //     calibration target is "tens of turns, not 183".
        //  3. woundProgress SUMS across hit locations, but a disable requires the damage to
        //     concentrate in ONE of them, so the summed figure systematically over-states real
        //     progress toward a kill. That is an argument for a value clearly below 1, and it is
        //     the only one of the three that is about physics rather than about tuning.
        //  4. It leaves `future` (20.9) at nearly the magnitude the surrounding score terms were
        //     tuned against pre-Phase-5 (22.6), so this phase does not silently re-scale
        //     commitment, role and readiness costs along with it.
        // Provisional pending the user's manual Godot verification; the sweep above is recorded
        // here so revisiting it is a one-line change, not a re-derivation.
        // The SHIPPED value, and a const like every other tunable in this codebase
        // (MoraleConstants is all `public const`). Phase 5 shipped this as a settable static so the
        // sweep could re-run in one process, and said so plainly; Phase 7 removed that. There is no
        // writable surface on this constant at all.
        internal const float WoundProgressCreditWeight = 0.5f;

        // TEST SEAM (internal; the assembly grants InternalsVisibleTo("OnlyWar.Tests") in
        // Properties/AssemblyInfo.cs). The sweep genuinely needs lambda to vary in one process --
        // ten planner runs, otherwise ten rebuilds -- but nothing else does, and Phase 5's settable
        // property let any caller leave the whole battle engine mis-tuned. This is the narrowest
        // shape that keeps the capability: no setter, one scoped override that always restores, so
        // the value cannot be left changed even by a test that throws. Shipping code never calls it
        // (grep OverrideWoundProgressCreditWeight -- the only caller is
        // OnlyWar.Tests/Battles/GradedRemovalCalibrationTests.cs).
        private static float _woundProgressCreditWeight = WoundProgressCreditWeight;

        /// <summary>Lambda as the scoring stack actually reads it: the shipped constant unless a
        /// calibration sweep currently holds an override scope.</summary>
        internal static float EffectiveWoundProgressCreditWeight => _woundProgressCreditWeight;

        internal static IDisposable OverrideWoundProgressCreditWeight(float value) =>
            new WoundProgressCreditWeightScope(value);

        private sealed class WoundProgressCreditWeightScope : IDisposable
        {
            private readonly float _previous;

            internal WoundProgressCreditWeightScope(float value)
            {
                _previous = _woundProgressCreditWeight;
                _woundProgressCreditWeight = value;
            }

            public void Dispose() => _woundProgressCreditWeight = _previous;
        }
        // ===================================================================================

        private readonly BattleGridManager _grid;
        private readonly ICollection<IAction> _shootActions;
        private readonly ICollection<IAction> _moveActions;
        private readonly ICollection<IAction> _meleeActions;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly IReadOnlyDictionary<int, MeleeWeaponTemplate> _meleeWeaponTemplates;
        private readonly IRNG _random;
        private readonly Action<string> _log;
        private readonly int _maxPlanningDegreeOfParallelism;
        // Shared, frozen-state memo for the pure targeting computations below. Handed in by the
        // resolver so the per-side planner and every worker sub-planner reuse each other's results;
        // a standalone planner (tests) gets its own. See BattlePlanningContext for the invariant.
        private readonly BattlePlanningContext _context;

        // Labelling for ENGAGE_EVAL traces only, set by the resolver after construction. Nothing in
        // planning reads it, so a planner without it (tests, the ambush-seeding pass) behaves
        // identically and simply renders turn=0 side=none.
        public int TraceTurnNumber { get; set; }
        public string TraceSideLabel { get; set; }

        internal sealed class RangedTargetEvaluation
        {
            public BattleSoldier Target { get; }
            public RangedWeapon Weapon { get; }
            public float Range { get; }
            public int ShotsToFire { get; }
            public float HitProbability { get; }
            public float TakeOutProbabilityOnHit { get; }
            public float ExpectedEnemyBattleValueRemoved { get; }
            public float ExpectedFriendlyBattleValueLost { get; }
            // The pre-roll to-hit total behind HitProbability (HitProbability ==
            // Phi((PreRollHitTotal - 10.5)/3)) and the target speed the range modifier was taken
            // at. Recorded rather than re-derived so the Phase 4 removal-rate table can rescale
            // this shot to another range in closed form -- inverting the CDF would be lossy, and
            // recomputing the total would duplicate RangedHitEstimateContext's assembly order.
            // See Design/Active/EngagementScoringOverhaul.md.
            public float PreRollHitTotal { get; }
            public float TargetSpeed { get; }
            // Phase 5: E[woundProgress; no takeout] for this shot, captured alongside the take-out
            // probability so the Phase 4 removal-rate table can carry the graded term without a
            // second hit-location walk. See CalculateRemovalFractionOnHit.
            public float WoundProgressOnHit { get; }
            public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

            public RangedTargetEvaluation(
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                int shotsToFire,
                float hitProbability,
                float takeOutProbabilityOnHit,
                float expectedEnemyBattleValueRemoved,
                float expectedFriendlyBattleValueLost,
                float preRollHitTotal = 0f,
                float targetSpeed = 0f,
                float woundProgressOnHit = 0f)
            {
                WoundProgressOnHit = woundProgressOnHit;
                Target = target;
                Weapon = weapon;
                Range = range;
                ShotsToFire = shotsToFire;
                HitProbability = hitProbability;
                TakeOutProbabilityOnHit = takeOutProbabilityOnHit;
                ExpectedEnemyBattleValueRemoved = expectedEnemyBattleValueRemoved;
                ExpectedFriendlyBattleValueLost = expectedFriendlyBattleValueLost;
                PreRollHitTotal = preRollHitTotal;
                TargetSpeed = targetSpeed;
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
                bool firingIntoMelee,
                float? targetSpeed = null)
            {
                _weaponSkill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                _rangeModifier = BattleModifiersUtil.CalculateRangeModifier(
                    range, targetSpeed ?? target.CurrentSpeed);
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

        internal int CachedRangedEvaluationCount => _context.RangedEvaluations.Count;

        // Rows (shooter squads) and cells (shooter/target squad pairs) currently memoized in the
        // Phase 4 removal-rate table. Test visibility only.
        internal int CachedPairRemovalRowCount => _context.PairRemovalRates.Count;

        internal int CachedPairRemovalRateCount =>
            _context.PairRemovalRates.Values.Sum(row => row.Count);

        public BattleSquadPlanner(BattleGridManager grid,
                                  IReadOnlyDictionary<int, BattleSoldier> soldiers,
                                  ICollection<IAction> shootActions,
                                  ICollection<IAction> moveActions,
                                  ICollection<IAction> meleeActions,
                                  Action<string> log,
                                  IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates,
                                  IRNG random,
                                  int maxPlanningDegreeOfParallelism = 1,
                                  BattlePlanningContext context = null)
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
            _maxPlanningDegreeOfParallelism = Math.Max(1, maxPlanningDegreeOfParallelism);
            // A standalone planner (unit tests, one-off callers) gets a private context, which
            // reproduces the previous per-planner cache scope exactly.
            _context = context ?? new BattlePlanningContext();
        }

        // How far behind the friendly fighting line an HQ squad tries to stay. Matches the
        // placers' HQ rear offset so a rear-deployed HQ starts the battle already satisfied.
        private const float HqLineBuffer = 10f;

        // Ambush opener (OnlyWar_TDD.md §6.6): an ambushing squad springs the
        // trap with weapons already trained on the kill zone. Called once, before the first turn is
        // planned, for each squad on the ambushing side. Every soldier holding a loaded conventional
        // ranged weapon is pre-seeded to the full aim bonus against the target the planner itself
        // would pick this turn -- SelectBestRangedTarget applies the same lane-spread bias the squad
        // uses every turn, so the opening volley fans across the enemy line instead of piling every
        // rifle onto the nearest man. The sticky/forced-shot paths then fire that seeded aim on turn
        // one rather than spending it lining up. Soldiers with only melee or template (cone/blast)
        // weapons, or no clear shot, keep a null aim and plan normally.
        public void SeedAmbushAim(BattleSquad squad)
        {
            // Player soldiers earn learn-by-doing credit for the aiming that notionally happened
            // while the ambush was being set; enemy factions accrue the counter but no aftermath
            // policy converts it (PlayerChapterBattleAftermathPolicy is the only consumer).
            bool creditAimingXp = squad.Squad?.Faction?.IsPlayerFaction == true;
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                if (soldier.EquippedRangedWeapons.Count == 0 || !IsPlaced(soldier))
                {
                    continue;
                }
                RangedTargetEvaluation evaluation = SelectBestRangedTarget(soldier, bulkMultiplier: 0f);
                if (evaluation?.Weapon == null)
                {
                    continue;
                }
                soldier.Aim = new ValueTuple<int, RangedWeapon, int>(
                    evaluation.Target.Soldier.Id, evaluation.Weapon, FullAimBonusTurns);
                soldier.CurrentSpeed = 0;
                if (creditAimingXp)
                {
                    soldier.TurnsAiming += FullAimBonusTurns;
                }
            }
        }

        public void PrepareActions(BattleSquad squad, IReadOnlyCollection<BattleSquad> friendlySquads = null)
        {
            BattleSoldier probe = squad.AbleSoldiers.FirstOrDefault();
            if (probe == null) return;
            _grid.GetNearestEnemy(probe.Soldier.Id, out int anyEnemyId);
            if (anyEnemyId == -1) return;

            if (squad.IsInMelee)
            {
                squad.MovementTier = SquadMovementTier.InMelee;
                ApplyDeclaredMovementState(squad);
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
                List<BattleSquad> all = _soldierMap.Values
                    .Select(soldier => soldier.BattleSquad)
                    .Where(candidate => candidate != null)
                    .DistinctBy(candidate => candidate.Id)
                    .ToList();
                bool side = _grid.GetSoldierSide(probe.Soldier.Id);
                List<BattleSquad> friendly = (friendlySquads ?? all
                        .Where(candidate => candidate.AbleSoldiers.Any(member =>
                            IsPlaced(member) && _grid.GetSoldierSide(member.Soldier.Id) == side)))
                    .OrderBy(candidate => candidate.Id)
                    .ToList();
                List<BattleSquad> enemy = all
                    .Where(candidate => candidate.AbleSoldiers.Any(member =>
                        IsPlaced(member) && _grid.GetSoldierSide(member.Soldier.Id) != side))
                    .OrderBy(candidate => candidate.Id)
                    .ToList();
                BattleEngagementFrameBuilder.PairedFrame paired =
                    BattleEngagementFrameBuilder.Build(friendly, enemy);
                SquadEngagementDecision decision = ChooseEngagementOption(
                    squad,
                    paired.Frames[squad.Id],
                    paired.Profiles,
                    friendly,
                    enemy);
                DeclareEngagementDecision(decision);
                BuildEngagementActions(decision);
            }
        }

        internal const int EngagementLookaheadHorizon = 2;
        private const float EngagementFutureDiscount = 0.65f;
        // The ply discount limits how much a short rollout can steer the current turn. The
        // terminal represents the remaining battle, so it needs an explicit battle-length scale
        // instead of reusing the ply discount as a geometric tail.
        private const float ExpectedRemainingTurns = 20f;
        private const float EngagementIndifferenceFraction = 0.02f;

        /// <summary>
        /// Layer 2: scores whole-squad semantic movement options without mutating movement state,
        /// aim, reservations or action collections. Current-turn fire may use the exact memoized
        /// per-soldier target evaluators; rollout steps below are capability-group aggregates only.
        /// </summary>
        internal SquadEngagementDecision ChooseEngagementOption(
            BattleSquad squad,
            SquadEngagementFrame frame,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads,
            IReadOnlyCollection<BattleSquad> roleTargets = null)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(frame);
            BattleSquadCapabilityProfile profile = profiles[squad.Id];
            List<BattleSquad> enemies = (roleTargets ?? enemySquads ?? [])
                .Where(candidate => candidate != null
                    && candidate.Status == BattleSquadStatus.Active
                    && candidate.AbleSoldiers.Count > 0)
                .OrderBy(candidate => candidate.Id)
                .ToList();
            BattleSquad primary = ResolvePrimary(frame, enemies, enemySquads);
            List<EngagementOptionKind> legal = GetLegalOptionKinds(
                squad, frame, primary, profile, allFrames);
            List<EngagementOptionEvaluation> evaluations = legal
                .Select(kind => EvaluateEngagementOption(
                    squad, kind, frame, profile, profiles, allFrames, primary, enemies))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Kind)
                .ToList();
            float bestScore = evaluations.Select(candidate => candidate.Score)
                .DefaultIfEmpty(0)
                .Max();
            float indifference = Math.Max(
                0.1f, profile.TotalAbleBattleValue * EngagementIndifferenceFraction);
            EngagementOptionEvaluation chosen = evaluations
                .Where(candidate => bestScore - candidate.Score <= indifference)
                .OrderByDescending(candidate => candidate.Kind == frame.BaselinePosture)
                .ThenByDescending(candidate => candidate.Kind == squad.LastEngagementOptionKind)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Kind)
                .FirstOrDefault()
                ?? new EngagementOptionEvaluation(
                    EngagementOptionKind.Hold,
                    SquadMovementTier.Stationary,
                    null, 0, 0, 0, 0, 0, 0, 0, [], 0, 0, 0, 0, 0);
            return new SquadEngagementDecision(
                squad,
                frame,
                chosen,
                evaluations,
                roleTargets);
        }

        internal SquadEngagementDecision ChooseEngagementOption(
            BattleSquad squad,
            SquadEngagementFrame frame,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads,
            IReadOnlyCollection<BattleSquad> roleTargets = null)
        {
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(friendlySquads, enemySquads);
            return ChooseEngagementOption(
                squad,
                frame,
                profiles,
                paired.Frames,
                friendlySquads,
                enemySquads,
                roleTargets);
        }

        /// <summary>
        /// How much one of a contact-seeker's shooters must remove per turn, as a share of one
        /// representative enemy, for standing off to be a real alternative to closing. Below this
        /// the squad is carrying a sidearm, not holding a firing line.
        ///
        /// <para>Quoted on the same scale as
        /// <see cref="RangedEffectivenessCurve.NegligibleRemovalFraction"/> (0.001, "a thousand
        /// turns to kill one enemy, which is plinking"). This is twenty times that: a fiftieth of
        /// an enemy per shooter per turn, i.e. a squad that would need roughly fifty turns of
        /// shooting per kill. A contact-seeker with a real gun clears it easily; an Acolyte Hybrid
        /// pistol against power armour is an order of magnitude below.</para>
        ///
        /// <para>It gates ONLY contact-seekers. A squad whose doctrine is already ranged keeps
        /// every option it had regardless of how badly the matchup is going -- being outmatched is
        /// not a reason to invent a charge.</para>
        /// </summary>
        private const float ContactSeekerRangedRelevanceFraction = 0.02f;

        /// <summary>
        /// A contact-seeking squad with no ranged answer WORTH HAVING against the enemy in front of
        /// it: every yard it covers is the whole of its contribution to the battle. Both the option
        /// mask (it may not give up ground) and the closing-progress term (it is paid for closing
        /// speed regardless of role) key off this.
        ///
        /// <para>THE TEST IS RELATIVE, NOT ABSOLUTE, and that is the correction. Asking only
        /// whether the squad owns a loaded gun (<c>UsableRangedBattleValue &gt; 0</c>) or has any
        /// derivable standoff at all (<c>EffectiveEngagementRange &gt; 0</c>) answers "can it
        /// shoot", when the question the mask needs answered is "can it shoot THESE enemies to any
        /// purpose". Twenty Acolyte Hybrids with autopistols facing Astartes power armour cleared
        /// both of the old clauses -- the pistol is loaded, and it has a perfectly well-defined
        /// preferred range of 350 yards -- so they kept `Hold` and `StepBack` on the option list,
        /// scored every static option within 0.06 of the others, and walked backwards for thirty
        /// turns on a tie-break. See `2.500.M39-Xibarrus_Nu-8` and
        /// Design/Active/EngagementScoringRepair.md.</para>
        /// </summary>
        private static bool HasNoViableRangedOption(BattleSquadCapabilityProfile profile) =>
            profile.IsContactSeeking
                && (profile.UsableRangedBattleValue <= 0
                    || profile.EffectiveEngagementRange <= 0
                    || profile.PeakRangedRemovalFraction
                        < ContactSeekerRangedRelevanceFraction);

        private List<EngagementOptionKind> GetLegalOptionKinds(
            BattleSquad squad,
            SquadEngagementFrame frame,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames)
        {
            if (frame.Role is EngagementSquadRole.Bound
                or EngagementSquadRole.BreakOff)
            {
                return frame.Role == EngagementSquadRole.Bound
                    ? [EngagementOptionKind.RunToward]
                    : [EngagementOptionKind.Hold];
            }
            if (frame.Role == EngagementSquadRole.Routing)
            {
                return [EngagementOptionKind.RunToward];
            }
            if (squad.IsInMelee || frame.Role == EngagementSquadRole.RearGuard && squad.IsInMelee)
            {
                return [EngagementOptionKind.CloseToContact];
            }
            if (frame.Role is EngagementSquadRole.Cover or EngagementSquadRole.RearGuard)
            {
                return [EngagementOptionKind.Hold, EngagementOptionKind.StepBack];
            }
            if (frame.Role == EngagementSquadRole.Standoff)
            {
                // Standoff is the force-level answer to an unwinnable chase with a worthwhile
                // current shot. It is a hard movement constraint: preserve aimed standing fire
                // rather than allowing the pursuit scorer to invent a running chase.
                return [EngagementOptionKind.Hold];
            }
            if (frame.Role == EngagementSquadRole.Pursuit)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                // A stationary aim is a real cross-turn commitment. Once a pursuit squad has
                // selected Hold and at least one soldier has a still-viable aim on the pursued
                // squad, movement would clear that aim and restart the cycle. Keep the squad
                // stationary until the soldier fires or the target/shot becomes invalid.
                if (HasPursuitFireCommitment(squad, frame, primary))
                {
                    return [EngagementOptionKind.Hold];
                }
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                EngagementOptionKind fast = distance <= profile.MoveSpeed
                        + BattleContactRules.MeleeContactAllowance
                    ? EngagementOptionKind.CloseToContact
                    : EngagementOptionKind.RunToward;
                return [EngagementOptionKind.Hold, EngagementOptionKind.JogToward, fast];
            }

            if (primary == null)
            {
                return [EngagementOptionKind.Hold];
            }

            float primaryDistance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
            // Two ways a contact-seeker can have nothing to shoot with. The first is about the
            // squad -- its gun is useless against this enemy at any range. The second is about
            // where it is standing right now: its gun does not reach that far, so holding ground
            // buys it literally no fire this turn and giving ground buys it less than none. The
            // positional clause deliberately lives here and not in HasNoViableRangedOption, which
            // is a capability question and also feeds the closing-progress reward.
            bool noViableRangedOption = HasNoViableRangedOption(profile)
                || (profile.IsContactSeeking && primaryDistance > profile.PreferredBandUpper);
            List<EngagementOptionKind> result =
            [
                EngagementOptionKind.Hold,
                EngagementOptionKind.StepBack,
                EngagementOptionKind.StepForward,
                EngagementOptionKind.JogToward,
                EngagementOptionKind.CloseToContact
            ];
            // A melee-only squad with no usable ranged answer has no doctrinal reason to give up
            // ground. This is the old WeakNoOption guarantee expressed as an option mask: the
            // score is still honest, but a negative-EV charge cannot be outvoted by retreat.
            if (noViableRangedOption && primaryDistance
                > BattleContactRules.MeleeContactAllowance)
            {
                result.Remove(EngagementOptionKind.Hold);
                result.Remove(EngagementOptionKind.StepBack);
                if (primaryDistance > profile.MoveSpeed
                    + BattleContactRules.MeleeContactAllowance)
                {
                    result.Remove(EngagementOptionKind.CloseToContact);
                    result.Add(EngagementOptionKind.RunToward);
                }
            }
            if (frame.InterposePoint.HasValue)
            {
                result.Add(EngagementOptionKind.MoveToInterpose);
            }
            return result;
        }

        private EngagementOptionEvaluation EvaluateEngagementOption(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames,
            BattleSquad primary,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            SquadMovementTier tier = GetOptionTier(kind, squad, primary, frame);
            ValueTuple<float, float>? intended = GetIntendedDestination(
                squad, kind, frame, primary, allFrames);
            (float feasibleSpeed, ValueTuple<float, float> projectedCentroid) =
                ProjectFeasibleSquadEndpoint(squad, kind, tier, intended, primary, frame);
            ValueTuple<int, int>? direction = GetOptionDirection(squad, kind, frame, primary, intended);
            (float enemyRemoval, float friendlyFire, float readiness,
                IReadOnlyList<PlannedSoldierAction> rootActions) =
                EvaluateImmediateActionValue(squad, tier, direction);
            float incoming = EvaluateIncomingNow(
                squad, feasibleSpeed, profiles, allFrames, enemies);
            (float meleeNow, float commitment) = EvaluateContactTerms(
                squad, kind, primary, profile);
            float arrivalTimeValue = EvaluateArrivalTimeValue(
                squad,
                projectedCentroid,
                profile,
                profiles,
                allFrames,
                enemies,
                frame);
            if (kind == EngagementOptionKind.CloseToContact
                && !profile.IsContactSeeking
                && primary != null
                && BattleEngagementFrameBuilder.MinimumDistance(squad, primary)
                    > profile.MoveSpeed + BattleContactRules.MeleeContactAllowance)
            {
                // A ranged squad's long approach is movement toward its firing band, not a
                // completed charge. Keep the candidate visible for diagnostics/calibration, but
                // charge the doctrinal cost of treating a multi-turn run-in as an assault.
                commitment += profile.TotalAbleBattleValue;
            }
            List<float> future = EvaluateFutureExchange(
                squad,
                projectedCentroid,
                kind,
                profile,
                profiles,
                allFrames,
                enemies);
            float roleTerm = EvaluateScreenRoleTerm(
                squad, kind, frame, profile, profiles, projectedCentroid, enemies);
            roleTerm += EvaluatePursuitContactProgress(
                squad,
                kind,
                frame,
                profile,
                primary,
                feasibleSpeed,
                primary != null
                    ? allFrames.GetValueOrDefault(primary.Id)?.Role
                    : null);
            float fireWindowValue = EvaluatePursuitFireWindowValue(
                squad,
                kind,
                frame,
                profile,
                primary,
                primary != null
                    ? allFrames.GetValueOrDefault(primary.Id)?.Role
                    : null);
            if (squad.MoraleState == MoraleState.Shaken
                && kind is EngagementOptionKind.StepForward
                    or EngagementOptionKind.JogToward
                    or EngagementOptionKind.CloseToContact
                    or EngagementOptionKind.MoveToInterpose
                    or EngagementOptionKind.RunToward)
            {
                commitment += profile.TotalAbleBattleValue * 0.35f;
            }
            if (frame.Role == EngagementSquadRole.Pursuit
                && kind == EngagementOptionKind.JogToward
                && feasibleSpeed + 0.0001f < frame.QuarryRunSpeed)
            {
                commitment += profile.TotalAbleBattleValue
                    * (1f - feasibleSpeed / Math.Max(0.1f, frame.QuarryRunSpeed));
            }
            if (SuppressHqAdvance(squad, kind))
            {
                commitment += profile.TotalAbleBattleValue;
            }
            // Stability is a tie policy in ChooseEngagementOption, not utility.  Keep the trace
            // column for compatibility while preventing an old posture from buying real BV.
            float hysteresis = 0;
            float discountedFuture = 0;
            for (int index = 0; index < future.Count; index++)
            {
                discountedFuture += (float)Math.Pow(EngagementFutureDiscount, index + 1)
                    * future[index];
            }
            // CONTRACT (Phase 5, Design/Active/EngagementScoringOverhaul.md). `enemyRemoval` and
            // `discountedFuture` are now the SAME currency: both are
            // hit * (takeOut + lambda * woundProgress) * targetBV, summed per soldier by
            // per-soldier argmax. `discountedFuture` used to be built on AggregateRemovalRate, a
            // capability proxy asserting a flat 10% of the ATTACKER'S OWN battle value per turn --
            // which put the two halves of this sum ~4 orders of magnitude apart (~10^-3 vs ~10^1)
            // and meant the immediate term could never change which option won. It can now.
            float score = enemyRemoval - friendlyFire + readiness + fireWindowValue
                - incoming + meleeNow + discountedFuture + arrivalTimeValue + roleTerm
                - commitment + hysteresis;
            return new EngagementOptionEvaluation(
                kind, tier, intended, feasibleSpeed,
                enemyRemoval, friendlyFire, readiness, fireWindowValue, incoming, meleeNow,
                future, arrivalTimeValue, roleTerm, commitment, hysteresis, score, rootActions);
        }

        private static SquadMovementTier GetOptionTier(
            EngagementOptionKind kind,
            BattleSquad squad,
            BattleSquad primary,
            SquadEngagementFrame frame)
        {
            return kind switch
            {
                EngagementOptionKind.Hold => SquadMovementTier.Stationary,
                EngagementOptionKind.StepBack or EngagementOptionKind.StepForward =>
                    SquadMovementTier.Walk,
                EngagementOptionKind.JogToward => SquadMovementTier.Jog,
                EngagementOptionKind.MoveToInterpose => InterposeTier(squad, frame),
                EngagementOptionKind.CloseToContact => primary != null
                    && BattleEngagementFrameBuilder.MinimumDistance(squad, primary)
                        <= squad.GetSquadMove() + 1
                            ? SquadMovementTier.InMelee
                            : SquadMovementTier.Run,
                EngagementOptionKind.RunToward => SquadMovementTier.Run,
                _ => SquadMovementTier.Stationary
            };
        }

        private static SquadMovementTier InterposeTier(BattleSquad squad, SquadEngagementFrame frame)
        {
            if (!frame.InterposePoint.HasValue) return SquadMovementTier.Stationary;
            (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
            float dx = frame.InterposePoint.Value.Item1 - x;
            float dy = frame.InterposePoint.Value.Item2 - y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float move = squad.GetSquadMove();
            if (distance <= move * WalkSpeedMultiplier) return SquadMovementTier.Walk;
            if (distance <= move * JogSpeedMultiplier) return SquadMovementTier.Jog;
            return SquadMovementTier.Run;
        }

        private static ValueTuple<float, float>? GetIntendedDestination(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquad primary,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames)
        {
            if (kind == EngagementOptionKind.MoveToInterpose) return frame.InterposePoint;
            if (primary == null) return null;
            ValueTuple<float, float> target = BattleEngagementFrameBuilder.Centroid(primary);
            if (frame.Role != EngagementSquadRole.Pursuit
                || allFrames.GetValueOrDefault(primary.Id)?.Role is not
                    (EngagementSquadRole.Bound or EngagementSquadRole.Routing))
            {
                return target;
            }

            // Movement is simultaneous. Lead a moving quarry instead of stopping at its current
            // centroid; otherwise a faster Run repeatedly arrives where the withdrawal used to be
            // and never spends its speed advantage. Bound movement has an exact force heading.
            // Routing movement runs the whole squad along the line from its closest threat through
            // its own centroid, so when this pursuer is that threat the line from here through the
            // quarry is exactly the rout heading, and a good approximation when it is not.
            SquadEngagementFrame quarryFrame = allFrames[primary.Id];
            float leadX;
            float leadY;
            if (quarryFrame.Role == EngagementSquadRole.Bound
                && quarryFrame.FixedHeading.HasValue)
            {
                (int x, int y) = BattleForcePlanner.GetHeadingVector(
                    quarryFrame.FixedHeading.Value);
                leadX = x;
                leadY = y;
            }
            else
            {
                (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
                leadX = target.Item1 - x;
                leadY = target.Item2 - y;
            }
            float length = (float)Math.Sqrt(leadX * leadX + leadY * leadY);
            if (length <= 0.0001f) return target;
            float leadDistance = Math.Max(0, frame.QuarryRunSpeed);
            return (
                target.Item1 + leadX / length * leadDistance,
                target.Item2 + leadY / length * leadDistance);
        }

        private ValueTuple<int, int>? GetOptionDirection(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquad primary,
            ValueTuple<float, float>? intended)
        {
            if (kind == EngagementOptionKind.Hold) return null;
            if (kind == EngagementOptionKind.StepBack && frame.FixedHeading.HasValue)
            {
                return BattleForcePlanner.GetHeadingVector(frame.FixedHeading.Value);
            }
            (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
            float targetX = intended?.Item1 ?? x;
            float targetY = intended?.Item2 ?? y;
            int dx = Math.Sign(targetX - x);
            int dy = Math.Sign(targetY - y);
            if (kind == EngagementOptionKind.StepBack)
            {
                dx = -dx;
                dy = -dy;
            }
            return new ValueTuple<int, int>(dx, dy);
        }

        private (float FeasibleSpeed, ValueTuple<float, float> Centroid)
            ProjectFeasibleSquadEndpoint(
                BattleSquad squad,
                EngagementOptionKind kind,
                SquadMovementTier tier,
                ValueTuple<float, float>? intended,
                BattleSquad primary,
                SquadEngagementFrame frame)
        {
            if (tier == SquadMovementTier.Stationary)
            {
                (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
                return (0, (x, y));
            }
            BattleGridManager overlay = (BattleGridManager)_grid.Clone();
            float distanceTotal = 0;
            float xTotal = 0;
            float yTotal = 0;
            int count = 0;
            foreach (BattleSoldier soldier in squad.AbleSoldiers
                .Where(IsPlaced)
                .OrderBy(member => member.Soldier.Id))
            {
                ValueTuple<int, int> line = MovementLineFor(
                    soldier, kind, frame, primary, intended);
                float budget = GetMovementBudget(soldier, tier);
                ValueTuple<int, int> desired = CalculateMovementAlongLine(line, budget);
                ValueTuple<int, int> target = (
                    soldier.TopLeft.Value.Item1 + desired.Item1,
                    soldier.TopLeft.Value.Item2 + desired.Item2);
                ushort orientation = CalculateOrientationFromVector(line, soldier, tier);
                ValueTuple<int, int> endpoint = FindBestLocation(
                    soldier, soldier.TopLeft.Value, target, budget, orientation, overlay);
                overlay.ReserveMoveDestination(soldier, endpoint, orientation);
                int dx = endpoint.Item1 - soldier.TopLeft.Value.Item1;
                int dy = endpoint.Item2 - soldier.TopLeft.Value.Item2;
                distanceTotal += (float)Math.Sqrt(dx * dx + dy * dy);
                xTotal += endpoint.Item1;
                yTotal += endpoint.Item2;
                count++;
            }
            if (count == 0) return (0, (0, 0));
            return (distanceTotal / count, (xTotal / count, yTotal / count));
        }

        private ValueTuple<int, int> MovementLineFor(
            BattleSoldier soldier,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquad primary,
            ValueTuple<float, float>? intended)
        {
            if (kind == EngagementOptionKind.StepBack && frame.FixedHeading.HasValue)
            {
                ValueTuple<int, int> heading = BattleForcePlanner.GetHeadingVector(
                    frame.FixedHeading.Value);
                return (heading.Item1 * 10_000, heading.Item2 * 10_000);
            }
            float targetX = intended?.Item1
                ?? primary?.AbleSoldiers.FirstOrDefault(IsPlaced)?.TopLeft?.Item1
                ?? soldier.TopLeft.Value.Item1;
            float targetY = intended?.Item2
                ?? primary?.AbleSoldiers.FirstOrDefault(IsPlaced)?.TopLeft?.Item2
                ?? soldier.TopLeft.Value.Item2;
            int dx = (int)Math.Round(targetX - soldier.TopLeft.Value.Item1);
            int dy = (int)Math.Round(targetY - soldier.TopLeft.Value.Item2);
            if (kind == EngagementOptionKind.StepBack)
            {
                dx = -dx;
                dy = -dy;
            }
            if (dx == 0 && dy == 0) dy = 1;
            return (dx, dy);
        }

        private (float EnemyRemoval, float FriendlyFire, float Readiness,
            IReadOnlyList<PlannedSoldierAction> RootActions)
            EvaluateImmediateActionValue(
                BattleSquad squad,
                SquadMovementTier tier,
                ValueTuple<int, int>? direction)
        {
            float bulk = tier switch
            {
                SquadMovementTier.Walk => WalkBulkMultiplier,
                SquadMovementTier.Jog => FullBulkMultiplier,
                _ => 0
            };
            float removal = 0;
            float friendly = 0;
            float readiness = 0;
            Dictionary<int, float> awardedByTarget = [];
            List<PlannedSoldierAction> rootActions = [];
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                if (!IsPlaced(soldier)) continue;
                PlannedSoldierAction action = PlanSoldierRootAction(
                    soldier, tier, bulk, direction);
                rootActions.Add(action);
                float awardedRemoval = action.ExpectedEnemyBattleValueRemoved;
                if (action.TargetId.HasValue && awardedRemoval > 0)
                {
                    int targetId = action.TargetId.Value;
                    float cap = _soldierMap.TryGetValue(targetId, out BattleSoldier target)
                        ? GetBattleValue(target)
                        : awardedRemoval;
                    float prior = awardedByTarget.GetValueOrDefault(targetId);
                    float award = Math.Min(
                        awardedRemoval,
                        Math.Max(0, cap - prior));
                    awardedByTarget[targetId] = prior + award;
                    removal += award;
                }
                friendly += action.ExpectedFriendlyBattleValueLost;
                readiness += action.ReadinessValue;
            }
            float enemyCap = _soldierMap.Values
                .Where(target => target.IsCombatEffective
                    && IsPlaced(target)
                    && target.BattleSquad != squad
                    && _grid.GetSoldierSide(target.Soldier.Id)
                        != _grid.GetSoldierSide(squad.AbleSoldiers[0].Soldier.Id))
                .Sum(GetBattleValue);
            return (Math.Min(removal, enemyCap), friendly, readiness, rootActions);
        }

        /// <summary>
        /// Selects the concrete root-turn action for one soldier under a candidate posture.  This
        /// method is deliberately pure: candidate workers call it against the frozen state, and
        /// the winning descriptors are later materialized without running target/action selection
        /// again.  In particular, Aim is never compared when the posture makes Aim illegal.
        /// </summary>
        private PlannedSoldierAction PlanSoldierRootAction(
            BattleSoldier soldier,
            SquadMovementTier tier,
            float bulkMultiplier,
            ValueTuple<int, int>? movementDirection)
        {
            if (tier is SquadMovementTier.Run or SquadMovementTier.InMelee)
            {
                return PlanRunUtilityAction(soldier);
            }
            if (soldier.RangedWeapons.Count == 0)
            {
                return new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
            }
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                RangedWeapon ready = soldier.RangedWeapons
                    .Where(weapon => (int)weapon.Template.Location <= soldier.FunctioningHands)
                    .OrderByDescending(weapon => weapon.Template.MaximumRange)
                    .ThenBy(weapon => weapon.Template.Id)
                    .FirstOrDefault();
                return ready == null
                    ? new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None)
                    : new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Ready,
                        WeaponTemplateId: ready.Template.Id,
                        ReadinessValue: GetBattleValue(soldier) * 0.025f);
            }
            RangedWeapon equipped = soldier.EquippedRangedWeapons[0];
            if (soldier.ReloadingPhase > 0 || equipped.LoadedAmmo == 0)
            {
                return new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.Reload,
                    WeaponTemplateId: equipped.Template.Id,
                    ReadinessValue: GetBattleValue(soldier) * 0.025f);
            }

            if (tier == SquadMovementTier.Stationary
                && soldier.Aim is ValueTuple<int, RangedWeapon, int> stickyAim
                && _soldierMap.TryGetValue(stickyAim.Item1, out BattleSoldier stickyTarget)
                && IsExistingAimStillViable(soldier))
            {
                float stickyRange = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id, stickyTarget.Soldier.Id);
                RangedTargetEvaluation stickyShot = EvaluateRangedTarget(
                    soldier,
                    stickyTarget,
                    stickyAim.Item2,
                    stickyRange,
                    stickyAim.Item2.Template.Accuracy + stickyAim.Item3 + 1);
                bool shoot = stickyAim.Item3 >= 3
                    || stickyTarget.GetMoveSpeed() > stickyRange
                    || stickyShot.TakeOutProbabilityOnHit * stickyShot.HitProbability >= 0.33f;
                return shoot
                    ? PlanConventionalShot(soldier, stickyShot, 0, 1)
                    : new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Aim,
                        stickyTarget.Soldier.Id,
                        stickyAim.Item2.Template.Id,
                        stickyRange,
                        ReadinessValue: GetBattleValue(soldier) * 0.05f);
            }

            float aimMultiplier = tier switch
            {
                SquadMovementTier.Stationary => 1f,
                SquadMovementTier.Walk => WalkAimMultiplier,
                _ => 0f
            };
            IReadOnlyList<BattleSoldier> candidates = BuildRankedRangedCandidates(
                soldier, movementDirection);
            TemplateFiringLineEvaluation template = SelectBestTemplateFiringLine(
                soldier, candidates, movementDirection);
            RangedTargetEvaluation targetEvaluation = EvaluateStickyTarget(
                    soldier, bulkMultiplier, movementDirection)
                ?? SelectBestRangedTarget(
                    soldier,
                    bulkMultiplier,
                    includeExistingAim: tier == SquadMovementTier.Stationary,
                    movementDirection: movementDirection);
            TemplateFiringLineEvaluation blast = SelectBestBlastThrow(
                soldier, movementDirection, bulkMultiplier, candidates);
            float bestConventional = Math.Max(
                template?.Score ?? float.MinValue,
                targetEvaluation?.Score ?? float.MinValue);
            if (blast != null
                && blast.Score > bestConventional + BlastOverConventionalScoreMargin)
            {
                return new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.BlastAttack,
                    blast.Target.Soldier.Id,
                    blast.Weapon.Template.Id,
                    blast.Range,
                    BulkMultiplier: bulkMultiplier,
                    ExpectedEnemyBattleValueRemoved: blast.ExpectedEnemyBattleValueRemoved,
                    ExpectedFriendlyBattleValueLost: blast.ExpectedFriendlyBattleValueLost,
                    Diagnostic: FormatGrenadeSelection(
                        soldier,
                        blast,
                        targetEvaluation,
                        template,
                        bestConventional,
                        bulkMultiplier));
            }
            if (template != null
                && template.Score >= (targetEvaluation?.Score ?? float.MinValue))
            {
                return new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.AreaAttack,
                    template.Target.Soldier.Id,
                    template.Weapon.Template.Id,
                    template.Range,
                    BulkMultiplier: bulkMultiplier,
                    ExpectedEnemyBattleValueRemoved: template.ExpectedEnemyBattleValueRemoved,
                    ExpectedFriendlyBattleValueLost: template.ExpectedFriendlyBattleValueLost);
            }
            if (targetEvaluation == null)
            {
                RangedWeapon emptyBlast = soldier.EquippedRangedWeapons
                    .Concat(soldier.RangedWeapons)
                    .FirstOrDefault(weapon => weapon.Template.IsBlastWeapon
                        && weapon.LoadedAmmo == 0);
                return soldier.ReloadingPhase == 0 && emptyBlast != null
                    ? new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Reload,
                        WeaponTemplateId: emptyBlast.Template.Id,
                        ReadinessValue: GetBattleValue(soldier) * 0.02f)
                    : new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
            }

            BattleSoldier target = targetEvaluation.Target;
            float range = _grid.GetDistanceBetweenSoldiers(
                soldier.Soldier.Id, target.Soldier.Id);
            if (soldier.Aim is ValueTuple<int, RangedWeapon, int> existingAim
                && existingAim.Item3 >= 3
                && existingAim.Item1 == target.Soldier.Id
                && existingAim.Item2.LoadedAmmo > 0
                && soldier.EquippedRangedWeapons.Contains(existingAim.Item2)
                && range <= existingAim.Item2.Template.MaximumRange)
            {
                float modifier = -(existingAim.Item2.Template.Bulk * bulkMultiplier)
                    + ((existingAim.Item2.Template.Accuracy + existingAim.Item3 + 1)
                        * aimMultiplier);
                return PlanConventionalShot(
                    soldier,
                    EvaluateRangedTarget(
                        soldier, target, existingAim.Item2, range, modifier),
                    bulkMultiplier,
                    aimMultiplier);
            }

            RangedTargetEvaluation shootNow = GetBestWeaponForSituation(
                soldier,
                target,
                range,
                bulkMultiplier,
                useAccuracy: false,
                aimMultiplier: aimMultiplier);
            // A moving candidate cannot aim.  Excluding that illegal alternative, rather than
            // comparing against it and later doing nothing, is the key plan/execution invariant.
            RangedTargetEvaluation aimNow = aimMultiplier > 0
                ? GetBestWeaponForSituation(
                    soldier,
                    target,
                    range,
                    bulkMultiplier,
                    useAccuracy: true,
                    aimMultiplier: aimMultiplier)
                : null;
            if (shootNow != null
                && (aimNow == null || shootNow.HitProbability * 2 > aimNow.HitProbability))
            {
                return PlanConventionalShot(
                    soldier, shootNow, bulkMultiplier, aimMultiplier);
            }
            if (aimMultiplier > 0)
            {
                RangedWeapon aimWeapon = aimNow?.Weapon
                    ?? soldier.EquippedRangedWeapons
                        .Where(weapon => !weapon.Template.IsTemplateWeapon)
                        .OrderByDescending(weapon => weapon.Template.MaximumRange)
                        .ThenBy(weapon => weapon.Template.Id)
                        .FirstOrDefault();
                if (aimWeapon != null)
                {
                    return new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Aim,
                        target.Soldier.Id,
                        aimWeapon.Template.Id,
                        range,
                        ReadinessValue: GetBattleValue(soldier) * 0.05f);
                }
            }
            return new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
        }

        private PlannedSoldierAction PlanRunUtilityAction(BattleSoldier soldier)
        {
            if (soldier.RangedWeapons.Count == 0)
            {
                return new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
            }
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                RangedWeapon ready = soldier.RangedWeapons
                    .Where(weapon => (int)weapon.Template.Location <= soldier.FunctioningHands)
                    .OrderByDescending(weapon => weapon.Template.MaximumRange)
                    .ThenBy(weapon => weapon.Template.Id)
                    .FirstOrDefault();
                return ready == null
                    ? new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None)
                    : new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Ready,
                        WeaponTemplateId: ready.Template.Id,
                        ReadinessValue: GetBattleValue(soldier) * 0.025f);
            }
            RangedWeapon weapon = soldier.ReloadingPhase > 0
                || soldier.EquippedRangedWeapons[0].LoadedAmmo == 0
                    ? soldier.EquippedRangedWeapons[0]
                    : soldier.RangedWeapons.FirstOrDefault(candidate =>
                        candidate.Template.IsBlastWeapon && candidate.LoadedAmmo == 0);
            return weapon == null
                ? new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None)
                : new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.Reload,
                    WeaponTemplateId: weapon.Template.Id,
                    ReadinessValue: GetBattleValue(soldier) * 0.025f);
        }

        private static PlannedSoldierAction PlanConventionalShot(
            BattleSoldier soldier,
            RangedTargetEvaluation shot,
            float bulkMultiplier,
            float aimMultiplier) => new(
                soldier.Soldier.Id,
                PlannedSoldierActionKind.Shoot,
                shot.Target.Soldier.Id,
                shot.Weapon.Template.Id,
                shot.Range,
                shot.ShotsToFire,
                bulkMultiplier,
                aimMultiplier,
                shot.ExpectedEnemyBattleValueRemoved,
                shot.ExpectedFriendlyBattleValueLost);

        private float EvaluateIncomingNow(
            BattleSquad squad,
            float feasibleSpeed,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            float incoming = 0;
            foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
            {
                if (!profiles.ContainsKey(enemy.Id)
                    || !frames.TryGetValue(enemy.Id, out SquadEngagementFrame enemyFrame))
                {
                    continue;
                }
                float allocation = enemyFrame.PairWeights.GetValueOrDefault(squad.Id);
                float attackerBulk = PostureBulkMultiplier(enemyFrame.BaselinePosture);
                if (!float.IsPositiveInfinity(attackerBulk))
                {
                    incoming += allocation * EstimateIncomingResponse(
                        enemy, squad, feasibleSpeed, attackerBulk);
                }
            }
            return incoming;
        }

        private float EstimateIncomingResponse(
            BattleSquad attackerSquad,
            BattleSquad targetSquad,
            float targetSpeed,
            float attackerBulk)
        {
            var cacheKey = (
                attackerSquad.Id,
                targetSquad.Id,
                BitConverter.SingleToInt32Bits(targetSpeed),
                BitConverter.SingleToInt32Bits(attackerBulk));
            if (_context.IncomingResponses.TryGetValue(cacheKey, out float cached))
            {
                return cached;
            }

            float response = 0;
            foreach (BattleSoldier shooter in attackerSquad.AbleSoldiers
                .Where(IsPlaced)
                .OrderBy(member => member.Soldier.Id))
            {
                RangedTargetEvaluation best = null;
                foreach (BattleSoldier target in targetSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(candidate => _grid.GetDistanceBetweenSoldiers(
                        shooter.Soldier.Id, candidate.Soldier.Id))
                    .ThenBy(candidate => candidate.Soldier.Id)
                    .Take(3))
                {
                    float range = _grid.GetDistanceBetweenSoldiers(
                        shooter.Soldier.Id, target.Soldier.Id);
                    foreach (RangedWeapon weapon in shooter.EquippedRangedWeapons
                        .Where(candidate => candidate.LoadedAmmo > 0
                            && !candidate.Template.IsTemplateWeapon
                            && range <= candidate.Template.MaximumRange)
                        .OrderBy(candidate => candidate.Template.Id))
                    {
                        RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                            shooter,
                            target,
                            weapon,
                            range,
                            -weapon.Template.Bulk * attackerBulk,
                            targetSpeed);
                        if (best == null || evaluation.Score > best.Score)
                        {
                            best = evaluation;
                        }
                    }
                }
                if (best != null && best.Score > 0)
                {
                    response += best.ExpectedEnemyBattleValueRemoved;
                }
            }
            response = Math.Min(
                response,
                targetSquad.AbleSoldiers.Where(IsPlaced).Sum(GetBattleValue));
            _context.IncomingResponses[cacheKey] = response;
            return response;
        }

        private (float MeleeNow, float Commitment) EvaluateContactTerms(
            BattleSquad squad,
            EngagementOptionKind kind,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile)
        {
            if (kind != EngagementOptionKind.CloseToContact || primary == null)
            {
                return (0, 0);
            }
            float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
            float melee = 0;
            float closing = 0;
            int reaches = 0;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                ChargeAssessment estimate = EstimateChargeNet(soldier, primary, distance);
                closing += estimate.ClosingCost;
                // EstimateChargeNet already discounts the melee payoff by arrival time. It is
                // still a valid future commitment when contact takes several turns; this flag is
                // only about seats and weapon-lock cost that apply on the current turn.
                melee += estimate.MeleeBattleValue;
                if (estimate.ReachesContactThisTurn)
                {
                    reaches++;
                }
            }
            float seatFraction = Math.Min(1f,
                profile.ContactCapacity / (float)Math.Max(1, squad.AbleSoldiers.Count));
            float currentContactFraction = reaches > 0
                ? Math.Min(seatFraction,
                    reaches / (float)Math.Max(1, squad.AbleSoldiers.Count))
                : seatFraction;
            melee *= currentContactFraction;
            float lockCost = reaches > 0
                ? Math.Max(0, profile.UsableRangedBattleValue - profile.UsableMeleeBattleValue)
                    * 0.12f
                : 0;
            return (
                Math.Min(melee, primary.AbleSoldiers.Sum(GetBattleValue)),
                Math.Min(closing, profile.TotalAbleBattleValue) + lockCost);
        }

        private List<float> EvaluateFutureExchange(
            BattleSquad squad,
            ValueTuple<float, float> projectedCentroid,
            EngagementOptionKind kind,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            Dictionary<int, float> ranges = enemies.ToDictionary(
                enemy => enemy.Id,
                enemy => Distance(projectedCentroid, BattleEngagementFrameBuilder.Centroid(enemy)));
            float continuation = EvaluateBestContinuation(
                squad,
                profile,
                profiles,
                frames,
                enemies,
                ranges,
                EngagementLookaheadHorizon);
            return [continuation];
        }

        /// <summary>
        /// Values the root option's change in time-to-useful-exchange using the same present-value
        /// currency as the lookahead terminal. A short rollout can make Walk, Jog and Run look
        /// nearly identical when the useful range is many turns away; this term exposes the root
        /// transition directly without assigning movement a unit-specific bonus.
        ///
        /// The value is positive when the candidate reaches a useful exchange sooner and negative
        /// when the exchange at that range is unfavorable. The latter is intentional: movement
        /// should not be rewarded merely because it is movement. A ranged squad uses its derived
        /// effective band; a contact-seeking squad uses the contact boundary.
        /// </summary>
        private float EvaluateArrivalTimeValue(
            BattleSquad squad,
            ValueTuple<float, float> projectedCentroid,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies,
            SquadEngagementFrame frame)
        {
            // A squad already inside its ordinary ranged band should not be pulled toward the
            // sharper derived range merely because that range exists. The baseline posture is the
            // existing generic statement of whether approach is currently warranted; this term
            // adds value to the speed of that approach rather than replacing the band policy.
            if (profile.MoveSpeed <= 0
                || enemies.Count == 0
                || frame.BaselinePosture is not (
                    EngagementOptionKind.CloseToContact
                    or EngagementOptionKind.JogToward
                    or EngagementOptionKind.RunToward))
            {
                return 0;
            }

            ValueTuple<float, float> currentCentroid =
                BattleEngagementFrameBuilder.Centroid(squad);
            float desiredRange = profile.IsContactSeeking
                ? 1f
                : Math.Max(1f, profile.EffectiveEngagementRange);
            float value = 0;
            foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
            {
                if (!profiles.TryGetValue(enemy.Id, out BattleSquadCapabilityProfile opposing)
                    || !frames.ContainsKey(enemy.Id))
                {
                    continue;
                }

                ValueTuple<float, float> enemyCentroid =
                    BattleEngagementFrameBuilder.Centroid(enemy);
                float before = Distance(currentCentroid, enemyCentroid);
                float after = Distance(projectedCentroid, enemyCentroid);

                // Both distances are measured to where the quarry is standing NOW, so against a
                // withdrawing enemy the gross closing this option shows is not what the squad
                // keeps: the quarry spends the same turn opening the range again. Netting it out
                // is what stops a stern chase from being repriced as progress every turn. Without
                // it a pursuer at matched speed scored the full value of closing 6 yards, took
                // none of it, and scored the identical 6 yards again next turn — arrival_value
                // 65.8 per turn for an arrival that never came (Xibarrus Theta, 2026-08-04).
                float quarrySpeed = QuarryWithdrawalRate(
                    frame, frames[enemy.Id].Role);
                after = before - Math.Max(0, before - after - quarrySpeed);
                if (before <= desiredRange || after >= before - 0.0001f) continue;

                // The discount has to run on the same net rate: at matched speed the useful range
                // is not profile.MoveSpeed turns away, it is unreachable, and the floor makes that
                // read as "so far off it is worth nothing" rather than "arrives next turn".
                float speed = Math.Max(0.1f, profile.MoveSpeed - quarrySpeed);
                float turnsBefore = Math.Max(0, before - desiredRange) / speed;
                float turnsAfter = Math.Max(0, after - desiredRange) / speed;
                float arrivalDiscountDelta =
                    1f / (1f + turnsAfter) - 1f / (1f + turnsBefore);
                if (arrivalDiscountDelta <= 0) continue;

                // Arrival value is the offensive opportunity unlocked by reaching the useful
                // range. Incoming exposure remains in EvaluateIncomingNow and the continuation
                // exchange, so using the net rate here would count that risk twice and could make
                // every necessary approach look worse simply because the enemy can shoot back.
                //
                // It is the MARGINAL rate, not the gross one. The gross rate at the destination
                // prices arrival as though the squad were doing nothing where it stands, so a
                // squad already delivering fire is paid the full post-arrival rate for abandoning
                // it. Measured 2026-08-07: a flamer bearer standing 10 yards from its target --
                // inside a 30-yard weapon, burning it for 0.775 battle value this turn -- scored
                // arrival 0.971 for running to contact and taking 0.000, so CloseToContact beat
                // Hold 1.705 to 1.310 and the cone was never fired.
                //
                // What closing actually buys is the IMPROVEMENT in the per-turn rate. A squad
                // whose rate is already what it will be at the destination gains nothing by
                // arriving sooner; a melee squad out of reach still scores 0 where it stands and
                // closes exactly as it did before, as does any squad outside its weapon's reach.
                // This is the same invariant the BaselinePosture guard above reaches for -- do not
                // pull a squad toward a sharper range merely because that range exists -- which
                // that guard cannot enforce for a contact-seeking profile, since a contact seeker
                // is precisely the case whose baseline posture is always a closing one.
                float exchangeRate = EvaluateOutgoingExchangeRate(
                    squad,
                    enemy,
                    profile,
                    opposing,
                    frames,
                    desiredRange);
                float currentRate = EvaluateOutgoingExchangeRate(
                    squad,
                    enemy,
                    profile,
                    opposing,
                    frames,
                    before);
                float rateGain = exchangeRate - currentRate;
                if (rateGain <= 0) continue;
                value += rateGain * ExpectedRemainingTurns * arrivalDiscountDelta;
            }
            return value;
        }

        private float EvaluateBestContinuation(
            BattleSquad squad,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies,
            IReadOnlyDictionary<int, float> ranges,
            int depth)
        {
            if (depth <= 0)
            {
                // PHASE 5d (Design/Active/EngagementScoringOverhaul.md). The terminal used to be
                // `attainable * 0.25 / (1 + turnsToAct)` -- 41% of `future` (9.312 of 22.84) built
                // from the squad's OWN battle value, with no per-turn semantics and no reference to
                // what it was shooting at. It remains the same per-turn net exchange as the
                // plies, but arrival is scaled separately from the short-ply discount:
                //
                //     terminal = exchange(rangeWhenActing) * ExpectedRemainingTurns
                //         / (1 + turnsToAct)
                //
                // Read literally: "once I am standing where I want to stand, this is what each
                // further turn is worth after arrival, scaled by the expected remaining battle
                // length. A short rollout discount must not make a battle that starts 400 yards
                // away effectively end before the charge can pay off.
                //
                // The closing gradient survives the switch to the honest rate table: a squad out of
                // weapon reach scores 0 exchange AT its current range, but the terminal is
                // evaluated at rangeWhenActing -- where it will be standing -- so closing still pays.
                // `desired` remains EffectiveEngagementRange (Phase 2), not PreferredBandUpper.
                float terminal = 0;
                foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
                {
                    float range = Math.Max(0, ranges[enemy.Id]);
                    float desired = profile.IsContactSeeking
                        ? 1f
                        : Math.Max(1f, profile.EffectiveEngagementRange);
                    // TurnsUntilWeReachTarget: own speed, own preferred band.
                    float turnsToAct = Math.Max(0, range - desired)
                        / Math.Max(0.1f, profile.MoveSpeed);
                    float rangeWhenActing = Math.Min(range, Math.Max(desired, 0f));
                    float exchangeRate = EvaluateExchangeRate(
                        squad,
                        enemy,
                        profile,
                        profiles[enemy.Id],
                        frames,
                        rangeWhenActing,
                        // A squad that has taken position stands and shoots, so the terminal is
                        // priced at the Hold retention rather than at a moving policy's.
                        outgoingRetention: 1f,
                        targetSpeed: 0f);
                    terminal += exchangeRate * ExpectedRemainingTurns
                        / (1f + turnsToAct);
                    // Terminal value represents attainable action opportunity, not generic distance:
                    // a squad with no usable offense receives no reward merely for closing.
                }
                return terminal;
            }
            float best = float.MinValue;
            // A future state chooses again.  This is the bounded policy comparison the previous
            // fixed baseline rollout lacked: root Hold may continue with Run, root Run may continue
            // with Hold/fire, and Jog is valued only at its aggregate moving-fire retention.
            foreach (EngagementOptionKind policy in new[]
            {
                EngagementOptionKind.Hold,
                EngagementOptionKind.JogToward,
                EngagementOptionKind.RunToward
            })
            {
                float exchange = 0;
                Dictionary<int, float> nextRanges = [];
                foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
                {
                    BattleSquadCapabilityProfile opposing = profiles[enemy.Id];
                    float range = Math.Max(0, ranges[enemy.Id]);
                    float outgoingRetention = policy switch
                    {
                        EngagementOptionKind.Hold => 1f,
                        EngagementOptionKind.JogToward => 0.65f,
                        _ => 0f
                    };
                    float ourMotion = PolicyRangeDelta(profile, range, policy);
                    exchange += EvaluateExchangeRate(
                        squad,
                        enemy,
                        profile,
                        opposing,
                        frames,
                        range,
                        outgoingRetention,
                        targetSpeed: Math.Max(0, -ourMotion));
                    float theirMotion = (frames[squad.Id].Role
                        is EngagementSquadRole.Pursuit or EngagementSquadRole.Standoff)
                        ? Math.Max(0, frames[squad.Id].QuarryRunSpeed)
                        : BaselineRangeDelta(opposing, frames[enemy.Id].Role, range);
                    nextRanges[enemy.Id] = Math.Max(0, range + ourMotion + theirMotion);
                }
                float value = exchange + EngagementFutureDiscount * EvaluateBestContinuation(
                    squad, profile, profiles, frames, enemies, nextRanges, depth - 1);
                if (value > best) best = value;
            }
            return best == float.MinValue ? 0 : best;
        }

        // Projected own motion for one lookahead policy. Phase 2
        // (Design/Active/EngagementScoringOverhaul.md): `desired` is the effectiveness-derived
        // EffectiveEngagementRange, not PreferredBandUpper. PreferredBandUpper is the weapon's
        // MAXIMUM range, so any range already inside reach yielded `range > desired == false` and
        // this returned 0 own-motion for EVERY policy -- the lookahead could not see its own
        // movement at all.
        private static float PolicyRangeDelta(
            BattleSquadCapabilityProfile profile,
            float range,
            EngagementOptionKind policy)
        {
            if (policy == EngagementOptionKind.Hold) return 0;
            float speed = profile.MoveSpeed * (policy == EngagementOptionKind.JogToward
                ? JogSpeedMultiplier
                : 1f);
            float desired = profile.IsContactSeeking
                ? 1f
                : Math.Max(1f, profile.EffectiveEngagementRange);
            return range > desired ? -Math.Min(speed, range - desired) : 0;
        }

        // `opposingRole` is the target's SquadEngagementFrame.Role for the CURRENT turn (Layer 1's
        // frozen withdrawal declaration -- see BattleEngagementFrameBuilder.BuildSide), not morale.
        // Bound and Routing squads have been ordered to run at full MoveSpeed away from the fight
        // (see BuildSide's quarryRunSpeed switch, which uses exactly these two roles); that takes
        // precedence over IsContactSeeking, so a melee-only profile does not get projected as
        // charging while its own side has it fleeing. Cover/RearGuard hold position to screen the
        // withdrawal (quarryRunSpeed 0 for those) and fall through to the normal band logic below --
        // Phase 1, Design/Active/EngagementScoringOverhaul.md.
        private static float BaselineRangeDelta(
            BattleSquadCapabilityProfile profile,
            EngagementSquadRole opposingRole,
            float range)
        {
            if (opposingRole is EngagementSquadRole.Bound or EngagementSquadRole.Routing)
            {
                return profile.MoveSpeed;
            }
            if (profile.IsContactSeeking) return range > 1
                ? -Math.Min(profile.MoveSpeed, range - 1)
                : 0;
            // Phase 2 audit: kept on the PreferredBand pair rather than EffectiveEngagementRange.
            // This is a hysteresis BAND with a matched lower edge (PreferredBandLower is derived
            // from the same reach), and it must agree with
            // BattleEngagementFrameBuilder.Baseline's posture choice, which uses the same pair.
            // Substituting only the upper edge could invert the band whenever the effectiveness-
            // derived range falls below PreferredBandLower.
            if (range > profile.PreferredBandUpper + 1)
            {
                return -Math.Min(profile.MoveSpeed * JogSpeedMultiplier,
                    range - profile.PreferredBandUpper);
            }
            if (range < profile.PreferredBandLower - 1)
            {
                return Math.Min(profile.MoveSpeed * WalkSpeedMultiplier,
                    profile.PreferredBandLower - range);
            }
            return 0;
        }

        /// <summary>
        /// PHASE 5c (Design/Active/EngagementScoringOverhaul.md). One ply's net battle-value
        /// exchange between <paramref name="squad"/> and <paramref name="enemy"/> at a projected
        /// centroid separation. This is what makes `outgoing` and `future` commensurable: both are
        /// now <c>hit * (takeOut + lambda * woundProgress) * targetBV</c>, summed per-soldier.
        ///
        /// <para>The predecessor, <c>AggregateRemovalRate</c>, was a CAPABILITY PROXY: a flat 10%
        /// of the ATTACKER'S OWN <c>UsableRangedBattleValue</c> per turn, with the defender read
        /// only as a cap and no hit, penetration, armour or constitution input anywhere. In the
        /// reference trace it asserted 8.198 BV/turn for a squad whose honest immediate-fire value
        /// was 0.001 -- the two halves of one score disagreeing about the same squad's shooting by
        /// a factor of ~8,000.</para>
        ///
        /// <para>PAIR WEIGHTS vs ARGMAX -- the question Phase 4 deliberately left open, resolved
        /// here ASYMMETRICALLY, because the two halves are asking different questions.</para>
        ///
        /// <para>OUTGOING uses the argmax table and NO <c>PairWeights</c>. The table is already
        /// target-selected: each of our soldiers contributes its single best target's removal to
        /// exactly one enemy squad's cell, so summing the cells over enemies reconstructs this
        /// squad's true whole-squad removal per turn -- the same quantity, computed the same way,
        /// as `outgoing`. <c>PairWeights</c> is a normalized allocation (it sums to 1 across enemy
        /// squads); multiplying an already-allocated rate by it would divide the squad's fire
        /// twice and systematically understate every shooting option. The lookahead does not go
        /// blind to a flank threat by this: the threat still appears in the INCOMING half below,
        /// which is where a distant enemy squad actually costs us something.</para>
        ///
        /// <para>INCOMING keeps <c>PairWeights</c>, because there it genuinely is an allocation:
        /// the question is what share of that enemy squad's fire lands on US rather than on our
        /// neighbours, and its argmax cell cannot answer that -- it is a single frozen choice made
        /// against this turn's geometry, so reading it directly would swing our projected incoming
        /// between "all of it" and "none of it" as the enemy's best target flickered between our
        /// squads. So: the enemy's WHOLE-squad rate at our projected separation, times our share.
        /// This mirrors the pre-Phase-5 structure exactly; only the rate itself became honest.</para>
        ///
        /// <para>MELEE is untouched by the table, which is ranged-only, and keeps its capability
        /// proxy (13% of the attacker's usable melee battle value inside 1.5). Dropping it would
        /// make melee-only enemies read as harmless in the lookahead. The outgoing melee half keeps
        /// its <c>PairWeights</c> allocation -- a squad can only be in contact with so many enemies
        /// at once -- and the two halves are combined with <c>max</c>, as before.</para>
        /// </summary>
        private float EvaluateOutgoingExchangeRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range)
        {
            float outgoingAllocation = frames.TryGetValue(
                squad.Id, out SquadEngagementFrame ourFrame)
                    ? ourFrame.PairWeights.GetValueOrDefault(enemy.Id)
                    : 0f;
            return Math.Min(
                opposing.TotalAbleBattleValue,
                Math.Max(
                    PairRangedRemovalRate(squad, enemy.Id, range),
                    outgoingAllocation * MeleeRemovalRate(profile, range)));
        }

        private float EvaluateExchangeRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range,
            float outgoingRetention,
            float targetSpeed)
        {
            float incomingAllocation = frames.TryGetValue(
                enemy.Id, out SquadEngagementFrame theirFrame)
                    ? theirFrame.PairWeights.GetValueOrDefault(squad.Id)
                    : 0f;

            float outgoing = EvaluateOutgoingExchangeRate(
                squad,
                enemy,
                profile,
                opposing,
                frames,
                range);
            float incomingBulk = PostureBulkMultiplier(
                frames.GetValueOrDefault(enemy.Id)?.BaselinePosture
                    ?? EngagementOptionKind.Hold);
            float incoming = float.IsPositiveInfinity(incomingBulk)
                ? 0
                : incomingAllocation * Math.Min(
                    profile.TotalAbleBattleValue,
                    Math.Max(
                        TotalRangedRemovalRate(
                            enemy,
                            range,
                            targetSpeed,
                            incomingBulk),
                        MeleeRemovalRate(opposing, range)));
            return (outgoing * outgoingRetention) - incoming;
        }

        private static float PostureBulkMultiplier(EngagementOptionKind posture)
        {
            return posture switch
            {
                EngagementOptionKind.StepBack or EngagementOptionKind.StepForward =>
                    WalkBulkMultiplier,
                EngagementOptionKind.JogToward => FullBulkMultiplier,
                EngagementOptionKind.CloseToContact or EngagementOptionKind.RunToward =>
                    float.PositiveInfinity,
                _ => 0f
            };
        }

        // The one surviving piece of the old capability proxy. The Phase 4/5 removal-rate table is
        // ranged-only, so melee threat is still priced from the attacker's usable melee battle
        // value. PHASE 6 did not replace it -- it is a per-turn exchange rate at contact, not a
        // range question, and the removal-rate table has no melee side to read. What Phase 6 did do
        // is share the coefficient: BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange
        // prices the SAME melee threat (discounted by arrival time) when it derives a standoff, and
        // the two must not disagree about what a charge landing is worth.
        private static float MeleeRemovalRate(
            BattleSquadCapabilityProfile attacker,
            float range)
        {
            return range <= 1.5f
                ? attacker.UsableMeleeBattleValue
                    * BattleModifiersUtil.MeleeContactRemovalFraction
                : 0f;
        }

        /// <summary>
        /// This squad's per-turn removal against ONE enemy squad at a projected separation, from
        /// the Phase 4 table. An absent cell is a genuine 0: no soldier's best target is in that
        /// squad, so the squad is not shooting at it.
        /// </summary>
        private float PairRangedRemovalRate(
            BattleSquad shooterSquad,
            int targetSquadId,
            float range)
        {
            return GetPairRemovalRates(shooterSquad)
                .TryGetValue(targetSquadId, out SquadPairRemovalRate rate)
                    ? rate.RateAtRange(range)
                    : 0f;
        }

        /// <summary>
        /// This squad's whole-squad per-turn removal at a projected separation -- every cell of its
        /// table row summed. Used for the INCOMING half, where the consumer then takes its own
        /// <c>PairWeights</c> share of the total.
        /// </summary>
        private float TotalRangedRemovalRate(
            BattleSquad shooterSquad,
            float range,
            float targetSpeed,
            float shooterBulkMultiplier)
        {
            float total = 0f;
            foreach (SquadPairRemovalRate rate in GetPairRemovalRates(shooterSquad).Values)
            {
                total += rate.RateAtRange(range, targetSpeed, shooterBulkMultiplier);
            }
            return total;
        }

        private static readonly IReadOnlyDictionary<int, SquadPairRemovalRate>
            EmptyPairRemovalRates = new Dictionary<int, SquadPairRemovalRate>();

        /// <summary>
        /// Phase 4 removal-rate table (Design/Active/EngagementScoringOverhaul.md). Returns, for
        /// one shooter squad, the per-enemy-squad removal rates -- expected enemy battle value
        /// removed per turn, in the SAME currency as `outgoing`, rescalable to any projected range
        /// in closed form. Memoized for the turn in the shared
        /// <see cref="BattlePlanningContext"/>, so repeated requests across options, plies and
        /// worker planners cost one build.
        ///
        /// <para>PHASE 5 WIRED THIS INTO PLANNING. <c>AggregateRemovalRate</c> is gone;
        /// <see cref="EvaluateExchangeRate"/> reads this table for both halves of every lookahead
        /// ply and for the depth-0 terminal, which is what finally puts `outgoing` and `future` in
        /// one currency. See <see cref="SquadPairRemovalRate"/> for the aggregation semantics.</para>
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
            foreach (BattleSoldier soldier in shooterSquad.AbleSoldiers
                .OrderBy(member => member.Soldier.Id))
            {
                if (!IsPlaced(soldier) || soldier.EquippedRangedWeapons.Count == 0)
                {
                    continue;
                }
                // Stationary, un-aimed, no-bulk reference posture -- see SquadPairRemovalRate.
                // Every EvaluateRangedTarget this walks is already memoized in the shared context.
                RangedTargetEvaluation evaluation = SelectBestRangedTarget(
                    soldier, bulkMultiplier: 0f)
                    // PHASE 5. SelectBestRangedTarget only considers enemies inside weapon reach,
                    // so a squad that is currently out of range would get an EMPTY row and the
                    // lookahead would price every future turn at 0 -- no reason to ever close, at
                    // any distance. The old capability proxy did not have that hole: it recomputed
                    // its range factor at the PROJECTED range and became positive as soon as the
                    // squads came inside reach. Capturing a term against the nearest enemy anyway
                    // restores exactly that gradient honestly -- PairRemovalTerm gates the rate to
                    // 0 beyond MaximumEffectiveRange, so this contributes nothing until the
                    // lookahead projects the squads into range, and then contributes the real
                    // hit x removal x BV at that projected range.
                    ?? EvaluateNearestOutOfReachTarget(soldier);
                // A CONE BEARER IS A SHOOTER. Both target selectors above skip
                // IsTemplateWeapon, so a soldier whose only weapon is a flamer used to
                // contribute NO term and his squad's whole row read rate 0 at every range --
                // the squad was modelled as unarmed. Everything downstream then followed from
                // that: EvaluateArrivalTimeValue saw an outgoing rate of 0 where the squad
                // stood and paid it to run to contact, so a flamer bearer burning a target for
                // 0.775 battle value at 10 yards abandoned the burst to charge (both
                // template-weapon planner tests, 2026-08-07).
                //
                // Take whichever branch the live planner would take -- PlanRangedAction gives
                // the cone ties, so this does too -- and price the burst the way the cone
                // actually resolves: one application per victim, no to-hit roll.
                TemplateFiringLineEvaluation coneLine = HasReadyTemplateWeapon(soldier)
                    ? SelectBestTemplateFiringLine(soldier)
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
                if (!termsByTargetSquad.TryGetValue(targetSquadId, out List<PairRemovalTerm> terms))
                {
                    terms = [];
                    termsByTargetSquad[targetSquadId] = terms;
                }
                terms.Add(BuildPairRemovalTerm(soldier, evaluation));
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
        /// the moment the lookahead projects the squads inside reach. Longest-reaching loaded
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
                : EvaluateRangedTarget(soldier, nearest, weapon, nearestDistance, 0f);
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
                    ? BuildTakeOutLocationTerms(
                        victim, armor * template.ArmorMultiplier, template.WoundMultiplier)
                    : null;
            (float takeOut, float progress) =
                CalculateRangedHitRemoval(victim, weapon, victimRange, armor);
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

        /// <summary>
        /// Stands in for "this weapon does not roll to hit". Far enough above
        /// <see cref="HitRollMean"/> that the burst model's first-shot threshold is met with
        /// certainty at any range and under any bulk multiplier, but finite, so the shared
        /// rescaling arithmetic applies to it unchanged.
        /// </summary>
        private const float ConeCertainHitTotal = 1_000f;

        private static PairRemovalTerm BuildPairRemovalTerm(
            BattleSoldier shooter,
            RangedTargetEvaluation evaluation)
        {
            RangedWeaponTemplate template = evaluation.Weapon.Template;
            float effectiveArmor = (evaluation.Target.Armor?.Template.ArmorProvided ?? 0f)
                * template.ArmorMultiplier;
            // Only a degrading weapon needs the K_loc vector. For a non-degrading weapon
            // CalculateDamageAtRange is a flat DamageMultiplier, so takeOut0 IS takeOut(r) for
            // every r -- exact, not an approximation.
            IReadOnlyList<TakeOutLocationTerm> takeOutTerms =
                template.DoesDamageDegradeWithRange
                    ? BuildTakeOutLocationTerms(
                        evaluation.Target, effectiveArmor, template.WoundMultiplier)
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
                takeOutTerms);
        }

        private static float EvaluateScreenRoleTerm(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            ValueTuple<float, float> endpoint,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            if (kind != EngagementOptionKind.MoveToInterpose
                || !frame.ProtectedSquadId.HasValue
                || !frame.ScreenThreatSquadId.HasValue)
            {
                return 0;
            }
            BattleSquad threat = enemies.FirstOrDefault(
                candidate => candidate.Id == frame.ScreenThreatSquadId.Value);
            if (threat == null) return 0;
            BattleSquadCapabilityProfile threatProfile = profiles[threat.Id];
            // TurnsUntilThreatReachesInterceptPoint: the screener's own projected endpoint to the
            // threat's speed, no ceiling and no preferred-range subtraction (distinct from the melee
            // charge-arrival discount above -- see Design/Active/EngagementScoringOverhaul.md
            // Phase 0).
            float interceptDistance = Distance(
                endpoint, BattleEngagementFrameBuilder.Centroid(threat));
            float turnsUntilThreatReachesInterceptPoint =
                interceptDistance / Math.Max(0.1f, threatProfile.MoveSpeed);
            float holding = Math.Min(1f,
                (profile.UsableMeleeBattleValue + profile.TotalAbleBattleValue * 0.25f)
                / Math.Max(1, threatProfile.UsableMeleeBattleValue));
            float capacity = Math.Min(1f,
                profile.ContactCapacity / (float)Math.Max(1, threatProfile.ContactCapacity));
            float interceptDiscount = 1f / (1f + turnsUntilThreatReachesInterceptPoint);
            return Math.Min(
                threatProfile.UsableMeleeBattleValue,
                profiles[frame.ProtectedSquadId.Value].TotalAbleBattleValue)
                * holding * capacity * interceptDiscount;
        }

        /// <summary>
        /// The rate at which the quarry is opening the range while this squad closes it, or 0 when
        /// nothing is running away. Any term that prices "how much nearer does this option get me"
        /// has to net this out, or it pays for gross closing the quarry immediately undoes.
        /// </summary>
        /// <remarks>
        /// QuarryRunSpeed is only populated for a Pursuit frame; on an ordinary approach the primary
        /// is not fleeing, so there is no withdrawal rate to subtract.
        /// </remarks>
        private static float QuarryWithdrawalRate(
            SquadEngagementFrame frame,
            EngagementSquadRole? quarryRole) =>
            frame.Role == EngagementSquadRole.Pursuit
                && quarryRole is EngagementSquadRole.Bound or EngagementSquadRole.Routing
                    ? Math.Max(0, frame.QuarryRunSpeed)
                    : 0;

        private bool HasPursuitFireCommitment(
            BattleSquad squad,
            SquadEngagementFrame frame,
            BattleSquad primary)
        {
            if (frame.Role != EngagementSquadRole.Pursuit
                || squad.LastEngagementOptionKind != EngagementOptionKind.Hold
                || primary == null)
            {
                return false;
            }

            // Aim is the authoritative commitment state. It is copied in the normal battle
            // snapshot and is cleared by ShootAction or by movement, so this remains true exactly
            // while the squad has something invested in the current aimed shot. Re-checking the
            // existing-aim viability gate releases the commitment if the target dies, leaves the
            // weapon's range, or becomes a bad shot.
            return squad.AbleSoldiers.Any(soldier =>
                soldier.Aim is ValueTuple<int, RangedWeapon, int> aim
                && _soldierMap.TryGetValue(aim.Item1, out BattleSoldier target)
                && target.BattleSquad?.Id == primary.Id
                && IsExistingAimStillViable(soldier));
        }

        /// <summary>
        /// Values the aimed shot that a pursuit squad can complete after holding for the full
        /// stationary fire cycle. The range projection is deliberately conservative: the quarry
        /// is assumed to open by its full withdrawal speed for all five turns, while the actual
        /// target evaluator supplies the hit, armor, wound-progress, burst, and friendly-fire
        /// terms. The future shot is discounted by the same continuation discount as the rest of
        /// engagement scoring, so this is a present-value nudge rather than free immediate fire.
        /// </summary>
        private float EvaluatePursuitFireWindowValue(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquadCapabilityProfile profile,
            BattleSquad primary,
            EngagementSquadRole? quarryRole)
        {
            if (kind != EngagementOptionKind.Hold
                || frame.Role != EngagementSquadRole.Pursuit
                || profile.IsContactSeeking
                || primary == null)
            {
                return 0;
            }

            float quarrySpeed = QuarryWithdrawalRate(frame, quarryRole);
            float projectedOpening = quarrySpeed * PursuitFireWindowTurns;
            Dictionary<int, float> awardedByTarget = [];
            float projectedValue = 0;

            foreach (BattleSoldier shooter in squad.AbleSoldiers.OrderBy(soldier => soldier.Soldier.Id))
            {
                if (!IsPlaced(shooter) || shooter.EquippedRangedWeapons.Count == 0)
                {
                    continue;
                }

                RangedTargetEvaluation best = null;
                foreach (BattleSoldier target in primary.AbleSoldiers
                    .Where(candidate => candidate.IsCombatEffective && IsPlaced(candidate))
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    float currentRange = _grid.GetDistanceBetweenSoldiers(
                        shooter.Soldier.Id,
                        target.Soldier.Id);
                    float projectedRange = currentRange + projectedOpening;
                    foreach (RangedWeapon weapon in shooter.EquippedRangedWeapons
                        .Where(candidate => !candidate.Template.IsTemplateWeapon
                            && candidate.LoadedAmmo > 0
                            && projectedRange <= candidate.Template.MaximumRange)
                        .OrderByDescending(candidate => candidate.Template.DamageMultiplier)
                        .ThenBy(candidate => candidate.Template.Id))
                    {
                        RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                            shooter,
                            target,
                            weapon,
                            projectedRange,
                            weapon.Template.Accuracy + FullAimBonusTurns + 1,
                            quarrySpeed);
                        if (evaluation.HitProbability <= StickyMinimumHitProbability
                            || evaluation.Score <= 0)
                        {
                            continue;
                        }
                        if (best == null
                            || evaluation.Score > best.Score
                            || (Math.Abs(evaluation.Score - best.Score) < 0.0001f
                                && evaluation.Target.Soldier.Id < best.Target.Soldier.Id))
                        {
                            best = evaluation;
                        }
                    }
                }

                if (best == null)
                {
                    continue;
                }

                float alreadyAwarded = awardedByTarget.GetValueOrDefault(best.Target.Soldier.Id);
                float remainingValue = Math.Max(0, GetBattleValue(best.Target) - alreadyAwarded);
                float contribution = Math.Min(remainingValue, Math.Max(0, best.Score));
                if (contribution <= 0)
                {
                    continue;
                }
                awardedByTarget[best.Target.Soldier.Id] = alreadyAwarded + contribution;
                projectedValue += contribution;
            }

            return projectedValue
                * (float)Math.Pow(EngagementFutureDiscount, PursuitFireWindowTurns);
        }

        private static float EvaluatePursuitContactProgress(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquadCapabilityProfile profile,
            BattleSquad primary,
            float feasibleSpeed,
            EngagementSquadRole? quarryRole)
        {
            // Pursuit is not the only posture in which closing speed IS the decision. A melee-only
            // squad on an ordinary approach has no shot to trade away and no legal retreat, so the
            // only thing separating StepForward from RunToward is how soon it arrives -- yet with
            // role=Normal this term used to return 0 and leave the choice to `incoming` and
            // `future`, both of which get slightly WORSE the closer the squad gets. Observed
            // 2026-08-04 (Xibarrus Nu): two identical Abominants ~490 yards out scored Jog and Run
            // within 8e-4 of each other and split the tie-break, one jogging and one running.
            // `arrival_value` cannot carry this: its 1/(1+turns) discount flattens to a ~1e-4
            // difference at that range, far below the noise it is competing against.
            bool closingIsTheOnlyPlay = HasNoViableRangedOption(profile);
            if (frame.Role != EngagementSquadRole.Pursuit && !closingIsTheOnlyPlay
                || primary == null
                || kind == EngagementOptionKind.Hold)
            {
                return 0;
            }
            ValueTuple<float, float> target = BattleEngagementFrameBuilder.Centroid(primary);
            float before = Distance(BattleEngagementFrameBuilder.Centroid(squad), target);
            float quarrySpeed = QuarryWithdrawalRate(frame, quarryRole);
            float attainable = profile.IsContactSeeking
                ? profile.UsableMeleeBattleValue
                : profile.UsableRangedBattleValue;

            // A ranged pursuit does not have a binary "outside reach / inside reach" need. Its
            // reason to keep running should taper through the authored preferred band: full at
            // PreferredBandUpper, zero at PreferredBandLower. This removes the discontinuous
            // score jump that made Hold and Run alternate when a fleeing quarry crossed one
            // threshold by a few yards. The second factor prices only net closing speed, so a
            // jog that the quarry outruns still receives no chase credit.
            if (frame.Role == EngagementSquadRole.Pursuit
                && !profile.IsContactSeeking
                && profile.PreferredBandUpper > profile.PreferredBandLower)
            {
                float bandWidth = Math.Max(
                    0.1f,
                    profile.PreferredBandUpper - profile.PreferredBandLower);
                float bandPressure = Math.Clamp(
                    (before - profile.PreferredBandLower) / bandWidth,
                    0,
                    1);
                float maximumNetClosing = Math.Max(0, profile.MoveSpeed - quarrySpeed);
                float actualNetClosing = Math.Max(0, feasibleSpeed - quarrySpeed);
                float closingFraction = maximumNetClosing <= 0
                    ? 0
                    : Math.Clamp(actualNetClosing / maximumNetClosing, 0, 1);
                return attainable * bandPressure * closingFraction;
            }

            // Deliberately still reach, not EffectiveEngagementRange (Phase 2 audit, RE-CHECKED IN
            // PHASE 6 and unchanged): this term prices recovering a LOST firing solution against a
            // quarry beyond the lookahead horizon, so the threshold that matters is "can I shoot at
            // all", and it must go to 0 as soon as the quarry is back in reach rather than paying
            // for further closing.
            //
            // Phase 6 made the derived band a real quantity, which strengthens rather than weakens
            // the case for reach here. The band answers "where do I want to STAND", and it is
            // derived from removal MINUS incoming -- a withdrawing quarry's incoming is precisely
            // what a pursuer has already decided to accept, so pursuing to the standoff band would
            // stop the chase at a distance chosen by a threat model that does not apply. Worse, the
            // band can legitimately be 0 (close) or, against a tough enemy, several hundred yards;
            // either would make pursuit progress mean something different per matchup. Reach is the
            // one threshold with a fixed meaning for a pursuit: past it there is no shot at all.
            float desiredRange = profile.IsContactSeeking
                ? 1f
                : Math.Max(1f, profile.PreferredBandUpper);
            if (before <= desiredRange) return 0;

            // Closing is not valuable only to assault troops. A ranged squad that has lost its
            // firing band must invest movement now to recover a later shot. The short exchange
            // rollout cannot express that once the quarry is more than its two-turn horizon away,
            // so price the fraction of one useful full-speed stride completed by this option.
            // This keeps Hold competitive while it can actually fire, makes Run valuable after
            // contact is lost, and still lets the existing quarry-speed penalty reject a Jog that
            // would fall farther behind.
            float usefulStride = Math.Min(
                Math.Max(0, profile.MoveSpeed - quarrySpeed),
                before - desiredRange);
            float progress = Math.Min(
                usefulStride,
                Math.Max(0, feasibleSpeed - quarrySpeed));
            return usefulStride <= 0
                ? 0
                : attainable * progress / Math.Max(0.1f, usefulStride);
        }

        private bool SuppressHqAdvance(BattleSquad squad, EngagementOptionKind kind)
        {
            if (kind is not (EngagementOptionKind.StepForward
                or EngagementOptionKind.JogToward
                or EngagementOptionKind.CloseToContact
                or EngagementOptionKind.RunToward))
            {
                return false;
            }
            if (squad.Squad?.SquadTemplate?.SquadType.HasFlag(
                    Models.Squads.SquadTypes.HQ) != true)
            {
                return false;
            }
            bool side = _grid.GetSoldierSide(squad.AbleSoldiers[0].Soldier.Id);
            return _soldierMap.Values
                .Select(soldier => soldier.BattleSquad)
                .Where(candidate => candidate != null && candidate.Id != squad.Id)
                .DistinctBy(candidate => candidate.Id)
                .Any(candidate => candidate.Status == BattleSquadStatus.Active
                    && candidate.Squad?.SquadTemplate?.SquadType.HasFlag(
                        Models.Squads.SquadTypes.HQ) != true
                    && candidate.AbleSoldiers.Any(member => IsPlaced(member)
                        && _grid.GetSoldierSide(member.Soldier.Id) == side)
                    && !candidate.IsInMelee);
        }

        private static BattleSquad ResolvePrimary(
            SquadEngagementFrame frame,
            IReadOnlyCollection<BattleSquad> preferredTargets,
            IReadOnlyCollection<BattleSquad> allTargets)
        {
            IEnumerable<BattleSquad> targets = (preferredTargets ?? [])
                .Concat(allTargets ?? [])
                .Where(target => target != null)
                .DistinctBy(target => target.Id);
            if (frame.PrimaryCounterpartSquadId.HasValue)
            {
                BattleSquad primary = targets.FirstOrDefault(
                    target => target.Id == frame.PrimaryCounterpartSquadId.Value);
                if (primary != null) return primary;
            }
            return targets.OrderBy(target => target.Id).FirstOrDefault();
        }

        private static float Distance(
            ValueTuple<float, float> first,
            ValueTuple<float, float> second)
        {
            float dx = first.Item1 - second.Item1;
            float dy = first.Item2 - second.Item2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Layer 2.5 declaration. Called for every squad before Layer 3.</summary>
        internal void DeclareEngagementDecision(SquadEngagementDecision decision)
        {
            BattleSquad squad = decision.Squad;
            squad.MovementTier = decision.Chosen.Tier;
            squad.WithdrawalRole = decision.Frame.Role switch
            {
                EngagementSquadRole.Cover => WithdrawalRole.Cover,
                EngagementSquadRole.RearGuard => WithdrawalRole.RearGuard,
                EngagementSquadRole.Bound => WithdrawalRole.Bound,
                EngagementSquadRole.Routing => WithdrawalRole.Routing,
                _ => WithdrawalRole.None
            };
            squad.LastEngagementOptionKind = decision.Chosen.Kind;
            squad.LastScreenThreatSquadId = decision.Frame.ScreenThreatSquadId;
            squad.LastProtectedSquadId = decision.Frame.ProtectedSquadId;
            ApplyDeclaredMovementState(squad);

            // A declaration receives only the speed its feasible projection actually covers. This
            // prevents a blocked or zero-distance move from receiving free ranged evasion.
            float tierReference = squad.GetSquadMove() * (decision.Chosen.Tier switch
            {
                SquadMovementTier.Walk => WalkSpeedMultiplier,
                SquadMovementTier.Jog => JogSpeedMultiplier,
                SquadMovementTier.Run or SquadMovementTier.InMelee => 1f,
                _ => 0f
            });
            float fraction = tierReference <= 0
                ? 0
                : Math.Clamp(decision.Chosen.FeasibleSpeed / tierReference, 0, 1);
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                soldier.CurrentSpeed *= fraction;
                if (soldier.CurrentSpeed <= 0) soldier.IsRunning = false;
            }
        }

        /// <summary>Layer 3: constructs the existing per-soldier actions for a declared option.</summary>
        internal void BuildEngagementActions(SquadEngagementDecision decision)
        {
            BattleSquad squad = decision.Squad;
            EngagementOptionKind kind = decision.Chosen.Kind;
            BattleSquad primary = ResolvePrimary(
                decision.Frame,
                decision.RoleTargets,
                _soldierMap.Values.Select(soldier => soldier.BattleSquad).DistinctBy(s => s.Id).ToList());
            // Logged before the role dispatch, because four of the seven roles — BreakOff,
            // Routing, Bound and Pursuit — return without ever reaching an action builder. Those
            // squads still score their (force-masked) option set, and for Pursuit that score IS
            // the posture decision, so discarding the table left the roles that hang a battle as
            // the only ones with no scored-option trace. Nothing between here and the former call
            // sites touches another squad's LastEngagementOptionKind, so enemy_revealed is
            // unchanged by the move.
            LogEngagementOptions(decision);
            if (decision.Frame.Role == EngagementSquadRole.BreakOff) return;
            if (decision.Frame.Role == EngagementSquadRole.Routing)
            {
                PrepareRoutingActions(squad);
                return;
            }
            if (decision.Frame.Role == EngagementSquadRole.Bound)
            {
                PrepareBoundActions(squad, decision.Frame.FixedHeading ?? 0);
                return;
            }
            // CloseToContact is also the ordinary semantic "run until contact is possible" option
            // for distant squads. Only convert it into a deferred charge when contact is actually
            // reachable this turn; otherwise preserve the selected moving root action (reload,
            // ready, and similar run-legal utility) while making a normal directed move.
            if (squad.IsInMelee
                || kind == EngagementOptionKind.CloseToContact
                    && decision.Chosen.Tier == SquadMovementTier.InMelee)
            {
                if (primary == null) return;
                foreach (BattleSoldier soldier in squad.AbleSoldiers
                    .OrderBy(member => member.Soldier.Id))
                {
                    MeleeWeapon meleeWeaponToReady = GetFirstUsableMeleeWeapon(soldier);
                    if (soldier.EquippedMeleeWeapons.Count == 0 && meleeWeaponToReady != null)
                    {
                        _shootActions.Add(new ReadyMeleeWeaponAction(soldier, meleeWeaponToReady));
                    }
                }
                _moveActions.Add(new SquadChargeIntentAction(
                    squad,
                    primary,
                    state => ResolveSquadChargeIntent(squad, primary, state)));
                return;
            }
            if (kind == EngagementOptionKind.Hold)
            {
                ExecutePlannedRootActions(decision);
            }
            else
            {
                PrepareDirectedMovingActions(squad, decision, primary);
            }
        }

        private void PrepareDirectedMovingActions(
            BattleSquad squad,
            SquadEngagementDecision decision,
            BattleSquad primary)
        {
            Dictionary<int, PlannedSoldierAction> actions = (decision.Chosen.RootActions ?? [])
                .ToDictionary(action => action.SoldierId);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                ValueTuple<int, int> line = MovementLineFor(
                    soldier,
                    decision.Chosen.Kind,
                    decision.Frame,
                    primary,
                    decision.Chosen.IntendedDestination);
                ValueTuple<int, int> direction = AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, decision.Chosen.Tier),
                    line,
                    decision.Chosen.Tier);
                if (actions.TryGetValue(soldier.Soldier.Id, out PlannedSoldierAction action))
                {
                    ExecutePlannedRootAction(action);
                }
            }
        }

        private void ExecutePlannedRootActions(SquadEngagementDecision decision)
        {
            foreach (PlannedSoldierAction action in (decision.Chosen.RootActions ?? [])
                .OrderBy(candidate => candidate.SoldierId))
            {
                ExecutePlannedRootAction(action);
            }
        }

        private void ExecutePlannedRootAction(PlannedSoldierAction plan)
        {
            if (!_soldierMap.TryGetValue(plan.SoldierId, out BattleSoldier soldier)) return;
            BattleSoldier target = plan.TargetId.HasValue
                && _soldierMap.TryGetValue(plan.TargetId.Value, out BattleSoldier foundTarget)
                    ? foundTarget
                    : null;
            RangedWeapon weapon = plan.WeaponTemplateId.HasValue
                ? soldier.EquippedRangedWeapons
                    .Concat(soldier.RangedWeapons)
                    .FirstOrDefault(candidate =>
                        candidate.Template.Id == plan.WeaponTemplateId.Value)
                : null;
            switch (plan.Kind)
            {
                case PlannedSoldierActionKind.Shoot when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _shootActions.Add(new ShootAction(
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.Id,
                        plan.Range,
                        plan.ShotsToFire,
                        plan.BulkMultiplier,
                        plan.AimMultiplier,
                        _grid,
                        _random));
                    break;
                case PlannedSoldierActionKind.Aim when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _shootActions.Add(new AimAction(soldier, target, weapon, _log));
                    break;
                case PlannedSoldierActionKind.Reload when weapon != null:
                    _shootActions.Add(new ReloadRangedWeaponAction(soldier, weapon));
                    break;
                case PlannedSoldierActionKind.Ready when weapon != null:
                    _shootActions.Add(new ReadyRangedWeaponAction(soldier, weapon));
                    break;
                case PlannedSoldierActionKind.AreaAttack when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _shootActions.Add(new AreaAttackAction(
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.Id,
                        _grid,
                        _random));
                    break;
                case PlannedSoldierActionKind.BlastAttack when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _shootActions.Add(new BlastAttackAction(
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.Id,
                        plan.Range,
                        plan.BulkMultiplier,
                        _grid,
                        _random));
                    EmitPlanDiagnostic(plan);
                    break;
            }
            LogSoldierAction(soldier, plan, target, weapon);
        }

        /// <summary>
        /// Per-soldier action trace: what this soldier was actually ordered to do, against whom,
        /// with what, and what the planner expected it to be worth.
        ///
        /// <para>WHY. Every other battle record is squad-level -- ENGAGE_EVAL reports which POSTURE a
        /// squad chose, never what the ten soldiers inside it then did with their turns. That left no
        /// way to answer "why did this marine throw a grenade instead of firing" from a log; the
        /// question had to be re-derived from the scoring code by hand. The expected-value fields are
        /// the same currency the posture score was built from, so a surprising action can be traced
        /// straight back to the number that justified it.</para>
        ///
        /// <para>Emitted for the MATERIALIZED action only. Root actions are planned once per
        /// candidate posture, so tracing at plan time would report several actions per soldier per
        /// turn, all but one of them hypothetical.</para>
        /// </summary>
        private void LogSoldierAction(
            BattleSoldier soldier,
            PlannedSoldierAction plan,
            BattleSoldier target,
            RangedWeapon weapon)
        {
            if (_log == null || plan.Kind == PlannedSoldierActionKind.None) return;
            List<KeyValuePair<string, string>> fields =
            [
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("squad", soldier.BattleSquad?.Id),
                BattleDecisionTrace.Field("action", plan.Kind),
                BattleDecisionTrace.Field("weapon", weapon?.Template.Name ?? "none"),
                BattleDecisionTrace.Field("target", target?.Soldier.Name ?? "none"),
                BattleDecisionTrace.Field("target_id", target?.Soldier.Id),
                BattleDecisionTrace.Field("range", plan.Range),
                BattleDecisionTrace.Field("shots", plan.ShotsToFire),
                BattleDecisionTrace.Field("enemy_bv", plan.ExpectedEnemyBattleValueRemoved),
                BattleDecisionTrace.Field("friendly_bv", plan.ExpectedFriendlyBattleValueLost),
                BattleDecisionTrace.Field("readiness", plan.ReadinessValue)
            ];
            string line = new BattleDecisionTrace("ACTION", fields).Render();
            lock (_log)
            {
                _log(line);
            }
        }

        /// <summary>
        /// Writes a planned action's pre-rendered trace, now that the action is known to be the one
        /// taken. Serialized on the shared <see cref="_log"/> delegate: materialization runs across
        /// worker threads and the sink (a List&lt;string&gt;.Add) is not thread-safe.
        /// </summary>
        private void EmitPlanDiagnostic(PlannedSoldierAction plan)
        {
            if (_log == null || plan.Diagnostic == null) return;
            lock (_log)
            {
                _log(plan.Diagnostic);
            }
        }

        private void LogEngagementOptions(SquadEngagementDecision decision)
        {
            if (!BattleLog.IsEnabled) return;
            float runnerUp = decision.Candidates
                .Where(candidate => !ReferenceEquals(candidate, decision.Chosen))
                .Select(candidate => candidate.Score)
                .DefaultIfEmpty(decision.Chosen.Score)
                .Max();
            // Identical for every candidate row, and it walks the whole soldier map to build a
            // squad lookup. Computed per row it made enabling the battle log cost
            // options x squads x turns x soldiers, which priced the trace out of the long
            // pursuit battles it exists to diagnose.
            string revealedEnemyChoices = RenderRevealedEnemyChoices(decision.Frame);
            foreach (EngagementOptionEvaluation candidate in decision.Candidates
                .OrderBy(option => option.Kind))
            {
                BattleLog.Write(new BattleDecisionTrace("ENGAGE_EVAL", new List<KeyValuePair<string, string>>
                {
                    BattleDecisionTrace.Field("turn", TraceTurnNumber),
                    BattleDecisionTrace.Field("side", TraceSideLabel ?? "none"),
                    BattleDecisionTrace.Field("squad", decision.Squad.Id),
                    BattleDecisionTrace.Field("role", decision.Frame.Role),
                    BattleDecisionTrace.Field("kind", candidate.Kind),
                    BattleDecisionTrace.Field("tier", candidate.Tier),
                    BattleDecisionTrace.Field("intended", candidate.IntendedDestination?.ToString() ?? "none"),
                    BattleDecisionTrace.Field("feasible_speed", candidate.FeasibleSpeed),
                    BattleDecisionTrace.Field("outgoing", candidate.ImmediateEnemyRemoval),
                    BattleDecisionTrace.Field("friendly_fire", candidate.ImmediateFriendlyFire),
                    BattleDecisionTrace.Field("readiness", candidate.ReadinessValue),
                    BattleDecisionTrace.Field("fire_window", candidate.FireWindowValue),
                    BattleDecisionTrace.Field("incoming", candidate.IncomingNow),
                    BattleDecisionTrace.Field("melee", candidate.MeleeNow),
                    BattleDecisionTrace.Field("future", string.Join(',', candidate.FutureExchange.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))),
                    BattleDecisionTrace.Field("arrival_value", candidate.ArrivalTimeValue),
                    BattleDecisionTrace.Field("role_term", candidate.RoleTerm),
                    BattleDecisionTrace.Field("commitment", candidate.ContactCommitmentCost),
                    BattleDecisionTrace.Field("hysteresis", candidate.Hysteresis),
                    BattleDecisionTrace.Field("score", candidate.Score),
                    BattleDecisionTrace.Field("chosen", candidate.Kind == decision.Chosen.Kind),
                    BattleDecisionTrace.Field("margin", decision.Chosen.Score - runnerUp),
                    BattleDecisionTrace.Field("baseline", decision.Frame.BaselinePosture),
                    BattleDecisionTrace.Field("enemy_revealed", revealedEnemyChoices)
                }).Render());
            }
        }

        private string RenderRevealedEnemyChoices(SquadEngagementFrame frame)
        {
            Dictionary<int, BattleSquad> squads = _soldierMap.Values
                .Select(soldier => soldier.BattleSquad)
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToDictionary(squad => squad.Id);
            string revealed = string.Join(',', frame.PairWeights.Keys
                .OrderBy(id => id)
                .Select(id => squads.TryGetValue(id, out BattleSquad squad)
                    ? $"{id}:{squad.LastEngagementOptionKind?.ToString() ?? "none"}"
                    : $"{id}:missing"));
            return string.IsNullOrEmpty(revealed) ? "none" : revealed;
        }

        private float NearestEnemyDistance(BattleSquad squad)
        {
            float min = float.MaxValue;
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int enemyId);
                if (enemyId != -1 && distance < min)
                {
                    min = distance;
                }
            }
            return min;
        }

        /// <summary>Plans a full-speed bound along the force's fixed withdrawal heading.</summary>
        public void PrepareBoundActions(BattleSquad squad, ushort withdrawalHeading)
        {
            squad.WithdrawalRole = WithdrawalRole.Bound;
            squad.MovementTier = SquadMovementTier.Run;
            ApplyDeclaredMovementState(squad);
            ValueTuple<int, int> direction = BattleForcePlanner.GetHeadingVector(withdrawalHeading);
            ValueTuple<int, int> movementLine = new(direction.Item1 * 10_000, direction.Item2 * 10_000);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                // A bound soldier caught in melee decides for himself whether to break contact.
                // Running is not free: he turns his back, so he defends with foot speed alone
                // (BattleSoldier.IsRunning). Withdrawal is an ordered movement, not a rout, so
                // unlike PrepareRoutingActions he is allowed the choice rather than pinned.
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id)
                    && DecideMeleeDisengagement(soldier).Choice
                        == MeleeDisengagementChoice.StandAndFight)
                {
                    AddMeleeActionsToBag(soldier);
                    continue;
                }

                AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, SquadMovementTier.Run),
                    movementLine,
                    SquadMovementTier.Run);
                AddPermittedRunUtilityActionToBag(soldier);
            }
        }

        /// <summary>
        /// Scores the melee a withdrawing soldier has been caught in, against the most dangerous
        /// enemy currently in contact. Both sides' chances are measured with the same
        /// <see cref="MeleeAttackAction.EstimateHitProbability"/> the live roll uses, so the
        /// decision cannot drift from the resolution it is predicting.
        /// </summary>
        private MeleeDisengagementPolicy.Result DecideMeleeDisengagement(BattleSoldier soldier)
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

        /// <summary>
        /// Plans a routing squad (OnlyWar_TDD.md §6.6): Run directly away
        /// from the nearest enemy; no shooting or voluntary utility action; an engaged routing
        /// soldier cannot simply leave melee and remains subject to normal enemy attacks.
        /// The heading is a squad property — see <see cref="CalculateSquadRoutLine"/>.
        /// </summary>
        public void PrepareRoutingActions(BattleSquad squad)
        {
            squad.WithdrawalRole = WithdrawalRole.Routing;
            squad.MovementTier = SquadMovementTier.Run;
            ApplyDeclaredMovementState(squad);
            ValueTuple<int, int>? routLine = CalculateSquadRoutLine(squad);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
                {
                    // Pinned in melee — he fights because he cannot flee, not because he wants to.
                    AddMeleeActionsToBag(soldier);
                    continue;
                }

                // No enemy this squad can locate: nothing to run from, so nobody moves.
                if (routLine == null) continue;
                AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, SquadMovementTier.Run),
                    routLine.Value,
                    SquadMovementTier.Run);
                // Deliberately no run-utility action: routing permits no voluntary actions.
            }
        }

        /// <summary>
        /// One flight heading for the whole squad: the line from the closest threat, through the
        /// squad centroid, outward. Deriving it per soldier let members whose nearest enemy differed
        /// break along diverging lines, and the squad centroid — the point pursuit, the engagement
        /// frame and the escape rules all steer by — ended up in empty ground between the fragments.
        /// Returns null when no member can find an enemy at all.
        /// </summary>
        private ValueTuple<int, int>? CalculateSquadRoutLine(BattleSquad squad)
        {
            float nearestDistance = float.MaxValue;
            ValueTuple<int, int>? threat = null;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                if (!soldier.TopLeft.HasValue) continue;
                float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
                if (closestEnemyId == -1 || distance >= nearestDistance) continue;
                nearestDistance = distance;
                threat = _grid.GetSoldierPosition(closestEnemyId)[0];
            }
            if (threat == null) return null;

            (float centroidX, float centroidY) = BattleEngagementFrameBuilder.Centroid(squad);
            float dx = centroidX - threat.Value.Item1;
            float dy = centroidY - threat.Value.Item2;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001f) return new ValueTuple<int, int>(0, RoutLineLength);
            // Normalized to a fixed length for two reasons: it keeps the direction's angular
            // resolution high, and it stops CalculateMovementAlongLine from treating the short
            // centroid-to-threat offset as a destination — a rout spends the whole Run budget, so
            // men close to the enemy must not end the turn nearer than men who started further off.
            return new ValueTuple<int, int>(
                (int)Math.Round(dx / length * RoutLineLength),
                (int)Math.Round(dy / length * RoutLineLength));
        }

        private void ApplyDeclaredMovementState(BattleSquad squad)
        {
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                // Only the Run tier strips a soldier's melee guard (see BattleSoldier.IsRunning).
                // A soldier who subsequently stops to fight clears the flag in
                // AddMeleeActionsToBag, so the declaration here is a default, not a verdict.
                soldier.IsRunning = squad.MovementTier == SquadMovementTier.Run;
                switch (squad.MovementTier)
                {
                    case SquadMovementTier.Stationary:
                        soldier.CurrentSpeed = 0;
                        soldier.LeftoverMovement = 0;
                        break;
                    case SquadMovementTier.Walk:
                        soldier.CurrentSpeed = soldier.GetMoveSpeed() * WalkSpeedMultiplier;
                        break;
                    case SquadMovementTier.Jog:
                        soldier.CurrentSpeed = soldier.GetMoveSpeed() * JogSpeedMultiplier;
                        soldier.Aim = null;
                        break;
                    case SquadMovementTier.Run:
                        soldier.CurrentSpeed = soldier.GetMoveSpeed();
                        soldier.Aim = null;
                        break;
                    case SquadMovementTier.InMelee:
                        bool isAdjacentToEnemy = _grid.IsAdjacentToEnemy(soldier.Soldier.Id);
                        soldier.CurrentSpeed = isAdjacentToEnemy ? 0 : soldier.GetMoveSpeed();
                        if (isAdjacentToEnemy)
                        {
                            // Carry-over represents an interrupted continuous move. Once a
                            // soldier settles into direct melee, that move has ended; retaining
                            // its bank here can produce an oversized charge after contact breaks.
                            soldier.LeftoverMovement = 0;
                        }
                        soldier.Aim = null;
                        break;
                }
            }
        }

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

        // Net outcome of a soldier charging the engaged enemy squad: the battle value his strikes
        // would remove on contact, and the friendly battle value expected to be lost crossing the
        // gap under fire. NetValue < 0 means the run-in costs more than the melee gains.
        private readonly struct ChargeAssessment
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

        private ChargeAssessment EstimateChargeNet(
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
            // allowance) -- see Design/Active/EngagementScoringOverhaul.md Phase 0. This is the ONE
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
                    RangedTargetEvaluation eval = EvaluateRangedTarget(
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
        private static List<MeleeWeapon> BuildProjectedWeaponSequence(
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

        private void AddEquipRangedWeaponActionToBag(BattleSoldier soldier)
        {
            List<RangedWeapon> usableWeapons = soldier.RangedWeapons
                .Where(weapon => (int)weapon.Template.Location <= soldier.FunctioningHands)
                .ToList();
            // we're standing here without a readied ranged weapon; we should do something about that
            if (usableWeapons.Count == 1)
            {
                // the easiest case... ready our one ranged weapon
                _shootActions.Add(new ReadyRangedWeaponAction(soldier, usableWeapons[0]));
            }
            else if (usableWeapons.Count > 1)
            {
                // ugh, this is a decision with a lot of factors that will only come up rarely
                // for now, let's go with the longer ranged weapon
                _shootActions.Add(new ReadyRangedWeaponAction(soldier, usableWeapons.OrderByDescending(w => w.Template.MaximumRange).First()));

            }
        }

        private void AddReloadRangedWeaponActionToBag(BattleSoldier soldier)
        {
            _shootActions.Add(new ReloadRangedWeaponAction(soldier, soldier.EquippedRangedWeapons[0]));
        }

        private static float GetTierSpeed(BattleSoldier soldier, SquadMovementTier tier)
        {
            return tier switch
            {
                SquadMovementTier.Walk => soldier.GetMoveSpeed() * WalkSpeedMultiplier,
                SquadMovementTier.Jog => soldier.GetMoveSpeed() * JogSpeedMultiplier,
                SquadMovementTier.Run or SquadMovementTier.InMelee => soldier.GetMoveSpeed(),
                _ => 0
            };
        }

        private static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier)
        {
            return GetTierSpeed(soldier, tier) + soldier.LeftoverMovement;
        }

        private void AddPermittedRunUtilityActionToBag(BattleSoldier soldier)
        {
            if (soldier.RangedWeapons.Count == 0)
            {
                return;
            }
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                AddEquipRangedWeaponActionToBag(soldier);
            }
            else if (soldier.ReloadingPhase > 0 || soldier.EquippedRangedWeapons[0].LoadedAmmo == 0)
            {
                AddReloadRangedWeaponActionToBag(soldier);
            }
            else
            {
                RangedWeapon emptyBlastWeapon = soldier.RangedWeapons
                    .FirstOrDefault(weapon => weapon.Template.IsBlastWeapon
                        && weapon.LoadedAmmo == 0);
                if (soldier.ReloadingPhase == 0 && emptyBlastWeapon != null)
                {
                    _shootActions.Add(new ReloadRangedWeaponAction(soldier, emptyBlastWeapon));
                }
            }
        }

        private void AddMeleeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            soldier.CurrentSpeed = 0;
            // He has stopped and turned to fight, so he defends with skill and parry again even
            // if his squad declared a Run this turn.
            soldier.IsRunning = false;
            List<BattleSoldier> adjacentEnemies = _grid.GetAdjacentEnemies(soldier.Soldier.Id)
                .Select(enemyId => _soldierMap[enemyId])
                .Where(enemy => enemy.IsCombatEffective)
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

            if (TryAddGunAndBladeActions(soldier, projectedStrikePlans))
            {
                return;
            }

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
            MeleeWeapon meleeWeaponToReady = GetFirstUsableMeleeWeapon(soldier);
            if (soldier.EquippedMeleeWeapons.Count == 0 && meleeWeaponToReady != null)
            {
                _shootActions.Add(new ReadyMeleeWeaponAction(soldier, meleeWeaponToReady));
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

        // A soldier gripping both a one-handed gun and a one-handed melee weapon does not choose
        // between them: the strike costs him nothing, so he always makes it, and the sidearm shot
        // at his strike target joins it whenever its own net value is positive. The evaluation's
        // stray-shot term prices in the scrum he is standing in -- himself and his brothers
        // included -- so a non-positive score means the trigger pull is expected to cost his side
        // more than it removes from the enemy.
        private bool TryAddGunAndBladeActions(
            BattleSoldier soldier,
            List<PlannedMeleeStrike> strikePlans)
        {
            if (strikePlans.Count == 0
                || !soldier.EquippedMeleeWeapons.Any(
                    weapon => weapon.Template.Location == EquipLocation.OneHand))
            {
                return false;
            }
            RangedWeapon sidearm = OrderRangedByTemplateId(soldier.EquippedRangedWeapons)
                .FirstOrDefault(weapon => weapon.Template.Location == EquipLocation.OneHand
                    && !weapon.Template.IsTemplateWeapon
                    && !weapon.Template.IsBlastWeapon
                    && weapon.LoadedAmmo > 0);
            if (sidearm == null)
            {
                return false;
            }

            _meleeActions.Add(new MeleeAttackAction(
                soldier,
                strikePlans,
                didMove: false,
                log: _log,
                random: _random,
                meleeWeaponTemplates: _meleeWeaponTemplates));

            BattleSoldier strikeTarget = _soldierMap[strikePlans[0].TargetId];
            float range = _grid.GetDistanceBetweenSoldiers(
                soldier.Soldier.Id,
                strikeTarget.Soldier.Id);
            if (range > sidearm.Template.MaximumRange)
            {
                return true;
            }
            RangedTargetEvaluation sidearmShot = EvaluateRangedTarget(
                soldier,
                strikeTarget,
                sidearm,
                range,
                additionalToHitModifier: -sidearm.Template.Bulk);
            if (sidearmShot.Score > 0)
            {
                soldier.TargetId = strikeTarget.Soldier.Id;
                _shootActions.Add(new ShootAction(
                    soldier.Soldier.Id,
                    strikeTarget.Soldier.Id,
                    sidearm.Template.Id,
                    range,
                    sidearmShot.ShotsToFire,
                    useBulk: true,
                    grid: _grid,
                    random: _random));
            }
            return true;
        }

        private IReadOnlyList<MeleeWeapon> GetProjectedMeleeLoadout(BattleSoldier soldier)
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

        private static MeleeWeapon GetSecondaryMeleeWeapon(IReadOnlyList<MeleeWeapon> loadout)
        {
            return loadout.Count >= 2
                && loadout[0].Template.Location == EquipLocation.OneHand
                && loadout[1].Template.Location == EquipLocation.OneHand
                    ? loadout[1]
                    : null;
        }

        private static MeleeWeapon GetFirstUsableMeleeWeapon(BattleSoldier soldier)
        {
            return soldier.MeleeWeapons.FirstOrDefault(
                weapon => (int)weapon.Template.Location <= soldier.FunctioningHands);
        }

        private RangedTargetEvaluation SelectBestPointBlankRangedTarget(
            BattleSoldier soldier,
            IReadOnlyList<BattleSoldier> adjacentEnemies)
        {
            RangedTargetEvaluation best = null;
            IReadOnlyList<RangedWeapon> sortedWeapons =
                OrderRangedByTemplateId(soldier.EquippedRangedWeapons);
            foreach (BattleSoldier target in adjacentEnemies.OrderBy(enemy => enemy.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
                {
                    RangedWeapon weapon = sortedWeapons[weaponIndex];
                    if (weapon.LoadedAmmo <= 0
                        || weapon.Template.IsTemplateWeapon
                        || range > weapon.Template.MaximumRange)
                    {
                        continue;
                    }

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

        private void AddChargeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
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
                float moveSpeed = GetMovementBudget(soldier, SquadMovementTier.InMelee);
                ValueTuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
                if (distance > moveSpeed + 1)
                {
                    ValueTuple<int, int> moveVector = new ValueTuple<int, int>(enemyPosition.Item1 - soldier.TopLeft.Value.Item1, enemyPosition.Item2 - soldier.TopLeft.Value.Item2);
                    // we can't make it to an enemy in one move
                    // soldier can't get there in one move, advance as far as possible
                    AddMoveAction(soldier, moveSpeed, moveVector, SquadMovementTier.InMelee);
                    AddPermittedRunUtilityActionToBag(soldier);
                }
                else
                {
                    ValueTuple<int, int> newPos = _grid.GetClosestOpenAdjacency(soldier.TopLeft.Value, enemyPosition);
                    BattleSquad oppSquad = _soldierMap[closestEnemyId].BattleSquad;
                    if (newPos == soldier.TopLeft.Value)
                    {
                        // find the next closest
                        // okay, this is one of those times where I made something because it made me feel smart,
                        // but it's probably unreadable so I should change it later
                        // basically, foreach soldier in the squad of the closest enemy, except the closest enemy (who we already checked)
                        // get their locations, and then sort it according to distance square
                        // PROTIP: SQRT is a relatively expensive operation, so sort by distance squares when it's about comparative, not absolute, distance
                        var map = oppSquad.AbleSoldiers
                            .Where(s => s.Soldier.Id != closestEnemyId)
                            .Select(s => new ValueTuple<int, ValueTuple<int, int>>(s.Soldier.Id, _grid.GetSoldierPosition(s.Soldier.Id)[0]))
                            .Select(t => new ValueTuple<int, ValueTuple<int, int>, ValueTuple<int, int>>(t.Item1, t.Item2, new ValueTuple<int, int>(t.Item2.Item1 - soldier.TopLeft.Value.Item1, t.Item2.Item2 - soldier.TopLeft.Value.Item2)))
                            .Select(u => new ValueTuple<int, ValueTuple<int, int>, int>(u.Item1, u.Item2, (u.Item3.Item1 * u.Item3.Item1 + u.Item3.Item2 * u.Item3.Item2)))
                            .OrderBy(u => u.Item3);
                        foreach (ValueTuple<int, ValueTuple<int, int>, int> soldierData in map)
                        {
                            newPos = _grid.GetClosestOpenAdjacency(soldier.TopLeft.Value, soldierData.Item2);
                            if (newPos != soldier.TopLeft.Value)
                            {
                                AddChargeActionsHelper(soldier, soldierData.Item1, soldier.TopLeft.Value, (float)Math.Sqrt(soldierData.Item3), oppSquad, newPos);
                                break;
                            }
                        }
                        if (newPos == soldier.TopLeft.Value)
                        {
                            // we weren't able to find an enemy to get near, guess we try to find someone to shoot, instead?
                            //Debug.Log("ISoldier in squad engaged in melee couldn't find anyone to attack");
                            ValueTuple<int, int> line = new ValueTuple<int, int>((short)(enemyPosition.Item1 - soldier.TopLeft.Value.Item1),
                                                                               (short)(enemyPosition.Item2 - soldier.TopLeft.Value.Item2));
                            // soldier can't get there in one move, advance as far as possible
                            AddMoveAction(soldier, moveSpeed, line, SquadMovementTier.InMelee);
                            AddPermittedRunUtilityActionToBag(soldier);
                        }
                    }
                    else
                    {
                        AddChargeActionsHelper(soldier, closestEnemyId, soldier.TopLeft.Value, distance, oppSquad, newPos);
                    }
                }
            }
        }

        private IReadOnlyList<IAction> ResolveSquadChargeIntent(
            BattleSquad chargingSquad,
            BattleSquad targetSquad,
            BattleState state)
        {
            List<IAction> resolvedMovement = [];
            if (chargingSquad.Status != BattleSquadStatus.Active
                || targetSquad.Status != BattleSquadStatus.Active)
            {
                return resolvedMovement;
            }

            // Resolve in stable soldier order against the live post-movement grid. Each successful
            // placement immediately occupies its cells, so later members naturally select another
            // defender or another open adjacency instead of dog-piling one reserved square.
            List<BattleSoldier> initialTargets = targetSquad.AbleSoldiers
                .Where(IsPlaced)
                .ToList();
            foreach (BattleSoldier charger in chargingSquad.AbleSoldiers
                .Where(IsPlaced)
                .Select(soldier => new
                {
                    Soldier = soldier,
                    Distance = initialTargets
                        .Select(target => _grid.GetDistanceBetweenSoldiers(
                            soldier.Soldier.Id, target.Soldier.Id))
                        .DefaultIfEmpty(float.MaxValue)
                        .Min()
                })
                .OrderByDescending(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Soldier.Soldier.Id)
                .Select(candidate => candidate.Soldier))
            {
                List<BattleSoldier> targets = targetSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(target => target.Soldier.Id)
                    .ToList();
                if (targets.Count == 0) break;

                List<BattleSoldier> adjacent = targets
                    .Where(target => _grid.GetDistanceBetweenSoldiers(
                        charger.Soldier.Id, target.Soldier.Id)
                        <= BattleContactRules.MeleeContactAllowance)
                    .ToList();
                if (adjacent.Count > 0)
                {
                    PrepareChargerForMelee(charger);
                    MeleeAttackAction attack = CreateMeleeAttackAction(
                        charger, adjacent, didMove: false);
                    if (attack != null) _meleeActions.Add(attack);
                    continue;
                }

                float budget = GetMovementBudget(charger, SquadMovementTier.InMelee);
                var approaches = targets
                    .Select(target =>
                    {
                        ValueTuple<int, int> position = _grid.GetSoldierPosition(
                            target.Soldier.Id)[0];
                        ValueTuple<int, int> adjacency = _grid.GetClosestOpenAdjacency(
                            charger.TopLeft.Value, position);
                        float distance = adjacency == charger.TopLeft.Value
                            ? float.MaxValue
                            : GridDistance(charger.TopLeft.Value, adjacency);
                        return new { Target = target, Position = position, Adjacency = adjacency, Distance = distance };
                    })
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Target.Soldier.Id)
                    .ToList();
                var reachable = approaches.FirstOrDefault(candidate =>
                    candidate.Distance <= budget + 0.0001f);
                BattleSoldier pursuedTarget = reachable?.Target
                    ?? targets.OrderBy(target => _grid.GetDistanceBetweenSoldiers(
                            charger.Soldier.Id, target.Soldier.Id))
                        .ThenBy(target => target.Soldier.Id)
                        .First();
                ValueTuple<int, int> pursuedPosition = _grid.GetSoldierPosition(
                    pursuedTarget.Soldier.Id)[0];
                ValueTuple<int, int> line;
                ValueTuple<int, int> destination;
                if (reachable != null)
                {
                    destination = reachable.Adjacency;
                    line = (
                        destination.Item1 - charger.TopLeft.Value.Item1,
                        destination.Item2 - charger.TopLeft.Value.Item2);
                }
                else
                {
                    line = (
                        pursuedPosition.Item1 - charger.TopLeft.Value.Item1,
                        pursuedPosition.Item2 - charger.TopLeft.Value.Item2);
                    ValueTuple<int, int> desired = CalculateMovementAlongLine(line, budget);
                    destination = (
                        charger.TopLeft.Value.Item1 + desired.Item1,
                        charger.TopLeft.Value.Item2 + desired.Item2);
                }

                ushort orientation = CalculateOrientationFromVector(
                    line, charger, SquadMovementTier.InMelee);
                destination = FindBestLocation(
                    charger,
                    charger.TopLeft.Value,
                    destination,
                    budget,
                    orientation);
                MoveAction move = new(
                    charger,
                    _grid,
                    charger.TopLeft.Value,
                    destination,
                    orientation,
                    budget);
                charger.CurrentSpeed = GetTierSpeed(charger, SquadMovementTier.InMelee);
                move.Execute(state);
                if (move.Succeeded) resolvedMovement.Add(move);

                if (move.Succeeded
                    && pursuedTarget.IsCombatEffective
                    && IsPlaced(pursuedTarget)
                    && _grid.GetDistanceBetweenSoldiers(
                        charger.Soldier.Id, pursuedTarget.Soldier.Id)
                        <= BattleContactRules.MeleeContactAllowance)
                {
                    PrepareChargerForMelee(charger);
                    MeleeAttackAction attack = CreateMeleeAttackAction(
                        charger, [pursuedTarget], didMove: true, isCharge: true);
                    if (attack != null) _meleeActions.Add(attack);
                }
            }
            return resolvedMovement;
        }

        private static float GridDistance(
            ValueTuple<int, int> first,
            ValueTuple<int, int> second)
        {
            int dx = first.Item1 - second.Item1;
            int dy = first.Item2 - second.Item2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void PrepareChargerForMelee(BattleSoldier soldier)
        {
            soldier.CurrentSpeed = 0;
            soldier.LeftoverMovement = 0;
            soldier.IsRunning = false;
        }

        private void AddChargeActionsHelper(BattleSoldier soldier, int closestEnemyId, ValueTuple<int, int> currentPosition, float distance, BattleSquad oppSquad, ValueTuple<int, int> newPos)
        {
            ValueTuple<int, int> move = new ValueTuple<int, int>(newPos.Item1 - currentPosition.Item1, newPos.Item2 - currentPosition.Item2);
            float moveSpeed = GetMovementBudget(soldier, SquadMovementTier.InMelee);
            if (distance > moveSpeed + 1)
            {
                // we can't make it to an enemy in one move
                // soldier can't get there in one move, advance as far as possible
                
                ValueTuple<int, int> realMove = CalculateMovementAlongLine(move, moveSpeed);
                AddMoveAction(soldier, moveSpeed, realMove, SquadMovementTier.InMelee);
                AddPermittedRunUtilityActionToBag(soldier);
            }
            else
            {
                //Debug.Log(soldier.Soldier.Name + " charging " + moveSpeed.ToString("F0"));
                soldier.CurrentSpeed = GetTierSpeed(soldier, SquadMovementTier.InMelee);
                _grid.ReserveSpace(newPos);
                ushort orientation = CalculateOrientationFromVector(move, soldier, SquadMovementTier.InMelee);
                _moveActions.Add(new MoveAction(
                    soldier,
                    _grid,
                    currentPosition,
                    newPos,
                    orientation,
                    moveSpeed));
                MeleeWeapon meleeWeaponToReady = GetFirstUsableMeleeWeapon(soldier);
                if (soldier.EquippedMeleeWeapons.Count == 0 && meleeWeaponToReady != null)
                {
                    _shootActions.Add(new ReadyMeleeWeaponAction(soldier, meleeWeaponToReady));
                }
                else
                {
                    BattleSoldier target = oppSquad.AbleSoldiers.Single(s => s.Soldier.Id == closestEnemyId);
                    MeleeAttackAction action = CreateMeleeAttackAction(
                        soldier,
                        [target],
                        didMove: true,
                        isCharge: true);
                    if (action != null)
                    {
                        _meleeActions.Add(action);
                    }
                }
            }
        }

        private MeleeAttackAction CreateMeleeAttackAction(
            BattleSoldier soldier,
            IEnumerable<BattleSoldier> candidateTargets,
            bool didMove,
            bool isCharge = false)
        {
            List<BattleSoldier> targets = candidateTargets
                .Where(target => target != null && target.IsCombatEffective)
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

            LogMeleeAttack(soldier, strikePlans, targets, didMove, isCharge);
            return new MeleeAttackAction(
                soldier,
                strikePlans,
                didMove,
                _log,
                _random,
                _meleeWeaponTemplates,
                isCharge);
        }

        /// <summary>
        /// Per-soldier melee trace, the counterpart of the ACTION record on the ranged side.
        ///
        /// <para>Melee attacks never pass through <see cref="PlannedSoldierAction"/> -- they are
        /// built here and dropped straight into the melee bag -- so without this the melee half of
        /// every turn is invisible in a log that records the ranged half in full. The strike list is
        /// the interesting part: <see cref="BuildStrikePlan"/> spreads a soldier's attacks across
        /// targets, moving on once cumulative take-out confidence clears the threshold, so which
        /// enemies a soldier split its blows between is a decision, not a detail.</para>
        /// </summary>
        private void LogMeleeAttack(
            BattleSoldier soldier,
            IReadOnlyList<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<BattleSoldier> candidateTargets,
            bool didMove,
            bool isCharge)
        {
            if (_log == null) return;
            string line = new BattleDecisionTrace("MELEE", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("squad", soldier.BattleSquad?.Id),
                BattleDecisionTrace.Field("charge", isCharge),
                BattleDecisionTrace.Field("did_move", didMove),
                BattleDecisionTrace.Field("candidates", candidateTargets.Count),
                BattleDecisionTrace.Field("strikes", strikePlans.Count),
                // weapon>target per strike, in swing order. Semicolon-separated: spaces are the
                // record format's field separator.
                BattleDecisionTrace.Field(
                    "plan",
                    string.Join(
                        ";",
                        strikePlans.Select(strike =>
                            $"{strike.WeaponName}>{strike.TargetName}")))
            }).Render();
            lock (_log)
            {
                _log(line);
            }
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

        // PHASE 5. The graded fraction, exactly as on the ranged side. Every caller multiplies this
        // by a battle value, so leaving melee on bare take-out probability while ranged fire was
        // credited for wounding would have rigged every ranged-versus-melee comparison the planner
        // makes -- including the Hold-versus-CloseToContact decision this phase is calibrated
        // against. The two must be quoted in one currency or neither is trustworthy.
        private float EstimateTakeOutOnHit(BattleSoldier target, BattleSoldier attacker, MeleeWeapon weapon)
        {
            return CalculateRemovalFractionOnHit(
                target,
                attacker.Soldier.Strength * weapon.Template.StrengthMultiplier,
                (target.Armor?.Template.ArmorProvided ?? 0)
                    * weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier);
        }

        // Shared ranged target acquisition. Rifle, cone, and blast all score against this one
        // ranked candidate set for the turn, so a soldier no longer rifles one target while
        // independently lobbing a grenade at another. The committed/aimed target is pinned first
        // (stickiness applies to every ranged option, not just the rifle); the rest are nearest
        // first. Capped at RangedCandidateEvaluationCount to keep the template/blast scans bounded.
        private IReadOnlyList<BattleSoldier> BuildRankedRangedCandidates(
            BattleSoldier soldier,
            ValueTuple<int, int>? movementDirection)
        {
            int committedId = soldier.Aim?.Item1 ?? soldier.TargetId ?? -1;
            List<(BattleSoldier Soldier, float Distance)> ranked = [];
            foreach (BattleSquad squad in GetNearestInRangeEnemySquads(soldier, movementDirection))
            {
                foreach (BattleSoldier enemy in squad.AbleSoldiers)
                {
                    if (enemy == null || !enemy.IsCombatEffective || !IsPlaced(enemy))
                    {
                        continue;
                    }
                    float distance = _grid.GetDistanceBetweenSoldiers(
                        soldier.Soldier.Id, enemy.Soldier.Id);
                    ranked.Add((enemy, distance));
                }
            }
            ranked.Sort((first, second) =>
            {
                bool firstCommitted = first.Soldier.Soldier.Id == committedId;
                bool secondCommitted = second.Soldier.Soldier.Id == committedId;
                if (firstCommitted != secondCommitted)
                {
                    return firstCommitted ? -1 : 1;
                }
                int byDistance = first.Distance.CompareTo(second.Distance);
                return byDistance != 0
                    ? byDistance
                    : first.Soldier.Soldier.Id.CompareTo(second.Soldier.Soldier.Id);
            });
            int count = Math.Min(ranked.Count, RangedCandidateEvaluationCount);
            BattleSoldier[] result = new BattleSoldier[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ranked[i].Soldier;
            }
            return result;
        }

        // Phase 2 sticky targeting. Replaces the former IsExistingAimStillBest, which reran the full
        // SelectBestRangedTarget scan every turn just to confirm the aim was still globally optimal.
        // Here the aim is kept while it stays viable and worthwhile — a hysteresis band that both
        // preserves the invested aim and skips the scan.
        private bool IsExistingAimStillViable(BattleSoldier soldier)
        {
            if (soldier.Aim is not ValueTuple<int, RangedWeapon, int> aim
                || !_soldierMap.TryGetValue(aim.Item1, out BattleSoldier target)
                || !target.IsCombatEffective
                || !IsPlaced(target)
                || _grid.GetSoldierSide(aim.Item1) == _grid.GetSoldierSide(soldier.Soldier.Id))
            {
                return false;
            }

            RangedWeapon weapon = aim.Item2;
            if (weapon.LoadedAmmo <= 0 || !soldier.EquippedRangedWeapons.Contains(weapon))
            {
                return false;
            }

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, aim.Item1);
            if (range > weapon.Template.MaximumRange
                || ShouldInterruptStickyTarget(soldier, target))
            {
                return false;
            }

            // Judge the shot the aim is being HELD FOR, not the one available part-way through it.
            // Aiming exists to turn a marginal shot into a good one, so scoring a half-finished aim
            // at its current bonus condemns exactly the shots worth aiming for: the gate fails at
            // bonus 0, the aim is discarded, the re-acquire path decides aiming still beats
            // shooting and starts a fresh aim at 0, and the soldier loops forever without firing.
            // That is the "sits, aims, never fires" long-range stall — most visible on a Standoff
            // fire-support squad, which is stationary and far away by design. Using the full bonus
            // matches what the >= 3 branch will actually fire with (Accuracy + 3 + 1), so a shot
            // that will be worthwhile once lined up is allowed to mature, while one that is
            // hopeless even fully aimed is still dropped.
            RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                soldier,
                target,
                weapon,
                range,
                weapon.Template.Accuracy + Math.Max(aim.Item3, FullAimBonusTurns) + 1);
            return evaluation.Score > 0 && evaluation.HitProbability > StickyMinimumHitProbability;
        }

        // Evaluates only the target the soldier already committed to (soldier.TargetId), skipping the
        // whole-field SelectBestRangedTarget scan. Returns the shot to take, or null to signal
        // "re-acquire" — the caller then falls back to a full scan. The per-target/weapon scoring
        // mirrors SelectBestRangedTarget's inner loop exactly, so a stuck result is identical to what
        // the scan would have produced for that target; only the target-selection hysteresis differs.
        private RangedTargetEvaluation EvaluateStickyTarget(
            BattleSoldier soldier,
            float bulkMultiplier,
            ValueTuple<int, int>? movementDirection)
        {
            if (soldier.TargetId is not int committedId
                || !_soldierMap.TryGetValue(committedId, out BattleSoldier target)
                || !target.IsCombatEffective
                || !IsPlaced(target)
                || _grid.GetSoldierSide(committedId) == _grid.GetSoldierSide(soldier.Soldier.Id))
            {
                return null;
            }
            if (HasRestrictedJogFiringArc(movementDirection)
                && !IsWithinJogFiringArc(soldier, target, movementDirection.Value))
            {
                return null;
            }
            if (ShouldInterruptStickyTarget(soldier, target))
            {
                return null;
            }
            // A target that has since broken into a run is not the shot it was committed to: it
            // cannot shoot back, and somebody else on that side is now doing the shooting. Without
            // this, sticky targeting would hold every pursuer on the runner it first acquired and
            // the fleeing-target bias could never take effect. Releasing the commitment only
            // re-opens the choice — the full scan may well re-acquire the same man.
            //
            // Deliberately NOT in ShouldInterruptStickyTarget: that predicate is shared with
            // IsExistingAimStillViable, where a "no" throws away the soldier's accumulated aim.
            // Re-opening a target choice is free; resetting an aim to zero every turn means a
            // standing shooter can never reach the bonus it needs to fire at all.
            if (TargetSelectionWeight(target) < 1f)
            {
                return null;
            }

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, committedId);
            RangedTargetEvaluation best = null;
            IReadOnlyList<RangedWeapon> sortedWeapons =
                OrderRangedByTemplateId(soldier.EquippedRangedWeapons);
            for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
            {
                RangedWeapon weapon = sortedWeapons[weaponIndex];
                if (weapon.LoadedAmmo <= 0
                    || weapon.Template.IsTemplateWeapon
                    || range > weapon.Template.MaximumRange)
                {
                    continue;
                }

                float toHitModifier = -weapon.Template.Bulk * bulkMultiplier;
                RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                    soldier,
                    target,
                    weapon,
                    range,
                    toHitModifier);
                if (best == null || evaluation.Score > best.Score)
                {
                    best = evaluation;
                }
            }

            // Re-acquire once the committed target is no longer a worthwhile shot.
            return best != null
                && best.Score > 0
                && best.HitProbability > StickyMinimumHitProbability
                    ? best
                    : null;
        }

        // Emergency re-acquire trigger: an enemy other than the committed target is about to reach
        // melee this soldier while the committed target sits farther away. A soldier already adjacent
        // to an enemy is routed to the melee/charge planner upstream, so this only covers the turn
        // before contact — it stops a soldier from calmly plinking a distant target while a different
        // enemy closes the last stretch into his face.
        private bool ShouldInterruptStickyTarget(BattleSoldier soldier, BattleSoldier committedTarget)
        {
            float nearestRange = _grid.GetNearestEnemy(soldier.Soldier.Id, out int nearestId);
            if (nearestId == -1
                || nearestId == committedTarget.Soldier.Id
                || !_soldierMap.TryGetValue(nearestId, out BattleSoldier nearest))
            {
                return false;
            }

            float committedRange = _grid.GetDistanceBetweenSoldiers(
                soldier.Soldier.Id,
                committedTarget.Soldier.Id);
            return nearestRange < committedRange && nearest.GetMoveSpeed() >= nearestRange;
        }

        /// <summary>
        /// Scores every soldier in the three nearest in-range enemy squads and returns the
        /// target/weapon pair with the greatest expected battle-value swing.
        /// </summary>
        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            bool useBulk,
            bool includeExistingAim = false,
            ValueTuple<int, int>? movementDirection = null)
        {
            return SelectBestRangedTarget(
                soldier,
                useBulk ? FullBulkMultiplier : 0,
                includeExistingAim,
                movementDirection);
        }

        // Phase 3 fire distribution. Returns the shooter squad's engagement frame for the turn,
        // computing it once and memoizing per squad. The frame is a pure function of the frozen
        // layout, so every member of the squad shares it.
        private SquadEngagementGeometry GetSquadEngagementGeometry(BattleSquad squad)
        {
            if (squad == null)
            {
                return default;
            }
            if (_context.SquadGeometry.TryGetValue(squad.Id, out SquadEngagementGeometry cached))
            {
                return cached;
            }
            SquadEngagementGeometry geometry = ComputeSquadEngagementGeometry(squad);
            _context.SquadGeometry[squad.Id] = geometry;
            return geometry;
        }

        private SquadEngagementGeometry ComputeSquadEngagementGeometry(BattleSquad squad)
        {
            double sumX = 0;
            double sumY = 0;
            int count = 0;
            bool shooterSide = false;
            bool haveSide = false;
            foreach (BattleSoldier member in squad.AbleSoldiers)
            {
                if (member.TopLeft is not ValueTuple<int, int> position
                    || !_grid.IsSoldierPlaced(member.Soldier.Id))
                {
                    continue;
                }
                sumX += position.Item1;
                sumY += position.Item2;
                count++;
                if (!haveSide)
                {
                    shooterSide = _grid.GetSoldierSide(member.Soldier.Id);
                    haveSide = true;
                }
            }
            if (count == 0 || !haveSide)
            {
                return default;
            }

            double enemyX = 0;
            double enemyY = 0;
            int enemyCount = 0;
            foreach (BattleSoldier enemy in _soldierMap.Values)
            {
                if (!enemy.IsCombatEffective
                    || enemy.TopLeft is not ValueTuple<int, int> enemyPosition
                    || !_grid.IsSoldierPlaced(enemy.Soldier.Id)
                    || _grid.GetSoldierSide(enemy.Soldier.Id) == shooterSide)
                {
                    continue;
                }
                enemyX += enemyPosition.Item1;
                enemyY += enemyPosition.Item2;
                enemyCount++;
            }
            if (enemyCount == 0)
            {
                return default;
            }

            float centroidX = (float)(sumX / count);
            float centroidY = (float)(sumY / count);
            float enemyCentroidX = (float)(enemyX / enemyCount);
            float enemyCentroidY = (float)(enemyY / enemyCount);
            float axisX = enemyCentroidX - centroidX;
            float axisY = enemyCentroidY - centroidY;
            float axisLength = MathF.Sqrt((axisX * axisX) + (axisY * axisY));
            if (axisLength < 1e-4f)
            {
                // Squads occupy the same point (should not happen with living enemies); no axis.
                return default;
            }
            // Perpendicular to the engagement axis is the lateral ("along the frontage") direction.
            float perpX = -axisY / axisLength;
            float perpY = axisX / axisLength;

            float discipline = squad.Squad?.Faction?.FireDiscipline ?? DefaultFireDiscipline;
            return new SquadEngagementGeometry(
                centroidX,
                centroidY,
                enemyCentroidX,
                enemyCentroidY,
                perpX,
                perpY,
                BaseLaneSpreadCoefficient * discipline);
        }

        // The shooter's own lateral position along its squad frontage — computed once per shooter.
        private static float ShooterLateralOffset(
            in SquadEngagementGeometry geometry,
            BattleSoldier soldier)
        {
            if (!geometry.Valid || soldier.TopLeft is not ValueTuple<int, int> position)
            {
                return 0f;
            }
            return ((position.Item1 - geometry.CentroidX) * geometry.PerpX)
                + ((position.Item2 - geometry.CentroidY) * geometry.PerpY);
        }

        // Penalty applied to a candidate's score so a shooter prefers the enemy in its own lane:
        // the lateral gap between where the shooter sits in its line and where the target sits in the
        // enemy line, scaled by the (discipline-weighted) spread coefficient.
        private static float LaneSpreadPenalty(
            in SquadEngagementGeometry geometry,
            float shooterLateral,
            BattleSoldier target)
        {
            if (!geometry.Valid
                || geometry.SpreadCoefficient <= 0f
                || target.TopLeft is not ValueTuple<int, int> position)
            {
                return 0f;
            }
            float targetLateral = ((position.Item1 - geometry.EnemyCentroidX) * geometry.PerpX)
                + ((position.Item2 - geometry.EnemyCentroidY) * geometry.PerpY);
            return geometry.SpreadCoefficient * MathF.Abs(shooterLateral - targetLateral);
        }

        /// <summary>
        /// TUNABLE: how heavily a fleeing target's expected damage is discounted when choosing whom
        /// to shoot. Bound and Routing squads are running, and a running squad cannot shoot at all,
        /// so in an organized withdrawal every round of return fire comes from the one Cover or
        /// RearGuard squad standing still. A pure expected-damage scorer happily spends the whole
        /// pursuit trading with the runners — the only enemies that cannot hurt it — while the
        /// covering squad fires back unopposed. At 0.5 a fleeing target has to look twice as
        /// valuable before it is preferred, so a badly exposed runner is still taken when it really
        /// is the better shot. 1.0 disables the bias.
        ///
        /// Like the lane-spread penalty it sits beside, this biases *selection* only: the returned
        /// evaluation keeps its true score, so a shot chosen this way still competes honestly
        /// against the template and blast options. Squads carry WithdrawalRole.None whenever nobody
        /// is withdrawing, so this is inert in an ordinary engagement.
        /// </summary>
        private const float FleeingTargetSelectionWeight = 0.5f;

        // The role is whatever the withdrawing side last planned. When that side plans second its
        // roles are a turn stale, which is still a good predictor: cover rotates only when the
        // incumbent becomes the closest squad.
        private static float TargetSelectionWeight(BattleSoldier target) =>
            target?.BattleSquad?.WithdrawalRole is WithdrawalRole.Bound or WithdrawalRole.Routing
                ? FleeingTargetSelectionWeight
                : 1f;

        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            float bulkMultiplier,
            bool includeExistingAim = false,
            ValueTuple<int, int>? movementDirection = null)
        {
            IReadOnlyList<RangedWeapon> equippedRanged = soldier?.EquippedRangedWeapons;
            if (equippedRanged == null || equippedRanged.Count == 0)
            {
                return null;
            }
            // The equipped list is tiny and its Template.Id ordering does not depend on the
            // per-target range, so sort it once here instead of rebuilding a LINQ Where/OrderBy
            // pipeline for every candidate target in the innermost loop. Ordering is preserved
            // exactly, keeping seeded tie-breaking stable.
            IReadOnlyList<RangedWeapon> sortedWeapons = OrderRangedByTemplateId(equippedRanged);

            // Phase 3: bias selection toward the enemy in the shooter's own firing lane so the squad
            // spreads its fire. The penalty affects only which target is picked, not the returned
            // evaluation's value (that still competes at its true score against template/blast options).
            SquadEngagementGeometry geometry = GetSquadEngagementGeometry(soldier.BattleSquad);
            float shooterLateral = ShooterLateralOffset(geometry, soldier);

            RangedTargetEvaluation best = null;
            float bestEffectiveScore = float.MinValue;
            foreach (BattleSquad candidateSquad in GetNearestInRangeEnemySquads(
                soldier,
                movementDirection))
            {
                foreach (BattleSoldier target in candidateSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
                    float lanePenalty = LaneSpreadPenalty(geometry, shooterLateral, target);
                    for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
                    {
                        RangedWeapon weapon = sortedWeapons[weaponIndex];
                        if (weapon.LoadedAmmo <= 0
                            || weapon.Template.IsTemplateWeapon
                            || range > weapon.Template.MaximumRange)
                        {
                            continue;
                        }

                        float toHitModifier = -weapon.Template.Bulk * bulkMultiplier;
                        if (includeExistingAim
                            && soldier.Aim?.Item1 == target.Soldier.Id
                            && soldier.Aim?.Item2.Template.Id == weapon.Template.Id)
                        {
                            toHitModifier += weapon.Template.Accuracy + soldier.Aim.Value.Item3 + 1;
                        }

                        RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                            soldier,
                            target,
                            weapon,
                            range,
                            toHitModifier);
                        // Candidate squads, soldiers, and weapons are ordered nearest-first and
                        // deterministically, so an exact tie naturally stays on the closer option.
                        float effectiveScore =
                            (evaluation.Score * TargetSelectionWeight(target)) - lanePenalty;
                        if (best == null || effectiveScore > bestEffectiveScore)
                        {
                            best = evaluation;
                            bestEffectiveScore = effectiveScore;
                        }
                    }
                }
            }

            return best;
        }

        internal TemplateFiringLineEvaluation SelectBestTemplateFiringLine(
            BattleSoldier soldier,
            IEnumerable<BattleSoldier> candidateTargets = null,
            ValueTuple<int, int>? movementDirection = null)
        {
            IReadOnlyList<RangedWeapon> equippedRanged = soldier?.EquippedRangedWeapons;
            if (equippedRanged == null
                || equippedRanged.Count == 0
                || !IsPlaced(soldier))
            {
                return null;
            }
            IReadOnlyList<RangedWeapon> sortedWeapons = OrderRangedByTemplateId(equippedRanged);

            IEnumerable<BattleSoldier> targets = candidateTargets
                ?? GetNearestInRangeEnemySquads(soldier, movementDirection)
                    .SelectMany(candidateSquad => candidateSquad.AbleSoldiers);
            if (candidateTargets != null && HasRestrictedJogFiringArc(movementDirection))
            {
                ValueTuple<int, int> firingDirection = movementDirection.Value;
                targets = targets.Where(target => target != null
                    && IsWithinJogFiringArc(soldier, target, firingDirection));
            }
            bool shooterSide = _grid.GetSoldierSide(soldier.Soldier.Id);
            TemplateFiringLineEvaluation best = null;
            foreach (BattleSoldier target in targets
                .Where(target => target != null
                    && target.IsCombatEffective
                    && IsPlaced(target)
                    && _grid.GetSoldierSide(target.Soldier.Id) != shooterSide)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
                {
                    RangedWeapon weapon = sortedWeapons[weaponIndex];
                    if (!weapon.Template.IsConeWeapon
                        || weapon.LoadedAmmo <= 0
                        || range > weapon.Template.MaximumRange)
                    {
                        continue;
                    }

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
                        if (!victim.IsCombatEffective)
                        {
                            // Incapacitated figures are still physically engulfed by the action,
                            // but their battle value has already been removed from the fight.
                            continue;
                        }

                        float victimRange = _grid.GetDistanceBetweenSoldiers(
                            soldier.Soldier.Id,
                            victimId);
                        float armor = victim.Armor?.Template.ArmorProvided ?? 0;
                        // Phase 5 graded fraction, matching the conventional ranged path so a cone
                        // burst and a rifle shot are quoted in the same currency.
                        float removalFraction = CalculateRangedRemovalFraction(
                            victim, weapon, victimRange, armor);
                        float expectedBattleValueRemoval =
                            removalFraction * GetBattleValue(victim);
                        if (_grid.GetSoldierSide(victimId) == shooterSide)
                        {
                            expectedFriendlyBattleValueLost += expectedBattleValueRemoval;
                        }
                        else
                        {
                            // Undiscounted, matching the conventional ranged path: this burst is
                            // fired now, so when the victim's squad would have reached us is
                            // irrelevant (Phase 3, Design/Active/EngagementScoringOverhaul.md).
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

        /// <summary>
        /// Scores grenade aim points and returns the best throw, or null when none removes more
        /// value than it costs in expectation. Each candidate enemy's cell is an aim point;
        /// <see cref="EvaluateBlastThrow"/> integrates expected enemy and friendly (self included)
        /// battle value over the full delivery scatter distribution and the per-victim damage roll,
        /// so a throw that only frags the squad when it misses is priced accordingly. When
        /// <paramref name="candidateTargets"/> is supplied the throw is scored against the shared
        /// acquired candidates (rifle/cone/blast agreeing on targets); otherwise it falls back to
        /// its own nearest-in-range scan.
        /// </summary>
        internal TemplateFiringLineEvaluation SelectBestBlastThrow(
            BattleSoldier soldier,
            ValueTuple<int, int>? movementDirection = null,
            float bulkMultiplier = 0,
            IReadOnlyList<BattleSoldier> candidateTargets = null)
        {
            if (soldier == null || !IsPlaced(soldier))
            {
                return null;
            }

            List<RangedWeapon> blastWeapons = GetLoadedBlastWeapons(soldier);
            if (blastWeapons.Count == 0)
            {
                return null;
            }

            float maximumEffectiveRange = blastWeapons.Max(weapon =>
                BattleModifiersUtil.GetEffectiveMaxRange(soldier.Soldier, weapon.Template));
            bool shooterSide = _grid.GetSoldierSide(soldier.Soldier.Id);
            IEnumerable<BattleSoldier> targets = candidateTargets
                ?? GetNearestEnemySquadsWithinRange(
                        soldier,
                        maximumEffectiveRange,
                        movementDirection)
                    .SelectMany(candidateSquad => candidateSquad.AbleSoldiers);
            TemplateFiringLineEvaluation best = null;
            foreach (BattleSoldier target in targets
                .Where(target => target != null
                    && target.IsCombatEffective
                    && IsPlaced(target)
                    && _grid.GetSoldierSide(target.Soldier.Id) != shooterSide)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                foreach (RangedWeapon weapon in blastWeapons
                    .Where(weapon => range <= BattleModifiersUtil.GetEffectiveMaxRange(
                        soldier.Soldier,
                        weapon.Template)))
                {
                    BlastThrowOutcome outcome = EvaluateBlastThrow(
                        soldier, target, weapon, range, bulkMultiplier);
                    TemplateFiringLineEvaluation evaluation = new(
                        target,
                        weapon,
                        range,
                        outcome.NominalVictimIds,
                        outcome.EnemyBattleValueRemoved,
                        outcome.FriendlyBattleValueLost);
                    // A throw that trades away as much friendly value (self included) as
                    // it removes is never worth the grenade.
                    if (evaluation.Score > 0 && (best == null || evaluation.Score > best.Score))
                    {
                        best = evaluation;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Emits a per-turn planning trace breaking down why a soldier chose to throw a grenade
        /// over its best conventional ranged action: the throw's to-hit/delivery math and
        /// enemy/friendly battle-value split, alongside the alternative rifle shot and template
        /// (cone) line it beat. The throw's to-hit and delivery confidence are recomputed here
        /// (mirroring <see cref="SelectBestBlastThrow"/>) because they are local to that scan and
        /// not carried on the returned evaluation; this only runs when a throw is actually
        /// selected and a log sink is attached, so the no-logging hot path is untouched.
        ///
        /// <para>Returns the line rather than writing it. The caller plans a root action for every
        /// candidate posture, only one of which is materialized, so the string rides on
        /// <see cref="PlannedSoldierAction.Diagnostic"/> and is emitted by
        /// <see cref="MaterializeSoldierAction"/> once the throw is known to be the one taken.</para>
        /// </summary>
        private string FormatGrenadeSelection(
            BattleSoldier soldier,
            TemplateFiringLineEvaluation blastThrow,
            RangedTargetEvaluation conventionalShot,
            TemplateFiringLineEvaluation conventionalTemplate,
            float bestConventionalScore,
            float bulkMultiplier)
        {
            if (_log == null) return null;

            RangedWeaponTemplate weapon = blastThrow.Weapon.Template;
            float range = blastThrow.Range;
            float skill = soldier.Soldier.GetTotalSkillValue(weapon.RelatedSkill);
            float rangeModifier = BattleModifiersUtil.CalculateBlastRangeModifier(
                soldier.Soldier, weapon, range);
            float bulkPenalty = weapon.Bulk * bulkMultiplier;
            float toHit = skill + rangeModifier - bulkPenalty;
            float deliveryConfidence = GaussianCalculator.ApproximateNormalCDF(
                (toHit - BlastDeliveryRollMean) / BlastDeliveryRollStdDev);

            // float.MinValue is the "no alternative existed" sentinel the caller's Math.Max produces;
            // rendering it as a number would read as a real score of -3.4e38.
            bool hasConventional = conventionalShot != null || conventionalTemplate != null;
            float? bestConventional = hasConventional ? bestConventionalScore : null;
            float? margin = hasConventional ? blastThrow.Score - bestConventionalScore : null;

            bool shooterSide = _grid.GetSoldierSide(soldier.Soldier.Id);
            List<string> caughtEnemies = [];
            List<string> caughtFriendlies = [];
            foreach (int victimId in blastThrow.VictimIds)
            {
                if (!_soldierMap.TryGetValue(victimId, out BattleSoldier victim)) continue;
                string label = victim.Soldier.Name;
                if (victimId == soldier.Soldier.Id) label += " (self)";
                if (_grid.GetSoldierSide(victimId) == shooterSide) caughtFriendlies.Add(label);
                else caughtEnemies.Add(label);
            }

            return new BattleDecisionTrace("GRENADE_CHOICE",
            [
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("weapon", weapon.Name),
                BattleDecisionTrace.Field("target", blastThrow.Target.Soldier.Name),
                BattleDecisionTrace.Field("range", range),
                BattleDecisionTrace.Field("score", blastThrow.Score),
                BattleDecisionTrace.Field("enemy_bv", blastThrow.ExpectedEnemyBattleValueRemoved),
                BattleDecisionTrace.Field("friendly_bv", blastThrow.ExpectedFriendlyBattleValueLost),
                BattleDecisionTrace.Field("to_hit", toHit),
                BattleDecisionTrace.Field("skill", skill),
                BattleDecisionTrace.Field("range_mod", rangeModifier),
                BattleDecisionTrace.Field("bulk_penalty", bulkPenalty),
                BattleDecisionTrace.Field("delivery", deliveryConfidence),
                // Semicolon-separated: the record format reserves spaces for field boundaries.
                BattleDecisionTrace.Field(
                    "caught_enemies",
                    caughtEnemies.Count == 0 ? "none" : string.Join(";", caughtEnemies)),
                BattleDecisionTrace.Field(
                    "caught_friendlies",
                    caughtFriendlies.Count == 0 ? "none" : string.Join(";", caughtFriendlies)),
                // The alternatives the throw beat. Without these the score above is unfalsifiable:
                // a throw looks arbitrary until you can see what firing instead was worth.
                BattleDecisionTrace.Field(
                    "alt_shot_weapon", conventionalShot?.Weapon.Template.Name),
                BattleDecisionTrace.Field(
                    "alt_shot_target", conventionalShot?.Target.Soldier.Name),
                BattleDecisionTrace.Field("alt_shot_shots", conventionalShot?.ShotsToFire),
                BattleDecisionTrace.Field("alt_shot_hit", conventionalShot?.HitProbability),
                BattleDecisionTrace.Field(
                    "alt_shot_takeout", conventionalShot?.TakeOutProbabilityOnHit),
                BattleDecisionTrace.Field("alt_shot_score", conventionalShot?.Score),
                BattleDecisionTrace.Field(
                    "alt_template_weapon", conventionalTemplate?.Weapon.Template.Name),
                BattleDecisionTrace.Field("alt_template_score", conventionalTemplate?.Score),
                BattleDecisionTrace.Field("best_conventional", bestConventional),
                BattleDecisionTrace.Field("margin", margin),
                BattleDecisionTrace.Field(
                    "margin_threshold", BlastOverConventionalScoreMargin)
            ]).Render();
        }

        private bool HasBlastTargetInRange(BattleSoldier soldier)
        {
            if (soldier == null || !IsPlaced(soldier))
            {
                return false;
            }

            float maximumEffectiveRange = GetLoadedBlastWeapons(soldier)
                .Select(weapon => BattleModifiersUtil.GetEffectiveMaxRange(
                    soldier.Soldier,
                    weapon.Template))
                .DefaultIfEmpty(0)
                .Max();
            return maximumEffectiveRange > 0
                && _grid.GetNearestEnemy(soldier.Soldier.Id, out _) <= maximumEffectiveRange;
        }

        /// <summary>
        /// Blast weapons ride on the belt (<see cref="BattleSoldier.RangedWeapons"/>)
        /// without occupying a hand, so both lists are candidates.
        /// </summary>
        private static List<RangedWeapon> GetLoadedBlastWeapons(BattleSoldier soldier)
        {
            return soldier.EquippedRangedWeapons
                .Concat(soldier.RangedWeapons)
                .Where(weapon => weapon.Template.IsBlastWeapon && weapon.LoadedAmmo > 0)
                .GroupBy(weapon => weapon.Template.Id)
                .Select(group => group.First())
                .OrderBy(weapon => weapon.Template.Id)
                .ToList();
        }

        /// <summary>
        /// Probability that one landed hit removes the target from the fight. This mirrors the
        /// resolver's location lottery, armor and normal damage roll, wound-level conversion,
        /// accumulated wound carry, motive/vital thresholds, and last-functioning-hand rule.
        /// </summary>
        internal static float CalculateTakeOutProbabilityOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return 0f;
            }
            return AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, damageCoefficient, null).TakeOut;
        }

        /// <summary>
        /// PHASE 5 (Design/Active/EngagementScoringOverhaul.md). The fraction of a target's battle
        /// value one landed hit is credited with removing:
        /// <c>P(takeout) + lambda * E[woundProgress; no takeout]</c>.
        ///
        /// <para>WHY. <see cref="CalculateTakeOutProbabilityOnHit"/> was already wound-state aware
        /// -- <c>FindMinimumDisablingWoundRatio</c> reads the wounds a location already carries, so
        /// take-out rises as a target is softened. What was missing was credit for CREATING that
        /// state: the planner scored only the finishing blow, never the twenty hits that made it
        /// possible, so a squad that could not one-shot anything scored ~0 for shooting and the
        /// decision fell entirely to the lookahead. This is a credit-assignment fix, not a new
        /// accumulator.</para>
        ///
        /// <para>The two terms decompose <c>E[progress]</c> exactly:
        /// <c>E[progress] = P(takeout)*1 + E[progress; no takeout]</c>, where progress is the
        /// fraction of the remaining gap to the disable threshold that the hit closes. lambda
        /// therefore interpolates between "only kills count" (0) and "all expected progress counts"
        /// (1), and the result is bounded by 1 for lambda in [0, 1].
        ///
        /// NOTE: the second term is the PARTIAL expectation (integrated over the no-takeout mass),
        /// not the conditional one the design doc's notation suggests. The conditional form
        /// diverges as P(takeout) approaches 1 -- a target that is certain to die would be scored
        /// as worth MORE than its battle value -- and it does not telescope with the first term.
        /// </para>
        ///
        /// <para>INVARIANT (Design doc "Invariants"): squads must not fire at targets they cannot
        /// damage. When penetration is impossible both the take-out threshold and the
        /// wound-onset threshold sit far out in the damage roll's tail, so the Gaussian mass
        /// between them vanishes and BOTH terms go to ~0. lambda cannot buy value against an
        /// impenetrable target; it only grades the penetrable-but-not-lethal middle.</para>
        /// </summary>
        internal static float CalculateRemovalFractionOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return 0f;
            }
            (float takeOut, float progress) = AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, damageCoefficient, null);
            return CombineRemovalFraction(takeOut, progress);
        }

        internal static float CombineRemovalFraction(float takeOut, float woundProgress)
        {
            float lambda = EffectiveWoundProgressCreditWeight;
            return lambda <= 0f
                ? Math.Clamp(takeOut, 0f, 1f)
                : Math.Clamp(takeOut + (lambda * woundProgress), 0f, 1f);
        }

        /// <summary>
        /// Expected fraction of a target's battle value removed by ONE burst, over the joint
        /// distribution of the to-hit roll and the resulting hit count.
        ///
        /// <para>WHY. Scoring used to be <c>P(hit) * removalFractionPerHit</c> -- the probability
        /// the ROLL succeeds times what a SINGLE hit is worth. But <see cref="Actions.ShootAction"/>
        /// resolves a recoil loop: the first hit needs margin &gt; 0, and each further hit needs the
        /// margin, less one Recoil per shot already fired, to stay above 1. A nine-round bolt burst
        /// against a large target at close range lands three to five hits and was priced as one, so
        /// every comparison against an option scored over MULTIPLE bodies -- a grenade, a flamer
        /// cone -- was rigged against the rifle.</para>
        ///
        /// <para>THE FORM. Hit k requires margin &gt; <c>t_k</c>, with <c>t_1 = 0</c> and
        /// <c>t_k = 1 + (k-1)*recoil</c>; margin is normal with mean
        /// <c>preRollHitTotal - HitRollMean</c> and deviation <c>HitRollStdDev</c>, so
        /// <c>q_k = P(H &gt;= k)</c> is one CDF each. Since
        /// <c>1 - (1-f)^H = f * sum_{j&lt;H} (1-f)^j</c>, taking expectations gives
        /// <c>E[removed] = f * sum_k q_k * (1-f)^(k-1)</c> -- compounding, so hits saturate at the
        /// target's full battle value instead of summing past it, and NOT
        /// <c>1 - (1-f)^E[H]</c>, which would credit a coin-flip shot from a certain-kill weapon
        /// with a certain kill. At one shot this is exactly the old <c>q_1 * f</c>, so a
        /// single-shot weapon's score is unchanged to the bit.</para>
        ///
        /// <para>Shared by all three consumers of the removal currency -- immediate action scoring
        /// in <see cref="EvaluateRangedTarget"/>, the Phase 4 lookahead table in
        /// <see cref="PairRemovalTerm.RemovalAt"/>, and the Phase 6 engagement-range model in
        /// <see cref="RangedEffectivenessCurve"/> -- because those three must stay commensurable.
        /// </para>
        /// </summary>
        internal static float ExpectedBurstRemovalFraction(
            float preRollHitTotal,
            int shotsToFire,
            float recoil,
            float removalFractionPerHit)
        {
            float perHit = Math.Clamp(removalFractionPerHit, 0f, 1f);
            if (perHit <= 0f)
            {
                return 0f;
            }
            int shots = Math.Max(1, shotsToFire);
            float mean = preRollHitTotal - HitRollMean;
            float survive = 1f - perHit;
            float expected = 0f;
            float weight = 1f;
            for (int k = 1; k <= shots; k++)
            {
                float threshold = k == 1 ? 0f : 1f + ((k - 1) * recoil);
                float reachesK = GaussianCalculator.ApproximateNormalCDF(
                    (mean - threshold) / HitRollStdDev);
                // q_k is non-increasing in k, so once it or the survival weight underflows no later
                // term can contribute. Deliberately tested against zero rather than a small epsilon:
                // a hopeless long-range shot's rate is ~1e-7, not 0, and the engagement-range model
                // reads exactly that tail to tell "barely worth shooting" from "cannot shoot at
                // all". The loop is bounded by RateOfFire regardless.
                if (reachesK <= 0f || weight <= 0f)
                {
                    break;
                }
                expected += reachesK * weight;
                weight *= survive;
            }
            return Math.Clamp(perHit * expected, 0f, 1f);
        }

        /// <summary>
        /// The range-INDEPENDENT half of <see cref="CalculateTakeOutProbabilityOnHit"/>: one
        /// <c>(w_loc, K_loc)</c> pair per hit location that can take the target out, where
        /// <c>K_loc = effectiveArmor + requiredPenetratingDamage</c>. Combined with
        /// <see cref="EvaluateTakeOutProbability"/> this reproduces the full function at any damage
        /// coefficient without walking hit locations or wound state again -- the Phase 4 lookahead
        /// path (Design/Active/EngagementScoringOverhaul.md). Both entry points run the SAME loop
        /// (<see cref="AccumulateTakeOutTerms"/>), so the two paths cannot drift.
        /// </summary>
        internal static IReadOnlyList<TakeOutLocationTerm> BuildTakeOutLocationTerms(
            BattleSoldier target,
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            List<TakeOutLocationTerm> terms = [];
            AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, null, terms);
            return terms;
        }

        /// <summary>
        /// takeOut(r) = sum over locations of w_loc * Phi((DamageRollMean - K_loc/damageCoefficient(r))
        /// / DamageRollStdDev). A fixed-size sum of normal CDFs; no allocation, no traversal.
        /// </summary>
        internal static float EvaluateTakeOutProbability(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return 0f;
            }
            float probability = 0f;
            for (int index = 0; index < terms.Count; index++)
            {
                probability += terms[index].Weight
                    * EvaluateTakeOutLocationTail(terms[index], damageCoefficient);
            }
            return Math.Clamp(probability, 0f, 1f);
        }

        private static float EvaluateTakeOutLocationTail(
            TakeOutLocationTerm term,
            float damageCoefficient)
        {
            float requiredRoll = term.PenetrationThreshold / damageCoefficient;
            return GaussianCalculator.ApproximateNormalCDF(
                (DamageRollMean - requiredRoll) / DamageRollStdDev);
        }

        /// <summary>
        /// PHASE 5. <c>E[woundProgress; no takeout]</c> from the same <c>(w_loc, K_loc)</c> vector
        /// take-out uses -- see <see cref="CalculateRemovalFractionOnHit"/> for the semantics.
        /// </summary>
        /// <summary>
        /// PHASE 5. Both halves of the graded fraction from ONE pass over the location vector. The
        /// lookahead calls this for every (policy, ply, enemy) triple, and running
        /// <see cref="EvaluateTakeOutProbability"/> and <see cref="EvaluateWoundProgress"/> back to
        /// back walked the vector twice and cost ~3x on the degrading-weapon path.
        /// </summary>
        internal static float EvaluateRemovalFraction(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return 0f;
            }
            bool graded = EffectiveWoundProgressCreditWeight > 0f;
            float takeOut = 0f;
            float progress = 0f;
            for (int index = 0; index < terms.Count; index++)
            {
                TakeOutLocationTerm term = terms[index];
                takeOut += term.Weight * EvaluateTakeOutLocationTail(term, damageCoefficient);
                if (graded)
                {
                    progress += term.Weight
                        * EvaluateWoundProgressTail(term, damageCoefficient);
                }
            }
            return CombineRemovalFraction(
                Math.Clamp(takeOut, 0f, 1f), Math.Clamp(progress, 0f, 1f));
        }

        internal static float EvaluateWoundProgress(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return 0f;
            }
            float progress = 0f;
            for (int index = 0; index < terms.Count; index++)
            {
                progress += terms[index].Weight
                    * EvaluateWoundProgressTail(terms[index], damageCoefficient);
            }
            return Math.Clamp(progress, 0f, 1f);
        }

        /// <summary>
        /// One location's partial expectation of fractional progress toward its disable threshold,
        /// integrated over the sub-take-out part of the damage roll.
        ///
        /// <para>The resolver's damage-to-wound-ratio map is affine in the damage roll, so the
        /// fraction of the remaining gap closed by a roll <c>R</c> is
        /// <c>(R - r0) / (r1 - r0)</c> between the wound-onset roll <c>r0 = K_zero / c</c> and the
        /// disabling roll <c>r1 = K_loc / c</c>. With <c>A</c> and <c>B</c> the standardized forms
        /// of those two rolls, the partial expectation has the closed form
        /// <c>[phi(A) - phi(B) - A*(Phi(B) - Phi(A))] / (B - A)</c> -- one exp and two CDFs, no
        /// quadrature, so the lookahead can afford it.</para>
        /// </summary>
        private static float EvaluateWoundProgressTail(
            TakeOutLocationTerm term,
            float damageCoefficient)
        {
            float disablingRoll = term.PenetrationThreshold / damageCoefficient;
            float onsetRoll = term.ZeroProgressThreshold / damageCoefficient;
            // A location already at (or past) its threshold on any wound at all has no partial
            // credit to give: every penetrating hit either disables it or does nothing.
            if (disablingRoll - onsetRoll <= 0.0001f)
            {
                return 0f;
            }
            float low = (onsetRoll - DamageRollMean) / DamageRollStdDev;
            float high = (disablingRoll - DamageRollMean) / DamageRollStdDev;
            float span = high - low;
            if (span <= 0.0001f)
            {
                return 0f;
            }
            float mass = GaussianCalculator.ApproximateNormalCDF(high)
                - GaussianCalculator.ApproximateNormalCDF(low);
            float partial = NormalPdf(low) - NormalPdf(high) - (low * mass);
            return Math.Clamp(partial / span, 0f, 1f);
        }

        /// <summary>
        /// The single hit-location walk behind both <see cref="CalculateTakeOutProbabilityOnHit"/>
        /// and <see cref="BuildTakeOutLocationTerms"/>. Pass a damage coefficient to get the
        /// probability, a collector to capture the range-independent terms, or both. When
        /// <paramref name="collector"/> is null nothing is allocated, so the hot scoring path is
        /// unchanged.
        /// </summary>
        private static (float TakeOut, float WoundProgress) AccumulateTakeOutTerms(
            BattleSoldier target,
            float effectiveArmor,
            float weaponWoundMultiplier,
            float? damageCoefficient,
            List<TakeOutLocationTerm> collector)
        {
            if (target == null || !target.IsCombatEffective || weaponWoundMultiplier <= 0)
            {
                return (0f, 0f);
            }

            Body body = target.Soldier.Body;
            int totalLocationWeight = body.TotalProbabilityMap[target.Stance];
            if (totalLocationWeight <= 0)
            {
                return (0f, 0f);
            }

            IReadOnlyList<int> functioningHands = target.FunctioningHandGroupIds;
            int? lastFunctioningHand = functioningHands.Count == 1
                ? functioningHands[0]
                : null;
            float probability = 0f;
            float woundProgress = 0f;
            foreach (HitLocation location in body.HitLocations)
            {
                int locationWeight = location.Template.HitProbabilityMap[(int)target.Stance];
                if (locationWeight <= 0 || location.IsSevered)
                {
                    continue;
                }

                bool canTakeOut =
                    location.Template.IsMotive
                    || location.Template.IsVital
                    || (lastFunctioningHand.HasValue
                        && location.Template.HandGroupId == lastFunctioningHand);
                if (!canTakeOut)
                {
                    continue;
                }

                float requiredRatio = FindMinimumDisablingWoundRatio(
                    location.Wounds.WoundTotal,
                    Math.Min(location.Template.CrippleWound, location.Template.SeverWound));
                if (float.IsPositiveInfinity(requiredRatio))
                {
                    continue;
                }

                // Execution first requires weapon penetration, then the resolver subtracts
                // natural armor and applies the hit-location multiplier before classifying the
                // wound. A carried Negligible wound only requires positive weapon penetration.
                float requiredPenetratingDamage = requiredRatio <= 0f
                    ? 0f
                    : ((target.Soldier.Constitution * requiredRatio)
                        / Math.Max(0.0001f, location.Template.WoundMultiplier)
                        + location.Template.NaturalArmor)
                        / weaponWoundMultiplier;
                // K_loc. Range-independent: the numerator of requiredRoll carries no range term,
                // which is what lets the Phase 4 table rescale take-out in closed form.
                // K_zero (Phase 5): the same expression evaluated at ratio -> 0+, i.e. the damage
                // at which this location first takes a wound at all. The gap between the two is
                // the graded band the woundProgress term integrates over.
                TakeOutLocationTerm term = new(
                    locationWeight / (float)totalLocationWeight,
                    effectiveArmor + requiredPenetratingDamage,
                    requiredRatio,
                    effectiveArmor
                        + (location.Template.NaturalArmor / weaponWoundMultiplier));
                collector?.Add(term);
                if (damageCoefficient.HasValue)
                {
                    probability += term.Weight
                        * EvaluateTakeOutLocationTail(term, damageCoefficient.Value);
                    // Only computed when lambda can actually use it; at lambda = 0 this walk is
                    // bitwise identical to the pre-Phase-5 one.
                    if (EffectiveWoundProgressCreditWeight > 0f)
                    {
                        woundProgress += term.Weight
                            * EvaluateWoundProgressTail(term, damageCoefficient.Value);
                    }
                }
            }
            return (
                Math.Clamp(probability, 0f, 1f),
                Math.Clamp(woundProgress, 0f, 1f));
        }

        private static float FindMinimumDisablingWoundRatio(
            uint currentWounds,
            uint disableThreshold)
        {
            ReadOnlySpan<(WoundLevel Level, float Ratio)> candidates =
            [
                (WoundLevel.Negligible, 0f),
                (WoundLevel.Minor, 0.125f),
                (WoundLevel.Moderate, 0.25f),
                (WoundLevel.Major, 0.5f),
                (WoundLevel.Critical, 1f),
                (WoundLevel.Massive, 2f),
                (WoundLevel.Mortal, 4f),
                (WoundLevel.Unsurvivable, 8f)
            ];
            foreach ((WoundLevel level, float ratio) in candidates)
            {
                if (AddWoundForEstimate(currentWounds, level) >= disableThreshold)
                {
                    return ratio;
                }
            }
            return float.PositiveInfinity;
        }

        // Pure mirror of Wounds.AddWound's six-per-level carry. Keeping this local lets scoring
        // inspect hypothetical hits without mutating the frozen battle state.
        private static uint AddWoundForEstimate(uint currentWounds, WoundLevel wound)
        {
            uint total = currentWounds + (uint)wound;
            for (int nibble = 0; nibble < 7; nibble++)
            {
                int shift = nibble * 4;
                if (((total >> shift) & 0xfu) <= Wounds.WOUND_MAX)
                {
                    continue;
                }
                total &= ~(0xfu << shift);
                total += 1u << (shift + 4);
            }
            return total;
        }

        private static float NormalPdf(float z)
        {
            return (float)(Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI));
        }

        // Midpoint quadrature over the delivery roll's standard normal on [-3, 3], weights
        // renormalized to sum to 1. Compile-time constant, so blast scoring is deterministic
        // and never touches the battle RNG.
        private static (float Z, float Weight)[] BuildStandardNormalQuadrature()
        {
            const float lo = -3f;
            const float hi = 3f;
            const float stepSize = 0.5f;
            List<(float Z, float Weight)> nodes = [];
            float total = 0f;
            for (float z = lo; z <= hi + 1e-4f; z += stepSize)
            {
                float weight = NormalPdf(z);
                nodes.Add((z, weight));
                total += weight;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = (nodes[i].Z, nodes[i].Weight / total);
            }
            return nodes.ToArray();
        }

        private readonly struct BlastNearbySoldier
        {
            public readonly float OffsetX;
            public readonly float OffsetY;
            public readonly bool Friendly;
            public readonly BattleSoldier Target;
            public readonly float BattleValue;
            public readonly RangedWeapon Weapon;

            public BlastNearbySoldier(
                float offsetX,
                float offsetY,
                bool friendly,
                BattleSoldier target,
                float battleValue,
                RangedWeapon weapon)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Friendly = friendly;
                Target = target;
                BattleValue = battleValue;
                Weapon = weapon;
            }
        }

        private readonly struct BlastThrowOutcome
        {
            public readonly float EnemyBattleValueRemoved;
            public readonly float FriendlyBattleValueLost;
            public readonly IReadOnlyList<int> NominalVictimIds;

            public BlastThrowOutcome(
                float enemyBattleValueRemoved,
                float friendlyBattleValueLost,
                IReadOnlyList<int> nominalVictimIds)
            {
                EnemyBattleValueRemoved = enemyBattleValueRemoved;
                FriendlyBattleValueLost = friendlyBattleValueLost;
                NominalVictimIds = nominalVictimIds;
            }
        }

        // Scores a single grenade aim point (the target's cell) by integrating expected enemy and
        // friendly battle value over BOTH the delivery scatter distribution and the per-victim
        // damage roll. A throw that only catches the squad when it scatters is no longer free:
        // every miss node lands the template somewhere and pays its friendly cost. Neither enemy
        // nor friendly value carries an arrival-time discount -- the grenade detonates this turn
        // (Phase 3, Design/Active/EngagementScoringOverhaul.md), matching the conventional ranged
        // path. Replaces the former perfect-impact-times-confidence estimate. See
        // OnlyWar_TDD.md §6.6.
        private BlastThrowOutcome EvaluateBlastThrow(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float bulkMultiplier)
        {
            float skill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
            float modifier = BattleModifiersUtil.CalculateBlastRangeModifier(
                    soldier.Soldier, weapon.Template, range)
                - (weapon.Template.Bulk * bulkMultiplier);
            // deliveryRoll = mean + stdDev * z, so margin(z) = (skill + modifier - mean) - stdDev * z.
            float baseMargin = skill + modifier - BlastDeliveryRollMean;
            float areaRadius = weapon.Template.AreaRadius;
            float radiusSquared = areaRadius * areaRadius;

            ValueTuple<int, int> aimCell = BlastTemplate.ResolveImpactCell(
                _grid, soldier.Soldier.Id, target.Soldier.Id, margin: 0f, directionRoll: 0.0);

            float gatherRadius = areaRadius + BlastScatterMaxGatherCells;
            float gatherRadiusSquared = gatherRadius * gatherRadius;
            bool shooterSide = _grid.GetSoldierSide(soldier.Soldier.Id);

            // One direct scan of the (small) field collects every soldier a scatter node could
            // reach, plus their offset from the aim cell — cheaper and more precise than a per-node
            // disc query, and it yields the nominal (on-target) victim list for logging in one pass.
            List<BlastNearbySoldier> nearby = [];
            List<int> nominalVictims = [];
            foreach (BattleSoldier candidate in _soldierMap.Values)
            {
                if (!candidate.IsCombatEffective || !IsPlaced(candidate))
                {
                    continue;
                }
                IList<ValueTuple<int, int>> footprint =
                    _grid.GetSoldierPosition(candidate.Soldier.Id);
                if (footprint == null || footprint.Count == 0)
                {
                    continue;
                }
                // Represent the soldier by whichever footprint cell sits closest to the aim
                // point, mirroring how BlastTemplate.GetVictims credits a figure's nearest cell.
                float offsetX = 0f;
                float offsetY = 0f;
                float distanceSquared = float.MaxValue;
                foreach (ValueTuple<int, int> cell in footprint)
                {
                    float cellX = cell.Item1 - aimCell.Item1;
                    float cellY = cell.Item2 - aimCell.Item2;
                    float cellDistanceSquared = (cellX * cellX) + (cellY * cellY);
                    if (cellDistanceSquared < distanceSquared)
                    {
                        distanceSquared = cellDistanceSquared;
                        offsetX = cellX;
                        offsetY = cellY;
                    }
                }
                if (distanceSquared > gatherRadiusSquared)
                {
                    continue;
                }
                bool friendly = _grid.GetSoldierSide(candidate.Soldier.Id) == shooterSide;
                nearby.Add(new BlastNearbySoldier(
                    offsetX,
                    offsetY,
                    friendly,
                    candidate,
                    GetBattleValue(candidate),
                    weapon));
                if (distanceSquared <= radiusSquared)
                {
                    nominalVictims.Add(candidate.Soldier.Id);
                }
            }

            float enemyBattleValueRemoved = 0f;
            float friendlyBattleValueLost = 0f;
            foreach ((float z, float weight) in BlastDeliveryQuadrature)
            {
                float margin = baseMargin - (BlastDeliveryRollStdDev * z);
                if (margin >= 0f)
                {
                    // On-target node: the whole weight lands on the aim cell.
                    AccumulateBlastNode(
                        nearby, 0f, 0f, areaRadius, radiusSquared, weight,
                        ref enemyBattleValueRemoved, ref friendlyBattleValueLost);
                    continue;
                }
                // Scattered node: the impact deviates |margin| * ScatterDistancePerPoint cells in a
                // uniformly random direction, so split the node weight across the angular samples.
                float scatterDistance = -margin * BlastTemplate.ScatterDistancePerPoint;
                float angleWeight = weight / BlastScatterAngleSamples;
                for (int angleIndex = 0; angleIndex < BlastScatterAngleSamples; angleIndex++)
                {
                    double angle = (2.0 * Math.PI * angleIndex) / BlastScatterAngleSamples;
                    float impactX = (float)(scatterDistance * Math.Cos(angle));
                    float impactY = (float)(scatterDistance * Math.Sin(angle));
                    AccumulateBlastNode(
                        nearby, impactX, impactY, areaRadius, radiusSquared, angleWeight,
                        ref enemyBattleValueRemoved, ref friendlyBattleValueLost);
                }
            }

            return new BlastThrowOutcome(
                enemyBattleValueRemoved, friendlyBattleValueLost, nominalVictims);
        }

        // Adds one integration node's contribution: every gathered soldier within the template of
        // an impact at (impactX, impactY) relative to the aim cell, weighted by the node weight.
        private static void AccumulateBlastNode(
            List<BlastNearbySoldier> nearby,
            float impactX,
            float impactY,
            float areaRadius,
            float radiusSquared,
            float weight,
            ref float enemyBattleValueRemoved,
            ref float friendlyBattleValueLost)
        {
            for (int i = 0; i < nearby.Count; i++)
            {
                BlastNearbySoldier victim = nearby[i];
                float dx = victim.OffsetX - impactX;
                float dy = victim.OffsetY - impactY;
                float distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }
                float falloff = 1f - (MathF.Sqrt(distanceSquared) / areaRadius);
                float armor = victim.Target.Armor?.Template.ArmorProvided ?? 0f;
                // Phase 5 graded fraction, applied to BOTH the enemy and the friendly half below --
                // Phase 3 showed that pricing one side of a blast differently from the other is
                // pure accounting asymmetry, not caution.
                float removalFraction = CalculateRemovalFractionOnHit(
                    victim.Target,
                    victim.Weapon.Template.DamageMultiplier * falloff * falloff,
                    armor * victim.Weapon.Template.ArmorMultiplier,
                    victim.Weapon.Template.WoundMultiplier);
                float removed = weight * removalFraction * victim.BattleValue;
                if (victim.Friendly)
                {
                    friendlyBattleValueLost += removed;
                }
                else
                {
                    enemyBattleValueRemoved += removed;
                }
            }
        }

        internal RangedTargetEvaluation EvaluateRangedTarget(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            float? targetSpeed = null)
        {
            float evaluatedTargetSpeed = targetSpeed ?? target.CurrentSpeed;
            var cacheKey = (
                soldier.Soldier.Id,
                target.Soldier.Id,
                weapon.Template.Id,
                BitConverter.SingleToInt32Bits(range),
                BitConverter.SingleToInt32Bits(additionalToHitModifier),
                BitConverter.SingleToInt32Bits(evaluatedTargetSpeed),
                (int)weapon.LoadedAmmo);
            if (_context.RangedEvaluations.TryGetValue(cacheKey, out RangedTargetEvaluation cached))
            {
                return cached;
            }

            ValueTuple<float, float, int, float, float> attackEstimate = EstimatePlannedRangedAttack(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                evaluatedTargetSpeed);
            float takeOutProbability = Math.Clamp(attackEstimate.Item2, 0, 1);
            // CONTRACT: the expected battle value this shot removes THIS TURN -- hit probability x
            // take-out probability x the target's battle value. It is deliberately undiscounted:
            // Phase 3 (Design/Active/EngagementScoringOverhaul.md) removed the old
            // 1/(1 + turnsUntilTargetReachesUs) factor, which scaled a fired bolt's worth by when
            // its target would reach us. Arrival time does not affect whether a bolt lands, and the
            // temporal preference it was standing in for is already carried by
            // EngagementFutureDiscount. Distance enters through CalculateRangeModifier, not here.
            // Phase 5: the multiplier is the GRADED removal fraction, not the bare take-out
            // probability -- a hit that softens a target it cannot yet kill has now done something.
            // At lambda = 0 CombineRemovalFraction is the clamp this line already applied.
            // The burst, not one hit: ExpectedBurstRemovalFraction integrates the recoil loop
            // ShootAction actually resolves, so a nine-round bolt burst is no longer priced as a
            // single bolt. Unchanged for a one-shot weapon.
            float enemyBattleValueRemoved = ExpectedBurstRemovalFraction(
                    attackEstimate.Item4,
                    attackEstimate.Item3,
                    weapon.Template.Recoil,
                    CombineRemovalFraction(attackEstimate.Item2, attackEstimate.Item5))
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
                friendlyBattleValueLost,
                attackEstimate.Item4,
                evaluatedTargetSpeed,
                attackEstimate.Item5);
            _context.RangedEvaluations[cacheKey] = result;
            return result;
        }

        private IReadOnlyList<BattleSquad> GetNearestInRangeEnemySquads(
            BattleSoldier shooter,
            ValueTuple<int, int>? movementDirection = null)
        {
            // The nearest in-range enemy squads are a pure function of the frozen layout, the
            // shooter, and the firing direction, yet SelectBestRangedTarget and
            // SelectBestTemplateFiringLine each request them with the same arguments (and again
            // across planning phases). Memoize per (shooter, direction) for the turn.
            var cacheKey = (shooter.Soldier.Id, movementDirection);
            if (_context.NearestInRangeSquads.TryGetValue(cacheKey, out IReadOnlyList<BattleSquad> cached))
            {
                return cached;
            }

            // Effective range matters for thrown weapons (a grenade's reach scales with
            // the thrower's Strength); every other weapon reads its raw MaximumRange.
            float maximumRange = shooter.EquippedRangedWeapons
                .Where(weapon => weapon.LoadedAmmo > 0)
                .Select(weapon => BattleModifiersUtil.GetEffectiveMaxRange(
                    shooter.Soldier,
                    weapon.Template))
                .DefaultIfEmpty(0)
                .Max();
            IReadOnlyList<BattleSquad> nearest = GetNearestEnemySquadsWithinRange(
                shooter,
                maximumRange,
                movementDirection);
            _context.NearestInRangeSquads[cacheKey] = nearest;
            return nearest;
        }

        private IReadOnlyList<BattleSquad> GetNearestEnemySquadsWithinRange(
            BattleSoldier shooter,
            float maximumRange,
            ValueTuple<int, int>? movementDirection = null)
        {
            if (maximumRange <= 0 || !IsPlaced(shooter)) return [];

            bool restrictFiringArc = HasRestrictedJogFiringArc(movementDirection);
            ValueTuple<int, int> firingDirection = movementDirection.GetValueOrDefault();

            // Keep only the three best squads while scanning. The previous LINQ pipeline
            // grouped every enemy, allocated a projection for every squad, sorted all of
            // them, and materialized the result on every firing evaluation.
            List<(BattleSquad Squad, float Distance)> candidates =
                new(RangedTargetSquadCandidateCount);
            foreach ((int enemyId, float distance) in
                _grid.GetEnemyDistances(shooter.Soldier.Id))
            {
                if (distance > maximumRange)
                {
                    continue;
                }
                if (!_soldierMap.TryGetValue(enemyId, out BattleSoldier enemy)
                    || !enemy.IsCombatEffective
                    || enemy.BattleSquad == null
                    || (restrictFiringArc && !IsWithinJogFiringArc(
                        shooter,
                        enemy,
                        firingDirection)))
                {
                    continue;
                }

                int existingIndex = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (ReferenceEquals(candidates[i].Squad, enemy.BattleSquad))
                    {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    if (distance >= candidates[existingIndex].Distance)
                    {
                        continue;
                    }
                    candidates.RemoveAt(existingIndex);
                }
                else if (candidates.Count == RangedTargetSquadCandidateCount
                    && CompareSquadRange(
                        distance,
                        enemy.BattleSquad.Id,
                        candidates[^1].Distance,
                        candidates[^1].Squad.Id) >= 0)
                {
                    continue;
                }

                int insertionIndex = 0;
                while (insertionIndex < candidates.Count
                    && CompareSquadRange(
                        candidates[insertionIndex].Distance,
                        candidates[insertionIndex].Squad.Id,
                        distance,
                        enemy.BattleSquad.Id) <= 0)
                {
                    insertionIndex++;
                }
                candidates.Insert(insertionIndex, (enemy.BattleSquad, distance));
                if (candidates.Count > RangedTargetSquadCandidateCount)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                }
            }

            if (candidates.Count == 0)
            {
                return [];
            }

            BattleSquad[] result = new BattleSquad[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                result[i] = candidates[i].Squad;
            }
            return result;
        }

        private static int CompareSquadRange(
            float leftDistance,
            int leftSquadId,
            float rightDistance,
            int rightSquadId)
        {
            int distanceComparison = leftDistance.CompareTo(rightDistance);
            return distanceComparison != 0
                ? distanceComparison
                : leftSquadId.CompareTo(rightSquadId);
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
                    // Phase 5 graded fraction: the friendly cost of a stray must be priced in the
                    // same currency as the enemy value the shot buys, or the trade is rigged.
                    float removalFraction = CalculateRangedRemovalFraction(
                        participant, weapon, range, armor);
                    return victimProbability
                        * removalFraction
                        * GetBattleValue(participant);
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

        private bool IsPlaced(BattleSoldier soldier)
        {
            return soldier != null
                && _grid.IsSoldierPlaced(soldier.Soldier.Id);
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

        private RangedTargetEvaluation GetBestWeaponForSituation(
            BattleSoldier soldier,
            BattleSoldier target,
            float range,
            float bulkMultiplier,
            bool useAccuracy,
            float aimMultiplier)
        {
            RangedTargetEvaluation best = null;
            float bestScore = float.MinValue;
            IReadOnlyList<RangedWeapon> orderedWeapons =
                OrderRangedByDamageMultiplierDescending(soldier.EquippedRangedWeapons);
            for (int weaponIndex = 0; weaponIndex < orderedWeapons.Count; weaponIndex++)
            {
                RangedWeapon weapon = orderedWeapons[weaponIndex];
                if (weapon.Template.IsTemplateWeapon
                    || range > weapon.Template.MaximumRange
                    || weapon.LoadedAmmo <= 0)
                {
                    continue;
                }

                float bulkAndAccMod = 0;
                bulkAndAccMod -= weapon.Template.Bulk * bulkMultiplier;
                // base accuracy bonus is the weapon's accuracy plus 1 for aiming making it an all-out attack
                bulkAndAccMod += useAccuracy
                    ? (weapon.Template.Accuracy + 1) * aimMultiplier
                    : 0;
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

        // Equipped-weapon lists are tiny (usually a single weapon), yet the innermost targeting
        // loops previously rebuilt a LINQ Where/OrderBy pipeline over them for every candidate
        // target, allocating an enumerator and an ordering buffer each pass. These helpers
        // materialize the deterministic ordering once per planning call; the single-weapon fast
        // path returns the source list without allocating.
        private static IReadOnlyList<RangedWeapon> OrderRangedByTemplateId(
            IReadOnlyList<RangedWeapon> equipped)
        {
            if (equipped.Count <= 1) return equipped;
            RangedWeapon[] ordered = new RangedWeapon[equipped.Count];
            for (int i = 0; i < equipped.Count; i++) ordered[i] = equipped[i];
            // Template.Id is unique, so this total ordering reproduces the previous OrderBy exactly.
            Array.Sort(ordered, static (first, second) =>
                first.Template.Id.CompareTo(second.Template.Id));
            return ordered;
        }

        private static IReadOnlyList<RangedWeapon> OrderRangedByDamageMultiplierDescending(
            IReadOnlyList<RangedWeapon> equipped)
        {
            if (equipped.Count <= 1) return equipped;
            RangedWeapon[] ordered = new RangedWeapon[equipped.Count];
            for (int i = 0; i < equipped.Count; i++) ordered[i] = equipped[i];
            // Stable insertion sort by descending DamageMultiplier, preserving the original relative
            // order on ties to match LINQ's stable OrderByDescending exactly. Equal keys must not be
            // reordered: the chosen weapon feeds seeded battle resolution.
            for (int i = 1; i < ordered.Length; i++)
            {
                RangedWeapon key = ordered[i];
                float keyMultiplier = key.Template.DamageMultiplier;
                int j = i - 1;
                while (j >= 0 && ordered[j].Template.DamageMultiplier < keyMultiplier)
                {
                    ordered[j + 1] = ordered[j];
                    j--;
                }
                ordered[j + 1] = key;
            }
            return ordered;
        }

        // (HitProbability, TakeOutProbabilityOnHit, ShotsToFire, PreRollHitTotal, WoundProgressOnHit)
        private ValueTuple<float, float, int, float, float> EstimatePlannedRangedAttack(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float moveAndAimMod,
            float? targetSpeed = null)
        {
            int shotsToFire = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, (int)weapon.LoadedAmmo));
            float armor = target.Armor?.Template.ArmorProvided ?? 0;
            (float takeOutProbability, float woundProgress) =
                CalculateRangedHitRemoval(target, weapon, range, armor);
            bool firingIntoMelee = _grid.IsTargetEngagedWithShootersAllies(
                soldier.Soldier.Id,
                target.Soldier.Id);
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                moveAndAimMod,
                firingIntoMelee,
                targetSpeed);
            ValueTuple<float, float, float> estimate = new(0, 0, 0);
            for (int iteration = 0; iteration < 4; iteration++)
            {
                estimate = EstimateHitAndDamage(
                    hitContext,
                    takeOutProbability,
                    shotsToFire);
                int revisedShots = CalculateShotsToFire(
                    weapon,
                    estimate.Item1,
                    estimate.Item2);
                if (revisedShots == shotsToFire)
                {
                    return new ValueTuple<float, float, int, float, float>(
                        estimate.Item1,
                        estimate.Item2,
                        shotsToFire,
                        estimate.Item3,
                        woundProgress);
                }

                shotsToFire = revisedShots;
            }

            // Recalculate once with the final shot count so the returned probability is exactly
            // the one ShootAction will resolve, even if a future rule introduces oscillation.
            estimate = EstimateHitAndDamage(
                hitContext,
                takeOutProbability,
                shotsToFire);
            return new ValueTuple<float, float, int, float, float>(
                estimate.Item1, estimate.Item2, shotsToFire, estimate.Item3, woundProgress);
        }

        private int CalculateShotsToFire(
            RangedWeapon weapon,
            float toHitAtPlannedRateOfFire,
            float takeOutProbabilityOnHit)
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

            if (takeOutProbabilityOnHit <= 0)
            {
                return minRoF;
            }

            // Fire enough independent shots to reach the same take-out confidence used by melee
            // strike planning. This quantity is now a probability, not a linear damage fraction.
            float perShotTakeOut = Math.Clamp(
                toHitAtPlannedRateOfFire * takeOutProbabilityOnHit,
                0f,
                1f);
            if (perShotTakeOut <= 0f)
            {
                return minRoF;
            }
            int killRof = perShotTakeOut >= 1f
                ? 1
                : (int)Math.Ceiling(
                    Math.Log(1f - TargetTakeOutConfidenceThreshold)
                    / Math.Log(1f - perShotTakeOut));

            return Math.Clamp(killRof, minRoF, maxRof);

        }

        private static ValueTuple<float, float, float> EstimateHitAndDamage(
            RangedHitEstimateContext hitContext,
            float expectedDamage,
            int numberOfShots)
        {
            float preRollHitTotal = hitContext.CalculatePreRollHitTotal(numberOfShots);
            float probability = GaussianCalculator.ApproximateNormalCDF(
                (preRollHitTotal - HitRollMean) / HitRollStdDev);
            return new ValueTuple<float, float, float>(
                probability, expectedDamage, preRollHitTotal);
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

        private ValueTuple<int, int> AddMoveAction(
            BattleSoldier soldier,
            float moveSpeed,
            ValueTuple<int, int> line,
            SquadMovementTier? tier = null)
        {
            ValueTuple<int, int> desiredMove = CalculateMovementAlongLine(line, moveSpeed);
            ValueTuple<int, int> newLocation = new ValueTuple<int, int>(soldier.TopLeft.Value.Item1 + desiredMove.Item1, soldier.TopLeft.Value.Item2 + desiredMove.Item2);
            SquadMovementTier movementTier = tier ?? soldier.BattleSquad.MovementTier;
            ushort orientation = CalculateOrientationFromVector(line, soldier, movementTier);
            newLocation = FindBestLocation(
                soldier,
                soldier.TopLeft.Value,
                newLocation,
                moveSpeed,
                orientation);
            _grid.ReserveMoveDestination(soldier, newLocation, orientation);
            _moveActions.Add(new MoveAction(
                soldier,
                _grid,
                soldier.TopLeft.Value,
                newLocation,
                orientation,
                moveSpeed));
            ValueTuple<int, int> actualDirection = new(
                newLocation.Item1 - soldier.TopLeft.Value.Item1,
                newLocation.Item2 - soldier.TopLeft.Value.Item2);
            soldier.CurrentSpeed = Math.Min(
                GetTierSpeed(soldier, movementTier),
                (float)Math.Sqrt(
                    actualDirection.Item1 * actualDirection.Item1
                    + actualDirection.Item2 * actualDirection.Item2));
            if (soldier.CurrentSpeed <= 0)
            {
                soldier.IsRunning = false;
            }
            LogMove(soldier, movementTier, moveSpeed, desiredMove, actualDirection);
            return actualDirection.Item1 == 0 && actualDirection.Item2 == 0
                ? line
                : actualDirection;
        }

        /// <summary>
        /// Per-soldier movement trace: what the tier allowed, what the soldier asked for, and what
        /// the grid actually gave it.
        ///
        /// <para>WHY THE THREE ARE SEPARATE. A soldier that covers less ground than its tier permits
        /// has been squeezed by <see cref="FindBestLocation"/>, which walks the major axis down one
        /// cell at a time until the whole footprint fits in free, unreserved cells. That is invisible
        /// in the squad-level ENGAGE_EVAL record -- which reports the posture CHOSEN, not the
        /// distance ACHIEVED -- and it bites large models hardest, since an N-cell footprint needs
        /// every one of those cells. Without <c>blocked</c> there is no way to tell a monster that
        /// decided to jog from one that tried to run and could not fit.</para>
        /// </summary>
        private void LogMove(
            BattleSoldier soldier,
            SquadMovementTier tier,
            float budget,
            ValueTuple<int, int> desired,
            ValueTuple<int, int> achieved)
        {
            if (_log == null) return;
            float desiredDistance = (float)Math.Sqrt(
                (desired.Item1 * desired.Item1) + (desired.Item2 * desired.Item2));
            float achievedDistance = (float)Math.Sqrt(
                (achieved.Item1 * achieved.Item1) + (achieved.Item2 * achieved.Item2));
            string line = new BattleDecisionTrace("MOVE", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("squad", soldier.BattleSquad?.Id),
                BattleDecisionTrace.Field("tier", tier),
                BattleDecisionTrace.Field("base_speed", soldier.GetMoveSpeed()),
                BattleDecisionTrace.Field("tier_speed", GetTierSpeed(soldier, tier)),
                BattleDecisionTrace.Field("budget", budget),
                BattleDecisionTrace.Field("leftover_in", soldier.LeftoverMovement),
                BattleDecisionTrace.Field("desired", desiredDistance),
                BattleDecisionTrace.Field("achieved", achievedDistance),
                // The whole point of the record: achieved < desired means the footprint did not fit.
                BattleDecisionTrace.Field("blocked", achievedDistance + 0.0001f < desiredDistance),
                BattleDecisionTrace.Field("current_speed", soldier.CurrentSpeed)
            }).Render();
            lock (_log)
            {
                _log(line);
            }
        }

        private static bool HasRestrictedJogFiringArc(
            ValueTuple<int, int>? movementDirection)
        {
            return movementDirection.HasValue
                && (movementDirection.Value.Item1 != 0
                    || movementDirection.Value.Item2 != 0);
        }

        private static bool IsWithinJogFiringArc(
            BattleSoldier shooter,
            BattleSoldier target,
            ValueTuple<int, int> movementDirection)
        {
            int targetX = target.TopLeft.Value.Item1 - shooter.TopLeft.Value.Item1;
            int targetY = target.TopLeft.Value.Item2 - shooter.TopLeft.Value.Item2;
            long dotProduct = ((long)movementDirection.Item1 * targetX)
                + ((long)movementDirection.Item2 * targetY);
            return dotProduct >= 0;
        }

        private ValueTuple<int, int> CalculateMovementAlongLine(ValueTuple<int, int> line, float moveSpeed)
        {
            ValueTuple<int, int> targetLocation;
            if (moveSpeed <= 0) return new ValueTuple<int, int>(0, 0);   // this shouldn't happen
            else if(line.Item1 == 0)
            {
                targetLocation = new ValueTuple<int, int>(0, line.Item2 < 0 ? -(int)moveSpeed : (int)moveSpeed);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }
            else if(line.Item2 == 0)
            {
                targetLocation = new ValueTuple<int, int>(line.Item1 < 0 ? -(int)moveSpeed : (int)moveSpeed, 0);
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
                    return new ValueTuple<int, int>(1, 0);
                }
                else
                {
                    return new ValueTuple<int, int>(0, 1);
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
                        return new ValueTuple<int, int>(x, y);
                    }
                }
                else if (line.Item2 != 0 && yLeftover != 0)
                {
                    int x = xDistance < 0 ? (int)xDistance - 1: (int)xDistance + 1;
                    int y = (int)yDistance;
                    if ((x * x) + (y * y) < speedSq)
                    {
                        return new ValueTuple<int, int>(x, y);
                    }
                }
            }
            return new ValueTuple<int, int> ((int)xDistance, (int)yDistance);
        }

        private ushort CalculateOrientationFromVector(
            ValueTuple<int, int> vector,
            BattleSoldier soldier = null,
            SquadMovementTier tier = SquadMovementTier.Stationary)
        {
            if (vector.Item1 == 0 && vector.Item2 == 0)
            {
                return soldier?.Orientation ?? 0;
            }

            double angle = Math.Atan2(vector.Item1, vector.Item2);
            int desired = (int)Math.Round(angle / (Math.PI / 4.0));
            desired = (desired % BattleOrientation.HeadingCount
                + BattleOrientation.HeadingCount)
                % BattleOrientation.HeadingCount;

            if (soldier == null
                || (tier != SquadMovementTier.Run && tier != SquadMovementTier.InMelee))
            {
                return (ushort)desired;
            }

            int current = soldier.Orientation % BattleOrientation.HeadingCount;
            int difference = desired - current;
            if (difference > BattleOrientation.HeadingCount / 2)
            {
                difference -= BattleOrientation.HeadingCount;
            }
            else if (difference < -(BattleOrientation.HeadingCount / 2))
            {
                difference += BattleOrientation.HeadingCount;
            }

            int limited = Math.Clamp(difference, -1, 1);
            return (ushort)((current + limited + BattleOrientation.HeadingCount)
                % BattleOrientation.HeadingCount);
        }

        private ValueTuple<int, int> FindBestLocation(
            BattleSoldier soldier,
            ValueTuple<int, int> startingPoint,
            ValueTuple<int, int> targetPoint,
            float speed,
            ushort orientation,
            BattleGridManager grid = null)
        {
            grid ??= _grid;
            float speedSq = speed * speed;
            int xMove = targetPoint.Item1 - startingPoint.Item1;
            int yMove = targetPoint.Item2 - startingPoint.Item2;
            // Shift around the shorter axis first: the major axis carries the intent of the move,
            // so give ground on the minor one.
            bool majorIsX = xMove * xMove > yMove * yMove;
            int major = majorIsX ? xMove : yMove;
            int minor = majorIsX ? yMove : xMove;
            // Which side of the intended lateral offset gets probed first. Outward (away from the
            // line of travel) matches the pre-existing bias.
            int leadSide = minor < 0 ? -1 : 1;

            while (major * major > 0)
            {
                float lateralBudgetSq = speedSq - (major * major);
                // Probe the intended offset first and then alternate outward from it — 0, +1, -1,
                // +2, -2, … A side that has left the movement budget is skipped rather than ending
                // the search, because when the intended offset is nonzero the two sides run out at
                // different magnitudes and the nearer one still has usable squares.
                for (int magnitude = 0; ; magnitude++)
                {
                    bool anyWithinBudget = false;
                    int sides = magnitude == 0 ? 1 : 2;
                    for (int side = 0; side < sides; side++)
                    {
                        int lateral = minor
                            + (magnitude * (side == 0 ? leadSide : -leadSide));
                        if (lateral * lateral > lateralBudgetSq) continue;
                        anyWithinBudget = true;
                        ValueTuple<int, int> newTarget = majorIsX
                            ? new ValueTuple<int, int>(
                                startingPoint.Item1 + major,
                                startingPoint.Item2 + lateral)
                            : new ValueTuple<int, int>(
                                startingPoint.Item1 + lateral,
                                startingPoint.Item2 + major);
                        if (grid.IsMoveDestinationAvailable(soldier, newTarget, orientation))
                        {
                            return newTarget;
                        }
                    }
                    if (!anyWithinBudget) break;
                }
                // if we can't find a lateral move that works, start over with the main axis reduced by 1
                major -= major > 0 ? 1 : -1;
            }
            return startingPoint;
        }

        // Mirror of ShootAction.HandleHit / AreaAttackAction: effective strength at range
        // scales the damage roll before armor subtraction.
        private static float CalculateRangedTakeOutProbability(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
        {
            return CalculateTakeOutProbabilityOnHit(
                target,
                BattleModifiersUtil.CalculateDamageAtRange(weapon, range),
                armor * weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier);
        }

        // Phase 5: the graded sibling of CalculateRangedTakeOutProbability. Every site that turns a
        // landed hit into expected BATTLE VALUE removed reads this; CalculateShotsToFire and the
        // Phase 4 table's ReferenceTakeOut keep reading the raw take-out probability, because a
        // shot count is a question about kills, not about accumulated wounds.
        private static float CalculateRangedRemovalFraction(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
        {
            (float takeOut, float progress) =
                CalculateRangedHitRemoval(target, weapon, range, armor);
            return CombineRemovalFraction(takeOut, progress);
        }

        // Both halves from ONE hit-location walk, for the path that needs the raw take-out
        // probability (shot count) and the graded fraction (battle value) from the same shot.
        private static (float TakeOut, float WoundProgress) CalculateRangedHitRemoval(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
        {
            float damageCoefficient = BattleModifiersUtil.CalculateDamageAtRange(weapon, range);
            if (damageCoefficient <= 0)
            {
                return (0f, 0f);
            }
            return AccumulateTakeOutTerms(
                target,
                armor * weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier,
                damageCoefficient,
                null);
        }
    }
}
