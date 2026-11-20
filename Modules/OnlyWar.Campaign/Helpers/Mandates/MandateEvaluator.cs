using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Units;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Mandates
{
    public enum MandateOutcome
    {
        Ongoing = 0,
        Fulfilled = 1,
        Failed = 2
    }

    /// <summary>Where a mandate stands. Progress is player-facing; Reason explains a result.</summary>
    public sealed record MandateEvaluation(MandateOutcome Outcome, string Progress, string Reason = null)
    {
        public static MandateEvaluation Ongoing(string progress) => new(MandateOutcome.Ongoing, progress);
        public static MandateEvaluation Fulfilled(string progress, string reason) =>
            new(MandateOutcome.Fulfilled, progress, reason);
        public static MandateEvaluation Failed(string progress, string reason) =>
            new(MandateOutcome.Failed, progress, reason);
    }

    /// <summary>
    /// Judges one mandate against the sector (PRD §4.25, TDD §6.15 *Family rules*). The goal is
    /// checked first, then the family's fail condition, then the deadline. With advance set, the
    /// call also moves the mandate's own counters forward one week; the Brief calls it without.
    /// The Promised World is never judged here: the scenario processor owns its result.
    /// </summary>
    public static class MandateEvaluator
    {
        public static MandateEvaluation Evaluate(
            Mandate mandate,
            Sector sector,
            GameRulesData rules,
            int currentWeek,
            bool advance)
        {
            if (mandate.IsPromisedWorld)
            {
                Planet promised = sector.Planets.GetValueOrDefault(mandate.PlanetIds.FirstOrDefault());
                return MandateEvaluation.Ongoing(promised == null
                    ? "The Promised World is unavailable."
                    : $"{HostileRegions(promised)} of {promised.Regions.Length} regions of {promised.Name} "
                        + "are still held openly by the enemy.");
            }

            MandateEvaluation result = mandate.Family switch
            {
                MandateFamily.EstablishChapterWorld => Establish(mandate, sector),
                MandateFamily.SecureChapterWorld => Secure(mandate, sector, advance),
                MandateFamily.RebuildCompany => Rebuild(mandate, sector),
                MandateFamily.ProtectStrategicWorlds => Protect(mandate, sector, currentWeek),
                MandateFamily.EradicateThreat => Eradicate(mandate, sector),
                MandateFamily.Reputation => Reputation(sector),
                MandateFamily.SupplyNetwork => Supply(mandate, sector),
                _ => MandateEvaluation.Ongoing(string.Empty)
            };

            if (result.Outcome == MandateOutcome.Ongoing
                && mandate.DeadlineWeek is int deadline
                && currentWeek >= deadline)
            {
                return MandateEvaluation.Failed(result.Progress, "The deadline passed before the vow was kept.");
            }
            return result;
        }

        private static MandateEvaluation Establish(Mandate mandate, Sector sector)
        {
            Planet world = sector.Planets.GetValueOrDefault(mandate.PlanetIds.FirstOrDefault());
            if (world == null) return MandateEvaluation.Failed("The world is gone.", "The world is gone.");
            string progress = $"{HostileRegions(world)} of {world.Regions.Length} regions of {world.Name} "
                + "are still held openly by the enemy.";
            if (ChapterWorldTests.IsLiberated(world))
            {
                return MandateEvaluation.Fulfilled(progress,
                    $"No enemy holds open ground on {world.Name}; it is now the Chapter World.");
            }
            if (!ChapterWorldTests.LoyalistsHoldGround(world))
            {
                return MandateEvaluation.Failed(progress,
                    $"No Imperial force holds ground on {world.Name} any longer.");
            }
            return MandateEvaluation.Ongoing(progress);
        }

        private static MandateEvaluation Secure(Mandate mandate, Sector sector, bool advance)
        {
            int worldId = mandate.PlanetIds.FirstOrDefault();
            Planet world = sector.Planets.GetValueOrDefault(worldId);
            if (world == null || sector.PlayerForce.HomeWorldPlanetId != worldId)
            {
                return MandateEvaluation.Failed("The Chapter World was renounced.",
                    "The Chapter renounced the world it had sworn to secure.");
            }
            bool hostile = ChapterWorldTests.HasPublicHostilePresence(world);
            if (advance)
            {
                mandate.CleanWeeks = hostile ? 0 : mandate.CleanWeeks + 1;
            }
            string progress = hostile
                ? $"The enemy still holds {HostileRegions(world)} regions of {world.Name} openly."
                : $"{world.Name} has been clear of open enemy ground for {mandate.CleanWeeks} of "
                    + $"{MandateConstants.SecureCleanWeeksRequired} weeks.";
            return mandate.CleanWeeks >= MandateConstants.SecureCleanWeeksRequired
                ? MandateEvaluation.Fulfilled(progress,
                    $"{world.Name} has been clear of open enemy ground for half a year.")
                : MandateEvaluation.Ongoing(progress);
        }

        private static MandateEvaluation Rebuild(Mandate mandate, Sector sector)
        {
            Unit company = sector.PlayerForce.Army?.OrderOfBattle?.ChildUnits?
                .FirstOrDefault(unit => unit.Id == mandate.UnitId);
            if (company == null)
            {
                return MandateEvaluation.Failed("The company no longer exists.",
                    $"The {mandate.SubjectName} was dissolved.");
            }
            CompanyStrength strength = CompanyStrength.Measure(company, sector.PlayerForce);
            string progress = $"The {company.Name} stands at {strength.DutyReady} of {strength.Full} "
                + $"duty-ready ({strength.Fraction:P0}); the goal is "
                + $"{MandateConstants.RebuildFulfilledAtFraction:P0}.";
            return strength.Fraction >= MandateConstants.RebuildFulfilledAtFraction
                ? MandateEvaluation.Fulfilled(progress, $"The {company.Name} is at strength again.")
                : MandateEvaluation.Ongoing(progress);
        }

        private static MandateEvaluation Protect(Mandate mandate, Sector sector, int currentWeek)
        {
            List<Planet> worlds = mandate.PlanetIds
                .Select(id => sector.Planets.GetValueOrDefault(id))
                .ToList();
            Planet lost = worlds.FirstOrDefault(world => world == null || !ChapterWorldTests.LoyalistsHoldGround(world));
            int threatened = worlds.Count(world => world != null && ChapterWorldTests.HasPublicHostilePresence(world));
            string progress = $"{worlds.Count} worlds named; {threatened} still have open enemy ground.";
            if (lost != null)
            {
                return MandateEvaluation.Failed(progress,
                    $"{lost?.Name ?? "A named world"} fell from Imperial hands.");
            }
            return mandate.DeadlineWeek is int deadline && currentWeek >= deadline
                ? MandateEvaluation.Fulfilled(progress, "Every named world was still in Imperial hands at the deadline.")
                : MandateEvaluation.Ongoing(progress);
        }

        private static MandateEvaluation Eradicate(Mandate mandate, Sector sector)
        {
            if (mandate.ThreatKind == MandateThreatKind.InvasionForce)
            {
                StrategicInvasionForce force = sector.StrategicInvasionForces
                    .FirstOrDefault(candidate => candidate.Id == mandate.InvasionForceId);
                bool gone = force == null
                    || !force.IsActive
                    || !NamedThreats.InvasionForceHasPublicGround(sector, force.Id);
                return gone
                    ? MandateEvaluation.Fulfilled($"{Capitalize(mandate.SubjectName)} holds no open ground.",
                        $"{Capitalize(mandate.SubjectName)} holds open ground on no world.")
                    : MandateEvaluation.Ongoing($"{Capitalize(mandate.SubjectName)} still holds open ground.");
            }

            Planet world = sector.Planets.GetValueOrDefault(mandate.PlanetIds.FirstOrDefault());
            bool present = mandate.FactionId is int factionId && NamedThreats.FactionHasPublicGround(world, factionId);
            return present
                ? MandateEvaluation.Ongoing($"{Capitalize(mandate.SubjectName)} still hold open ground.")
                : MandateEvaluation.Fulfilled($"{Capitalize(mandate.SubjectName)} hold no open ground.",
                    $"{Capitalize(mandate.SubjectName)} hold no open ground.");
        }

        private static MandateEvaluation Reputation(Sector sector)
        {
            Character lord = sector.GetSectorLord();
            if (lord == null) return MandateEvaluation.Ongoing("The seat of Sector Lord is empty.");
            string progress = $"{lord.Name}'s opinion of the Chapter is {Describe(lord.OpinionOfPlayerForce)}.";
            return lord.OpinionOfPlayerForce >= MandateConstants.ReputationWarmOpinion
                ? MandateEvaluation.Fulfilled(progress, $"{lord.Name} now holds the Chapter in warm regard.")
                : MandateEvaluation.Ongoing(progress);
        }

        private static MandateEvaluation Supply(Mandate mandate, Sector sector)
        {
            int worlds = sector.PlayerForce.Pledges
                .Where(pledge => pledge.Id > mandate.BaselinePledgeId)
                .Select(pledge => pledge.SourcePlanetId)
                .Distinct()
                .Count();
            string progress = $"New pledges from {worlds} of {MandateConstants.SupplyPledgesRequired} worlds.";
            return worlds >= MandateConstants.SupplyPledgesRequired
                ? MandateEvaluation.Fulfilled(progress,
                    $"Pledges from {worlds} worlds now supply the Chapter.")
                : MandateEvaluation.Ongoing(progress);
        }

        internal static int HostileRegions(Planet planet) =>
            planet.Regions.Count(region => region != null && region.RegionFactionMap.Values.Any(regionFaction =>
                regionFaction.IsPublic
                && regionFaction.PlanetFaction?.Faction != null
                && !FactionRelationshipService.IsImperial(regionFaction.PlanetFaction.Faction)
                && (regionFaction.Population > 0 || regionFaction.Garrison > 0)));

        // The petition narrator treats 0.7 as warm and 0.3 as cold.
        private static string Describe(float opinion) => opinion switch
        {
            >= MandateConstants.ReputationWarmOpinion => "warm",
            <= 0.3f => "cold",
            _ => "neutral"
        };

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
