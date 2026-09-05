using System;
using System.Collections.Generic;
using OnlyWar.Contracts.Battles;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Contracts.Operations;

/// <summary>Campaign facts selected by the caller for mission execution.</summary>
public sealed record MissionCampaignInputs(
    Date Date,
    ChapterOperationalDoctrine Doctrine = null,
    RecruitmentProgram Recruitment = null,
    IReadOnlyList<StrategicInvasionForce> InvasionForces = null,
    FactionBehaviorRulesProfile FactionRules = null,
    IBattleEquipmentSource Equipment = null)
{
    public IReadOnlyList<StrategicInvasionForce> PhysicalForces =>
        InvasionForces ?? Array.Empty<StrategicInvasionForce>();
}

