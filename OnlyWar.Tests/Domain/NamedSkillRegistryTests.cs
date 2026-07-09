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
    public void Registry_ResolvesRequiredSkills_FromRealRulesDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        var registry = new NamedSkillRegistry(rules.BaseSkills);

        Assert.Equal("Stealth", registry.Stealth.Name);
        Assert.Equal("Tactics", registry.Tactics.Name);
        Assert.Equal("Fist", registry.Fist.Name);
    }

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
}
