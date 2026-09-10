using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Drives a squad through the SAME three layers BattleActionPlanningCoordinator uses in
/// production: Layer 2 chooses the engagement option, Layer 2.5 declares it, and Layer 3
/// materializes the actions.
///
/// <para>Attacks are built by <see cref="Plan"/>, against turn-start geometry, because that is when
/// the game builds them (TDD §6.6). A test asserting what an engaged
/// soldier does needs nothing else. <see cref="PlanAndResolveClosingMoves"/> additionally runs the
/// movement phase's deferred half, which decides only where separated members END UP -- it builds no
/// attacks. Reach for it when the assertion is about movement or about a charge being marked.</para>
///
/// <para>Note the remaining differences from the coordinator, which matter when reading a
/// failure: it supplies EngagementRoleConstraints, calls InitializeEngagementHorizon before any
/// squad chooses, shares one BattlePlanningContext across both sides, and crosses two serial
/// barriers so every squad decides before any squad declares. This driver plans one squad
/// end to end.</para>
/// </summary>
internal static class EngagementPathDriver
{
    /// <summary>
    /// Choose, declare and build for one squad. Soldiers in contact at turn start get their strikes
    /// here; separated members get a deferred <see cref="SquadClosingMoveAction"/> instead.
    /// </summary>
    internal static SquadEngagementDecision Plan(
        BattleSquadPlanner planner,
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> enemySquads)
    {
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([squad], enemySquads);
        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            squad,
            paired.Frames[squad.Id],
            paired.Profiles,
            paired.Frames,
            enemySquads);
        planner.DeclareEngagementDecision(decision);
        planner.BuildEngagementActions(decision);
        return decision;
    }

    /// <summary>
    /// Plan, then run the movement phase's deferred half: execute any
    /// <see cref="SquadClosingMoveAction"/>. This produces movement and may mark an arriving soldier
    /// as having charged; it never produces an attack. MoveAction ignores the BattleState argument,
    /// so null is safe.
    /// </summary>
    internal static SquadEngagementDecision PlanAndResolveClosingMoves(
        BattleSquadPlanner planner,
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> enemySquads,
        IEnumerable<IAction> moveActions)
    {
        SquadEngagementDecision decision = Plan(planner, squad, enemySquads);
        foreach (SquadClosingMoveAction closing in moveActions
            .OfType<SquadClosingMoveAction>()
            .ToList())
        {
            closing.Execute(null);
        }
        return decision;
    }
}
