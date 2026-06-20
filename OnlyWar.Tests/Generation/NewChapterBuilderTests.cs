using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

public class NewChapterBuilderTests
{
    private readonly GameRulesData _data;

    public NewChapterBuilderTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, new Date(39, 500, 1), null);
    }

    [Fact]
    public void CreateChapter_AppliesChapterNameToUnitArmyFleetAndFoundingHistory()
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, CreateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "Crimson Sentinels");

        Assert.Equal("Crimson Sentinels", chapter.Army.OrderOfBattle.Name);
        Assert.Equal("Crimson Sentinels Ground Forces", chapter.Army.ForceName);
        Assert.Equal("Crimson Sentinels Fleet", chapter.Fleet.ForceName);

        bool foundingMentionsName = chapter.BattleHistory.Values
            .SelectMany(events => events)
            .SelectMany(history => history.SubEvents)
            .Any(entry => entry.Contains("Crimson Sentinels"));
        Assert.True(foundingMentionsName, "Founding history should mention the chapter name.");
    }

    [Fact]
    public void CreateChapter_TrimsSurroundingWhitespaceFromChapterName()
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, CreateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "  Iron Wardens  ");

        Assert.Equal("Iron Wardens", chapter.Army.OrderOfBattle.Name);
        Assert.Equal("Iron Wardens Fleet", chapter.Fleet.ForceName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateChapter_FallsBackToDefaultNameWhenNoneProvided(string chapterName)
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, CreateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), chapterName);

        Assert.Equal("Heart of the Emperor", chapter.Army.OrderOfBattle.Name);
    }

    [Fact]
    public void GenerateSector_ThreadsSeedAndChapterNameThroughToAGeneratedSector()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, new Date(39, 500, 1), "Storm Knights");

        Assert.NotEmpty(sector.Planets);
        Assert.Equal("Storm Knights", sector.PlayerForce.Army.OrderOfBattle.Name);
        // The new-game seed also drives the warp-network build, which should be populated.
        Assert.NotEmpty(sector.WarpLanes);
    }

    private ISoldierTrainingService CreateTrainingService()
    {
        RatingCalculator ratingCalculator = new(_data.RatingDefinitions, _data.RatingAwardTiers,
                                                _data.BaseSkillMap, StaticRNG.Instance);
        return new SoldierTrainingCalculator(_data.BaseSkillMap.Values, _data.TrainingProfiles.Values,
                                             ratingCalculator);
    }
}
