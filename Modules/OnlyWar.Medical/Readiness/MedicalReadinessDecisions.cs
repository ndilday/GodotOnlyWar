using System.Collections.Generic;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Readiness;

/// <summary>
/// Medical's implementation of the readiness capability Operations consumes (SB-05b-1). It is a
/// pure adapter over the same <see cref="DutyReadinessService"/>/<see cref="SquadReadinessService"/>
/// policy the rest of the game uses, so there is one readiness rule and one result vocabulary --
/// the interface exists to reverse the reference direction, not to introduce a second policy.
/// </summary>
public sealed class MedicalReadinessDecisions : IReadinessDecisions
{
    /// <summary>
    /// Shared because the policy is stateless. Composition may still construct its own instance.
    /// </summary>
    public static MedicalReadinessDecisions Instance { get; } = new();

    public DutyReadinessEvaluation EvaluateSoldier(
        ISoldier soldier,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        DutyReadinessService.Evaluate(soldier, doctrine, program);

    public IReadOnlyList<ISoldier> GetDutyReadyMembers(
        Squad squad,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        DutyReadinessService.GetDutyReadyMembers(squad, doctrine, program);

    public SquadReadinessSnapshot EvaluateSquad(
        Squad squad,
        SquadDeploymentContext context = null,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null) =>
        SquadReadinessService.Evaluate(squad, context, program, doctrine);

    public bool CanBeginNewDeployment(
        Squad squad,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null) =>
        SquadReadinessService.CanBeginNewDeployment(squad, program, doctrine);
}
