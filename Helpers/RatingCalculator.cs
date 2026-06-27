using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using Attribute = OnlyWar.Models.Soldiers.Attribute;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Computes soldier ratings and applies rating-driven awards from data-driven
    /// <see cref="RatingDefinition"/>s and <see cref="RatingAwardTier"/>s, rather than
    /// hardcoded formulas. See Design/DataDrivenRatings.md.
    /// </summary>
    public sealed class RatingCalculator
    {
        private const string BestSkillInCategoryPlaceholder = "{bestSkillInCategory}";

        private readonly IReadOnlyList<RatingDefinition> _definitions;
        private readonly IReadOnlyList<RatingAwardTier> _awardTiers;
        private readonly IReadOnlyDictionary<int, BaseSkill> _skillsById;
        private readonly IRNG _rng;

        public RatingCalculator(IEnumerable<RatingDefinition> definitions,
                                IEnumerable<RatingAwardTier> awardTiers,
                                IReadOnlyDictionary<int, BaseSkill> skillsById,
                                IRNG rng)
        {
            _definitions = definitions.ToList();
            _awardTiers = awardTiers?.ToList() ?? new List<RatingAwardTier>();
            _skillsById = skillsById;
            _rng = rng;
        }

        public SoldierEvaluation Evaluate(ISoldier soldier, Date date)
        {
            Dictionary<string, float> ratings = new();
            foreach (RatingDefinition definition in _definitions)
            {
                ratings[definition.Key] = EvaluateDefinition(definition, soldier);
            }
            return new SoldierEvaluation(date, ratings);
        }

        private float EvaluateDefinition(RatingDefinition definition, ISoldier soldier)
        {
            IEnumerable<float> values = definition.Components
                .OrderBy(c => c.Ordinal)
                .Select(c => ComponentValue(c, soldier));

            float aggregate = definition.Aggregation == RatingAggregation.Product
                ? values.Aggregate(1f, (acc, v) => acc * v)
                : values.Sum();

            double divisor = definition.NormalizationFactors
                .OrderBy(f => f.Ordinal)
                .Aggregate(1.0, (acc, f) => acc * _rng.GetDoubleInRange(f.Low, f.High));

            return divisor == 0 ? 0f : (float)(aggregate / divisor);
        }

        private float ComponentValue(RatingComponent component, ISoldier soldier)
        {
            switch (component.ComponentType)
            {
                case RatingComponentType.AttributeValue:
                    return AttributeValueOf((Attribute)component.TargetId, soldier);
                case RatingComponentType.SkillTotal:
                    return soldier.GetTotalSkillValue(_skillsById[component.TargetId]);
                case RatingComponentType.BestSkillBonusInCategory:
                    return soldier.GetBestSkillInCategory((SkillCategory)component.TargetId).SkillBonus;
                case RatingComponentType.BestSkillTotalInCategory:
                    return soldier.GetTotalSkillValue(
                        soldier.GetBestSkillInCategory((SkillCategory)component.TargetId).BaseSkill);
                default:
                    return 0f;
            }
        }

        // Mirrors Soldier.GetStatForBaseAttribute (Presence maps to Charisma) using only
        // the ISoldier surface, since GetStatForBaseAttribute is not on the interface.
        private static float AttributeValueOf(Attribute attribute, ISoldier soldier)
        {
            return attribute switch
            {
                Attribute.Strength => soldier.Strength,
                Attribute.Dexterity => soldier.Dexterity,
                Attribute.Constitution => soldier.Constitution,
                Attribute.Intelligence => soldier.Intelligence,
                Attribute.Ego => soldier.Ego,
                Attribute.Presence => soldier.Charisma,
                _ => soldier.Dexterity
            };
        }

        /// <summary>
        /// Applies the highest award/flag tier whose threshold each rating exceeds.
        /// Replaces the hardcoded medal/flag block in soldier evaluation.
        /// </summary>
        public void ApplyAwards(PlayerSoldier soldier, SoldierEvaluation evaluation, Date date)
        {
            foreach (IGrouping<string, RatingAwardTier> tiersForRating in _awardTiers.GroupBy(t => t.RatingKey))
            {
                float ratingValue = evaluation[tiersForRating.Key];
                RatingAwardTier best = tiersForRating
                    .Where(t => ratingValue > t.Threshold)
                    .OrderByDescending(t => t.Level)
                    .FirstOrDefault();
                if (best == null)
                {
                    continue;
                }

                string name = ResolveAwardName(best, tiersForRating.Key, soldier);
                if (best.Effect == RatingAwardEffect.HistoryFlag)
                {
                    soldier.AddEvent(new SoldierEvent(date, SoldierEventType.RatingFlag, name));
                }
                else
                {
                    AwardSoldier(soldier, date, name, best.AwardType, best.Level);
                }
            }
        }

        private string ResolveAwardName(RatingAwardTier tier, string ratingKey, ISoldier soldier)
        {
            if (!tier.NameTemplate.Contains(BestSkillInCategoryPlaceholder))
            {
                return tier.NameTemplate;
            }
            SkillCategory category = BestSkillCategoryFor(ratingKey);
            string skillName = soldier.GetBestSkillInCategory(category).BaseSkill.Name;
            return tier.NameTemplate.Replace(BestSkillInCategoryPlaceholder, skillName);
        }

        // The category for a {bestSkillInCategory} placeholder is taken from the rating's
        // best-skill component, so the award names the same category the rating measures.
        private SkillCategory BestSkillCategoryFor(string ratingKey)
        {
            RatingDefinition definition = _definitions.FirstOrDefault(d => d.Key == ratingKey)
                ?? throw new InvalidOperationException(
                    $"Award tier references rating '{ratingKey}', which has no definition.");
            RatingComponent best = definition.Components.FirstOrDefault(c =>
                c.ComponentType is RatingComponentType.BestSkillBonusInCategory
                    or RatingComponentType.BestSkillTotalInCategory)
                ?? throw new InvalidOperationException(
                    $"Award name for rating '{ratingKey}' uses {BestSkillInCategoryPlaceholder} " +
                    "but the rating has no best-skill-in-category component.");
            return (SkillCategory)best.TargetId;
        }

        private static void AwardSoldier(PlayerSoldier soldier, Date awardDate, string awardName,
                                         string type, int level)
        {
            if (!soldier.SoldierAwards.Any(a => a.Type == type && a.Level >= level))
            {
                soldier.AddEvent(new SoldierEvent(awardDate, SoldierEventType.AwardReceived, "Awarded " + awardName));
                soldier.AddAward(new SoldierAward(awardDate, awardName, type, (ushort)level));
            }
        }
    }
}
