using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class CharacterLoadoutServiceTests
{
    private readonly WeaponSet _crozius = new(801, "Bolt Pistol + Crozius Arcanum");
    private readonly WeaponSet _powerAxe = new(802, "Bolt Pistol + Power Axe");
    private readonly WeaponSet _thunderHammer = new(803, "Bolt Pistol + Thunder Hammer");

    [Fact]
    public void Resolve_NoOverrides_UsesTheRolesAuthoredDefault()
    {
        (Soldier chaplain, PlayerForce force) = CreateContext();

        EffectiveCharacterLoadout effective = CharacterLoadoutService.Resolve(chaplain, force);

        Assert.Equal(CharacterLoadoutSource.Template, effective.Source);
        Assert.Same(_crozius, effective.WeaponSet);
    }

    [Fact]
    public void Resolve_ChapterRoleDefault_OverridesTheAuthoredDefault()
    {
        (Soldier chaplain, PlayerForce force) = CreateContext();
        force.Army.CharacterLoadoutDoctrine.SetRoleDefault(chaplain.Template.Id, _powerAxe);

        EffectiveCharacterLoadout effective = CharacterLoadoutService.Resolve(chaplain, force);

        Assert.Equal(CharacterLoadoutSource.Chapter, effective.Source);
        Assert.Same(_powerAxe, effective.WeaponSet);
    }

    [Fact]
    public void Resolve_PersonalLoadout_OverridesTheChapterRoleDefault()
    {
        (Soldier chaplain, PlayerForce force) = CreateContext();
        force.Army.CharacterLoadoutDoctrine.SetRoleDefault(chaplain.Template.Id, _powerAxe);
        CharacterLoadoutService.SetPersonalLoadout(chaplain, _thunderHammer, force);

        EffectiveCharacterLoadout effective = CharacterLoadoutService.Resolve(chaplain, force);

        Assert.Equal(CharacterLoadoutSource.Personal, effective.Source);
        Assert.Same(_thunderHammer, effective.WeaponSet);
    }

    [Fact]
    public void ClearPersonalLoadout_FallsBackToTheChapterRoleDefault()
    {
        (Soldier chaplain, PlayerForce force) = CreateContext();
        force.Army.CharacterLoadoutDoctrine.SetRoleDefault(chaplain.Template.Id, _powerAxe);
        CharacterLoadoutService.SetPersonalLoadout(chaplain, _thunderHammer, force);

        CharacterLoadoutService.ClearPersonalLoadout(chaplain, force);

        EffectiveCharacterLoadout effective = CharacterLoadoutService.Resolve(chaplain, force);
        Assert.Equal(CharacterLoadoutSource.Chapter, effective.Source);
        Assert.Same(_powerAxe, effective.WeaponSet);
    }

    [Fact]
    public void Resolve_PersonalLoadoutKeyedByIndividual_DoesNotLeakToOthersInTheSameRole()
    {
        (Soldier chaplain, PlayerForce force, Squad squad) = CreateContextWithSquad();
        Soldier otherChaplain = TestModelFactory.CreateSoldier(chaplain.Template, "Brother Kaine");
        squad.AddSquadMember(otherChaplain);
        CharacterLoadoutService.SetPersonalLoadout(chaplain, _thunderHammer, force);

        EffectiveCharacterLoadout other = CharacterLoadoutService.Resolve(otherChaplain, force);

        Assert.Equal(CharacterLoadoutSource.Template, other.Source);
        Assert.Same(_crozius, other.WeaponSet);
    }

    // A trooper's element carries no Command Weapon quota, so pooled squad allocation still owns
    // his kit. Callers rely on the null to tell them so. "Character" is no longer a flag on the
    // soldier template — it depends on which element (squad slot) the soldier occupies — so this
    // has to place the trooper in a squad to be a meaningful check at all.
    [Fact]
    public void Resolve_TrooperElementWithNoCommandWeaponQuota_IsNotACharacter()
    {
        (_, PlayerForce force) = CreateContext();
        Soldier trooper = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate);
        TestModelFactory.CreateSquad("Line Squad", trooper);

        Assert.False(CharacterLoadoutService.IsCharacter(trooper));
        Assert.Null(CharacterLoadoutService.Resolve(trooper, force));
    }

    private (Soldier Chaplain, PlayerForce Force) CreateContext()
    {
        (Soldier chaplain, PlayerForce force, _) = CreateContextWithSquad();
        return (chaplain, force);
    }

    // Builds a Chaplain soldier standing in a single-body element whose Command Weapon quota and
    // menu carry exactly what SoldierTemplate.DefaultWeapons/.WeaponOptions used to carry before
    // the default moved onto the element (see element-loadout-spec.md's resolution order).
    private (Soldier Chaplain, PlayerForce Force, Squad Squad) CreateContextWithSquad()
    {
        SoldierTemplate template = new(
            900, TestModelFactory.HumanSpecies, "Chaplain", 5, 4, false, 4,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            weaponOptionsByGroup: new Dictionary<string, IReadOnlyList<WeaponSet>>
            {
                [CharacterLoadoutService.CommandWeaponGroup] = [_crozius, _powerAxe, _thunderHammer]
            });
        SquadTemplateElement element = new(
            template, 0, 1,
            defaultWeapons: _crozius,
            quotas: [new SquadTemplateElementQuota(CharacterLoadoutService.CommandWeaponGroup, 0, 1)]);
        SquadTemplate squadTemplate = new(
            901, "Test Reclusium", TestModelFactory.DefaultWeapons, [], TestModelFactory.TestArmor,
            [element], SquadTypes.None);

        Faction faction = new(
            11, "Player", Color.Black, true, false, false, GrowthType.None,
            null, null, new Dictionary<int, SquadTemplate>(),
            null, null, null, null);
        Army army = new("Army", null, null, null, []);
        PlayerForce force = new(faction, army, new Fleet("Fleet", null, null));
        Soldier chaplain = TestModelFactory.CreateSoldier(template, "Brother Vaan");
        Squad squad = new("Reclusium", null, squadTemplate);
        squad.AddSquadMember(chaplain);
        return (chaplain, force, squad);
    }
}
