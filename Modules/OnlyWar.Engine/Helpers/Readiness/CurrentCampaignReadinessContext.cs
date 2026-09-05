using System;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Readiness;

/// <summary>
/// Transitional application adapter for callers without a session argument. Resolve at execution,
/// never cache a campaign. SB-04/10 replace its callers with explicit personnel inputs.
/// This adapter is not part of the readiness policy.
/// </summary>
public static class CurrentCampaignReadinessContext
{
    private static PlayerForce ForceFor(Squad squad)
    {
        var force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        return squad?.Faction != null && ReferenceEquals(squad.Faction, force?.Faction) ? force : null;
    }
    public static ChapterOperationalDoctrine ResolveDoctrine(Squad squad, ChapterOperationalDoctrine doctrine = null) =>
        doctrine ?? ForceFor(squad)?.Army?.ChapterOperationalDoctrine;
    public static RecruitmentProgram ResolveProgram(Squad squad, RecruitmentProgram program = null) =>
        program ?? ForceFor(squad)?.RecruitmentProgram;
}
