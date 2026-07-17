using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Per-turn synapse coverage predicate (Design/Active/MoraleAndRout.md §4.2). Stateless
    /// and recomputed every call, matching how <see cref="BattleForceEvaluator"/> and
    /// <see cref="BattleContactRules"/> recompute their inputs each round rather than
    /// caching a flag on the squad.
    ///
    /// Coverage reads post-round physical state (§5.1): callers must pass only squads that
    /// are still Active after this round's casualties resolved (e.g.
    /// <c>BattleState.ActiveAttackerSquads</c> / <c>ActiveOpposingSquads</c>). A provider
    /// wiped this round is therefore already absent from the candidate set, so its
    /// dependents lose coverage the same turn — there is no one-turn grace period.
    /// </summary>
    public static class SynapseCoverageEvaluator
    {
        /// <summary>
        /// True iff a living, same-faction, synapse-providing squad other than
        /// <paramref name="squad"/> has at least one able soldier within that provider's
        /// <see cref="BattleSquad.SynapseRadius"/> of at least one able soldier in
        /// <paramref name="squad"/>.
        /// </summary>
        /// <param name="squad">The squad whose coverage is being checked.</param>
        /// <param name="friendlySquads">
        /// Candidate providers, normally every other Active squad on <paramref name="squad"/>'s
        /// side this turn. May include <paramref name="squad"/> itself and non-providers; both
        /// are filtered out.
        /// </param>
        /// <param name="grid">Grid used to measure soldier-to-soldier distance.</param>
        public static bool IsSynapseCovered(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            BattleGridManager grid)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(friendlySquads);
            ArgumentNullException.ThrowIfNull(grid);

            List<BattleSoldier> coveredSoldiers = squad.AbleSoldiers;
            if (coveredSoldiers.Count == 0)
            {
                return false;
            }

            int? factionId = squad.Squad?.Faction?.Id;

            foreach (BattleSquad provider in friendlySquads)
            {
                if (provider == null || provider.Id == squad.Id)
                {
                    continue;
                }
                if (!provider.SquadProvidesSynapse)
                {
                    continue;
                }
                if (provider.Squad?.Faction?.Id != factionId)
                {
                    continue;
                }
                float radius = provider.SynapseRadius;
                if (radius <= 0f)
                {
                    continue;
                }
                List<BattleSoldier> providerSoldiers = provider.AbleSoldiers;
                if (providerSoldiers.Count == 0)
                {
                    // "Living" provider only (§4.2) — a provider wiped this round grants no
                    // coverage, same turn, no grace period.
                    continue;
                }

                if (IsWithinRadius(coveredSoldiers, providerSoldiers, radius, grid))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWithinRadius(
            List<BattleSoldier> coveredSoldiers,
            List<BattleSoldier> providerSoldiers,
            float radius,
            BattleGridManager grid)
        {
            foreach (BattleSoldier covered in coveredSoldiers)
            {
                foreach (BattleSoldier provider in providerSoldiers)
                {
                    float distance = grid.GetDistanceBetweenSoldiers(covered.Soldier.Id, provider.Soldier.Id);
                    if (distance <= radius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
