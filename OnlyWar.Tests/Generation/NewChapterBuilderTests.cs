using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
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
    public void CreateChapter_LeavesNoEmptyCompanySquadsAndPlacesEverySoldier()
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, CreateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "Crimson Sentinels");

        var oob = chapter.Army.OrderOfBattle;

        // Line squads are created on demand, so no empty non-HQ squad should linger
        // in any company. (HQ squads are always present and may legitimately be empty,
        // e.g. a Veteran Company HQ with no qualifying captain.)
        foreach (var company in oob.ChildUnits)
        {
            var emptyLineSquads = company.Squads
                .Where(squad => (squad.SquadTemplate.SquadType & SquadTypes.HQ) == 0)
                .Where(squad => squad.Members.Count == 0)
                .ToList();
            Assert.True(emptyLineSquads.Count == 0,
                $"{company.Name} has empty line squads: " +
                string.Join(", ", emptyLineSquads.Select(s => $"{s.Name} ({s.SquadTemplate.Name})")));
        }

        // No soldier is lost in the create-on-demand assignment.
        Assert.Equal(1000, oob.GetAllMembers().Count());
    }

    [Fact]
    public void CreateChapter_VeteranLineSquadsAreLedByVeteranSergeantsNotCaptains()
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, CreateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "Crimson Sentinels");
        Unit oob = chapter.Army.OrderOfBattle;

        Unit veteranCompany = oob.ChildUnits.First(c => c.UnitTemplate.Name == "Veteran Company");
        var lineVeteranSquads = veteranCompany.Squads
            .Where(s => (s.SquadTemplate.SquadType & SquadTypes.HQ) == 0)
            .Where(s => (s.SquadTemplate.SquadType & SquadTypes.Elite) > 0)
            .Where(s => s.Members.Count > 0)
            .ToList();
        Assert.NotEmpty(lineVeteranSquads);

        // Where a line veteran squad has a leader, it is a Veteran Sergeant; the Captain
        // rank belongs only to the company HQ squad. (A squad may legitimately be
        // leaderless when too few veterans qualify as sergeants.)
        var ledSquads = lineVeteranSquads.Where(s => s.SquadLeader != null).ToList();
        Assert.NotEmpty(ledSquads);
        foreach (Squad squad in ledSquads)
        {
            Assert.Equal("Veteran Sergeant", squad.SquadLeader.Template.Name);
        }

        // Regression: the transfer dropdown previously offered a captain-rank promotion
        // in every line veteran squad, because the squad template's leader slot pointed
        // at a captain. A rank-and-file veteran must not be offered a captain-rank
        // (rank 6+) promotion into any of the veteran company's line squads; captains
        // belong only to the HQ squad.
        var lineSquadIds = lineVeteranSquads.Select(s => s.Id).ToHashSet();
        PlayerSoldier veteran = lineVeteranSquads
            .SelectMany(s => s.Members)
            .OfType<PlayerSoldier>()
            .First(m => m.Template.Name == "Veteran");

        var options = new SoldierTransferService().GetTransferOptions(oob, veteran);
        Assert.DoesNotContain(options, option =>
            option.SoldierTemplate.Rank >= 6 && lineSquadIds.Contains(option.SquadId));
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
