using System;
using System.Collections.Generic;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Contracts.Runtime;

/// <summary>Portable result of constructing one soldier; no Campaign entity is returned.</summary>
public sealed record RuntimeSoldier(
    int Id,
    Body Body,
    float Strength,
    float Dexterity,
    float Constitution,
    float Ego,
    float Charisma,
    float Perception,
    float Intelligence,
    float AttackSpeed,
    float MoveSpeed,
    float Size,
    float PsychicPower,
    IReadOnlyList<Skill> Skills);

/// <summary>Only the template data required for a runtime squad construction.</summary>
public sealed record RuntimeSquadElement(
    SoldierTemplate SoldierTemplate,
    int MinimumNumber,
    int MaximumNumber,
    bool RollsStrength = false);

public sealed record RuntimeSquadTemplate(
    int Id,
    string Name,
    int BattleValue,
    IReadOnlyList<RuntimeSquadElement> Elements,
    SquadTypesForRuntime SquadType = SquadTypesForRuntime.None);

public enum RuntimeForceCompositionProfile
{
    Generic,
    ScoutPatrol,
    SpecialHqTarget
}

[Flags]
public enum SquadTypesForRuntime
{
    None = 0,
    Hq = 1,
    Scout = 2,
    Elite = 4,
    Fast = 8,
    Heavy = 16,
    Bodyguard = 32
}

public sealed record RuntimeSquad(
    int Id,
    string Name,
    RuntimeSquadTemplate Template,
    IReadOnlyList<RuntimeSoldier> Members);

public sealed record RuntimeForceGenerationRequest(
    IReadOnlyList<RuntimeSquadTemplate> Templates,
    long TargetBattleValue,
    RuntimeForceCompositionProfile Profile = RuntimeForceCompositionProfile.Generic,
    int Tier = 0);

public interface IRuntimeSoldierFactory
{
    RuntimeSoldier Create(
        SoldierTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds);
}
