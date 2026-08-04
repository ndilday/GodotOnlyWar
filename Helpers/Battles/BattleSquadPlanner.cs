using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        private const float DamageRollMean = 3.5f;
        private const float DamageRollStdDev = 1.75f;
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
            public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

            public RangedTargetEvaluation(
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                int shotsToFire,
                float hitProbability,
                float takeOutProbabilityOnHit,
                float expectedEnemyBattleValueRemoved,
                float expectedFriendlyBattleValueLost)
            {
                Target = target;
                Weapon = weapon;
                Range = range;
                ShotsToFire = shotsToFire;
                HitProbability = hitProbability;
                TakeOutProbabilityOnHit = takeOutProbabilityOnHit;
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

        internal int CachedSquadImminenceCount => _context.SquadImminence.Count;
        internal int CachedRangedEvaluationCount => _context.RangedEvaluations.Count;

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
                .OrderByDescending(candidate => candidate.Kind == squad.LastEngagementOptionKind)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Kind)
                .FirstOrDefault()
                ?? new EngagementOptionEvaluation(
                    EngagementOptionKind.Hold,
                    SquadMovementTier.Stationary,
                    null, 0, 0, 0, 0, 0, 0, [], 0, 0, 0, 0);
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

        private static List<EngagementOptionKind> GetLegalOptionKinds(
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
            if (frame.Role == EngagementSquadRole.Pursuit)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                EngagementOptionKind fast = distance <= profile.MoveSpeed
                        + BattleContactRules.MeleeContactAllowance
                    ? EngagementOptionKind.CloseToContact
                    : EngagementOptionKind.RunToward;
                return [EngagementOptionKind.Hold, EngagementOptionKind.JogToward, fast];
            }

            List<EngagementOptionKind> result =
            [
                EngagementOptionKind.Hold,
                EngagementOptionKind.StepBack,
                EngagementOptionKind.StepForward,
                EngagementOptionKind.JogToward,
                EngagementOptionKind.CloseToContact
            ];
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
            float score = enemyRemoval - friendlyFire + readiness - incoming + meleeNow
                + discountedFuture + roleTerm - commitment + hysteresis;
            return new EngagementOptionEvaluation(
                kind, tier, intended, feasibleSpeed,
                enemyRemoval, friendlyFire, readiness, incoming, meleeNow,
                future, roleTerm, commitment, hysteresis, score, rootActions);
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
                .Where(target => target.CanFight
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
                    ExpectedFriendlyBattleValueLost: blast.ExpectedFriendlyBattleValueLost);
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
                float attackerBulk = enemyFrame.BaselinePosture switch
                {
                    EngagementOptionKind.StepBack or EngagementOptionKind.StepForward =>
                        WalkBulkMultiplier,
                    EngagementOptionKind.JogToward => FullBulkMultiplier,
                    EngagementOptionKind.CloseToContact or EngagementOptionKind.RunToward =>
                        float.PositiveInfinity,
                    _ => 0f
                };
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
                if (estimate.ReachesContactThisTurn)
                {
                    melee += estimate.MeleeBattleValue;
                    reaches++;
                }
            }
            float seatFraction = Math.Min(1f,
                profile.ContactCapacity / (float)Math.Max(1, squad.AbleSoldiers.Count));
            melee *= Math.Min(seatFraction, reaches / (float)Math.Max(1, squad.AbleSoldiers.Count));
            float lockCost = reaches > 0
                ? Math.Max(0, profile.UsableRangedBattleValue - profile.UsableMeleeBattleValue)
                    * 0.12f
                : 0;
            return (Math.Min(melee, primary.AbleSoldiers.Sum(GetBattleValue)), closing + lockCost);
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

        private static float EvaluateBestContinuation(
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
                float terminal = 0;
                foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
                {
                    float range = Math.Max(0, ranges[enemy.Id]);
                    float desired = profile.IsContactSeeking
                        ? 1f
                        : Math.Max(1f, profile.PreferredBandUpper);
                    float turnsToAct = Math.Max(0, range - desired)
                        / Math.Max(0.1f, profile.MoveSpeed);
                    float attainable = Math.Max(
                        profile.UsableRangedBattleValue,
                        profile.UsableMeleeBattleValue);
                    terminal += frames[squad.Id].PairWeights.GetValueOrDefault(enemy.Id)
                        * attainable * 0.25f / (1f + turnsToAct);
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
                    float outgoingAllocation = frames[squad.Id].PairWeights.GetValueOrDefault(enemy.Id);
                    float incomingAllocation = frames[enemy.Id].PairWeights.GetValueOrDefault(squad.Id);
                    float outgoingRetention = policy switch
                    {
                        EngagementOptionKind.Hold => 1f,
                        EngagementOptionKind.JogToward => 0.65f,
                        _ => 0f
                    };
                    exchange += outgoingAllocation
                        * AggregateRemovalRate(profile, opposing, range)
                        * outgoingRetention
                        - incomingAllocation * AggregateRemovalRate(opposing, profile, range);
                    float ourMotion = PolicyRangeDelta(profile, range, policy);
                    float theirMotion = frames[squad.Id].Role == EngagementSquadRole.Pursuit
                        ? Math.Max(0, frames[squad.Id].QuarryRunSpeed)
                        : BaselineRangeDelta(opposing, range);
                    nextRanges[enemy.Id] = Math.Max(0, range + ourMotion + theirMotion);
                }
                float value = exchange + EngagementFutureDiscount * EvaluateBestContinuation(
                    squad, profile, profiles, frames, enemies, nextRanges, depth - 1);
                if (value > best) best = value;
            }
            return best == float.MinValue ? 0 : best;
        }

        private static float PolicyRangeDelta(
            BattleSquadCapabilityProfile profile,
            float range,
            EngagementOptionKind policy)
        {
            if (policy == EngagementOptionKind.Hold) return 0;
            float speed = profile.MoveSpeed * (policy == EngagementOptionKind.JogToward
                ? JogSpeedMultiplier
                : 1f);
            float desired = profile.IsContactSeeking ? 1f : profile.PreferredBandUpper;
            return range > desired ? -Math.Min(speed, range - desired) : 0;
        }

        private static float BaselineRangeDelta(
            BattleSquadCapabilityProfile profile,
            float range)
        {
            if (profile.IsContactSeeking) return range > 1
                ? -Math.Min(profile.MoveSpeed, range - 1)
                : 0;
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

        private static float AggregateRemovalRate(
            BattleSquadCapabilityProfile attacker,
            BattleSquadCapabilityProfile defender,
            float range)
        {
            float rangedReach = attacker.PreferredBandUpper;
            float rangedRangeFactor = rangedReach <= 0 || range > rangedReach
                ? 0
                : Math.Clamp(1f - (range / Math.Max(1, rangedReach)) * 0.35f, 0.1f, 1f);
            float ranged = attacker.UsableRangedBattleValue * 0.10f
                * rangedRangeFactor;
            float melee = range <= 1.5f
                ? attacker.UsableMeleeBattleValue * 0.13f
                : 0;
            return Math.Min(defender.TotalAbleBattleValue, Math.Max(ranged, melee));
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
            float interceptDistance = Distance(
                endpoint, BattleEngagementFrameBuilder.Centroid(threat));
            float interceptTurns = interceptDistance / Math.Max(0.1f, threatProfile.MoveSpeed);
            float holding = Math.Min(1f,
                (profile.UsableMeleeBattleValue + profile.TotalAbleBattleValue * 0.25f)
                / Math.Max(1, threatProfile.UsableMeleeBattleValue));
            float capacity = Math.Min(1f,
                profile.ContactCapacity / (float)Math.Max(1, threatProfile.ContactCapacity));
            float intercept = 1f / (1f + interceptTurns);
            return Math.Min(
                threatProfile.UsableMeleeBattleValue,
                profiles[frame.ProtectedSquadId.Value].TotalAbleBattleValue)
                * holding * capacity * intercept;
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
            if (frame.Role != EngagementSquadRole.Pursuit
                || primary == null
                || kind == EngagementOptionKind.Hold)
            {
                return 0;
            }
            ValueTuple<float, float> target = BattleEngagementFrameBuilder.Centroid(primary);
            float before = Distance(BattleEngagementFrameBuilder.Centroid(squad), target);
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
            float quarrySpeed = quarryRole is EngagementSquadRole.Bound
                or EngagementSquadRole.Routing
                    ? Math.Max(0, frame.QuarryRunSpeed)
                    : 0;
            float usefulStride = Math.Min(
                Math.Max(0, profile.MoveSpeed - quarrySpeed),
                before - desiredRange);
            float progress = Math.Min(
                usefulStride,
                Math.Max(0, feasibleSpeed - quarrySpeed));
            float attainable = profile.IsContactSeeking
                ? profile.UsableMeleeBattleValue
                : profile.UsableRangedBattleValue;
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
                    break;
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
                    BattleDecisionTrace.Field("incoming", candidate.IncomingNow),
                    BattleDecisionTrace.Field("melee", candidate.MeleeNow),
                    BattleDecisionTrace.Field("future", string.Join(',', candidate.FutureExchange.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))),
                    BattleDecisionTrace.Field("role_term", candidate.RoleTerm),
                    BattleDecisionTrace.Field("commitment", candidate.ContactCommitmentCost),
                    BattleDecisionTrace.Field("hysteresis", candidate.Hysteresis),
                    BattleDecisionTrace.Field("score", candidate.Score),
                    BattleDecisionTrace.Field("chosen", candidate.Kind == decision.Chosen.Kind),
                    BattleDecisionTrace.Field("margin", decision.Chosen.Score - runnerUp),
                    BattleDecisionTrace.Field("enemy_baseline", decision.Frame.BaselinePosture),
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
                .Where(enemy => enemy.CanFight)
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
        // after adding contact imminence so long charges no longer get both an undiscounted payoff
        // and an aggressively capped cost.
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
            int turnsToContact = moveSpeed <= 0
                ? int.MaxValue
                : (int)Math.Ceiling(Math.Max(0f, distance - 1f) / moveSpeed);
            // Quote future melee in the same present-value currency as ranged targeting. Contact
            // already made has full value; every turn spent closing discounts the payoff.
            float contactImminence = turnsToContact == int.MaxValue
                ? 0f
                : 1f / (1f + turnsToContact);
            meleeBattleValue *= contactImminence;
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
                    || !enemy.CanFight)
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
                    && pursuedTarget.CanFight
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
                _meleeWeaponTemplates,
                isCharge);
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
            return CalculateTakeOutProbabilityOnHit(
                target,
                attacker.Soldier.Strength * weapon.Template.StrengthMultiplier,
                (target.Armor?.Template.ArmorProvided ?? 0)
                    * weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier);
        }

        private void AddRangedActionToBag(BattleSoldier soldier, bool isMoving)
        {
            AddRangedActionToBag(
                soldier,
                isMoving ? FullBulkMultiplier : 0,
                isMoving ? 0 : 1);
        }

        private void AddRangedActionToBag(
            BattleSoldier soldier,
            float bulkMultiplier,
            float aimMultiplier,
            ValueTuple<int, int>? movementDirection = null)
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
                AddShootOrAimActionToBag(
                    soldier,
                    bulkMultiplier,
                    aimMultiplier,
                    movementDirection);
            }
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
                    if (enemy == null || !enemy.CanFight || !IsPlaced(enemy))
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

        private void AddShootOrAimActionToBag(
            BattleSoldier soldier,
            float bulkMultiplier,
            float aimMultiplier,
            ValueTuple<int, int>? movementDirection = null)
        {
            IReadOnlyList<BattleSoldier> candidates =
                BuildRankedRangedCandidates(soldier, movementDirection);
            TemplateFiringLineEvaluation templateLine = SelectBestTemplateFiringLine(
                soldier,
                candidates,
                movementDirection);
            // Sticky targeting (Phase 2): stay on the already-committed target when it is still a
            // worthwhile shot, falling back to the full-field scan only to re-acquire.
            RangedTargetEvaluation targetEvaluation =
                EvaluateStickyTarget(soldier, bulkMultiplier, movementDirection)
                ?? SelectBestRangedTarget(
                    soldier,
                    bulkMultiplier,
                    movementDirection: movementDirection);
            TemplateFiringLineEvaluation blastThrow = SelectBestBlastThrow(
                soldier,
                movementDirection,
                bulkMultiplier,
                candidates);
            float bestConventionalScore = Math.Max(
                templateLine?.Score ?? float.MinValue,
                targetEvaluation?.Score ?? float.MinValue);
            // SelectBestBlastThrow already requires a positive score (more enemy value
            // removed than friendly value lost, thrower included); the sidearm rule adds
            // that the throw must also beat the soldier's best conventional action.
            if (blastThrow != null
                && blastThrow.Score > bestConventionalScore + BlastOverConventionalScoreMargin)
            {
                LogGrenadeSelection(
                    soldier,
                    blastThrow,
                    targetEvaluation,
                    templateLine,
                    bestConventionalScore,
                    bulkMultiplier);
                soldier.TargetId = blastThrow.Target.Soldier.Id;
                _shootActions.Add(new BlastAttackAction(
                    soldier.Soldier.Id,
                    blastThrow.Target.Soldier.Id,
                    blastThrow.Weapon.Template.Id,
                    blastThrow.Range,
                    bulkMultiplier,
                    _grid,
                    _random));
                return;
            }

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
                // No shot available this turn: restock a spent grenade from the belt
                // (ReloadTime 1, so it is back in hand next turn). Reloading while moving
                // follows the existing mid-move reload precedent; a soldier partway
                // through another weapon's reload must not restart his phase counter.
                RangedWeapon emptyBlastWeapon = soldier.EquippedRangedWeapons
                    .Concat(soldier.RangedWeapons)
                    .FirstOrDefault(weapon => weapon.Template.IsBlastWeapon
                        && weapon.LoadedAmmo == 0);
                if (soldier.ReloadingPhase == 0 && emptyBlastWeapon != null)
                {
                    _shootActions.Add(new ReloadRangedWeaponAction(soldier, emptyBlastWeapon));
                }
                return;
            }

            BattleSoldier target = targetEvaluation.Target;
            soldier.TargetId = target.Soldier.Id;

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
            // Walking soldiers retain their aim while reaching this general ranged path. Apply the
            // same cap the stationary path applies so movement cannot let the accumulated aim pass 3.
            if (soldier.Aim is ValueTuple<int, RangedWeapon, int> existingAim
                && existingAim.Item3 >= 3
                && existingAim.Item1 == target.Soldier.Id
                && existingAim.Item2.LoadedAmmo > 0
                && soldier.EquippedRangedWeapons.Contains(existingAim.Item2)
                && range <= existingAim.Item2.Template.MaximumRange)
            {
                float forcedShotModifier = -(existingAim.Item2.Template.Bulk * bulkMultiplier)
                    + ((existingAim.Item2.Template.Accuracy + existingAim.Item3 + 1)
                        * aimMultiplier);
                RangedTargetEvaluation forcedShot = EvaluateRangedTarget(
                    soldier,
                    target,
                    existingAim.Item2,
                    range,
                    forcedShotModifier);
                _shootActions.Add(new ShootAction(
                    soldier.Soldier.Id,
                    target.Soldier.Id,
                    existingAim.Item2.Template.Id,
                    range,
                    forcedShot.ShotsToFire,
                    bulkMultiplier,
                    aimMultiplier,
                    _grid,
                    _random));
                return;
            }

            // decide whether to shoot or aim
            // calculate the expected number of hits if the soldier shoots now
            // calculate the expected number of hits if the soldier aims for a turn, then shoots
            // if aiming >= 2xshooting, aim
            RangedTargetEvaluation shootNow = GetBestWeaponForSituation(
                soldier,
                target,
                range,
                bulkMultiplier,
                useAccuracy: false,
                aimMultiplier: aimMultiplier);
            RangedTargetEvaluation aimNow = GetBestWeaponForSituation(
                soldier,
                target,
                range,
                bulkMultiplier,
                useAccuracy: true,
                aimMultiplier: aimMultiplier);
            if (shootNow != null && (aimNow == null || shootNow.HitProbability * 2 > aimNow.HitProbability))
            {
                _shootActions.Add(new ShootAction(soldier.Soldier.Id,
                    target.Soldier.Id,
                    shootNow.Weapon.Template.Id,
                    range,
                    shootNow.ShotsToFire,
                    bulkMultiplier,
                    aimMultiplier,
                    _grid,
                    _random));
            }
            else if (aimMultiplier > 0)
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

        // Phase 2 sticky targeting. Replaces the former IsExistingAimStillBest, which reran the full
        // SelectBestRangedTarget scan every turn just to confirm the aim was still globally optimal.
        // Here the aim is kept while it stays viable and worthwhile — a hysteresis band that both
        // preserves the invested aim and skips the scan.
        private bool IsExistingAimStillViable(BattleSoldier soldier)
        {
            if (soldier.Aim is not ValueTuple<int, RangedWeapon, int> aim
                || !_soldierMap.TryGetValue(aim.Item1, out BattleSoldier target)
                || !target.CanFight
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
                || !target.CanFight
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
                if (!enemy.CanFight
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
                        float takeOutProbability = CalculateRangedTakeOutProbability(
                            victim, weapon, victimRange, armor);
                        float expectedBattleValueRemoval =
                            takeOutProbability * GetBattleValue(victim);
                        if (_grid.GetSoldierSide(victimId) == shooterSide)
                        {
                            expectedFriendlyBattleValueLost += expectedBattleValueRemoval;
                        }
                        else
                        {
                            expectedEnemyBattleValueRemoved += expectedBattleValueRemoval
                                * GetSquadImminence(
                                    soldier.BattleSquad,
                                    victim.BattleSquad);
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
        /// Writes are serialized on the shared <see cref="_log"/> delegate: battle planning runs
        /// across worker threads and the sink (a List&lt;string&gt;.Add) is not thread-safe.
        /// </summary>
        private void LogGrenadeSelection(
            BattleSoldier soldier,
            TemplateFiringLineEvaluation blastThrow,
            RangedTargetEvaluation conventionalShot,
            TemplateFiringLineEvaluation conventionalTemplate,
            float bestConventionalScore,
            float bulkMultiplier)
        {
            if (_log == null) return;

            RangedWeaponTemplate weapon = blastThrow.Weapon.Template;
            float range = blastThrow.Range;
            float skill = soldier.Soldier.GetTotalSkillValue(weapon.RelatedSkill);
            float rangeModifier = BattleModifiersUtil.CalculateBlastRangeModifier(
                soldier.Soldier, weapon, range);
            float bulkPenalty = weapon.Bulk * bulkMultiplier;
            float toHit = skill + rangeModifier - bulkPenalty;
            float deliveryConfidence = GaussianCalculator.ApproximateNormalCDF(
                (toHit - BlastDeliveryRollMean) / BlastDeliveryRollStdDev);

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

            StringBuilder sb = new();
            sb.Append($"[GrenadeChoice] {soldier.Soldier.Name} throws {weapon.Name} at ")
                .Append($"{blastThrow.Target.Soldier.Name} (range {range:F1}).");
            sb.Append($" Throw score {blastThrow.Score:F2} = enemyBV ")
                .Append($"{blastThrow.ExpectedEnemyBattleValueRemoved:F2} - friendlyBV ")
                .Append($"{blastThrow.ExpectedFriendlyBattleValueLost:F2}.");
            sb.Append($" To-hit {toHit:F1} (skill {skill:F1} + range {rangeModifier:F1} - bulk ")
                .Append($"{bulkPenalty:F1}), delivery confidence {deliveryConfidence:P0}.");
            sb.Append($" Caught enemies [{string.Join(", ", caughtEnemies)}]; friendlies ")
                .Append($"[{string.Join(", ", caughtFriendlies)}].");

            if (conventionalShot != null)
            {
                sb.Append($" Alt shot: {conventionalShot.Weapon.Template.Name} at ")
                    .Append($"{conventionalShot.Target.Soldier.Name} (range {conventionalShot.Range:F1}), ")
                    .Append($"{conventionalShot.ShotsToFire} shot(s), hit {conventionalShot.HitProbability:P0}, ")
                    .Append($"takeout {conventionalShot.TakeOutProbabilityOnHit:F2}, score {conventionalShot.Score:F2} ")
                    .Append($"(enemyBV {conventionalShot.ExpectedEnemyBattleValueRemoved:F2} - friendlyBV ")
                    .Append($"{conventionalShot.ExpectedFriendlyBattleValueLost:F2}).");
            }
            else
            {
                sb.Append(" Alt shot: none.");
            }

            if (conventionalTemplate != null)
            {
                sb.Append($" Alt template: {conventionalTemplate.Weapon.Template.Name} at ")
                    .Append($"{conventionalTemplate.Target.Soldier.Name} (range {conventionalTemplate.Range:F1}), ")
                    .Append($"score {conventionalTemplate.Score:F2} ")
                    .Append($"(enemyBV {conventionalTemplate.ExpectedEnemyBattleValueRemoved:F2} - friendlyBV ")
                    .Append($"{conventionalTemplate.ExpectedFriendlyBattleValueLost:F2}).");
            }
            else
            {
                sb.Append(" Alt template: none.");
            }

            if (conventionalShot != null || conventionalTemplate != null)
            {
                sb.Append($" Best conventional {bestConventionalScore:F2}; throw wins by ")
                    .Append($"{blastThrow.Score - bestConventionalScore:F2} ")
                    .Append($"(margin threshold {BlastOverConventionalScoreMargin:F2}).");
            }
            else
            {
                sb.Append(" No conventional shot or template line available this turn.");
            }

            lock (_log)
            {
                _log(sb.ToString());
            }
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
            if (target == null || !target.CanFight || damageCoefficient <= 0
                || weaponWoundMultiplier <= 0)
            {
                return 0f;
            }

            Body body = target.Soldier.Body;
            int totalLocationWeight = body.TotalProbabilityMap[target.Stance];
            if (totalLocationWeight <= 0)
            {
                return 0f;
            }

            IReadOnlyList<int> functioningHands = target.FunctioningHandGroupIds;
            int? lastFunctioningHand = functioningHands.Count == 1
                ? functioningHands[0]
                : null;
            float probability = 0f;
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
                float requiredRoll =
                    (effectiveArmor + requiredPenetratingDamage) / damageCoefficient;
                float damageTailProbability = GaussianCalculator.ApproximateNormalCDF(
                    (DamageRollMean - requiredRoll) / DamageRollStdDev);
                probability += (locationWeight / (float)totalLocationWeight)
                    * damageTailProbability;
            }
            return Math.Clamp(probability, 0f, 1f);
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
            public readonly float Imminence;
            public readonly RangedWeapon Weapon;

            public BlastNearbySoldier(
                float offsetX,
                float offsetY,
                bool friendly,
                BattleSoldier target,
                float battleValue,
                float imminence,
                RangedWeapon weapon)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Friendly = friendly;
                Target = target;
                BattleValue = battleValue;
                Imminence = imminence;
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
        // every miss node lands the template somewhere and pays its friendly cost. Enemy value is
        // discounted per victim by squad imminence (matching the conventional ranged path); friendly
        // and self value is never discounted. Replaces the former perfect-impact-times-confidence
        // estimate. See OnlyWar_TDD.md §6.6.
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
                if (!candidate.CanFight || !IsPlaced(candidate))
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
                float imminence = friendly
                    ? 1f
                    : GetSquadImminence(soldier.BattleSquad, candidate.BattleSquad);
                nearby.Add(new BlastNearbySoldier(
                    offsetX,
                    offsetY,
                    friendly,
                    candidate,
                    GetBattleValue(candidate),
                    imminence,
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
                float takeOutProbability = CalculateTakeOutProbabilityOnHit(
                    victim.Target,
                    victim.Weapon.Template.DamageMultiplier * falloff * falloff,
                    armor * victim.Weapon.Template.ArmorMultiplier,
                    victim.Weapon.Template.WoundMultiplier);
                float removed = weight * takeOutProbability * victim.BattleValue;
                if (victim.Friendly)
                {
                    friendlyBattleValueLost += removed;
                }
                else
                {
                    enemyBattleValueRemoved += removed * victim.Imminence;
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

            ValueTuple<float, float, int> attackEstimate = EstimatePlannedRangedAttack(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                evaluatedTargetSpeed);
            float takeOutProbability = Math.Clamp(attackEstimate.Item2, 0, 1);
            float imminence = GetSquadImminence(soldier.BattleSquad, target.BattleSquad);
            float enemyBattleValueRemoved = imminence
                * attackEstimate.Item1
                * takeOutProbability
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
                    || !enemy.CanFight
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
                    float takeOutProbability = CalculateRangedTakeOutProbability(
                        participant, weapon, range, armor);
                    return victimProbability
                        * takeOutProbability
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

        internal float GetSquadImminence(BattleSquad attackerSquad, BattleSquad targetSquad)
        {
            if (attackerSquad == null || targetSquad == null) return 0;

            var cacheKey = (attackerSquad.Id, targetSquad.Id);
            if (_context.SquadImminence.TryGetValue(cacheKey, out float cached))
            {
                return cached;
            }

            float calculated = CalculateSquadImminence(attackerSquad, targetSquad);
            _context.SquadImminence[cacheKey] = calculated;
            return calculated;
        }

        private float CalculateSquadImminence(BattleSquad attackerSquad, BattleSquad targetSquad)
        {
            if (!attackerSquad.AbleSoldiers.Any(IsPlaced)
                || !targetSquad.AbleSoldiers.Any(IsPlaced)) return 0;

            float distance = _grid.GetMinimumDistanceBetweenSquads(attackerSquad, targetSquad);
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

        private ValueTuple<float, float, int> EstimatePlannedRangedAttack(
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
            float takeOutProbability = CalculateRangedTakeOutProbability(
                target, weapon, range, armor);
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
            ValueTuple<float, float> estimate = new(0,0);
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
                    return new ValueTuple<float, float, int>(
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
                takeOutProbability,
                shotsToFire);
            return new ValueTuple<float, float, int>(estimate.Item1, estimate.Item2, shotsToFire);
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

        private static ValueTuple<float, float> EstimateHitAndDamage(
            RangedHitEstimateContext hitContext,
            float expectedDamage,
            int numberOfShots)
        {
            float preRollHitTotal = hitContext.CalculatePreRollHitTotal(numberOfShots);
            float probability = GaussianCalculator.ApproximateNormalCDF(
                (preRollHitTotal - 10.5f) / 3f);
            return new ValueTuple<float, float>(probability, expectedDamage);
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
            return actualDirection.Item1 == 0 && actualDirection.Item2 == 0
                ? line
                : actualDirection;
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
    }
}
