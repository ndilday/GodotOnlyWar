using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Owns the weekly secular-unrest simulation. Contentment is intentionally internal state:
    /// governors and Chapter intelligence report evidence, while this processor maintains the
    /// underlying allegiance, embedded-PDF, armed-cadre, migration, and reveal state.
    /// </summary>
    internal sealed class CivilUnrestTurnProcessor
    {
        private const double HiddenCorruptionPenaltyPerPopulationShare = 100.0;
        private const double MaximumHiddenCorruptionPenalty = 10.0;
        private const double AdjacentPublicRevoltPenalty = 4.0;
        private const double MaximumAdjacentRevoltPenalty = 8.0;
        private const double FalseCrackdownWeeklyChanceScale = 0.10;
        private const double CrackdownArmedSuppressionPerPdf = 0.01;

        private readonly GameSession _session;

        internal CivilUnrestTurnProcessor(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal void ProcessPlanet(Planet planet)
        {
            if (planet == null) return;
            Faction defaultFaction = _session.Rules.DefaultFaction;
            Faction unrestFaction = _session.Rules.SectorFactions.Insurrectionists;
            if (!planet.PlanetFactionMap.TryGetValue(defaultFaction.Id, out PlanetFaction imperialPlanetFaction))
            {
                return;
            }

            PlanetFaction unrestPlanetFaction = planet.PlanetFactionMap.TryGetValue(
                unrestFaction.Id, out PlanetFaction existingUnrestPlanetFaction)
                    ? existingUnrestPlanetFaction
                    : null;

            foreach (Region region in planet.Regions.Where(region => region != null))
            {
                if (!region.RegionFactionMap.TryGetValue(defaultFaction.Id, out RegionFaction imperial)
                    || !imperial.IsPublic || imperial.Population <= 0)
                {
                    continue;
                }

                RegionFaction unrest = unrestPlanetFaction != null
                    && region.RegionFactionMap.TryGetValue(unrestFaction.Id, out RegionFaction presence)
                        ? presence
                        : null;

                UpdateContentment(planet, region, imperial, unrest, imperialPlanetFaction.Leader);
                long desiredUnrestPopulation = CalculateDesiredUnrestPopulation(imperial, unrest);
                long currentUnrestPopulation = unrest?.Population ?? 0;

                if (desiredUnrestPopulation > currentUnrestPopulation)
                {
                    unrestPlanetFaction ??= EnsurePlanetFaction(planet, unrestFaction);
                    unrest ??= EnsureRegionFaction(region, unrestPlanetFaction);
                    RecruitFromImperialPopulation(
                        imperial, unrest, Math.Min(imperial.Population, desiredUnrestPopulation - currentUnrestPopulation));
                }
                else if (unrest != null && desiredUnrestPopulation < currentUnrestPopulation)
                {
                    ReturnToImperialPopulation(
                        imperial, unrest, Math.Min(unrest.Population, currentUnrestPopulation - desiredUnrestPopulation));
                }

                if (unrest != null)
                {
                    UpdateArmedCivilianCadres(unrest, imperial.Contentment);
                    ApplyGovernorResponse(imperial, unrest, imperialPlanetFaction.Leader);
                }
            }

            if (unrestPlanetFaction == null) return;

            MigrateHiddenSupportersTowardPublicRevolt(planet, unrestPlanetFaction);
            ResolveRegionalVisibility(planet, unrestPlanetFaction);
            RemoveEmptyUnrestPresences(planet, unrestPlanetFaction);
        }

        private void UpdateContentment(
            Planet planet,
            Region region,
            RegionFaction imperial,
            RegionFaction unrest,
            Character governor)
        {
            double structural = CivilUnrestRules.CalculateStructuralBaseline(
                NormalizeTax(planet.TaxLevel), governor?.Competence ?? 0.5f, governor?.Severity ?? 0.5f);
            double target = CivilUnrestRules.CalculateContentmentTarget(
                structural,
                imperial.Garrison,
                imperial.Population,
                region.NonConsumerPopulation,
                region.CarryingCapacity);

            long hiddenConvertingPopulation = region.RegionFactionMap.Values
                .Where(rf => !rf.IsPublic && rf.PlanetFaction.Faction.GrowthType == GrowthType.Conversion)
                .Sum(rf => rf.Population);
            double corruptionShare = hiddenConvertingPopulation /
                (double)Math.Max(1L, region.NonConsumerPopulation);
            target -= Math.Min(
                MaximumHiddenCorruptionPenalty,
                corruptionShare * HiddenCorruptionPenaltyPerPopulationShare);

            int adjacentPublicRevolts = region.GetAdjacentRegions().Count(adjacent =>
                adjacent.RegionFactionMap.Values.Any(rf =>
                    rf.IsPublic && rf.PlanetFaction.Faction.GrowthType == GrowthType.Unrest));
            target -= Math.Min(
                MaximumAdjacentRevoltPenalty,
                adjacentPublicRevolts * AdjacentPublicRevoltPenalty);

            imperial.Contentment = (float)CivilUnrestRules.DriftContentment(imperial.Contentment, target);
        }

        private long CalculateDesiredUnrestPopulation(RegionFaction imperial, RegionFaction unrest)
        {
            long total = imperial.Population + (unrest?.Population ?? 0);
            if (total <= 0) return 0;

            double currentShare = (unrest?.Population ?? 0) / (double)total;
            double targetShare = CivilUnrestRules.CalculateTargetUnrestShare(imperial.Contentment);
            double nextShare = CivilUnrestRules.DriftUnrestShare(currentShare, targetShare);
            return RoundStochastically(total * nextShare);
        }

        private void RecruitFromImperialPopulation(
            RegionFaction imperial,
            RegionFaction unrest,
            long recruits)
        {
            if (recruits <= 0 || imperial.Population <= 0) return;
            recruits = Math.Min(recruits, imperial.Population);
            long loyalCivilians = Math.Max(0, imperial.Population - imperial.Garrison);
            double pdfChance = CivilUnrestRules.CalculatePdfRecruitSelectionChance(
                imperial.Garrison, loyalCivilians);
            long pdfRecruits = Math.Min(imperial.Garrison, RoundStochastically(recruits * pdfChance));

            imperial.Garrison -= pdfRecruits;
            imperial.Population -= recruits;
            unrest.Population += recruits;
            unrest.Garrison += pdfRecruits;
        }

        private void ReturnToImperialPopulation(
            RegionFaction imperial,
            RegionFaction unrest,
            long returnees)
        {
            if (returnees <= 0 || unrest.Population <= 0) return;
            returnees = Math.Min(returnees, unrest.Population);
            long embeddedReturning = unrest.IsPublic
                ? 0
                : Math.Min(unrest.Garrison,
                    RoundStochastically(returnees * (unrest.Garrison / (double)unrest.Population)));
            long armedDemobilizing = Math.Min(unrest.ArmedCivilians,
                RoundStochastically(returnees * (unrest.ArmedCivilians / (double)unrest.Population)));

            unrest.Garrison -= embeddedReturning;
            unrest.ArmedCivilians -= armedDemobilizing;
            unrest.Population -= returnees;
            imperial.Population += returnees;
            imperial.Garrison += embeddedReturning;
        }

        private void UpdateArmedCivilianCadres(RegionFaction unrest, float contentment)
        {
            long maximum = Math.Max(0, unrest.Population - unrest.Garrison);
            long target = Math.Min(maximum, RoundStochastically(
                unrest.Population * CivilUnrestRules.CalculateTargetArmedCivilianFraction(contentment)));
            long current = unrest.ArmedCivilians;
            long next = current + RoundSigned((target - current) * CivilUnrestRules.WeeklyAllegianceGapClosingRate);
            unrest.ArmedCivilians = Math.Clamp(next, 0, maximum);
        }

        private void ApplyGovernorResponse(
            RegionFaction imperial,
            RegionFaction unrest,
            Character governor)
        {
            if (governor == null || unrest.IsPublic) return;
            double unrestShare = unrest.Population /
                (double)Math.Max(1L, imperial.Population + unrest.Population);
            bool foundRealEvidence = unrest.Population > 0
                && _session.Random.GetLinearDouble()
                    < Math.Clamp(governor.Investigation * unrestShare * 10.0, 0.0, 1.0);
            bool falsePositive = !foundRealEvidence
                && _session.Random.GetLinearDouble()
                    < governor.Paranoia * FalseCrackdownWeeklyChanceScale;
            if (!foundRealEvidence && !falsePositive) return;

            double intensity = Math.Clamp(governor.Severity, 0f, 1f);
            if (foundRealEvidence && unrest.ArmedCivilians > 0)
            {
                long suppressed = Math.Min(unrest.ArmedCivilians,
                    RoundStochastically(imperial.Garrison * CrackdownArmedSuppressionPerPdf * intensity));
                unrest.ArmedCivilians -= suppressed;
            }

            double shock = intensity * (falsePositive ? 4.0 : 1.0);
            imperial.Contentment -= (float)shock;
        }

        private void MigrateHiddenSupportersTowardPublicRevolt(
            Planet planet,
            PlanetFaction unrestPlanetFaction)
        {
            List<RegionFaction> publicRevolts = planet.Regions
                .Where(region => region != null)
                .Select(region => region.RegionFactionMap.TryGetValue(
                    unrestPlanetFaction.Faction.Id, out RegionFaction rf) ? rf : null)
                .Where(rf => rf?.IsPublic == true)
                .ToList();
            if (publicRevolts.Count == 0 || HasPublicExternalEnemy(planet, unrestPlanetFaction.Faction))
            {
                return;
            }

            List<Migration> migrations = [];
            foreach (RegionFaction source in planet.Regions
                .Where(region => region != null)
                .Select(region => region.RegionFactionMap.TryGetValue(
                    unrestPlanetFaction.Faction.Id, out RegionFaction rf) ? rf : null)
                .Where(rf => rf != null && !rf.IsPublic && rf.Population > rf.Garrison))
            {
                (Region nextStep, RegionFaction target) = FindMigrationStep(source.Region, publicRevolts);
                if (nextStep == null || target == null) continue;
                long eligible = source.Population - source.Garrison;
                long amount = Math.Min(eligible,
                    RoundStochastically(CivilUnrestRules.CalculateWeeklyMigration(eligible)));
                if (amount <= 0) continue;
                long armed = Math.Min(source.ArmedCivilians,
                    RoundStochastically(amount * (source.ArmedCivilians / (double)eligible)));
                migrations.Add(new Migration(source, nextStep, amount, armed));
            }

            foreach (Migration migration in migrations)
            {
                migration.Source.ArmedCivilians -= migration.Armed;
                migration.Source.Population -= migration.Amount;
                RegionFaction destination = EnsureRegionFaction(migration.Destination, unrestPlanetFaction);
                destination.Population += migration.Amount;
                destination.ArmedCivilians += migration.Armed;
            }
        }

        private void ResolveRegionalVisibility(Planet planet, PlanetFaction unrestPlanetFaction)
        {
            bool externalEnemy = HasPublicExternalEnemy(planet, unrestPlanetFaction.Faction);
            foreach (Region region in planet.Regions.Where(region => region != null))
            {
                if (!region.RegionFactionMap.TryGetValue(
                    unrestPlanetFaction.Faction.Id, out RegionFaction unrest))
                {
                    continue;
                }

                long loyalStrength = CalculateLocalLoyalStrength(region);
                if (!unrest.IsPublic && CivilUnrestRules.ShouldGoPublic(
                    unrest.MilitaryStrength, loyalStrength, externalEnemy))
                {
                    FactionRevealService.Reveal(unrest);
                    unrest.Organization = 100;
                    TransferDefensesOnReveal(region, unrest);
                }
                else if (unrest.IsPublic && CivilUnrestRules.ShouldReturnToHiding(
                    unrest.MilitaryStrength, loyalStrength))
                {
                    unrest.IsPublic = false;
                    unrest.HalveDefensesOnGoingToGround();
                }
            }

            unrestPlanetFaction.IsPublic = planet.Regions
                .Where(region => region != null)
                .Any(region => region.RegionFactionMap.TryGetValue(
                    unrestPlanetFaction.Faction.Id, out RegionFaction rf) && rf.IsPublic);
        }

        private static long CalculateLocalLoyalStrength(Region region)
        {
            long strength = region.RegionFactionMap.Values
                .Where(rf => rf.IsPublic
                    && (rf.PlanetFaction.Faction.IsDefaultFaction
                        || rf.PlanetFaction.Faction.IsPlayerFaction))
                .Sum(rf => rf.MilitaryStrength);
            strength += region.RegionFactionMap.Values
                .Where(rf => rf.IsPublic
                    && (rf.PlanetFaction.Faction.IsDefaultFaction
                        || rf.PlanetFaction.Faction.IsPlayerFaction))
                .SelectMany(rf => rf.LandedSquads)
                .SelectMany(squad => squad.Members)
                .Sum(member => (long)member.Template.BattleValue);
            return strength;
        }

        private void TransferDefensesOnReveal(Region region, RegionFaction insurgents)
        {
            RegionFaction defender = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.IsPublic);
            if (defender == null) return;

            double listeningPost = TransferDefense(defender.ListeningPost);
            defender.ListeningPost -= listeningPost;
            insurgents.ListeningPost += listeningPost;
            double antiAir = TransferDefense(defender.AntiAir);
            defender.AntiAir -= antiAir;
            insurgents.AntiAir += antiAir;
            double entrenchment = TransferDefense(defender.Entrenchment);
            defender.Entrenchment -= entrenchment;
            insurgents.Entrenchment += entrenchment;
            defender.Organization = (int)(_session.Random.GetLinearDouble() * 100);
        }

        private double TransferDefense(double defense)
        {
            if (defense <= 0) return 0;
            return Math.Clamp(
                defense / 2.0 + _session.Random.NextRandomZValue(), 0.0, defense);
        }

        private void RemoveEmptyUnrestPresences(Planet planet, PlanetFaction unrestPlanetFaction)
        {
            foreach (Region region in planet.Regions.Where(region => region != null))
            {
                if (region.RegionFactionMap.TryGetValue(
                        unrestPlanetFaction.Faction.Id, out RegionFaction unrest)
                    && unrest.Population <= 0)
                {
                    region.RegionFactionMap.Remove(unrestPlanetFaction.Faction.Id);
                }
            }

            if (!planet.Regions.Where(region => region != null).Any(region =>
                region.RegionFactionMap.ContainsKey(unrestPlanetFaction.Faction.Id)))
            {
                planet.PlanetFactionMap.Remove(unrestPlanetFaction.Faction.Id);
            }
        }

        private double NormalizeTax(int taxLevel)
        {
            int minimum = _session.Rules.PlanetTemplateMap.Values.Min(template => template.TaxRange.MinValue);
            int maximum = _session.Rules.PlanetTemplateMap.Values.Max(template => template.TaxRange.MaxValue);
            return maximum <= minimum ? 0.0 : Math.Clamp((taxLevel - minimum) / (double)(maximum - minimum), 0.0, 1.0);
        }

        private static PlanetFaction EnsurePlanetFaction(Planet planet, Faction faction)
        {
            if (planet.PlanetFactionMap.TryGetValue(faction.Id, out PlanetFaction existing))
            {
                return existing;
            }

            PlanetFaction created = new(faction) { IsPublic = false };
            planet.PlanetFactionMap[faction.Id] = created;
            return created;
        }

        private static RegionFaction EnsureRegionFaction(Region region, PlanetFaction planetFaction)
        {
            if (region.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction existing))
            {
                return existing;
            }

            RegionFaction created = new(planetFaction, region)
            {
                IsPublic = false,
                Population = 0,
                Garrison = 0,
                ArmedCivilians = 0,
                Organization = 100
            };
            region.RegionFactionMap[planetFaction.Faction.Id] = created;
            return created;
        }

        private bool HasPublicExternalEnemy(Planet planet, Faction unrestFaction) =>
            planet.Regions.Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Any(rf => rf.IsPublic
                    && rf.PlanetFaction.Faction.Id != unrestFaction.Id
                    && FactionDispositionService.IsExternalEnemy(
                        rf.PlanetFaction.Faction, unrestFaction));

        private static (Region NextStep, RegionFaction Target) FindMigrationStep(
            Region source,
            IReadOnlyList<RegionFaction> targets)
        {
            Queue<(Region Region, Region FirstStep, int Distance)> queue = new();
            HashSet<Region> visited = [source];
            foreach (Region adjacent in source.GetAdjacentRegions().OrderBy(region => region.Id))
            {
                queue.Enqueue((adjacent, adjacent, 1));
                visited.Add(adjacent);
            }

            int bestDistance = int.MaxValue;
            List<(Region FirstStep, RegionFaction Target)> candidates = [];
            while (queue.Count > 0)
            {
                (Region current, Region firstStep, int distance) = queue.Dequeue();
                if (distance > bestDistance) break;
                RegionFaction target = targets.FirstOrDefault(candidate => candidate.Region == current);
                if (target != null)
                {
                    bestDistance = distance;
                    candidates.Add((firstStep, target));
                    continue;
                }

                foreach (Region adjacent in current.GetAdjacentRegions().OrderBy(region => region.Id))
                {
                    if (visited.Add(adjacent)) queue.Enqueue((adjacent, firstStep, distance + 1));
                }
            }

            var chosen = candidates
                .OrderByDescending(candidate => candidate.Target.MilitaryStrength)
                .ThenBy(candidate => candidate.Target.Region.Id)
                .FirstOrDefault();
            return (chosen.FirstStep, chosen.Target);
        }

        private long RoundStochastically(double value)
        {
            if (value <= 0) return 0;
            long whole = (long)Math.Floor(value);
            return _session.Random.GetLinearDouble() < value - whole ? whole + 1 : whole;
        }

        private long RoundSigned(double value) => value >= 0
            ? RoundStochastically(value)
            : -RoundStochastically(-value);

        private sealed record Migration(
            RegionFaction Source,
            Region Destination,
            long Amount,
            long Armed);
    }
}
