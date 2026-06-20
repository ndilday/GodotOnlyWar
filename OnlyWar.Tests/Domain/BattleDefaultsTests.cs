using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class BattleDefaultsTests
{
    [Fact]
    public void Registry_ResolvesBothFists_DisambiguatedByRelatedSkill()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var skills = new NamedSkillRegistry(rules.BaseSkills);

        var defaults = new BattleDefaults(rules.MeleeWeaponTemplates, skills);

        // Both unarmed defaults are named "Fist" but differ by related skill; the
        // registry must resolve each to the correct, distinct template.
        Assert.Equal("Fist", defaults.ImperialUnarmedWeapon.Name);
        Assert.Equal("Fist", defaults.GenericUnarmedWeapon.Name);
        Assert.Same(skills.Fist, defaults.ImperialUnarmedWeapon.RelatedSkill);
        Assert.Same(skills.GenericMelee, defaults.GenericUnarmedWeapon.RelatedSkill);
        Assert.NotEqual(defaults.ImperialUnarmedWeapon.Id, defaults.GenericUnarmedWeapon.Id);
    }

    [Fact]
    public void Registry_FailsFast_WhenUnarmedWeaponForSkillIsMissing()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var skills = new NamedSkillRegistry(rules.BaseSkills);
        // Drop the Imperial-skill "Fist" weapon, leaving only the Generic Melee one.
        Dictionary<int, MeleeWeaponTemplate> withoutImperialFist = rules.MeleeWeaponTemplates.Values
            .Where(t => !(t.Name == "Fist" && t.RelatedSkill == skills.Fist))
            .ToDictionary(t => t.Id);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new BattleDefaults(withoutImperialFist, skills));
        Assert.Contains("Fist", ex.Message);
    }
}
