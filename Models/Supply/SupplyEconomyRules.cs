using System.Collections.Generic;
using System;

namespace OnlyWar.Models.Supply
{
    public sealed class SupplyEconomyRules
    {
        public RequestValuationRules RequestValuation { get; }
        public GovernorOfferRules GovernorOffers { get; }
        public int DefaultServiceWeeks { get; }

        /// <summary>
        /// Fallback deadline, used only when <see cref="SeverityDeadlineWeeks"/> has no entry for
        /// a severity. Live requests take their deadline from that table instead.
        /// </summary>
        public int DefaultDeadlineWeeks { get; }
        public int DefaultDeliveryWeeks { get; }
        public int StandingCadenceWeeks { get; }
        public decimal StandingDeliveryFraction { get; }
        public int StandingMinimumOffer { get; }
        public int RequestCooldownWeeks { get; }

        /// <summary>
        /// Scales how often an eligible governor petitions the Chapter, applied to the
        /// neediness/opinion roll in GovernorTurnProcessor. The two generation gates are each
        /// linear in the governor's traits, so sector-wide arrivals per week work out to roughly
        /// <c>governorCount * 0.125 * RequestGenerationRate</c> - about
        /// <c>100 * RequestGenerationRate</c> for the ~800-governor production sector. Because the
        /// scale is applied multiplicatively it changes only the rate, not which governors ask.
        ///
        /// The setter is a test hook: SectorSimulationFixture pins this to 1 so single-planet
        /// simulation tests can still force a request deterministically.
        /// </summary>
        public decimal RequestGenerationRate { get; internal set; }
        /// <summary>
        /// How long a governor will wait, keyed by <c>RequestSeverity</c>. The deadline is a
        /// property of the petitioning world's situation, not of where the Chapter happens to be:
        /// a world asking for a reassuring show of the flag can wait the better part of a year,
        /// one facing collapse cannot. That keeps the Chapter's dispersal out of the governor's
        /// reasoning while still making urgent petitions answerable only from nearby - a round
        /// trip costs 4 weeks of system transit before any warp travel, so short fuses are
        /// implicitly a proximity requirement.
        /// </summary>
        public IReadOnlyDictionary<string, int> SeverityDeadlineWeeks { get; }
        public IReadOnlyList<QualificationPremium> QualificationPremiums { get; }
        public IReadOnlyDictionary<string, decimal> HazardMultipliers { get; }
        public IReadOnlyDictionary<string, decimal> AuthorityMultipliers { get; }
        public IReadOnlyDictionary<string, decimal> DesperationMultipliers { get; }
        public IReadOnlyDictionary<int, decimal> WorldRequisitionMultipliers { get; }
        public decimal RelationshipBaseMultiplier { get; }
        public decimal RelationshipOpinionScale { get; }

        public SupplyEconomyRules(
            RequestValuationRules requestValuation,
            GovernorOfferRules governorOffers,
            int defaultServiceWeeks,
            int defaultDeadlineWeeks,
            int defaultDeliveryWeeks,
            int standingCadenceWeeks,
            decimal standingDeliveryFraction,
            int standingMinimumOffer,
            int requestCooldownWeeks,
            decimal requestGenerationRate,
            IReadOnlyDictionary<string, int> severityDeadlineWeeks,
            IReadOnlyList<QualificationPremium> qualificationPremiums,
            IReadOnlyDictionary<string, decimal> hazardMultipliers,
            IReadOnlyDictionary<string, decimal> authorityMultipliers,
            IReadOnlyDictionary<string, decimal> desperationMultipliers,
            IReadOnlyDictionary<int, decimal> worldRequisitionMultipliers,
            decimal relationshipBaseMultiplier,
            decimal relationshipOpinionScale)
        {
            RequestValuation = requestValuation;
            GovernorOffers = governorOffers;
            DefaultServiceWeeks = defaultServiceWeeks;
            DefaultDeadlineWeeks = defaultDeadlineWeeks;
            DefaultDeliveryWeeks = defaultDeliveryWeeks;
            StandingCadenceWeeks = standingCadenceWeeks;
            StandingDeliveryFraction = standingDeliveryFraction;
            StandingMinimumOffer = standingMinimumOffer;
            RequestCooldownWeeks = requestCooldownWeeks;
            RequestGenerationRate = requestGenerationRate;
            SeverityDeadlineWeeks = severityDeadlineWeeks ?? throw new ArgumentNullException(nameof(severityDeadlineWeeks));
            QualificationPremiums =qualificationPremiums ?? throw new ArgumentNullException(nameof(qualificationPremiums));
            HazardMultipliers = hazardMultipliers ?? throw new ArgumentNullException(nameof(hazardMultipliers));
            AuthorityMultipliers = authorityMultipliers ?? throw new ArgumentNullException(nameof(authorityMultipliers));
            DesperationMultipliers = desperationMultipliers ?? throw new ArgumentNullException(nameof(desperationMultipliers));
            WorldRequisitionMultipliers = worldRequisitionMultipliers ?? throw new ArgumentNullException(nameof(worldRequisitionMultipliers));
            RelationshipBaseMultiplier = relationshipBaseMultiplier;
            RelationshipOpinionScale = relationshipOpinionScale;
        }
    }
}
