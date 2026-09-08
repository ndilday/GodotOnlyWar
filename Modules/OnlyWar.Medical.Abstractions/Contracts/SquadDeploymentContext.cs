using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Medical.Abstractions;

public enum SquadDeploymentAction { None, BeginOrder, Land, Embark, Transfer, Inspect }

public class SquadDeploymentContext
{
    public SquadDeploymentAction Action { get; }
    public IReadOnlyList<SquadReadinessBlocker> Restrictions { get; }
    public SquadDeploymentContext(SquadDeploymentAction action = SquadDeploymentAction.None,
        IReadOnlyList<SquadReadinessBlocker> restrictions = null)
    {
        Action = action;
        Restrictions = restrictions ?? Array.Empty<SquadReadinessBlocker>();
    }
}
