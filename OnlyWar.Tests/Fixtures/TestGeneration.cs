using OnlyWar.Builders;
using OnlyWar.Generation.Contracts;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Application.Adapters.Generation;
using OnlyWar.Models;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Composes the generation support bundle for tests. Production composes it at the point of
/// installation (SB-09); tests do the same thing here so a generation test names the campaign
/// services its run is given instead of the generator reaching for them.
/// </summary>
internal static class TestGeneration
{
    internal static GenerationSupport Support(GameRulesData data, Date date)
    {
        TestCampaignComposition composition = TestPersonnelComposition.CreateCampaign();
        return composition.Services.Generation.CreateSupport(
            data, date, composition.Services.Random);
    }

    internal static Sector GenerateSector(
        int seed,
        GameRulesData data,
        Date currentDate,
        string chapterName = null,
        ScenarioFactionSelection invaderSelection = null) =>
        SectorBuilder.GenerateSector(
            seed, data, currentDate, Support(data, currentDate), chapterName, invaderSelection);

    internal static PlayerForce CreateChapter(
        GameRulesData data,
        ISoldierTrainingService trainingService,
        Date trainingStartDate,
        Date date,
        string chapterName = null,
        int foundingSoldierCount = 1000,
        string chapterProfileKey = null) =>
        NewChapterBuilder.CreateChapter(
            data,
            Support(data, date) with { Training = trainingService },
            trainingStartDate,
            date,
            chapterName,
            foundingSoldierCount,
            chapterProfileKey);
}
