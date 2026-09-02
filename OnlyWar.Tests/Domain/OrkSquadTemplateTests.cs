using System;
using System.Linq;

using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

using Xunit;

namespace OnlyWar.Tests.Domain;

public class OrkSquadTemplateTests
{
    [Fact]
    public void OrkSquadRosterMatchesAuthoredComposition()
    {
        Faction orks = Fixtures.RulesDatabaseFixture.LoadRules().Factions
            .Single(faction => faction.Name == "Orks");

        string[] expectedSquads =
        ["Shoota Boyz", "Slugga Boyz", "'Eavy Shoota Boyz", "'Eavy Slugga Boyz", "Gretchin", "Kommandos",
         "Nobz", "'Eavy Nobz", "Meganobz", "'Eavy Warboss", "Megaboss", "Flash Gitz", "Lootas"];
        Assert.Equal(
            expectedSquads.OrderBy(name => name),
            orks.SquadTemplates.Values
                .Where(template => expectedSquads.Contains(template.Name))
                .Select(template => template.Name)
                .OrderBy(name => name));

        AssertMob(orks, "Shoota Boyz", "Ork Boy", 9, 29, "Shoota", "Orkish Skin");
        AssertMob(orks, "Slugga Boyz", "Ork Boy", 9, 29, "Slugga", "Orkish Skin");
        AssertMob(orks, "'Eavy Shoota Boyz", "Ork Boy", 9, 29, "Shoota", "'Eavy Armor");
        AssertMob(orks, "'Eavy Slugga Boyz", "Ork Boy", 9, 29, "Slugga", "'Eavy Armor");
        AssertEliteMob(orks, "Nobz", "Orkish Skin", "Slugga + Choppa + Frag Grenade");
        AssertEliteMob(orks, "'Eavy Nobz", "'Eavy Armor", "Slugga + Choppa + Frag Grenade");
        AssertEliteMob(orks, "Meganobz", "Mega Armor", "Big Shoota + Power Klaw + Frag Grenade");
        AssertHqVariant(orks, "'Eavy Warboss", "'Eavy Armor", "Slugga + Choppa");
        AssertHqVariant(orks, "Megaboss", "Mega Armor", "Big Shoota + Power Klaw");
        AssertHeavyMob(orks, "Flash Gitz", 5, 10, "Snazzgun + Frag Grenade");
        AssertHeavyMob(orks, "Lootas", 5, 15, "Deffgun + Frag Grenade");

        SquadTemplate gretchin = orks.SquadTemplates.Values.Single(template => template.Name == "Gretchin");
        Assert.Equal("No Armor", gretchin.Armor.Name);
        AssertElement(gretchin, "Gretchin", 10, 30, "Grot Blasta");
        SquadTemplateElement runtherd = gretchin.Elements.Single(element => element.SoldierTemplate.Name == "Runtherd");
        Assert.Equal("Grabba Stikk + Slugga + Frag Grenade", runtherd.DefaultWeapons.Name);
        Assert.Equal("Orkish Skin", runtherd.DefaultArmor.Name);

        SquadTemplate kommandos = orks.SquadTemplates.Values.Single(template => template.Name == "Kommandos");
        Assert.Equal(SquadTypes.Scout, kommandos.SquadType);
        Assert.Equal(4, kommandos.Elements.Where(element => !element.SoldierTemplate.IsSquadLeader)
            .Sum(element => element.MinimumNumber));
        Assert.Equal(14, kommandos.Elements.Where(element => !element.SoldierTemplate.IsSquadLeader)
            .Sum(element => element.MaximumNumber));
        AssertElement(kommandos, "Kommando", 4, 14, "Slugga + Choppa + Frag Grenade");
        AssertElement(kommandos, "Kommando Nob", 1, 1, "Slugga + Choppa + Frag Grenade");
        Assert.DoesNotContain(
            kommandos.Elements,
            element => element.SoldierTemplate.Name is "Kommando Rokkit" or "Kommando Big Shoota");
        Assert.DoesNotContain(
            orks.SoldierTemplates.Values,
            template => template.Name is "Kommando Rokkit" or "Kommando Big Shoota");

        SquadTemplateElement kommando = kommandos.Elements
            .Single(element => element.SoldierTemplate.Name == "Kommando");
        Assert.Equal(
            ["Big Shoota", "Rokkit Launcha"],
            kommando.Quotas.Select(quota => quota.OptionGroup).OrderBy(group => group));
        Assert.Equal(
            "Rokkit Launcha + Choppa + Frag Grenade",
            kommando.GetMenu("Rokkit Launcha").Single().Name);
        Assert.Equal(
            "Big Shoota + Choppa + Frag Grenade",
            kommando.GetMenu("Big Shoota").Single().Name);
    }

    [Fact]
    public void OrkRolesUseRequestedBaselineSkills()
    {
        Faction orks = Fixtures.RulesDatabaseFixture.LoadRules().Factions
            .Single(faction => faction.Name == "Orks");
        string[] meleeRoles =
        ["Ork Boy", "Ork Nob", "Runtherd", "Kommando", "Kommando Nob"];

        foreach (string roleName in meleeRoles)
        {
            SoldierTemplate role = orks.SoldierTemplates.Values.Single(template => template.Name == roleName);
            AssertSkillPoints(role, "generic_melee", 1);
            Assert.DoesNotContain(role.MosTraining, training => training.Item1.SkillKey == "generic_ranged");

            if (roleName.StartsWith("Kommando", StringComparison.Ordinal))
            {
                AssertSkillPoints(role, "stealth", 1);
            }
        }

        SoldierTemplate gretchin = orks.SoldierTemplates.Values.Single(template => template.Name == "Gretchin");
        AssertSkillPoints(gretchin, "generic_ranged", 1);
        Assert.DoesNotContain(gretchin.MosTraining, training => training.Item1.SkillKey == "generic_melee");
    }

    [Fact]
    public void BoyzHeavyWeaponQuotaCountsTheNobAndSharesBothWeaponChoices()
    {
        Faction orks = Fixtures.RulesDatabaseFixture.LoadRules().Factions
            .Single(faction => faction.Name == "Orks");
        string[] boyzSquads =
        ["Shoota Boyz", "Slugga Boyz", "'Eavy Shoota Boyz", "'Eavy Slugga Boyz"];

        foreach (string squadName in boyzSquads)
        {
            SquadTemplate squad = orks.SquadTemplates.Values
                .Single(template => template.Name == squadName);
            SquadTemplateElement boyz = squad.Elements
                .Single(element => element.SoldierTemplate.Name == "Ork Boy");
            SquadTemplateElementQuota quota = boyz.Quotas
                .Single(candidate => candidate.OptionGroup == "Heavy Weapon");

            Assert.Equal(0, quota.MinimumRequired);
            Assert.Equal(3, quota.MaximumAllowed);
            Assert.Equal(SquadQuotaModelBasis.Squad, quota.ModelBasis);
            Assert.Equal(10, quota.ModelsPerBlock);
            Assert.Equal(1, quota.SlotsPerBlock);
            Assert.Equal(0, quota.GetMaximumAllowed(9, 9));
            Assert.Equal(1, quota.GetMaximumAllowed(9, 10));
            Assert.Equal(1, quota.GetMaximumAllowed(18, 19));
            Assert.Equal(2, quota.GetMaximumAllowed(19, 20));
            Assert.Equal(3, quota.GetMaximumAllowed(29, 30));

            Assert.Equal(
                ["Big Shoota + Choppa + Frag Grenade", "Rokkit Launcha + Choppa + Frag Grenade"],
                boyz.GetMenu("Heavy Weapon").Select(option => option.Name).OrderBy(name => name));
        }
    }

    [Fact]
    public void GeneratedNpcBoyzFillTheirResolvedHeavyWeaponAllowance()
    {
        Faction orks = Fixtures.RulesDatabaseFixture.LoadRules().Factions
            .Single(faction => faction.Name == "Orks");
        string[] boyzSquads =
        ["Shoota Boyz", "Slugga Boyz", "'Eavy Shoota Boyz", "'Eavy Slugga Boyz"];

        foreach (string squadName in boyzSquads)
        {
            SquadTemplate template = orks.SquadTemplates.Values
                .Single(candidate => candidate.Name == squadName);
            SquadTemplateElement boyz = template.Elements
                .Single(element => element.SoldierTemplate.Name == "Ork Boy");
            SquadTemplateElementQuota quota = boyz.Quotas
                .Single(candidate => candidate.OptionGroup == "Heavy Weapon");

            Squad generated = SquadFactory.GenerateSquad(template, new Fixtures.FixedRNG());
            int actualBoyz = generated.Members.Count(
                member => member.Template == boyz.SoldierTemplate);
            int expected = quota.GetMaximumAllowed(actualBoyz, generated.Members.Count);
            int actual = generated.Loadout.Count(loadout =>
                boyz.GetMenu("Heavy Weapon").Any(option => option.Id == loadout.Id));

            Assert.Equal(10, generated.Members.Count);
            Assert.Equal(1, expected);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void GeneratedKommandosUseTheSharedRoleAndBothFixedSpecialWeapons()
    {
        Faction orks = Fixtures.RulesDatabaseFixture.LoadRules().Factions
            .Single(faction => faction.Name == "Orks");
        SquadTemplate template = orks.SquadTemplates.Values
            .Single(candidate => candidate.Name == "Kommandos");

        Squad generated = SquadFactory.GenerateSquad(template, new Fixtures.FixedRNG());

        Assert.Equal(5, generated.Members.Count);
        Assert.All(generated.Members, member =>
            Assert.True(member.Template.Name is "Kommando" or "Kommando Nob"));
        Assert.Equal(2, generated.Loadout.Count);
        Assert.Contains(
            generated.Loadout,
            weaponSet => weaponSet.Name == "Rokkit Launcha + Choppa + Frag Grenade");
        Assert.Contains(
            generated.Loadout,
            weaponSet => weaponSet.Name == "Big Shoota + Choppa + Frag Grenade");
    }

    private static void AssertMob(
        Faction faction,
        string squadName,
        string roleName,
        int minimum,
        int maximum,
        string rangedWeapon,
        string armor)
    {
        SquadTemplate squad = faction.SquadTemplates.Values.Single(template => template.Name == squadName);
        Assert.Equal(armor, squad.Armor.Name);
        AssertElement(squad, roleName, minimum, maximum, $"{rangedWeapon} + Choppa + Frag Grenade");
        SquadTemplateElement leader = squad.Elements.Single(element => element.SoldierTemplate.IsSquadLeader);
        Assert.Equal("Ork Nob", leader.SoldierTemplate.Name);
        Assert.Equal($"{rangedWeapon} + Choppa", leader.DefaultWeapons.Name);
    }

    private static void AssertEliteMob(
        Faction faction,
        string squadName,
        string armor,
        string weaponSetName)
    {
        SquadTemplate squad = faction.SquadTemplates.Values.Single(template => template.Name == squadName);
        Assert.Equal(SquadTypes.Elite, squad.SquadType);
        Assert.Equal(armor, squad.Armor.Name);
        SquadTemplateElement element = Assert.Single(squad.Elements);
        Assert.Equal("Ork Nob", element.SoldierTemplate.Name);
        Assert.Equal(3, element.MinimumNumber);
        Assert.Equal(10, element.MaximumNumber);
        Assert.Equal(weaponSetName, squad.DefaultWeapons.Name);
        Assert.Equal(weaponSetName, element.DefaultWeapons.Name);
        string expectedRangedWeapon = squadName == "Meganobz" ? "Big Shoota" : "Slugga";
        string expectedMeleeWeapon = squadName == "Meganobz" ? "Power Klaw" : "Choppa";
        Assert.Equal(expectedRangedWeapon, squad.DefaultWeapons.PrimaryRangedWeapon.Name);
        Assert.Equal(expectedMeleeWeapon, squad.DefaultWeapons.PrimaryMeleeWeapon.Name);
        Assert.Equal("Frag Grenade", squad.DefaultWeapons.GrenadeWeapon.Name);
    }

    private static void AssertHeavyMob(
        Faction faction,
        string squadName,
        int minimum,
        int maximum,
        string weaponSetName)
    {
        SquadTemplate squad = faction.SquadTemplates.Values.Single(template => template.Name == squadName);
        Assert.Equal(SquadTypes.Heavy, squad.SquadType);
        Assert.Equal("Orkish Skin", squad.Armor.Name);
        SquadTemplateElement element = Assert.Single(squad.Elements);
        Assert.Equal("Ork Boy", element.SoldierTemplate.Name);
        Assert.Equal("Ork Boy", element.SoldierTemplate.Species.Name);
        Assert.Equal(minimum, element.MinimumNumber);
        Assert.Equal(maximum, element.MaximumNumber);
        Assert.True(element.RollsStrength);
        Assert.Equal(weaponSetName, squad.DefaultWeapons.Name);
        Assert.Equal(weaponSetName, element.DefaultWeapons.Name);
        Assert.Equal(weaponSetName.Split(" + ")[0], squad.DefaultWeapons.PrimaryRangedWeapon.Name);
        Assert.Equal("Frag Grenade", squad.DefaultWeapons.GrenadeWeapon.Name);
        Assert.Null(squad.DefaultWeapons.PrimaryMeleeWeapon);
    }

    private static void AssertHqVariant(
        Faction faction,
        string squadName,
        string armor,
        string weaponSetName)
    {
        SquadTemplate squad = faction.SquadTemplates.Values.Single(template => template.Name == squadName);
        Assert.Equal(SquadTypes.HQ, squad.SquadType);
        Assert.Equal(armor, squad.Armor.Name);
        SquadTemplateElement element = Assert.Single(squad.Elements);
        Assert.Equal("Warboss", element.SoldierTemplate.Name);
        Assert.Equal(1, element.MinimumNumber);
        Assert.Equal(1, element.MaximumNumber);
        Assert.Equal(weaponSetName, squad.DefaultWeapons.Name);
        Assert.Equal(weaponSetName, element.DefaultWeapons.Name);
        Assert.Equal(weaponSetName.Split(" + ")[0], squad.DefaultWeapons.PrimaryRangedWeapon.Name);
        Assert.Equal(weaponSetName.Split(" + ")[1], squad.DefaultWeapons.PrimaryMeleeWeapon.Name);
        Assert.Null(squad.DefaultWeapons.GrenadeWeapon);
    }

    private static void AssertElement(
        SquadTemplate squad,
        string roleName,
        int minimum,
        int maximum,
        string weaponSetName)
    {
        SquadTemplateElement element = squad.Elements
            .Single(candidate => candidate.SoldierTemplate.Name == roleName);
        Assert.Equal(minimum, element.MinimumNumber);
        Assert.Equal(maximum, element.MaximumNumber);
        Assert.Equal(weaponSetName, element.DefaultWeapons.Name);
    }

    private static void AssertSkillPoints(SoldierTemplate role, string skillKey, float expected)
    {
        Assert.Equal(expected, role.MosTraining
            .Single(training => training.Item1.SkillKey == skillKey).Item2);
    }
}
