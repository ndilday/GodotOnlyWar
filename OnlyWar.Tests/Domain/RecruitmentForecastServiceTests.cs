using System;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models.Recruitment;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class RecruitmentForecastServiceTests
{
    private readonly RecruitmentForecastService _service = new();

    [Fact]
    public void Calculate_UsesHomeWorldPopulationAndOrganicGrowthForEligibleCohort()
    {
        RecruitmentProgram program = CreateStaffedProgram();

        RecruitmentForecast forecast = _service.Calculate(program, new RecruitmentForecastInput
        {
            ChapterHomeWorldPopulation = 1_000_000,
            OrganicPopulationGrowth = 50,
            PlayerReputation = 1
        });

        Assert.Equal(200, forecast.ChildrenReachingRecruitmentAge, 6);
        Assert.Equal(100, forecast.EligibleMaleCohort, 6);
    }

    [Fact]
    public void Calculate_ClampsNegativeBirthProxyAtZero()
    {
        RecruitmentForecast forecast = _service.Calculate(
            CreateStaffedProgram(),
            new RecruitmentForecastInput
            {
                ChapterHomeWorldPopulation = 1_000,
                OrganicPopulationGrowth = -1_000,
                PlayerReputation = 1
            });

        Assert.Equal(0, forecast.ChildrenReachingRecruitmentAge);
        Assert.Equal(0, forecast.EligibleMaleCohort);
    }

    [Fact]
    public void FoundingCohort_RepresentsThreeYearsOfMaleSurvivorChildren()
    {
        const long chapterPopulation = 1_000_000;

        double population =
            RecruitmentRules.CalculateFoundingCohortPopulation(chapterPopulation);

        Assert.Equal(
            chapterPopulation
            * RecruitmentRules.HistoricBirthProxyPopulationRate
            * 156
            * RecruitmentRules.MaleEligibilityFraction,
            population,
            6);
        Assert.Equal(10, RecruitmentRules.FoundingCohortMinimumAge);
        Assert.Equal(12, RecruitmentRules.FoundingCohortMaximumAge);
    }

    [Fact]
    public void PhaseAgeWindows_RequireBlackCarapaceBeforeNineteenthBirthday()
    {
        RecruitmentRules.AgeWindow window = RecruitmentRules.GetPhaseAgeWindow(
            RecruitmentPhase.Phase13BlackCarapace);

        Assert.True(window.Contains(18.99));
        Assert.False(window.Contains(19));
    }

    [Fact]
    public void Calculate_RequiresAllThreeStaffAxesForScreening()
    {
        RecruitmentProgram program = CreateStaffedProgram();
        program.StaffAssignments.RemoveAll(
            assignment => assignment.Role == RecruitmentStaffRole.Chaplain);

        RecruitmentForecast forecast = _service.Calculate(program, StandardInput());

        Assert.True(forecast.NonGeneticScreeningCapacity > 0);
        Assert.True(forecast.GeneticScreeningCapacity > 0);
        Assert.Equal(0, forecast.SpiritualScreeningCapacity);
        Assert.Equal(0, forecast.ScreeningCapacity);
        Assert.Equal(0, forecast.ExpectedQualifiedCandidates);
        Assert.Equal(0, forecast.AspirantTrainingCapacity);
    }

    [Fact]
    public void Calculate_UsesLeadershipMedicalAndChaplainPietyLeadershipBlend()
    {
        RecruitmentProgram program = new();
        program.StaffAssignments.Add(new RecruitmentStaffAssignment(
            1, RecruitmentStaffRole.ScoutSergeant, leadershipRating: 100));
        program.StaffAssignments.Add(new RecruitmentStaffAssignment(
            2, RecruitmentStaffRole.Apothecary, medicalRating: 100));
        program.StaffAssignments.Add(new RecruitmentStaffAssignment(
            3, RecruitmentStaffRole.Chaplain, leadershipRating: 50, pietyRating: 100));

        RecruitmentForecast forecast = _service.Calculate(program, StandardInput());

        Assert.Equal(
            RecruitmentRules.BaseNonGeneticScreensPerScoutSergeant * 1.5,
            forecast.NonGeneticScreeningCapacity,
            6);
        Assert.Equal(
            RecruitmentRules.BaseGeneticScreensPerApothecary * 1.5,
            forecast.GeneticScreeningCapacity,
            6);
        Assert.Equal(
            RecruitmentRules.BaseSpiritualScreensPerChaplain * 1.3,
            forecast.SpiritualScreeningCapacity,
            6);
    }

    [Fact]
    public void PublicCompliance_VoluntaryRespondsMoreStronglyToReputation()
    {
        double voluntaryLow = RecruitmentRules.GetPublicCompliance(
            RecruitmentPolicy.VoluntaryPresentation, 0);
        double voluntaryHigh = RecruitmentRules.GetPublicCompliance(
            RecruitmentPolicy.VoluntaryPresentation, 1);
        double titheLow = RecruitmentRules.GetPublicCompliance(
            RecruitmentPolicy.PlanetaryTithe, 0);
        double titheHigh = RecruitmentRules.GetPublicCompliance(
            RecruitmentPolicy.PlanetaryTithe, 1);

        Assert.True(voluntaryHigh - voluntaryLow > titheHigh - titheLow);
        Assert.True(titheLow > voluntaryLow);
        Assert.InRange(voluntaryHigh, 0, 1);
        Assert.InRange(titheHigh, 0, 1);
        Assert.Equal(0, RecruitmentRules.GetWeeklyPublicSentimentChange(
            RecruitmentPolicy.VoluntaryPresentation));
        Assert.True(RecruitmentRules.GetWeeklyPublicSentimentChange(
            RecruitmentPolicy.PlanetaryTithe) < 0);
    }

    [Fact]
    public void AttributeFilters_MultiplyFiveIndependentHalfSigmaPassRates()
    {
        RecruitmentAttributeFilters filters = new()
        {
            StrengthHalfSigmaSteps = 0,
            ConstitutionHalfSigmaSteps = 0,
            IntelligenceHalfSigmaSteps = 0,
            DexterityHalfSigmaSteps = 0,
            EgoHalfSigmaSteps = 0
        };

        double actual = RecruitmentForecastService.CalculateAttributePassRate(filters, 0);

        Assert.Equal(System.Math.Pow(0.5, 5), actual, 5);
    }

    [Fact]
    public void SourceModifier_IsSharedByFeralAndDeathWorldsAndImprovesPassRate()
    {
        RecruitmentAttributeFilters filters = new();
        double standardModifier = RecruitmentRules.GetSourceAttributeMeanModifier(
            RecruitmentWorldType.Standard);
        double feralModifier = RecruitmentRules.GetSourceAttributeMeanModifier(
            RecruitmentWorldType.Feral);
        double deathModifier = RecruitmentRules.GetSourceAttributeMeanModifier(
            RecruitmentWorldType.Death);

        double standardPass = RecruitmentForecastService.CalculateAttributePassRate(
            filters, standardModifier);
        double feralPass = RecruitmentForecastService.CalculateAttributePassRate(
            filters, feralModifier);
        double deathPass = RecruitmentForecastService.CalculateAttributePassRate(
            filters, deathModifier);

        Assert.Equal(feralModifier, deathModifier);
        Assert.Equal(feralPass, deathPass, 10);
        Assert.True(feralPass > standardPass);
    }

    [Fact]
    public void Calculate_UsesUniformCompatibilityThresholdForScreening()
    {
        RecruitmentProgram program = CreateStaffedProgram();
        program.MinimumGeneticCompatibility = 0.9f;

        RecruitmentForecast forecast = _service.Calculate(program, StandardInput());

        Assert.Equal(0.1, forecast.GeneticPassRate, 6);
    }

    [Fact]
    public void ExpectedCompatibilityPower_IsExactConditionalUniformMean()
    {
        const double threshold = 0.9;

        double phase12 = RecruitmentForecastService.ExpectedCompatibilityPower(
            threshold, 12);
        double phase13 = RecruitmentForecastService.ExpectedCompatibilityPower(
            threshold, 13);

        double expected12 = (1 - System.Math.Pow(threshold, 13))
            / (13 * (1 - threshold));
        double expected13 = (1 - System.Math.Pow(threshold, 14))
            / (14 * (1 - threshold));
        Assert.Equal(expected12, phase12, 12);
        Assert.Equal(expected13, phase13, 12);
        Assert.True(phase13 < phase12);
        Assert.InRange(phase13, 0.55, 0.57);
    }

    [Fact]
    public void Calculate_ReservesPhaseZeroPlacesForExistingCandidateWaitlist()
    {
        RecruitmentProgram program = CreateStaffedProgram();
        for (int i = 0; i < 10; i++)
        {
            program.QualifiedCandidates.Add(new RecruitmentCandidate { Id = i + 1 });
        }

        RecruitmentForecast forecast = _service.Calculate(program, StandardInput());

        Assert.True(forecast.AvailablePhaseZeroPlaces > 0);
        Assert.Equal(
            System.Math.Max(0, forecast.AvailablePhaseZeroPlaces - 10),
            forecast.AvailablePhaseZeroPlacesAfterWaitlist);
        Assert.Equal(
            System.Math.Min(
                forecast.ExpectedQualifiedCandidates,
                forecast.AvailablePhaseZeroPlacesAfterWaitlist),
            forecast.ExpectedNewPhaseZeroAdmissions,
            6);
    }

    [Fact]
    public void Calculate_ChargesForAllAssignedRecruitmentStaff()
    {
        RecruitmentProgram program = CreateStaffedProgram();

        RecruitmentForecast forecast = _service.Calculate(program, StandardInput());

        Assert.Equal(3 * RecruitmentRules.RequisitionPerAssignedRecruiter,
            forecast.WeeklyRequisitionCost);
    }

    [Theory]
    [InlineData(0, "0 per week")]
    [InlineData(0.25, "approximately 1 every 4 weeks")]
    [InlineData(0.75, "approximately 1 every 2 weeks")]
    [InlineData(1, "1 per week")]
    [InlineData(2.5, "2.5 per week")]
    public void RateFormatter_MakesSubWeeklyRatesLegible(double rate, string expected)
    {
        Assert.Equal(expected, RecruitmentRateFormatter.FormatWeekly(rate));
    }

    private static RecruitmentProgram CreateStaffedProgram()
    {
        RecruitmentProgram program = new()
        {
            Policy = RecruitmentPolicy.VoluntaryPresentation,
            MinimumGeneticCompatibility = 0.9f
        };
        program.StaffAssignments.Add(new RecruitmentStaffAssignment(
            1, RecruitmentStaffRole.ScoutSergeant, leadershipRating: 100));
        program.StaffAssignments.Add(new RecruitmentStaffAssignment(
            2, RecruitmentStaffRole.Apothecary, medicalRating: 100));
        program.StaffAssignments.Add(new RecruitmentStaffAssignment(
            3, RecruitmentStaffRole.Chaplain, leadershipRating: 100, pietyRating: 100));
        return program;
    }

    private static RecruitmentForecastInput StandardInput() => new()
    {
        ChapterHomeWorldPopulation = 100_000_000,
        OrganicPopulationGrowth = 0,
        PlayerReputation = 1
    };
}
