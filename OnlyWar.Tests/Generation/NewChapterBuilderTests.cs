using System.Collections.Generic;
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
            _data, new VeteranCandidateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "Crimson Sentinels");
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
    public void CreateChapter_AssignsCompanyChaplainsToCaptainedCompaniesAndReclusiumJudiciars()
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, CreateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "Crimson Sentinels");
        Unit oob = chapter.Army.OrderOfBattle;

        Squad reclusium = oob.Squads.First(s => s.SquadTemplate.Name == "Reclusium");

        // At most one Master of Sanctity and one Reclusiarch, and both live in the Reclusium.
        var mastersOfSanctity = oob.GetAllMembers().OfType<PlayerSoldier>()
            .Where(s => s.Template.Name == "Master of Sanctity").ToList();
        var reclusiarchs = oob.GetAllMembers().OfType<PlayerSoldier>()
            .Where(s => s.Template.Name == "Reclusiarch").ToList();
        Assert.True(mastersOfSanctity.Count <= 1);
        Assert.True(reclusiarchs.Count <= 1);
        Assert.All(mastersOfSanctity, s => Assert.Equal(reclusium, s.AssignedSquad));
        Assert.All(reclusiarchs, s => Assert.Equal(reclusium, s.AssignedSquad));

        // Every Chaplain is seconded to a company HQ squad whose company has a Captain.
        var chaplains = oob.GetAllMembers().OfType<PlayerSoldier>()
            .Where(s => s.Template.Name == "Chaplain").ToList();
        foreach (PlayerSoldier chaplain in chaplains)
        {
            Squad hq = chaplain.AssignedSquad;
            Assert.True((hq.SquadTemplate.SquadType & SquadTypes.HQ) > 0,
                $"{chaplain.Name} (Chaplain) is not in an HQ squad.");
            Assert.NotNull(hq.SquadLeader);
            Assert.Equal("Captain", hq.SquadLeader.Template.Name);
        }
        // No more than one Chaplain per company HQ.
        Assert.All(chaplains.GroupBy(c => c.AssignedSquad.Id), g => Assert.True(g.Count() == 1));

        // Each Judiciar is either seconded to a captained company HQ (at most one per HQ)
        // or held in the Reclusium as part of the aspirant reserve.
        var judiciars = oob.GetAllMembers().OfType<PlayerSoldier>()
            .Where(s => s.Template.Name == "Judiciar").ToList();
        var companyJudiciars = judiciars.Where(j => j.AssignedSquad != reclusium).ToList();
        foreach (PlayerSoldier judiciar in companyJudiciars)
        {
            Squad hq = judiciar.AssignedSquad;
            Assert.True((hq.SquadTemplate.SquadType & SquadTypes.HQ) > 0,
                $"{judiciar.Name} (Judiciar) is not in an HQ squad or the Reclusium.");
            Assert.NotNull(hq.SquadLeader);
            Assert.Equal("Captain", hq.SquadLeader.Template.Name);
        }
        Assert.All(companyJudiciars.GroupBy(j => j.AssignedSquad.Id), g => Assert.True(g.Count() == 1));

        // A captain-less company HQ receives neither a Chaplain nor a Judiciar.
        foreach (Unit company in oob.ChildUnits)
        {
            Squad hq = company.HQSquad;
            if (hq != null && hq.SquadLeader == null)
            {
                Assert.DoesNotContain(hq.Members.OfType<PlayerSoldier>(),
                    m => m.Template.Name is "Chaplain" or "Judiciar");
            }
        }
    }

    [Fact]
    public void CreateChapter_VeteransRequireTacticalBaselineAndAdamantiumCombatSpike()
    {
        PlayerForce chapter = NewChapterBuilder.CreateChapter(
            _data, new VeteranCandidateTrainingService(), new Date(39, 496, 1), new Date(39, 500, 1), "Crimson Sentinels");
        Unit oob = chapter.Army.OrderOfBattle;

        var veterans = oob.GetAllMembers()
            .OfType<PlayerSoldier>()
            .Where(s => s.Template.Name is "Veteran" or "Veteran Sergeant")
            .ToList();

        Assert.NotEmpty(veterans);
        foreach (PlayerSoldier veteran in veterans)
        {
            SoldierEvaluation evaluation = veteran.SoldierEvaluationHistory[0];
            Assert.True(evaluation.MeleeRating > 90, $"{veteran.Name} lacks the Veteran melee baseline.");
            Assert.True(evaluation.RangedRating > 105, $"{veteran.Name} lacks the Veteran ranged baseline.");
            Assert.True(evaluation.MeleeRating > 115 || evaluation.RangedRating > 120,
                $"{veteran.Name} lacks an Adamantium-level melee or ranged spike.");
            if (veteran.Template.Name == "Veteran Sergeant")
            {
                Assert.True(evaluation.LeadershipRating > 60, $"{veteran.Name} lacks Veteran Sergeant leadership.");
            }
        }
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

    private sealed class VeteranCandidateTrainingService : ISoldierTrainingService
    {
        private int _initialEvaluationIndex;

        public void UpdateRatings(Date date, PlayerSoldier soldier)
        {
            EvaluateSoldier(soldier, date);
        }

        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear)
        {
            if (soldier.SoldierEvaluationHistory.Count > 0)
            {
                soldier.AddEvaluation(soldier.SoldierEvaluationHistory[0]);
                return;
            }

            int index = _initialEvaluationIndex++;
            SoldierEvaluation evaluation = index switch
            {
                < 50 => new SoldierEvaluation(trainingFinishedYear, melee: 80, ranged: 80, lead: 90,
                    med: 0, tech: 0, piety: 0, ancient: 0),
                < 70 => new SoldierEvaluation(trainingFinishedYear, melee: 116, ranged: 106, lead: 61,
                    med: 0, tech: 0, piety: 0, ancient: 0),
                < 80 => new SoldierEvaluation(trainingFinishedYear, melee: 91, ranged: 121, lead: 61,
                    med: 0, tech: 0, piety: 0, ancient: 0),
                < 100 => new SoldierEvaluation(trainingFinishedYear, melee: 116, ranged: 106, lead: 40,
                    med: 0, tech: 0, piety: 0, ancient: 0),
                < 120 => new SoldierEvaluation(trainingFinishedYear, melee: 91, ranged: 121, lead: 40,
                    med: 0, tech: 0, piety: 0, ancient: 0),
                _ => new SoldierEvaluation(trainingFinishedYear, melee: 85, ranged: 100, lead: 40,
                    med: 0, tech: 0, piety: 0, ancient: 0)
            };
            soldier.AddEvaluation(evaluation);
        }

        public void ApplySoldierWorkExperience(ISoldier soldier, Squad squad, float points)
        {
        }

        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap, float points = 0.2f)
        {
        }
    }
}
