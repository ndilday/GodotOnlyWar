using OnlyWar.Domain;
using OnlyWar.Domain.Mandates;
using System;

namespace OnlyWar.Campaign.Mandates
{
    /// <summary>
    /// Starting values for mandates and legacy (PRD §4.25, TDD §6.15). They are code constants
    /// until tuning shows a need to move them to the rules database. The scale: a
    /// fulfilled mandate is worth about 100 legacy, and a mandate that fails costs half its value.
    /// </summary>
    public static class MandateConstants
    {
        public const int WeeksPerYear = 52;

        // Deadlines: each family has its own.
        public const int EstablishDeadlineWeeks = 3 * WeeksPerYear;
        public const int SecureDeadlineWeeks = 3 * WeeksPerYear;
        public const int RebuildDeadlineWeeks = 5 * WeeksPerYear;
        public const int ProtectDeadlineWeeks = 2 * WeeksPerYear;
        public const int EradicateDeadlineWeeks = 3 * WeeksPerYear;
        public const int ReputationDeadlineWeeks = 3 * WeeksPerYear;
        public const int SupplyDeadlineWeeks = 3 * WeeksPerYear;

        // Eligibility and goals (TDD §6.15 *Family rules*).
        public const int SecureCleanWeeksRequired = 26;
        public const double RebuildEligibleBelowFraction = 0.6;
        public const double RebuildRaisedAtFraction = 0.6;
        public const double RebuildFulfilledAtFraction = 0.9;
        public const int ProtectMinimumThreatenedWorlds = 2;
        public const int ProtectMaximumNamedWorlds = 3;
        public const float ReputationWarmOpinion = 0.7f;
        public const int SupplyPledgesRequired = 3;

        // Establish is the one mandate with a practical result (PRD §4.25): the Sector Lord's opinion
        // moves on its win and on its failure. Half the Promised World's 0.5, because the player
        // chose this world rather than receiving it as a charge.
        public const float EstablishSectorLordOpinionReward = 0.25f;
        public const float EstablishSectorLordOpinionPenalty = 0.25f;

        // Each family has its own legacy value. A failed or abandoned mandate costs half.
        public const int EstablishLegacy = 150;
        public const int SecureLegacy = 100;
        public const int RebuildLegacy = 80;
        public const int ProtectLegacy = 120;
        public const int EradicateLegacy = 100;
        public const int ReputationLegacy = 60;
        public const int SupplyLegacy = 60;

        // Worlds saved and lost where the Chapter took part (WorldControlChangedPayload.ChapterParticipated).
        public const int WorldSavedLegacy = 20;
        public const int WorldLostLegacy = -20;

        // Large battles: both sides' starting strategic Battle Value (the rules-DB StrategicValue, not
        // the battle-time tactical value) above the threshold.
        // A Space Marine is worth about 70, so 5000 is roughly a company-scale engagement. The
        // amount grows in proportion to size and is capped at four times the threshold.
        public const long LargeBattleThreshold = 5000;
        public const int LargeBattleLegacyAtThreshold = 10;
        public const int LargeBattleLegacyCap = 40;

        // The Chapter's own dead (enemy kills do not count): the notability classifier's senior-casualty boundary
        // separates the two tiers.
        public const int BrotherKilledLegacy = -1;
        public const int SeniorBrotherKilledLegacy = -5;

        public static int LegacyValue(MandateFamily family) => family switch
        {
            MandateFamily.EstablishChapterWorld => EstablishLegacy,
            MandateFamily.SecureChapterWorld => SecureLegacy,
            MandateFamily.RebuildCompany => RebuildLegacy,
            MandateFamily.ProtectStrategicWorlds => ProtectLegacy,
            MandateFamily.EradicateThreat => EradicateLegacy,
            MandateFamily.Reputation => ReputationLegacy,
            MandateFamily.SupplyNetwork => SupplyLegacy,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
        };

        public static int DeadlineWeeks(MandateFamily family) => family switch
        {
            MandateFamily.EstablishChapterWorld => EstablishDeadlineWeeks,
            MandateFamily.SecureChapterWorld => SecureDeadlineWeeks,
            MandateFamily.RebuildCompany => RebuildDeadlineWeeks,
            MandateFamily.ProtectStrategicWorlds => ProtectDeadlineWeeks,
            MandateFamily.EradicateThreat => EradicateDeadlineWeeks,
            MandateFamily.Reputation => ReputationDeadlineWeeks,
            MandateFamily.SupplyNetwork => SupplyDeadlineWeeks,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
        };

        // Fulfilled earns the value; failed or abandoned costs half of it, rounded away from zero.
        public static int FailureLegacy(int legacyValue) =>
            -(int)Math.Ceiling(legacyValue / 2.0);

        // Every request severity counts, Existential at the top. A request the Chapter ignored counts
        // as failed: each request is made to the Chapter itself.
        public static int RequestLegacy(RequestSeverity severity) => severity switch
        {
            RequestSeverity.Concerned => 5,
            RequestSeverity.Serious => 10,
            RequestSeverity.Desperate => 20,
            RequestSeverity.Existential => 40,
            _ => 5
        };

        public static int LargeBattleLegacy(long startingStrategicValue)
        {
            if (startingStrategicValue < LargeBattleThreshold) return 0;
            double scaled = LargeBattleLegacyAtThreshold * (double)startingStrategicValue / LargeBattleThreshold;
            return (int)Math.Min(LargeBattleLegacyCap, Math.Round(scaled, MidpointRounding.AwayFromZero));
        }
    }
}
