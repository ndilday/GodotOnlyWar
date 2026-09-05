namespace OnlyWar.Models.Recruitment
{
    /// <summary>
    /// Inputs that come from the public Chapter presence on the Home World.
    /// Hidden populations and other factions deliberately have no place in this contract.
    /// OrganicPopulationGrowth is the demographic component for the current week, not
    /// combat, migration, or faction-conversion change.
    /// </summary>
    public sealed class RecruitmentForecastInput
    {
        public long ChapterHomeWorldPopulation { get; init; }
        public double OrganicPopulationGrowth { get; init; }
        public float PlayerReputation { get; init; }
    }

    public sealed class RecruitmentForecast
    {
        public double ChildrenReachingRecruitmentAge { get; init; }
        public double EligibleMaleCohort { get; init; }
        public double UnscreenedBacklog { get; init; }
        public double ScreeningDemand { get; init; }
        public double NonGeneticScreeningCapacity { get; init; }
        public double GeneticScreeningCapacity { get; init; }
        public double SpiritualScreeningCapacity { get; init; }
        public double ScreeningCapacity { get; init; }
        public double ScreeningCoverage { get; init; }
        public double ExpectedScreenedCandidates { get; init; }
        public double PublicCompliance { get; init; }
        public double WeeklyPublicSentimentChange { get; init; }
        public double ExpectedCompliantCandidates { get; init; }
        public double GeneticPassRate { get; init; }
        public double AttributePassRate { get; init; }
        public double ExpectedQualifiedCandidates { get; init; }
        public int AspirantTrainingCapacity { get; init; }
        public int AvailablePhaseZeroPlaces { get; init; }
        public int QualifiedCandidateWaitlist { get; init; }
        public int AvailablePhaseZeroPlacesAfterWaitlist { get; init; }
        public double ExpectedNewPhaseZeroAdmissions { get; init; }
        public double ExpectedCandidateOverflow { get; init; }
        public double ExpectedPhase12Survivors { get; init; }
        public double ExpectedPhase13BattleBrothers { get; init; }
        public double ExpectedPhase12SurvivalRate { get; init; }
        public double ExpectedPhase13SurvivalRate { get; init; }
        public int WeeklyRequisitionCost { get; init; }
        public double SourceAttributeMeanModifierSigma { get; init; }
    }
}
