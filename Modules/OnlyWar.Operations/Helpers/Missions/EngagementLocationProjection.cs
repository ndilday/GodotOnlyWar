using System;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain.Planets;

namespace OnlyWar.Operations.Missions;

/// <summary>Projects a live campaign region into the detached tactical location reference.</summary>
public static class EngagementLocationProjection
{
    public static EngagementLocation ToEngagementLocation(this Region region)
    {
        if (region == null) throw new ArgumentNullException(nameof(region));
        return new EngagementLocation(region.Id, region.Name, region.Planet?.Name);
    }
}
