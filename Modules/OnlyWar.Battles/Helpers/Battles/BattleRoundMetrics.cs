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
        // Per side: each squad's starting able headcount and its weight, the squad's share of its
        // OWN side's starting battle value. Weights are within-side only, so force shares never
        // compare battle value across sides and are unchanged by any rescaling of it.
        private readonly Dictionary<BattleSide, Dictionary<int, SquadStart>> _squadStarts = [];
        // Per side: the counted able headcount of every starting squad after each of the last
        // rounds (oldest first), seeded with the starting headcounts.
        private readonly Dictionary<BattleSide, Queue<Dictionary<int, int>>> _ableCountHistory = [];
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
            foreach (BattleSide side in Enum.GetValues<BattleSide>())
            {
                Dictionary<int, SquadStart> starts = BuildSquadStarts(GetAllSquads(side));
                _squadStarts[side] = starts;
                _ableCountHistory[side] = new Queue<Dictionary<int, int>>(
                    [starts.Values.ToDictionary(start => start.SquadId, start => start.AbleSoldiers)]);
            }
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
                Queue<Dictionary<int, int>> counts = _ableCountHistory[side];
                counts.Enqueue(CountedAbleSoldiers(side));
                while (counts.Count > 3) counts.Dequeue();

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

            // Headcount shares. Each starting squad contributes w * able / starting, where w is
            // its share of its OWN side's starting battle value. A squad that stood down from a
            // pursuit left the field on its own terms, not as a casualty, so it still counts at
            // the headcount it stood down with (see BattleSideState.StoodDownSquadIds).
            Dictionary<int, int> counted = CountedAbleSoldiers(side);
            Dictionary<int, int> prior = _ableCountHistory[side].Peek();
            double remaining = 0;
            double lostPreviousTwoRounds = 0;
            foreach (SquadStart start in _squadStarts[side].Values)
            {
                int now = counted.GetValueOrDefault(start.SquadId);
                int before = prior.GetValueOrDefault(start.SquadId, now);
                remaining += start.Share(now);
                lostPreviousTwoRounds += start.Share(Math.Max(0, before - now));
            }

            float fastest = squads.Select(SafeSquadMove).DefaultIfEmpty(0).Max();
            float slowest = squads.Select(SafeSquadMove).DefaultIfEmpty(0).Min();
            int cover = squads.Count(squad => !squad.IsInMelee
                && squad.AbleSoldiers.Any(soldier => soldier.EquippedRangedWeapons.Count > 0));
            return new BattleForceMetrics(
                remaining,
                lostPreviousTwoRounds,
                soldiers.Count,
                fastest,
                slowest,
                cover,
                squads.Any(squad => squad.IsInMelee),
                _damageActionHistory[side].Any(value => value),
                soldiers.Count > 0);
        }

        /// <summary>
        /// The squad's contribution to its side's force share when <paramref name="ableCount"/>
        /// of its soldiers are able: w * able / starting. Zero for a squad that began the battle
        /// with no able soldiers.
        /// </summary>
        internal double SquadShare(BattleSide side, BattleSquad squad, int ableCount) =>
            _squadStarts[side].TryGetValue(squad.Id, out SquadStart start)
                ? start.Share(ableCount)
                : 0;

        /// <summary>
        /// Counted able headcount per starting squad: active squads at their live count, squads
        /// that stood down at the count they stood down with, everyone else at zero.
        /// </summary>
        private Dictionary<int, int> CountedAbleSoldiers(BattleSide side)
        {
            IReadOnlyDictionary<int, BattleSquad> active = side == BattleSide.Attacker
                ? _state.ActiveAttackerSquads
                : _state.ActiveOpposingSquads;
            BattleSideState sideState = GetSideState(side);
            Dictionary<int, int> counted = [];
            foreach (int squadId in _squadStarts[side].Keys)
            {
                counted[squadId] = active.TryGetValue(squadId, out BattleSquad squad)
                    ? squad.AbleSoldiers.Count
                    : sideState.StoodDownAbleCounts.GetValueOrDefault(squadId);
            }
            return counted;
        }

        private static Dictionary<int, SquadStart> BuildSquadStarts(
            IReadOnlyCollection<BattleSquad> squads)
        {
            List<(int Id, int Able, long BattleValue)> raw = squads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .Select(squad => (
                    squad.Id,
                    squad.AbleSoldiers.Count,
                    squad.AbleSoldiers.Sum(soldier => (long)soldier.EffectiveBattleValue)))
                .ToList();
            long totalBattleValue = raw.Sum(item => item.BattleValue);
            long totalAble = raw.Sum(item => (long)item.Able);
            // A side whose battle value is all zero has no within-side proportion to take, so
            // fall back to headcount weights rather than dividing by zero.
            return raw.ToDictionary(
                item => item.Id,
                item => new SquadStart(
                    item.Id,
                    item.Able,
                    totalBattleValue > 0
                        ? (double)item.BattleValue / totalBattleValue
                        : (double)item.Able / totalAble));
        }

        private readonly record struct SquadStart(int SquadId, int AbleSoldiers, double Weight)
        {
            internal double Share(int ableCount) =>
                Weight * Math.Min(Math.Max(ableCount, 0), AbleSoldiers) / AbleSoldiers;
        }

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

        private static float SafeSquadMove(BattleSquad squad) =>
            squad.AbleSoldiers.Count == 0 ? 0 : squad.GetSquadMove();
    }
}
