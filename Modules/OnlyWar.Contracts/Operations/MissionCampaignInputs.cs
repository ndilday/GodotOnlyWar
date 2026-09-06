using System;
using System.Collections.Generic;
using OnlyWar.Contracts.Battles;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Contracts.Operations;

/// <summary>Campaign facts selected by the caller for mission execution.</summary>
public sealed record MissionCampaignInputs(
    Date Date,
    ChapterOperationalDoctrine Doctrine = null,
    RecruitmentProgram Recruitment = null,
    IReadOnlyList<StrategicInvasionForce> InvasionForces = null,
    FactionBehaviorRulesProfile FactionRules = null,
    IBattleEquipmentSource Equipment = null,
    OnlyWar.Helpers.Readiness.IReadinessDecisions Readiness = null,
    IOperationsPersonnelSurface Personnel = null,
    Func<StrategicInvasionForce, Region, float, IRNG, FactionBehaviorRulesProfile, bool> StrategicCommanderCanBeReached = null)
{
    public IReadOnlyList<StrategicInvasionForce> PhysicalForces =>
        InvasionForces ?? Array.Empty<StrategicInvasionForce>();
}
