using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Helpers.Extensions;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Applies reports and awareness gains, including the one-pass Allied fan-out. The target
    /// belief store itself remains responsible for validating and mutating one observer's record.
    /// </summary>
    public static class FactionIntelligenceService
    {
        /// <summary>
        /// Producer boundary for public activity. It samples current presence only to create a
        /// transient report; all subsequent planning and presentation reads the resulting belief.
        /// This is also used when an isolated NPC-planning fixture enters the planning phase without
        /// having run the preceding planetary intelligence phase.
        /// </summary>
        public static void ObservePublicActivity(Planet planet, int evidenceWeek)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                planet.RelationshipLedger?.RegisterFaction(planetFaction.Faction);
            }
            foreach (Faction faction in planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Select(regionFaction => regionFaction.PlanetFaction.Faction))
            {
                planet.RelationshipLedger?.RegisterFaction(faction);
            }

            foreach (Region region in planet.Regions
                .Where(region => region != null)
                .OrderBy(region => region.Id))
            {
                foreach (RegionFaction target in region.RegionFactionMap.Values
                    .Where(regionFaction => regionFaction.IsPublic)
                    .OrderBy(regionFaction => regionFaction.PlanetFaction.Faction.Id))
                {
                    foreach (PlanetFaction observer in planet.PlanetFactionMap.Values
                        .Where(planetFaction => planetFaction.Faction.Id
                            != target.PlanetFaction.Faction.Id)
                        .OrderBy(planetFaction => planetFaction.Faction.Id))
                    {
                        bool hasPlanetaryPresence = planet.Regions.Any(candidateRegion =>
                            candidateRegion.RegionFactionMap.ContainsKey(observer.Faction.Id));
                        if (!hasPlanetaryPresence
                            && observer.GetRegionAwareness(region) <= 0f)
                        {
                            continue;
                        }

                        FactionIntelBelief previous = observer.GetTargetBelief(
                            region,
                            target.PlanetFaction.Faction);
                        float delta = FactionIntelligenceRules.ConfirmedThreshold
                            - (previous?.Evidence ?? 0f);
                        if (delta <= 0f) continue;

                        ApplyObservation(
                            planet,
                            new IntelObservation(
                                observer,
                                region,
                                target.PlanetFaction.Faction,
                                delta,
                                EstimatePublicActivity(
                                    target.Population,
                                    observer.GetRegionAwareness(region)),
                                EstimatePublicActivity(
                                    target.GetDeployedStrength(),
                                    observer.GetRegionAwareness(region)),
                                IntelObservationSource.PublicActivity,
                                Math.Max(0, evidenceWeek)));
                    }
                }
            }
        }

        public static IReadOnlyList<FactionIntelBelief> ApplyObservation(
            Planet planet,
            IntelObservation observation)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (!ReferenceEquals(observation.Region.Planet, planet))
            {
                throw new InvalidOperationException("The observation region is not on the supplied planet.");
            }

            FactionRelationshipLedger ledger = planet.RelationshipLedger;
            if (ledger == null)
            {
                throw new InvalidOperationException("The planet has no faction relationship context.");
            }
            ledger.RegisterFaction(observation.Observer.Faction);
            ledger.RegisterFaction(observation.BelievedTarget);
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                ledger.RegisterFaction(planetFaction.Faction);
            }
            foreach (Faction faction in planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Select(regionFaction => regionFaction.PlanetFaction.Faction))
            {
                ledger.RegisterFaction(faction);
            }

            List<PlanetFaction> recipients = GetAlliedRecipients(
                planet,
                observation.Observer,
                observation.BelievedTarget.Id);
            List<FactionIntelBelief> applied = new();
            FactionIntelBelief original = observation.Observer.ApplyObservation(observation);
            if (original != null) applied.Add(original);

            foreach (PlanetFaction recipient in recipients)
            {
                IntelObservation copy = new(
                    recipient,
                    observation.Region,
                    observation.BelievedTarget,
                    observation.EvidenceDelta,
                    observation.EstimatedPopulation,
                    observation.EstimatedMilitaryStrength,
                    IntelObservationSource.AllyReport,
                    observation.EvidenceWeek);
                FactionIntelBelief belief = recipient.ApplyObservation(copy);
                if (belief != null) applied.Add(belief);
            }
            return applied;
        }

        public static IReadOnlyList<PlanetFaction> ApplyAwarenessGain(
            Planet planet,
            PlanetFaction observer,
            Region region,
            float amount)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (region == null) throw new ArgumentNullException(nameof(region));
            if (!float.IsFinite(amount) || amount == 0f)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount < 0f)
            {
                observer.AddRegionAwareness(region, amount);
                return [observer];
            }

            FactionRelationshipLedger ledger = planet.RelationshipLedger
                ?? throw new InvalidOperationException("The planet has no faction relationship context.");
            ledger.RegisterFaction(observer.Faction);
            List<PlanetFaction> recipients = GetAlliedRecipients(planet, observer, null);
            observer.AddRegionAwareness(region, amount);
            foreach (PlanetFaction recipient in recipients)
            {
                recipient.AddRegionAwareness(region, amount);
            }
            return new[] { observer }.Concat(recipients).ToList();
        }

        private static List<PlanetFaction> GetAlliedRecipients(
            Planet planet,
            PlanetFaction observer,
            int? excludedFactionId)
        {
            FactionRelationshipLedger ledger = planet.RelationshipLedger;
            List<Faction> allies = ledger.KnownFactions.Values
                .Where(faction => faction.Id != observer.Faction.Id
                    && faction.Id != excludedFactionId
                    && ledger.GetStance(observer.Faction, faction) == FactionStance.Allied)
                .OrderBy(faction => faction.Id)
                .ToList();

            List<PlanetFaction> recipients = new();
            foreach (Faction ally in allies)
            {
                if (!planet.PlanetFactionMap.TryGetValue(ally.Id, out PlanetFaction recipient))
                {
                    recipient = new PlanetFaction(ally) { IsPublic = false };
                    planet.PlanetFactionMap[ally.Id] = recipient;
                    planet.NotifyPlanetFactionAdded(recipient);
                }
                recipients.Add(recipient);
            }
            return recipients;
        }

        private static long? EstimatePublicActivity(long value, float awareness)
        {
            if (value < 0) return null;
            if (awareness >= FactionIntelligenceRules.LocatedThreshold) return value;
            long divisor = (long)Math.Pow(
                10,
                Math.Max(0, 3 - (int)Math.Floor(Math.Max(0f, awareness))));
            if (divisor <= 1) return value;
            return value <= 0 ? 0 : Math.Max(1, value / divisor * divisor);
        }
    }
}
