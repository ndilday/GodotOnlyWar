using System;
using System.Linq;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Helpers.Recruitment
{
    /// <summary>
    /// Deterministic recruitment funnel calculation shared by previews and turn processing.
    /// It does not read global state and does not mutate the program or its inputs.
    /// </summary>
    public sealed class RecruitmentForecastService
    {
        public RecruitmentForecast Calculate(
            RecruitmentProgram program,
            RecruitmentForecastInput input)
        {
            ArgumentNullException.ThrowIfNull(program);
            ArgumentNullException.ThrowIfNull(input);
            Validate(program, input);

            double children = Math.Max(
                0,
                input.OrganicPopulationGrowth
                + RecruitmentRules.HistoricBirthProxyPopulationRate
                * input.ChapterHomeWorldPopulation);
            double eligibleMaleCohort = children * RecruitmentRules.MaleEligibilityFraction;
            double backlog = program.UnscreenedEligiblePopulation;
            double demand = eligibleMaleCohort + backlog;

            double nonGeneticCapacity = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.ScoutSergeant)
                .Sum(staff => RecruitmentRules.BaseNonGeneticScreensPerScoutSergeant
                    * RecruitmentRules.GetStaffEffectiveness(staff.LeadershipRating));
            double geneticCapacity = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.Apothecary)
                .Sum(staff => RecruitmentRules.BaseGeneticScreensPerApothecary
                    * RecruitmentRules.GetStaffEffectiveness(staff.MedicalRating));
            double spiritualCapacity = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.Chaplain)
                .Sum(staff => RecruitmentRules.BaseSpiritualScreensPerChaplain
                    * RecruitmentRules.GetStaffEffectiveness(
                        (float)RecruitmentRules.GetChaplainRelevantRating(staff)));
            double screeningCapacity = Math.Min(
                nonGeneticCapacity,
                Math.Min(geneticCapacity, spiritualCapacity));
            double screened = Math.Min(demand, screeningCapacity);
            double coverage = demand <= 0 ? 0 : screened / demand;

            double compliance = RecruitmentRules.GetPublicCompliance(
                program.Policy,
                input.PlayerReputation);
            double compliantCandidates = screened * compliance;
            double geneticPassRate = 1 - program.MinimumGeneticCompatibility;
            double sourceModifier = RecruitmentRules.GetSourceAttributeMeanModifier(program.WorldType);
            double attributePassRate = CalculateAttributePassRate(
                program.AttributeFilters,
                sourceModifier);
            double qualifiedCandidates =
                compliantCandidates * geneticPassRate * attributePassRate;

            int trainingCapacity = CalculateTrainingCapacity(program);
            int availablePlaces = Math.Max(0, trainingCapacity - program.Aspirants.Count);
            int waitlist = program.QualifiedCandidates.Count;
            int availableAfterWaitlist = Math.Max(0, availablePlaces - waitlist);
            double newAdmissions = Math.Min(qualifiedCandidates, availableAfterWaitlist);
            double overflow = Math.Max(0, qualifiedCandidates - availableAfterWaitlist);

            double phase12Survival = ExpectedCompatibilityPower(
                program.MinimumGeneticCompatibility,
                12);
            double phase13Survival = ExpectedCompatibilityPower(
                program.MinimumGeneticCompatibility,
                13);

            return new RecruitmentForecast
            {
                ChildrenReachingRecruitmentAge = children,
                EligibleMaleCohort = eligibleMaleCohort,
                UnscreenedBacklog = backlog,
                ScreeningDemand = demand,
                NonGeneticScreeningCapacity = nonGeneticCapacity,
                GeneticScreeningCapacity = geneticCapacity,
                SpiritualScreeningCapacity = spiritualCapacity,
                ScreeningCapacity = screeningCapacity,
                ScreeningCoverage = coverage,
                ExpectedScreenedCandidates = screened,
                PublicCompliance = compliance,
                WeeklyPublicSentimentChange =
                    RecruitmentRules.GetWeeklyPublicSentimentChange(program.Policy),
                ExpectedCompliantCandidates = compliantCandidates,
                GeneticPassRate = geneticPassRate,
                AttributePassRate = attributePassRate,
                ExpectedQualifiedCandidates = qualifiedCandidates,
                AspirantTrainingCapacity = trainingCapacity,
                AvailablePhaseZeroPlaces = availablePlaces,
                QualifiedCandidateWaitlist = waitlist,
                AvailablePhaseZeroPlacesAfterWaitlist = availableAfterWaitlist,
                ExpectedNewPhaseZeroAdmissions = newAdmissions,
                ExpectedCandidateOverflow = overflow,
                ExpectedPhase12SurvivalRate = phase12Survival,
                ExpectedPhase13SurvivalRate = phase13Survival,
                ExpectedPhase12Survivors = newAdmissions * phase12Survival,
                ExpectedPhase13BattleBrothers = newAdmissions * phase13Survival,
                WeeklyRequisitionCost =
                    program.StaffAssignments.Count
                    * RecruitmentRules.RequisitionPerAssignedRecruiter,
                SourceAttributeMeanModifierSigma = sourceModifier
            };
        }

        public static double CalculateAttributePassRate(
            RecruitmentAttributeFilters filters,
            double sourceMeanModifierSigma)
        {
            ArgumentNullException.ThrowIfNull(filters);

            double passRate = 1;
            foreach (int halfSigmaSteps in filters.AllHalfSigmaSteps)
            {
                ValidateHalfSigmaSteps(halfSigmaSteps);
                double thresholdSigma =
                    halfSigmaSteps * RecruitmentRules.AttributeFilterStepSigma;
                passRate *= 1 - GaussianCalculator.ApproximateNormalCDF(
                    (float)(thresholdSigma - sourceMeanModifierSigma));
            }

            return passRate;
        }

        public static double ExpectedCompatibilityPower(
            double minimumCompatibility,
            int phaseCount)
        {
            if (minimumCompatibility < 0 || minimumCompatibility > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumCompatibility));
            }
            if (phaseCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseCount));
            }
            if (minimumCompatibility == 1 || phaseCount == 0)
            {
                return 1;
            }

            // For C ~ Uniform(t, 1), E[C^n] is the exact conditional mean below.
            return (1 - Math.Pow(minimumCompatibility, phaseCount + 1))
                / ((phaseCount + 1) * (1 - minimumCompatibility));
        }

        public static int CalculateTrainingCapacity(RecruitmentProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);

            double sergeantCapacity = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.ScoutSergeant)
                .Sum(staff => RecruitmentRules.BaseAspirantsPerScoutSergeant
                    * RecruitmentRules.GetStaffEffectiveness(staff.LeadershipRating));
            double chaplainCapacity = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.Chaplain)
                .Sum(staff => RecruitmentRules.BaseAspirantsPerChaplain
                    * RecruitmentRules.GetStaffEffectiveness(
                        (float)RecruitmentRules.GetChaplainRelevantRating(staff)));

            return Math.Max(0, (int)Math.Floor(Math.Min(sergeantCapacity, chaplainCapacity)));
        }

        public static int CalculateImplantationCapacity(RecruitmentProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);

            double capacity = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.Apothecary)
                .Sum(staff => RecruitmentRules.BaseImplantationsPerApothecary
                    * RecruitmentRules.GetStaffEffectiveness(staff.MedicalRating));
            return Math.Max(0, (int)Math.Floor(capacity));
        }

        private static void Validate(
            RecruitmentProgram program,
            RecruitmentForecastInput input)
        {
            if (input.ChapterHomeWorldPopulation < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.ChapterHomeWorldPopulation));
            }
            if (double.IsNaN(input.OrganicPopulationGrowth)
                || double.IsInfinity(input.OrganicPopulationGrowth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.OrganicPopulationGrowth));
            }
            if (float.IsNaN(input.PlayerReputation)
                || float.IsInfinity(input.PlayerReputation))
            {
                throw new ArgumentOutOfRangeException(nameof(input.PlayerReputation));
            }
            if (program.MinimumGeneticCompatibility < 0
                || program.MinimumGeneticCompatibility > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(program.MinimumGeneticCompatibility));
            }
            if (program.AttributeFilters == null)
            {
                throw new ArgumentException(
                    "Recruitment attribute filters are required.",
                    nameof(program));
            }
            if (program.StaffAssignments
                .GroupBy(assignment => assignment.SoldierId)
                .Any(group => group.Count() > 1))
            {
                throw new ArgumentException(
                    "A soldier may hold only one recruitment staff assignment.",
                    nameof(program));
            }
        }

        private static void ValidateHalfSigmaSteps(int halfSigmaSteps)
        {
            if (halfSigmaSteps < RecruitmentRules.MinimumAttributeFilterHalfSteps
                || halfSigmaSteps > RecruitmentRules.MaximumAttributeFilterHalfSteps)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfSigmaSteps),
                    $"Attribute filters must be between "
                    + $"{RecruitmentRules.MinimumAttributeFilterHalfSteps} and "
                    + $"{RecruitmentRules.MaximumAttributeFilterHalfSteps} half-sigma steps.");
            }
        }
    }
}
