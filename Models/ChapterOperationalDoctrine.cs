using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;

namespace OnlyWar.Models
{
    /// <summary>
    /// Player-Chapter policy for deciding which physically deployable brothers are withheld
    /// from a new engagement. This is campaign state, not rules data.
    /// </summary>
    public sealed class ChapterOperationalDoctrine
    {
        public const int DefaultMinimumDutyReadySquadStrength = 5;

        public static IReadOnlyList<WoundLevel?> InjuryThresholdOptions { get; } =
            new WoundLevel?[]
            {
                null,
                WoundLevel.Critical,
                WoundLevel.Major,
                WoundLevel.Moderate,
                WoundLevel.Minor,
                WoundLevel.Negligible
            };

        private WoundLevel? _injuryThreshold = WoundLevel.Major;
        private int _minimumDutyReadySquadStrength = DefaultMinimumDutyReadySquadStrength;

        /// <summary>
        /// Inclusive worst-wound threshold. Null is the explicit Incapacitated policy: wounds
        /// alone never withhold a brother, while physical eligibility and procedure reservations
        /// remain unconditional exclusions.
        /// </summary>
        public WoundLevel? InjuryThreshold
        {
            get => _injuryThreshold;
            set => _injuryThreshold = NormalizeThreshold(value);
        }

        // Friendly aliases for callers that phrase this setting as the UI does.
        public WoundLevel? UnfitForDutyThreshold
        {
            get => InjuryThreshold;
            set => InjuryThreshold = value;
        }

        public bool RequireDutyReadySquadLeader { get; set; } = true;

        public int MinimumDutyReadySquadStrength
        {
            get => _minimumDutyReadySquadStrength;
            set => _minimumDutyReadySquadStrength = Math.Max(1, value);
        }

        public bool IsIncapacitatedPolicy => !InjuryThreshold.HasValue;

        public static ChapterOperationalDoctrine CreateDefault() => new();

        public ChapterOperationalDoctrine() { }

        public ChapterOperationalDoctrine(
            WoundLevel? injuryThreshold,
            bool requireDutyReadySquadLeader = true,
            int minimumDutyReadySquadStrength = DefaultMinimumDutyReadySquadStrength)
        {
            InjuryThreshold = injuryThreshold;
            RequireDutyReadySquadLeader = requireDutyReadySquadLeader;
            MinimumDutyReadySquadStrength = minimumDutyReadySquadStrength;
        }

        public void Set(
            WoundLevel? injuryThreshold,
            bool requireDutyReadySquadLeader,
            int minimumDutyReadySquadStrength)
        {
            InjuryThreshold = injuryThreshold;
            RequireDutyReadySquadLeader = requireDutyReadySquadLeader;
            MinimumDutyReadySquadStrength = minimumDutyReadySquadStrength;
        }

        public void ReplaceWith(ChapterOperationalDoctrine source)
        {
            if (source == null)
            {
                Set(WoundLevel.Major, true, DefaultMinimumDutyReadySquadStrength);
                return;
            }

            Set(source.InjuryThreshold,
                source.RequireDutyReadySquadLeader,
                source.MinimumDutyReadySquadStrength);
        }

        public ChapterOperationalDoctrine DeepCopy() => new(
            InjuryThreshold,
            RequireDutyReadySquadLeader,
            MinimumDutyReadySquadStrength);

        public static string DescribeThreshold(WoundLevel? threshold) => threshold switch
        {
            null => "Incapacitated",
            WoundLevel.Critical => "Critical",
            WoundLevel.Major => "Major",
            WoundLevel.Moderate => "Moderate",
            WoundLevel.Minor => "Minor",
            WoundLevel.Negligible => "Negligible",
            _ => threshold.Value.ToString()
        };

        public static WoundLevel? NormalizeThreshold(WoundLevel? threshold)
        {
            if (!threshold.HasValue || threshold.Value == WoundLevel.None)
            {
                return null;
            }

            return threshold.Value switch
            {
                WoundLevel.Negligible or
                WoundLevel.Minor or
                WoundLevel.Moderate or
                WoundLevel.Major or
                WoundLevel.Critical => threshold,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(threshold), threshold, "Unknown wound threshold.")
            };
        }
    }
}
