using System.Collections.Generic;
using OnlyWar.Models.Soldiers.Ratings;

namespace OnlyWar.Models.Soldiers
{
    public class SoldierEvaluation
    {
        // Canonical, open-ended store: rating key -> value. New ratings are pure data
        // (rules + save) and require no change here. The named properties below are
        // convenience accessors over the well-known keys for readable call sites.
        private readonly Dictionary<string, float> _ratings;
        public IReadOnlyDictionary<string, float> Ratings => _ratings;
        public Date EvaluationDate { get; }

        public float this[string ratingKey] =>
            _ratings.TryGetValue(ratingKey, out float value) ? value : 0f;

        public float MeleeRating => this[RatingKeys.Melee];
        public float RangedRating => this[RatingKeys.Ranged];
        public float LeadershipRating => this[RatingKeys.Leadership];
        public float MedicalRating => this[RatingKeys.Medical];
        public float TechRating => this[RatingKeys.Tech];
        public float PietyRating => this[RatingKeys.Piety];
        public float AncientRating => this[RatingKeys.Ancient];

        public SoldierEvaluation(Date evaluationDate, IReadOnlyDictionary<string, float> ratings)
        {
            EvaluationDate = evaluationDate;
            _ratings = new Dictionary<string, float>(ratings);
        }

        // Convenience constructor for the seven well-known ratings (tests, legacy callers).
        public SoldierEvaluation(Date evaluationDate, float melee, float ranged, float lead,
                                 float med, float tech, float piety, float ancient)
            : this(evaluationDate, new Dictionary<string, float>
            {
                [RatingKeys.Melee] = melee,
                [RatingKeys.Ranged] = ranged,
                [RatingKeys.Leadership] = lead,
                [RatingKeys.Medical] = med,
                [RatingKeys.Tech] = tech,
                [RatingKeys.Piety] = piety,
                [RatingKeys.Ancient] = ancient
            })
        {
        }
    }
}
