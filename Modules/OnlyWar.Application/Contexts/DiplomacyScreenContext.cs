using System;
using System.Collections.Generic;
using OnlyWar.Domain;

namespace OnlyWar.Application;

/// <summary>
/// Owns the Diplomacy workspace's read-only projection inputs.
/// </summary>
internal sealed class DiplomacyScreenContext
{
    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly DiplomacyScreenProjector _projector = new();

    internal DiplomacyScreenContext(Sector sector, GameRulesData rules)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    internal IReadOnlyList<TreeNode> QueryDiplomacy() =>
        _projector.Build(_sector, _rules);
}
