using System.Collections.Generic;
using System.Linq;
using OnlyWar.Runtime.Abstractions;
using OnlyWar.Runtime.Allocators;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using RuntimeFactory = OnlyWar.Runtime.Factories.RuntimeSoldierFactory;

namespace OnlyWar.Runtime.Factories;

/// <summary>
/// Materializer for the campaign soldier entity. Stat/body construction is owned by RuntimeFactory;
/// this class preserves the live-entity API while keeping identity allocation explicit. It is
/// Runtime-owned (plan §3.2) so generation and live simulation share one construction path.
/// </summary>
public sealed class SoldierFactory
{
    private readonly RuntimeFactory _runtime = new();
    // Legacy overloads remain source-compatible, but their IDs are scoped to this factory
    // instance. Campaign composition passes the session allocator explicitly.
    private readonly IEntityIdAllocator _compatibilityEntityIds = new SequentialEntityIdAllocator();

    public SoldierFactory() { }

    public Soldier GenerateNewSoldier(SoldierTemplate template, IRNG random) =>
        GenerateNewSoldier(template, random, null);

    public Soldier GenerateNewSoldier(
        SoldierTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        entityIds ??= _compatibilityEntityIds;
        RuntimeSoldier generated = _runtime.Create(
            template,
            random,
            entityIds);
        return Materialize(generated);
    }

    public Soldier GenerateNewSoldier(
        Species species,
        IReadOnlyList<SkillTemplate> newRecruitSkills,
        IRNG random) =>
        GenerateNewSoldier(species, newRecruitSkills, random, null);

    public Soldier GenerateNewSoldier(
        Species species,
        IReadOnlyList<SkillTemplate> newRecruitSkills,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        entityIds ??= _compatibilityEntityIds;
        RuntimeSoldier generated = _runtime.Create(
            species,
            newRecruitSkills,
            random,
            entityIds);
        return Materialize(generated);
    }

    public Soldier[] GenerateNewSoldiers(int count, SoldierTemplate template, IRNG random) =>
        GenerateNewSoldiers(count, template, random, null);

    public Soldier[] GenerateNewSoldiers(
        int count,
        SoldierTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        entityIds ??= _compatibilityEntityIds;
        Soldier[] result = new Soldier[count];
        for (int i = 0; i < count; i++)
            result[i] = GenerateNewSoldier(template, random, entityIds);
        return result;
    }

    public Soldier[] GenerateNewSoldiers(
        int count,
        Species species,
        IReadOnlyList<SkillTemplate> newRecruitSkills,
        IRNG random) =>
        GenerateNewSoldiers(count, species, newRecruitSkills, random, null);

    public Soldier[] GenerateNewSoldiers(
        int count,
        Species species,
        IReadOnlyList<SkillTemplate> newRecruitSkills,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        entityIds ??= _compatibilityEntityIds;
        Soldier[] result = new Soldier[count];
        for (int i = 0; i < count; i++)
            result[i] = GenerateNewSoldier(species, newRecruitSkills, random, entityIds);
        return result;
    }

    private static Soldier Materialize(RuntimeSoldier generated)
    {
        Soldier soldier = new(generated.Body.HitLocations.ToList(), generated.Skills.ToList())
        {
            Id = generated.Id,
            Strength = generated.Strength,
            Dexterity = generated.Dexterity,
            Constitution = generated.Constitution,
            Ego = generated.Ego,
            Charisma = generated.Charisma,
            Perception = generated.Perception,
            Intelligence = generated.Intelligence,
            AttackSpeed = generated.AttackSpeed,
            MoveSpeed = generated.MoveSpeed,
            Size = generated.Size,
            PsychicPower = generated.PsychicPower
        };
        return soldier;
    }

}
