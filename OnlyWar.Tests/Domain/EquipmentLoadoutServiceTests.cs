using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class EquipmentLoadoutServiceTests
{
    [Fact]
    public void Resolve_DefaultKitWithoutArmor_InheritsTheFormationArmor()
    {
        (SquadTemplateElement element, EquipmentKitTemplate authoredKit, EquipmentTemplate armor) =
            CreateRoleFixture();

        ResolvedEquipmentLoadout resolved = OnlyWar.Campaign.EquipmentLoadoutService.Resolve(
            1,
            element,
            doctrine: null,
            authoredKit,
            elementFallbackKit: null,
            squadFallbackKit: null,
            inheritedArmor: armor);

        Assert.Equal(EquipmentLoadoutSource.AuthoredRole, resolved.Source);
        Assert.Same(armor, resolved.Loadout.Armor);
        Assert.Empty(resolved.ValidationIssues);
    }

    [Fact]
    public void Resolve_ExplicitNoArmorRoleOverride_RemainsArmorless()
    {
        (SquadTemplateElement element, EquipmentKitTemplate authoredKit, EquipmentTemplate armor) =
            CreateRoleFixture();
        EquipmentLoadoutDoctrine doctrine = new();
        doctrine.SetRoleDefault(element.PersonalEquipmentRole.Id, new EquipmentLoadout());

        ResolvedEquipmentLoadout resolved = OnlyWar.Campaign.EquipmentLoadoutService.Resolve(
            1,
            element,
            doctrine,
            authoredKit,
            elementFallbackKit: null,
            squadFallbackKit: null,
            inheritedArmor: armor);

        Assert.Equal(EquipmentLoadoutSource.ChapterRole, resolved.Source);
        Assert.Null(resolved.Loadout.Armor);
        Assert.Empty(resolved.ValidationIssues);
    }

    private static (SquadTemplateElement Element, EquipmentKitTemplate AuthoredKit, EquipmentTemplate Armor)
        CreateRoleFixture()
    {
        PersonalEquipmentRole role = new(501, "Test role", 502);
        SquadTemplateElement element = new(
            TestModelFactory.MarineTemplate,
            1,
            1,
            personalEquipmentRole: role);
        EquipmentKitTemplate authoredKit = new(502, "Test weapon kit");
        EquipmentTemplate armor = new(
            503,
            "Test armor",
            tags: EquipmentTags.Armor,
            armorProfile: new ArmorProfile(10));
        return (element, authoredKit, armor);
    }
}
