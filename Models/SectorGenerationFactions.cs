using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    /// <summary>
    /// Resolves the factions that sector generation uses from data-owned role assignments into
    /// stable, typed accessors. Resolution happens once at rules-database load and fails fast with
    /// a clear error if a required assignment is missing or points at an unknown faction.
    /// </summary>
    internal sealed class SectorGenerationFactions
    {
        /// <summary>The hidden, infiltration-capable faction.</summary>
        public Faction Infiltrator { get; }

        /// <summary>The overt invasion faction.</summary>
        public Faction Invader { get; }

        /// <summary>The sector-wide rebellion faction.</summary>
        public Faction Insurrectionists { get; }

        public SectorGenerationFactions(
            IReadOnlyList<Faction> factions,
            IEnumerable<FactionRoleAssignment> assignments)
        {
            if (factions == null) throw new ArgumentNullException(nameof(factions));

            Dictionary<string, int> factionIdsByRole = new(StringComparer.OrdinalIgnoreCase);
            foreach (FactionRoleAssignment assignment in assignments ?? [])
            {
                if (assignment == null)
                {
                    throw new InvalidOperationException("A faction role assignment is null.");
                }
                if (!FactionRoleKeys.TryParse(assignment.RoleKey, out string roleKey))
                {
                    throw new InvalidOperationException(
                        $"Unknown faction role '{assignment.RoleKey}'.");
                }
                if (!factionIdsByRole.TryAdd(roleKey, assignment.FactionId))
                {
                    throw new InvalidOperationException(
                        $"Faction role '{assignment.RoleKey}' is assigned more than once.");
                }
            }

            Infiltrator = Resolve(factions, factionIdsByRole, FactionRoleKeys.Infiltrator);
            Invader = Resolve(factions, factionIdsByRole, FactionRoleKeys.Invader);
            Insurrectionists = Resolve(factions, factionIdsByRole, FactionRoleKeys.Insurrectionists);
        }

        private static Faction Resolve(
            IReadOnlyList<Faction> factions,
            IReadOnlyDictionary<string, int> factionIdsByRole,
            string roleKey)
        {
            if (!factionIdsByRole.TryGetValue(roleKey, out int factionId))
            {
                throw new InvalidOperationException(
                    $"Required faction role '{roleKey}' is not assigned.");
            }

            Faction faction = factions.FirstOrDefault(candidate => candidate.Id == factionId);
            if (faction == null)
            {
                throw new InvalidOperationException(
                    $"Faction role '{roleKey}' references missing faction id {factionId}.");
            }
            return faction;
        }
    }
}
