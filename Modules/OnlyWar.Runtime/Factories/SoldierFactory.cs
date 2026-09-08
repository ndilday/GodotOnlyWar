using System.Collections.Generic;
using System.Linq;
using OnlyWar.Runtime.Contracts;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using RuntimeFactory = OnlyWar.Runtime.Factories.RuntimeSoldierFactory;

namespace OnlyWar.Builders;

/// <summary>
/// Materializer for the campaign soldier entity. Stat/body construction is owned by RuntimeFactory;
/// this class preserves the live-entity API and the legacy persistent ID seed that save loading
/// re-seeds, until Campaign registration moves in SB-10. It is Runtime-owned (plan §3.2) so that
/// generation and live simulation share one construction path and one ID counter.
/// </summary>
public sealed class SoldierFactory
{
    private static readonly SoldierFactory _instance = new();
    private static int _nextId;
    private readonly RuntimeFactory _runtime = new();

    private SoldierFactory() { }

    public static SoldierFactory Instance => _instance;

    public void SetCurrentHighestSoldierId(int highestId) => _nextId = highestId + 1;

    public Soldier GenerateNewSoldier(SoldierTemplate template, IRNG random) =>
        GenerateNewSoldier(template, random, null);

    public Soldier GenerateNewSoldier(
        SoldierTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        RuntimeSoldier generated = _runtime.Create(
            template,
            random,
            entityIds ?? new PersistentIdAllocator());
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
        RuntimeSoldier generated = _runtime.Create(
            species,
            newRecruitSkills,
            random,
            entityIds ?? new PersistentIdAllocator());
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

    private sealed class PersistentIdAllocator : IEntityIdAllocator
    {
        public int GetNextId() => _nextId++;
    }
}

