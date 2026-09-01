using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Planets
{
    /// <summary>
    /// Stable generation contexts that consume planet-template eligibility assignments.
    /// The context keys are code-owned contracts; the rules database chooses the templates
    /// that satisfy each contract.
    /// </summary>
    public static class PlanetTemplateEligibilityKeys
    {
        public const string PromisedWorld = "scenario.promised_world";
        public const string OrkGhostSource = "ambient.ork_ghost_source";
    }

    /// <summary>
    /// Assigns a rules-database planet template to a generation context. Display names are not
    /// part of the contract, so a template can be renamed without changing its behavior.
    /// </summary>
    public sealed record PlanetTemplateEligibilityAssignment(
        string ContextKey,
        int PlanetTemplateId);

    /// <summary>
    /// Validated lookup of planet templates allowed in each generation context.
    /// </summary>
    public sealed class PlanetTemplateEligibilityCatalog
    {
        private readonly IReadOnlyList<PlanetTemplateEligibilityAssignment> _assignments;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<int>> _templateIdsByContext;

        public PlanetTemplateEligibilityCatalog(
            IEnumerable<PlanetTemplateEligibilityAssignment> assignments)
        {
            List<PlanetTemplateEligibilityAssignment> assignmentList =
                (assignments ?? []).ToList();
            Dictionary<string, List<int>> idsByContext =
                new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (PlanetTemplateEligibilityAssignment assignment in assignmentList)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.ContextKey))
                {
                    throw new InvalidOperationException(
                        "A planet-template eligibility assignment has no context key.");
                }

                string contextKey = assignment.ContextKey.Trim();
                string duplicateKey = $"{contextKey}\u001f{assignment.PlanetTemplateId}";
                if (!seen.Add(duplicateKey))
                {
                    throw new InvalidOperationException(
                        $"Planet-template eligibility context '{contextKey}' assigns "
                        + $"template id {assignment.PlanetTemplateId} more than once.");
                }

                if (!idsByContext.TryGetValue(contextKey, out List<int> templateIds))
                {
                    templateIds = [];
                    idsByContext[contextKey] = templateIds;
                }
                templateIds.Add(assignment.PlanetTemplateId);
            }

            _assignments = assignmentList.AsReadOnly();
            _templateIdsByContext = idsByContext.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<int>)entry.Value.OrderBy(id => id).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<PlanetTemplateEligibilityAssignment> Assignments => _assignments;

        public IReadOnlyList<int> GetEligibleTemplateIds(string contextKey)
        {
            if (string.IsNullOrWhiteSpace(contextKey))
            {
                return [];
            }

            return _templateIdsByContext.TryGetValue(contextKey.Trim(), out IReadOnlyList<int> ids)
                ? ids
                : [];
        }

        public bool IsEligible(string contextKey, int planetTemplateId) =>
            !string.IsNullOrWhiteSpace(contextKey)
            && _templateIdsByContext.TryGetValue(contextKey.Trim(), out IReadOnlyList<int> ids)
            && ids.Contains(planetTemplateId);
    }
}
