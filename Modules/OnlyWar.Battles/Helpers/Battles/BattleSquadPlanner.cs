using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquadPlanner
    {
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

        // Live state aliases used by the public compatibility entry points.
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        // Shared, frozen-state memo for the pure targeting computations below. Handed in by the
        // resolver so both per-side planners reuse each other's results; a standalone planner
        // (tests) gets its own. See BattlePlanningContext for the invariant.
        private readonly BattlePlanningContext _context;
        // Ranged target selection and shot estimation.
        private readonly RangedTargetSelector _ranged;
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
        // Grenade scoring. Reads the same state this planner does, plus a delegate onto the
        // selector's enemy-acquisition scan.
        private readonly BlastThrowEvaluator _blast;
        private readonly SquadEngagementPolicy _engagementPolicy;
        internal SquadEngagementPolicy EngagementPolicy => _engagementPolicy;
        private readonly SquadActionBuilder _actionBuilder;

        // Labelling for ENGAGE_EVAL traces only, set by the resolver after construction. Nothing in
        // planning reads it, so a planner without it (tests, the ambush-seeding pass) behaves
        // identically and simply renders turn=0 side=none.
        private int _traceTurnNumber;
        private string _traceSideLabel;

        public int TraceTurnNumber
        {
            get => _traceTurnNumber;
            set
            {
                _traceTurnNumber = value;
                if (_engagementPolicy != null) _engagementPolicy.TraceTurnNumber = value;
            }
        }

        public string TraceSideLabel
        {
            get => _traceSideLabel;
            set
            {
                _traceSideLabel = value;
                if (_engagementPolicy != null) _engagementPolicy.TraceSideLabel = value;
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

        // Compatibility seam for the existing removal-rate tests and callers. The table itself
        // remains the owner of the memoized pair calculations.
        internal IReadOnlyDictionary<int, SquadPairRemovalRate> GetPairRemovalRates(
            BattleSquad shooterSquad) =>
            _removalRates.GetPairRemovalRates(shooterSquad);

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
                log,
                context ?? new BattlePlanningContext());
            ActionSink actions = new(shootActions, moveActions, meleeActions);

            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
            _context = _services.Context;

            RangedTargetingServices targeting = new(_services);
            _ranged = new RangedTargetSelector(targeting);
            SoldierMovementProjector movementProjection = new(_services.Grid);
            SoldierMovementPlanner movement = new(
                movementProjection,
                actions,
                _services.Log);
            _removalRates = new PairRemovalRateTable(_services, _ranged);
            _melee = new MeleeStrikeEstimator(_services, _ranged);
            _exchange = new EngagementExchangeModel(
                _services, _ranged, _melee, _removalRates);
            _potential = new EngagementPotential(
                _grid,
                _ranged,
                _exchange,
                tacticsSkill,
                _context);
            _blast = new BlastThrowEvaluator(
                targeting,
                (soldier, range, movementDirection) =>
                    _ranged.GetNearestEnemySquadsWithinRange(soldier, range, movementDirection)
                        .SelectMany(candidateSquad => candidateSquad.AbleSoldiers));
            SoldierActionPlanner soldierActions = new(_services, _ranged, _blast);
            _engagementPolicy = new SquadEngagementPolicy(
                _services,
                _ranged,
                movementProjection,
                _exchange,
                _potential,
                soldierActions);
            SquadRunUtilityActionBuilder runUtility =
                new(_services, actions);
            MeleeActionBuilder meleeBuilder = new(
                _services,
                actions,
                random,
                _ranged,
                _melee,
                movement,
                runUtility.AddPermittedRunUtilityActionToBag);
            _actionBuilder = new SquadActionBuilder(
                _services,
                actions,
                random,
                movement,
                _melee,
                meleeBuilder,
                runUtility,
                _engagementPolicy);
            _engagementPolicy.TraceTurnNumber = _traceTurnNumber;
            _engagementPolicy.TraceSideLabel = _traceSideLabel;
        }

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
                _actionBuilder.PrepareMeleeActions(squad);
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
            return _engagementPolicy.ChooseEngagementOption(
                squad,
                frame,
                profiles,
                allFrames,
                enemySquads,
                roleTargets);
        }

        internal void InitializeEngagementHorizon(
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            int maxDegreeOfParallelism)
        {
            _engagementPolicy.InitializeEngagementHorizon(
                profiles,
                frames,
                maxDegreeOfParallelism);
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

        /// <summary>Layer 2.5 declaration. Called for every squad before Layer 3.</summary>
        internal void DeclareEngagementDecision(SquadEngagementDecision decision)
        {
            _actionBuilder.DeclareEngagementDecision(decision);
        }

        /// <summary>Layer 3: constructs the existing per-soldier actions for a declared option.</summary>
        internal void BuildEngagementActions(SquadEngagementDecision decision)
        {
            _actionBuilder.BuildEngagementActions(decision);
        }

        /// <summary>Plans a full-speed bound along the force's fixed withdrawal heading.</summary>
        public void PrepareBoundActions(BattleSquad squad, ushort withdrawalHeading)
        {
            _actionBuilder.PrepareBoundActions(squad, withdrawalHeading);
        }

        /// <summary>
        /// Plans a routing squad (OnlyWar_TDD.md §6.6): Run directly away
        /// from the nearest enemy; no shooting or voluntary utility action; an engaged routing
        /// soldier cannot simply leave melee and remains subject to normal enemy attacks.
        /// The heading is a squad property and is owned by the action builder.
        /// </summary>
        public void PrepareRoutingActions(BattleSquad squad)
        {
            _actionBuilder.PrepareRoutingActions(squad);
        }

        // TEST SEAM, like the ranged forwarders above: BattleSquadPlannerTests drives forfeited
        // parry risk through a constructed planner. It is the only melee scorer the tests reach
        // for; everything else on MeleeStrikeEstimator is called through _melee directly.
        internal float EstimateForfeitedParryRisk(
            BattleSoldier defender,
            IReadOnlyList<BattleSoldier> adjacentAttackers,
            IReadOnlyCollection<MeleeWeapon> projectedDefensiveWeapons) =>
            _melee.EstimateForfeitedParryRisk(
                defender, adjacentAttackers, projectedDefensiveWeapons);

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

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

    }
}
