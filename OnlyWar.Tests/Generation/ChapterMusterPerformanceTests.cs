using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ChapterMusterPerformanceTests
{
    private readonly GameRulesData _data;
    private readonly ITestOutputHelper _output;

    public ChapterMusterPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, new Date(39, 500, 1), null);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void BuildMusterDomainSequence_ForDefault1000SoldierChapter()
    {
        RNG.Reset(20260821);
        Stopwatch timer = Stopwatch.StartNew();

        PlayerForce force = NewChapterBuilder.CreateChapter(
            _data,
            CreateTrainingService(),
            new Date(39, 496, 1),
            new Date(39, 500, 1),
            "Muster Performance Chapter");
        double chapterBuildMs = timer.Elapsed.TotalMilliseconds;

        SoldierTransferService transfers = new();
        timer.Restart();
        SoldierTransferContext context = transfers.CreateContext(force.Army.OrderOfBattle);
        double contextBuildMs = timer.Elapsed.TotalMilliseconds;

        ChapterMusterViewModelBuilder builder = new();
        MusterPlanService plan = new();
        timer.Restart();
        IReadOnlyList<MusterCandidateViewModel> candidates = builder.BuildCandidates(
            force,
            plan,
            MusterPopulationMode.PromotionEligible,
            context: context);
        double candidateBuildMs = timer.Elapsed.TotalMilliseconds;

        Assert.Equal(1000, force.Army.OrderOfBattle.GetAllMembers().Count());
        Assert.NotEmpty(candidates);

        PlayerSoldier selected = force.Army.PlayerSoldierMap[candidates[0].SoldierId];
        timer.Restart();
        IReadOnlyList<FormationVacancyViewModel> formations = builder.BuildFormations(
            force,
            selected,
            plan,
            context);
        double formationBuildMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        MusterPlanValidation validation = plan.Validate(force, context);
        double validationMs = timer.Elapsed.TotalMilliseconds;

        Assert.NotEmpty(formations);
        Assert.True(validation.IsValid);

        _output.WriteLine($"Roster: {force.Army.OrderOfBattle.GetAllMembers().Count()} soldiers");
        _output.WriteLine($"Candidates: {candidates.Count}");
        _output.WriteLine($"Selected: {selected.Id} ({selected.Name})");
        _output.WriteLine($"Formation rows: {formations.Count}");
        _output.WriteLine($"Chapter build: {chapterBuildMs:F1} ms");
        _output.WriteLine($"Transfer context: {contextBuildMs:F1} ms");
        _output.WriteLine($"Build candidates: {candidateBuildMs:F1} ms");
        _output.WriteLine($"Build formations: {formationBuildMs:F1} ms");
        _output.WriteLine($"Validate plan: {validationMs:F1} ms");
    }

    private ISoldierTrainingService CreateTrainingService()
    {
        RatingCalculator ratingCalculator = new(
            _data.RatingDefinitions,
            _data.RatingAwardTiers,
            _data.BaseSkillMap,
            StaticRNG.Instance);
        return new SoldierTrainingCalculator(
            _data.BaseSkillMap.Values,
            _data.TrainingProfiles.Values,
            ratingCalculator,
            scoutTrainingOptions: _data.ScoutTrainingOptions.Options);
    }
}
