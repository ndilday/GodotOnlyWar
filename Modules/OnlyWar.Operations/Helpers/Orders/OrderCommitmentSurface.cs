using OnlyWar.Operations.Abstractions;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Operations.Orders;

/// <summary>
/// Operations' implementation of the commitment-release surface Campaign personnel consumes
/// (SB-05b-1). It only forwards to the two order services that already own each half of the
/// forward/reverse commitment links, so there is still exactly one place that retires a
/// commitment -- this type exists to reverse the reference direction, not to add policy.
/// </summary>
public sealed class OrderCommitmentSurface : IOrderCommitmentSurface
{
    public bool ReleaseCharacter(PlayerSoldier character) =>
        OrderForceService.RemoveCharacter(character);

    public bool ReleaseSquad(Squad squad) =>
        squad != null && OrderAssignment.UnassignSquads([squad]);
}
