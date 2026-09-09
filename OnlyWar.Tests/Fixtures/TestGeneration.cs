using OnlyWar.Generation.World;
using OnlyWar.Abstractions;
using OnlyWar.Generation.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Application.Adapters.Generation;
using OnlyWar.Runtime.Naming;

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
        return BuildSupport(data, date).Support;
    }

    internal static Sector GenerateSector(
        int seed,
        GameRulesData data,
        Date currentDate,
        string chapterName = null,
        ScenarioFactionSelection invaderSelection = null)
    {
        (GenerationSupport support, NameGenerator nameGenerator) = BuildSupport(data, currentDate);
        return SectorBuilder.GenerateSector(
            seed,
            data,
            currentDate,
            support,
            chapterName,
            invaderSelection,
            nameGenerator);
    }

    internal static PlayerForce CreateChapter(
        GameRulesData data,
        ISoldierTrainingService trainingService,
        Date trainingStartDate,
        Date date,
        string chapterName = null,
        int foundingSoldierCount = 1000,
        string chapterProfileKey = null)
    {
        (GenerationSupport support, NameGenerator nameGenerator) = BuildSupport(data, date);
        return NewChapterBuilder.CreateChapter(
            data,
            support with { Training = trainingService },
            trainingStartDate,
            date,
            chapterName,
            foundingSoldierCount,
            chapterProfileKey,
            nameGenerator);
    }

    private static (GenerationSupport Support, NameGenerator NameGenerator) BuildSupport(
        GameRulesData data,
        Date date)
    {
        TestCampaignComposition composition = TestPersonnelComposition.CreateCampaign();
        IPersistentIdAllocator identity = new OnlyWar.Runtime.Allocators.PersistentIdAllocator();
        return (
            composition.Services.Generation.CreateSupport(
                data, date, composition.Services.Random, identity),
            composition.Services.NameGenerator);
    }
}
