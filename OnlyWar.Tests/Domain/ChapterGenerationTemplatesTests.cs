using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ChapterGenerationDoctrineTests
{
    [Fact]
    public void Doctrine_FailsFast_WhenRequiredSoldierTemplateIsMissing()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var playerFaction = rules.Factions.Single(f => f.IsPlayerFaction);
        ChapterGenerationProfileData profile = rules.ChapterGenerationProfiles
            .Single(item => item.FactionId == playerFaction.Id && item.IsDefault);
        int captainTemplateId = profile.TemplateAssignments
            .Single(item => item.RoleKey == ChapterGenerationRoleKeys.Soldier(ChapterSoldierRole.Captain))
            .TemplateId;
        // Rebuild the player faction without its Captain template to simulate a
        // missing role target. Squad/unit dictionaries are left empty because the
        // doctrine resolves soldier roles first and should report that failure.
        var withoutCaptain = new Faction(
            playerFaction.Id,
            playerFaction.Name,
            playerFaction.Color,
            playerFaction.IsPlayerFaction,
            playerFaction.IsDefaultFaction,
            playerFaction.Behavior,
            playerFaction.GrowthType,
            playerFaction.Species,
            playerFaction.SoldierTemplates.Values.Where(st => st.Id != captainTemplateId).ToDictionary(st => st.Id),
            new Dictionary<int, SquadTemplate>(),
            null,
            null,
            null,
            null);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ChapterGenerationDoctrine(withoutCaptain, profile));
        Assert.Contains("Captain", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Doctrine_FailsFast_WhenFormationBindingDoesNotMatchItsSquadSlots()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var playerFaction = rules.Factions.Single(f => f.IsPlayerFaction);
        ChapterGenerationProfileData source = rules.ChapterGenerationProfiles
            .Single(item => item.FactionId == playerFaction.Id && item.IsDefault);
        List<ChapterFormationAssignmentData> formations = source.FormationAssignments
            .Select(assignment => assignment.FormationKey ==
                    ChapterGenerationRoleKeys.Formation(ChapterFormationRole.Tactical)
                ? new ChapterFormationAssignmentData
                {
                    FormationKey = assignment.FormationKey,
                    SquadRoleKey = assignment.SquadRoleKey,
                    MemberSoldierRoleKey = ChapterGenerationRoleKeys.Soldier(
                        ChapterSoldierRole.AssaultMarine),
                    LeaderSoldierRoleKey = assignment.LeaderSoldierRoleKey,
                    MemberFoundingRoleKey = assignment.MemberFoundingRoleKey,
                    LeaderFoundingRoleKey = assignment.LeaderFoundingRoleKey
                }
                : assignment)
            .ToList();
        ChapterGenerationProfileData invalid = new()
        {
            ProfileKey = source.ProfileKey,
            FactionId = source.FactionId,
            RootUnitTemplateId = source.RootUnitTemplateId,
            IsDefault = source.IsDefault,
            TemplateAssignments = source.TemplateAssignments,
            FormationAssignments = formations,
            UnitOrders = source.UnitOrders
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ChapterGenerationDoctrine(playerFaction, invalid));
        Assert.Contains("member/leader slots", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Doctrine_FailsFast_WhenUnitOrderIsIncomplete()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var playerFaction = rules.Factions.Single(f => f.IsPlayerFaction);
        ChapterGenerationProfileData source = rules.ChapterGenerationProfiles
            .Single(item => item.FactionId == playerFaction.Id && item.IsDefault);
        ChapterGenerationProfileData invalid = new()
        {
            ProfileKey = source.ProfileKey,
            FactionId = source.FactionId,
            RootUnitTemplateId = source.RootUnitTemplateId,
            IsDefault = source.IsDefault,
            TemplateAssignments = source.TemplateAssignments,
            FormationAssignments = source.FormationAssignments,
            UnitOrders = source.UnitOrders.Take(source.UnitOrders.Count - 1).ToList()
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ChapterGenerationDoctrine(playerFaction, invalid));
        Assert.Contains("unit-order", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
