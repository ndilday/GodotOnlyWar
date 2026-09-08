using OnlyWar.Medical.Abstractions;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Operations.Contracts;

/// <summary>
/// The campaign an order command resolves against. Order lifecycle mutations register and retire
/// orders on one specific sector and stamp postings with one specific campaign date; supplying both
/// explicitly is what lets the same policy run for a candidate campaign during generation warm-up
/// and for an isolated test session (SB-05a).
/// </summary>
public sealed record OrderCommandContext(
    Sector Sector,
    Date Date,
    IReadinessDecisions Readiness = null,
    IPersonnelAvailabilityQueries Personnel = null)
{
    /// <summary>
    /// The readiness capability this command evaluates participants with (SB-05b-1). Order policy
    /// consumes readiness decisions rather than the Medical policy that produces them, so the
    /// implementation arrives here from composition. A command issued without one cannot honestly
    /// answer "may this formation deploy", so it is a construction error rather than a default.
    /// </summary>
    public IReadinessDecisions RequireReadiness() =>
        Readiness ?? throw new System.InvalidOperationException(
            "OrderCommandContext was built without a readiness capability; supply one from composition.");

    /// <summary>Reservation facts read while validating an issue; null when there is no player force.</summary>
    public RecruitmentProgram Recruitment => Sector?.PlayerForce?.RecruitmentProgram;

    public Faction PlayerFaction => Sector?.PlayerForce?.Faction;

    /// <summary>
    /// The force whose doctrine and reservations govern this command's participants. Pass it to
    /// <c>ForceReadinessInputs</c> rather than resolving readiness from the installed campaign.
    /// </summary>
    public PlayerForce Force => Sector?.PlayerForce;

    /// <summary>The date postings created by this command are stamped with.</summary>
    public Date PostingDate => Date ?? new Date(1);
}
