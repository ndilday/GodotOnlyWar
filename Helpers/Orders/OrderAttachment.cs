using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // Order-level attachment of individual specialists (Design/Reference/SpecialistAttachment.md,
    // Phase 2a). An attached specialist is WITH the force but not IN the engagement: he has no
    // BattleSquad binding, takes no battle-time effects, and cannot become a casualty. That is
    // deliberately deferred (Phase 2c).
    //
    // This type owns BOTH halves of the pointer pair -- Order.AttachedSoldiers and
    // PlayerSoldier.AttachedOrder -- so nothing anywhere can leave a soldier half-attached.
    // Everything else in the codebase should call Attach/Detach/ReleaseAll rather than
    // touching either side.
    //
    // The soldier is deliberately NOT removed from his home squad's Members. Squad membership
    // drives Soldier.SquadId in the save, and GameStateDataAccess treats a squadless decorated
    // soldier as a FALLEN BROTHER on load -- so evicting him would kill him on the next save.
    public static class OrderAttachment
    {
        private static readonly IndividualPostingService PostingService = new();
        // Attaches an individual to an operation. Idempotent for the same order; re-attaching
        // a soldier who is on a different order moves him.
        public static void Attach(PlayerSoldier soldier, Order order)
        {
            if (soldier == null || order == null)
            {
                return;
            }
            if (ReferenceEquals(soldier.CurrentOrder, order))
            {
                if (!order.AssignedCharacters.Contains(soldier))
                {
                    order.AssignedCharacters.Add(soldier);
                }
                return;
            }

            // Some legacy test/migration objects predate organizational squad ownership. Keep
            // the facade tolerant of those incomplete objects; production posting creation still
            // enforces a home formation through IndividualPostingService.CanCreate.
            if (soldier.AssignedSquad == null)
            {
                Detach(soldier);
                soldier.CurrentOrder = order;
                order.AssignedCharacters.Add(soldier);
                return;
            }

            if (soldier.CurrentOrder != null && !ReferenceEquals(soldier.CurrentOrder, order))
            {
                Detach(soldier);
            }

            if (soldier.AssignedSquad.PermitsIndividualDeployment)
            {
                OrderForceService.AssignCharacter(order, soldier);
                return;
            }

            PostingService.Restore(
                soldier,
                IndividualPostingKind.OperationalAttachment,
                CampaignLocation.Landed(order.Mission?.RegionFaction?.Region)
                    ?? CampaignLocationService.ForSquad(soldier.AssignedSquad),
                GameDataSingleton.Instance?.Date ?? new Date(1),
                order);
        }

        // Releases one individual from whatever operation he is on. Safe on an unattached man.
        public static void Detach(PlayerSoldier soldier)
        {
            if (soldier?.CurrentOrder == null)
            {
                return;
            }
            if (soldier.IndividualPosting?.Location == null)
            {
                soldier.CurrentOrder.AssignedCharacters.Remove(soldier);
                soldier.CurrentOrder = null;
            }
            else
            {
                PostingService.ReleaseFromOrder(soldier);
            }
        }

        // Releases every individual attached to an order. Called wherever an order ends:
        // player unassignment, end-of-turn cleanup of resolved orders, and the last-squad-left
        // teardown in OrderAssignment.
        public static void ReleaseAll(Order order)
        {
            if (order == null)
            {
                return;
            }
            foreach (PlayerSoldier soldier in order.AssignedCharacters.ToList())
            {
                if (soldier.IndividualPosting == null)
                {
                    OrderForceService.RemoveCharacter(order, soldier);
                }
                else
                {
                    PostingService.ReleaseFromOrder(soldier);
                }
            }
        }

        // True if this squad has any member currently attached to a different order. Used by
        // the end-turn preflight so a formation whose specialist is forward does not get
        // flagged as idle.
        public static bool HasAttachedMembers(Squad squad, Order excludingOrder = null)
        {
            return squad?.Members.OfType<PlayerSoldier>().Any(member =>
                member.CurrentOrder != null
                && !ReferenceEquals(member.CurrentOrder, excludingOrder)) == true;
        }

        // The reverse guard: is any member of this squad committed to an order other than the
        // target one?
        public static bool IsAttachedElsewhere(Squad squad, Order target)
        {
            return HasAttachedMembers(squad, target);
        }

        /// <summary>
        /// May this brother be attached to this operation? Runs before any mutation; the caller
        /// creates nothing on a false result. See the design doc §3.2 for the six guards.
        /// </summary>
        /// <param name="originRegion">
        /// The staging region the order is being issued from, or null to accept co-location
        /// with any squad already assigned to the order.
        /// </param>
        public static bool CanAttach(
            PlayerSoldier soldier,
            Order order,
            Region originRegion,
            out string reason)
        {
            return CanAttach(soldier, order, order?.AssignedSquads, originRegion, out reason);
        }

        /// <summary>
        /// As above, but with the staging force supplied explicitly. Order issue needs this
        /// overload: the squads being committed are known before the Order object exists.
        /// </summary>
        public static bool CanAttach(
            PlayerSoldier soldier,
            Order order,
            IReadOnlyList<Squad> stagingSquads,
            Region originRegion,
            out string reason)
        {
            reason = null;
            if (soldier == null)
            {
                reason = "No soldier selected.";
                return false;
            }
            if (order != null && ReferenceEquals(soldier.CurrentOrder, order))
            {
                return true;
            }

            // 1. Only formations whose function is to supply specialists may give a man up.
            Squad squad = soldier.AssignedSquad;
            if (squad?.PermitsIndividualDeployment != true
                && squad?.SquadTemplate?.PermitsIndividualDetachment != true)
            {
                reason = $"{soldier.Name} belongs to a formation that deploys as a unit.";
                return false;
            }

            // 2. One man, one operation.
            if (soldier.CurrentOrder != null && !ReferenceEquals(soldier.CurrentOrder, order))
            {
                reason = $"{soldier.Name} is already attached to another operation.";
                return false;
            }

            // (Guard 3 of the design doc -- "his home squad is not itself deployed" -- is
            // vacuous: a detachable formation is never orderable, so its members' home squad
            // can never be under orders.)

            // 4. Fit to march.
            if (!soldier.IsCombatEffective)
            {
                reason = $"{soldier.Name} is not fit for field duty.";
                return false;
            }

            // 5. Co-located with the operation's staging point.
            if (!IsCoLocated(squad, stagingSquads, originRegion))
            {
                reason = $"{soldier.Name} is not with the force mounting this operation.";
                return false;
            }

            // 6. Not reserved for a procedure this week.
            if (RecruitmentPromotionService.IsReservedForProcedure(
                    GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram,
                    soldier.Id))
            {
                reason = $"{soldier.Name} is committed to a Chapter procedure this week.";
                return false;
            }

            return true;
        }

        private static bool IsCoLocated(
            Squad squad, IReadOnlyList<Squad> stagingSquads, Region originRegion)
        {
            if (squad == null)
            {
                return false;
            }
            if (originRegion != null && squad.CurrentRegion?.Id == originRegion.Id)
            {
                return true;
            }
            return stagingSquads?.Any(assigned => SameLocation(squad, assigned)) == true;
        }

        // Same shape as MedicalProcedureService.SameLocation: aboard the same ship, or landed
        // in the same region.
        private static bool SameLocation(Squad a, Squad b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (a.BoardedLocation != null && b.BoardedLocation != null)
            {
                return a.BoardedLocation.Id == b.BoardedLocation.Id;
            }
            if (a.CurrentRegion != null && b.CurrentRegion != null)
            {
                return a.CurrentRegion.Id == b.CurrentRegion.Id;
            }
            return false;
        }

    }
}
