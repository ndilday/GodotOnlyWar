using OnlyWar.Models;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // One player order aimed at a given region, plus the display strings both the Region Ops
    // dossier and the Planet Detail context panel render for it. Kept as a shared model so the
    // "inbound orders" surface reads identically on both screens.
    public sealed class InboundOrderInfo
    {
        public Order Order { get; }
        public string MissionLabel { get; }
        public string MissionAndAggressionLabel => $"{MissionLabel} ({Order.LevelOfAggression})";
        // Where the order's squads are tasked FROM (the region they currently sit in), so recon or
        // an advance already converging on this hex from a different region is visible.
        public string OriginLabel { get; }
        public int SquadCount { get; }
        // Characters are first-class order participants. Keep the old property as a display/API
        // compatibility alias while new callers use CharacterCount.
        public int CharacterCount => Order?.AssignedCharacters?.Count ?? 0;
        [System.Obsolete("Use CharacterCount.")]
        public int AttachedCount => CharacterCount;
        public string SummaryLabel
        {
            get
            {
                string squads = SquadCount == 1 ? "1 squad" : $"{SquadCount} squads";
                string characters = CharacterCount > 0
                    ? $" · +{CharacterCount} attached character(s)"
                    : "";
                return $"{MissionAndAggressionLabel} · {squads}{characters} · from {OriginLabel}";
            }
        }
        public string HoverText
        {
            get
            {
                string recommendedMinimum =
                    AmbushMissionSizing.FormatRecommendedMinimumForce(Order.Mission);
                return recommendedMinimum == null
                    ? SummaryLabel
                    : $"{SummaryLabel}\n{recommendedMinimum}";
            }
        }

        public InboundOrderInfo(Order order, string missionLabel, string originLabel, int squadCount)
        {
            Order = order;
            MissionLabel = missionLabel;
            OriginLabel = originLabel;
            SquadCount = squadCount;
        }
    }

    public static class InboundOrders
    {
        // Every player order whose mission targets the given region, from anywhere in the sector.
        // Orders live centrally on the Sector keyed only by their target RegionFaction, which is
        // what lets this show tasking that originates in a *different* region than the one being
        // viewed - the whole point of the inbound view.
        public static List<InboundOrderInfo> ForRegion(Region target)
        {
            if (target == null) return [];

            return GameDataSingleton.Instance.Sector.Orders.Values
                .Where(order => order.Mission?.RegionFaction?.Region == target
                    && HasPlayerParticipant(order)
                    && !order.Force.IsEmpty)
                .Select(order => new InboundOrderInfo(
                    order,
                    target.SpecialMissions.Any(mission => mission?.Id == order.Mission.Id)
                        ? SpecialMissionPresentation.Format(order.Mission, target)
                        : MissionAvailability.GetOrderLabel(order.Mission),
                    BuildOriginLabel(order, target),
                    order.AssignedSquads.Count))
                .ToList();
        }

        // "local" when the squads operate from the target itself (e.g. a Defend/Patrol on this hex,
        // or a Diversion demonstrating from here); otherwise the origin region's name, with a "+N"
        // suffix if the order somehow spans several origin regions.
        private static bool HasPlayerParticipant(Order order) =>
            order?.OwnerFaction?.IsPlayerFaction == true
            || order?.AssignedSquads.Any(squad => squad?.Faction?.IsPlayerFaction == true) == true
            || order?.AssignedCharacters.Any(character =>
                character?.AssignedSquad?.Faction?.IsPlayerFaction == true) == true;

        private static string BuildOriginLabel(Order order, Region target)
        {
            List<Region> origins = order.AssignedSquads
                .Select(squad => squad.CurrentRegion)
                .Concat(order.AssignedCharacters
                    .Select(character => CampaignLocationService.ForSoldier(character)?.Region))
                .Where(region => region != null)
                .Distinct()
                .ToList();

            if (origins.Count == 0) return "unknown";
            string firstName = origins[0] == target ? "local" : origins[0].Name;
            return origins.Count == 1 ? firstName : $"{firstName} +{origins.Count - 1}";
        }
    }
}
