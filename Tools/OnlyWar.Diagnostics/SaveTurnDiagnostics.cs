using System;
using System.IO;
using System.Linq;
using OnlyWar.Application;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace OnlyWar.Diagnostics;

/// <summary>
/// Loads a save, ends one turn in memory and prints the scenario, mandate and legacy state, so a
/// hand-made test save can be checked before it is handed over (2026-11-19). Nothing is written
/// back: the save is copied first and the copy is deleted. Opt-in:
/// <c>dotnet test Tools\OnlyWar.Diagnostics --filter "FullyQualifiedName~SaveTurnDiagnostics" -e RUN_SAVE_TURN=&lt;path&gt;</c>
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SaveTurnDiagnostics
{
    private readonly ITestOutputHelper _output;

    public SaveTurnDiagnostics(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EndOneTurnOnASave()
    {
        string path = Environment.GetEnvironmentVariable("RUN_SAVE_TURN");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);

        string copy = Path.Combine(Path.GetTempPath(), $"save-turn-{Guid.NewGuid():N}.s3db");
        File.Copy(path, copy);
        try
        {
            TestCampaignComposition composition = TestPersonnelComposition.CreateCampaign(new SeededRNG(1));
            GameSession session = composition.CreateApplication().LoadCampaign(copy);
            Sector sector = session.Sector;
            Report("before", sector);
            composition.CreateTurnController(session).ProcessTurn(sector);
            Report("after", sector);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private void Report(string label, Sector sector)
    {
        PlayerForce force = sector.PlayerForce;
        _output.WriteLine($"--- {label} ---");
        _output.WriteLine($"scenario={sector.Scenario?.State} homeWorld={force.HomeWorldPlanetId?.ToString() ?? "none"} "
            + $"legacy={force.Mandates.ChapterTotal}");
        foreach (Mandate mandate in force.Mandates.All)
        {
            _output.WriteLine($"mandate {mandate.Id} '{mandate.Title}' state={mandate.State}");
        }
        if (sector.Scenario != null
            && sector.Planets.TryGetValue(sector.Scenario.PromisedPlanetId, out Planet promised))
        {
            foreach (Region region in promised.Regions.Where(region => region != null))
            {
                string hostile = string.Join(", ", region.RegionFactionMap.Values
                    .Where(rf => !FactionRelationshipService.IsImperial(rf.PlanetFaction.Faction)
                        && (rf.Population > 0 || rf.Garrison > 0))
                    .Select(rf => $"{rf.PlanetFaction.Faction.Name} pub={rf.IsPublic} pop={rf.Population} gar={rf.Garrison}"));
                if (hostile.Length > 0) _output.WriteLine($"  region {region.Id}: {hostile}");
            }
            _output.WriteLine($"  liberated={ChapterWorldTests.IsLiberated(promised)} "
                + $"loyalistsHold={ChapterWorldTests.LoyalistsHoldGround(promised)}");
        }
    }
}
