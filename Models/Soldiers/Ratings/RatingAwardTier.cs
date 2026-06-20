namespace OnlyWar.Models.Soldiers.Ratings
{
    public enum RatingAwardEffect
    {
        /// <summary>Grants a <see cref="SoldierAward"/> of <see cref="RatingAwardTier.AwardType"/>.</summary>
        Award = 0,
        /// <summary>Adds a flavor entry to the soldier's history (no award object).</summary>
        HistoryFlag = 1
    }

    /// <summary>
    /// A data-driven award/flag threshold tied to a rating. For a given rating, the
    /// highest <see cref="Level"/> whose <see cref="Threshold"/> is exceeded is applied.
    /// <see cref="NameTemplate"/> may contain the placeholder <c>{bestSkillInCategory}</c>,
    /// replaced with the soldier's best skill name in the rating's category.
    /// See Design/DataDrivenRatings.md §3.
    /// </summary>
    public sealed class RatingAwardTier
    {
        public int Id { get; }
        public string RatingKey { get; }
        public int Level { get; }
        public double Threshold { get; }
        public RatingAwardEffect Effect { get; }
        public string AwardType { get; }
        public string NameTemplate { get; }

        public RatingAwardTier(int id, string ratingKey, int level, double threshold,
                               RatingAwardEffect effect, string awardType, string nameTemplate)
        {
            Id = id;
            RatingKey = ratingKey;
            Level = level;
            Threshold = threshold;
            Effect = effect;
            AwardType = awardType;
            NameTemplate = nameTemplate;
        }
    }
}
