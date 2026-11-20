using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Medical.Readiness;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Mandates
{
    /// <summary>One mandate the chooser offers (PRD §4.25).</summary>
    public sealed record MandateOption(
        string Key,
        MandateFamily Family,
        string Title,
        string Goal,
        string SubjectName,
        int DeadlineWeeks,
        int LegacyValue,
        IReadOnlyList<int> PlanetIds,
        int? UnitId = null,
        int? FactionId = null,
        long? InvasionForceId = null,
        MandateThreatKind ThreatKind = MandateThreatKind.None);

    /// <summary>A named public threat an Eradicate mandate can name, judged by public presence only.</summary>
    public sealed record NamedThreat(
        MandateThreatKind Kind,
        string Key,
        string Name,
        int FactionId,
        long? InvasionForceId,
        int? PlanetId);

    /// <summary>
    /// Lists every eligible mandate, with no random subset or cap (TDD §6.15 *Rejected alternatives*). Eligibility is evaluated when the chooser opens, and a
    /// mandate is offered only when it is meaningful in the current sector.
    /// </summary>
    public static class MandateCatalog
    {
        public static IReadOnlyList<MandateOption> ListEligible(Sector sector, GameRulesData rules)
        {
            if (sector?.PlayerForce == null) return [];
            List<MandateOption> options = [];
            AddChapterWorld(options, sector);
            AddRebuild(options, sector, rules);
            AddProtect(options, sector);
            AddEradicate(options, sector);
            AddReputation(options, sector);
            AddSupply(options, sector);
            return options;
        }

        private static void AddChapterWorld(List<MandateOption> options, Sector sector)
        {
            PlayerForce force = sector.PlayerForce;
            if (force.HomeWorldPlanetId is int homeId)
            {
                Planet home = sector.Planets.GetValueOrDefault(homeId);
                if (home != null && ChapterWorldTests.HasPublicHostilePresence(home))
                {
                    options.Add(Option(
                        $"secure/{home.Id}",
                        MandateFamily.SecureChapterWorld,
                        $"Secure {home.Name}, the Chapter World",
                        $"Keep every enemy from holding ground openly on {home.Name} for "
                            + $"{MandateConstants.SecureCleanWeeksRequired} weeks in a row. "
                            + "Renouncing the world breaks the vow.",
                        home.Name,
                        [home.Id]));
                }
                return;
            }

            foreach (Planet planet in sector.Planets.Values
                .Where(planet => ChapterWorldTests.LoyalistsHoldGround(planet)
                    && ChapterWorldTests.HasPublicHostilePresence(planet))
                .OrderBy(planet => planet.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(planet => planet.Id))
            {
                options.Add(Option(
                    $"establish/{planet.Id}",
                    MandateFamily.EstablishChapterWorld,
                    $"Liberate {planet.Name} as the Chapter World",
                    $"Drive every enemy from open ground on {planet.Name} while Imperial forces "
                        + "still hold some of it. The world becomes the Chapter World, and the Sector "
                        + "Lord's opinion rises. If no Imperial force holds ground there, the vow fails "
                        + "and the Sector Lord's opinion falls.",
                    planet.Name,
                    [planet.Id]));
            }
        }

        private static void AddRebuild(List<MandateOption> options, Sector sector, GameRulesData rules)
        {
            PlayerForce force = sector.PlayerForce;
            ChapterMandates mandates = force.Mandates;
            foreach (Unit company in Companies(force, rules))
            {
                CompanyStrength strength = CompanyStrength.Measure(company, force);
                if (!mandates.WasCompanyRaised(company.Id)
                    || strength.Fraction >= MandateConstants.RebuildEligibleBelowFraction)
                {
                    continue;
                }
                options.Add(Option(
                    $"rebuild/{company.Id}",
                    MandateFamily.RebuildCompany,
                    $"Rebuild the {company.Name}",
                    $"Bring the {company.Name} back to {MandateConstants.RebuildFulfilledAtFraction:P0} "
                        + $"duty-ready strength. It now stands at {strength.DutyReady} of {strength.Full}.",
                    company.Name,
                    [],
                    unitId: company.Id));
            }
        }

        private static void AddProtect(List<MandateOption> options, Sector sector)
        {
            List<Planet> threatened = StrategicWorlds(sector)
                .Where(ChapterWorldTests.HasPublicHostilePresence)
                .ToList();
            if (threatened.Count < MandateConstants.ProtectMinimumThreatenedWorlds) return;
            List<Planet> named = threatened.Take(MandateConstants.ProtectMaximumNamedWorlds).ToList();
            string names = JoinNames(named.Select(planet => planet.Name).ToList());
            options.Add(Option(
                $"protect/{string.Join("-", named.Select(planet => planet.Id))}",
                MandateFamily.ProtectStrategicWorlds,
                $"Protect {names}",
                $"Keep {names} in Imperial hands until the deadline. If any of them falls, the vow fails.",
                names,
                named.Select(planet => planet.Id).ToList()));
        }

        private static void AddEradicate(List<MandateOption> options, Sector sector)
        {
            foreach (NamedThreat threat in NamedThreats.FindPublic(sector))
            {
                options.Add(Option(
                    $"eradicate/{threat.Key}",
                    MandateFamily.EradicateThreat,
                    $"Eradicate {threat.Name}",
                    threat.Kind == MandateThreatKind.InvasionForce
                        ? $"Leave {threat.Name} without open ground on any world."
                        : $"Leave {threat.Name} without open ground. A remnant in hiding does not count.",
                    threat.Name,
                    threat.PlanetId is int planetId ? [planetId] : [],
                    factionId: threat.FactionId,
                    invasionForceId: threat.InvasionForceId,
                    threatKind: threat.Kind));
            }
        }

        private static void AddReputation(List<MandateOption> options, Sector sector)
        {
            Character lord = sector.GetSectorLord();
            if (lord == null || lord.OpinionOfPlayerForce >= MandateConstants.ReputationWarmOpinion) return;
            Planet seat = sector.GetSectorCapital();
            options.Add(Option(
                $"reputation/{lord.Id}",
                MandateFamily.Reputation,
                "Win the favour of the Sector Lord",
                $"Raise the Sector Lord's opinion of the Chapter to warm. {lord.Name} now holds the "
                    + "seat; if the seat changes hands, the new Sector Lord's opinion counts.",
                lord.Name,
                seat == null ? [] : [seat.Id]));
        }

        private static void AddSupply(List<MandateOption> options, Sector sector)
        {
            options.Add(Option(
                "supply",
                MandateFamily.SupplyNetwork,
                "Build a supply network",
                $"Gain {MandateConstants.SupplyPledgesRequired} new pledges, one-off or standing, "
                    + "from three different worlds.",
                "the Chapter's supply",
                []));
        }

        private static MandateOption Option(
            string key,
            MandateFamily family,
            string title,
            string goal,
            string subject,
            IReadOnlyList<int> planetIds,
            int? unitId = null,
            int? factionId = null,
            long? invasionForceId = null,
            MandateThreatKind threatKind = MandateThreatKind.None) =>
            new(key, family, title, goal, subject,
                MandateConstants.DeadlineWeeks(family),
                MandateConstants.LegacyValue(family),
                planetIds, unitId, factionId, invasionForceId, threatKind);

        /// <summary>
        /// The companies a Rebuild mandate can name: every company except the Scout Company.
        /// </summary>
        public static IEnumerable<Unit> Companies(PlayerForce force, GameRulesData rules)
        {
            UnitTemplate scoutCompany = rules?.ChapterDoctrine?.ScoutCompany;
            return (force?.Army?.OrderOfBattle?.ChildUnits ?? [])
                .Where(unit => unit != null && unit.UnitTemplate != scoutCompany)
                .OrderBy(unit => unit.Id);
        }

        /// <summary>
        /// The sector's most important Imperial worlds: the governance seats, in the same order
        /// SectorTopologyBuilder.AssignGovernance ranks them (Importance, Population, Id).
        /// </summary>
        public static IEnumerable<Planet> StrategicWorlds(Sector sector) =>
            sector.Planets.Values
                .Where(planet => planet.GovernanceTier != GovernanceTier.Planetary)
                .OrderByDescending(planet => planet.Importance)
                .ThenByDescending(planet => planet.Population)
                .ThenBy(planet => planet.Id);

        internal static string JoinNames(IReadOnlyList<string> names) => names.Count switch
        {
            0 => "no worlds",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
        };
    }

    /// <summary>A company's duty-ready strength against its full establishment.</summary>
    public sealed record CompanyStrength(int DutyReady, int Full)
    {
        public double Fraction => Full <= 0 ? 0 : (double)DutyReady / Full;

        public static CompanyStrength Measure(Unit company, PlayerForce force)
        {
            if (company == null) return new CompanyStrength(0, 0);
            UnitTemplate template = company.UnitTemplate;
            int full = (template?.HQSquad?.Elements?.Sum(element => element.MaximumNumber) ?? 0)
                + (template?.GetChildSquadSlots().Sum(slot =>
                    (slot.Template?.Elements?.Sum(element => element.MaximumNumber) ?? 0) * slot.MaxCount) ?? 0);
            int dutyReady = company.Squads.Sum(squad => SquadStrengthSnapshotBuilder.Build(
                squad,
                force?.RecruitmentProgram,
                force?.Army?.ChapterOperationalDoctrine).DutyReady);
            // A company can hold more than its template if soldiers were transferred in.
            return new CompanyStrength(dutyReady, Math.Max(full, company.Squads.Sum(squad => squad.Members.Count)));
        }
    }

    /// <summary>
    /// Finds the named public threats in the sector. An invasion force (a Waaagh!) is one threat
    /// however many worlds it holds; any other hostile faction is one threat per world. Ground
    /// held by an invasion force is not counted again as its faction's presence on that world.
    /// </summary>
    public static class NamedThreats
    {
        public static IReadOnlyList<NamedThreat> FindPublic(Sector sector)
        {
            List<NamedThreat> threats = [];
            foreach (StrategicInvasionForce force in sector.StrategicInvasionForces
                .Where(force => force.IsActive)
                .OrderBy(force => force.Id))
            {
                Planet planet = PublicInvasionRegions(sector, force.Id)
                    .Select(region => region.Region?.Planet)
                    .FirstOrDefault(world => world != null);
                if (planet == null) continue;
                threats.Add(new NamedThreat(
                    MandateThreatKind.InvasionForce,
                    $"force/{force.Id}",
                    InvasionForceName(force),
                    force.Faction.Id,
                    force.Id,
                    planet.Id));
            }

            foreach (Planet planet in sector.Planets.Values.OrderBy(planet => planet.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(planet => planet.Id))
            {
                foreach (Faction faction in planet.Regions
                    .Where(region => region != null)
                    .SelectMany(region => region.RegionFactionMap.Values)
                    .Where(IsPublicHostileGround)
                    .Where(regionFaction => regionFaction.StrategicInvasionForceId == null)
                    .Select(regionFaction => regionFaction.PlanetFaction.Faction)
                    .Distinct()
                    .OrderBy(faction => faction.Id))
                {
                    threats.Add(new NamedThreat(
                        MandateThreatKind.FactionOnWorld,
                        $"faction/{faction.Id}/{planet.Id}",
                        $"the {faction.Name} on {planet.Name}",
                        faction.Id,
                        null,
                        planet.Id));
                }
            }
            return threats;
        }

        public static bool InvasionForceHasPublicGround(Sector sector, long invasionForceId) =>
            PublicInvasionRegions(sector, invasionForceId).Any();

        public static bool FactionHasPublicGround(Planet planet, int factionId) =>
            planet != null && planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Any(regionFaction => IsPublicHostileGround(regionFaction)
                    && regionFaction.StrategicInvasionForceId == null
                    && regionFaction.PlanetFaction.Faction.Id == factionId);

        public static string InvasionForceName(StrategicInvasionForce force)
        {
            string commander = force?.StrategicCommander?.Name;
            return string.IsNullOrWhiteSpace(commander)
                ? $"the {force?.Faction?.Name ?? "enemy"} host"
                : $"the {force.Faction.Name} host of {commander}";
        }

        private static IEnumerable<RegionFaction> PublicInvasionRegions(Sector sector, long invasionForceId) =>
            sector.Planets.Values
                .SelectMany(planet => planet.Regions)
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Where(regionFaction => regionFaction.StrategicInvasionForceId == invasionForceId
                    && IsPublicHostileGround(regionFaction));

        private static bool IsPublicHostileGround(RegionFaction regionFaction) =>
            regionFaction.IsPublic
            && (regionFaction.Population > 0 || regionFaction.Garrison > 0)
            && regionFaction.PlanetFaction?.Faction != null
            && !FactionRelationshipService.IsImperial(regionFaction.PlanetFaction.Faction);
    }
}
