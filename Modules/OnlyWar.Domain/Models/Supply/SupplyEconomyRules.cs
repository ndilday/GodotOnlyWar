using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Supply
{
    public enum SupplyWorldArchetype
    {
        Agri,
        Civilised,
        Death,
        Feral,
        Feudal,
        Forge,
        Hive
    }

    public sealed class SupplyEconomyRules
    {
        public RequestValuationRules RequestValuation { get; }
        public GovernorOfferRules GovernorOffers { get; }
        public int DefaultServiceWeeks { get; }

        /// <summary>
        /// Fallback deadline, used only when <see cref="SeverityDeadlineWeeks"/> has no entry for
        /// a severity. Live requests take their deadline from the profile map instead.
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
        public IReadOnlyDictionary<RequestSeverity, int> SeverityDeadlineWeeks { get; }
        public IReadOnlyList<QualificationPremium> QualificationPremiums { get; }
        public IReadOnlyDictionary<RequestHazard, decimal> HazardMultipliers { get; }
        public IReadOnlyDictionary<GovernanceTier, decimal> AuthorityMultipliers { get; }
        public IReadOnlyDictionary<RequestSeverity, decimal> DesperationMultipliers { get; }
        public IReadOnlyDictionary<SupplyWorldArchetype, decimal> WorldRequisitionMultipliers { get; }
        public decimal RelationshipBaseMultiplier { get; }
        public decimal RelationshipOpinionScale { get; }

        /// <summary>
        /// The supply economy is a code-owned balance profile. It is deliberately constructed
        /// here rather than loaded from the universe-content database: requests, pledges, and
        /// their valuation are one closed game system, not authored universe content.
        /// </summary>
        public static SupplyEconomyRules CreateDefault()
        {
            return new SupplyEconomyRules(
                new RequestValuationRules(
                    requisitionPerBattleValueTime: 0.25m,
                    throughputBands: new ThroughputPremiumBand[]
                    {
                        new(100, 1.0m),
                        new(250, 1.1m),
                        new(500, 1.25m),
                        new(1000, 1.5m),
                        new(long.MaxValue, 2.0m)
                    },
                    minimumRequestValue: 25,
                    maximumRequestValue: 1000,
                    maximumCombinedPremium: 4.0m),
                new GovernorOfferRules(
                    MinimumOffer: 25,
                    MaximumOffer: 1500,
                    MinimumWillingnessMultiplier: 0.5m,
                    MaximumWillingnessMultiplier: 2.0m),
                defaultServiceWeeks: 4,
                defaultDeadlineWeeks: 8,
                defaultDeliveryWeeks: 4,
                standingCadenceWeeks: 52,
                standingDeliveryFraction: 0.2m,
                standingMinimumOffer: 300,
                requestCooldownWeeks: 8,
                requestGenerationRate: 0.006m,
                severityDeadlineWeeks: new Dictionary<RequestSeverity, int>
                {
                    [RequestSeverity.Concerned] = 39,
                    [RequestSeverity.Serious] = 26,
                    [RequestSeverity.Desperate] = 13,
                    [RequestSeverity.Existential] = 13
                },
                qualificationPremiums: new QualificationPremium[]
                {
                    new("ForceComposition", "Scout", 1.2m),
                    new("Operational", "Covert", 1.25m),
                    new("Personnel", "Techmarine", 1.3m)
                },
                hazardMultipliers: new Dictionary<RequestHazard, decimal>
                {
                    [RequestHazard.Routine] = 1.0m,
                    [RequestHazard.Dangerous] = 1.25m,
                    [RequestHazard.Extreme] = 1.8m
                },
                authorityMultipliers: new Dictionary<GovernanceTier, decimal>
                {
                    [GovernanceTier.Planetary] = 1.0m,
                    [GovernanceTier.SubsectorCapital] = 1.1m,
                    [GovernanceTier.SectorCapital] = 1.25m
                },
                desperationMultipliers: new Dictionary<RequestSeverity, decimal>
                {
                    [RequestSeverity.Concerned] = 1.0m,
                    [RequestSeverity.Serious] = 1.25m,
                    [RequestSeverity.Desperate] = 1.5m,
                    [RequestSeverity.Existential] = 2.0m
                },
                worldRequisitionMultipliers: new Dictionary<SupplyWorldArchetype, decimal>
                {
                    [SupplyWorldArchetype.Agri] = 0.85m,
                    [SupplyWorldArchetype.Civilised] = 1.0m,
                    [SupplyWorldArchetype.Death] = 0.75m,
                    [SupplyWorldArchetype.Feral] = 0.65m,
                    [SupplyWorldArchetype.Feudal] = 0.8m,
                    [SupplyWorldArchetype.Forge] = 1.2m,
                    [SupplyWorldArchetype.Hive] = 1.15m
                },
                relationshipBaseMultiplier: 0.75m,
                relationshipOpinionScale: 0.5m);
        }

        /// <summary>
        /// Resolves the code-owned world archetype vocabulary. Unknown or newly authored planet
        /// templates receive the neutral multiplier rather than becoming a database contract.
        /// </summary>
        public decimal GetWorldRequisitionMultiplier(PlanetTemplate planetTemplate)
        {
            ArgumentNullException.ThrowIfNull(planetTemplate);
            return Enum.TryParse(planetTemplate.Name, ignoreCase: true, out SupplyWorldArchetype archetype)
                && Enum.IsDefined(archetype)
                && WorldRequisitionMultipliers.TryGetValue(archetype, out decimal multiplier)
                    ? multiplier
                    : 1m;
        }

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
            IReadOnlyDictionary<RequestSeverity, int> severityDeadlineWeeks,
            IReadOnlyList<QualificationPremium> qualificationPremiums,
            IReadOnlyDictionary<RequestHazard, decimal> hazardMultipliers,
            IReadOnlyDictionary<GovernanceTier, decimal> authorityMultipliers,
            IReadOnlyDictionary<RequestSeverity, decimal> desperationMultipliers,
            IReadOnlyDictionary<SupplyWorldArchetype, decimal> worldRequisitionMultipliers,
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
