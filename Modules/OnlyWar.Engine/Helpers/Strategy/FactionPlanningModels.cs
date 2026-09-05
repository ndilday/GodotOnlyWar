using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Mutable planning data shared by the policies that participate in one planet pass.
/// These types intentionally contain no policy or order-creation behavior.
/// </summary>
internal class RegionForceState
{
    public RegionFaction RegionFaction { get; }

    // A reserve is a battle-value allocation budget, not a headcount or a live military pool.
    public long RequiredDefensiveBattleValue { get; }
    public long AssignedDefensiveBattleValue { get; }
    public long SpareTroops { get; set; }
    public long DefensiveShortfall { get; set; }

    public RegionForceState(
        RegionFaction factionInfo,
        long requiredDefensiveBattleValue,
        long assignedDefensiveBattleValue,
        long spareTroops,
        long defensiveShortfall)
    {
        RegionFaction = factionInfo;
        RequiredDefensiveBattleValue = requiredDefensiveBattleValue;
        AssignedDefensiveBattleValue = assignedDefensiveBattleValue;
        SpareTroops = spareTroops;
        DefensiveShortfall = defensiveShortfall;
    }
}

internal class PotentialOffensive
{
    public Region TargetRegion { get; set; }
    public RegionFaction TargetFaction { get; set; }
    public List<Region> AttackingRegions { get; set; } = new List<Region>();
    public long AvailableAttackingForce { get; set; }
    public double Reward { get; set; }
    public long DefenderBattleValue { get; set; }
    public long EstimatedDefenderBattleValue { get; set; }
}

internal enum OffensivePlan
{
    None,
    Assault,
    Recon,
    Raid
}

internal sealed class MissionCandidate
{
    public OffensivePlan Plan { get; set; }
    public PotentialOffensive Offensive { get; set; }
    public double Score { get; set; }
}
