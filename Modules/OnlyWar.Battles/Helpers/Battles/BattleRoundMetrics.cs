using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
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
        private readonly BattleState _state;
        private readonly Dictionary<BattleSide, Queue<int>> _battleValueHistory = [];
        private readonly Dictionary<BattleSide, Queue<bool>> _damageActionHistory = [];

        internal BattleRoundMetrics(BattleState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _battleValueHistory[BattleSide.Attacker] =
                new Queue<int>([_state.AttackerSide.StartingBattleValue]);
            _battleValueHistory[BattleSide.Opposing] =
                new Queue<int>([_state.OpposingSide.StartingBattleValue]);
            _damageActionHistory[BattleSide.Attacker] = new Queue<bool>();
            _damageActionHistory[BattleSide.Opposing] = new Queue<bool>();
        }

        /// <summary>
        /// Records the post-cleanup state and the actions executed in the completed round. The
        /// resolver calls this at its existing phase boundary, after wounds and casualty removal.
        /// </summary>
        internal void RecordRound(IReadOnlyCollection<IAction> executedActions)
        {
            ArgumentNullException.ThrowIfNull(executedActions);
            foreach (BattleSide side in Enum.GetValues<BattleSide>())
            {
                Queue<int> values = _battleValueHistory[side];
                values.Enqueue(CurrentBattleValue(side));
                while (values.Count > 3) values.Dequeue();

                bool usedDamagingAction = executedActions.Any(action =>
                    IsSoldierOnSide(action.ActorId, side)
                    && action is ShootAction or AreaAttackAction or BlastAttackAction
                        or MeleeAttackAction);
                Queue<bool> damage = _damageActionHistory[side];
                damage.Enqueue(usedDamagingAction);
                while (damage.Count > 2) damage.Dequeue();
            }
        }

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
