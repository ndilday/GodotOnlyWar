using System;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Tests.Fixtures;

internal static class TestModelFactory
{
    public static Species HumanSpecies { get; } = new(
        1,
        "Test Human",
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(0),
        Value(10),
        Value(6),
        Value(1),
        1,
        1,
        HumanBodyTemplate.Instance);

    public static SoldierTemplate MarineTemplate { get; } = new(
        1,
        HumanSpecies,
        "Test Marine",
        1,
        1,
        false,
        0,
        Array.Empty<Tuple<BaseSkill, float>>());

    public static SoldierTemplate SergeantTemplate { get; } = new(
        2,
        HumanSpecies,
        "Test Sergeant",
        2,
        1,
        true,
        0,
        Array.Empty<Tuple<BaseSkill, float>>());

    public static ArmorTemplate TestArmor { get; } = new(1, "Test Armor", 5, 0);

    public static WeaponSet DefaultWeapons { get; } = new(
        1,
        "Test Weapons",
        primaryRanged: new RangedWeaponTemplate(
            1,
            "Test Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            0,
            1,
            1,
            0,
            5,
            100,
            1,
            10,
            0,
            1,
            true,
            1),
        primaryMelee: new MeleeWeaponTemplate(
            2,
            "Test Knife",
            EquipLocation.OneHand,
            TestSkills.Melee,
            0,
            1,
            1,
            0,
            1,
            0,
            0,
            0));

    public static SquadTemplate SquadTemplate { get; } = new(
        1,
        "Test Squad",
        DefaultWeapons,
        [],
        TestArmor,
        [new SquadTemplateElement(SergeantTemplate, 0, 1), new SquadTemplateElement(MarineTemplate, 0, 4)],
        SquadTypes.None,
        10);

    public static Soldier CreateSoldier(
        SoldierTemplate template = null,
        string name = "Test Marine",
        float dexterity = 10,
        float strength = 10,
        float charisma = 10,
        params Skill[] skills)
    {
        Soldier soldier = new(HumanBodyTemplate.Instance)
        {
            Name = name,
            Template = template ?? MarineTemplate,
            Dexterity = dexterity,
            Strength = strength,
            Charisma = charisma,
            Constitution = 10,
            Intelligence = 10,
            Ego = 10,
            Perception = 10,
            AttackSpeed = 10,
            MoveSpeed = 6,
            Size = 1
        };

        foreach (Skill skill in skills)
        {
            soldier.AddSkillPoints(skill.BaseSkill, skill.PointsInvested);
        }

        return soldier;
    }

    public static Squad CreateSquad(string name, params Soldier[] soldiers)
    {
        Squad squad = new(name, null, SquadTemplate);
        foreach (Soldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }

        return squad;
    }

    private static NormalizedValueTemplate Value(float value)
    {
        return new NormalizedValueTemplate
        {
            BaseValue = value,
            StandardDeviation = 0
        };
    }
}
