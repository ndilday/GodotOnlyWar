using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Runtime.Abstractions;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Runtime.Factories;

/// <summary>
/// Constructs the portable part of a soldier.  A Campaign adapter may materialize the returned
/// value into its live roster, but generation and tactical callers share this implementation.
/// </summary>
public sealed class RuntimeSoldierFactory : IRuntimeSoldierFactory
{
    public RuntimeSoldier Create(
        SoldierTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(entityIds);
        RuntimeSoldier result = Create(template.Species, null, random, entityIds);
        List<Skill> skills = result.Skills.ToList();
        foreach ((BaseSkill skill, float points) in template.MosTraining ?? [])
        {
            Skill existing = skills.FirstOrDefault(item => item.BaseSkill.Id == skill.Id);
            if (existing == null) skills.Add(new Skill(skill, points));
            else existing.AddPoints(points);
        }
        return result with { Skills = skills };
    }

    public RuntimeSoldier Create(
        Species species,
        IReadOnlyList<SkillTemplate> recruitSkills,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(entityIds);

        RuntimeSoldier result = new(
            entityIds.GetNextId(),
            new Body(species.BodyTemplate),
            Roll(species.Strength.BaseValue, species.Strength.StandardDeviation, random),
            Roll(species.Dexterity.BaseValue, species.Dexterity.StandardDeviation, random),
            Roll(species.Constitution.BaseValue, species.Constitution.StandardDeviation, random),
            Roll(species.Ego.BaseValue, species.Ego.StandardDeviation, random),
            Roll(species.Charisma.BaseValue, species.Charisma.StandardDeviation, random),
            Roll(species.Perception.BaseValue, species.Perception.StandardDeviation, random),
            Roll(species.Intelligence.BaseValue, species.Intelligence.StandardDeviation, random),
            Roll(species.AttackSpeed.BaseValue, species.AttackSpeed.StandardDeviation, random),
            Roll(species.MoveSpeed.BaseValue, species.MoveSpeed.StandardDeviation, random),
            Roll(species.Size.BaseValue, species.Size.StandardDeviation, random),
            Roll(species.PsychicPower.BaseValue, species.PsychicPower.StandardDeviation, random),
            []);

        List<Skill> skills = [];
        foreach (SkillTemplate skillTemplate in recruitSkills ?? [])
        {
            float value = Roll(skillTemplate.BaseValue, skillTemplate.StandardDeviation, random);
            if (value > 0) skills.Add(new Skill(skillTemplate.BaseSkill, value));
        }

        return result with { Skills = skills };
    }

    private static float Roll(float baseValue, float standardDeviation, IRNG random) =>
        baseValue + (float)(random.NextRandomZValue() * standardDeviation);
}
