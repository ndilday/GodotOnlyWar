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
            QualificationPremiums = qualificationPremiums ?? throw new ArgumentNullException(nameof(qualificationPremiums));
            HazardMultipliers = hazardMultipliers ?? throw new ArgumentNullException(nameof(hazardMultipliers));
            AuthorityMultipliers = authorityMultipliers ?? throw new ArgumentNullException(nameof(authorityMultipliers));
            DesperationMultipliers = desperationMultipliers ?? throw new ArgumentNullException(nameof(desperationMultipliers));
            WorldRequisitionMultipliers = worldRequisitionMultipliers ?? throw new ArgumentNullException(nameof(worldRequisitionMultipliers));
            RelationshipBaseMultiplier = relationshipBaseMultiplier;
            RelationshipOpinionScale = relationshipOpinionScale;
        }
    }
}
