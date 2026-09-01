using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
        private const float WalkBulkMultiplier = SoldierMovementPlanner.WalkBulkMultiplier;
        private const float FullBulkMultiplier = SoldierMovementPlanner.FullBulkMultiplier;
        // Length the squad rout heading is normalized to. Long enough that no rout is ever capped
        // by the line itself (CalculateMovementAlongLine treats a line shorter than the move budget
        // as a destination), short enough that its squared length stays well inside int range.
        private const int RoutLineLength = 1_000;
        private const float WalkAimMultiplier = 0.5f;
        // Aim bonus a pre-sprung ambusher opens with. Matches the planner's own "aim can no
        // longer be improved" ceiling (the >= 3 checks in the standing/forced-shot paths), so a
        // seeded ambusher is indistinguishable from a soldier who spent three turns lining up the
        // shot. See SeedAmbushAim and OnlyWar_TDD.md §6.6.
        private const int FullAimBonusTurns = RangedTargetSelector.FullAimBonusTurns;
        // A fresh stationary aim starts at bonus 0, takes four Aim actions to reach the planner's
        // full-aim threshold (3), and fires on the fifth turn. Pursuit uses this same cycle when
        // deciding how far a squad must run before it can safely stop and complete a shot.

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
        private readonly BaseSkill _tacticsSkill;
        // Ranged target selection and shot estimation.
        private readonly RangedTargetSelector _ranged;
        // Movement placement: line + budget -> destination, orientation, and a MoveAction.
        private readonly SoldierMovementPlanner _movement;
        // The Phase 4 lookahead's removal-rate table, memoized per shooter squad.
        private readonly PairRemovalRateTable _removalRates;
        // Per-turn exchange rates and current-turn contact accounting behind posture choice.
        private readonly EngagementExchangeModel _exchange;
        // State-only Φ behind the posture score. Unlike the option evaluator, this collaborator
        // never receives an EngagementOptionKind; candidate-specific values arrive as projected
        // state (endpoint plus pure action descriptors).
        private readonly EngagementPotential _potential;
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

        // Whether SetEngagementHorizon has run for this planning turn. Test visibility only.
        // A false here means every squad reads ExpectedExchangeTurnsFor's dictionary-miss
        // default -- MaximumExchangeTurns -- so the derived horizon is not being exercised at
        // all and any posture measured under it is measuring the fallback.
        internal bool EngagementHorizonInitialized => _context.EngagementHorizonInitialized;

        // The horizon one squad actually received. Test visibility only.
        internal float ExpectedExchangeTurnsFor(int squadId) =>
            _context.ExpectedExchangeTurnsFor(squadId);

        internal EngagementPotential.Breakdown EvaluatePotential(
            EngagementPotential.State state) =>
            _potential.Evaluate(state);

        public BattleSquadPlanner(BattleGridManager grid,
                                  IReadOnlyDictionary<int, BattleSoldier> soldiers,
                                  ICollection<IAction> shootActions,
                                  ICollection<IAction> moveActions,
                                  ICollection<IAction> meleeActions,
                                  Action<string> log,
                                  IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates,
                                  IRNG random,
                                  BattlePlanningContext context = null,
                                  BaseSkill tacticsSkill = null)
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
            _tacticsSkill = tacticsSkill;
            _shootActions = _actions.Shoot;
            _moveActions = _actions.Move;

            _ranged = new RangedTargetSelector(_services);
            _movement = new SoldierMovementPlanner(_services, _actions);
            _removalRates = new PairRemovalRateTable(_services, _ranged);
            _melee = new MeleeStrikeEstimator(_services, _ranged);
            _exchange = new EngagementExchangeModel(
                _services, _ranged, _melee, _removalRates);
            _potential = new EngagementPotential(
                _grid,
                _ranged,
                _exchange,
                _tacticsSkill,
                _context);
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
            bool creditAimingXp = squad.Faction?.IsPlayerFaction == true;
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

        // Retained as a shared planning horizon for BattleEscapeRules' retargeting policy. The
        // engagement score itself now uses EngagementPotential rather than a policy rollout.
        internal const int EngagementLookaheadHorizon =
            EngagementExchangeModel.EngagementLookaheadHorizon;
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
            EnsureEngagementHorizon(profiles, allFrames);
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
                    null, 0, 0, 0, 0, 0, 0, 0, [], 0, 0, 0, 0);
            return new SquadEngagementDecision(
                squad,
                frame,
                chosen,
                evaluations,
                roleTargets);
        }

        /// <summary>
        /// Freezes the force-level exchange horizon for this planning turn, from the resolver,
        /// BEFORE the parallel squad-decision pass starts.
        ///
        /// <para>WHY THE RESOLVER CALLS THIS. <see cref="ChooseEngagementOption"/> begins with
        /// <see cref="EnsureEngagementHorizon"/>, so when it was only ever reached from inside the
        /// worker body every worker piled onto <c>EngagementHorizonGate</c> at once: one computed
        /// the horizon and the rest blocked for its full duration. In an instrumented seed-1
        /// generation that was 8,614 <c>Monitor.Enter</c> calls -- about one per worker per
        /// planning pass -- for 603 seconds of blocked worker time, 23% of all thread time in the
        /// profile. Doing it here means the gate is already satisfied when the workers start and
        /// none of them ever contends for it.</para>
        ///
        /// <para>The horizon is force-level, not side-level: it walks every active squad on both
        /// sides, so either side's planner computes the identical value and the resolver need only
        /// call one of them.</para>
        /// </summary>
        internal void InitializeEngagementHorizon(
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            int maxDegreeOfParallelism)
        {
            EnsureEngagementHorizon(profiles, frames, maxDegreeOfParallelism);
        }

        /// <summary>
        /// Freezes the force-level exchange horizon once per planning turn. The value is computed
        /// from the turn-start geometry and shared by every squad's state potential, so changing a
        /// candidate option cannot change the horizon used to score that option.
        ///
        /// <para>The double-checked gate remains for the standalone path -- a planner constructed
        /// directly, as the tests do, still has to initialize its own horizon lazily. In a resolver
        /// pass <see cref="InitializeEngagementHorizon"/> has already run and every call here takes
        /// the volatile fast path.</para>
        /// </summary>
        private void EnsureEngagementHorizon(
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            int maxDegreeOfParallelism = 1)
        {
            if (_context.EngagementHorizonInitialized)
            {
                return;
            }

            lock (_context.EngagementHorizonGate)
            {
                if (_context.EngagementHorizonInitialized)
                {
                    return;
                }

                List<BattleSquad> active = _soldierMap.Values
                    .Select(soldier => soldier.BattleSquad)
                    .Where(candidate => candidate != null
                        && candidate.Status == BattleSquadStatus.Active
                        && candidate.AbleSoldiers.Any(IsPlaced))
                    .DistinctBy(candidate => candidate.Id)
                    .OrderBy(candidate => candidate.Id)
                    .ToList();
                Dictionary<int, bool> sideBySquad = [];
                foreach (BattleSquad candidate in active)
                {
                    BattleSoldier anchor = candidate.AbleSoldiers.First(IsPlaced);
                    sideBySquad[candidate.Id] = _grid.GetSoldierSide(anchor.Soldier.Id);
                }

                Dictionary<bool, List<BattleSquad>> sides = active
                    .GroupBy(candidate => sideBySquad[candidate.Id])
                    .ToDictionary(group => group.Key, group => group.ToList());
                Dictionary<int, float> expectedExchangeTurnsBySquad = [];
                float totalBattleValueAtRisk = 0;
                float totalRemovalRate = 0;
                foreach ((bool side, List<BattleSquad> attackers) in sides)
                {
                    List<BattleSquad> targets = sides
                        .Where(entry => entry.Key != side)
                        .SelectMany(entry => entry.Value)
                        .OrderBy(candidate => candidate.Id)
                        .ToList();
                    float battleValueAtRisk = targets.Sum(candidate =>
                        profiles.TryGetValue(
                            candidate.Id,
                            out BattleSquadCapabilityProfile candidateProfile)
                                ? candidateProfile.TotalAbleBattleValue
                                : candidate.AbleSoldiers.Sum(GetBattleValue));
                    // The dominant cost of the whole horizon: |attackers| x |targets| exchange-rate
                    // evaluations, each of which is a full pass over the removal math. Every
                    // attacker is independent -- EvaluateOutgoingExchangeRate reads frozen
                    // turn-start state and writes only BattlePlanningContext's concurrent memos,
                    // which the squad-decision pass below already drives from many workers -- so
                    // the attackers fan out.
                    //
                    // Each attacker's rate lands in its own slot and the slots are summed in
                    // attacker order afterwards, rather than being accumulated across threads. The
                    // result is therefore identical run to run, which is what the planner's
                    // determinism rests on. It is NOT bit-identical to the old single running
                    // total, which folded one attacker's target loop into the next attacker's, so
                    // seeded battles diverge from this commit.
                    float[] attackerRates = new float[attackers.Count];
                    void AccumulateAttackerRate(int attackerIndex)
                    {
                        BattleSquad attacker = attackers[attackerIndex];
                        if (!frames.TryGetValue(
                                attacker.Id,
                                out SquadEngagementFrame attackerFrame)
                            // A pursuit turn is the no-exchange part of the chase. Its separate
                            // fire-window potential is already priced elsewhere, so it must not
                            // inflate the horizon that scales the ordinary exchange rate.
                            || attackerFrame.Role is EngagementSquadRole.Pursuit
                                or EngagementSquadRole.Follow
                                or EngagementSquadRole.Press)
                        {
                            return;
                        }

                        if (!profiles.TryGetValue(
                                attacker.Id,
                                out BattleSquadCapabilityProfile attackerProfile))
                        {
                            return;
                        }

                        float attackerRate = 0;
                        foreach (BattleSquad target in targets)
                        {
                            if (!profiles.TryGetValue(
                                    target.Id,
                                    out BattleSquadCapabilityProfile targetProfile)
                                || !frames.ContainsKey(target.Id))
                            {
                                continue;
                            }

                            float range = EngagementExchangeModel.Distance(
                                BattleEngagementFrameBuilder.Centroid(attacker),
                                BattleEngagementFrameBuilder.Centroid(target));
                            attackerRate += Math.Max(
                                0,
                                _exchange.EvaluateOutgoingExchangeRate(
                                    attacker,
                                    target,
                                    attackerProfile,
                                    targetProfile,
                                    frames,
                                    range));
                        }
                        attackerRates[attackerIndex] = attackerRate;
                    }

                    if (maxDegreeOfParallelism <= 1 || attackers.Count <= 1)
                    {
                        for (int index = 0; index < attackers.Count; index++)
                        {
                            AccumulateAttackerRate(index);
                        }
                    }
                    else
                    {
                        Parallel.For(
                            0,
                            attackers.Count,
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism = maxDegreeOfParallelism
                            },
                            AccumulateAttackerRate);
                    }

                    float currentRemovalRate = 0;
                    for (int index = 0; index < attackerRates.Length; index++)
                    {
                        currentRemovalRate += attackerRates[index];
                    }

                    float expectedExchangeTurns =
                        EngagementHorizonModel.DeriveExpectedExchangeTurns(
                            battleValueAtRisk,
                            currentRemovalRate);
                    foreach (BattleSquad attacker in attackers)
                    {
                        expectedExchangeTurnsBySquad[attacker.Id] = expectedExchangeTurns;
                    }
                    totalBattleValueAtRisk += battleValueAtRisk;
                    totalRemovalRate += currentRemovalRate;
                }

                _context.SetEngagementHorizon(
                    expectedExchangeTurnsBySquad,
                    totalBattleValueAtRisk,
                    totalRemovalRate);
            }
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
        /// Design/Reference/BattleLogic.md.</para>
        /// </summary>
        private static bool HasNoViableRangedOption(BattleSquadCapabilityProfile profile) =>
            profile.IsContactSeeking
                && (profile.UsableRangedBattleValue <= 0
                    || profile.EffectiveEngagementRange <= 0
                    || profile.PeakRangedRemovalFraction
                        < ContactSeekerRangedRelevanceFraction);

        private static bool IsPursuitRole(EngagementSquadRole role) =>
            role is EngagementSquadRole.Pursuit
                or EngagementSquadRole.Follow
                or EngagementSquadRole.Press;

        private static EngagementOptionKind FastApproachOption(BattleSquad squad) =>
            squad.CanRun
                ? EngagementOptionKind.RunToward
                : EngagementOptionKind.JogToward;

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
                    ? [FastApproachOption(squad)]
                    : [EngagementOptionKind.Hold];
            }
            if (frame.Role == EngagementSquadRole.Routing)
            {
                return [FastApproachOption(squad)];
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
            if (frame.Role == EngagementSquadRole.Follow)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                bool contactSeekerMustClose = profile.IsContactSeeking
                    && (HasNoViableRangedOption(profile)
                        || distance > profile.PreferredBandUpper);
                if (contactSeekerMustClose)
                {
                    return distance <= profile.MoveSpeed + BattleContactRules.MeleeContactAllowance
                        ? [EngagementOptionKind.CloseToContact]
                        : [FastApproachOption(squad)];
                }
                if (HasPursuitAimCommitment(squad, frame, primary))
                {
                    return [EngagementOptionKind.Hold];
                }
                // Follow normally preserves the option to fire, but it may also choose a full
                // run when the projected gain from closing outweighs the moving shot it gives up.
                // RunToward is intentionally scored here rather than treated as Press doctrine:
                // this keeps the choice between hold, low-accuracy moving fire, and a faster
                // approach in the same value comparison.
                return squad.CanRun
                    ? [
                        EngagementOptionKind.Hold,
                        EngagementOptionKind.JogToward,
                        EngagementOptionKind.RunToward
                    ]
                    : [EngagementOptionKind.Hold, EngagementOptionKind.JogToward];
            }
            if (frame.Role == EngagementSquadRole.Press)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                return distance <= profile.MoveSpeed + BattleContactRules.MeleeContactAllowance
                    ? [EngagementOptionKind.CloseToContact]
                    : [FastApproachOption(squad)];
            }
            if (frame.Role == EngagementSquadRole.Pursuit)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                bool contactSeekerMustClose = profile.IsContactSeeking
                    && (HasNoViableRangedOption(profile)
                        || distance > profile.PreferredBandUpper);
                if (contactSeekerMustClose)
                {
                    // Contact-seeker doctrine is a mask, including for pursuit frames. Apply it
                    // before any aimed-fire stickiness so a useless sidearm cannot freeze a squad.
                    return distance <= profile.MoveSpeed + BattleContactRules.MeleeContactAllowance
                        ? [EngagementOptionKind.CloseToContact]
                        : [FastApproachOption(squad)];
                }
                // Pursuit stickiness is a policy commitment, not a price for having an aim. The
                // latter is carried by the state potential so non-pursuit squads may trade it
                // against geometry and immediate exchange value.
                if (HasPursuitAimCommitment(squad, frame, primary))
                {
                    return [EngagementOptionKind.Hold];
                }
                EngagementOptionKind fast = distance <= profile.MoveSpeed
                        + BattleContactRules.MeleeContactAllowance
                    ? EngagementOptionKind.CloseToContact
                    : FastApproachOption(squad);
                return new[] { EngagementOptionKind.Hold, EngagementOptionKind.JogToward, fast }
                    .Distinct()
                    .ToList();
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
            bool hasRangedWeapon = squad.AbleSoldiers.Any(
                soldier => soldier.RangedWeapons.Count > 0);
            if (noViableRangedOption
                && hasRangedWeapon
                && primaryDistance > profile.MoveSpeed
                    + BattleContactRules.MeleeContactAllowance)
            {
                // A contact-seeker with no useful ranged answer has one doctrine: close as fast
                // as the movement rules permit. Slower approach policies are not value choices in
                // this state; exposing them lets a zero-rate exchange tie resolve to a walk/jog
                // baseline even though every yard covered is the squad's only contribution.
                result = [FastApproachOption(squad)];
            }
            if (!profile.IsContactSeeking
                && primaryDistance > profile.MoveSpeed + BattleContactRules.MeleeContactAllowance)
            {
                // CloseToContact is a charge legality choice, not a long-range approach alias.
                // Ranged squads keep RunToward/JogToward for movement toward their useful band;
                // only a contact-reachable state may expose the charge option.
                result.Remove(EngagementOptionKind.CloseToContact);
                result.Add(FastApproachOption(squad));
            }
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
                    result.Add(FastApproachOption(squad));
                }
            }
            if (frame.InterposePoint.HasValue)
            {
                result.Add(EngagementOptionKind.MoveToInterpose);
            }
            return result.Distinct().ToList();
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
            (float meleeNow, float contactCommitment) = EvaluateContactTerms(
                squad, kind, primary, profile);
            IReadOnlyCollection<BattleSquad> friendlySquads = GetFriendlySquads(squad);
            EngagementPotential.Breakdown rootPotential = _potential.Evaluate(
                new EngagementPotential.State(
                    squad,
                    BattleEngagementFrameBuilder.Centroid(squad),
                    profile,
                    profiles,
                    allFrames,
                    enemies,
                    frame,
                    0,
                    primary,
                    null,
                    friendlySquads));
            EngagementPotential.Breakdown projectedPotential = _potential.Evaluate(
                new EngagementPotential.State(
                    squad,
                    projectedCentroid,
                    profile,
                    profiles,
                    allFrames,
                    enemies,
                    frame,
                    feasibleSpeed,
                    primary,
                    rootActions,
                    friendlySquads));
            // The net-rate portion is split across the legacy trace columns so the trace remains
            // comparable: FutureExchange is γΦ_net(s') and ArrivalTimeValue is -Φ_net(s). The
            // readiness, screen and access columns carry their own potential differences. Together
            // these columns are exactly γΦ(s') - Φ(s), not independent option bonuses.
            float discountedNetRate =
                EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.NetRateValue;
            float arrivalTimeValue = -rootPotential.NetRateValue;
            readiness = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.ReadinessValue
                - rootPotential.ReadinessValue;
            float roleTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.RoleValue
                - rootPotential.RoleValue;
            float fireWindowValue = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.FireWindowValue
                - rootPotential.FireWindowValue;
            float moraleTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.MoraleValue
                - rootPotential.MoraleValue;
            float commandTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.CommandValue
                - rootPotential.CommandValue;
            float accessTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.AccessValue
                - rootPotential.AccessValue;
            List<float> future = [discountedNetRate];
            // Phase 3: every value term is now state potential. The only remaining direct
            // commitment cost is the current-turn contact exchange returned by EvaluateContactTerms.
            float score = EngagementPotential.ScoreTransition(
                enemyRemoval - friendlyFire - incoming + meleeNow,
                rootPotential,
                projectedPotential,
                contactCommitment);
            return new EngagementOptionEvaluation(
                kind, tier, intended, feasibleSpeed,
                enemyRemoval, friendlyFire, readiness, fireWindowValue, incoming, meleeNow,
                future, arrivalTimeValue, roleTerm, contactCommitment, score, rootActions,
                moraleTerm, commandTerm, accessTerm);
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
                            : squad.CanRun
                                ? SquadMovementTier.Run
                                : SquadMovementTier.Jog,
                EngagementOptionKind.RunToward => squad.CanRun
                    ? SquadMovementTier.Run
                    : SquadMovementTier.Jog,
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
            return squad.CanRun ? SquadMovementTier.Run : SquadMovementTier.Jog;
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
            if (!IsPursuitRole(frame.Role)
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
            if (equipped.CanReload && (equipped.ReloadProgress > 0 || equipped.LoadedAmmo == 0))
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
                        ExpectedEnemyBattleValueRemoved:
                            stickyShot.ExpectedEnemyBattleValueRemoved,
                        ReadinessValue: EngagementPotential.ReadinessForPreparedShot(
                            soldier,
                            stickyShot));
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
                return emptyBlast != null && emptyBlast.CanReload && emptyBlast.ReloadProgress == 0
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
                        ExpectedEnemyBattleValueRemoved: aimNow?.ExpectedEnemyBattleValueRemoved ?? 0,
                        ReadinessValue: EngagementPotential.ReadinessForPreparedShot(
                            soldier,
                            aimNow));
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
            RangedWeapon equipped = soldier.EquippedRangedWeapons[0];
            RangedWeapon weapon = equipped.CanReload
                && (equipped.ReloadProgress > 0 || equipped.LoadedAmmo == 0)
                    ? equipped
                    : soldier.RangedWeapons.FirstOrDefault(candidate =>
                        candidate.Template.IsBlastWeapon && candidate.CanReload && candidate.LoadedAmmo == 0);
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

        private bool HasPursuitAimCommitment(
            BattleSquad squad,
            SquadEngagementFrame frame,
            BattleSquad primary)
        {
            if (!IsPursuitRole(frame.Role)
                || squad.LastEngagementOptionKind != EngagementOptionKind.Hold
                || primary == null)
            {
                return false;
            }

            // Any viable aim is sticky only after the pursuit policy selected Hold. Re-check
            // viability so a dead, out-of-range, or otherwise invalid target releases the hold.
            return squad.AbleSoldiers.Any(soldier =>
                soldier.Aim is ValueTuple<int, RangedWeapon, int> aim
                && _soldierMap.TryGetValue(aim.Item1, out BattleSoldier target)
                && target.BattleSquad?.Id == primary.Id
                && _ranged.IsExistingAimStillViable(soldier));
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
                SquadMovementTier.Run when squad.CanRun => 1f,
                SquadMovementTier.Run => JogSpeedMultiplier,
                SquadMovementTier.InMelee => 1f,
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
            SquadMovementTier movementTier = decision.Chosen.Tier == SquadMovementTier.Run
                && !squad.CanRun
                    ? SquadMovementTier.Jog
                    : decision.Chosen.Tier;
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
                    GetMovementBudget(soldier, movementTier),
                    line,
                    movementTier);
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
                    BattleDecisionTrace.Field("access_potential", candidate.AccessPotentialValue),
                    BattleDecisionTrace.Field("morale_potential", candidate.MoralePotentialValue),
                    BattleDecisionTrace.Field("command_potential", candidate.CommandPotentialValue),
                    BattleDecisionTrace.Field("commitment", candidate.ContactCommitmentCost),
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
            SquadMovementTier movementTier = squad.MovementTier;
            ValueTuple<int, int> direction = BattleForcePlanner.GetHeadingVector(withdrawalHeading);
            ValueTuple<int, int> movementLine = new(direction.Item1 * 10_000, direction.Item2 * 10_000);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                // A bound soldier caught in melee decides for himself whether to break contact.
                // Running is not free: he turns his back, so he defends with foot speed alone
                // (BattleSoldier.IsRunning). Withdrawal is an ordered movement, not a rout, so
                // unlike PrepareRoutingActions he is allowed the choice rather than pinned.
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id)
                    && _melee.DecideMeleeDisengagement(soldier).Choice
                        == MeleeDisengagementChoice.StandAndFight)
                {
                    AddMeleeActionsToBag(soldier);
                    continue;
                }

                AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, movementTier),
                    movementLine,
                    movementTier);
                AddPermittedRunUtilityActionToBag(soldier);
            }
        }

        /// <summary>
        /// Scores the melee a withdrawing soldier has been caught in, against the most dangerous
        /// enemy currently in contact. Both sides' chances are measured with the same
        /// <see cref="MeleeAttackAction.EstimateHitProbability"/> the live roll uses, so the
        /// decision cannot drift from the resolution it is predicting.
        /// </summary>
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
            SquadMovementTier movementTier = squad.MovementTier;
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
                    GetMovementBudget(soldier, movementTier),
                    routLine.Value,
                    movementTier);
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
            if (squad.MovementTier == SquadMovementTier.Run && !squad.CanRun)
            {
                squad.MovementTier = SquadMovementTier.Jog;
            }
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                // Only the Run tier strips a soldier's melee guard (see BattleSoldier.IsRunning).
                // A soldier who subsequently stops to fight clears the flag in
                // AddMeleeActionsToBag, so the declaration here is a default, not a verdict.
                soldier.IsRunning = squad.MovementTier == SquadMovementTier.Run
                    && soldier.CanRun;
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
            else if (soldier.EquippedRangedWeapons[0].CanReload
                && (soldier.EquippedRangedWeapons[0].ReloadProgress > 0
                    || soldier.EquippedRangedWeapons[0].LoadedAmmo == 0))
            {
                AddReloadRangedWeaponActionToBag(soldier);
            }
            else
            {
                RangedWeapon emptyBlastWeapon = soldier.RangedWeapons
                    .FirstOrDefault(weapon => weapon.Template.IsBlastWeapon
                        && weapon.LoadedAmmo == 0);
                if (emptyBlastWeapon != null && emptyBlastWeapon.CanReload
                    && emptyBlastWeapon.ReloadProgress == 0)
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

        private static MeleeWeapon GetFirstUsableMeleeWeapon(BattleSoldier soldier) =>
            MeleeStrikeEstimator.GetFirstUsableMeleeWeapon(soldier);

        // TEST SEAM, like the ranged forwarders above: BattleSquadPlannerTests drives forfeited
        // parry risk through a constructed planner. It is the only melee scorer the tests reach
        // for; everything else on MeleeStrikeEstimator is called through _melee directly.
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

        private IReadOnlyCollection<BattleSquad> GetFriendlySquads(BattleSquad squad)
        {
            BattleSoldier anchor = squad?.AbleSoldiers.FirstOrDefault(IsPlaced);
            if (anchor == null)
            {
                return squad == null ? [] : [squad];
            }

            bool side = _grid.GetSoldierSide(anchor.Soldier.Id);
            return _soldierMap.Values
                .Select(soldier => soldier.BattleSquad)
                .Where(candidate => candidate != null
                    && candidate.AbleSoldiers.Any(member =>
                        IsPlaced(member)
                        && _grid.GetSoldierSide(member.Soldier.Id) == side))
                .DistinctBy(candidate => candidate.Id)
                .OrderBy(candidate => candidate.Id)
                .ToList();
        }

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
