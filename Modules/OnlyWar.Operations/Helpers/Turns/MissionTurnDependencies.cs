using OnlyWar.Battles.Abstractions;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Runtime.Factories;

using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Readiness;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Soldiers.Ratings;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.FactionBehaviors;
using System;
using System.Collections.Generic;

namespace OnlyWar.Operations.Turns;

/// <summary>
/// Explicit inputs and host capabilities required by the weekly mission phase. The Operations
/// assembly owns the sequencing policy; the application supplies campaign state and adapters.
/// </summary>
public sealed class MissionTurnDependencies
{
    public Sector Sector { get; init; }
    public GameRulesData Rules { get; init; }
    public Date CurrentDate { get; init; }
    public IRNG Random { get; init; }
    public IReadinessDecisions Readiness { get; init; }
    public IEngagementResolver Engagements { get; init; }
    public MissionRules MissionRules { get; init; }
    public ChapterOperationalDoctrine Doctrine { get; init; }
    public RecruitmentProgram Recruitment { get; init; }
    public IReadOnlyList<StrategicInvasionForce> InvasionForces { get; init; } = [];
    public FactionBehaviorRulesProfile FactionRules { get; init; }
    /// <summary>Physical posting commands used by mission return/exfiltration policy.</summary>
    public IPhysicalPostingCommands Personnel { get; init; }
    public Func<StrategicInvasionForce, Region, float, IRNG, FactionBehaviorRulesProfile, bool> StrategicCommanderCanBeReached { get; init; }

    public IEngagementElementFactory EngagementElements { get; init; }
    public Action<IEnumerable<ISoldier>> ApplyDailyHealing { get; init; }
    public Func<IReadOnlyList<BaseSkill>> ResolveMedicalSkills { get; init; }
    public Action<Order, FieldCareReport, IReadOnlyList<BaseSkill>, int, RatingConsumerBindings> ApplyDailyFieldCare { get; init; }
    public Action<PlanetFaction, Region, float> RecordIntelGain { get; init; }
    public Action<IntelObservation> RecordTargetObservation { get; init; }
    public Action<RegionFaction, long, Faction> RecordScenarioPdfLost { get; init; }
    public Action<Region, long, Faction> RecordScenarioBlighting { get; init; }
    public Action<RegionFaction, long, Faction> RecordScenarioCivilianKills { get; init; }

    public void Validate()
    {
        if (Sector == null) throw new ArgumentNullException(nameof(Sector));
        if (Rules == null) throw new ArgumentNullException(nameof(Rules));
        if (CurrentDate == null) throw new ArgumentNullException(nameof(CurrentDate));
        if (Random == null) throw new ArgumentNullException(nameof(Random));
        if (Readiness == null) throw new ArgumentNullException(nameof(Readiness));
        if (Engagements == null) throw new ArgumentNullException(nameof(Engagements));
        if (MissionRules == null) throw new ArgumentNullException(nameof(MissionRules));
        if (EngagementElements == null) throw new ArgumentNullException(nameof(EngagementElements));
    }
}
