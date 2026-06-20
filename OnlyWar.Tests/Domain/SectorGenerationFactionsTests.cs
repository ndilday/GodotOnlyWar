using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SectorGenerationFactionsTests
{
    [Fact]
    public void Registry_ResolvesRequiredFactions_FromRealRulesDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        var sectorFactions = new SectorGenerationFactions(rules.Factions);

        Assert.Equal("Genestealer Cult", sectorFactions.Infiltrator.Name);
        Assert.Equal("Tyranids", sectorFactions.Invader.Name);
    }

    [Fact]
    public void Registry_FailsFast_WhenRequiredFactionIsMissing()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        List<Faction> withoutTyranids = rules.Factions.Where(f => f.Name != "Tyranids").ToList();

        var ex = Assert.Throws<InvalidOperationException>(() => new SectorGenerationFactions(withoutTyranids));
        Assert.Contains("Tyranids", ex.Message);
    }
}
