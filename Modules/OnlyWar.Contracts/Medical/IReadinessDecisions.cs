using System.Collections.Generic;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Readiness;

/// <summary>
/// The readiness questions an order/mission caller is allowed to ask. Readiness policy belongs to
/// Medical (§3.1); Operations consumes the decisions in <c>ReadinessDecisions.cs</c> through this
/// capability rather than calling the policy implementation, which is what lets order and mission
/// code stop referencing a service it does not own (SB-05b-1).
///
/// The doctrine and reservation arguments are the already-resolved facts. Resolve them with
/// <see cref="ForceReadinessInputs"/> against a named force before calling; nothing here consults
/// an installed campaign.
/// </summary>
public interface IReadinessDecisions
{
    /// <summary>Is this soldier fit to be committed to the field?</summary>
    DutyReadinessEvaluation EvaluateSoldier(
        ISoldier soldier,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    /// <summary>The members of a formation who may be committed to the field.</summary>
    IReadOnlyList<ISoldier> GetDutyReadyMembers(
        Squad squad,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    /// <summary>Full structural/contextual readiness for a formation.</summary>
    SquadReadinessSnapshot EvaluateSquad(
        Squad squad,
        SquadDeploymentContext context = null,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null);

    /// <summary>May this formation be newly committed to an order?</summary>
    bool CanBeginNewDeployment(
        Squad squad,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null);
}
