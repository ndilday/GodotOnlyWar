using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SectorGenerationFactionsTests
{
    [Fact]
    public void Registry_FailsFast_WhenRequiredFactionIsMissing()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        int invaderId = rules.FactionRoleAssignments
            .Single(assignment => assignment.RoleKey == FactionRoleKeys.Invader)
            .FactionId;
        List<Faction> withoutInvader = rules.Factions
            .Where(f => f.Id != invaderId)
            .ToList();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SectorGenerationFactions(withoutInvader, rules.FactionRoleAssignments));
        Assert.Contains(FactionRoleKeys.Invader, ex.Message);
    }

    [Fact]
    public void Registry_ResolvesTheFactionSelectedByAssignmentId()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        int invaderId = rules.FactionRoleAssignments
            .Single(assignment => assignment.RoleKey == FactionRoleKeys.Invader)
            .FactionId;
        int alternateFactionId = rules.FactionRoleAssignments
            .Single(assignment => assignment.RoleKey == FactionRoleKeys.Insurrectionists)
            .FactionId;
        Faction alternateFaction = rules.Factions.Single(faction => faction.Id == alternateFactionId);
        List<FactionRoleAssignment> assignments = rules.FactionRoleAssignments
            .Select(assignment => assignment.RoleKey == FactionRoleKeys.Invader
                ? assignment with { FactionId = alternateFaction.Id }
                : assignment)
            .ToList();

        SectorGenerationFactions registry = new(rules.Factions, assignments);

        Assert.Same(alternateFaction, registry.Invader);
    }
}
