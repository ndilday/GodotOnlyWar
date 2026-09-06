using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Readiness;

/// <summary>
/// Resolves the doctrine and reservation facts a readiness evaluation needs, against a force the
/// caller names. Doctrine and the recruitment pipeline belong to one player force, so they apply
/// only to that force's own squads — a squad of another faction gets neither, which is what keeps
/// an NPC formation from being judged against the Chapter's deployment doctrine.
///
/// This is the explicit-input form of the same predicate the transitional
/// <c>CurrentCampaignReadinessContext</c> adapter applies to the installed campaign; the adapter
/// now delegates here, so there is one rule rather than two (SB-05a).
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
