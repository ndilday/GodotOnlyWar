using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Campaign;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Soldiers.Ratings;
using OnlyWar.Domain.Squads;
using OnlyWar.Medical.Readiness;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace OnlyWar.Tests.Generation;

/// <summary>
/// Mars pipeline (TDD §6.14): the chapter founds with no Techmarine
/// present. Every worthy Techmarine candidate left for Mars at the end of the first training phase
/// and waits in the Armory, off-sector, while the Adeptus Mechanicus lends the chapter tech-priests.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MarsFoundingTests
{
    private static readonly Date TrainingStart = new(39, 496, 1);
    private static readonly Date GameStart = new(39, 500, 1);

    private readonly GameRulesData _data;
    private readonly ITestOutputHelper _output;

    public MarsFoundingTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
    }

    [Fact]
    public void Founding_SendsTheWholeWorthyCohortToMars_AndNoTechmarineIsPresent()
    {
        RNG.Reset(20260929);
        PlayerForce chapter = CreateChapter();
        ChapterGenerationDoctrine doctrine = _data.ChapterDoctrine;
        List<PlayerSoldier> members = chapter.Army.PlayerSoldierMap.Values.ToList();
        List<PlayerSoldier> cohort = members.Where(MechanicusTrainingService.IsOnMars).ToList();
        Squad armory = chapter.Army.OrderOfBattle.Squads.Single(s => s.SquadTemplate == doctrine.Armory);

        Assert.NotEmpty(cohort);
        Assert.True(chapter.IsMechanicusLoanActive);
        Assert.Equal(1000, members.Count);

        // No Techmarine or Master of the Forge is present anywhere, and the Armory holds only
        // the absent cohort.
        Assert.DoesNotContain(members, soldier =>
            (soldier.Template == doctrine.Techmarine || soldier.Template == doctrine.MasterOfTheForge)
            && !MechanicusTrainingService.IsOnMars(soldier));
        Assert.Equal(cohort.Select(s => s.Id).OrderBy(id => id),
            armory.Members.Select(s => s.Id).OrderBy(id => id));
        Assert.Equal(0, SoldierPresenceService.PresentCount(armory));

        Date departure = Date.FromTotalWeeks(TrainingStart.GetTotalWeeks() + 105);
        Assert.All(cohort, soldier =>
        {
            Assert.Same(doctrine.Techmarine, soldier.Template);
            Assert.True(soldier.IndividualPosting.Location.IsOffSector);
            Assert.Equal(departure, soldier.IndividualPosting.StartedDate);
            Assert.Equal(MechanicusDepartureService.ExpectedReturnDate(departure),
                soldier.IndividualPosting.ExpectedReturnDate);
            Assert.Equal(DutyReadinessReasonCode.OffSector,
                DutyReadinessService.Evaluate(soldier).ReasonCode);
            Assert.Contains(soldier.SoldierEvents, entry => entry.Detail.Contains("Mars"));
            Assert.True(IsWorthy(soldier), $"{soldier.Name} fails the worthiness test.");
        });

        // Every candidate went: nobody left behind meets both parts of the test.
        Assert.DoesNotContain(members.Except(cohort), soldier =>
            soldier.PsychicPower <= 0 && IsWorthy(soldier));
    }

    // The cohort skips the phase-2 MOS training and is credited Mars training for the weeks
    // between departure and game start instead.
    [Fact]
    public void Founding_CreditsPreStartMarsTrainingInPlaceOfTheLineMos()
    {
        RNG.Reset(20260930);
        PlayerForce chapter = CreateChapter();
        List<PlayerSoldier> cohort = chapter.Army.PlayerSoldierMap.Values
            .Where(MechanicusTrainingService.IsOnMars)
            .ToList();
        BaseSkill servoArm = _data.ChapterDoctrine.Techmarine.MosTraining
            .Select(mos => mos.Item1)
            .First(skill => skill.Name == "Servo-Arm");
        int weeksOnMars = GameStart.GetTotalWeeks() - cohort[0].IndividualPosting.StartedDate.GetTotalWeeks();
        TrainingProfile profile = MechanicusTrainingService.FindProfile(_data.TrainingProfiles.Values);
        float servoArmWeight = profile.Entries.Where(entry => entry.Skill?.Id == servoArm.Id).Sum(entry => entry.Weight);
        float expected = MechanicusTrainingService.WeeklyPoints * weeksOnMars
            * servoArmWeight / profile.Entries.Sum(entry => entry.Weight);

        Assert.True(weeksOnMars > 100);
        Assert.All(cohort, soldier =>
            Assert.Equal(expected, Points(soldier, servoArm), 3));
    }

    [Fact]
    public void Founding_TheDirectiveStatesTheLoanAndWhenItEnds()
    {
        RNG.Reset(20261001);
        PlayerForce chapter = CreateChapter();
        List<PlayerSoldier> cohort = chapter.Army.PlayerSoldierMap.Values
            .Where(MechanicusTrainingService.IsOnMars)
            .ToList();
        OnlyWar.Generation.Abstractions.BriefingTokens tokens = new()
        {
            ChapterName = "Heart of the Emperor",
            PlanetName = "Calderis",
            SubsectorName = "Meridian Subsector",
            AuthorityName = "Vandire",
            AuthorityTitle = "Lord of the Sector",
            EnemyName = "Tyranids",
            MarsCohortCount = cohort.Count,
            MarsCohortReturnDate = cohort[0].IndividualPosting.ExpectedReturnDate
        };

        string briefing = OnlyWar.Campaign.Narrative.BriefingComposer.ComposePromisedWorldBriefing(tokens);
        string invasion = OnlyWar.Campaign.Narrative.BriefingComposer.ComposeInvasionPromisedWorldBriefing(tokens);

        foreach (string text in new[] { briefing, invasion })
        {
            Assert.Contains("Adeptus Mechanicus", text);
            Assert.Contains($"{cohort.Count} of your brothers", text);
            Assert.Contains(tokens.MarsCohortReturnDate.ToString(), text);
        }
    }

    // Measured 2026-09-29 over these 40 seeds with the worthiness test: min 5, max 25, mean 13.2.
    // The band holds that spread with a little room; it guards the pipeline, not the tuning.
    [Trait("Category", "Slow")]
    [Fact]
    public void Founding_CohortCountStaysInItsBandAcrossSeeds()
    {
        List<int> counts = [];
        for (int seed = 1; seed <= 40; seed++)
        {
            RNG.Reset(seed);
            PlayerForce chapter = CreateChapter();
            counts.Add(chapter.Army.PlayerSoldierMap.Values.Count(MechanicusTrainingService.IsOnMars));
        }
        string summary = $"min {counts.Min()}, max {counts.Max()}, mean {counts.Average():F1}: "
            + string.Join(",", counts);
        _output.WriteLine(summary);

        Assert.True(counts.All(count => count >= MinimumCohort && count <= MaximumCohort), summary);
    }

    private const int MinimumCohort = 3;
    private const int MaximumCohort = 30;

    private bool IsWorthy(PlayerSoldier soldier)
    {
        SoldierEvaluation e = soldier.SoldierEvaluationHistory[0];
        float Rating(RatingConsumerRole role) => _data.RatingConsumers.Get(e, role);
        float melee = Rating(RatingConsumerRole.MeleeCombat);
        float ranged = Rating(RatingConsumerRole.RangedCombat);
        bool tactical = melee > 90 && ranged > 105;
        bool assault = melee > 90 && ranged > 95 && ranged < 105;
        bool devastator = melee > 80 && melee < 90 && ranged > 95;
        bool veteran = tactical && (melee > 115 || ranged > 120);
        return Rating(RatingConsumerRole.TechnicalCapability) > 60
            && (tactical || assault || devastator || veteran);
    }

    private static float Points(ISoldier soldier, BaseSkill skill) =>
        soldier.Skills.SingleOrDefault(s => s.BaseSkill.Id == skill.Id)?.PointsInvested ?? 0;

    private PlayerForce CreateChapter() =>
        TestGeneration.CreateChapter(_data, CreateTrainingService(), TrainingStart, GameStart, "Mars Founding");

    private ISoldierTrainingService CreateTrainingService()
    {
        RatingCalculator ratingCalculator = new(
            _data.RatingDefinitions,
            _data.RatingAwardTiers,
            _data.BaseSkillMap,
            new StaticRNG());
        return new SoldierTrainingCalculator(
            _data.BaseSkillMap.Values,
            _data.TrainingProfiles.Values,
            ratingCalculator,
            scoutTrainingOptions: _data.ScoutTrainingOptions.Options);
    }
}
