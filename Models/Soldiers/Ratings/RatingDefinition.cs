using System.Collections.Generic;

namespace OnlyWar.Models.Soldiers.Ratings
{
    /// <summary>
    /// Stable string keys for the ratings the game logic references directly. The set
    /// of ratings is otherwise open-ended (data-driven); these constants exist so code
    /// that cares about a specific rating (chapter generation, UI) avoids magic strings.
    /// See Design/DataDrivenRatings.md.
    /// </summary>
    public static class RatingKeys
    {
        public const string Melee = "melee";
        public const string Ranged = "ranged";
        public const string Leadership = "leadership";
        public const string Ancient = "ancient";
        public const string Medical = "medical";
        public const string Tech = "tech";
        public const string Piety = "piety";
    }

    public enum RatingAggregation
    {
        Product = 0,
        Sum = 1
    }

    public enum RatingComponentType
    {
        AttributeValue = 0,
        SkillTotal = 1,
        BestSkillBonusInCategory = 2,
        BestSkillTotalInCategory = 3
    }

    /// <summary>
    /// One input to a rating formula. <see cref="TargetId"/> is interpreted by
    /// <see cref="ComponentType"/>: an <see cref="Attribute"/> value, a
    /// <see cref="BaseSkill"/> id, or a <see cref="SkillCategory"/> value.
    /// </summary>
    public sealed class RatingComponent
    {
        public RatingComponentType ComponentType { get; }
        public int TargetId { get; }
        public int Ordinal { get; }

        public RatingComponent(RatingComponentType componentType, int targetId, int ordinal)
        {
            ComponentType = componentType;
            TargetId = targetId;
            Ordinal = ordinal;
        }
    }

    /// <summary>
    /// One uniform normalization factor. The evaluator samples each factor once and
    /// divides the aggregated component value by the product of the samples.
    /// </summary>
    public sealed class RatingNormalizationFactor
    {
        public double Low { get; }
        public double High { get; }
        public int Ordinal { get; }

        public RatingNormalizationFactor(double low, double high, int ordinal)
        {
            Low = low;
            High = high;
            Ordinal = ordinal;
        }
    }

    /// <summary>
    /// A data-driven rating formula: <c>Aggregate(components) / Π sample(factor)</c>.
    /// </summary>
    public sealed class RatingDefinition
    {
        public int Id { get; }
        public string Key { get; }
        public string DisplayName { get; }
        public RatingAggregation Aggregation { get; }
        public IReadOnlyList<RatingComponent> Components { get; }
        public IReadOnlyList<RatingNormalizationFactor> NormalizationFactors { get; }

        public RatingDefinition(int id, string key, string displayName, RatingAggregation aggregation,
                                IReadOnlyList<RatingComponent> components,
                                IReadOnlyList<RatingNormalizationFactor> normalizationFactors)
        {
            Id = id;
            Key = key;
            DisplayName = displayName;
            Aggregation = aggregation;
            Components = components;
            NormalizationFactors = normalizationFactors;
        }
    }
}
