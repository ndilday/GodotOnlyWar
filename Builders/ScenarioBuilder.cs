using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Narrative;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;

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
        // first objective), deterministically near the lower-middle of the band. Fallbacks widen
        // the band and, ultimately, reuse the old lowest-population-enemy rule so generation can
        // never fail.
        private static Planet SelectPromisedWorld(List<Planet> planetList, GameRulesData data)
        {
            List<Planet> eligible = planetList
                .Where(p => p.GetControllingFaction().IsDefaultFaction
                            && p.GovernanceTier == GovernanceTier.Planetary
                            && p.Population >= ScenarioRules.MinPromisedWorldPopulation
                            && p.Population <= ScenarioRules.MaxPromisedWorldPopulation)
                .OrderBy(p => p.Population).ThenBy(p => p.Id)
                .ToList();

            if (eligible.Count == 0)
            {
                // Widen: any non-capital Imperial world, regardless of the population band.
                eligible = planetList
                    .Where(p => p.GetControllingFaction().IsDefaultFaction
                                && p.GovernanceTier == GovernanceTier.Planetary)
                    .OrderBy(p => p.Population).ThenBy(p => p.Id)
                    .ToList();
            }

            if (eligible.Count > 0)
            {
                // Lower-middle of the ordered band: a worthwhile but not premier world.
                return eligible[eligible.Count / 3];
            }

            // Ultimate fallback: the old FoundTakebackPlanet rule, so generation cannot fail.
            return planetList
                .Where(p => !p.GetControllingFaction().IsDefaultFaction)
                .OrderBy(p => p.Population).ThenBy(p => p.Id)
                .First();
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

                RegionFaction tyranid = new RegionFaction(tyranidPlanetFaction, region)
                {
                    IsPublic = true,
                    Population = ScenarioRules.TyranidRegionPopulation,
                    Garrison = ScenarioRules.TyranidRegionGarrison,
                    Organization = 1,
                    Entrenchment = 0,
                    Detection = 0,
                    AntiAir = 0,
                    GrowthMultiplier = ScenarioRules.TyranidGrowthMultiplier
                };
                region.RegionFactionMap[tyranidFaction.Id] = tyranid;
            }
        }

        // §3.3 — park the chapter in orbit. Squads stay embarked (no CurrentRegion, no
        // LandedSquads); the player's first action is to land them via the Planet Tactical screen.
        private static void PlaceFleetInOrbit(Sector sector, PlayerForce playerForce, Planet promised)
        {
            foreach (TaskForce taskForce in playerForce.Fleet.TaskForces)
            {
                taskForce.Planet = promised;
                taskForce.Position = promised.Position;
                sector.AddNewFleet(taskForce);
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
