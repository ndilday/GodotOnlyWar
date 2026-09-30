using OnlyWar.Domain.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign
{
    /// <summary>
    /// Training on Mars (TDD §6.14). A brother with a Mechanicus posting trains
    /// every week against the rules-data profile <see cref="ProfileName"/>, wherever his Armory
    /// squad happens to be: he is off-sector, so the squad's orders, location and Warp transit
    /// do not touch him, and he takes no garrison work experience.
    ///
    /// The profile's entries mirror the Techmarine MOS training with the same relative weights.
    /// At the ordinary weekly rate every Techmarine skill receives several times its MOS points
    /// over the 1,040-week period, which meets the "at least the MOS points" rule.
    /// </summary>
    public static class MechanicusTrainingService
    {
        public const string ProfileName = "mechanicus_mars_training";
        // The ordinary weekly training rate (ChapterUpkeepProcessor): Mars redirects a brother's
        // development rather than adding to or taking from it.
        public const float WeeklyPoints = 0.2f;

        public static bool IsOnMars(ISoldier soldier) =>
            soldier is PlayerSoldier player
            && player.IndividualPosting?.Purpose == IndividualPostingPurpose.Mechanicus;

        public static TrainingProfile FindProfile(IEnumerable<TrainingProfile> profiles) =>
            profiles?.FirstOrDefault(profile => profile?.Name == ProfileName);

        /// <summary>Applies one week of Mars training to every brother on Mars.</summary>
        public static void Train(
            IEnumerable<PlayerSoldier> soldiers,
            IEnumerable<TrainingProfile> profiles,
            float points = WeeklyPoints)
        {
            List<PlayerSoldier> onMars = (soldiers ?? []).Where(IsOnMars).ToList();
            if (onMars.Count == 0) return;
            TrainingProfile profile = FindProfile(profiles)
                ?? throw new InvalidOperationException(
                    $"The rules data has no '{ProfileName}' training profile.");
            foreach (PlayerSoldier soldier in onMars)
            {
                SoldierTrainingCalculator.ApplyProfile(soldier, profile, points);
            }
        }
    }
}
