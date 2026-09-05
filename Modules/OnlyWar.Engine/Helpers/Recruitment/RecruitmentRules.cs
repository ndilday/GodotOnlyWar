using System;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Helpers.Recruitment
{
    /// <summary>
    /// Central recruitment tuning. Candidate generation and forecasting should both use
    /// these values so displayed expectations match simulation behavior.
    /// </summary>
    public static class RecruitmentRules
    {
        public readonly record struct AgeWindow(
            double MinimumAge,
            double MaximumAgeExclusive)
        {
            public bool Contains(double age) =>
                age >= MinimumAge && age < MaximumAgeExclusive;
        }

        public const double HistoricBirthProxyPopulationRate = 0.00015;
        public const double MaleEligibilityFraction = 0.5;
        public const int FoundingCohortWeeklyEquivalentCount = 156;
        public const float FoundingCohortMinimumAge = 10;
        public const float FoundingCohortMaximumAge = 12;
        public const double AttributeFilterStepSigma = 0.5;
        public const int MinimumAttributeFilterHalfSteps = -4;
        public const int MaximumAttributeFilterHalfSteps = 6;
        public const double FeralAndDeathWorldAttributeMeanModifierSigma = 0.35;

        public const int CandidateMaximumWaitWeeks = 104;
        public const double FoundingCohortWeeklyDecay = 0.01;
        public const int FoundingCohortExpirationWeeks = 104;

        public const int RequisitionPerAssignedRecruiter = 1;
        public const double BaseNonGeneticScreensPerScoutSergeant = 50_000;
        public const double BaseGeneticScreensPerApothecary = 25_000;
        public const double BaseSpiritualScreensPerChaplain = 50_000;
        public const int BaseAspirantsPerScoutSergeant = 8;
        public const int BaseAspirantsPerChaplain = 8;
        public const int BaseImplantationsPerApothecary = 2;

        public const double VoluntaryBaseCompliance = 0.25;
        public const double VoluntaryReputationWeight = 0.55;
        public const double VoluntaryLiberationBonus = 0.20;
        public const double VoluntaryWeeklyPublicSentimentChange = 0;
        public const double TitheBaseCompliance = 0.75;
        public const double TitheReputationWeight = 0.10;
        public const double TitheLiberationBonus = 0.10;
        public const double TitheWeeklyPublicSentimentChange = -0.002;

        public static double GetSourceAttributeMeanModifier(RecruitmentWorldType worldType) =>
            worldType == RecruitmentWorldType.Feral || worldType == RecruitmentWorldType.Death
                ? FeralAndDeathWorldAttributeMeanModifierSigma
                : 0;

        public static AgeWindow GetPhaseAgeWindow(RecruitmentPhase phase) =>
            phase switch
            {
                RecruitmentPhase.Phase0PreImplantation => new(10, 19),
                RecruitmentPhase.Phase1 => new(10, 13),
                RecruitmentPhase.Phase2 => new(12, 14),
                RecruitmentPhase.Phase3 => new(14, 17),
                RecruitmentPhase.Phase4 => new(14, 17),
                RecruitmentPhase.Phase5 => new(14, 17),
                RecruitmentPhase.Phase6 => new(14, 17),
                RecruitmentPhase.Phase7 => new(15, 17),
                RecruitmentPhase.Phase8 => new(15, 17),
                RecruitmentPhase.Phase9 => new(15, 17),
                RecruitmentPhase.Phase10 => new(16, 17),
                RecruitmentPhase.Phase11 => new(16, 18),
                RecruitmentPhase.Phase12 => new(16, 19),
                RecruitmentPhase.Phase13BlackCarapace => new(16, 19),
                _ => throw new ArgumentOutOfRangeException(nameof(phase))
            };

        public static double CalculateFoundingCohortPopulation(
            long chapterHomeWorldPopulation)
        {
            if (chapterHomeWorldPopulation < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chapterHomeWorldPopulation));
            }

            return chapterHomeWorldPopulation
                * HistoricBirthProxyPopulationRate
                * FoundingCohortWeeklyEquivalentCount
                * MaleEligibilityFraction;
        }

        public static double GetStaffEffectiveness(float relevantRating)
        {
            // Ratings normally sit near 100. Preserve a useful floor for junior staff and
            // cap exceptional outliers so one generated character cannot dominate a program.
            return Math.Clamp(0.5 + Math.Max(0, relevantRating) / 100.0, 0.5, 2.0);
        }

        public static double GetChaplainRelevantRating(RecruitmentStaffAssignment assignment) =>
            0.6 * assignment.PietyRating + 0.4 * assignment.LeadershipRating;

        public static double GetPublicCompliance(RecruitmentPolicy policy, float playerReputation)
        {
            double reputation = Math.Clamp(playerReputation, 0, 1);
            return policy switch
            {
                RecruitmentPolicy.VoluntaryPresentation => Math.Clamp(
                    VoluntaryBaseCompliance
                    + VoluntaryReputationWeight * reputation
                    + VoluntaryLiberationBonus,
                    0,
                    1),
                RecruitmentPolicy.PlanetaryTithe => Math.Clamp(
                    TitheBaseCompliance
                    + TitheReputationWeight * reputation
                    + TitheLiberationBonus,
                    0,
                    1),
                _ => throw new ArgumentOutOfRangeException(nameof(policy))
            };
        }

        public static double GetWeeklyPublicSentimentChange(RecruitmentPolicy policy) =>
            policy switch
            {
                RecruitmentPolicy.VoluntaryPresentation =>
                    VoluntaryWeeklyPublicSentimentChange,
                RecruitmentPolicy.PlanetaryTithe =>
                    TitheWeeklyPublicSentimentChange,
                _ => throw new ArgumentOutOfRangeException(nameof(policy))
            };
    }
}
