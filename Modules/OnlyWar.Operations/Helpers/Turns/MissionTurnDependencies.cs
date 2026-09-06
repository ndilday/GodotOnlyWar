using OnlyWar.Contracts.Battles;
using OnlyWar.Contracts.Medical;
using OnlyWar.Contracts.Operations;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Medical;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.FactionBehaviors;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Turns;

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
    public IBattleEquipmentSource Equipment { get; init; }
    public MissionRules MissionRules { get; init; }
    public ChapterOperationalDoctrine Doctrine { get; init; }
    public RecruitmentProgram Recruitment { get; init; }
    public IReadOnlyList<StrategicInvasionForce> InvasionForces { get; init; } = [];
    public FactionBehaviorRulesProfile FactionRules { get; init; }
    public IOperationsPersonnelSurface Personnel { get; init; }
    public Func<StrategicInvasionForce, Region, float, IRNG, FactionBehaviorRulesProfile, bool> StrategicCommanderCanBeReached { get; init; }

    public Func<bool, Squad, ChapterOperationalDoctrine, RecruitmentProgram, BattleSquad> CreateBattleSquad { get; init; }
    public Func<PlayerSoldier, int, Faction, ChapterOperationalDoctrine, RecruitmentProgram, BattleSquad> CreateAttachedBattleSquad { get; init; }
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
        if (CreateBattleSquad == null) throw new ArgumentNullException(nameof(CreateBattleSquad));
        if (CreateAttachedBattleSquad == null) throw new ArgumentNullException(nameof(CreateAttachedBattleSquad));
    }
}
