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
        // Aliases onto the canonical tier speeds; see SoldierMovementPlanner.
        private const float WalkSpeedMultiplier = SoldierMovementPlanner.WalkSpeedMultiplier;
        private const float JogSpeedMultiplier = SoldierMovementPlanner.JogSpeedMultiplier;
        private const float WalkBulkMultiplier = 0.5f;
        private const float FullBulkMultiplier = 1f;
        // Length the squad rout heading is normalized to. Long enough that no rout is ever capped
        // by the line itself (CalculateMovementAlongLine treats a line shorter than the move budget
        // as a destination), short enough that its squared length stays well inside int range.
        private const int RoutLineLength = 1_000;
        private const float WalkAimMultiplier = 0.5f;
        // TUNABLE (Phase 2 sticky targeting): a soldier keeps engaging the target it already
        // committed to (soldier.TargetId / soldier.Aim) across turns rather than rescanning the whole
        // field every turn, re-acquiring only when that target stops being a viable, worthwhile shot
        // or an un-engaged enemy is about to reach melee. "Worthwhile" reuses the planner's existing
        // floor: positive expected value and better than a one-in-ten chance to hit. Raising this
        // makes soldiers abandon marginal targets (and rescan) sooner.
        private const float StickyMinimumHitProbability = 0.1f;
        // Aim bonus a pre-sprung ambusher opens with. Matches the planner's own "aim can no
        // longer be improved" ceiling (the >= 3 checks in the standing/forced-shot paths), so a
        // seeded ambusher is indistinguishable from a soldier who spent three turns lining up the
        // shot. See SeedAmbushAim and OnlyWar_TDD.md §6.6.
        private const int FullAimBonusTurns = 3;
        // A fresh stationary aim starts at bonus 0, takes four Aim actions to reach the planner's
        // full-aim threshold (3), and fires on the fifth turn. Pursuit uses this same cycle when
        // deciding how far a squad must run before it can safely stop and complete a shot.
        private const int PursuitFireWindowTurns = FullAimBonusTurns + 2;

        // The read context and the action bags, bundled so an extracted collaborator takes one
        // parameter per half rather than six loose dependencies. See SquadPlanningServices.
        private readonly SquadPlanningServices _services;
        private readonly ActionSink _actions;

        // Convenience aliases onto the two bundles above. The planner's own body reads these
        // several hundred times; extracted collaborators take the bundles instead.
        private readonly BattleGridManager _grid;
        private readonly ICollection<IAction> _shootActions;
        private readonly ICollection<IAction> _moveActions;
        private readonly ICollection<IAction> _meleeActions;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly IReadOnlyDictionary<int, MeleeWeaponTemplate> _meleeWeaponTemplates;
        private readonly IRNG _random;
        private readonly Action<string> _log;
        // Shared, frozen-state memo for the pure targeting computations below. Handed in by the
        // resolver so both per-side planners reuse each other's results; a standalone planner
        // (tests) gets its own. See BattlePlanningContext for the invariant.
        private readonly BattlePlanningContext _context;
        // Ranged target selection and shot estimation.
        private readonly RangedTargetSelector _ranged;
        // Movement placement: line + budget -> destination, orientation, and a MoveAction.
        private readonly SoldierMovementPlanner _movement;
        // Grenade scoring. Reads the same state this planner does, plus a delegate onto the
        // selector's enemy-acquisition scan.
        private readonly BlastThrowEvaluator _blast;

        // Labelling for ENGAGE_EVAL traces only, set by the resolver after construction. Nothing in
        // planning reads it, so a planner without it (tests, the ambush-seeding pass) behaves
        // identically and simply renders turn=0 side=none.
        public int TraceTurnNumber { get; set; }
        public string TraceSideLabel { get; set; }

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
                                  BattlePlanningContext context = null)
        {
            // A standalone planner (unit tests, one-off callers) gets a private context, which
            // reproduces the previous per-planner cache scope exactly.
            _services = new SquadPlanningServices(
                grid,
                soldiers,
                meleeWeaponTemplates,
                random,
                log,
                context ?? new BattlePlanningContext());
            _actions = new ActionSink(shootActions, moveActions, meleeActions);

            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
            _meleeWeaponTemplates = _services.MeleeWeaponTemplates;
            _random = _services.Random;
            _log = _services.Log;
            _context = _services.Context;
            _shootActions = _actions.Shoot;
            _moveActions = _actions.Move;
            _meleeActions = _actions.Melee;

            _ranged = new RangedTargetSelector(_services);
            _movement = new SoldierMovementPlanner(_services, _actions);
            _blast = new BlastThrowEvaluator(
                _services,
                (soldier, range, movementDirection) =>
                    _ranged.GetNearestEnemySquadsWithinRange(soldier, range, movementDirection)
                        .SelectMany(candidateSquad => candidateSquad.AbleSoldiers));
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
                RangedTargetEvaluation evaluation =
                    _ranged.SelectBestRangedTarget(soldier, bulkMultiplier: 0f);
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
            // CONTRACT (Phase 5, Design/Reference/EngagementScoringOverhaul.md). `enemyRemoval` and
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
                && _ranged.IsExistingAimStillViable(soldier))
            {
                float stickyRange = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id, stickyTarget.Soldier.Id);
                RangedTargetEvaluation stickyShot = _ranged.EvaluateRangedTarget(
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
            IReadOnlyList<BattleSoldier> candidates = _ranged.BuildRankedRangedCandidates(
                soldier, movementDirection);
            TemplateFiringLineEvaluation template = _ranged.SelectBestTemplateFiringLine(
                soldier, candidates, movementDirection);
            RangedTargetEvaluation targetEvaluation = _ranged.EvaluateStickyTarget(
                    soldier, bulkMultiplier, movementDirection)
                ?? _ranged.SelectBestRangedTarget(
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
                && blast.Score > bestConventional
                    + BlastThrowEvaluator.OverConventionalScoreMargin)
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
                    Diagnostic: _blast.FormatGrenadeSelection(
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
                    _ranged.EvaluateRangedTarget(
                        soldier, target, existingAim.Item2, range, modifier),
                    bulkMultiplier,
                    aimMultiplier);
            }

            RangedTargetEvaluation shootNow = _ranged.GetBestWeaponForSituation(
                soldier,
                target,
                range,
                bulkMultiplier,
                useAccuracy: false,
                aimMultiplier: aimMultiplier);
            // A moving candidate cannot aim.  Excluding that illegal alternative, rather than
            // comparing against it and later doing nothing, is the key plan/execution invariant.
            RangedTargetEvaluation aimNow = aimMultiplier > 0
                ? _ranged.GetBestWeaponForSituation(
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
                        RangedTargetEvaluation evaluation = _ranged.EvaluateRangedTarget(
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
                // PHASE 5d (Design/Reference/EngagementScoringOverhaul.md). The terminal used to be
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
        // (Design/Reference/EngagementScoringOverhaul.md): `desired` is the effectiveness-derived
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
        // Phase 1, Design/Reference/EngagementScoringOverhaul.md.
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
        /// PHASE 5c (Design/Reference/EngagementScoringOverhaul.md). One ply's net battle-value
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
        /// Phase 4 removal-rate table (Design/Reference/EngagementScoringOverhaul.md). Returns, for
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
                RangedTargetEvaluation evaluation = _ranged.SelectBestRangedTarget(
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
                        victim, armor * template.ArmorMultiplier, template.WoundMultiplier)
                    : null;
            (float takeOut, float progress) = RangedTargetSelector.CalculateRangedHitRemoval(
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

        /// <summary>
        /// Stands in for "this weapon does not roll to hit". Far enough above
        /// <see cref="RemovalMath.HitRollMean"/> that the burst model's first-shot threshold is met with
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
                    ? RemovalMath.BuildTakeOutLocationTerms(
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
            // charge-arrival discount above -- see Design/Reference/EngagementScoringOverhaul.md
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
                && _ranged.IsExistingAimStillViable(soldier));
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
                        RangedTargetEvaluation evaluation = _ranged.EvaluateRangedTarget(
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

        private static float GetTierSpeed(BattleSoldier soldier, SquadMovementTier tier) =>
            SoldierMovementPlanner.GetTierSpeed(soldier, tier);

        private static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier) =>
            SoldierMovementPlanner.GetMovementBudget(soldier, tier);

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
            TemplateFiringLineEvaluation pointBlankTemplate = _ranged.SelectBestTemplateFiringLine(
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
            RangedWeapon sidearm = RangedTargetSelector
                .OrderRangedByTemplateId(soldier.EquippedRangedWeapons)
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
            RangedTargetEvaluation sidearmShot = _ranged.EvaluateRangedTarget(
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
                RangedTargetSelector.OrderRangedByTemplateId(soldier.EquippedRangedWeapons);
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

                    RangedTargetEvaluation evaluation = _ranged.EvaluateRangedTarget(
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
            return RemovalMath.CalculateRemovalFractionOnHit(
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
        // first. Capped at RangedTargetSelector.RangedCandidateEvaluationCount to keep the
        // template/blast scans bounded.
        // TEST SEAM. The planner's own paths call _ranged directly; these three remain because the
        // battle test fixtures drive ranged scoring through a constructed planner. Delete them when
        // those tests are repointed at RangedTargetSelector.
        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            bool useBulk,
            bool includeExistingAim = false,
            ValueTuple<int, int>? movementDirection = null) =>
            _ranged.SelectBestRangedTarget(
                soldier, useBulk, includeExistingAim, movementDirection);

        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            float bulkMultiplier,
            bool includeExistingAim = false,
            ValueTuple<int, int>? movementDirection = null) =>
            _ranged.SelectBestRangedTarget(
                soldier, bulkMultiplier, includeExistingAim, movementDirection);

        internal TemplateFiringLineEvaluation SelectBestTemplateFiringLine(
            BattleSoldier soldier,
            IEnumerable<BattleSoldier> candidateTargets = null,
            ValueTuple<int, int>? movementDirection = null) =>
            _ranged.SelectBestTemplateFiringLine(
                soldier, candidateTargets, movementDirection);


        /// <summary>
        /// Grenade scoring lives in <see cref="BlastThrowEvaluator"/>; this is the planner-facing
        /// entry point the action-planning path and the grenade tests call.
        /// </summary>
        internal TemplateFiringLineEvaluation SelectBestBlastThrow(
            BattleSoldier soldier,
            ValueTuple<int, int>? movementDirection = null,
            float bulkMultiplier = 0,
            IReadOnlyList<BattleSoldier> candidateTargets = null) =>
            _blast.SelectBestThrow(
                soldier, movementDirection, bulkMultiplier, candidateTargets);

        // Ranged scoring lives in RangedTargetSelector; these are the planner-facing entry points
        // its own planning paths (and the battle tests) call.
        internal RangedTargetEvaluation EvaluateRangedTarget(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            float? targetSpeed = null) =>
            _ranged.EvaluateRangedTarget(
                soldier, target, weapon, range, additionalToHitModifier, targetSpeed);

        // Both of these now live on SquadPlanningServices so every collaborator shares one
        // definition; these forwarders keep the planner's own call sites unchanged.
        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);

        // Movement placement lives in SoldierMovementPlanner; these forward the planner's own call
        // sites, which pass through here on every posture that moves.
        private ValueTuple<int, int> AddMoveAction(
            BattleSoldier soldier,
            float moveSpeed,
            ValueTuple<int, int> line,
            SquadMovementTier? tier = null) =>
            _movement.AddMoveAction(soldier, moveSpeed, line, tier);

        private ValueTuple<int, int> CalculateMovementAlongLine(
            ValueTuple<int, int> line,
            float moveSpeed) =>
            _movement.CalculateMovementAlongLine(line, moveSpeed);

        private ushort CalculateOrientationFromVector(
            ValueTuple<int, int> vector,
            BattleSoldier soldier = null,
            SquadMovementTier tier = SquadMovementTier.Stationary) =>
            _movement.CalculateOrientationFromVector(vector, soldier, tier);

        private ValueTuple<int, int> FindBestLocation(
            BattleSoldier soldier,
            ValueTuple<int, int> startingPoint,
            ValueTuple<int, int> targetPoint,
            float speed,
            ushort orientation,
            BattleGridManager grid = null) =>
            _movement.FindBestLocation(
                soldier, startingPoint, targetPoint, speed, orientation, grid);

    }
}
