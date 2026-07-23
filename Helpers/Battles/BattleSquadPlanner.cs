using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquadPlanner
    {
        private const float TargetTakeOutConfidenceThreshold = MeleeMath.TakeOutConfidenceTarget;
        private const int RangedTargetSquadCandidateCount = 3;
        // TUNABLE: the grenade is a sidearm, not the main gun. A blast throw must beat the
        // soldier's best conventional action (rifle shot or cone burst) by more than this
        // expected-battle-value margin before it is chosen. 0 keeps any strict improvement;
        // raise it to reserve grenades for clearly better (clustered) opportunities.
        private const float BlastOverConventionalScoreMargin = 0.25f;
        private const float WalkSpeedMultiplier = 0.2f;
        private const float JogSpeedMultiplier = 0.5f;
        private const float WalkBulkMultiplier = 0.5f;
        private const float FullBulkMultiplier = 1f;
        private const float WalkAimMultiplier = 0.5f;
        // TUNABLE: a step-back (kite) applies WalkBulkMultiplier x Bulk to every shot that
        // turn. For a light weapon that cost is negligible and repositioning to the sweet
        // spot is worthwhile; for a heavy, high-Bulk weapon it guts the soldier's firepower,
        // so backpedaling to "optimal" range is self-defeating -- he removes more value
        // planting his feet and firing (Bulk 0). A soldier only votes to walk back when the
        // walking shot preserves at least this fraction of his stationary shot's value;
        // otherwise he votes to hold. Calibrated so standard infantry rifles (Bulk 4:
        // Boltgun, Lasgun) keep kiting while genuine heavy weapons (Bulk 6-8: Heavy Bolter,
        // Lascannon, Autocannon, Missile Launcher) plant and fire. 1.0 forbids any bulk
        // loss; 0 restores the old bulk-blind kiting.
        private const float WalkBulkShootingRetention = 0.4f;
        // Blast planning integrates enemy AND friendly value over the delivery scatter
        // distribution (not just the on-target impact), so a throw that only frags the
        // squad when it misses is no longer scored as free. See EvaluateBlastThrow and
        // Design/Active/UnifiedRangedTargetingAndScatterScoring.md.
        private const float BlastDeliveryRollMean = 10.5f;
        private const float BlastDeliveryRollStdDev = 3.0f;
        // The execution-time damage roll (BlastAttackAction.HandleHit): DamageMultiplier is
        // scaled by (mean + z * stdDev) per victim. The planner's wound estimate integrates
        // over this roll so armored figures carry their real armor-penetrating tail instead
        // of being scored invulnerable at the mean.
        private const float BlastDamageRollMean = 3.5f;
        private const float BlastDamageRollStdDev = 1.75f;
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
        // targeting exactly. This value needs tuning against real battles.
        private const float BaseLaneSpreadCoefficient = 1.0f;
        // Fire discipline used when a squad has no faction (test fixtures, stray battle squads).
        private const float DefaultFireDiscipline = 0.5f;

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

        internal sealed class RangedTargetEvaluation
        {
            public BattleSoldier Target { get; }
            public RangedWeapon Weapon { get; }
            public float Range { get; }
            public int ShotsToFire { get; }
            public float HitProbability { get; }
            public float ExpectedDamageRatio { get; }
            public float ExpectedEnemyBattleValueRemoved { get; }
            public float ExpectedFriendlyBattleValueLost { get; }
            public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

            public RangedTargetEvaluation(
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                int shotsToFire,
                float hitProbability,
                float expectedDamageRatio,
                float expectedEnemyBattleValueRemoved,
                float expectedFriendlyBattleValueLost)
            {
                Target = target;
                Weapon = weapon;
                Range = range;
                ShotsToFire = shotsToFire;
                HitProbability = hitProbability;
                ExpectedDamageRatio = expectedDamageRatio;
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
                bool firingIntoMelee)
            {
                _weaponSkill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                _rangeModifier = BattleModifiersUtil.CalculateRangeModifier(range, target.CurrentSpeed);
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

        private sealed class WorkerPlan
        {
            internal readonly List<IAction> ShootActions = [];
            internal readonly List<IAction> MoveActions = [];
            internal readonly List<IAction> MeleeActions = [];
            internal BattleSquadPlanner Planner { get; }

            internal WorkerPlan(BattleSquadPlanner owner)
            {
                Planner = new BattleSquadPlanner(
                    owner._grid,
                    owner._soldierMap,
                    ShootActions,
                    MoveActions,
                    MeleeActions,
                    owner._log,
                    owner._meleeWeaponTemplates,
                    owner._random,
                    1,
                    owner._context);
            }
        }

        private void MergeWorkerPlans(IReadOnlyList<WorkerPlan> plans)
        {
            foreach (WorkerPlan plan in plans)
            {
                if (plan == null) continue;
                foreach (IAction action in plan.ShootActions) _shootActions.Add(action);
                foreach (IAction action in plan.MoveActions) _moveActions.Add(action);
                foreach (IAction action in plan.MeleeActions) _meleeActions.Add(action);
            }
        }

        private void PrepareMovingRangedSoldiers(
            IReadOnlyList<BattleSoldier> soldiers,
            Func<BattleSquadPlanner, BattleSoldier, ValueTuple<int, int>> planMovement,
            Action<BattleSquadPlanner, BattleSoldier, ValueTuple<int, int>> planRanged)
        {
            WorkerPlan[] plans = new WorkerPlan[soldiers.Count];
            ValueTuple<int, int>[] movementDirections = new ValueTuple<int, int>[soldiers.Count];

            // Destination selection and reservation remain deliberately ordered. Each completed
            // reservation makes the next soldier choose a non-conflicting destination.
            for (int index = 0; index < soldiers.Count; index++)
            {
                WorkerPlan worker = new(this);
                plans[index] = worker;
                movementDirections[index] = planMovement(worker.Planner, soldiers[index]);
            }

            void PlanRangedAt(int index) => planRanged(
                plans[index].Planner,
                soldiers[index],
                movementDirections[index]);

            if (_maxPlanningDegreeOfParallelism <= 1 || soldiers.Count <= 1)
            {
                for (int index = 0; index < soldiers.Count; index++) PlanRangedAt(index);
            }
            else
            {
                Parallel.For(
                    0,
                    soldiers.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxPlanningDegreeOfParallelism
                    },
                    PlanRangedAt);
            }
            MergeWorkerPlans(plans);
        }

        private void PrepareStandingSoldiers(
            IReadOnlyList<BattleSoldier> soldiers,
            bool allowCharge)
        {
            WorkerPlan[] results = new WorkerPlan[soldiers.Count];
            List<int> rangedIndices = [];
            for (int index = 0; index < soldiers.Count; index++)
            {
                BattleSoldier soldier = soldiers[index];
                if (allowCharge && ShouldChargeFromStanding(soldier))
                {
                    // Charging owns the shared reservation grid and consumes melee-planning RNG,
                    // so retain the legacy soldier order for this minority path.
                    WorkerPlan worker = new(this);
                    worker.Planner.AddChargeActionsToBag(soldier);
                    results[index] = worker;
                }
                else
                {
                    rangedIndices.Add(index);
                }
            }

            void PlanRangedAt(int rangedIndex)
            {
                int soldierIndex = rangedIndices[rangedIndex];
                WorkerPlan worker = new(this);
                worker.Planner.AddStandingActionsToBag(
                    soldiers[soldierIndex],
                    allowCharge: false);
                results[soldierIndex] = worker;
            }

            if (_maxPlanningDegreeOfParallelism <= 1 || rangedIndices.Count <= 1)
            {
                for (int index = 0; index < rangedIndices.Count; index++) PlanRangedAt(index);
            }
            else
            {
                Parallel.For(
                    0,
                    rangedIndices.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxPlanningDegreeOfParallelism
                    },
                    PlanRangedAt);
            }
            MergeWorkerPlans(results);
        }

        // How far behind the friendly fighting line an HQ squad tries to stay. Matches the
        // placers' HQ rear offset so a rear-deployed HQ starts the battle already satisfied.
        private const float HqLineBuffer = 10f;

        public void PrepareActions(BattleSquad squad, IReadOnlyCollection<BattleSquad> friendlySquads = null)
        {
            // If no living enemy remains on the grid, this squad has nothing to plan against: every
            // targeting helper below resolves the "nearest enemy" to -1 and then indexes
            // _soldierMap[-1], which throws. Enemy presence is global to a side, so one probe settles
            // it — if the opposing side still has anyone on the grid, every per-soldier lookup will
            // find them. The battle is effectively decided; hold and let the resolver's end-of-turn
            // check close it out. (Latent since forever; surfaced once fully-organized NPC factions
            // began actually joining tactical battles at scale.)
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
                // An HQ squad with line troops still standing keeps itself behind them
                // ("stay behind the troop wall") and only joins a melee once the troops are
                // stuck in. Everything below is skipped if the HQ decides to hold or fall
                // back; otherwise it plans like any other squad, minus the charge impulse.
                bool suppressCharge = false;
                if (squad.Squad?.SquadTemplate?.SquadType.HasFlag(Models.Squads.SquadTypes.HQ) == true
                    && friendlySquads != null)
                {
                    List<BattleSquad> wall = friendlySquads
                        .Where(s => s.Id != squad.Id
                            && s.Status == BattleSquadStatus.Active
                            && s.AbleSoldiers.Count > 0
                            && s.Squad?.SquadTemplate?.SquadType.HasFlag(Models.Squads.SquadTypes.HQ) != true)
                        .ToList();
                    if (wall.Count > 0)
                    {
                        bool wallEngaged = wall.Any(s => s.IsInMelee);
                        suppressCharge = !wallEngaged;
                        if (!wallEngaged)
                        {
                            float wallDistance = wall.Min(NearestEnemyDistance);
                            float ownDistance = NearestEnemyDistance(squad);
                            if (ownDistance < wallDistance + HqLineBuffer)
                            {
                                // At or ahead of the wall: back off at a walk, weapons up.
                                squad.MovementTier = SquadMovementTier.Walk;
                                ApplyDeclaredMovementState(squad);
                                foreach (BattleSoldier soldier in squad.AbleSoldiers)
                                {
                                    AddRetreatingActionsToBag(soldier, SquadMovementTier.Walk);
                                }
                                return;
                            }
                            float jogReach = squad.GetSquadMove() * JogSpeedMultiplier;
                            if (ownDistance < wallDistance + HqLineBuffer + jogReach)
                            {
                                // Comfortably tucked behind the line: hold and shoot rather
                                // than jitter across the buffer boundary every turn.
                                squad.MovementTier = SquadMovementTier.Stationary;
                                ApplyDeclaredMovementState(squad);
                                PrepareStandingSoldiers(
                                    squad.AbleSoldiers,
                                    allowCharge: false);
                                return;
                            }
                        }
                    }
                }

                // need some concept of squad disposition... stance, whether they're actively aiming
                // determine closest enemy
                // determine our optimal range
                // determine closest enemy optimal range
                // if the enemy wants to advance, we want to stay put, and vice versa
                // if we both want to get closer or both want to stay put, it's more interesting

                // retreatng is moving full tilt away from the enemy
                // TODO: this won't be a valid option when surrounded
                int retreatVotes = 0;
                // falling back is moving at 1/3 speed away from the enemy,
                // leaving the possibility of shooting
                //int fallbackVotes = 0;
                // advancing is sprinting toward the enemy
                int advanceVotes = 0;
                // walking is a small withdrawal/reposition while retaining effective fire
                int walkVotes = 0;
                // standing is not moving
                int standVotes = 0;
                // charging is moving into hand-to-hand contact with the enemy
                int chargeVotes = 0;
                foreach (BattleSoldier soldier in squad.AbleSoldiers)
                {
                    switch (AssessSoldierEngagement(soldier))
                    {
                        case EngagementIntent.Retreat:
                            retreatVotes++;
                            break;
                        case EngagementIntent.Walk:
                            walkVotes++;
                            break;
                        case EngagementIntent.Advance:
                            advanceVotes++;
                            break;
                        case EngagementIntent.Charge:
                            // A charge is a full-speed advance that also wants melee contact;
                            // the tier selection below turns a charge-heavy advance into InMelee.
                            advanceVotes++;
                            chargeVotes++;
                            break;
                        default:
                            standVotes++;
                            break;
                    }
                }

                // "If I want to melee, let the troops get stuck in first": an HQ behind an
                // unengaged wall folds its charge impulse into a plain advance.
                if (suppressCharge)
                {
                    chargeVotes = 0;
                }

                // A Shaken squad will not advance toward the enemy (Design/Active/
                // MoraleAndRout.md §6): advance/charge impulses collapse into holding ground.
                // The state is recomputed every turn, so this is not sticky.
                if (squad.MoraleState == MoraleState.Shaken && advanceVotes > 0)
                {
                    standVotes += advanceVotes;
                    advanceVotes = 0;
                    chargeVotes = 0;
                }

                if (advanceVotes > standVotes
                    && advanceVotes > retreatVotes
                    && advanceVotes > walkVotes)
                {
                    if (chargeVotes > 0 && chargeVotes * 2 >= advanceVotes)
                    {
                        squad.MovementTier = SquadMovementTier.InMelee;
                        ApplyDeclaredMovementState(squad);
                        foreach (BattleSoldier soldier in squad.AbleSoldiers)
                        {
                            AddChargeActionsToBag(soldier);
                        }
                    }
                    else
                    {
                        squad.MovementTier = ShouldJogAndShoot(squad)
                            ? SquadMovementTier.Jog
                            : SquadMovementTier.Run;
                        ApplyDeclaredMovementState(squad);
                        PrepareAdvanceSoldiers(squad.AbleSoldiers, squad.MovementTier);
                    }
                }
                else if (retreatVotes > standVotes
                    && retreatVotes > advanceVotes
                    && retreatVotes > walkVotes)
                {
                    squad.MovementTier = SquadMovementTier.Run;
                    ApplyDeclaredMovementState(squad);
                    PrepareRetreatingSoldiers(squad.AbleSoldiers, squad.MovementTier);
                }
                else if (walkVotes > standVotes
                    && walkVotes >= advanceVotes
                    && walkVotes >= retreatVotes)
                {
                    squad.MovementTier = SquadMovementTier.Walk;
                    ApplyDeclaredMovementState(squad);
                    PrepareRetreatingSoldiers(squad.AbleSoldiers, squad.MovementTier);
                }
                else
                {
                    squad.MovementTier = SquadMovementTier.Stationary;
                    ApplyDeclaredMovementState(squad);
                    PrepareStandingSoldiers(squad.AbleSoldiers, allowCharge: true);
                }
            }
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

        /// <summary>Plans the stationary firing element of a leapfrog withdrawal.</summary>
        public void PrepareCoverActions(BattleSquad squad)
        {
            squad.WithdrawalRole = WithdrawalRole.Cover;
            squad.MovementTier = SquadMovementTier.Stationary;
            ApplyDeclaredMovementState(squad);
            PrepareStandingSoldiers(
                squad.AbleSoldiers.OrderBy(s => s.Soldier.Id).ToList(),
                allowCharge: false);
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
                AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, SquadMovementTier.Run),
                    movementLine,
                    SquadMovementTier.Run);
                AddPermittedRunUtilityActionToBag(soldier);
            }
        }

        /// <summary>Plans a rear guard holding in place, or continuing an existing melee.</summary>
        public void PrepareRearGuardActions(BattleSquad squad)
        {
            squad.WithdrawalRole = WithdrawalRole.RearGuard;
            if (squad.IsInMelee)
            {
                squad.MovementTier = SquadMovementTier.InMelee;
                ApplyDeclaredMovementState(squad);
                foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
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
                return;
            }

            squad.MovementTier = SquadMovementTier.Stationary;
            ApplyDeclaredMovementState(squad);
            PrepareStandingSoldiers(
                squad.AbleSoldiers.OrderBy(s => s.Soldier.Id).ToList(),
                allowCharge: false);
        }

        /// <summary>
        /// Plans a routing squad (Design/Active/WithdrawalAndPursuit.md §10): Run directly away
        /// from the nearest enemy; no shooting or voluntary utility action; an engaged routing
        /// soldier cannot simply leave melee and remains subject to normal enemy attacks.
        /// </summary>
        public void PrepareRoutingActions(BattleSquad squad)
        {
            squad.WithdrawalRole = WithdrawalRole.Routing;
            squad.MovementTier = SquadMovementTier.Run;
            ApplyDeclaredMovementState(squad);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
                {
                    // Pinned in melee — he fights because he cannot flee, not because he wants to.
                    AddMeleeActionsToBag(soldier);
                    continue;
                }

                float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
                if (closestEnemyId == -1) continue;
                ValueTuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
                ValueTuple<int, int> awayLine = new(
                    soldier.TopLeft.Value.Item1 - enemyPosition.Item1,
                    soldier.TopLeft.Value.Item2 - enemyPosition.Item2);
                if (awayLine.Item1 == 0 && awayLine.Item2 == 0)
                {
                    awayLine = new ValueTuple<int, int>(0, 1);
                }
                AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, SquadMovementTier.Run),
                    awayLine,
                    SquadMovementTier.Run);
                // Deliberately no run-utility action: routing permits no voluntary actions.
            }
        }

        /// <summary>Plans pursuit against the supplied active withdrawing formations.</summary>
        public void PreparePursuitActions(
            BattleSquad squad,
            PursuitPosture posture,
            IReadOnlyCollection<BattleSquad> withdrawingTargets)
        {
            squad.WithdrawalRole = WithdrawalRole.None;
            switch (posture)
            {
                case PursuitPosture.BreakOff:
                    squad.MovementTier = SquadMovementTier.Stationary;
                    ApplyDeclaredMovementState(squad);
                    return;
                case PursuitPosture.Follow:
                    PrepareFollowingActions(squad, withdrawingTargets);
                    return;
                case PursuitPosture.Press:
                    PreparePressingActions(squad, withdrawingTargets);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(posture), posture, null);
            }
        }

        private void PrepareFollowingActions(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> withdrawingTargets)
        {
            // Following is a firing pursuit, and the jog only pays while the guns are
            // actually worth firing. Out of effective range the squad runs to regain it —
            // otherwise any quarry faster than a jog simply walks away from the pursuit —
            // and drops back to jog-and-shoot once shots are worthwhile again.
            if (!ShouldJogAndShoot(squad))
            {
                squad.MovementTier = SquadMovementTier.Run;
                ApplyDeclaredMovementState(squad);
                foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
                {
                    BattleSoldier target = FindNearestTarget(soldier, withdrawingTargets);
                    if (target == null) continue;
                    ValueTuple<int, int> line = new(
                        target.TopLeft.Value.Item1 - soldier.TopLeft.Value.Item1,
                        target.TopLeft.Value.Item2 - soldier.TopLeft.Value.Item2);
                    AddMoveAction(
                        soldier,
                        GetMovementBudget(soldier, SquadMovementTier.Run),
                        line,
                        SquadMovementTier.Run);
                    AddPermittedRunUtilityActionToBag(soldier);
                }
                return;
            }

            squad.MovementTier = SquadMovementTier.Jog;
            ApplyDeclaredMovementState(squad);
            List<BattleSoldier> firingPursuers = [];
            Dictionary<int, BattleSoldier> pursuitTargets = [];
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                BattleSoldier target = FindNearestTarget(soldier, withdrawingTargets);
                if (target == null) continue;
                firingPursuers.Add(soldier);
                pursuitTargets.Add(soldier.Soldier.Id, target);
            }
            PrepareMovingRangedSoldiers(
                firingPursuers,
                (planner, soldier) =>
                {
                    BattleSoldier target = pursuitTargets[soldier.Soldier.Id];
                    ValueTuple<int, int> line = new(
                        target.TopLeft.Value.Item1 - soldier.TopLeft.Value.Item1,
                        target.TopLeft.Value.Item2 - soldier.TopLeft.Value.Item2);
                    return planner.AddMoveAction(
                        soldier,
                        GetMovementBudget(soldier, SquadMovementTier.Jog),
                        line,
                        SquadMovementTier.Jog);
                },
                (planner, soldier, direction) => planner.AddRangedActionToBag(
                    soldier,
                    FullBulkMultiplier,
                    aimMultiplier: 0,
                    movementDirection: direction));
        }

        private void PreparePressingActions(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> withdrawingTargets)
        {
            squad.MovementTier = SquadMovementTier.Run;
            ApplyDeclaredMovementState(squad);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                BattleSoldier target = FindNearestTarget(soldier, withdrawingTargets);
                if (target == null) continue;
                ValueTuple<int, int> line = new(
                    target.TopLeft.Value.Item1 - soldier.TopLeft.Value.Item1,
                    target.TopLeft.Value.Item2 - soldier.TopLeft.Value.Item2);
                AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, SquadMovementTier.Run),
                    line,
                    SquadMovementTier.Run);
                AddPermittedRunUtilityActionToBag(soldier);
            }
        }

        private BattleSoldier FindNearestTarget(
            BattleSoldier pursuer,
            IReadOnlyCollection<BattleSquad> withdrawingTargets)
        {
            if (withdrawingTargets == null) return null;

            HashSet<int> targetIds = withdrawingTargets
                .Where(squad => squad != null && squad.Status == BattleSquadStatus.Active)
                .SelectMany(squad => squad.AbleSoldiers)
                .Where(target => _soldierMap.ContainsKey(target.Soldier.Id))
                .Select(target => target.Soldier.Id)
                .ToHashSet();
            int bestTargetId = -1;
            float bestDistance = float.MaxValue;
            foreach ((int enemyId, float distance) in
                _grid.GetEnemyDistances(pursuer.Soldier.Id))
            {
                if (targetIds.Contains(enemyId)
                    && (distance < bestDistance
                        || (distance == bestDistance
                            && (bestTargetId == -1 || enemyId < bestTargetId))))
                {
                    bestTargetId = enemyId;
                    bestDistance = distance;
                }
            }
            return bestTargetId == -1 ? null : _soldierMap[bestTargetId];
        }

        private void ApplyDeclaredMovementState(BattleSquad squad)
        {
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
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
                        soldier.CurrentSpeed = _grid.IsAdjacentToEnemy(soldier.Soldier.Id)
                            ? 0
                            : soldier.GetMoveSpeed();
                        soldier.Aim = null;
                        break;
                }
            }
        }

        // Per-soldier movement intent produced by AssessSoldierEngagement, tallied into the
        // squad's advance/stand/walk/retreat/charge vote in PrepareActions.
        private enum EngagementIntent { Retreat, Walk, Stand, Advance, Charge }

        // TUNABLE (engagement-value model): a charge must beat the best held ranged shot by at
        // least this expected-battle-value margin before a soldier who cannot establish a good
        // standoff range abandons his gun and runs in. 0 charges on any strict improvement; raise
        // it to reserve melee rushes for clearly winning trades. See
        // Design/Active/EngagementValueModel.md.
        private const float EngagementChargeMargin = 0.25f;
        // How many of the engaged squad's nearest members a would-be charger projects strikes
        // against when estimating a melee's value. A charger reaches only the front of a squad.
        private const int EngagementMeleeTargetSampleCount = 4;
        // Cap on the number of turns of incoming fire charged against a run-in. Bounds the closing
        // cost of a very long charge (which the AI should almost never take anyway).
        private const int EngagementMaxExposureTurns = 4;
        // Enemies more than this far beyond the target contribute negligible fire during a run-in;
        // the nearest-first distance scan stops there to stay bounded in large battles.
        private const float EngagementRearThreatCutoff = 30f;

        /// <summary>
        /// Decides how a single soldier wants to move relative to the nearest enemy, as a vote in
        /// its squad's collective movement decision. The core question is where this soldier can
        /// deal with that enemy: template (cone) weapons want to enter their auto-hit envelope;
        /// conventional weapons with a real standoff range hold or reposition around it; and a
        /// soldier with no effective standoff range (cannot reliably hit, or cannot wound at any
        /// range) weighs charging into melee against simply holding and taking low-odds shots
        /// (<see cref="ResolveWeakRangedEngagement"/>) rather than blindly closing the distance.
        /// </summary>
        private EngagementIntent AssessSoldierEngagement(BattleSoldier soldier)
        {
            float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestSoldierId);
            BattleSoldier closestEnemy = _soldierMap[closestSoldierId];
            BattleSquad closestSquad = closestEnemy.BattleSquad;
            float targetSize = closestSquad.GetAverageSize();
            float targetArmor = closestSquad.GetAverageArmor();
            float targetCon = closestSquad.GetAverageConstitution();
            float targetEvasion = closestSquad.GetAverageRangedEvasion();

            float templateMaximumRange = soldier.RangedWeapons
                .Where(weapon => weapon.Template.IsConeWeapon)
                .Select(weapon => weapon.Template.MaximumRange)
                .DefaultIfEmpty(0)
                .Max();
            if (templateMaximumRange > 0)
            {
                // Template weapons auto-hit once they are in reach, so their movement decision is
                // about entering the template rather than finding a to-hit sweet spot derived from
                // accuracy, target size, or evasion.
                if (distance > templateMaximumRange)
                {
                    return EngagementIntent.Advance;
                }
                // Physically in range but no safe positive-value firing line: reposition rather
                // than declare Stationary and then smuggle a full-speed closing move into it.
                return SelectBestTemplateFiringLine(soldier) == null
                    ? EngagementIntent.Advance
                    : EngagementIntent.Stand;
            }

            float preferredHitDistance = BattleModifiersUtil.CalculateOptimalDistance(
                soldier, targetSize, targetArmor, targetCon, targetEvasion);

            if (preferredHitDistance == -1)
            {
                // No functioning hands — no weapon to fight with. Fall back.
                return EngagementIntent.Retreat;
            }

            if (preferredHitDistance == 0)
            {
                // No effective standoff range against this target — either we cannot reliably hit
                // it (single-shot heavy weapon vs a small, evasive target) or cannot wound it at any
                // range (light weapon vs heavy armor and constitution). Rather than blindly close
                // the distance (which sent missile squads and autogun cultists sprinting at Space
                // Marines), weigh a charge against holding and taking the low-odds shots.
                return ResolveWeakRangedEngagement(
                    soldier, closestEnemy, closestSquad, distance, targetArmor);
            }

            if (distance > preferredHitDistance * 3)
            {
                return EngagementIntent.Advance;
            }
            if (distance < preferredHitDistance)
            {
                // The enemy is inside this soldier's sweet spot, so geometry wants a step back.
                // Only take it if the walk's Bulk penalty doesn't gut his firepower.
                return WalkBackPreservesShooting(soldier)
                    ? EngagementIntent.Walk
                    : EngagementIntent.Stand;
            }

            // Within [preferredHitDistance, 3x]: advance only if we out-range the enemy's own
            // preferred engagement distance. Uses the closest enemy as a deterministic representative
            // (the prior random draw perturbed the seeded planning stream for no benefit).
            float targetPreferredDistance = BattleModifiersUtil.CalculateOptimalDistance(
                closestEnemy,
                soldier.Soldier.Size,
                soldier.Armor.Template.ArmorProvided,
                soldier.Soldier.Constitution,
                soldier.Soldier.Template.Species.RangedEvasion);
            return preferredHitDistance < targetPreferredDistance
                ? EngagementIntent.Advance
                : EngagementIntent.Stand;
        }

        /// <summary>
        /// Movement decision for a soldier with no effective standoff range against the nearest
        /// enemy. Compares the net value of charging into melee (projected melee battle value minus
        /// the cost of crossing the gap under fire) against the best shot he could take by holding
        /// position. He charges only when melee clearly wins; otherwise, if any positive-value shot
        /// exists, he plants and fires; and only when neither shooting nor charging is productive
        /// does he fall back to the old "close to maximize a hit/penetration chance" behavior.
        /// </summary>
        private EngagementIntent ResolveWeakRangedEngagement(
            BattleSoldier soldier,
            BattleSoldier closestEnemy,
            BattleSquad closestSquad,
            float distance,
            float targetArmor)
        {
            RangedTargetEvaluation heldShot = SelectBestRangedTarget(soldier, useBulk: false);
            float rangedScore = heldShot?.Score ?? 0f;

            ChargeAssessment charge = EstimateChargeNet(soldier, closestSquad, distance);
            if (charge.MeleeBattleValue > 0
                && charge.NetValue > rangedScore
                && charge.NetValue > EngagementChargeMargin)
            {
                return charge.ReachesContactThisTurn
                    ? EngagementIntent.Charge
                    : EngagementIntent.Advance;
            }

            if (rangedScore > 0)
            {
                // A low-odds shot from safety still beats a losing charge: hold and fire.
                return EngagementIntent.Stand;
            }

            // Neither shooting nor charging looks productive here. Preserve the old desperate
            // close: getting nearer at least maximizes a hit/penetration chance, and there is
            // nothing to be gained by standing still.
            if (soldier.EquippedRangedWeapons.Count >= 1)
            {
                float desperateHitDistance = Math.Min(
                    EstimateArmorPenDistance(soldier.EquippedRangedWeapons[0], targetArmor),
                    BattleModifiersUtil.EstimateHitDistance(
                        soldier.Soldier,
                        soldier.EquippedRangedWeapons[0],
                        closestSquad.GetAverageSize(),
                        soldier.FunctioningHands,
                        closestSquad.GetAverageRangedEvasion()));
                if (desperateHitDistance > 0)
                {
                    float targetPreferredDistance = BattleModifiersUtil.CalculateOptimalDistance(
                        closestEnemy,
                        soldier.Soldier.Size,
                        soldier.Armor.Template.ArmorProvided,
                        soldier.Soldier.Constitution,
                        soldier.Soldier.Template.Species.RangedEvasion);
                    return desperateHitDistance < targetPreferredDistance
                        ? EngagementIntent.Advance
                        : EngagementIntent.Stand;
                }
            }
            return EngagementIntent.Charge;
        }

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

        /// <summary>
        /// Whether stepping back is worth the Bulk penalty it imposes on this soldier's
        /// shooting this turn. Both shots are scored against the current (unmoved) layout so
        /// only the Bulk multiplier differs: a stationary shot (Bulk 0) versus a walking shot
        /// (<see cref="WalkBulkMultiplier"/>). A light weapon keeps nearly all its value and
        /// the kite is fine; a heavy, high-Bulk weapon loses too much and the soldier is
        /// better off holding and firing. When there is no worthwhile stationary shot to
        /// protect (no ranged weapon, or a non-positive score), the step-back is about
        /// spacing rather than firepower, so it is left unchanged.
        /// </summary>
        private bool WalkBackPreservesShooting(BattleSoldier soldier)
        {
            RangedTargetEvaluation standing = SelectBestRangedTarget(soldier, 0f);
            if (standing == null || standing.Score <= 0)
            {
                return true;
            }

            RangedTargetEvaluation walking = SelectBestRangedTarget(soldier, WalkBulkMultiplier);
            float walkingScore = walking?.Score ?? 0f;
            return walkingScore >= standing.Score * WalkBulkShootingRetention;
        }

        private bool ShouldJogAndShoot(BattleSquad squad)
        {
            int worthwhileShots = 0;
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                ValueTuple<int, int>? movementDirection = GetDirectionToNearestEnemy(soldier);
                RangedTargetEvaluation conventionalShot = SelectBestRangedTarget(
                    soldier,
                    useBulk: true,
                    movementDirection: movementDirection);
                bool hasShot = SelectBestTemplateFiringLine(
                        soldier,
                        movementDirection: movementDirection) != null
                    || HasBlastTargetInRange(soldier)
                    || (conventionalShot?.Score > 0
                        && conventionalShot.HitProbability > 0.1f);
                if (hasShot)
                {
                    worthwhileShots++;
                }
            }

            return worthwhileShots > 0
                && worthwhileShots * 2 >= squad.AbleSoldiers.Count;
        }

        private bool ShouldChargeFromStanding(BattleSoldier soldier)
        {
            float range = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            if (closestEnemyId == -1) return false;
            float speed = _soldierMap[closestEnemyId].BattleSquad.AbleSoldiers
                .First()
                .GetMoveSpeed();
            bool hasLoadedTemplateWeapon = soldier.EquippedRangedWeapons.Any(
                weapon => weapon.Template.IsConeWeapon && weapon.LoadedAmmo > 0);
            return !hasLoadedTemplateWeapon
                && speed >= range
                && (soldier.Aim == null
                    || soldier.RangedWeapons.Count == 0
                    || soldier.RangedWeapons[0].LoadedAmmo == 0);
        }

        private void AddStandingActionsToBag(BattleSoldier soldier, bool allowCharge = true)
        {
            float range = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            // see if the enemy is within charging range and the soldier doesn't already have a target lined up
            if (allowCharge && ShouldChargeFromStanding(soldier))
            {
                AddChargeActionsToBag(soldier);
            }
            else if (soldier.RangedWeapons.Count == 0)
            {
                //Debug.Log("ISoldier with no ranged weapons just standing around");
            }
            // do we have a ranged weapon equipped
            else if (soldier.EquippedRangedWeapons.Count == 0 && soldier.RangedWeapons.Count > 0)
            {
                AddEquipRangedWeaponActionToBag(soldier);
            }
            else if (soldier.ReloadingPhase > 0 ||
                    (soldier.EquippedRangedWeapons.Count > 0 && soldier.RangedWeapons[0].LoadedAmmo == 0))
            {
                AddReloadRangedWeaponActionToBag(soldier);
            }
            // Sticky targeting (Phase 2): keep an established aim while it remains a viable,
            // worthwhile shot instead of rescanning the whole field every turn to confirm it is still
            // the single best option. The soldier re-acquires only once the aimed target dies, leaves
            // range, stops being worth shooting, or an un-engaged enemy is about to reach melee.
            else if (soldier.Aim != null
                && _soldierMap.ContainsKey(soldier.Aim.Value.Item1)
                && IsExistingAimStillViable(soldier))
            {
                BattleSoldier target = _soldierMap[soldier.Aim.Value.Item1];
                range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, soldier.Aim.Value.Item1);
                // if the aim cannot be improved, go ahead and shoot
                if (soldier.Aim.Value.Item3 >= 3)
                {
                    RangedTargetEvaluation effectEstimate = EvaluateRangedTarget(
                        soldier,
                        target,
                        soldier.Aim.Value.Item2,
                        range,
                        soldier.Aim.Value.Item2.Template.Accuracy + 4);
                    soldier.CurrentSpeed = 0;
                    _shootActions.Add(new ShootAction(soldier.Soldier.Id,
                        soldier.Aim.Value.Item1,
                        soldier.Aim.Value.Item2.Template.Id,
                        range,
                        effectEstimate.ShotsToFire,
                        false,
                        _grid,
                        _random));
                }
                else
                {
                    // the aim can be improved
                    // current aim bonus is 1 for all-out attack, plus weapon accuracy, plus aim
                    float currentModifiers = soldier.Aim.Value.Item2.Template.Accuracy + soldier.Aim.Value.Item3 + 1;
                    // item1 is the pre-roll to-hit total; item2 is the expected ratio of damage to con, so 1 is a potential killshot
                    RangedTargetEvaluation resultEstimate = EvaluateRangedTarget(
                        soldier,
                        target,
                        soldier.Aim.Value.Item2,
                        range,
                        currentModifiers);
                    // it's about to attack, go ahead and shoot, you may not get another chance
                    if (target.GetMoveSpeed() > range
                        // there's a good chance of both hitting and killing, go ahead and shoot now
                        || (resultEstimate.ExpectedDamageRatio >= 1 && resultEstimate.HitProbability >= 0.33f))
                    {
                        soldier.CurrentSpeed = 0;
                        _shootActions.Add(new ShootAction(soldier.Soldier.Id,
                            soldier.Aim.Value.Item1,
                            soldier.Aim.Value.Item2.Template.Id,
                            range,
                            resultEstimate.ShotsToFire,
                            false,
                            _grid,
                            _random));
                    }
                    else
                    {
                        // keep aiming
                        soldier.CurrentSpeed = 0;
                        _shootActions.Add(new AimAction(soldier, target, soldier.Aim.Value.Item2, _log));
                    }
                }
            }
            // need to aim or shoot at a new target
            else
            {
                soldier.Aim = null;
                soldier.CurrentSpeed = 0;
                AddRangedActionToBag(soldier, false);
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

        private void PrepareAdvanceSoldiers(
            IReadOnlyList<BattleSoldier> soldiers,
            SquadMovementTier tier)
        {
            if (tier != SquadMovementTier.Jog)
            {
                foreach (BattleSoldier soldier in soldiers) AddAdvanceActionsToBag(soldier, tier);
                return;
            }

            PrepareMovingRangedSoldiers(
                soldiers,
                (planner, soldier) => planner.AddAdvanceMoveAction(soldier, tier),
                (planner, soldier, direction) => planner.AddRangedActionToBag(
                    soldier,
                    FullBulkMultiplier,
                    aimMultiplier: 0,
                    movementDirection: direction));
        }

        private void PrepareRetreatingSoldiers(
            IReadOnlyList<BattleSoldier> soldiers,
            SquadMovementTier tier)
        {
            if (tier is not (SquadMovementTier.Walk or SquadMovementTier.Jog))
            {
                foreach (BattleSoldier soldier in soldiers) AddRetreatingActionsToBag(soldier, tier);
                return;
            }

            PrepareMovingRangedSoldiers(
                soldiers,
                (planner, soldier) => planner.AddRetreatMoveAction(soldier, tier),
                (planner, soldier, direction) =>
                {
                    if (tier == SquadMovementTier.Walk)
                    {
                        planner.AddRangedActionToBag(
                            soldier,
                            WalkBulkMultiplier,
                            WalkAimMultiplier);
                    }
                    else
                    {
                        planner.AddRangedActionToBag(
                            soldier,
                            FullBulkMultiplier,
                            aimMultiplier: 0,
                            movementDirection: direction);
                    }
                });
        }

        private ValueTuple<int, int> AddAdvanceMoveAction(
            BattleSoldier soldier,
            SquadMovementTier tier)
        {
            _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            float moveSpeed = GetMovementBudget(soldier, tier);
            ValueTuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
            ValueTuple<int, int> line = new(
                (short)(enemyPosition.Item1 - soldier.TopLeft.Value.Item1),
                (short)(enemyPosition.Item2 - soldier.TopLeft.Value.Item2));
            return AddMoveAction(soldier, moveSpeed, line, tier);
        }

        private ValueTuple<int, int> AddRetreatMoveAction(
            BattleSoldier soldier,
            SquadMovementTier tier)
        {
            float moveSpeed = GetMovementBudget(soldier, tier);
            int newY = (int)(_grid.GetSoldierSide(soldier.Soldier.Id) ? -moveSpeed : moveSpeed);
            return AddMoveAction(soldier, moveSpeed, new ValueTuple<int, int>(0, newY), tier);
        }

        private void AddAdvanceActionsToBag(BattleSoldier soldier, SquadMovementTier tier)
        {
            // for now advance toward closest enemy;
            // down the road, we may want to advance toward a rearward enemy, ignoring the closest enemy

            ValueTuple<int, int> movementDirection = AddAdvanceMoveAction(soldier, tier);

            if (tier == SquadMovementTier.Jog)
            {
                AddRangedActionToBag(
                    soldier,
                    FullBulkMultiplier,
                    aimMultiplier: 0,
                    movementDirection: movementDirection);
            }
            else
            {
                AddPermittedRunUtilityActionToBag(soldier);
            }
        }

        private void AddRetreatingActionsToBag(BattleSoldier soldier, SquadMovementTier tier)
        {
            ValueTuple<int, int> movementDirection = AddRetreatMoveAction(soldier, tier);

            if (tier == SquadMovementTier.Walk)
            {
                AddRangedActionToBag(soldier, WalkBulkMultiplier, WalkAimMultiplier);
            }
            else if (tier == SquadMovementTier.Jog)
            {
                AddRangedActionToBag(
                    soldier,
                    FullBulkMultiplier,
                    aimMultiplier: 0,
                    movementDirection: movementDirection);
            }
            else
            {
                AddPermittedRunUtilityActionToBag(soldier);
            }
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
            float woundRatio = EstimateTakeOutOnHit(defender, attacker, attackingWeapon);
            return strikeCount
                * increasedHitProbability
                * woundRatio
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
                            //AddStandingActionsToBag(soldier);
                        }
                    }
                    else
                    {
                        AddChargeActionsHelper(soldier, closestEnemyId, soldier.TopLeft.Value, distance, oppSquad, newPos);
                    }
                }
            }
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
            float averageDamage = attacker.Soldier.Strength * weapon.Template.StrengthMultiplier * 3.5f;
            float effectiveArmor = (target.Armor?.Template.ArmorProvided ?? 0) * weapon.Template.ArmorMultiplier;
            float penetratingDamage = Math.Max(0, averageDamage - effectiveArmor);
            float woundRatio = (penetratingDamage * weapon.Template.WoundMultiplier) / target.Soldier.Constitution;
            return Math.Clamp(woundRatio, 0, 1);
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
            // Walking soldiers retain their aim, but they reach this general ranged path rather than
            // AddStandingActionsToBag. Apply the same cap here so movement cannot let the accumulated
            // aim pass 3 (after which the stationary cap would also have been missed historically).
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

            // The evaluation here shares the context cache with the aim branch's own re-evaluation
            // (identical key), so keeping the aim viable costs nothing extra.
            RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                soldier,
                target,
                weapon,
                range,
                weapon.Template.Accuracy + aim.Item3 + 1);
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
                        float effectiveScore = evaluation.Score - lanePenalty;
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
                        float woundRatio = Math.Clamp(
                            CalculateExpectedDamage(
                                weapon,
                                victimRange,
                                armor,
                                victim.Soldier.Constitution),
                            0,
                            1);
                        float expectedBattleValueRemoval = woundRatio * GetBattleValue(victim);
                        if (_grid.GetSoldierSide(victimId) == shooterSide)
                        {
                            expectedFriendlyBattleValueLost += expectedBattleValueRemoval;
                        }
                        else
                        {
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
                    .Append($"dmgRatio {conventionalShot.ExpectedDamageRatio:F2}, score {conventionalShot.Score:F2} ")
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
        /// Mirror of <see cref="BlastAttackAction"/>'s damage math at the planning
        /// expectation used by CalculateExpectedDamage: the quadratic falloff scales the
        /// damage roll before armor subtraction, and everyone caught is auto-hit.
        /// </summary>
        private static float CalculateExpectedBlastDamage(
            RangedWeapon weapon,
            float distanceFromImpact,
            float armor,
            float con)
        {
            float falloff = 1f - (distanceFromImpact / weapon.Template.AreaRadius);
            float damage = weapon.Template.DamageMultiplier * 4.25f * falloff * falloff;
            float effectiveArmor = armor * weapon.Template.ArmorMultiplier;
            float penetratingDamage = Math.Max(0, damage - effectiveArmor);
            return con <= 0
                ? 1
                : (penetratingDamage * weapon.Template.WoundMultiplier) / con;
        }

        // Expected wound ratio for a blast hit, integrated over the execution-time damage roll
        // (roll ~ N(BlastDamageRollMean, BlastDamageRollStdDev), damage = DamageMultiplier * roll
        // * falloff^2). Unlike the mean-only CalculateExpectedBlastDamage, this credits the
        // armor-penetrating tail: an armored figure the mean never scratches still carries the
        // fraction of rolls that punch through. falloffSquared is the quadratic distance falloff
        // already squared.
        private static float CalculateExpectedBlastWoundRatio(
            RangedWeapon weapon,
            float falloffSquared,
            float armor,
            float con)
        {
            if (con <= 0)
            {
                return 1f;
            }
            float coefficient = weapon.Template.DamageMultiplier * falloffSquared;
            if (coefficient <= 0)
            {
                return 0f;
            }
            float effectiveArmor = armor * weapon.Template.ArmorMultiplier;
            // penetrating = coefficient * max(0, roll - armor/coefficient); take its expectation
            // over the normal roll via the closed form E[max(0, X - t)] for X ~ N(mu, sigma).
            float threshold = effectiveArmor / coefficient;
            float z = (BlastDamageRollMean - threshold) / BlastDamageRollStdDev;
            float expectedExcessRoll =
                ((BlastDamageRollMean - threshold) * GaussianCalculator.ApproximateNormalCDF(z))
                + (BlastDamageRollStdDev * NormalPdf(z));
            float expectedPenetrating = coefficient * Math.Max(0f, expectedExcessRoll);
            return (expectedPenetrating * weapon.Template.WoundMultiplier) / con;
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
            public readonly float Armor;
            public readonly float Constitution;
            public readonly float BattleValue;
            public readonly float Imminence;
            public readonly RangedWeapon Weapon;

            public BlastNearbySoldier(
                float offsetX,
                float offsetY,
                bool friendly,
                float armor,
                float constitution,
                float battleValue,
                float imminence,
                RangedWeapon weapon)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Friendly = friendly;
                Armor = armor;
                Constitution = constitution;
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
        // estimate. See Design/Active/UnifiedRangedTargetingAndScatterScoring.md.
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
                float armor = candidate.Armor?.Template.ArmorProvided ?? 0f;
                float imminence = friendly
                    ? 1f
                    : GetSquadImminence(soldier.BattleSquad, candidate.BattleSquad);
                nearby.Add(new BlastNearbySoldier(
                    offsetX,
                    offsetY,
                    friendly,
                    armor,
                    candidate.Soldier.Constitution,
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
                float woundRatio = Math.Clamp(
                    CalculateExpectedBlastWoundRatio(
                        victim.Weapon, falloff * falloff, victim.Armor, victim.Constitution),
                    0f,
                    1f);
                float removed = weight * woundRatio * victim.BattleValue;
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
            float additionalToHitModifier)
        {
            var cacheKey = (
                soldier.Soldier.Id,
                target.Soldier.Id,
                weapon.Template.Id,
                BitConverter.SingleToInt32Bits(range),
                BitConverter.SingleToInt32Bits(additionalToHitModifier),
                BitConverter.SingleToInt32Bits(target.CurrentSpeed),
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
                additionalToHitModifier);
            float woundRatio = Math.Clamp(attackEstimate.Item2, 0, 1);
            float imminence = GetSquadImminence(soldier.BattleSquad, target.BattleSquad);
            float enemyBattleValueRemoved = imminence
                * attackEstimate.Item1
                * woundRatio
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
                    float woundRatio = Math.Clamp(
                        CalculateExpectedDamage(
                            weapon,
                            range,
                            armor,
                            participant.Soldier.Constitution),
                        0,
                        1);
                    return victimProbability * woundRatio * GetBattleValue(participant);
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
            float moveAndAimMod)
        {
            int shotsToFire = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, (int)weapon.LoadedAmmo));
            float armor = target.Armor?.Template.ArmorProvided ?? 0;
            float con = target.Soldier.Constitution;
            float expectedDamage = CalculateExpectedDamage(weapon, range, armor, con);
            bool firingIntoMelee = _grid.IsTargetEngagedWithShootersAllies(
                soldier.Soldier.Id,
                target.Soldier.Id);
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                moveAndAimMod,
                firingIntoMelee);
            ValueTuple<float, float> estimate = new(0,0);
            for (int iteration = 0; iteration < 4; iteration++)
            {
                estimate = EstimateHitAndDamage(
                    hitContext,
                    expectedDamage,
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
                expectedDamage,
                shotsToFire);
            return new ValueTuple<float, float, int>(estimate.Item1, estimate.Item2, shotsToFire);
        }

        private int CalculateShotsToFire(RangedWeapon weapon, float toHitAtPlannedRateOfFire, float damagePerShot)
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

            if (damagePerShot <= 0)
            {
                return minRoF;
            }

            int killRof = (int)Math.Round(1 / damagePerShot) + 1;

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
            newLocation = FindBestLocation(soldier.TopLeft.Value, newLocation, moveSpeed);
            SquadMovementTier movementTier = tier ?? soldier.BattleSquad.MovementTier;
            soldier.CurrentSpeed = GetTierSpeed(soldier, movementTier);
            _grid.ReserveSpace(newLocation);
            ushort orientation = CalculateOrientationFromVector(line, soldier, movementTier);
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
            return actualDirection.Item1 == 0 && actualDirection.Item2 == 0
                ? line
                : actualDirection;
        }

        private ValueTuple<int, int>? GetDirectionToNearestEnemy(BattleSoldier soldier)
        {
            _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
            if (closestEnemyId == -1)
            {
                return null;
            }

            ValueTuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
            return new ValueTuple<int, int>(
                enemyPosition.Item1 - soldier.TopLeft.Value.Item1,
                enemyPosition.Item2 - soldier.TopLeft.Value.Item2);
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

        private ValueTuple<int, int> FindBestLocation(ValueTuple<int, int> startingPoint, ValueTuple<int, int> targetPoint, float speed)
        {
            float speedSq = speed * speed;
            int xMove = targetPoint.Item1 - startingPoint.Item1;
            int xMoveSq = xMove * xMove;
            int yMove = targetPoint.Item2 - startingPoint.Item2;
            int yMoveSq = yMove * yMove;
            // try shifting around the shorter axis first
            if (xMoveSq > yMoveSq)
            {

                while (xMoveSq > 0)
                {
                    int direction = yMove < 0 ? -1 : 1;
                    int i = 2;
                    int newY = yMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    while (newY * newY <= speedSq - xMoveSq)
                    {
                        ValueTuple<int, int> newTarget = new ValueTuple<int, int>(startingPoint.Item1 + xMove, startingPoint.Item2 + newY);
                        if (_grid.IsSpaceAvailable(newTarget))
                        {
                            return newTarget;
                        }
                        i++;
                        newY = yMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    }
                    // if we can't find a lateral move that works, start over with the main axis reduced by 1
                    xMove -= xMove > 0 ? 1 : -1;
                    xMoveSq = xMove * xMove;
                }
                //Debug.Log($"There is no place in the world for this move: {startingPoint.Item1}, {startingPoint.Item2}->{targetPoint.Item1},{targetPoint.Item2}, {speed}");
                return startingPoint;
            }
            else
            {
                while (yMoveSq > 0)
                {
                    int direction = xMove < 0 ? -1 : 1;
                    int i = 2;
                    int newX = xMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    while (newX * newX <= speedSq - yMoveSq)
                    {
                        ValueTuple<int, int> newTarget = new ValueTuple<int, int>(startingPoint.Item1 + newX, startingPoint.Item2 + yMove);
                        if (_grid.IsSpaceAvailable(newTarget))
                        {
                            return newTarget;
                        }
                        i++;
                        newX = xMove + ((i / 2) * direction * (i % 1 == 1 ? -1 : 1));
                    }
                    // if we can't find a lateral move that works, start over with the main axis reduced by 1
                    yMove -= yMove > 0 ? 1 : -1;
                    yMoveSq = yMove * yMove;
                }
                //Debug.Log($"There is no place in the world for this move: {startingPoint.Item1}, {startingPoint.Item2}->{targetPoint.Item1},{targetPoint.Item2}, {speed}");
                return startingPoint;
            }
        }

        private float CalculateExpectedDamage(RangedWeapon weapon, float range, float armor, float con)
        {
            float effectiveStrength = BattleModifiersUtil.CalculateDamageAtRange(weapon, range);
            float effectiveArmor = armor * weapon.Template.ArmorMultiplier;
            float penetratingDamage = Math.Max(0, (effectiveStrength * 4.25f) - effectiveArmor);
            return con <= 0
                ? 1
                : (penetratingDamage * weapon.Template.WoundMultiplier) / con;
        }
    }
}
