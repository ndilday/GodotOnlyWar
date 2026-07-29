namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// The source of a data-driven requirement for promotion into a soldier template.
    /// Requirements on a template are conjunctive.
    /// </summary>
    public enum SoldierTemplateRequirementType
    {
        SoldierStat = 0,
        Rating = 1,
        CurrentSpecialistType = 2
    }

    public enum SoldierTemplateRequirementComparison
    {
        Equal = 0,
        GreaterThan = 1,
        GreaterThanOrEqual = 2
    }

    public static class SoldierTemplateRequirementKeys
    {
        public const string PsychicPower = "psychic_power";
        public const string SpecialistType = "specialist_type";
    }

    /// <summary>
    /// One condition a soldier must meet to enter the owning destination template.
    /// <see cref="RequirementKey"/> is interpreted by <see cref="RequirementType"/>.
    /// </summary>
    public sealed class SoldierTemplateRequirement
    {
        public SoldierTemplateRequirementType RequirementType { get; }
        public string RequirementKey { get; }
        public SoldierTemplateRequirementComparison Comparison { get; }
        public float RequiredValue { get; }

        public SoldierTemplateRequirement(
            SoldierTemplateRequirementType requirementType,
            string requirementKey,
            SoldierTemplateRequirementComparison comparison,
            float requiredValue)
        {
            RequirementType = requirementType;
            RequirementKey = requirementKey;
            Comparison = comparison;
            RequiredValue = requiredValue;
        }
    }
}
