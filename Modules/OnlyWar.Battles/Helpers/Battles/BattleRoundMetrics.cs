using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Battle-scoped owner of the recent round histories used by force-level decisions and of the
    /// force metrics derived from the live battle state.
    ///
    /// <para>The queues are deliberately kept here rather than on the withdrawal service. Morale,
    /// continuation, pursuit, diagnostics, and future planning all read the same post-round
    /// measurements, while the service itself remains responsible only for withdrawal lifecycle
    /// state.</para>
    /// </summary>
    internal sealed class BattleRoundMetrics
    {
        private const int RecentActionWindowRounds = 2;

        private readonly BattleState _state;
        private readonly Dictionary<BattleSide, Queue<int>> _battleValueHistory = [];
        private readonly Dictionary<BattleSide, Queue<bool>> _damageActionHistory = [];
        private readonly Dictionary<int, int> _squadIdBySoldierId = [];
        private readonly Queue<HashSet<SquadPair>> _attackPairHistory = [];
        private readonly HashSet<int> _pendingDamagingActorIds = [];
        private HashSet<SquadPair> _pendingAttackPairs = [];
        private HashSet<SquadPair> _pendingAimProgressPairs = [];
        private HashSet<RangedPreparationProgress> _pendingRangedPreparationProgress = [];
        private HashSet<SquadPair> _currentRoundAimProgressPairs = [];
        private HashSet<RangedPreparationProgress> _currentRoundRangedPreparationProgress = [];

        internal BattleRoundMetrics(BattleState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _battleValueHistory[BattleSide.Attacker] =
                new Queue<int>([_state.AttackerSide.StartingBattleValue]);
            _battleValueHistory[BattleSide.Opposing] =
                new Queue<int>([_state.OpposingSide.StartingBattleValue]);
            _damageActionHistory[BattleSide.Attacker] = new Queue<bool>();
            _damageActionHistory[BattleSide.Opposing] = new Queue<bool>();

            foreach (BattleSquad squad in _state.AllAttackerSquads.Values
                .Concat(_state.AllOpposingSquads.Values))
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _squadIdBySoldierId[soldier.Soldier.Id] = squad.Id;
                }
            }
        }

        /// <summary>
        /// Captures facts from actions that have already executed in the current round. This is a
        /// separate step because the resolver removes casualties before it commits the post-round
        /// force metrics, while an executed action may have targeted one of those casualties.
        /// </summary>
        internal void RecordExecutedActions(IReadOnlyCollection<IAction> executedActions)
        {
            ArgumentNullException.ThrowIfNull(executedActions);

            _pendingDamagingActorIds.Clear();
            _pendingAttackPairs = [];
            _pendingAimProgressPairs = [];
            _pendingRangedPreparationProgress = [];
            foreach (IAction action in executedActions)
            {
                if (IsAttackAction(action))
                {
                    _pendingDamagingActorIds.Add(action.ActorId);
                }

                if (!TryGetSquadId(action.ActorId, out int actorSquadId))
                {
                    continue;
                }

                if (action is ReadyRangedWeaponAction readyAction && readyAction.Succeeded)
                {
                    _pendingRangedPreparationProgress.Add(new(
                        action.ActorId,
                        readyAction.Weapon));
                }
                else if (action is ReloadRangedWeaponAction reloadAction
                    && reloadAction.Succeeded)
                {
                    _pendingRangedPreparationProgress.Add(new(
                        action.ActorId,
                        reloadAction.Weapon));
                }

                if (action is AimAction aimAction)
                {
                    if (TryGetSquadId(aimAction.TargetId, out int aimTargetSquadId))
                    {
                        _pendingAimProgressPairs.Add(new(actorSquadId, aimTargetSquadId));
                    }

                    continue;
                }

                if (!IsAttackAction(action))
                {
                    continue;
                }

                foreach (int targetId in GetAttackTargetIds(action))
                {
                    if (TryGetSquadId(targetId, out int targetSquadId))
                    {
                        _pendingAttackPairs.Add(new(actorSquadId, targetSquadId));
                    }
                }
            }
        }

        /// <summary>
        /// Records the post-cleanup state and the supplied actions as the completed round. The
        /// resolver uses <see cref="RecordExecutedActions"/> before cleanup and the parameterless
        /// overload after cleanup so both action facts and legacy force metrics retain their
        /// correct state boundary.
        /// </summary>
        internal void RecordRound(IReadOnlyCollection<IAction> executedActions)
        {
            ArgumentNullException.ThrowIfNull(executedActions);
            RecordExecutedActions(executedActions);
            RecordRound();
        }

        /// <summary>
        /// Commits the post-cleanup force metrics and the action facts captured for the round.
        /// </summary>
        internal void RecordRound()
        {
            foreach (BattleSide side in Enum.GetValues<BattleSide>())
            {
                Queue<int> values = _battleValueHistory[side];
                values.Enqueue(CurrentBattleValue(side));
                while (values.Count > 3) values.Dequeue();

                bool usedDamagingAction = _pendingDamagingActorIds.Any(actorId =>
                    IsSoldierOnSide(actorId, side));
                Queue<bool> damage = _damageActionHistory[side];
                damage.Enqueue(usedDamagingAction);
                while (damage.Count > RecentActionWindowRounds) damage.Dequeue();
            }

            _attackPairHistory.Enqueue(_pendingAttackPairs);
            while (_attackPairHistory.Count > RecentActionWindowRounds)
            {
                _attackPairHistory.Dequeue();
            }

            _currentRoundAimProgressPairs = _pendingAimProgressPairs;
            _currentRoundRangedPreparationProgress = _pendingRangedPreparationProgress;
            _pendingDamagingActorIds.Clear();
            _pendingAttackPairs = [];
            _pendingAimProgressPairs = [];
            _pendingRangedPreparationProgress = [];
        }

        /// <summary>Whether this pursuer/quarry pair attacked in the recent action window.</summary>
        internal bool HasPairAttackedRecently(int pursuerSquadId, int quarrySquadId) =>
            _attackPairHistory.Any(round => round.Contains(new(pursuerSquadId, quarrySquadId)));

        /// <summary>
        /// Whether an AimAction executed this round that started or advanced aim for this pair.
        /// Retained aim state alone never enters this set.
        /// </summary>
        internal bool HasPairFireCycleProgressedThisRound(int pursuerSquadId, int quarrySquadId) =>
            _currentRoundAimProgressPairs.Contains(new(pursuerSquadId, quarrySquadId));

        /// <summary>
        /// Returns the successful ranged preparation actions executed by one squad this round.
        /// The withdrawal service supplies the assigned quarry and Hold-policy checks; this class
        /// records only the executed action and the exact weapon it changed.
        /// </summary>
        internal IReadOnlyCollection<RangedPreparationProgress>
            GetRangedPreparationProgressThisRound(int squadId) =>
            _currentRoundRangedPreparationProgress
                .Where(progress => _squadIdBySoldierId.GetValueOrDefault(progress.SoldierId) == squadId)
                .ToList();

        private bool TryGetSquadId(int soldierId, out int squadId) =>
            _squadIdBySoldierId.TryGetValue(soldierId, out squadId);

        private static bool IsAttackAction(IAction action) => action is
            ShootAction or AreaAttackAction or BlastAttackAction or MeleeAttackAction;

        private static IEnumerable<int> GetAttackTargetIds(IAction action) => action switch
        {
            ShootAction shootAction => [shootAction.TargetId],
            AreaAttackAction areaAttackAction => [areaAttackAction.TargetId],
            BlastAttackAction blastAttackAction => [blastAttackAction.TargetId],
            MeleeAttackAction meleeAttackAction => meleeAttackAction.TargetedDefenderIds,
            _ => []
        };

        private readonly record struct SquadPair(int PursuerSquadId, int QuarrySquadId);

        internal readonly record struct RangedPreparationProgress(
            int SoldierId,
            RangedWeapon Weapon);

        /// <summary>Builds a force snapshot from the current active squads.</summary>
        internal BattleForceMetrics BuildMetrics(BattleSide side)
        {
            List<BattleSquad> squads = GetActiveSquads(side).ToList();
            List<BattleSoldier> soldiers = squads.SelectMany(squad => squad.AbleSoldiers).ToList();
            int current = soldiers.Sum(soldier => soldier.EffectiveBattleValue);
            Queue<int> history = _battleValueHistory[side];
            int prior = history.Count > 0 ? history.Peek() : current;
            float fastest = squads.Select(SafeSquadMove).DefaultIfEmpty(0).Max();
            float slowest = squads.Select(SafeSquadMove).DefaultIfEmpty(0).Min();
            int cover = squads.Count(squad => !squad.IsInMelee
                && squad.AbleSoldiers.Any(soldier => soldier.EquippedRangedWeapons.Count > 0));
            return new BattleForceMetrics(
                GetSideState(side).StartingBattleValue,
                current,
                Math.Max(0, prior - current),
                soldiers.Count,
                fastest,
                slowest,
                cover,
                squads.Any(squad => squad.IsInMelee),
                _damageActionHistory[side].Any(value => value),
                soldiers.Count > 0);
        }

        internal int CurrentBattleValue(BattleSide side) => GetActiveSquads(side)
            .Sum(CurrentBattleValue);

        private IReadOnlyCollection<BattleSquad> GetActiveSquads(BattleSide side) =>
            side == BattleSide.Attacker
                ? _state.ActiveAttackerSquads.Values.ToList()
                : _state.ActiveOpposingSquads.Values.ToList();

        private IReadOnlyCollection<BattleSquad> GetAllSquads(BattleSide side) =>
            side == BattleSide.Attacker
                ? _state.AllAttackerSquads.Values.ToList()
                : _state.AllOpposingSquads.Values.ToList();

        private BattleSideState GetSideState(BattleSide side) =>
            side == BattleSide.Attacker ? _state.AttackerSide : _state.OpposingSide;

        private bool IsSoldierOnSide(int soldierId, BattleSide side) => GetAllSquads(side)
            .SelectMany(squad => squad.Soldiers)
            .Any(soldier => soldier.Soldier.Id == soldierId);

        private static int CurrentBattleValue(BattleSquad squad) => squad.AbleSoldiers
            .Sum(soldier => soldier.EffectiveBattleValue);

        private static float SafeSquadMove(BattleSquad squad) =>
            squad.AbleSoldiers.Count == 0 ? 0 : squad.GetSquadMove();
    }
}
