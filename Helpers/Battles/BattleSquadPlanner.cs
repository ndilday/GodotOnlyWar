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
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
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
        // The Phase 4 lookahead's removal-rate table, memoized per shooter squad.
        private readonly PairRemovalRateTable _removalRates;
        // Per-turn exchange rates and the bounded policy lookahead behind posture choice.
        private readonly EngagementExchangeModel _exchange;
        // Melee scoring: strike plans, projected melee value, charge net, forfeited parry risk.
        private readonly MeleeStrikeEstimator _melee;
        // Melee emission: strikes, point-blank shots, charge movement, squad charge resolution.
        private readonly MeleeActionBuilder _meleeBuilder;
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
            _random = _services.Random;
            _log = _services.Log;
            _context = _services.Context;
            _shootActions = _actions.Shoot;
            _moveActions = _actions.Move;

            _ranged = new RangedTargetSelector(_services);
            _movement = new SoldierMovementPlanner(_services, _actions);
            _removalRates = new PairRemovalRateTable(_services, _ranged);
            _melee = new MeleeStrikeEstimator(_services, _ranged);
            _exchange = new EngagementExchangeModel(
                _services, _ranged, _melee, _removalRates);
            _meleeBuilder = new MeleeActionBuilder(
                _services, _actions, _ranged, _melee, _movement,
                AddPermittedRunUtilityActionToBag);
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
                squad, frame, primary, profile);
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
            BattleSquadCapabilityProfile profile)
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
            ValueTuple<int, int>? direction = GetOptionDirection(squad, kind, frame, intended);
            (float enemyRemoval, float friendlyFire, float readiness,
                IReadOnlyList<PlannedSoldierAction> rootActions) =
                EvaluateImmediateActionValue(squad, tier, direction);
            float incoming = EvaluateIncomingNow(
                squad, feasibleSpeed, profiles, allFrames, enemies);
            (float meleeNow, float commitment) = EvaluateContactTerms(
                squad, kind, primary, profile);
            float arrivalTimeValue = _exchange.EvaluateArrivalTimeValue(
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
            List<float> future = _exchange.EvaluateFutureExchange(
                squad,
                projectedCentroid,
                kind,
                profile,
                profiles,
                allFrames,
                enemies);
            float roleTerm = EvaluateScreenRoleTerm(
                kind, frame, profile, profiles, projectedCentroid, enemies);
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

        private static ValueTuple<int, int>? GetOptionDirection(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
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

        private static PlannedSoldierAction PlanRunUtilityAction(BattleSoldier soldier)
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

        // The exchange/lookahead model lives in EngagementExchangeModel; these forward the option
        // scorer's call sites.
        private float EvaluateIncomingNow(
            BattleSquad squad,
            float feasibleSpeed,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies) =>
            _exchange.EvaluateIncomingNow(squad, feasibleSpeed, profiles, frames, enemies);

        private (float MeleeNow, float Commitment) EvaluateContactTerms(
            BattleSquad squad,
            EngagementOptionKind kind,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile) =>
            _exchange.EvaluateContactTerms(squad, kind, primary, profile);

        // The Phase 4 removal-rate table lives in PairRemovalRateTable; this is the planner-facing
        // entry point the exchange-rate model and the battle tests call.
        internal IReadOnlyDictionary<int, SquadPairRemovalRate> GetPairRemovalRates(
            BattleSquad shooterSquad) =>
            _removalRates.GetPairRemovalRates(shooterSquad);


        private static float EvaluateScreenRoleTerm(
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
                BattleLog.Write(new BattleDecisionTrace("ENGAGE_EVAL",
                [
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
                ]).Render());
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

        // Melee and charge emission live in MeleeActionBuilder; these forward the planner's own
        // call sites.
        private void AddMeleeActionsToBag(BattleSoldier soldier) =>
            _meleeBuilder.AddMeleeActionsToBag(soldier);

        private IReadOnlyList<MeleeWeapon> GetProjectedMeleeLoadout(BattleSoldier soldier) =>
            _melee.GetProjectedMeleeLoadout(soldier);

        private static MeleeWeapon GetSecondaryMeleeWeapon(IReadOnlyList<MeleeWeapon> loadout) =>
            MeleeStrikeEstimator.GetSecondaryMeleeWeapon(loadout);

        private static MeleeWeapon GetFirstUsableMeleeWeapon(BattleSoldier soldier) =>
            MeleeStrikeEstimator.GetFirstUsableMeleeWeapon(soldier);

        // TEST SEAM, like the ranged forwarders above: BattleSquadPlannerTests drives parry risk
        // through a constructed planner.
        internal float EstimateProjectedMeleeBattleValue(
            BattleSoldier attacker,
            IReadOnlyList<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<MeleeWeapon> plannedWeapons,
            bool didMove = false) =>
            _melee.EstimateProjectedMeleeBattleValue(
                attacker, strikePlans, plannedWeapons, didMove);

        internal float EstimateForfeitedParryRisk(
            BattleSoldier defender,
            IReadOnlyList<BattleSoldier> adjacentAttackers,
            IReadOnlyCollection<MeleeWeapon> projectedDefensiveWeapons) =>
            _melee.EstimateForfeitedParryRisk(
                defender, adjacentAttackers, projectedDefensiveWeapons);

        private void AddChargeActionsToBag(BattleSoldier soldier) =>
            _meleeBuilder.AddChargeActionsToBag(soldier);

        private IReadOnlyList<IAction> ResolveSquadChargeIntent(
            BattleSquad chargingSquad,
            BattleSquad targetSquad,
            BattleState state) =>
            _meleeBuilder.ResolveSquadChargeIntent(chargingSquad, targetSquad, state);


        private List<PlannedMeleeStrike> BuildStrikePlan(
            BattleSoldier attacker,
            IReadOnlyList<BattleSoldier> targets,
            IReadOnlyList<MeleeWeapon> plannedWeapons,
            bool didMove) =>
            _melee.BuildStrikePlan(attacker, targets, plannedWeapons, didMove);

        private float EstimateTakeOutProbability(
            BattleSoldier attacker,
            BattleSoldier target,
            MeleeWeapon weapon,
            bool didMove) =>
            _melee.EstimateTakeOutProbability(attacker, target, weapon, didMove);

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
