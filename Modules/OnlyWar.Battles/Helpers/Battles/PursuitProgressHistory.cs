using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles;

/// <summary>
/// Calibration for the observed pursuit-evidence window. The values are intentionally small and
/// are validated by the contact-lifecycle regressions rather than by a battle-duration heuristic.
/// </summary>
internal static class PursuitProgressPolicy
{
    /// <summary>
    /// Four completed movement samples retain one productive sample through a short obstruction,
    /// while sustained blocking ages it out on the next window.
    /// </summary>
    internal const int HistoryLength = 4;

    /// <summary>
    /// Net nearest-soldier separation must improve by more than half a cell over the retained
    /// window. This absorbs one-cell grid rounding without treating a stationary pair as closing.
    /// </summary>
    internal const float ProgressTolerance = 0.5f;

    /// <summary>
    /// A new assignment may complete two observed turns without progress before it is judged
    /// stalled. This is enough to expose a first grid move and a brief obstruction, but bounded.
    /// </summary>
    internal const int StartupGraceTurns = 2;

    /// <summary>
    /// A pursuer may not obtain a fresh startup window forever by rotating through unproductive
    /// quarry assignments. Productive progress resets this counter for a legitimate retarget.
    /// </summary>
    internal const int MaxUnproductiveAssignmentSwitches = 2;
}

/// <summary>One completed pre-action/post-movement observation for an assigned pair.</summary>
internal readonly record struct PursuitProgressSample(
    int TurnNumber,
    float SeparationBefore,
    float SeparationAfter,
    float ActualPursuerDisplacement,
    float ActualQuarryDisplacement,
    float PlannedPursuerDisplacement,
    float PlannedQuarryDisplacement,
    int SuccessfulMoveCount,
    int FailedMoveCount)
{
    internal float SeparationGain => SeparationBefore - SeparationAfter;
}

/// <summary>Mutable battle-lifetime history for one concrete squad pair.</summary>
internal sealed class PursuitPairProgressHistory
{
    internal int PursuerSquadId { get; }
    internal int QuarrySquadId { get; }
    internal int AssignmentStartTurn { get; set; }
    internal bool StartupAllowed { get; set; }
    internal HashSet<int> PursuerMemberIds { get; private set; } = [];
    internal HashSet<int> QuarryMemberIds { get; private set; } = [];
    internal Queue<PursuitProgressSample> Samples { get; } = [];
    internal string LastSampleValidityReason { get; private set; } = "not_sampled";
    internal string LastResetReason { get; private set; } = "new_assignment";

    internal PursuitPairProgressHistory(
        int pursuerSquadId,
        int quarrySquadId,
        BattleSquad pursuer,
        BattleSquad quarry,
        int assignmentStartTurn,
        bool startupAllowed)
    {
        PursuerSquadId = pursuerSquadId;
        QuarrySquadId = quarrySquadId;
        AssignmentStartTurn = assignmentStartTurn;
        StartupAllowed = startupAllowed;
        SetMembership(pursuer, quarry);
    }

    internal bool MembershipMatches(BattleSquad pursuer, BattleSquad quarry) =>
        PursuerMemberIds.SetEquals(MemberIds(pursuer))
        && QuarryMemberIds.SetEquals(MemberIds(quarry));

    internal void SetMembership(BattleSquad pursuer, BattleSquad quarry)
    {
        PursuerMemberIds = MemberIds(pursuer);
        QuarryMemberIds = MemberIds(quarry);
    }

    internal void ResetForMembership(
        BattleSquad pursuer,
        BattleSquad quarry,
        int assignmentStartTurn,
        string resetReason = "membership_changed")
    {
        Samples.Clear();
        SetMembership(pursuer, quarry);
        AssignmentStartTurn = assignmentStartTurn;
        LastSampleValidityReason = resetReason;
        LastResetReason = resetReason;
    }

    internal void Add(PursuitProgressSample sample)
    {
        Samples.Enqueue(sample);
        while (Samples.Count > PursuitProgressPolicy.HistoryLength)
        {
            Samples.Dequeue();
        }
        LastSampleValidityReason = "valid";
        LastResetReason = "none";
    }

    internal float RollingSeparationGain
    {
        get
        {
            if (Samples.Count == 0) return 0;
            PursuitProgressSample first = Samples.Peek();
            PursuitProgressSample last = Samples.Last();
            return first.SeparationBefore - last.SeparationAfter;
        }
    }

    internal bool HasObservedClosingProgress =>
        Samples.Count > 0
        && IsFinite(RollingSeparationGain)
        && RollingSeparationGain > PursuitProgressPolicy.ProgressTolerance;

    internal PursuitProgressSample? LatestSample =>
        Samples.Count == 0 ? null : Samples.Last();

    internal bool HasStartupGrace =>
        StartupAllowed && Samples.Count < PursuitProgressPolicy.StartupGraceTurns;

    internal bool LastSampleWasValid => LastSampleValidityReason == "valid";

    private static HashSet<int> MemberIds(BattleSquad squad) =>
        (squad?.AbleSoldiers ?? [])
            .Select(soldier => soldier.Soldier.Id)
            .ToHashSet();

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
