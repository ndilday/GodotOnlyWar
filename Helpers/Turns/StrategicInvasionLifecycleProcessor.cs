using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fleets;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves the strategic invasion lifecycle which is not expressible as ordinary faction growth: latent
    /// off-map sources, strategic invasion force formation, landing allocation, local strategic invasion force attraction, and leader loss.
    /// </summary>
    internal sealed class StrategicInvasionLifecycleProcessor
    {
        private readonly GameSession _session;

        internal StrategicInvasionLifecycleProcessor(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal static void SeedGhostSources(Sector sector, GameRulesData rules, IRNG random)
        {
            Faction ghostFaction = FactionCapabilities.WithCapability(
                rules?.Factions, FactionBehavior.HasGhostPlanets).FirstOrDefault();
            if (sector == null || ghostFaction == null || rules.FactionBehaviorRules == null || random == null) return;
            FactionBehaviorRulesProfile behaviorRules = rules.FactionBehaviorRules;

            IReadOnlyList<int> eligibleTemplateIds = rules.PlanetTemplateEligibility
                ?.GetEligibleTemplateIds(PlanetTemplateEligibilityKeys.GhostPopulationSource)
                ?? [];
            List<PlanetTemplate> eligibleTemplates = eligibleTemplateIds
                .Select(id => rules.PlanetTemplateMap.GetValueOrDefault(id))
                .Where(template => template != null)
                .ToList();
            if (eligibleTemplates.Count == 0) return;

            SectorGenerationProfile profile = rules.SectorGenerationProfile;
            List<Coordinate> emptyTiles = [];
            for (ushort y = 0; y < profile.SectorHeight; y++)
            {
                for (ushort x = 0; x < profile.SectorWidth; x++)
                {
                    Coordinate position = new(x, y);
                    if (sector.GetPlanetByPosition(position) == null
                        && !sector.GhostPopulationSources.Any(source => source.Position.Equals(position)))
                    {
                        emptyTiles.Add(position);
                    }
                }
            }

            int nextId = sector.GhostPopulationSources.Select(source => source.Id).DefaultIfEmpty(0).Max() + 1;
            foreach (Coordinate position in emptyTiles)
            {
                if (random.GetLinearDouble() >= behaviorRules.GhostSourceChancePerEmptyTile)
                {
                    continue;
                }

                sector.AddGhostPopulationSource(CreateGhostSource(
                    nextId++, position, eligibleTemplates, random, ghostFaction));
            }

            if (sector.GhostPopulationSources.Count == 0 && emptyTiles.Count >= behaviorRules.MinimumGhostSourcesPerSector)
            {
                Coordinate position = emptyTiles[random.GetIntBelowMax(0, emptyTiles.Count)];
                sector.AddGhostPopulationSource(CreateGhostSource(
                    nextId, position, eligibleTemplates, random, ghostFaction));
            }
        }

        internal void ProcessWeeklyState(Sector sector)
        {
            Faction dormantFaction = FactionCapabilities.WithCapability(
                _session.Rules.Factions, FactionBehavior.HasDormantPopulations).FirstOrDefault();
            Faction invasionFaction = FactionCapabilities.WithCapability(
                _session.Rules.Factions, FactionBehavior.GeneratesInvasions).FirstOrDefault();
            FactionBehaviorRulesProfile behaviorRules = _session.Rules.FactionBehaviorRules;
            if (sector == null || behaviorRules == null) return;

            if (dormantFaction != null)
            {
                ResolvePdfCulling(sector, dormantFaction, behaviorRules);
                RegrowDormantPopulations(sector, dormantFaction, invasionFaction, invasionFaction != null);
            }
            foreach (GhostPopulationSource source in sector.GhostPopulationSources.ToList())
            {
                if (source.FactionId.HasValue
                    && !FactionCapabilities.GeneratesInvasions(
                        _session.Rules.Factions.FirstOrDefault(faction => faction.Id == source.FactionId.Value)))
                {
                    continue;
                }
                ApplyLogisticGrowth(source, _session.Random, behaviorRules);
                source.Consolidation = DormantPopulationRules.UpdateConsolidation(
                    behaviorRules, source.Consolidation,
                    _session.Random.NextRandomZValue());
                if (source.Consolidation >= 1.0 && invasionFaction != null)
                {
                    FormInvasionFromGhostSource(sector, source, invasionFaction);
                }
            }

            // Merge any persisted successor identities before one of them can arrive ahead of the
            // others. Newly created successors are also merged again after this turn's battles.
            if (invasionFaction != null) MergeSuccessorForces(sector, invasionFaction);
            ProcessInvasionTransit(sector);
        }

        private static void ResolvePdfCulling(
            Sector sector,
            Faction invasionFaction,
            FactionBehaviorRulesProfile rules)
        {
            Faction pdfFaction = sector.PlayerForce?.Faction?.IsDefaultFaction == true
                ? sector.PlayerForce.Faction
                : sector.Planets.Values.SelectMany(planet => planet.Regions)
                    .SelectMany(region => region.RegionFactionMap.Values)
                    .Select(presence => presence.PlanetFaction.Faction)
                    .FirstOrDefault(faction => faction.IsDefaultFaction);
            if (pdfFaction == null) return;

            foreach (Region region in sector.Planets.Values.SelectMany(planet => planet.Regions))
            {
                RegionFaction target = region.RegionFactionMap.Values.FirstOrDefault(presence =>
                    presence.PlanetFaction.Faction == invasionFaction
                    && presence.StrategicInvasionForceId == null
                    && !presence.IsPublic);
                RegionFaction pdf = region.RegionFactionMap.GetValueOrDefault(pdfFaction.Id);
                FactionIntelBelief belief = pdf?.PlanetFaction.GetTargetBelief(region, invasionFaction);
                if (target == null || pdf == null || !DormantPopulationCulling.CanTarget(target, belief)) continue;
                if (pdf.GetDeployedStrength() < rules.DormantCullingOutsideHelpEffectivePdfFloor
                    || HasPublicHostileThreat(region, pdfFaction)
                    || HasCommittedDefence(pdf, region))
                {
                    // Culling is a secondary governor task. A weak PDF requests help, while
                    // public threats and already-committed defenders retain priority.
                    continue;
                }

                DormantPopulationCullingResult result = DormantPopulationCulling.Resolve(
                    target, belief, rules, pdf.GetDeployedStrength());
                if (result.PopulationRemoved > 0) target.RemoveMilitaryStrength(result.PopulationRemoved);
                target.DormantConsolidation = Math.Clamp(
                    target.DormantConsolidation - result.ConsolidationRemoved, 0.0, 1.0);
            }
        }

        private static bool HasPublicHostileThreat(Region region, Faction pdfFaction) =>
            region.RegionFactionMap.Values.Any(presence =>
                presence.IsPublic
                && presence.PlanetFaction?.Faction != null
                && presence.PlanetFaction.Faction != pdfFaction
                && FactionRelationshipService.AreHostile(
                    pdfFaction,
                    presence.PlanetFaction.Faction,
                    region.Planet));

        private static bool HasCommittedDefence(RegionFaction pdf, Region region) =>
            pdf.LandedSquads.Any(squad =>
                squad?.CurrentOrders?.Mission?.Region == region
                && squad.CurrentOrders.Mission.MissionType == MissionType.DefenseInDepth);

        internal void ProcessAttractionAndFragmentation(Sector sector)
        {
            Faction invasionFaction = FactionCapabilities.WithCapability(
                _session.Rules.Factions, FactionBehavior.GeneratesInvasions).FirstOrDefault();
            if (sector == null || invasionFaction == null) return;

            AttractUnaffiliatedPopulation(sector, invasionFaction);
            MergeSuccessorForces(sector, invasionFaction);
        }

        /// <summary>
        /// Gives the invasion opening its persistent identity. The scenario stamp creates the public
        /// beachhead using the same region allocation rules as a normal landing; this method then
        /// binds every stamped region to one real commander instead of leaving a collection of
        /// unrelated faction presences.
        /// </summary>
        internal StrategicInvasionForce EstablishOpeningInvasion(Sector sector, Planet planet, Faction invasionFaction)
        {
            if (sector == null || planet == null || invasionFaction == null) return null;
            List<RegionFaction> presences = planet.Regions
                .Select(region => region.RegionFactionMap.GetValueOrDefault(invasionFaction.Id))
                .Where(presence => presence?.IsPublic == true && presence.Population > 0)
                .ToList();
            if (presences.Count == 0) return null;

            long id = sector.GetNextStrategicInvasionForceId();
            Squad command = CreateCommandSquad(invasionFaction, id, _session.Random);
            RegisterCommandUnit(sector, invasionFaction, command, id);
            Region primary = presences
                .OrderByDescending(presence => presence.OrganizedMilitaryStrength)
                .ThenBy(presence => presence.Region.Id)
                .First().Region;
            command.CurrentRegion = primary;
            StrategicInvasionForce invasionForce = new(id, invasionFaction, command, primary, planet);
            foreach (RegionFaction presence in presences)
            {
                presence.StrategicInvasionForceId = id;
                presence.DormantConsolidation = 1.0;
                invasionForce.TrackRegion(presence);
            }
            sector.AddStrategicInvasionForce(invasionForce);
            return invasionForce;
        }

        internal static bool StrategicCommanderCanBeReached(
            StrategicInvasionForce invasionForce,
            Region region,
            float successMargin,
            IRNG random)
            => StrategicCommanderCanBeReached(invasionForce, region, successMargin, random, null);

        internal static bool StrategicCommanderCanBeReached(
            StrategicInvasionForce invasionForce,
            Region region,
            float successMargin,
            IRNG random,
            FactionBehaviorRulesProfile rules)
        {
            if (invasionForce?.IsActive != true || region == null || random == null) return false;

            long regionalBattleValue = invasionForce.KnownRegions
                .Where(presence => presence?.StrategicInvasionForceId == invasionForce.Id
                    && presence.Region == region)
                .Sum(presence => presence.OrganizedMilitaryStrength);
            long totalBattleValue = invasionForce.KnownRegions
                .Where(presence => presence?.StrategicInvasionForceId == invasionForce.Id)
                .Sum(presence => presence.OrganizedMilitaryStrength);
            double concentration = totalBattleValue <= 0
                ? 0.0
                : Math.Clamp(regionalBattleValue / (double)totalBattleValue, 0.0, 1.0);
            double successQuality = Math.Clamp(
                successMargin / (rules?.ExceptionalAssassinationMargin
                    ?? DormantPopulationRules.ExceptionalAssassinationMargin),
                0.0,
                1.0);
            return random.GetLinearDouble() < concentration * successQuality;
        }

        internal static void AttachCommandersToTacticalOrders(
            Sector sector,
            Faction faction,
            IList<Order> orders,
            IEnumerable<Order> existingOrders = null)
        {
            if (sector == null || faction == null || orders == null) return;
            foreach (StrategicInvasionForce invasionForce in sector.StrategicInvasionForces.Where(item =>
                item.IsActive && item.Faction == faction && item.CurrentRegion != null))
            {
                bool currentRegionIsAssaulted = existingOrders?.Any(candidate =>
                    candidate?.OwnerFaction != faction
                    && candidate.Mission?.RegionFaction?.Region == invasionForce.CurrentRegion
                    && (candidate.Mission is StrategicCombatMission
                        || candidate.Force?.AllSoldiers.Any() == true)
                    && candidate.Mission.MissionType is MissionType.Advance
                        or MissionType.Ambush
                        or MissionType.LightningRaid) == true;
                if (currentRegionIsAssaulted)
                {
                    // Defensive battle priority: leave the command squad unattached so
                    // PrepareAssaultMissionStep can pull it into the regional defence.
                    invasionForce.CommandSquad.CurrentOrders = null;
                    continue;
                }
                if (invasionForce.CommandSquad.CurrentOrders != null
                    && orders.Contains(invasionForce.CommandSquad.CurrentOrders)) continue;
                // NPC orders are transient and are not kept in the sector order index. Release the
                // command squad's back-reference before planning a new week.
                invasionForce.CommandSquad.CurrentOrders = null;

                Order order = orders.FirstOrDefault(candidate =>
                    candidate.OwnerFaction == faction
                    && (candidate.StrategicInvasionForceId == null || candidate.StrategicInvasionForceId == invasionForce.Id)
                    && candidate.Force.AllSoldiers.Any()
                    && candidate.Mission?.MissionType is MissionType.Advance
                        or MissionType.Ambush
                        or MissionType.LightningRaid
                    && candidate.AssignedSquads.Any(squad => squad.CurrentRegion == invasionForce.CurrentRegion));
                if (order == null) continue;

                order.AssignedSquads.Add(invasionForce.CommandSquad);
                order.StrategicInvasionForceId = invasionForce.Id;
                invasionForce.CommandSquad.CurrentOrders = order;
                invasionForce.CommandSquad.CurrentRegion = invasionForce.CurrentRegion;
            }
        }

        internal void ResolveStrategicLeaderDeaths(
            Sector sector,
            IEnumerable<StrategicCombatResult> results)
        {
            if (sector == null || results == null) return;
            foreach (StrategicCombatResult result in results)
            {
                if (result?.Attacker == null || result.Target?.Region == null) continue;
                Faction defender = result.Target.PlanetFaction?.Faction;
                StrategicInvasionForce invasionForce = ResolveInvasionForceForResult(sector, result, result.Attacker)
                    ?? ResolveInvasionForceForResult(sector, result, defender);
                if (invasionForce == null) continue;

                bool invasionAttacked = invasionForce.Faction == result.Attacker;
                long committed = invasionAttacked ? result.CommittedBattleValue : result.DefenderBattleValue;
                long losses = invasionAttacked ? result.AttackerLosses : result.DefenderLosses;
                double chance = 0.5 * Math.Clamp(
                    losses / (double)Math.Max(1L, committed), 0.0, 1.0);
                if (_session.Random.GetLinearDouble() < chance) KillAndFragmentForce(sector, invasionForce);
            }
        }

        internal void ResolveTacticalLeaderDeaths(Sector sector, IEnumerable<MissionContext> contexts)
        {
            if (sector == null || contexts == null) return;
            HashSet<int> killed = contexts.SelectMany(context => context.KilledSoldierIds).ToHashSet();
            foreach (StrategicInvasionForce invasionForce in sector.StrategicInvasionForces.Where(item =>
                item.IsActive && item.StrategicCommander?.Id is int id && killed.Contains(id)).ToList())
            {
                KillAndFragmentForce(sector, invasionForce);
            }
        }

        internal void AffiliateCapturedRegion(Sector sector, StrategicCombatResult result)
        {
            if (sector == null || result?.Attacker == null
                || !FactionCapabilities.GeneratesInvasions(result.Attacker))
            {
                return;
            }

            StrategicInvasionForce invasionForce = ResolveInvasionForceForResult(sector, result, result.Attacker);
            RegionFaction presence = result.Target?.Region?.RegionFactionMap
                .GetValueOrDefault(result.Attacker.Id);
            if (invasionForce == null || presence == null) return;

            presence.IsPublic = true;
            presence.StrategicInvasionForceId = invasionForce.Id;
            presence.DormantConsolidation = 1.0;
            invasionForce.TrackRegion(presence);
            invasionForce.CurrentRegion = result.Target.Region;
        }

        internal void AffiliateTacticalCaptures(Sector sector, IEnumerable<MissionContext> contexts)
        {
            if (sector == null || contexts == null) return;
            foreach (MissionContext context in contexts.Where(item =>
                item?.Order?.Mission?.MissionType == MissionType.Advance
                && FactionCapabilities.GeneratesInvasions(item.Order.OwnerFaction)))
            {
                Region region = context.Order.Mission.RegionFaction?.Region;
                if (region == null) continue;
                StrategicInvasionForce invasionForce = ResolveInvasionForceForOrder(sector, context.Order);
                RegionFaction presence = region.RegionFactionMap
                    .GetValueOrDefault(context.Order.OwnerFaction.Id);
                if (invasionForce == null || presence == null) continue;
                presence.IsPublic = true;
                presence.StrategicInvasionForceId = invasionForce.Id;
                presence.DormantConsolidation = 1.0;
                invasionForce.TrackRegion(presence);
                if (context.MissionSquads.Any(squad => squad.CampaignSquad == invasionForce.CommandSquad))
                {
                    invasionForce.CurrentRegion = region;
                }
            }
        }

        private void FormInvasionFromGhostSource(Sector sector, GhostPopulationSource source, Faction invasionFaction)
        {
            long dispatchedBattleValue = (long)Math.Floor(
                source.Population * DormantPopulationRules.MobilizationFraction(
                    _session.Rules.FactionBehaviorRules,
                    _session.Random.NextRandomZValue()));
            dispatchedBattleValue = Math.Clamp(dispatchedBattleValue, 0, source.Population);
            source.Population -= dispatchedBattleValue;
            source.Consolidation = source.PopulationCapacity <= 0
                ? 0.0
                : Math.Clamp(source.Population / (double)source.PopulationCapacity, 0.0, 1.0);

            if (dispatchedBattleValue <= 0)
            {
                // Do not create an empty command identity when a tiny remainder rounds below the
                // minimum representable BV. The ecosystem remains latent and can grow normally.
                return;
            }

            Planet target = ChooseLandingPlanet(sector, invasionFaction, dispatchedBattleValue);
            if (target == null)
            {
                // Keep a latent ecosystem intact if a degenerate sector has no landing world.
                // This should not occur in normal campaigns, but it prevents a threshold crossing
                // from silently deleting population when a test or future mode supplies an empty
                // sector.
                source.Population += dispatchedBattleValue;
                source.Consolidation = source.PopulationCapacity <= 0
                    ? 0.0
                    : Math.Clamp(source.Population / (double)source.PopulationCapacity, 0.0, 1.0);
                return;
            }

            long invasionForceId = sector.GetNextStrategicInvasionForceId();
            List<(Region Region, long BattleValue)> allocations = AllocateLanding(target, invasionFaction, dispatchedBattleValue);
            Region primary = allocations.FirstOrDefault(item => item.BattleValue > 0).Region
                ?? target.Regions.OrderByDescending(region => region.Population).ThenBy(region => region.Id).First();
            Squad commandSquad = CreateCommandSquad(invasionFaction, invasionForceId, _session.Random);
            RegisterCommandUnit(sector, invasionFaction, commandSquad, invasionForceId);
            commandSquad.CurrentRegion = primary;
            StrategicInvasionForce invasionForce = new(invasionForceId, invasionFaction, commandSquad, primary, target);

            foreach (var allocation in allocations.Where(item => item.BattleValue > 0))
            {
                RegionFaction presence = EstablishFactionPresence(invasionFaction, allocation.Region, allocation.BattleValue);
                presence.StrategicInvasionForceId = invasionForce.Id;
                presence.DormantConsolidation = 1.0;
                invasionForce.TrackRegion(presence);
            }

            sector.AddStrategicInvasionForce(invasionForce);
            GameLog.Info(() =>
                $"Strategic invasion force {invasionForce.Id} formed at {target.Name}: dispatchedBV={dispatchedBattleValue}, "
                + $"regions={allocations.Count(item => item.BattleValue > 0)}, "
                + $"sourceRemainder={source.Population}");
        }

        private void KillAndFragmentForce(Sector sector, StrategicInvasionForce invasionForce)
        {
            invasionForce.IsActive = false;
            invasionForce.CommandSquad.CurrentOrders = null;
            invasionForce.CurrentRegion = null;

            List<RegionFaction> surviving = AllCapabilityPresences(sector, invasionForce.Faction)
                .Where(presence => presence.StrategicInvasionForceId == invasionForce.Id)
                .ToList();
            foreach (RegionFaction presence in surviving)
            {
                presence.StrategicInvasionForceId = null;
                // A dead commander breaks coordination, not the dormant ecosystem's physical presence.
                // Keep an existing public faction holding visible so an single-faction world remains an
                // on-map ghost planet; only an actually empty indelible presence is hidden by its
                // Population setter.
                presence.DormantConsolidation = 0.0;
            }

            foreach (RegionFaction presence in surviving
                .Where(item => item.OrganizedMilitaryStrength >= _session.Rules.FactionBehaviorRules.SuccessorGenerationMinimumBattleValue))
            {
                CreateSuccessorForce(sector, invasionForce.Faction, presence);
            }
        }

        private void CreateSuccessorForce(Sector sector, Faction faction, RegionFaction presence)
        {
            long id = sector.GetNextStrategicInvasionForceId();
            Squad command = CreateCommandSquad(faction, id, _session.Random);
            RegisterCommandUnit(sector, faction, command, id);
            long survivingBattleValue = presence.OrganizedMilitaryStrength;
            Planet destination = ChooseSuccessorDestination(sector, faction, presence);
            Region currentRegion = destination == presence.Region.Planet
                ? presence.Region
                : null;
            StrategicInvasionForce successor = new(id, faction, command, currentRegion, presence.Region.Planet)
            {
                DestinationPlanet = currentRegion == null ? destination : null,
                TravelWeeksRemaining = currentRegion == null
                    ? CalculateInvasionTravelWeeks(sector, presence.Region.Planet, destination)
                    : 0,
                TransitBattleValue = currentRegion == null ? survivingBattleValue : 0
            };
            command.CurrentRegion = currentRegion;
            if (currentRegion == null && survivingBattleValue > 0)
            {
                // A traveling splinter leaves no active population behind. The indelible
                // RegionFaction remains as a zero-strength, hidden ecological marker and will
                // regrow only if the traveling identity does not return.
                presence.RemoveMilitaryStrength(survivingBattleValue);
                presence.IsPublic = false;
            }
            presence.StrategicInvasionForceId = id;
            presence.IsPublic = currentRegion != null && presence.Population > 0;
            presence.DormantConsolidation = 1.0;
            successor.TrackRegion(presence);
            sector.AddStrategicInvasionForce(successor);
        }

        private Planet ChooseSuccessorDestination(Sector sector, Faction faction, RegionFaction presence)
        {
            Planet origin = presence.Region.Planet;
            long available = presence.OrganizedMilitaryStrength;
            bool originHasViableTarget = origin.Regions.Any(region =>
                IsViableLanding(region, faction, available));
            if (originHasViableTarget) return origin;

            Planet viableDestination = sector.Planets.Values
                .Where(planet => planet != origin)
                .Where(planet => planet.Regions.Any(region => IsViableLanding(region, faction, available)))
                .OrderBy(planet => FleetRouteCalculator.CalculateDistance(origin, planet))
                .ThenBy(planet => planet.Id)
                .FirstOrDefault() ?? origin;
            return viableDestination;
        }

        private bool IsViableLanding(Region region, Faction faction, long available)
        {
            long defending = DefendingBattleValue(region, faction);
            long required = defending > 0
                ? (long)Math.Ceiling(defending * _session.Rules.FactionBehaviorRules.DefendedLandingRatio)
                : _session.Rules.FactionBehaviorRules.UndefendedLandingBattleValue;
            return available >= required;
        }

        private int CalculateInvasionTravelWeeks(Sector sector, Planet origin, Planet destination)
        {
            if (origin == null || destination == null || origin == destination) return 0;
            FleetRouteScope scope = FleetRouteCalculator.DetermineScope(
                origin,
                destination,
                _session.Rules.SectorGenerationProfile.MaxSubsectorDiameter);
            FleetRoute route = new FleetRouteCalculator().CalculateBestRoute(
                origin,
                destination,
                sector.WarpLanes,
                scope,
                _session.Random.NextRandomZValue(),
                _session.Random.NextRandomZValue());
            return Math.Max(1, (int)Math.Ceiling(
                route.ObjectiveTotalWeeks * _session.Rules.FactionBehaviorRules.TravelMultiplier));
        }

        private void MergeSuccessorForces(Sector sector, Faction faction)
        {
            foreach (IGrouping<Planet, StrategicInvasionForce> group in sector.StrategicInvasionForces
                .Where(invasionForce => invasionForce.IsActive
                    && invasionForce.Faction == faction
                    && invasionForce.IsInTransit
                    && invasionForce.DestinationPlanet != null)
                .GroupBy(invasionForce => invasionForce.DestinationPlanet))
            {
                List<StrategicInvasionForce> invasionForces = group
                    .OrderByDescending(invasionForce => invasionForce.OrganizedBattleValue)
                    .ThenBy(invasionForce => invasionForce.Id)
                    .ToList();
                if (invasionForces.Count < 2) continue;

                StrategicInvasionForce strongest = invasionForces[0];
                long combined = invasionForces.Sum(invasionForce => invasionForce.OrganizedBattleValue);
                long merged = (long)Math.Floor(combined * Math.Max(
                    0.0,
                    1.0 - _session.Rules.FactionBehaviorRules.SuccessorMergeLeaderLoss * (invasionForces.Count - 1)));
                long mergedId = sector.GetNextStrategicInvasionForceId();
                StrategicInvasionForce mergedForce = new(
                    mergedId,
                    faction,
                    strongest.CommandSquad,
                    null,
                    strongest.OriginPlanet)
                {
                    DestinationPlanet = group.Key,
                    TravelWeeksRemaining = invasionForces.Min(invasionForce => invasionForce.TravelWeeksRemaining),
                };

                foreach (StrategicInvasionForce absorbed in invasionForces)
                {
                    foreach (RegionFaction presence in AllCapabilityPresences(sector, faction)
                        .Where(presence => presence.StrategicInvasionForceId == absorbed.Id))
                    {
                        presence.StrategicInvasionForceId = mergedId;
                        mergedForce.TrackRegion(presence);
                    }
                    absorbed.IsActive = false;
                    absorbed.CommandSquad.CurrentOrders = null;
                    absorbed.CurrentRegion = null;
                }
                sector.AddStrategicInvasionForce(mergedForce);

                // Regional remnants are moved onto the new identity before the merge loss is
                // applied. The transit payload then carries the rest, so the leader-loss formula is
                // accounted for exactly once even when a successor left a hidden organized remnant
                // behind at its departure world.
                long regionalBattleValue = mergedForce.KnownRegions
                    .Where(presence => presence.StrategicInvasionForceId == mergedId)
                    .Sum(presence => presence.OrganizedMilitaryStrength);
                long commandBattleValue = CommandBattleValue(mergedForce.CommandSquad);
                long maximumRegionalBattleValue = Math.Max(0L, merged - commandBattleValue);
                long regionalLoss = Math.Max(0L, regionalBattleValue - maximumRegionalBattleValue);
                foreach (RegionFaction presence in mergedForce.KnownRegions
                    .Where(presence => presence.StrategicInvasionForceId == mergedId)
                    .OrderByDescending(presence => presence.OrganizedMilitaryStrength))
                {
                    if (regionalLoss <= 0) break;
                    regionalLoss -= presence.RemoveOrganizedMilitaryStrength(regionalLoss);
                }
                regionalBattleValue = mergedForce.KnownRegions
                    .Where(presence => presence.StrategicInvasionForceId == mergedId)
                    .Sum(presence => presence.OrganizedMilitaryStrength);
                mergedForce.TransitBattleValue = Math.Max(
                    0L,
                    merged - commandBattleValue - regionalBattleValue);
            }
        }

        private void AttractUnaffiliatedPopulation(Sector sector, Faction invasionFaction)
        {
            List<StrategicInvasionForce> active = sector.StrategicInvasionForces
                .Where(invasionForce => invasionForce.IsActive && invasionForce.CurrentRegion != null)
                .ToList();
            if (active.Count == 0) return;

            foreach (RegionFaction source in AllCapabilityPresences(sector, invasionFaction)
                .Where(presence => presence.StrategicInvasionForceId == null && presence.Population > 0)
                .ToList())
            {
                StrategicInvasionForce attractor = active
                    .Where(invasionForce => invasionForce.CurrentRegion.GetAdjacentRegions().Contains(source.Region))
                    .OrderByDescending(invasionForce => invasionForce.CurrentBattleValue)
                    .ThenBy(invasionForce => invasionForce.Id)
                    .FirstOrDefault();
                if (attractor == null) continue;

                long moved = source.Population / 3;
                if (moved <= 0) continue;
                source.Population -= moved;
                source.IsPublic = source.Population > 0 && source.IsPublic;

                RegionFaction destination = EstablishFactionPresence(
                    invasionFaction,
                    attractor.CurrentRegion,
                    moved);
                destination.StrategicInvasionForceId = attractor.Id;
                destination.IsPublic = true;
                attractor.TrackRegion(destination);
            }
        }

        private void RegrowDormantPopulations(
            Sector sector,
            Faction dormantFaction,
            Faction invasionFaction,
            bool canGenerateInvasions)
        {
            FactionBehaviorRulesProfile rules = _session.Rules.FactionBehaviorRules;
            foreach (RegionFaction presence in AllCapabilityPresences(sector, dormantFaction)
                .Where(presence => presence.StrategicInvasionForceId == null))
            {
                // PlanetDemographicsProcessor handles population growth. This pass owns only the
                // feral consolidation clock and the emergence gate; an empty marker never
                // resurrects itself merely because a turn elapsed.
                presence.GrowthMultiplier = 1.0f;
                presence.DormantConsolidation = DormantPopulationRules.UpdateConsolidation(
                    rules, presence.DormantConsolidation,
                    _session.Random.NextRandomZValue());

                bool canEmerge = canGenerateInvasions
                    && presence.Population >= rules.DormantEmergenceMinimumPopulation
                    && presence.DormantConsolidation >= 1.0
                    && presence.OrganizedMilitaryStrength >= rules.SuccessorGenerationMinimumBattleValue;
                if (canEmerge && _session.Random.GetLinearDouble() < rules.DormantEmergenceChance)
                {
                    CreateSuccessorForce(sector, invasionFaction, presence);
                }
            }
        }

        private static void ApplyLogisticGrowth(GhostPopulationSource source, IRNG random, FactionBehaviorRulesProfile rules)
        {
            double growth = source.Population * rules.GhostLogisticGrowthRate
                * CarryingCapacityFactor(source.PopulationCapacity, source.Population);
            source.Population = Math.Max(0, source.Population + StochasticRound(growth, random));
        }

        private static double CarryingCapacityFactor(Region region, long population) =>
            CarryingCapacityFactor(region?.CarryingCapacity ?? 0, population);

        private static double CarryingCapacityFactor(long capacity, long population)
        {
            if (capacity <= 0) return 1.0;
            return Math.Clamp(1.0 - population / (double)capacity, 0.0, 1.0);
        }

        private static long StochasticRound(double value, IRNG random)
        {
            long whole = (long)Math.Truncate(value);
            double fraction = value - whole;
            return whole + (random.GetLinearDouble() < fraction ? 1 : 0);
        }

        private static GhostPopulationSource CreateGhostSource(int id, Coordinate position,
            IReadOnlyList<PlanetTemplate> templates, IRNG random, Faction faction)
        {
            PlanetTemplate template = WeightedTemplate(templates, random);
            long capacity = RollLogNormal(template.CarryingCapacityRange.Floor,
                template.CarryingCapacityRange.Scale, random);
            long population = Math.Min(capacity, RollLogNormal(template.PopulationRange.Floor,
                template.PopulationRange.Scale, random));
            return new GhostPopulationSource(id, position, template, population, capacity,
                random.GetDoubleInRange(0.0, 1.0), faction);
        }

        private static PlanetTemplate WeightedTemplate(IReadOnlyList<PlanetTemplate> templates, IRNG random)
        {
            long total = templates.Sum(template => Math.Max(0, template.Probability));
            if (total <= 0) return templates[0];
            long roll = (long)(random.GetLinearDouble() * total);
            long cumulative = 0;
            foreach (PlanetTemplate template in templates)
            {
                cumulative += Math.Max(0, template.Probability);
                if (roll < cumulative) return template;
            }
            return templates[^1];
        }

        private static long RollLogNormal(double floor, double scale, IRNG random) =>
            Math.Max(1L, (long)(floor + Math.Pow(10, random.NextRandomZValue()) * scale));

        private Planet ChooseLandingPlanet(Sector sector, Faction invasionFaction, long available)
        {
            var candidates = sector.Planets.Values.Select(planet => new
            {
                Planet = planet,
                Defended = planet.Regions
                    .Select(region => DefendingBattleValue(region, invasionFaction))
                    .DefaultIfEmpty(0)
                    .Max()
            }).ToList();

            return candidates
                .Where(candidate => candidate.Defended > 0
                    && available >= (long)Math.Ceiling(candidate.Defended * _session.Rules.FactionBehaviorRules.DefendedLandingRatio))
                .OrderByDescending(candidate => candidate.Defended)
                .ThenByDescending(candidate => candidate.Planet.Population)
                .ThenBy(candidate => candidate.Planet.Id)
                .Select(candidate => candidate.Planet)
                .FirstOrDefault()
                ?? candidates
                    .OrderByDescending(candidate => candidate.Planet.Population)
                    .ThenBy(candidate => candidate.Planet.Id)
                    .Select(candidate => candidate.Planet)
                    .FirstOrDefault();
        }

        private List<(Region Region, long BattleValue)> AllocateLanding(
            Planet planet, Faction invasionFaction, long available)
        {
            List<Region> defended = planet.Regions
                .Where(region => DefendingBattleValue(region, invasionFaction) > 0)
                .OrderByDescending(region => DefendingBattleValue(region, invasionFaction))
                .ThenBy(region => region.Id)
                .ToList();
            List<Region> undefended = planet.Regions
                .Where(region => !defended.Contains(region))
                .OrderByDescending(region => region.Population)
                .ThenBy(region => region.Id)
                .ToList();
            List<(Region Region, long BattleValue)> result = [];
            long remaining = Math.Max(0, available);

            foreach (Region region in defended)
            {
                if (remaining <= 0) break;
                long desired = (long)Math.Ceiling(
                    DefendingBattleValue(region, invasionFaction) * _session.Rules.FactionBehaviorRules.DefendedLandingRatio);
                long allocation = Math.Min(remaining, desired);
                result.Add((region, allocation));
                remaining -= allocation;
            }
            foreach (Region region in undefended)
            {
                if (remaining <= 0) break;
                long allocation = Math.Min(remaining, _session.Rules.FactionBehaviorRules.UndefendedLandingBattleValue);
                result.Add((region, allocation));
                remaining -= allocation;
            }

            if (remaining > 0)
            {
                Region primary = defended.FirstOrDefault() ?? undefended.FirstOrDefault();
                if (primary != null)
                {
                    int index = result.FindIndex(item => item.Region == primary);
                    if (index < 0) result.Add((primary, remaining));
                    else result[index] = (primary, result[index].BattleValue + remaining);
                }
            }
            return result;
        }

        private static long DefendingBattleValue(Region region, Faction invasionFaction) =>
            region.RegionFactionMap.Values
                .Where(presence => presence.PlanetFaction.Faction != invasionFaction
                    && FactionRelationshipService.AreHostile(
                        invasionFaction, presence.PlanetFaction.Faction, region.Planet))
                .Sum(StrategicCombatResolver.CalculateDefenderBattleValue);

        private static RegionFaction EstablishFactionPresence(Faction invasionFaction, Region region, long battleValue)
        {
            if (!region.Planet.PlanetFactionMap.TryGetValue(invasionFaction.Id, out PlanetFaction planetFaction))
            {
                planetFaction = new PlanetFaction(invasionFaction) { IsPublic = true };
                region.Planet.PlanetFactionMap[invasionFaction.Id] = planetFaction;
                region.Planet.NotifyPlanetFactionAdded(planetFaction);
            }
            planetFaction.IsPublic = true;
            if (!region.RegionFactionMap.TryGetValue(invasionFaction.Id, out RegionFaction presence))
            {
                presence = new RegionFaction(planetFaction, region)
                {
                    IsPublic = true,
                    GrowthMultiplier = 1.0f
                };
                region.RegionFactionMap[invasionFaction.Id] = presence;
            }
            presence.IsPublic = true;
            presence.AddMilitaryStrength(Math.Max(0, battleValue));
            return presence;
        }

        private static Squad CreateCommandSquad(Faction faction, long invasionForceId, IRNG random)
        {
            SquadTemplate template = faction.SquadTemplates?.Values
                .Where(candidate => candidate.BattleValue > 0
                    && candidate.SquadType.HasFlag(SquadTypes.HQ))
                .OrderByDescending(candidate => candidate.Elements
                    .Count(element => element.SoldierTemplate?.IsSquadLeader == true))
                .ThenByDescending(candidate => candidate.BattleValue)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"Faction '{faction.Name}' has no HQ squad template with a command role.");
            }
            return SquadFactory.GenerateSquad(template, random, name: $"Invasion force {invasionForceId} command");
        }

        private void RegisterCommandUnit(Sector sector, Faction faction, Squad commandSquad, long invasionForceId)
        {
            UnitTemplate template = faction.UnitTemplates?.Values.FirstOrDefault(candidate =>
                candidate.HQSquad == commandSquad.SquadTemplate)
                ?? _session.Rules.StrategicCommandUnitTemplate
                ?? faction.UnitTemplates?.Values.FirstOrDefault();
            if (template == null) return;

            int nextUnitId = _session.Rules.Factions
                .SelectMany(candidate => candidate.Units ?? [])
                .SelectMany(FlattenUnits)
                .Select(unit => unit.Id)
                .DefaultIfEmpty(0)
                .Max() + 1;
            Unit unit = new(nextUnitId, $"Invasion force {invasionForceId} warband", template, [commandSquad]);
            // The list-taking Unit constructor intentionally mirrors the load path and does not
            // infer parent links. The command squad must nevertheless be rooted here so it is
            // included by the normal recursive save and the strategic-invasion-force row can reference its unit.
            commandSquad.ParentUnit = unit;
            faction.Units.Add(unit);
        }

        private static IEnumerable<Unit> FlattenUnits(Unit unit)
        {
            if (unit == null) yield break;
            yield return unit;
            foreach (Unit child in unit.ChildUnits?.SelectMany(FlattenUnits) ?? [])
            {
                yield return child;
            }
        }

        private static IEnumerable<RegionFaction> AllCapabilityPresences(Sector sector, Faction invasionFaction) =>
            sector.Planets.Values.SelectMany(planet => planet.Regions)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Where(presence => presence.PlanetFaction.Faction == invasionFaction);

        private static long CommandBattleValue(Squad commandSquad) => commandSquad?.Members
            .Sum(member => (long)(member.Template?.BattleValue ?? 0)) ?? 0L;

        private static StrategicInvasionForce ResolveInvasionForceForOrder(Sector sector, Order order)
        {
            if (sector == null || order == null) return null;
            if (order.StrategicInvasionForceId is long id)
            {
                return sector.StrategicInvasionForces.FirstOrDefault(invasionForce => invasionForce.IsActive && invasionForce.Id == id);
            }

            List<StrategicInvasionForce> matches = sector.StrategicInvasionForces
                .Where(invasionForce => invasionForce.IsActive && invasionForce.Faction == order.OwnerFaction
                    && order.AssignedSquads.Contains(invasionForce.CommandSquad))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static StrategicInvasionForce ResolveInvasionForceForResult(
            Sector sector,
            StrategicCombatResult result,
            Faction faction)
        {
            if (sector == null || result == null || faction == null) return null;
            if (result.OriginatingStrategicInvasionForceId is long id)
            {
                return sector.StrategicInvasionForces.FirstOrDefault(invasionForce =>
                    invasionForce.IsActive && invasionForce.Id == id && invasionForce.Faction == faction);
            }

            // Legacy/direct result callers carry no identity. Refuse to guess when more than one
            // strategic invasion force could own the battle; this is the important distinction from first-active.
            List<StrategicInvasionForce> candidates = sector.StrategicInvasionForces
                .Where(invasionForce => invasionForce.IsActive
                    && invasionForce.Faction == faction
                    && invasionForce.CurrentRegion == result.Target?.Region)
                .ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private void ProcessInvasionTransit(Sector sector)
        {
            foreach (StrategicInvasionForce invasionForce in sector.StrategicInvasionForces.Where(item =>
                item.IsActive && item.IsInTransit && item.TravelWeeksRemaining > 0).ToList())
            {
                invasionForce.TravelWeeksRemaining--;
                if (invasionForce.TravelWeeksRemaining <= 0)
                {
                    Planet destination = invasionForce.DestinationPlanet;
                    long available = invasionForce.TransitBattleValue + CommandBattleValue(invasionForce.CommandSquad);
                    List<(Region Region, long BattleValue)> allocations = destination == null
                        ? []
                        : AllocateLanding(destination, invasionForce.Faction, available);
                    Region primary = allocations.FirstOrDefault(item => item.BattleValue > 0).Region
                        ?? destination?.Regions.OrderByDescending(region => region.Population)
                            .ThenBy(region => region.Id).FirstOrDefault();
                    if (primary != null && available > 0)
                    {
                        foreach ((Region Region, long BattleValue) allocation in allocations
                            .Where(item => item.BattleValue > 0))
                        {
                            RegionFaction presence = EstablishFactionPresence(
                                invasionForce.Faction,
                                allocation.Region,
                                allocation.BattleValue);
                            presence.StrategicInvasionForceId = invasionForce.Id;
                            presence.DormantConsolidation = 1.0;
                            invasionForce.TrackRegion(presence);
                        }
                        invasionForce.CurrentRegion = primary;
                    }
                    invasionForce.TransitBattleValue = 0;
                    invasionForce.DestinationPlanet = null;
                }
            }
        }
    }
}

