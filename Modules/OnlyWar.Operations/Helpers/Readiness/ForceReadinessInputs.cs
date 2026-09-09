using OnlyWar.Domain;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Operations.Readiness;

/// <summary>
/// Resolves the doctrine and reservation facts an Operations query needs against the force named by
/// the caller. This is Operations composition policy, not part of Medical's fact-only boundary.
/// </summary>
public static class ForceReadinessInputs
{
    /// <summary>The force whose personnel policy governs this squad, or null if it is not theirs.</summary>
    public static PlayerForce OwnerOf(PlayerForce force, Squad squad) =>
        squad?.Faction != null && ReferenceEquals(squad.Faction, force?.Faction) ? force : null;

    /// <summary>An explicitly supplied doctrine always wins; otherwise the owning force's.</summary>
    public static ChapterOperationalDoctrine DoctrineFor(
        PlayerForce force, Squad squad, ChapterOperationalDoctrine doctrine = null) =>
        doctrine ?? OwnerOf(force, squad)?.Army?.ChapterOperationalDoctrine;

    /// <summary>An explicitly supplied program always wins; otherwise the owning force's.</summary>
    public static RecruitmentProgram ProgramFor(
        PlayerForce force, Squad squad, RecruitmentProgram program = null) =>
        program ?? OwnerOf(force, squad)?.RecruitmentProgram;
}
