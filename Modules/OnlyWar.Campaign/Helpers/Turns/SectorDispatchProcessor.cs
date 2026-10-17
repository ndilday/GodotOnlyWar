using OnlyWar.Campaign.Events;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// The public sector state at the start of a turn that the end-of-turn comparison needs and
    /// the persisted tracker does not hold.
    /// </summary>
    internal sealed record SectorDispatchSnapshot(
        IReadOnlySet<(int PlanetId, int FactionId)> PublicSwarms,
        IReadOnlySet<int> PublicInvasionCommandSquadIds);

    /// <summary>
    /// Detects the sector dispatch triggers (OnlyWar_TDD.md §6.1) and records one
    /// <see cref="CampaignEventType.SectorDispatch"/> event for each. It reads only public state, so
    /// a dispatch never discloses what the Chapter could not know.
    /// </summary>
    /// <remarks>
    /// Danger episodes, revolts and the Sector Lord need memory across turns and saves, so they use
    /// the persisted <see cref="SectorDispatchTracker"/>. Swarm arrivals and new invasion forces
    /// compare against a snapshot taken at the start of the turn, as the hidden-cult check does.
    /// </remarks>
    internal sealed class SectorDispatchProcessor
    {
        private readonly Sector _sector;
        private readonly Faction _imperialFaction;
        private readonly Faction _insurrectionists;
        private readonly IReadOnlyList<Faction> _factions;

        internal SectorDispatchProcessor(
            Sector sector,
            Faction imperialFaction,
            Faction insurrectionists,
            IEnumerable<Faction> factions)
        {
            _sector = sector ?? throw new ArgumentNullException(nameof(sector));
            _imperialFaction = imperialFaction ?? throw new ArgumentNullException(nameof(imperialFaction));
            _insurrectionists = insurrectionists;
            _factions = (factions ?? []).ToList();
        }

        /// <summary>
        /// Seeds the tracker with any world or Sector Lord it has not seen, without reporting it,
        /// and captures the start-of-turn snapshot.
        /// </summary>
        internal SectorDispatchSnapshot BeginTurn()
        {
            PlayerForce force = _sector.PlayerForce;
            if (force != null)
            {
                foreach (Planet planet in _sector.Planets.Values)
                {
                    (int hostile, int total) = CountHostileRegions(planet);
                    force.SectorDispatches.Seed(
                        planet.Id, hostile, total, IsImperialControlled(planet), HasPublicRevolt(planet));
                }
                (Character lord, Planet capital) = GetImperialSectorLord();
                force.SectorDispatches.SeedSectorLord(lord?.Id, capital?.Id);
            }

            return new SectorDispatchSnapshot(
                PublicSwarms().ToHashSet(),
                PubliclyKnownInvasionForces()
                    .Select(invasionForce => invasionForce.CommandSquad.Id)
                    .ToHashSet());
        }

        /// <summary>
        /// Compares the settled end-of-turn state with the start of the turn and records every
        /// dispatch it finds (no cap, no summary).
        /// </summary>
        /// <param name="chapterPlanetIds">Worlds where Chapter forces had orders or fought this week.</param>
        internal IReadOnlyList<CampaignEvent> EndTurn(
            SectorDispatchSnapshot start,
            IReadOnlySet<int> chapterPlanetIds,
            int week)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            PlayerForce force = _sector.PlayerForce;
            if (force == null) return [];
            chapterPlanetIds ??= new HashSet<int>();
            CampaignEventRecorder recorder = force.GetCampaignEventRecorder();
            List<CampaignEvent> dispatches = [];

            RecordWorldControlDispatches(force, recorder, dispatches, week);
            RecordPlanetDispatches(force, recorder, dispatches, chapterPlanetIds, week);
            RecordSwarmDispatches(start, recorder, dispatches, chapterPlanetIds, week);
            RecordInvasionForceDispatches(start, recorder, dispatches, chapterPlanetIds, week);
            RecordSectorLordDispatch(force, recorder, dispatches, week);
            return dispatches;
        }

        private void RecordWorldControlDispatches(
            PlayerForce force,
            CampaignEventRecorder recorder,
            List<CampaignEvent> dispatches,
            int week)
        {
            List<CampaignEvent> worldEvents = force.CurrentTurnEvents
                .Where(@event => @event.Type is CampaignEventType.WorldSaved or CampaignEventType.WorldLost)
                .ToList();
            foreach (CampaignEvent source in worldEvents)
            {
                if (source.Payload is not WorldControlChangedPayload world) continue;
                bool lost = source.Type == CampaignEventType.WorldLost;
                // A world saved with the Chapter is the Chapter's own report, not a dispatch.
                if (!lost && world.ChapterParticipated) continue;

                Planet planet = _sector.Planets.GetValueOrDefault(world.PlanetId);
                Faction controller = _factions.FirstOrDefault(faction => faction.Id == world.CurrentControllingFactionId);
                (SectorDispatchChapterPart part, Subsector subsector, IReadOnlyList<string> present) =
                    ResolveChapterPart(planet, world.ChapterParticipated);
                SectorDispatchPayload payload = new(
                    lost ? SectorDispatchKind.WorldLost : SectorDispatchKind.WorldSavedWithoutChapter,
                    world.PlanetId,
                    world.PlanetName,
                    subsector?.Id,
                    subsector?.Name,
                    lost ? controller?.Id : null,
                    lost ? controller?.Name : null,
                    part,
                    present,
                    SourceEventId: source.Id,
                    IsSecession: lost && controller != null && controller.Id == _insurrectionists?.Id);
                Record(recorder, dispatches,
                    payload, week, $"dispatch/world-control/{source.Id}");
            }
        }

        private void RecordPlanetDispatches(
            PlayerForce force,
            CampaignEventRecorder recorder,
            List<CampaignEvent> dispatches,
            IReadOnlySet<int> chapterPlanetIds,
            int week)
        {
            foreach (Planet planet in _sector.Planets.Values.OrderBy(planet => planet.Id))
            {
                (int hostile, int total) = CountHostileRegions(planet);
                bool isImperial = IsImperialControlled(planet);
                bool tookPart = chapterPlanetIds.Contains(planet.Id);
                SectorDispatchPlanetChange change = force.SectorDispatches.Observe(
                    planet.Id, hostile, total, isImperial, HasPublicRevolt(planet), tookPart);

                if (change.DangerOpened)
                {
                    (SectorDispatchChapterPart part, Subsector subsector, IReadOnlyList<string> present) =
                        ResolveChapterPart(planet, tookPart);
                    Record(recorder, dispatches,
                        new SectorDispatchPayload(
                            SectorDispatchKind.WorldInDanger,
                            planet.Id,
                            planet.Name,
                            subsector?.Id,
                            subsector?.Name,
                            null,
                            null,
                            part,
                            present,
                            HostileRegionCount: hostile,
                            RegionCount: total),
                        week,
                        $"dispatch/world-in-danger/{planet.Id}/{week}");
                }

                // A revolt that ends on a world the Imperium still holds was put down. One that
                // ends because the world fell is reported as the lost world.
                if (change.RevoltEnded && isImperial)
                {
                    (SectorDispatchChapterPart part, Subsector subsector, IReadOnlyList<string> present) =
                        ResolveChapterPart(planet, change.RevoltChapterParticipated);
                    Record(recorder, dispatches,
                        new SectorDispatchPayload(
                            SectorDispatchKind.RevoltCrushed,
                            planet.Id,
                            planet.Name,
                            subsector?.Id,
                            subsector?.Name,
                            _insurrectionists?.Id,
                            _insurrectionists?.Name,
                            part,
                            present),
                        week,
                        $"dispatch/revolt-crushed/{planet.Id}/{week}");
                }
            }
        }

        private void RecordSwarmDispatches(
            SectorDispatchSnapshot start,
            CampaignEventRecorder recorder,
            List<CampaignEvent> dispatches,
            IReadOnlySet<int> chapterPlanetIds,
            int week)
        {
            foreach ((int planetId, int factionId) in PublicSwarms()
                .Where(swarm => !start.PublicSwarms.Contains(swarm))
                .OrderBy(swarm => swarm.PlanetId)
                .ThenBy(swarm => swarm.FactionId))
            {
                Planet planet = _sector.Planets[planetId];
                Faction faction = planet.PlanetFactionMap.GetValueOrDefault(factionId)?.Faction
                    ?? _factions.First(item => item.Id == factionId);
                (SectorDispatchChapterPart part, Subsector subsector, IReadOnlyList<string> present) =
                    ResolveChapterPart(planet, chapterPlanetIds.Contains(planetId));
                Record(recorder, dispatches,
                    new SectorDispatchPayload(
                        SectorDispatchKind.SwarmReachedWorld,
                        planet.Id,
                        planet.Name,
                        subsector?.Id,
                        subsector?.Name,
                        faction.Id,
                        faction.Name,
                        part,
                        present),
                    week,
                    $"dispatch/swarm/{planetId}/{factionId}/{week}");
            }
        }

        private void RecordInvasionForceDispatches(
            SectorDispatchSnapshot start,
            CampaignEventRecorder recorder,
            List<CampaignEvent> dispatches,
            IReadOnlySet<int> chapterPlanetIds,
            int week)
        {
            foreach (StrategicInvasionForce invasionForce in PubliclyKnownInvasionForces()
                .Where(item => !start.PublicInvasionCommandSquadIds.Contains(item.CommandSquad.Id))
                .OrderBy(item => item.Id))
            {
                Planet planet = invasionForce.CurrentRegion?.Planet
                    ?? PublicRegions(invasionForce).Select(presence => presence.Region.Planet).First();
                (SectorDispatchChapterPart part, Subsector subsector, IReadOnlyList<string> present) =
                    ResolveChapterPart(planet, chapterPlanetIds.Contains(planet.Id));
                // Keyed by the command squad, not the force id: a merged force keeps the
                // strongest commander, and is the same host the dispatch already named.
                Record(recorder, dispatches,
                    new SectorDispatchPayload(
                        SectorDispatchKind.InvasionForceFormed,
                        planet.Id,
                        planet.Name,
                        subsector?.Id,
                        subsector?.Name,
                        invasionForce.Faction.Id,
                        invasionForce.Faction.Name,
                        part,
                        present),
                    week,
                    $"dispatch/invasion-force/{invasionForce.CommandSquad.Id}");
            }
        }

        private void RecordSectorLordDispatch(
            PlayerForce force,
            CampaignEventRecorder recorder,
            List<CampaignEvent> dispatches,
            int week)
        {
            (Character lord, Planet capital) = GetImperialSectorLord();
            SectorLordState previous = force.SectorDispatches.ObserveSectorLord(lord?.Id, capital?.Id);
            if (previous == null || lord == null) return;

            Character previousLord = previous.CharacterId is int previousId
                ? _sector.Characters.FirstOrDefault(character => character.Id == previousId)
                : null;
            Subsector subsector = FindSubsector(capital);
            Record(recorder, dispatches,
                new SectorDispatchPayload(
                    SectorDispatchKind.NewSectorLord,
                    capital.Id,
                    capital.Name,
                    subsector?.Id,
                    subsector?.Name,
                    null,
                    null,
                    SectorDispatchChapterPart.NotApplicable,
                    PreviousSectorLordId: previousLord?.Id,
                    PreviousSectorLordName: previousLord?.Name,
                    SectorLordId: lord.Id,
                    SectorLordName: lord.Name,
                    CapitalMoved: previous.CapitalPlanetId.HasValue && previous.CapitalPlanetId != capital.Id),
                week,
                $"dispatch/sector-lord/{lord.Id}/{week}");
        }

        /// <summary>
        /// Took part, or present in the same subsector and not committed, or absent. Present
        /// forces are named by their formation, so the dispatch can say which company stood by.
        /// </summary>
        private (SectorDispatchChapterPart Part, Subsector Subsector, IReadOnlyList<string> PresentFormations)
            ResolveChapterPart(Planet planet, bool tookPart)
        {
            Subsector subsector = FindSubsector(planet);
            if (tookPart) return (SectorDispatchChapterPart.TookPart, subsector, []);
            if (subsector == null) return (SectorDispatchChapterPart.Absent, null, []);

            HashSet<int> subsectorPlanetIds = subsector.Planets.Select(item => item.Id).ToHashSet();
            List<string> present = (_sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads() ?? [])
                .Where(squad => squad.Members.Count > 0)
                .Where(squad => LocatedPlanet(squad) is Planet location
                    && subsectorPlanetIds.Contains(location.Id))
                .Select(squad => squad.ParentUnit?.Name ?? squad.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            return present.Count == 0
                ? (SectorDispatchChapterPart.Absent, subsector, [])
                : (SectorDispatchChapterPart.PresentNotEngaged, subsector, present);
        }

        private static Planet LocatedPlanet(Squad squad)
        {
            CampaignLocation location = CampaignLocationService.ForSquad(squad);
            return location?.Region?.Planet ?? location?.Ship?.Fleet?.Planet;
        }

        private Subsector FindSubsector(Planet planet) =>
            planet == null
                ? null
                : _sector.Subsectors.FirstOrDefault(subsector =>
                    subsector.Planets.Any(item => item.Id == planet.Id));

        private (int Hostile, int Total) CountHostileRegions(Planet planet)
        {
            List<Region> regions = planet.Regions.Where(region => region != null).ToList();
            int hostile = regions.Count(region => region.RegionFactionMap.Values.Any(presence =>
                IsPublicPresence(presence)
                && presence.PlanetFaction.Faction.Id != _imperialFaction.Id
                && FactionRelationshipService.AreHostile(_imperialFaction, presence.PlanetFaction.Faction, planet)));
            return (hostile, regions.Count);
        }

        private static bool IsImperialControlled(Planet planet)
        {
            Faction controller = planet.GetControllingFaction();
            return controller != null && FactionRelationshipService.IsImperial(controller);
        }

        private bool HasPublicRevolt(Planet planet) =>
            _insurrectionists != null
            && planet.Regions.Any(region => region?.RegionFactionMap
                .GetValueOrDefault(_insurrectionists.Id) is RegionFaction presence
                && IsPublicPresence(presence));

        private IEnumerable<(int PlanetId, int FactionId)> PublicSwarms() =>
            _sector.Planets.Values.SelectMany(planet => planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Where(presence => IsPublicPresence(presence)
                    && presence.PlanetFaction.Faction.GrowthType == GrowthType.Consumption)
                .Select(presence => (planet.Id, presence.PlanetFaction.Faction.Id)))
                .Distinct();

        private IEnumerable<StrategicInvasionForce> PubliclyKnownInvasionForces() =>
            _sector.StrategicInvasionForces.Where(invasionForce =>
                invasionForce.IsActive && PublicRegions(invasionForce).Any());

        private static IEnumerable<RegionFaction> PublicRegions(StrategicInvasionForce invasionForce) =>
            invasionForce.KnownRegions.Where(presence =>
                presence?.StrategicInvasionForceId == invasionForce.Id
                && presence.Region?.Planet != null
                && IsPublicPresence(presence));

        private (Character Lord, Planet Capital) GetImperialSectorLord()
        {
            Planet capital = _sector.GetSectorCapital();
            return capital != null && IsImperialControlled(capital)
                ? (capital.Governor, capital)
                : (null, capital);
        }

        private static bool IsPublicPresence(RegionFaction presence) =>
            presence?.IsPublic == true && presence.Population > 0;

        private static void Record(
            CampaignEventRecorder recorder,
            List<CampaignEvent> dispatches,
            SectorDispatchPayload payload,
            int week,
            string dedupeKey)
        {
            // An existing key means this dispatch was already given; the recorder would hand the
            // old event back, and it is not news this week.
            if (recorder.Ledger.GetByDedupeKey(dedupeKey) != null) return;
            dispatches.Add(recorder.RecordSectorDispatch(payload, week, dedupeKey));
        }
    }
}
