using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Pooled squad allocation hands each weapon set to whoever has the best skill for it. That is
/// right for interchangeable troopers and wrong for command staff, whose kit belongs to the
/// individual. These cover the character pass that runs ahead of it.
/// </summary>
public class CharacterEquipmentAllocationTests
{
    private static readonly MeleeWeaponTemplate RelicBladeTemplate =
        new(90, "Relic Blade", EquipLocation.OneHand, TestSkills.Melee, 0, 1, 1, 0, 1, 0, 1);

    private static readonly MeleeWeaponTemplate PowerFistTemplate =
        new(91, "Power Fist", EquipLocation.OneHand, TestSkills.Melee, 0, 1, 1, 0, 1, 0, 1);

    private static readonly WeaponSet RelicBlade = new(910, "Relic Blade", primaryMelee: RelicBladeTemplate);
    private static readonly WeaponSet PowerFist = new(911, "Power Fist", primaryMelee: PowerFistTemplate);

    private static readonly SoldierTemplate ChampionTemplate = new(
        95, TestModelFactory.HumanSpecies, "Champion", 5, 2, false, 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        weaponOptionsByGroup: new Dictionary<string, IReadOnlyList<WeaponSet>>
        {
            [CharacterLoadoutService.CommandWeaponGroup] = [RelicBlade]
        });

    // A single-body element with a Command Weapon quota is what makes the Champion a character —
    // same mechanism a sergeant now uses, not a soldier-template flag.
    private static readonly SquadTemplateElement ChampionElement = new(
        ChampionTemplate, 0, 1,
        defaultWeapons: RelicBlade,
        quotas: [new SquadTemplateElementQuota(CharacterLoadoutService.CommandWeaponGroup, 0, 1)]);

    private static readonly SquadTemplateElement TrooperElement = new(TestModelFactory.MarineTemplate, 0, 4);

    private static readonly SquadTemplate CommandSquadTemplate = new(
        950, "Test Command Squad", TestModelFactory.DefaultWeapons, [], TestModelFactory.TestArmor,
        [ChampionElement, TrooperElement], SquadTypes.None);

    [Fact]
    public void AllocateEquipment_Character_CarriesHisOwnKitRatherThanTheSquadDefault()
    {
        Soldier champion = TestModelFactory.CreateSoldier(ChampionTemplate, "Champion Dolan");
        Soldier trooper = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Brother Sten");
        BattleSquad battleSquad = new(true, CreateCommandSquad("Command", champion, trooper));

        BattleSoldier equippedChampion = FindSoldier(battleSquad, champion.Id);
        BattleSoldier equippedTrooper = FindSoldier(battleSquad, trooper.Id);

        Assert.Equal("Relic Blade", Assert.Single(equippedChampion.MeleeWeapons).Template.Name);
        Assert.Equal("Test Knife", Assert.Single(equippedTrooper.MeleeWeapons).Template.Name);
    }

    // The reason the character pass has to run first: a trooper who happens to be the better
    // swordsman would otherwise win the squad's special weapon set off the character.
    [Fact]
    public void AllocateEquipment_Character_IsExcludedFromThePooledSkillContest()
    {
        Soldier champion = TestModelFactory.CreateSoldier(
            ChampionTemplate, "Champion Dolan", skills: new Skill(TestSkills.Melee, 100f));
        Soldier trooper = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Brother Sten");
        Squad squad = CreateCommandSquad("Command", champion, trooper);
        squad.Loadout = [PowerFist];
        BattleSquad battleSquad = new(true, squad);

        // Despite being by far the better melee fighter, the champion keeps his own blade and the
        // pooled power fist falls to the trooper.
        Assert.Equal("Relic Blade",
            Assert.Single(FindSoldier(battleSquad, champion.Id).MeleeWeapons).Template.Name);
        Assert.Equal("Power Fist",
            Assert.Single(FindSoldier(battleSquad, trooper.Id).MeleeWeapons).Template.Name);
    }

    private static Squad CreateCommandSquad(string name, params Soldier[] soldiers)
    {
        Squad squad = new(name, null, CommandSquadTemplate);
        foreach (Soldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }
        return squad;
    }

    private static BattleSoldier FindSoldier(BattleSquad battleSquad, int soldierId) =>
        battleSquad.Soldiers.Single(battleSoldier => battleSoldier.Soldier.Id == soldierId);
}
