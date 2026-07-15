using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Applies resolved tactical-mission effects to strategic state, accounts for surviving
    /// offensive forces, and cleans up the player orders and special missions consumed this turn.
    /// </summary>
    internal sealed class MissionAftermathProcessor
    {
        private readonly Action<PlanetFaction, Region, float> _recordIntelGain;
        private readonly Action<RegionFaction, long, Faction> _recordScenarioPdfLost;

        internal MissionAftermathProcessor(
            Action<PlanetFaction, Region, float> recordIntelGain,
            Action<RegionFaction, long, Faction> recordScenarioPdfLost)
        {
            _recordIntelGain = recordIntelGain;
            _recordScenarioPdfLost = recordScenarioPdfLost;
        }

        internal void ApplyMissionResults(IEnumerable<MissionContext> missionContexts)
        {
            foreach (MissionContext context in missionContexts)
            {
                RegionFaction regionFaction = context.Order.Mission.RegionFaction;
                switch (context.Order.Mission.MissionType)
                {
                    case MissionType.Assassination:
                        int orgLost = (int)(context.Impact * 100 / regionFaction.Population);
                        regionFaction.Organization -= Math.Min(orgLost, regionFaction.Organization);
                        break;
                    case MissionType.Recon:
                        ResolveReconResult(
                            context.Order.AssignedSquads.FirstOrDefault()?.Faction,
                            regionFaction,
                            context.Impact,
                            _recordIntelGain);
                        break;
                    case MissionType.Sabotage:
                        SabotageMission sabotageMission = (SabotageMission)context.Order.Mission;
                        double impact = Math.Min(context.Impact, sabotageMission.MissionSize);
                        switch (sabotageMission.DefenseType)
                        {
                            case DefenseType.Entrenchment:
                                regionFaction.Entrenchment = Math.Max(
                                    0.0,
                                    regionFaction.Entrenchment - impact);
                                break;
                            case DefenseType.ListeningPost:
                                regionFaction.ListeningPost = Math.Max(
                                    0.0,
                                    regionFaction.ListeningPost - impact);
                                break;
                            case DefenseType.AntiAir:
                                regionFaction.AntiAir = Math.Max(
                                    0.0,
                                    regionFaction.AntiAir - impact);
                                break;
                        }
                        break;
                }

                long defenderCasualties = FallenBattleValue(context.OpposingSquads);
                if (regionFaction.Entrenchment > 0)
                {
                    double casualtyMultiplier = 1.0 / (1.0 + regionFaction.Entrenchment / 5.0);
                    defenderCasualties = (long)(defenderCasualties * casualtyMultiplier);
                }
                long defenderStrengthBefore = regionFaction.MilitaryStrength;
                regionFaction.RemoveMilitaryStrength(defenderCasualties);
                long defenderStrengthAfter = regionFaction.MilitaryStrength;
                Faction attackingFaction = context.Order.AssignedSquads.FirstOrDefault()?.Faction;
                _recordScenarioPdfLost?.Invoke(
                    regionFaction,
                    Math.Max(0, defenderStrengthBefore - defenderStrengthAfter),
                    attackingFaction);
                GameLog.Debug(() =>
                    $"Mission attrition {context.Order.Mission.MissionType} -> "
                    + $"{MissionTurnProcessor.DescribeRegionFaction(regionFaction)}: "
                    + $"defenderLosses={defenderCasualties}, "
                    + $"defenderStrength={defenderStrengthBefore}->{regionFaction.MilitaryStrength}");

                ResolveOffensiveSurvivors(context);
            }
        }

        internal static void RemoveConsumedSpecialMissions(IEnumerable<Order> playerOrdersThisTurn)
        {
            foreach (Order order in playerOrdersThisTurn.Where(o => !ShouldPersistPlayerOrder(o)))
            {
                Mission mission = order.Mission;
                mission.RegionFaction?.Region?.SpecialMissions.Remove(mission);
            }
        }

        internal static void CleanupResolvedPlayerOrders(
            Sector sector,
            IEnumerable<Order> playerOrdersThisTurn)
        {
            foreach (Order order in playerOrdersThisTurn.ToList())
            {
                if (ShouldPersistPlayerOrder(order)) continue;

                sector.RemoveOrder(order);
                foreach (Squad squad in order.AssignedSquads)
                {
                    if (ReferenceEquals(squad.CurrentOrders, order))
                    {
                        squad.CurrentOrders = null;
                    }
                }
            }
        }

        internal static bool ShouldPersistPlayerOrder(Order order)
        {
            return order.Mission is ConstructionMission
                && order.AssignedSquads.Any(s => s.Faction?.IsPlayerFaction == true);
        }

        internal static void PruneInvalidSpecialMissions(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                foreach (Region region in planet.Regions)
                {
                    region.SpecialMissions.RemoveAll(mission =>
                    {
                        RegionFaction target = mission.RegionFaction;
                        if (target?.PlanetFaction?.Faction == null) return true;
                        if (!ReferenceEquals(target.Region, region)) return true;
                        if (!region.RegionFactionMap.TryGetValue(
                            target.PlanetFaction.Faction.Id,
                            out RegionFaction current))
                        {
                            return true;
                        }

                        return !ReferenceEquals(current, target);
                    });
                }
            }
        }

        internal static void ResolveReconResult(
            Faction reconningFaction,
            RegionFaction target,
            float impact,
            Action<PlanetFaction, Region, float> recordIntelGain = null)
        {
            if (target == null) return;
            PlanetFaction reconningPlanetFaction =
                reconningFaction != null
                && target.Region.Planet.PlanetFactionMap.TryGetValue(
                    reconningFaction.Id,
                    out PlanetFaction pf)
                    ? pf
                    : null;
            float observerBefore = reconningPlanetFaction?.GetRegionIntel(target.Region) ?? 0f;
            if (reconningPlanetFaction != null)
            {
                if (recordIntelGain != null)
                {
                    recordIntelGain(reconningPlanetFaction, target.Region, impact);
                }
                else
                {
                    reconningPlanetFaction.AddRegionIntel(target.Region, impact);
                }
            }
            float observerAfter = reconningPlanetFaction?.GetRegionIntel(target.Region) ?? observerBefore;
            GameLog.Debug(() =>
                $"Recon result {reconningFaction?.Name ?? "Unknown"} -> "
                + $"{MissionTurnProcessor.DescribeRegionFaction(target)}: "
                + $"impact={impact:F2}, regionIntel={observerBefore:F2}->{observerAfter:F2}");
        }

        private static long FallenBattleValue(IEnumerable<BattleSquad> squads)
        {
            if (squads == null) return 0;
            return squads
                .SelectMany(squad => squad.Soldiers)
                .Where(soldier => !soldier.CanFight)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
        }

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads)
        {
            if (squads == null) return 0;
            return squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
        }

        private static void ResolveOffensiveSurvivors(MissionContext context)
        {
            if (context.Order.Mission.MissionType != MissionType.Advance
                && context.Order.Mission.MissionType != MissionType.LightningRaid)
            {
                return;
            }
            BattleSquad first = context.MissionSquads.FirstOrDefault();
            if (first == null || first.IsPlayerSquad) return;

            long survivors = AbleBattleValue(context.MissionSquads);
            if (survivors <= 0) return;

            Faction attacker = first.Squad.Faction;
            if (context.Order.Mission.MissionType == MissionType.Advance && attacker.InvadesOnVictory)
            {
                EstablishInvaderPresence(
                    attacker,
                    context.Order.Mission.RegionFaction.Region,
                    survivors);
                GameLog.Debug(() =>
                    $"Offensive survivors {attacker.Name}: established foothold in "
                    + $"{context.Order.Mission.RegionFaction.Region.Planet.Name}/"
                    + $"{context.Order.Mission.RegionFaction.Region.Name}, survivors={survivors}");
            }
            else if (first.Squad.CurrentRegion != null
                     && first.Squad.CurrentRegion.RegionFactionMap.TryGetValue(
                         attacker.Id,
                         out RegionFaction home))
            {
                home.AddMilitaryStrength(survivors);
                GameLog.Debug(() =>
                    $"Offensive survivors {attacker.Name}: returned to "
                    + $"{home.Region.Planet.Name}/{home.Region.Name}, survivors={survivors}");
            }
        }

        internal static void EstablishInvaderPresence(Faction attacker, Region region, long survivors)
        {
            InvaderPresenceService.Establish(attacker, region, survivors);
        }
    }
}
