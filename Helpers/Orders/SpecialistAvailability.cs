using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // The rules behind the Region Ops "ATTACHMENTS" roster group, extracted out of
    // RegionScreenController for the same reason OrderAssignment was: a Godot partial class
    // cannot be unit-tested, so the decisions live here and the controller only does tree
    // wiring. See Design/Reference/SpecialistAttachment.md §7.1.
    public sealed class SpecialistOption
    {
        public PlayerSoldier Soldier { get; }
        public Squad HomeSquad { get; }
        // Where he currently is, for the roster's right-hand status column: the region of the
        // operation he is attached to, or "None".
        public string StatusLabel { get; }

        public SpecialistOption(PlayerSoldier soldier, Squad homeSquad, string statusLabel)
        {
            Soldier = soldier;
            HomeSquad = homeSquad;
            StatusLabel = statusLabel;
        }

        public string Label => $"{Soldier.Name} | {HomeSquad?.Name}";
    }

    public static class SpecialistAvailability
    {
        /// <summary>
        /// May this squad be offered in the Region Ops squad roster as a deployable unit?
        /// Formations that lend individuals are personnel pools and never deploy as units
        /// (§3.3), so they drop out of the squad list even though they stay operational.
        /// </summary>
        public static bool IsDeployableFormation(Squad squad)
        {
            return squad != null
                && squad.IsOperational
                && squad.Members.Count > 0
                && squad.SquadTemplate?.PermitsIndividualDetachment != true;
        }

        /// <summary>
        /// The individuals who may be lent to an operation staged out of <paramref name="originRegion"/>.
        /// Drawn from the landed squads of the player's presence there whose templates permit
        /// detachment, filtered through <see cref="OrderAttachment.CanAttach"/>.
        /// </summary>
        /// <param name="contextOrder">
        /// The order being edited, when the player re-opened an existing one from the inbound
        /// dossier -- its own attached specialists stay selectable. Null when issuing a fresh
        /// order, in which case anyone already committed elsewhere is excluded.
        /// </param>
        public static IReadOnlyList<SpecialistOption> EnumerateCandidates(
            RegionFaction playerRegionFaction,
            Region originRegion,
            Order contextOrder = null)
        {
            if (playerRegionFaction == null)
            {
                return [];
            }

            return playerRegionFaction.LandedSquads
                .Where(squad => squad?.SquadTemplate?.PermitsIndividualDetachment == true)
                .SelectMany(squad => squad.Members
                    .OfType<PlayerSoldier>()
                    .Where(soldier => OrderAttachment.CanAttach(
                        soldier, contextOrder, null, originRegion, out _))
                    .Select(soldier => new SpecialistOption(
                        soldier, squad, DescribeStatus(soldier))))
                .OrderBy(option => option.HomeSquad.Name)
                .ThenBy(option => option.Soldier.Name)
                .ToList();
        }

        private static string DescribeStatus(PlayerSoldier soldier)
        {
            return soldier.AttachedOrder?.Mission?.RegionFaction?.Region?.Name ?? "None";
        }

        /// <summary>
        /// Specialists already attached to a given order, for the "release both" half of the
        /// Unassign action and for re-selecting an order from the inbound dossier.
        /// </summary>
        public static IReadOnlyList<PlayerSoldier> AttachedTo(Order order)
        {
            return order?.AttachedSoldiers.ToList() ?? [];
        }
    }
}
