using System;
using System.Collections.Generic;
using OnlyWar.Abstractions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;

namespace OnlyWar.Operations.Abstractions;

/// <summary>Campaign facts selected by the caller for mission execution.</summary>
public sealed record MissionCampaignInputs(
    Date Date,
    ChapterOperationalDoctrine Doctrine = null,
    RecruitmentProgram Recruitment = null,
    IReadOnlyList<StrategicInvasionForce> InvasionForces = null,
    FactionBehaviorRulesProfile FactionRules = null,
    IBattleEquipmentSource Equipment = null,
    IReadinessDecisions Readiness = null,
    IPhysicalPostingCommands Personnel = null,
    Func<StrategicInvasionForce, Region, float, IRNG, FactionBehaviorRulesProfile, bool> StrategicCommanderCanBeReached = null)
{
    public IReadOnlyList<StrategicInvasionForce> PhysicalForces =>
        InvasionForces ?? Array.Empty<StrategicInvasionForce>();
}
