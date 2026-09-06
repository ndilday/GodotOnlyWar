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
    // The rule itself lives in ForceReadinessInputs so explicit callers and this adapter cannot
    // drift apart; all this adds is "the force is whichever campaign is installed right now".
    private static PlayerForce CurrentForce => CampaignRuntimeDefaults.PlayerForce;

    public static ChapterOperationalDoctrine ResolveDoctrine(Squad squad, ChapterOperationalDoctrine doctrine = null) =>
        ForceReadinessInputs.DoctrineFor(CurrentForce, squad, doctrine);

    public static RecruitmentProgram ResolveProgram(Squad squad, RecruitmentProgram program = null) =>
        ForceReadinessInputs.ProgramFor(CurrentForce, squad, program);
}
