using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class NamedSkillRegistryTests
{
    [Fact]
    public void Registry_FailsFast_WhenRequiredSkillIsMissing()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        // Drop "Stealth" to simulate a rename/removal in the rules database.
        Dictionary<int, BaseSkill> withoutStealth = rules.BaseSkills.Values
            .Where(s => s.Name != "Stealth")
            .ToDictionary(s => s.Id, s => s);

        var ex = Assert.Throws<InvalidOperationException>(() => new NamedSkillRegistry(withoutStealth));
        Assert.Contains("Stealth", ex.Message);
    }

    [Fact]
    public void Registry_UsesStableKeys_WhenDisplayNameChanges()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        BaseSkill stealth = rules.BaseSkills.Values.Single(skill => skill.SkillKey == SkillRoleKeys.Stealth);
        stealth.Name = "Infiltration";

        NamedSkillRegistry registry = new(rules.BaseSkills, rules.SkillRoleAssignments);

        Assert.Same(stealth, registry.Stealth);
    }

    [Fact]
    public void Registry_UsesDataOwnedRoleAssignment()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        BaseSkill marine = rules.BaseSkills.Values.Single(skill => skill.SkillKey == "marine");
        List<SkillRoleAssignment> assignments = rules.SkillRoleAssignments.ToList();
        int powerArmorIndex = assignments.FindIndex(
            assignment => assignment.RoleKey == SkillRoleKeys.PowerArmor);
        assignments[powerArmorIndex] = new SkillRoleAssignment(
            SkillRoleKeys.PowerArmor, marine.SkillKey);

        NamedSkillRegistry registry = new(rules.BaseSkills, assignments);

        Assert.Same(marine, registry.PowerArmor);
    }

    [Fact]
    public void Registry_FailsFast_WhenRoleReferencesMissingSkillKey()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        List<SkillRoleAssignment> assignments = rules.SkillRoleAssignments.ToList();
        int stealthIndex = assignments.FindIndex(assignment => assignment.RoleKey == SkillRoleKeys.Stealth);
        assignments[stealthIndex] = new SkillRoleAssignment(SkillRoleKeys.Stealth, "missing_skill");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new NamedSkillRegistry(rules.BaseSkills, assignments));

        Assert.Contains("stealth", ex.Message);
        Assert.Contains("missing_skill", ex.Message);
    }
}
