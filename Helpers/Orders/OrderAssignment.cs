using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // Pure-logic extraction of OrderDialogController.OnOrdersConfirmed's Mission-construction and
    // Order-creation logic, generalized to accept more than one squad at once (for a future
    // multi-squad operations board). Depends on GameDataSingleton.Instance.Sector for the same
    // AddNewOrder/RemoveOrder bookkeeping the dialog used to do inline, but has no Godot/UI
    // dependency of its own.
    public static class OrderAssignment
    {
        // Builds the Mission for the given target region/mission descriptor, then - for every
        // passed squad - detaches it from any prior order (removing that order from the Sector
        // if it becomes empty), creates ONE new Order containing all the passed squads, and
        // registers it with the Sector. Returns null (and creates nothing) if the mission
        // descriptor can't be resolved to a valid Mission (e.g. Recon/Diversion with no valid
        // region-faction target) - callers should treat this the same as the dialog's previous
        // "push a warning and bail" behavior.
        //
        // targetFactionId: for Attack/Diversion on a multi-enemy region this selects which enemy
        // faction to target; pass a negative value ("not specified") to auto-resolve the way the
        // original single-squad dialog did (fall back to the player's own RegionFaction for
        // Attack/Move, or fail for Diversion since it requires an explicit enemy target).
        public static Order AssignSquadsToMission(
            IReadOnlyList<Squad> squads,
            Region targetRegion,
            AvailableMission mission,
            int targetFactionId,
            Aggression aggression)
        {
            Mission builtMission = BuildMission(targetRegion, mission, targetFactionId);
            if (builtMission == null)
            {
                return null;
            }

            foreach (Squad squad in squads)
            {
                if (squad.CurrentOrders != null)
                {
                    Order oldOrder = squad.CurrentOrders;
                    oldOrder.AssignedSquads.Remove(squad);
                    if (oldOrder.AssignedSquads.Count == 0)
                    {
                        GameDataSingleton.Instance.Sector.RemoveOrder(oldOrder);
                    }
                }
            }

            // The Order constructor sets squad.CurrentOrders = this for every squad passed in,
            // so assigning CurrentOrders separately afterwards is unnecessary.
            Order newOrder = new Order(squads.ToList(), Disposition.Mobile, true, false, aggression, builtMission);
            GameDataSingleton.Instance.Sector.AddNewOrder(newOrder);
            return newOrder;
        }

        private static Mission BuildMission(Region selectedRegion, AvailableMission mission, int targetFactionId)
        {
            switch (mission.Kind)
            {
                case MissionAvailabilityKind.Recon:
                    {
                        // use the first non-player, non-default region faction in this region
                        RegionFaction enemyRegionFaction = GetEnemyRegionFaction(selectedRegion)
                            ?? GetDefaultRegionFaction(selectedRegion)
                            ?? GetOrCreatePlayerRegionFaction(selectedRegion);
                        if (enemyRegionFaction == null)
                        {
                            return null;
                        }
                        return new Mission(MissionType.Recon, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Attack:
                case MissionAvailabilityKind.Move:
                    {
                        // Player-selected target faction from the dialog's dropdown, when one was
                        // targetable (region has public enemies) - falls back to the player's own
                        // RegionFaction for the "Move" case where no enemy target was offered.
                        RegionFaction enemyRegionFaction = GetSelectedTargetRegionFaction(selectedRegion, targetFactionId)
                            ?? GetOrCreatePlayerRegionFaction(selectedRegion);
                        return new Mission(MissionType.Advance, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Defend:
                    return new Mission(MissionType.DefenseInDepth, GetOrCreatePlayerRegionFaction(selectedRegion), 0);
                case MissionAvailabilityKind.Patrol:
                    return new Mission(MissionType.Patrol, GetOrCreatePlayerRegionFaction(selectedRegion), 0);
                case MissionAvailabilityKind.FortifyEntrenchment:
                    return new ConstructionMission(DefenseType.Entrenchment, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
                case MissionAvailabilityKind.BuildListeningPost:
                    return new ConstructionMission(DefenseType.ListeningPost, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
                case MissionAvailabilityKind.BuildAntiAir:
                    return new ConstructionMission(DefenseType.AntiAir, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
                case MissionAvailabilityKind.Diversion:
                    {
                        // Diversion: feint against an enemy-held region while the squad stays in
                        // its own region (it demonstrates from adjacent territory rather than
                        // entering the target).
                        RegionFaction enemyRegionFaction = GetSelectedTargetRegionFaction(selectedRegion, targetFactionId);
                        if (enemyRegionFaction == null)
                        {
                            return null;
                        }
                        return new Mission(MissionType.Diversion, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Special:
                    return mission.SpecialMission;
                default:
                    return null;
            }
        }

        // Returns the player's RegionFaction in the given region, creating (and registering) one
        // if the player does not yet have a presence there. Player-built fortifications are stored
        // on this region faction. Mirrors the on-demand creation used for Advance orders.
        private static RegionFaction GetOrCreatePlayerRegionFaction(Region region)
        {
            Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
            if (!region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction playerRegionFaction))
            {
                playerRegionFaction = new RegionFaction(region.Planet.PlanetFactionMap[playerFaction.Id], region);
                region.RegionFactionMap[playerFaction.Id] = playerRegionFaction;
            }
            return playerRegionFaction;
        }

        private static RegionFaction GetEnemyRegionFaction(Region region)
        {
            return region.RegionFactionMap.Values.FirstOrDefault(rf =>
                !rf.PlanetFaction.Faction.IsPlayerFaction
                && !rf.PlanetFaction.Faction.IsDefaultFaction);
        }

        // Looks up the enemy RegionFaction the player picked in the Target Faction dropdown by
        // faction id. Returns null if no target was selected (negative id: dropdown not
        // applicable/populated) or the region faction map no longer contains that faction (e.g.
        // it was wiped out this turn).
        private static RegionFaction GetSelectedTargetRegionFaction(Region region, int targetFactionId)
        {
            if (targetFactionId < 0)
            {
                return null;
            }
            return region.RegionFactionMap.TryGetValue(targetFactionId, out RegionFaction targetRegionFaction)
                ? targetRegionFaction
                : null;
        }

        private static RegionFaction GetDefaultRegionFaction(Region region)
        {
            return region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        }
    }
}
