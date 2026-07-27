using System.Collections.Generic;
using System;

namespace OnlyWar.Models.Supply
{
    public sealed class SupplyEconomyRules
    {
        public RequestValuationRules RequestValuation { get; }
        public GovernorOfferRules GovernorOffers { get; }
        public int DefaultServiceWeeks { get; }
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
