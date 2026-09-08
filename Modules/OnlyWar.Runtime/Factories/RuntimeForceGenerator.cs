using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Runtime.Abstractions;
using OnlyWar.Helpers;

namespace OnlyWar.Runtime.Factories;

/// <summary>
/// Runtime force selection policy.  It consumes a detached template catalog and never invokes
/// new-game generation or registers a result on a campaign faction.
/// </summary>
public sealed class RuntimeForceGenerator
{
    private readonly RuntimeSquadFactory _squads;

    public RuntimeForceGenerator(RuntimeSquadFactory squads = null)
    {
        _squads = squads ?? new RuntimeSquadFactory();
    }

    public IReadOnlyList<RuntimeSquad> Generate(
        RuntimeForceGenerationRequest request,
        IRNG random,
        IEntityIdAllocator entityIds)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(entityIds);
        List<RuntimeSquadTemplate> templates = (request.Templates ?? [])
            .Where(template => template != null && template.BattleValue > 0)
            .OrderBy(template => template.Id)
            .ToList();
        if (templates.Count == 0 || request.TargetBattleValue <= 0) return [];

        IEnumerable<RuntimeSquadTemplate> candidates = request.Profile switch
        {
            RuntimeForceCompositionProfile.ScoutPatrol => templates.Where(template =>
                (template.SquadType & SquadTypesForRuntime.Scout) != 0),
            RuntimeForceCompositionProfile.SpecialHqTarget => templates.Where(template =>
                (template.SquadType & SquadTypesForRuntime.Hq) != 0),
            _ => templates.Where(template =>
                (template.SquadType & SquadTypesForRuntime.Hq) == 0)
        };
        List<RuntimeSquadTemplate> usable = candidates.ToList();
        if (usable.Count == 0) return [];

        long remaining = request.TargetBattleValue;
        List<RuntimeSquad> result = [];
        if (request.Profile == RuntimeForceCompositionProfile.SpecialHqTarget)
        {
            int index = Math.Clamp(request.Tier - 1, 0, usable.Count - 1);
            result.Add(_squads.Create(usable[index], random, entityIds));
            return result;
        }

        HashSet<int> used = [];
        while (remaining > 0)
        {
            List<RuntimeSquadTemplate> affordable = usable
                .Where(template => template.BattleValue <= remaining)
                .ToList();
            if (affordable.Count == 0) break;
            List<RuntimeSquadTemplate> unused = affordable
                .Where(template => !used.Contains(template.Id))
                .ToList();
            if (unused.Count == 0)
            {
                used.Clear();
                unused = affordable;
            }

            RuntimeSquadTemplate selected = unused[random.GetIntBelowMax(0, unused.Count)];
            used.Add(selected.Id);
            result.Add(_squads.Create(selected, random, entityIds));
            remaining -= selected.BattleValue;
            if (request.Profile == RuntimeForceCompositionProfile.ScoutPatrol
                && result.Count >= Math.Max(1, request.Tier)) break;
        }
        return result;
    }
}
