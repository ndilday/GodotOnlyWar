using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Shared resolution and planning rules for shots directed into a melee scrum.
    /// </summary>
    public static class RangedFriendlyFireRules
    {
        public const float FiringIntoMeleePenalty = -3f;
        public const float NearMissBandWidth = 1f;

        public static bool IsNearMiss(float hitTotal)
        {
            return hitTotal <= 0 && hitTotal >= -NearMissBandWidth;
        }

        /// <summary>
        /// Calculates the probability that a 10.5 + 3z attack roll falls in the stray-shot
        /// band immediately above the pre-roll hit total.
        /// </summary>
        public static float CalculateNearMissProbability(float preRollHitTotal)
        {
            float lowerZ = (preRollHitTotal - 10.5f) / 3f;
            float upperZ = (preRollHitTotal + NearMissBandWidth - 10.5f) / 3f;
            return Math.Max(0,
                GaussianCalculator.ApproximateNormalCDF(upperZ)
                - GaussianCalculator.ApproximateNormalCDF(lowerZ));
        }

        public static float GetParticipantWeight(BattleSoldier participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            return Math.Max(0.01f, participant.Soldier.Size);
        }

        public static float CalculateStrayTargetProbability(
            BattleSoldier participant,
            IEnumerable<BattleSoldier> scrumParticipants)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }
            if (scrumParticipants == null)
            {
                throw new ArgumentNullException(nameof(scrumParticipants));
            }

            List<BattleSoldier> participants = scrumParticipants.ToList();
            float totalWeight = participants.Sum(GetParticipantWeight);
            if (totalWeight <= 0 || !participants.Contains(participant))
            {
                return 0;
            }

            return GetParticipantWeight(participant) / totalWeight;
        }

        /// <summary>
        /// Selects a participant by size from a unit-interval roll. Taking the roll as an input
        /// keeps this distribution reusable and deterministic for callers and tests.
        /// </summary>
        public static BattleSoldier SelectStrayTarget(
            IReadOnlyList<BattleSoldier> scrumParticipants,
            double unitRoll)
        {
            if (scrumParticipants == null)
            {
                throw new ArgumentNullException(nameof(scrumParticipants));
            }
            if (scrumParticipants.Count == 0)
            {
                throw new ArgumentException("At least one scrum participant is required.", nameof(scrumParticipants));
            }
            if (unitRoll < 0 || unitRoll >= 1)
            {
                throw new ArgumentOutOfRangeException(nameof(unitRoll), "Roll must be in [0, 1).");
            }

            float totalWeight = scrumParticipants.Sum(GetParticipantWeight);
            double threshold = unitRoll * totalWeight;
            double cumulative = 0;
            foreach (BattleSoldier participant in scrumParticipants)
            {
                cumulative += GetParticipantWeight(participant);
                if (threshold < cumulative)
                {
                    return participant;
                }
            }

            // Floating-point rounding can only leave us infinitesimally beyond the last bucket.
            return scrumParticipants[^1];
        }
    }
}
