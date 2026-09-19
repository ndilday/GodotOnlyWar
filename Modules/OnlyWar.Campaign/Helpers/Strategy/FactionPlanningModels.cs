using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;

namespace OnlyWar.Campaign.Strategy;

/// <summary>
/// Mutable planning data shared by the policies that participate in one planet pass.
/// These types intentionally contain no policy or order-creation behavior.
/// </summary>
internal class RegionForceState
{
    public RegionFaction RegionFaction { get; }

    // A reserve is a battle-value allocation budget, not a headcount or a live military pool.
    public long RequiredDefensiveBattleValue { get; }

    /// <summary>
    /// What the region ended up committing to holding. This is now an OUTPUT of the allocation
    /// auction rather than a subtraction taken before anything else is considered. It is still
    /// clamped to the troops that exist and still persisted on the region faction, because the
    /// tactical assault path materialises the defence days after planning and reading an unbounded
    /// want there let a region field several times its entire organized strength.
    /// </summary>
    public long AssignedDefensiveBattleValue { get; set; }

    /// <summary>
    /// Battle value this region has left to allocate. Under the old priority ladder this was the
    /// residual AFTER the defensive reserve was taken off the top, which is what froze regions: a
    /// region facing several neighbours reserved everything and then appeared, to every later policy,
    /// to have no troops at all. It is now the region's whole allocatable strength, and defence
    /// competes for it like anything else.
    /// </summary>
    public long SpareTroops { get; set; }

    public long DefensiveShortfall { get; set; }

    /// <summary>
    /// The smallest battle value force generation can actually honour for this faction. A region that
    /// cannot field even this is genuinely unable to act and is excluded from bidding; its strength
    /// counts only as passive local defence.
    /// </summary>
    public long MinimumBid { get; }

    /// <summary>
    /// The increment in which this region offers battle value. Derived so that no region proposes more
    /// than ForceAllocationConstants.MaxBidStepsPerRegion candidate sizes, which is the quantum's only
    /// remaining job once battle value is the auction's currency - it bounds the work, it does not
    /// decide the outcome. Note MinimumForceRequest prices the MINIMUM-strength squad, so a quantum is
    /// a budget unit and not a squad count.
    /// </summary>
    public long Quantum { get; }

    /// <summary>
    /// Strength this region may never send away on an offensive, however badly the attack needs it.
    /// </summary>
    /// <remarks>
    /// An offensive DRAWS its force out of the staging regions - FactionOffensiveOrderBuilder calls
    /// RemoveMilitaryStrength on each contributor - and nothing stopped a region contributing the
    /// whole of itself. Grist Nine, 2026-09-18: the Orks took Theta with a 60-point foothold one week
    /// and spent all 60 of it on the assault on Mu the next, so the region they had just captured was
    /// left with no garrison at all. Iota did the same.
    ///
    /// That is worse than it sounds for an Indelible faction, because a presence whose population
    /// reaches zero hides itself permanently (RegionFaction.Population) and nothing outside the
    /// strategic-invasion lifecycle ever republishes it. Both regions ended the run invisible, Iota
    /// while holding 573 Orks.
    ///
    /// Deliberately the MINIMUM reserve fraction and not the region's defensive requirement. The
    /// requirement is an unbounded want - on Grist Nine it read 31,599 against an army of 4,827 - so
    /// reserving it would bar a frontier region from ever attacking, which is the freeze this whole
    /// design exists to escape. This only guarantees the region is not stripped bare.
    /// </remarks>
    public long OffensiveGarrisonFloor { get; }

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

        OffensiveGarrisonFloor = (long)(factionInfo.GetDeployedStrength()
            * OnlyWar.Operations.Strategy.FactionThreatAssessment.MinimumDefensiveReserveFraction);

        MinimumBid = Math.Max(1L, factionInfo.PlanetFaction.Faction.MinimumForceRequest);
        long steps = Math.Max(
            1L,
            (long)Math.Ceiling(
                spareTroops
                / (double)(Allocation.ForceAllocationConstants.MaxBidStepsPerRegion * MinimumBid)));
        Quantum = MinimumBid * steps;
    }
}

internal class PotentialOffensive
{
    public Region TargetRegion { get; set; }
    public RegionFaction TargetFaction { get; set; }
    public List<Region> AttackingRegions { get; set; } = new List<Region>();

    /// <summary>
    /// What taking this region is worth, as a property of the TARGET alone.
    /// </summary>
    /// <remarks>
    /// This deliberately no longer scales with the force the attacker happens to have spare beside it.
    /// The old expression ended `reward * availableAttackingForce / defenderForce`, so a target scored
    /// higher because of an accident of who was standing next to it - the same coupling the
    /// ReconUtility comment already had to fight off once ("deliberately NOT divided by the force
    /// available to stage it"). Under marginal allocation importance must be a property of the target
    /// and ALL force-dependence must live in the value curve, or the auction double-counts it.
    /// </remarks>
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
