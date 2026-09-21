namespace OnlyWar.Battles;

/// <summary>
/// Immutable activity evidence for one pursuer/quarry squad pairing in the current turn.
///
/// <para>The speed values are the pair's declared/actual turn speeds, not force-wide capability
/// maxima. Every contact decision must be made from these pair-local facts so unrelated pursuer
/// and quarry capabilities cannot be combined.</para>
/// </summary>
public readonly record struct PursuitPairActivity(
    int PursuerSquadId,
    int QuarrySquadId,
    float CurrentSeparation,
    float PursuerDeclaredSpeed,
    float QuarryWithdrawalSpeed,
    bool PairAttackedRecently,
    bool FireCycleProgressedThisTurn,
    bool FireCommitmentRemainsViable)
{
    /// <summary>The pair's current relative closing rate.</summary>
    public float ClosingSpeed => PursuerDeclaredSpeed - QuarryWithdrawalSpeed;

    /// <summary>
    /// Whether the pursuer has a meaningful positive speed advantage over this quarry. The shared
    /// tolerance prevents a trivial speed edge from being treated as a real chase.
    /// </summary>
    public bool HasMeaningfulPositiveClosingSpeed =>
        ClosingSpeed > BattleContactRules.PursuitSpeedAdvantageTolerance;

    /// <summary>
    /// Whether this pair made progress on a still-viable fire commitment this turn.
    /// </summary>
    public bool HasQualifyingFireCycleProgress =>
        FireCycleProgressedThisTurn && FireCommitmentRemainsViable;

    /// <summary>
    /// Compact diagnostic code for this pair. The codes intentionally describe only the evidence
    /// used by the contact invariant; they do not serialize soldier state or weapon details.
    /// Multiple codes are retained because a pair may be closing while it is also firing.
    /// </summary>
    public string EvidenceReasonCode
    {
        get
        {
            string code = null;
            if (HasMeaningfulPositiveClosingSpeed) code = AppendCode(code, "close");
            if (PairAttackedRecently) code = AppendCode(code, "attack");
            if (HasQualifyingFireCycleProgress) code = AppendCode(code, "fire");
            if (CanReachContactThisTurn) code = AppendCode(code, "reach");
            return code ?? "none";
        }
    }

    /// <summary>
    /// Whether this assigned pair can collide with melee contact on this turn. This is kept as a
    /// separate escape hatch from active-pursuit evidence: a pair that is already at contact, or
    /// can reach it in the current movement pass, must not be allowed to escape merely because it
    /// did not also fire or make a meaningful multi-turn speed gain.
    /// </summary>
    public bool CanReachContactThisTurn =>
        BattleContactRules.CanReachContactThisTurn(
            CurrentSeparation,
            PursuerDeclaredSpeed,
            QuarryWithdrawalSpeed);

    /// <summary>
    /// Whether this concrete pair currently supplies any active-pursuit evidence.
    /// </summary>
    public bool HasActivePursuitEvidence =>
        HasMeaningfulPositiveClosingSpeed
        || PairAttackedRecently
        || HasQualifyingFireCycleProgress;

    private static string AppendCode(string current, string next) =>
        current == null ? next : current + "+" + next;
}
