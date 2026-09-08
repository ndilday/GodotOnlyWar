using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Runtime.Abstractions;
using OnlyWar.Helpers;

namespace OnlyWar.Runtime.Factories;

public sealed class RuntimeSquadFactory
{
    private readonly IRuntimeSoldierFactory _soldiers;

    public RuntimeSquadFactory(IRuntimeSoldierFactory soldiers = null)
    {
        _soldiers = soldiers ?? new RuntimeSoldierFactory();
    }

    public RuntimeSquad Create(
        RuntimeSquadTemplate template,
        IRNG random,
        IEntityIdAllocator entityIds,
        string name = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(entityIds);
        List<RuntimeSoldier> members = [];
        foreach (RuntimeSquadElement element in template.Elements ?? [])
        {
            int minimum = Math.Max(0, element.MinimumNumber);
            int maximum = Math.Max(minimum, element.MaximumNumber);
            int count = element.RollsStrength && maximum > minimum
                ? random.GetIntBelowMax(minimum, maximum + 1)
                : maximum;
            for (int index = 0; index < count; index++)
            {
                members.Add(_soldiers.Create(element.SoldierTemplate, random, entityIds));
            }
        }

        return new RuntimeSquad(
            entityIds.GetNextId(),
            string.IsNullOrWhiteSpace(name) ? template.Name : name,
            template,
            members);
    }
}
