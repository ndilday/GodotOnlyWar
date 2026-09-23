namespace OnlyWar.Battles;

/// <summary>
/// Immutable activity evidence for one pursuer/quarry squad pairing in the current turn.
///
/// <para>The speed values remain pair-local diagnostics and are retained for the corrected
/// immediate-contact fallback and trace inspection. They are deliberately not pursuit evidence:
/// sustained pursuit is proved by the observed separation history supplied by the withdrawal
/// lifecycle.</para>
/// </summary>
public readonly record struct PursuitPairActivity(
    int PursuerSquadId,
    int QuarrySquadId,
    float CurrentSeparation,
    float PursuerDeclaredSpeed,
    float QuarryWithdrawalSpeed,
    bool PairAttackedRecently,
    bool FireCycleProgressedThisTurn,
    bool FireCommitmentRemainsViable,
    float ObservedSeparationGain = 0,
    bool HasObservedClosingProgress = false,
    bool HasStartupGrace = false,
    bool? ProjectedCanReachContactThisTurn = null,
    int ProgressHistorySamples = 0,
    bool ProgressHistoryValid = true,
    float ActualPursuerDisplacement = 0,
    float ActualQuarryDisplacement = 0,
    float PlannedPursuerDisplacement = 0,
    float PlannedQuarryDisplacement = 0,
    int FailedMoveCount = 0,
    int ProgressHistoryWindowLength = 4,
    string ProgressValidityReason = "not_sampled",
    string ProgressResetReason = "none",
    bool QuarryEliminated = false,
    float? EffectSeparation = null)
{
    /// <summary>The pair's current relative closing rate, retained for diagnostics only.</summary>
    public float ClosingSpeed => PursuerDeclaredSpeed - QuarryWithdrawalSpeed;

    /// <summary>
    /// Observed closing per turn over the retained history window: the rolling separation gain
    /// divided by the samples it spans.
    /// </summary>
    public float ObservedClosingRate =>
        ProgressHistorySamples > 0 ? ObservedSeparationGain / ProgressHistorySamples : 0;

    /// <summary>
    /// Turns until this pair reaches the separation at which it can act on its quarry
    /// (<see cref="EffectSeparation"/>), at its observed closing rate. Zero once it is there;
    /// infinite when it is not closing.
    /// </summary>
    public float TurnsToEffect =>
        EffectSeparation is not float target || CurrentSeparation <= target
            ? 0
            : ObservedClosingRate > 0
                ? (CurrentSeparation - target) / ObservedClosingRate
                : float.PositiveInfinity;

    /// <summary>
    /// Closing progress that will matter: observed progress that brings the pair to where it can
    /// act within <see cref="BattlePursuitPlanner.MaximumChaseTurns"/>, the same horizon a
    /// pressing squad uses to decide whether a chase is worth running. Any closing at all used to
    /// count, so a pursuer gaining half a cell a turn from 445 cells kept contact for hundreds of
    /// turns with no shot fired (Grist Nine Epsilon, 2026-09-22, to the 1000-turn cap). With no
    /// <see cref="EffectSeparation"/> supplied, any observed progress still counts.
    /// </summary>
    public bool HasTimelyClosingProgress =>
        HasObservedClosingProgress && TurnsToEffect <= BattlePursuitPlanner.MaximumChaseTurns;

    /// <summary>
    /// Compatibility name for the old scalar evidence field. It now means smoothed observed
    /// geometric progress; declared speed advantage is never sufficient on its own.
    /// </summary>
    public bool HasMeaningfulPositiveClosingSpeed => HasObservedClosingProgress;

    /// <summary>
    /// Whether this pair made progress on a still-viable fire commitment this turn.
    /// </summary>
    public bool HasQualifyingFireCycleProgress =>
        FireCycleProgressedThisTurn && FireCommitmentRemainsViable;

    /// <summary>
    /// Compact diagnostic code for this pair. The codes intentionally describe only the evidence
    /// used by the contact invariant; they do not serialize soldier state or weapon details.
    /// Multiple codes are retained because a pair may be progressing while it is also firing.
    /// </summary>
    public string EvidenceReasonCode
    {
        get
        {
            string code = null;
            if (HasTimelyClosingProgress) code = AppendCode(code, "progress");
            else if (HasObservedClosingProgress) code = AppendCode(code, "slow_progress");
            if (HasStartupGrace) code = AppendCode(code, "startup");
            if (PairAttackedRecently) code = AppendCode(code, "attack");
            if (HasQualifyingFireCycleProgress) code = AppendCode(code, "fire");
            if (CanReachContactThisTurn) code = AppendCode(code, "reach");
            if (QuarryEliminated) code = AppendCode(code, "eliminated");
            return code ?? "none";
        }
    }

    /// <summary>
    /// Whether this assigned pair can collide with melee contact on this turn. This is kept as a
    /// separate escape hatch from active-pursuit evidence: a pair that is already at contact, or
    /// can reach it in the current movement pass, must not be allowed to escape merely because it
    /// did not also fire or make a meaningful multi-turn geometric gain.
    /// </summary>
    public bool CanReachContactThisTurn =>
        ProjectedCanReachContactThisTurn
            ?? BattleContactRules.CanReachContactThisTurn(
                CurrentSeparation,
                PursuerDeclaredSpeed,
                QuarryWithdrawalSpeed);

    /// <summary>
    /// Whether this concrete pair currently supplies any active-pursuit evidence. A quarry that
    /// was eliminated during the pairing's own turn is the strongest evidence there is: the
    /// pursuit did not lose contact, it finished its target. Without this, a force whose every
    /// pursuer was assigned to one small squad read as a stalled pursuit the moment that squad
    /// died (observed 2026-09-22, Grist Nine Epsilon turn 57: 33 pursuers, 0 pairs).
    /// </summary>
    public bool HasActivePursuitEvidence =>
        HasTimelyClosingProgress
        || HasStartupGrace
        || PairAttackedRecently
        || HasQualifyingFireCycleProgress
        || QuarryEliminated;

    /// <summary>Whether this pair currently qualifies for contact maintenance by any evidence.</summary>
    public bool QualifiesAsContactMaintenanceEvidence => HasActivePursuitEvidence;

    private static string AppendCode(string current, string next) =>
        current == null ? next : current + "+" + next;
}
