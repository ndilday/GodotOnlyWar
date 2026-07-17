using OnlyWar.Models.Orders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Battles
{
    public enum BattleSide
    {
        Attacker = 0,
        Opposing = 1
    }

    public enum BattleSideIntent
    {
        Engaged = 0,
        FightingWithdrawal = 1,
        RearGuardWithdrawal = 2,
        Pursuing = 3,
        Rout = 4,
        Disengaged = 5
    }

    public enum BattleRole
    {
        Attacker = 0,
        Defender = 1,
        Ambusher = 2,
        Ambushed = 3
    }

    public enum BattleSquadStatus
    {
        Active = 0,
        Disengaged = 1,
        Eliminated = 2
    }

    public enum WithdrawalRole
    {
        None = 0,
        Cover = 1,
        Bound = 2,
        RearGuard = 3,
        Routing = 4
    }

    // Squad-level morale outcome from the per-turn check (Design/Active/MoraleAndRout.md §6).
    // Steady/Shaken are recomputed statelessly every turn and may flicker (non-sticky, §6);
    // Routing is sticky and is also reflected in WithdrawalRole.Routing so the withdrawal /
    // pursuit / aftermath machinery keyed on that role picks it up.
    public enum MoraleState
    {
        Steady = 0,
        Shaken = 1,
        Routing = 2
    }

    public sealed class BattleSideProfile
    {
        public Aggression Aggression { get; }
        public BattleRole BattleRole { get; }

        public BattleSideProfile(Aggression aggression, BattleRole battleRole)
        {
            Aggression = aggression;
            BattleRole = battleRole;
        }
    }

    public sealed class BattleSideState
    {
        public BattleSideIntent Intent { get; set; }
        public Aggression Aggression { get; }
        public BattleRole BattleRole { get; }
        public int StartingBattleValue { get; }
        public int StartingSoldierCount { get; }
        public ushort? WithdrawalHeading { get; set; }
        public int? CoveringSquadId { get; set; }
        public int? RearGuardSquadId { get; set; }
        public int? WithdrawalStartedTurn { get; set; }

        public BattleSideState(BattleSideProfile profile, int startingBattleValue, int startingSoldierCount)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            Intent = BattleSideIntent.Engaged;
            Aggression = profile.Aggression;
            BattleRole = profile.BattleRole;
            StartingBattleValue = startingBattleValue;
            StartingSoldierCount = startingSoldierCount;
        }

        public BattleSideState(BattleSideState original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));

            Intent = original.Intent;
            Aggression = original.Aggression;
            BattleRole = original.BattleRole;
            StartingBattleValue = original.StartingBattleValue;
            StartingSoldierCount = original.StartingSoldierCount;
            WithdrawalHeading = original.WithdrawalHeading;
            CoveringSquadId = original.CoveringSquadId;
            RearGuardSquadId = original.RearGuardSquadId;
            WithdrawalStartedTurn = original.WithdrawalStartedTurn;
        }
    }

    public enum BattleEndReason
    {
        Annihilation = 0,
        Withdrawal = 1,
        Rout = 2,
        MutualDisengagement = 3,
        TurnCap = 4
    }

    public sealed class BattleOutcome
    {
        public BattleEndReason EndReason { get; }
        public BattleSide? SideHoldingField { get; }
        public IReadOnlyList<int> DisengagedSquadIds { get; }
        public IReadOnlyList<int> EliminatedSquadIds { get; }
        public IReadOnlyList<int> RoutingSquadIds { get; }
        public IReadOnlyList<int> RearGuardSquadIds { get; }

        public BattleOutcome(
            BattleEndReason endReason,
            BattleSide? sideHoldingField,
            IEnumerable<int> disengagedSquadIds = null,
            IEnumerable<int> eliminatedSquadIds = null,
            IEnumerable<int> routingSquadIds = null,
            IEnumerable<int> rearGuardSquadIds = null)
        {
            EndReason = endReason;
            SideHoldingField = sideHoldingField;
            DisengagedSquadIds = CopyIds(disengagedSquadIds);
            EliminatedSquadIds = CopyIds(eliminatedSquadIds);
            RoutingSquadIds = CopyIds(routingSquadIds);
            RearGuardSquadIds = CopyIds(rearGuardSquadIds);
        }

        private static IReadOnlyList<int> CopyIds(IEnumerable<int> ids) =>
            (ids ?? Enumerable.Empty<int>()).Distinct().OrderBy(id => id).ToArray();
    }

    public enum BattleEventType
    {
        WithdrawalOrdered = 0,
        CoverAssigned = 1,
        RearGuardAssigned = 2,
        PursuitStarted = 3,
        PursuitEnded = 4,
        SquadDisengaged = 5,
        SquadRouted = 6,
        ForceDisengaged = 7
    }

    public sealed class BattleEvent
    {
        public BattleEventType Type { get; }
        public int TurnNumber { get; }
        public BattleSide Side { get; }
        public int? PrimarySquadId { get; }
        public IReadOnlyList<int> RelatedSquadIds { get; }
        public string Description { get; }

        public BattleEvent(
            BattleEventType type,
            int turnNumber,
            BattleSide side,
            int? primarySquadId,
            IEnumerable<int> relatedSquadIds,
            string description)
        {
            Type = type;
            TurnNumber = turnNumber;
            Side = side;
            PrimarySquadId = primarySquadId;
            RelatedSquadIds = (relatedSquadIds ?? Enumerable.Empty<int>()).ToArray();
            Description = description ?? string.Empty;
        }
    }
}
