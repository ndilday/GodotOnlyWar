using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class ChapterGenerationTemplatesTests
{
    private static Faction LoadPlayerFaction()
    {
        return RulesDatabaseFixture.LoadRules().Factions.Single(f => f.IsPlayerFaction);
    }

    [Fact]
    public void Registry_ResolvesRequiredTemplates_FromRealRulesDatabase()
    {
        var templates = new ChapterGenerationTemplates(LoadPlayerFaction());

        // A representative spread across the soldier-template and squad-template sets.
        Assert.Equal("Chapter Master", templates.ChapterMaster.Name);
        Assert.Equal("Codiciers", templates.Codicier.Name);
        Assert.Equal("Sergeant", templates.Sergeant.Name);
        Assert.Equal("Scout Marine", templates.ScoutMarine.Name);
        Assert.Equal("Tactical Squad", templates.TacticalSquad.Name);
        Assert.Equal("Scout Squad", templates.ScoutSquad.Name);

        // Specialist chapter HQ squad templates and the distinct-identity companies
        // used to locate generated units/squads by template rather than display name.
        Assert.Equal("Librarius", templates.Librarius.Name);
        Assert.Equal("Armory", templates.Armory.Name);
        Assert.Equal("Apothecarion", templates.Apothecarion.Name);
        Assert.Equal("Reclusium", templates.Reclusium.Name);
        Assert.Equal("Veteran Company", templates.VeteranCompany.Name);
        Assert.Equal("Scout Company", templates.ScoutCompany.Name);
    }

    [Fact]
    public void Registry_FailsFast_WhenRequiredSoldierTemplateIsMissing()
    {
        var playerFaction = LoadPlayerFaction();
        // Rebuild the player faction without its "Captain" soldier template to
        // simulate a rename/removal in the rules database. Squad/unit dictionaries
        // are left empty to avoid the constructor's template back-reference writes;
        // "Captain" is resolved before any squad template, so the registry still
        // throws on the missing soldier template first.
        var withoutCaptain = new Faction(
            playerFaction.Id,
            playerFaction.Name,
            playerFaction.Color,
            playerFaction.IsPlayerFaction,
            playerFaction.IsDefaultFaction,
            playerFaction.CanInfiltrate,
            playerFaction.GrowthType,
            playerFaction.Species,
            playerFaction.SoldierTemplates.Values.Where(st => st.Name != "Captain").ToDictionary(st => st.Id),
            new Dictionary<int, SquadTemplate>(),
            null,
            null,
            null,
            null);

        var ex = Assert.Throws<InvalidOperationException>(() => new ChapterGenerationTemplates(withoutCaptain));
        Assert.Contains("Captain", ex.Message);
    }
}
