using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Narrative;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Builders
{
    // Stamps the "Promised World" opening scenario on top of an already-generated, already-
    // governed sector (Design/OpeningScenario.md §3). This is an override layer, not a fork of
    // the generator: it selects a mostly-Imperial world, confines a Tyranid incursion to a few
    // regions, parks the chapter fleet in orbit (the player must land), resolves the sitting
    // Sector Lord as the promising authority, and returns the persistent CampaignScenario.
    //
    // All randomness draws from the already-seeded RNG stream so that seed + scenario reproduces
    // the same opening. Replaces the old SectorBuilder.FoundTakebackPlanet prototype.
    internal static class ScenarioBuilder
    {
        // Note on signature deviation from the design sketch (§3): the fleet is registered via
        // sector.AddNewFleet rather than appended to a forceList consumed by the Sector
        // constructor, because by this point the sector already exists (governance must be
        // assigned before GetSectorLord can resolve). currentDate is threaded for the real
        // BriefingComposer / founding-history entry that lands next session.
        internal static CampaignScenario StampPromisedWorld(
            Sector sector, GameRulesData data, Date currentDate,
            PlayerForce playerForce, List<Planet> planetList, List<Character> characterList)
        {
            Planet promised = SelectPromisedWorld(planetList, data);
            EnsureGenestealerCult(promised, data);
            StampTyranidPresence(promised, data);
            PlaceFleetInOrbit(sector, playerForce, promised);
            Character authority = ResolveAuthority(sector, planetList, characterList, data,
                                                   out GovernanceTier authorityTier);
            string briefingText = ComposeBriefing(sector, promised, authority, authorityTier,
                                                  playerForce, data, currentDate);

            return new CampaignScenario(
                ScenarioType.PromisedWorld,
                promised.Id,
                briefingText,
                authority.Id);
        }

        // §3.1 — the promised world is Imperial-habitable but invaded. We pick a default-faction
        // world in a tuned population band, excluding governance capitals (too central for a
        // first objective). Among the band, the world nearest the sector edge is chosen: the
        // opening invasion sits on the frontier, which both reads correctly (a rimward incursion
        // the over-stretched Imperium can't spare a regiment for) and keeps the first objective off
        // the populous sector core. Fallbacks widen the band and, ultimately, reuse the old
        // lowest-population-enemy rule so generation can never fail.
        private static Planet SelectPromisedWorld(List<Planet> planetList, GameRulesData data)
        {
            List<Planet> eligible = planetList
                .Where(p => p.GetControllingFaction().IsDefaultFaction
                            && p.GovernanceTier == GovernanceTier.Planetary
                            && p.Population >= ScenarioRules.MinPromisedWorldPopulation
                            && p.Population <= ScenarioRules.MaxPromisedWorldPopulation)
                .ToList();

            if (eligible.Count == 0)
            {
                // Widen: any non-capital Imperial world, regardless of the population band.
                eligible = planetList
                    .Where(p => p.GetControllingFaction().IsDefaultFaction
                                && p.GovernanceTier == GovernanceTier.Planetary)
                    .ToList();
            }

            if (eligible.Count > 0)
            {
                // Nearest the sector edge wins; population then id are deterministic tie-breaks so
                // a seed reproduces the same world.
                return eligible
                    .OrderBy(p => EdgeDistance(p, data))
                    .ThenBy(p => p.Population)
                    .ThenBy(p => p.Id)
                    .First();
            }

            // Ultimate fallback: the old FoundTakebackPlanet rule, so generation cannot fail.
            return planetList
                .Where(p => !p.GetControllingFaction().IsDefaultFaction)
                .OrderBy(p => p.Population).ThenBy(p => p.Id)
                .First();
        }

        // Chebyshev distance from the planet's grid cell to the nearest sector boundary. Smaller
        // means closer to the edge; a corner world is 0. Used to bias the opening invasion rimward.
        private static int EdgeDistance(Planet planet, GameRulesData data)
        {
            int maxX = data.SectorSize.X - 1;
            int maxY = data.SectorSize.Y - 1;
            int x = planet.Position.X;
            int y = planet.Position.Y;
            return Math.Min(Math.Min(x, maxX - x), Math.Min(y, maxY - y));
        }

        // §3.1a — canon: the Tyranid invasion is drawn in by a Genestealer Cult that has already
        // infiltrated the target world, its psychic beacon calling the hive fleet down. So the
        // promised world must always harbour a hidden cult, whether or not planet generation
        // happened to seed one there. If a cult is already present (generation rolled the ~10%
        // chance), we leave it as-is; otherwise we seed one with the same infiltration logic
        // generation uses. This runs before StampTyranidPresence so the cult carves its
        // population out of the intact Imperial regions, not the reduced post-incursion remnant.
        private static void EnsureGenestealerCult(Planet promised, GameRulesData data)
        {
            Faction cultFaction = data.SectorFactions.Infiltrator;
            if (promised.PlanetFactionMap.ContainsKey(cultFaction.Id))
            {
                return;
            }
            PlanetBuilder.HandleInfiltratingFaction(cultFaction, promised);
        }

        // §3.2 — confine the Tyranids to a contiguous cluster of N regions, leaving the rest of
        // the world default-Imperial. Each stamped region gets a public Tyranid RegionFaction
        // with tuned strength and a sub-1 growth throttle; the local Imperial garrison is broken
        // and its civilians reduced to a hidden, displaced remnant so the region resolves to
        // single (Tyranid) control.
        private static void StampTyranidPresence(Planet promised, GameRulesData data)
        {
            Faction tyranidFaction = data.SectorFactions.Invader;
            if (!promised.PlanetFactionMap.TryGetValue(tyranidFaction.Id, out PlanetFaction tyranidPlanetFaction))
            {
                tyranidPlanetFaction = new PlanetFaction(tyranidFaction);
                promised.PlanetFactionMap[tyranidFaction.Id] = tyranidPlanetFaction;
            }
            // The Navy already identified the incursion; the world is known to be invaded.
            tyranidPlanetFaction.IsPublic = true;

            // Size the Tyranids relative to the world's own PDF (measured before the stamp), so the
            // fight scales across the wide promised-world population band rather than being fixed by
            // an absolute headcount that is meaningless on a hive-scale world (§8 / ScenarioRules).
            (long tyranidGarrison, long tyranidPopulation) = ScaledTyranidStrength(promised, data);

            int regionCount = RNG.GetIntBelowMax(
                ScenarioRules.MinTyranidRegions, ScenarioRules.MaxTyranidRegions + 1);
            int startIndex = RNG.GetIntBelowMax(0, promised.Regions.Length);

            for (int i = 0; i < regionCount; i++)
            {
                Region region = promised.Regions[(startIndex + i) % promised.Regions.Length];

                if (region.RegionFactionMap.TryGetValue(data.DefaultFaction.Id, out RegionFaction imperial))
                {
                    imperial.Garrison = 0;
                    imperial.Population = (long)(imperial.Population * ScenarioRules.ImperialRemnantFraction);
                    // Displaced remnant: hidden, so the region reads as Tyranid-controlled rather
                    // than as two-public-faction (which has no single controlling faction).
                    imperial.IsPublic = false;
                }

                // The world-average-scaled Tyranid population can exceed a specific region's
                // carrying capacity (regions vary in size); clamp it so the stamped Tyranid plus every
                // population already in the region (the displaced Imperial remnant and the hidden
                // Genestealer Cult seeded by EnsureGenestealerCult) never overpopulate it — a
                // generation invariant (no region starts above capacity). The Tyranid faction is not
                // added yet, so region.Population is the current headcount to leave room for. Garrison
                // is not population, so it is left unclamped.
                long existingPopulation = region.Population;
                long regionTyranidPopulation = Math.Max(0L,
                    Math.Min(tyranidPopulation, region.CarryingCapacity - existingPopulation));

                RegionFaction tyranid = new RegionFaction(tyranidPlanetFaction, region)
                {
                    IsPublic = true,
                    Population = regionTyranidPopulation,
                    Garrison = tyranidGarrison,
                    // Raiders, not dug-in defenders: low organization (and zero fortification) keeps
                    // their offensive throughput modest, so spread is gradual rather than runaway.
                    Organization = 1,
                    Entrenchment = 0,
                    Detection = 0,
                    AntiAir = 0,
                    GrowthMultiplier = ScenarioRules.TyranidGrowthMultiplier
                };
                region.RegionFactionMap[tyranidFaction.Id] = tyranid;
            }
        }

        // Tyranid per-region garrison/population as a fraction of the promised world's average
        // Imperial region, measured before any region is overrun (§8). Returns at least 1 of each
        // so a stamped region is never empty even on a tiny world.
        private static (long garrison, long population) ScaledTyranidStrength(Planet promised, GameRulesData data)
        {
            List<RegionFaction> imperialRegions = promised.Regions
                .Where(r => r.RegionFactionMap.ContainsKey(data.DefaultFaction.Id))
                .Select(r => r.RegionFactionMap[data.DefaultFaction.Id])
                .ToList();
            if (imperialRegions.Count == 0)
            {
                return (1L, 1L);
            }
            double avgGarrison = imperialRegions.Average(rf => rf.Garrison);
            double avgPopulation = imperialRegions.Average(rf => rf.Population);
            long garrison = Math.Max(1L, (long)(avgGarrison * ScenarioRules.TyranidStrengthFraction));
            long population = Math.Max(1L, (long)(avgPopulation * ScenarioRules.TyranidStrengthFraction));
            return (garrison, population);
        }

        // §3.3 — park the chapter in orbit. Squads stay embarked (no CurrentRegion, no
        // LandedSquads); the player's first action is to land them via the Planet Tactical screen.
        // "Embarked" is a real ship assignment, not just the absence of a region: the Planet
        // Tactical screen's landing/loading actions both pivot off a squad's current state
        // (BoardedLocation to land, a region's LandedSquads to load) and have no path for a
        // squad that is in neither, so every squad must be placed onto a ship here.
        private static void PlaceFleetInOrbit(Sector sector, PlayerForce playerForce, Planet promised)
        {
            IEnumerator<Squad> squads = playerForce.Army.SquadMap.Values
                .Where(s => s.Members.Count > 0).GetEnumerator();
            bool hasSquad = squads.MoveNext();
            foreach (TaskForce taskForce in playerForce.Fleet.TaskForces)
            {
                taskForce.Planet = promised;
                taskForce.Position = promised.Position;
                foreach (Ship ship in taskForce.Ships)
                {
                    while (hasSquad && squads.Current.Members.Count <= ship.AvailableCapacity)
                    {
                        ship.LoadSquad(squads.Current);
                        squads.Current.BoardedLocation = ship;
                        hasSquad = squads.MoveNext();
                    }
                }
                sector.AddNewFleet(taskForce);
            }
            if (hasSquad)
            {
                int remainingSoldiers = 0;
                do
                {
                    remainingSoldiers += squads.Current.Members.Count;
                }
                while (squads.MoveNext());

                int fleetCapacity = playerForce.Fleet.TaskForces
                    .SelectMany(taskForce => taskForce.Ships)
                    .Sum(ship => ship.Template.SoldierCapacity);
                throw new InvalidOperationException(
                    "Starting fleet capacity is insufficient to embark the chapter. " +
                    $"{remainingSoldiers} soldiers could not be assigned to a ship " +
                    $"(fleet capacity {fleetCapacity}).");
            }
        }

        // §3.4 — no character is created on the common path: the authority is the sitting Sector
        // Lord (governor of the sector capital). Fall back to the highest-importance Imperial
        // governor anywhere, then — only if no Imperial governor exists at all — to a generated
        // free-standing commander, so the scenario can never lack an authority.
        private static Character ResolveAuthority(Sector sector, List<Planet> planetList,
                                                  List<Character> characterList, GameRulesData data,
                                                  out GovernanceTier authorityTier)
        {
            Planet capital = sector.GetSectorCapital();
            if (capital?.Governor != null)
            {
                authorityTier = capital.GovernanceTier;   // SectorCapital on the common path
                return capital.Governor;
            }

            Planet fallbackSeat = planetList
                .Where(p => p.GetControllingFaction().IsDefaultFaction && p.Governor != null)
                .OrderByDescending(p => p.Importance).ThenByDescending(p => p.Population).ThenBy(p => p.Id)
                .FirstOrDefault();
            if (fallbackSeat != null)
            {
                authorityTier = fallbackSeat.GovernanceTier;
                return fallbackSeat.Governor;
            }

            // Last resort (the only path that creates a character): a free-standing commander.
            // Title them as the highest authority, since no seated governor exists to rank.
            authorityTier = GovernanceTier.SectorCapital;
            int newId = (sector.Characters.Count > 0 ? sector.Characters.Max(c => c.Id) : -1) + 1;
            Character authority = CharacterBuilder.GenerateCharacter(newId, data.DefaultFaction);
            sector.Characters.Add(authority);
            characterList.Add(authority);
            return authority;
        }

        // §4 — compose the briefing through the token-substitution BriefingComposer (a placeholder
        // for the eventual §4.19 narrator) and record a matching founding-history entry so the
        // objective sits alongside "Chapter Founding" on the Chapter screen. The authority title is
        // derived from the rank of the seat they hold; the subsector name is sourced from its
        // governance capital (subsectors carry no authored name today).
        private static string ComposeBriefing(Sector sector, Planet promised, Character authority,
                                              GovernanceTier authorityTier, PlayerForce playerForce,
                                              GameRulesData data, Date currentDate)
        {
            string chapterName = playerForce.Army.OrderOfBattle.Name;
            string authorityTitle = BriefingComposer.GetAuthorityTitle(authorityTier);
            string enemyName = data.SectorFactions.Invader.Name;
            string subsectorName = ResolveSubsectorName(sector, promised);

            BriefingTokens tokens = new BriefingTokens
            {
                ChapterName = chapterName,
                PlanetName = promised.Name,
                SubsectorName = subsectorName,
                AuthorityName = authority.Name,
                AuthorityTitle = authorityTitle,
                EnemyName = enemyName,
                // Stable per-seed selector: the promised planet id is deterministic per seed.
                TemplateSelector = promised.Id
            };

            string briefingText = BriefingComposer.ComposePromisedWorldBriefing(tokens);

            playerForce.AddToBattleHistory(currentDate, "The Promised World", new List<string>
            {
                $"{authorityTitle} {authority.Name} pledges {promised.Name}, in the {subsectorName}, "
                + $"to the {chapterName} should the {enemyName} be driven from it — the world to "
                + "become the Chapter's home."
            });

            return briefingText;
        }

        // §4 token sourcing — subsectors have no authored name, so name the promised world's
        // subsector after its governance capital ("{Capital} Subsector"). Falls back to the
        // subsector's id-name, then the planet name, if no capital is seated.
        private static string ResolveSubsectorName(Sector sector, Planet promised)
        {
            Subsector subsector = sector.Subsectors.FirstOrDefault(s => s.Planets.Contains(promised));
            Planet capital = subsector?.GovernanceSeat;
            if (capital != null)
            {
                return $"{capital.Name} Subsector";
            }
            return subsector != null ? $"{subsector.Name} Subsector" : promised.Name;
        }
    }
}
