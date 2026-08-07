using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Fortifications;
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
                        int orgLost = regionFaction.Population <= 0
                            ? 0
                            : (int)(context.Impact * 100 / regionFaction.Population);
                        int targetOrganization = Math.Max(
                            0,
                            regionFaction.Organization - orgLost);
                        long targetOrganizedBattleValue = (long)(
                            regionFaction.MilitaryStrength * (targetOrganization / 100.0));
                        regionFaction.DisorganizeMilitaryStrength(
                            Math.Max(
                                0L,
                                regionFaction.OrganizedMilitaryStrength
                                - targetOrganizedBattleValue));
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
                        // Sabotage wrecks the position that is actually there, so the loss comes
                        // out of the side's pooled works rather than only the nominal target's
                        // share of them (RegionDefenses.Damage spreads it across contributors).
                        context.SabotageDefenseLevelBefore = RegionDefenses.GetShared(
                            regionFaction, sabotageMission.DefenseType);
                        context.SabotageDamageDealt = RegionDefenses.Damage(
                            regionFaction, sabotageMission.DefenseType, impact);
                        break;
                }

                // Cumulative across every engagement in the mission, not just the last one.
                // context.OpposingSquads is REPLACED each time a step raises a fresh opposing force, so
                // once a mission can contain several battles (a multi-day assault, a recon intercepted on
                // more than one day) reading it here would report only the final engagement's dead and
                // silently discard every earlier day's attrition from the campaign. The battle steps
                // accumulate measured losses into the context as they happen; fall back to the old read
                // for any path that populated OpposingSquads without a battle step recording them.
                long defenderCasualties = context.DefenderBattleValueDestroyed > 0
                    ? context.DefenderBattleValueDestroyed
                    : FallenBattleValue(context.OpposingSquads);
                double entrenchment = RegionDefenses.GetShared(regionFaction, DefenseType.Entrenchment);
                if (entrenchment > 0)
                {
                    double casualtyMultiplier = 1.0 / (1.0 + entrenchment / 5.0);
                    defenderCasualties = (long)(defenderCasualties * casualtyMultiplier);
                }
                long defenderStrengthBefore = regionFaction.MilitaryStrength;
                if (context.Order.Mission.MissionType == MissionType.Ambush)
                {
                    RemoveProportionalAmbushLosses(regionFaction, defenderCasualties);
                }
                else
                {
                    regionFaction.RemoveOrganizedMilitaryStrength(defenderCasualties);
                }
                regionFaction.RemoveDisorganizedMilitaryStrength(
                    context.DisorganizedDefenderBattleValueDestroyed);
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

        internal static void RemoveProportionalAmbushLosses(
            RegionFaction regionFaction,
            long casualties)
        {
            long total = regionFaction.MilitaryStrength;
            if (casualties <= 0 || total <= 0) return;
            long actual = Math.Min(casualties, total);
            long organized = regionFaction.OrganizedMilitaryStrength;
            long organizedLoss = (long)Math.Round(actual * (organized / (double)total));
            organizedLoss = Math.Min(organized, organizedLoss);
            long disorganizedLoss = actual - organizedLoss;
            long removedDisorganized =
                regionFaction.RemoveDisorganizedMilitaryStrength(disorganizedLoss);
            regionFaction.RemoveOrganizedMilitaryStrength(
                actual - removedDisorganized);
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
                // The primary release path for attached specialists: attachment lasts exactly
                // as long as the order does, so an ordinary one-week mission returns its
                // specialists here. A Construction / Show-of-Force order persists and keeps
                // them, matching what it does with its squads.
                Orders.OrderAttachment.ReleaseAll(order);
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
            // Show of Force joins construction as a standing, multi-week commitment: the request
            // it answers is measured in squad-weeks, so releasing the squads every turn (and
            // consuming the posted mission with them) would make the commitment unfulfillable.
            return (order.Mission is ConstructionMission
                    || order.Mission.MissionType == MissionType.ShowOfForce)
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
            PlanetFaction reconningPlanetFaction = null;
            if (reconningFaction != null)
            {
                Planet planet = target.Region.Planet;
                if (!planet.PlanetFactionMap.TryGetValue(
                    reconningFaction.Id,
                    out reconningPlanetFaction))
                {
                    // RegionIntel belongs to the observer, even when that faction does not occupy
                    // ground on the target planet. Recon from orbit therefore still needs a sparse
                    // PlanetFaction record in which to retain the resulting belief.
                    reconningPlanetFaction = new PlanetFaction(reconningFaction)
                    {
                        IsPublic = reconningFaction.IsPlayerFaction
                    };
                    planet.PlanetFactionMap[reconningFaction.Id] = reconningPlanetFaction;
                }
            }
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
            GameLog.Debug(() =>
                $"Recon result {reconningFaction?.Name ?? "Unknown"} -> "
                + $"{MissionTurnProcessor.DescribeRegionFaction(target)}: "
                + $"impact={impact:F2}");
        }

        private static long FallenBattleValue(IEnumerable<BattleSquad> squads)
        {
            if (squads == null) return 0;
            return squads
                .SelectMany(squad => squad.Soldiers)
                .Where(soldier => !soldier.IsCombatEffective)
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
            if (context.Order.Mission.MissionType == MissionType.Advance
                && attacker.InvadesOnVictory
                && !context.ReciprocalAssaultDefeated)
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
