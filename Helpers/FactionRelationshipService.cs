using System;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Resolves sector relationship state and the small set of contextual planetary policies that
    /// sit above it. The ledger is the source of base stance; this service never infers hostility
    /// from Imperial/non-Imperial identity.
    /// </summary>
    public sealed class FactionRelationshipService
    {
        public FactionRelationshipLedger Ledger { get; }

        public FactionRelationshipService(FactionRelationshipLedger ledger)
        {
            Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public FactionStance GetBaseStance(Faction first, Faction second) =>
            Ledger.GetStance(first, second);

        public FactionStance GetEffectiveStance(Faction first, Faction second)
        {
            return ResolveEffectiveStance(first, second, null);
        }

        public static FactionStance GetBaseStance(
            Faction first,
            Faction second,
            FactionRelationshipLedger ledger) =>
            (ledger ?? throw new ArgumentNullException(nameof(ledger))).GetStance(first, second);

        public static FactionStance GetEffectiveStance(Faction first, Faction second, Planet context)
        {
            if (context?.RelationshipLedger == null)
            {
                return ResolveWithoutPlanet(first, second);
            }
            return new FactionRelationshipService(context.RelationshipLedger)
                .ResolveEffectiveStance(first, second, context);
        }

        private FactionStance ResolveEffectiveStance(Faction first, Faction second, Planet context)
        {
            FactionStance baseStance = GetBaseStance(first, second);
            if (baseStance != FactionStance.Hostile
                || first == null
                || second == null
                || first.HasBehavior(FactionBehavior.UniversallyHostile)
                || second.HasBehavior(FactionBehavior.UniversallyHostile))
            {
                return baseStance;
            }

            Faction rebel = first.HasBehavior(FactionBehavior.OffersExternalEnemyTruce) ? first
                : second.HasBehavior(FactionBehavior.OffersExternalEnemyTruce) ? second
                : null;
            Faction imperial = IsImperial(first) ? first : IsImperial(second) ? second : null;
            if (rebel == null || imperial == null || !HasPublicExternalEnemy(context, rebel))
            {
                return baseStance;
            }

            return FactionStance.Neutral;
        }

        public bool AreHostile(Faction first, Faction second) =>
            GetEffectiveStance(first, second) == FactionStance.Hostile;

        public static bool AreHostile(Faction first, Faction second, Planet context) =>
            GetEffectiveStance(first, second, context) == FactionStance.Hostile;

        public static bool AreAllied(Faction first, Faction second, Planet context) =>
            GetEffectiveStance(first, second, context) == FactionStance.Allied;

        public static FactionRelationshipService For(Planet planet)
        {
            if (planet?.RelationshipLedger == null)
            {
                throw new InvalidOperationException(
                    "The planet is not attached to a sector faction relationship ledger.");
            }
            return new FactionRelationshipService(planet.RelationshipLedger);
        }

        /// <summary>
        /// Role identity remains useful for governance and Chapter supply. It is not a relationship
        /// question and must not be used to decide whether two factions are enemies.
        /// </summary>
        public static bool IsImperial(Faction faction) =>
            faction != null && (faction.IsPlayerFaction || faction.IsDefaultFaction);

        public static bool DefendsHostAgainst(RegionFaction hiddenFaction, Faction attacker)
        {
            if (hiddenFaction?.PlanetFaction?.Faction == null || attacker == null)
            {
                return false;
            }
            if (hiddenFaction.IsPublic
                || !hiddenFaction.PlanetFaction.Faction.HasBehavior(
                    FactionBehavior.DefendsHostWhileHidden))
            {
                return false;
            }

            Planet planet = hiddenFaction.Region?.Planet;
            if (planet?.RelationshipLedger == null)
            {
                return attacker.Id != hiddenFaction.PlanetFaction.Faction.Id;
            }

            return For(planet).AreHostile(
                attacker,
                hiddenFaction.PlanetFaction.Faction);
        }

        private static FactionStance ResolveWithoutPlanet(Faction first, Faction second)
        {
            if (first == null || second == null) return FactionStance.Hostile;
            if (first.Id == second.Id) return FactionStance.Allied;
            if (first.HasBehavior(FactionBehavior.UniversallyHostile)
                || second.HasBehavior(FactionBehavior.UniversallyHostile))
            {
                return FactionStance.Hostile;
            }
            return FactionStance.Hostile;
        }

        /// <summary>
        /// Explicit external-enemy policy used by revolt behavior. This is intentionally narrower
        /// than the relationship resolver and is not a substitute for a stance lookup.
        /// </summary>
        public static bool IsExternalEnemy(Faction faction, Faction humanHostAlignedFaction = null)
        {
            if (faction == null || IsImperial(faction)) return false;
            if (faction.HasBehavior(FactionBehavior.OffersExternalEnemyTruce)) return false;
            return humanHostAlignedFaction == null || faction.Id != humanHostAlignedFaction.Id;
        }

        private static bool HasPublicExternalEnemy(Planet planet, Faction rebel)
        {
            if (planet?.Regions == null) return false;
            return planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Any(regionFaction => regionFaction.IsPublic
                    && regionFaction.PlanetFaction.Faction.Id != rebel.Id
                    && IsExternalEnemy(regionFaction.PlanetFaction.Faction, rebel));
        }
    }
}
