using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Linq;

namespace OnlyWar.Operations.Orders
{
    // Queries about individual specialists attached to operations (Design/Reference/SpecialistAttachment.md,
    // Phase 2a). Attaching and releasing them -- both halves of the pointer pair,
    // Order.AssignedCharacters and PlayerSoldier.CurrentOrder -- is owned by OrderForceService, and
    // whether a man may be attached is decided by IPersonnelAvailabilityQueries.EvaluateOrderAssignment.
    //
    // An attached soldier is deliberately NOT removed from his home squad's Members. Squad membership
    // drives Soldier.SquadId in the save, and GameStateDataAccess treats a squadless decorated
    // soldier as a FALLEN BROTHER on load -- so evicting him would kill him on the next save.
    public static class OrderAttachment
    {
        // True if this squad has any member currently attached to a different order. Used by
        // the end-turn preflight so a formation whose specialist is forward does not get
        // flagged as idle.
        public static bool HasAttachedMembers(Squad squad, Order excludingOrder = null)
        {
            return squad?.Members.OfType<PlayerSoldier>().Any(member =>
                member.CurrentOrder != null
                && !ReferenceEquals(member.CurrentOrder, excludingOrder)) == true;
        }
    }
}
