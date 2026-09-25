using System.Collections.Generic;
using OnlyWar.Abstractions;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Runtime.Abstractions;

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

public interface IRuntimeSoldierFactory
{
    RuntimeSoldier Create(
        SoldierTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds);
}
