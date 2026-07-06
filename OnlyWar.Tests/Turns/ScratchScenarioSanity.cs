using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace OnlyWar.Tests.Turns;

public class ScratchScenarioSanity
{
    private const string ResultPath =
        @"C:\Users\nadil\AppData\Local\Temp\claude\C--Projects-GodotOnlyWar\f0c3954c-b81f-4a29-9747-779e9e12637e\scratchpad\board.txt";

    private readonly ITestOutputHelper _out;
    public ScratchScenarioSanity(ITestOutputHelper output) => _out = output;

    [Fact]
    public void DumpBoardState()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        var data = new GameRulesData();
        var date = new Date(39, 500, 1);
        GameDataSingleton.Instance.LoadGameDataFromBlob(data, date, null);

        Faction tyr = data.SectorFactions.Invader;
        Faction cult = data.SectorFactions.Infiltrator;
        Faction imp = data.DefaultFaction;

        File.WriteAllText(ResultPath, $"cult strength fraction = {ScenarioRules.PromisedWorldCultStrengthFraction}\n");
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 7, 11 })
        {
            Sector sector = SectorBuilder.GenerateSector(seed, data, date, "Board");
            Planet p = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

            // Clean control = exactly one public faction in the region (Region.ControllingFaction).
            int impClean = p.Regions.Count(r => r.ControllingFaction?.PlanetFaction.Faction.Id == imp.Id);
            int cultClean = p.Regions.Count(r => r.ControllingFaction?.PlanetFaction.Faction.Id == cult.Id);
            int tyrClean = p.Regions.Count(r => r.ControllingFaction?.PlanetFaction.Faction.Id == tyr.Id);
            int contested = p.Regions.Count(r => r.ControllingFaction == null);
            bool cultPublic = p.PlanetFactionMap.TryGetValue(cult.Id, out var cpf) && cpf.IsPublic;

            string line =
                $"seed {seed,2}: ctrl={p.GetControllingFaction().Name,-10} " +
                $"clean[imp={impClean,2} cult={cultClean,2} tyr={tyrClean,2} contested={contested,2}] " +
                $"cult={(cultPublic ? "PUB" : "hid")}\n";
            File.AppendAllText(ResultPath, line);
            _out.WriteLine(line);
        }
        Assert.True(true);
    }
}
