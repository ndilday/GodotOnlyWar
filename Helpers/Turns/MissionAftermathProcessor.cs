using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Orders;
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
        private readonly Action<IntelObservation> _recordTargetObservation;
        private readonly Action<RegionFaction, long, Faction> _recordScenarioPdfLost;

        internal MissionAftermathProcessor(
            Action<PlanetFaction, Region, float> recordIntelGain,
            Action<RegionFaction, long, Faction> recordScenarioPdfLost,
            Action<IntelObservation> recordTargetObservation = null)
        {
            _recordIntelGain = recordIntelGain;
            _recordScenarioPdfLost = recordScenarioPdfLost;
            _recordTargetObservation = recordTargetObservation;
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
                            context.Order.OwnerFaction,
                            regionFaction,
                            context.Impact,
                            _recordIntelGain,
                            _recordTargetObservation);
                        break;
                    case MissionType.Patrol:
                        RecordPatrolContacts(context, _recordTargetObservation);
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

                RecordTacticalBattleContacts(context, _recordTargetObservation);

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
                Faction attackingFaction = context.Order.OwnerFaction
                    ?? context.MissionSquads.FirstOrDefault()?.Faction;
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
                mission.Region?.SpecialMissions.Remove(mission);
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
                // Release the complete participant set. Characters remain at their resulting
                // physical posting; order lifetime is not encoded in that posting anymore.
                OrderForceService.ReleaseOrder(order);
            }
        }

        internal static bool ShouldPersistPlayerOrder(Order order)
        {
            // Show of Force joins construction as a standing, multi-week commitment: the request
            // it answers is measured in squad-weeks, so releasing the squads every turn (and
            // consuming the posted mission with them) would make the commitment unfulfillable.
            return (order.Mission is ConstructionMission
                    || order.Mission.MissionType == MissionType.ShowOfForce
                    || order.Mission.MissionType == MissionType.Recruitment)
                && order.OwnerFaction?.IsPlayerFaction == true;
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
                        if (mission.MissionType == MissionType.Extermination
                            && target.IsPublic)
                        {
                            return true;
                        }
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
            Action<PlanetFaction, Region, float> recordIntelGain = null,
            Action<IntelObservation> recordTargetObservation = null)
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
                    // RegionAwareness belongs to the observer, even when that faction does not occupy
                    // ground on the target planet. Recon from orbit therefore still needs a sparse
                    // PlanetFaction record in which to retain the resulting belief.
                    reconningPlanetFaction = new PlanetFaction(reconningFaction)
                    {
                        IsPublic = reconningFaction.IsPlayerFaction
                    };
                    planet.PlanetFactionMap[reconningFaction.Id] = reconningPlanetFaction;
                    planet.NotifyPlanetFactionAdded(reconningPlanetFaction);
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
                    reconningPlanetFaction.AddRegionAwareness(target.Region, impact);
                }

                if (recordTargetObservation != null
                    && impact != 0f
                    && target.PlanetFaction?.Faction != null
                    && target.PlanetFaction.Faction.Id != reconningFaction.Id)
                {
                    float evidence = impact > 0f
                        ? Math.Max(0.25f, impact)
                        : Math.Min(-0.25f, impact);
                    recordTargetObservation(new IntelObservation(
                        reconningPlanetFaction,
                        target.Region,
                        target.PlanetFaction.Faction,
                        evidence,
                        evidence > 0f ? target.Population : null,
                        evidence > 0f ? target.GetDeployedStrength() : null,
                        IntelObservationSource.Recon,
                        0));
                }
            }
            GameLog.Debug(() =>
                $"Recon result {reconningFaction?.Name ?? "Unknown"} -> "
                + $"{MissionTurnProcessor.DescribeRegionFaction(target)}: "
                + $"impact={impact:F2}");
        }

        private static void RecordPatrolContacts(
            MissionContext context,
            Action<IntelObservation> recordTargetObservation)
        {
            if (recordTargetObservation == null || context?.Order?.Mission?.RegionFaction == null)
            {
                return;
            }

            RegionFaction observerPresence = context.Order.Mission.RegionFaction;
            Faction observerFaction = observerPresence.PlanetFaction?.Faction;
            Region region = observerPresence.Region;
            if (observerFaction == null || region?.Planet == null || context.Impact <= 0f)
            {
                return;
            }

            PlanetFaction observer = GetAttachedPlanetFaction(region.Planet, observerPresence.PlanetFaction);
            float evidence = Math.Max(0.25f, context.Impact);
            foreach (RegionFaction target in region.RegionFactionMap.Values
                .Where(candidate => candidate?.PlanetFaction?.Faction != null
                    && candidate.PlanetFaction.Faction.Id != observerFaction.Id
                    && FactionRelationshipService.AreHostile(
                        observerFaction,
                        candidate.PlanetFaction.Faction,
                        region.Planet))
                .OrderBy(candidate => candidate.PlanetFaction.Faction.Id))
            {
                recordTargetObservation(new IntelObservation(
                    observer,
                    region,
                    target.PlanetFaction.Faction,
                    evidence,
                    target.Population,
                    target.GetDeployedStrength(),
                    IntelObservationSource.PatrolContact,
                    0));
            }
        }

        private static PlanetFaction GetAttachedPlanetFaction(
            Planet planet,
            PlanetFaction preferred)
        {
            if (planet.PlanetFactionMap.TryGetValue(
                preferred.Faction.Id,
                out PlanetFaction attached))
            {
                return attached;
            }

            planet.PlanetFactionMap[preferred.Faction.Id] = preferred;
            planet.NotifyPlanetFactionAdded(preferred);
            return preferred;
        }

        private static void RecordTacticalBattleContacts(
            MissionContext context,
            Action<IntelObservation> recordTargetObservation)
        {
            if (recordTargetObservation == null
                || context?.Order?.Mission?.RegionFaction == null
                || context.MissionSquads == null
                || context.MissionSquads.Count == 0)
            {
                return;
            }

            RegionFaction targetPresence = context.Order.Mission.RegionFaction;
            Region region = targetPresence.Region;
            Faction attackerFaction = context.MissionSquads
                .Select(squad => squad?.Faction)
                .FirstOrDefault(faction => faction != null);
            if (region?.Planet == null || attackerFaction == null) return;

            PlanetFaction attackerObserver = GetAttachedPlanetFaction(
                region.Planet,
                GetOrCreatePlanetFaction(region.Planet, attackerFaction));
            List<(Faction Faction, PlanetFaction Observer, long? Population, long? Military)> participants =
                new()
                {
                    (
                        attackerFaction,
                        attackerObserver,
                        null,
                        Math.Max(0L, context.MissionSquads
                            .SelectMany(squad => squad.AbleSoldiers)
                            .Sum(soldier => (long)soldier.Soldier.Template.BattleValue)))
                };

            AddTacticalParticipant(
                participants,
                targetPresence.PlanetFaction,
                targetPresence.Population,
                targetPresence.GetDeployedStrength());

            foreach (BattleSquad opposing in context.OpposingSquads ?? [])
            {
                Faction faction = opposing?.Faction;
                if (faction == null || participants.Any(item => item.Faction.Id == faction.Id)) continue;
                RegionFaction presence = region.RegionFactionMap.GetValueOrDefault(faction.Id);
                AddTacticalParticipant(
                    participants,
                    presence?.PlanetFaction ?? GetOrCreatePlanetFaction(region.Planet, faction),
                    presence?.Population,
                    presence?.GetDeployedStrength());
            }

            foreach ((Faction Faction, PlanetFaction Observer, long? Population, long? Military) observer in participants)
            {
                foreach ((Faction Faction, PlanetFaction Observer, long? Population, long? Military) subject in participants)
                {
                    if (observer.Faction.Id == subject.Faction.Id) continue;
                    FactionIntelBelief previous = observer.Observer.GetTargetBelief(region, subject.Faction);
                    float evidenceDelta = Math.Max(
                        0.25f,
                        FactionIntelligenceRules.LocatedThreshold - (previous?.Evidence ?? 0f));
                    recordTargetObservation(new IntelObservation(
                        observer.Observer,
                        region,
                        subject.Faction,
                        evidenceDelta,
                        subject.Population,
                        subject.Military,
                        IntelObservationSource.BattleContact,
                        0));
                }
            }
        }

        private static void AddTacticalParticipant(
            ICollection<(Faction Faction, PlanetFaction Observer, long? Population, long? Military)> participants,
            PlanetFaction observer,
            long? population,
            long? military)
        {
            if (observer?.Faction == null
                || participants.Any(item => item.Faction.Id == observer.Faction.Id))
            {
                return;
            }

            participants.Add((observer.Faction, observer, population, military));
        }

        private static PlanetFaction GetOrCreatePlanetFaction(Planet planet, Faction faction)
        {
            if (planet.PlanetFactionMap.TryGetValue(faction.Id, out PlanetFaction existing))
            {
                return existing;
            }

            PlanetFaction created = new(faction) { IsPublic = faction.IsPlayerFaction };
            planet.PlanetFactionMap[faction.Id] = created;
            planet.NotifyPlanetFactionAdded(created);
            return created;
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

            Faction attacker = first.Faction;
            Region returnRegion = first.CampaignCharacter?.EffectiveRegion
                ?? first.CampaignSquad?.CurrentRegion;
            if (context.Order.Mission.MissionType == MissionType.Advance
                && attacker.HasBehavior(FactionBehavior.InvadesOnVictory)
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
            else if (returnRegion != null
                     && returnRegion.RegionFactionMap.TryGetValue(
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
