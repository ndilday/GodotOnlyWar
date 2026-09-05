using OnlyWar.Models;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Contracts.Operations;

/// <summary>
/// The campaign an order command resolves against. Order lifecycle mutations register and retire
/// orders on one specific sector and stamp postings with one specific campaign date; supplying both
/// explicitly is what lets the same policy run for a candidate campaign during generation warm-up
/// and for an isolated test session (SB-05a).
/// </summary>
public sealed record OrderCommandContext(Sector Sector, Date Date)
{
    /// <summary>Reservation facts read while validating an issue; null when there is no player force.</summary>
    public RecruitmentProgram Recruitment => Sector?.PlayerForce?.RecruitmentProgram;

    public Faction PlayerFaction => Sector?.PlayerForce?.Faction;

    /// <summary>The date postings created by this command are stamped with.</summary>
    public Date PostingDate => Date ?? new Date(1);
}
