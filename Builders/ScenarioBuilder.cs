using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Narrative;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Events;

namespace OnlyWar.Builders
{
    // Stamps the "Promised World" opening scenario on top of an already-generated, already-
    // governed sector (Design/Reference/OpeningScenario.md). This is an override layer, not a fork of
    // the generator: it selects a mostly-host-controlled world, confines an invader incursion to a few
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
            ScenarioProfile profile = data.ScenarioProfiles.GetRequired(ScenarioKeys.PromisedWorld);
            Faction infiltrator = SelectScenarioFaction(
                profile, ScenarioFactionSlotKeys.Infiltrator, data);
            Faction invader = SelectScenarioFaction(
                profile, ScenarioFactionSlotKeys.Invader, data);

            // The opening plays out as a timed sequence during generation rather than being stamped
            // as a static board (Design/Reference/OpeningScenario.md): the
            // world the player inherits is emergent — sometimes a fresh invader beachhead, sometimes a
            // month-eaten ruin — from the same simulation that runs during play. All draws come from
            // the already-seeded RNG stream, so seed + scenario still reproduces the same opening.
            Planet promised = SelectPromisedWorld(planetList, data, profile);

            // Seed the hidden infiltrator, pull it up to landing-site strength (this world was
            // chosen because its infiltrator is deep and ready), then have it rise in open revolt.
            EnsureInfiltrator(promised, infiltrator, profile);
            StrengthenPromisedWorldInfiltrator(promised, infiltrator, profile);
            RevealInfiltrator(promised, infiltrator);
            SeedPromisedWorldInfiltratorIntel(promised, infiltrator, profile);

            // A single TurnController drives both planet-scoped sims (no player upkeep, no other
            // planets, no scenario resolution — see SimulatePlanetForward).
            TurnController controller = new TurnController();

            // Pre-landing: the revealed infiltrator wars against the host faction, weakening the
            // defenders the invader will land into.
            controller.SimulatePlanetForward(sector, promised, profile.PreLandingTurns);

            // The authored beachhead makes planetfall onto the now-weakened board.
            StampInvaderPresence(promised, data, invader, profile);

            // Post-landing: the stranded swarm eats and spreads for a Gaussian-random stretch (the
            // Navy strands it — no reinforcement mechanism exists) before the player arrives.
            controller.SimulatePlanetForward(sector, promised, PostLandingTurns(profile));

            // The player arrives last, into whatever state the sims produced.
            PlaceFleetInOrbit(sector, playerForce, promised);
            Character authority = ResolveAuthority(sector, planetList, characterList, data,
                                                   out GovernanceTier authorityTier);
            string briefingText = ComposeBriefing(sector, promised, authority, authorityTier,
                                                  playerForce, invader, currentDate);

            PlayerSoldier chapterMaster = playerForce.Army.OrderOfBattle
                .GetAllMembers()
                .OfType<PlayerSoldier>()
                .FirstOrDefault(soldier => soldier.Template == data.ChapterDoctrine.ChapterMaster);
            playerForce.RecordChapterFounded(
                currentDate,
                new ChapterFoundedPayload(
                    playerForce.Army.OrderOfBattle.Name,
                    currentDate.GetTotalWeeks(),
                    chapterMaster?.Id,
                    chapterMaster?.Name,
                    playerForce.Army.PlayerSoldierMap.Count,
                    authority.Name,
                    briefingText,
                    promised.Id,
                    promised.Name),
                chapterMaster?.Id,
                chapterMaster?.Name,
                promised.Id,
                promised.Name);

            return new CampaignScenario(
                ScenarioType.PromisedWorld,
                promised.Id,
                briefingText,
                authority.Id);
        }

        // Weeks the stranded invader force feeds after planetfall before the player arrives:
        // max(0, round(mean + z)), z ~ N(0,1), so the opening varies from a fresh beachhead to a
        // deeply consumed world across seeds (Design/Reference/OpeningScenario.md). Drawn from the
        // seeded RNG so it is deterministic per seed.
        private static int PostLandingTurns(ScenarioProfile profile)
        {
            double turns = profile.PostLandingTurnsMean + RNG.NextRandomZValue();
            return Math.Max(0, (int)Math.Round(turns));
        }

        private static Faction SelectScenarioFaction(
            ScenarioProfile profile,
            string slotKey,
            GameRulesData data)
        {
            IReadOnlyList<ScenarioFactionOption> options = profile.GetFactionOptions(slotKey);
            if (options.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Scenario profile '{profile.Key}' has no faction for slot '{slotKey}'.");
            }

            ScenarioFactionOption selected = options[0];
            if (options.Count > 1)
            {
                double totalWeight = options.Sum(option => option.SelectionWeight);
                double roll = RNG.GetLinearDouble() * totalWeight;
                foreach (ScenarioFactionOption option in options)
                {
                    if (roll < option.SelectionWeight)
                    {
                        selected = option;
                        break;
                    }
                    roll -= option.SelectionWeight;
                }
            }

            return data.Factions.First(faction => faction.Id == selected.FactionId);
        }

        // Pulls the promised world's infiltrator up to landing-site strength: in each region the
        // infiltrator takes the profile's strength fraction of the combined
        // population and garrison, carving the increase out of the Imperial owner — the deep
        // infiltration that hollowed out this world's PDF and drew the swarm (§4.24). Only ever adds
        // to the infiltrator (a region where a random roll already seeded a larger presence is left alone).
        private static void StrengthenPromisedWorldInfiltrator(
            Planet promised,
            Faction infiltrator,
            ScenarioProfile profile)
        {
            Faction imperialFaction = promised.Regions
                .SelectMany(region => region.RegionFactionMap.Values)
                .Select(regionFaction => regionFaction.PlanetFaction.Faction)
                .FirstOrDefault(faction => faction.IsDefaultFaction);
            if (imperialFaction == null) return;

            float share = profile.PromisedWorldInfiltratorStrengthFraction;
            foreach (Region region in promised.Regions)
            {
                if (!region.RegionFactionMap.TryGetValue(infiltrator.Id, out RegionFaction infiltratorPresence)
                    || !region.RegionFactionMap.TryGetValue(imperialFaction.Id, out RegionFaction imperial))
                {
                    continue;
                }

                long targetPopulation = (long)((infiltratorPresence.Population + imperial.Population) * share);
                if (targetPopulation > infiltratorPresence.Population)
                {
                    long delta = targetPopulation - infiltratorPresence.Population;
                    infiltratorPresence.Population += delta;
                    imperial.Population -= delta;
                }

                long targetGarrison = (long)((infiltratorPresence.Garrison + imperial.Garrison) * share);
                if (targetGarrison > infiltratorPresence.Garrison)
                {
                    long delta = targetGarrison - infiltratorPresence.Garrison;
                    infiltratorPresence.Garrison += delta;
                    imperial.Garrison -= delta;
                }
            }
        }

        // The infiltrated faction throws off concealment and rises in open revolt. It has been
        // waiting for this moment, so its cells are already fully
        // mobilized (Organization 100 — the whole cell can field offensive force immediately).
        // Idempotent if no infiltrator is present (EnsureInfiltrator always seeds one first).
        //
        // The per-region flip goes through FactionRevealService rather than setting IsPublic by
        // hand, so the opening reveal is the same transition CheckForPlanetaryRevolt performs.
        // Setting the flag directly skipped the service's whole reason for existing: a hidden infiltrator's
        // Garrison is personnel embedded in the nominal PDF, and reveal is supposed to strip them
        // from that roster. Because a PUBLIC Conversion faction neither converts nor drafts, nothing
        // downstream ever cleared the seeded garrison either, so every cell carried a vestigial
        // embedded-PDF count for the rest of the campaign. It also picks up HasEmergenceAdvantage
        // (the infiltrator's first offensive after rising is planned as an Ambush) and the PlanetFaction
        // rollup, both of which the hand-rolled flip was silently missing.
        private static void RevealInfiltrator(Planet promised, Faction infiltrator)
        {
            if (!promised.PlanetFactionMap.TryGetValue(infiltrator.Id, out PlanetFaction infiltratorPlanetFaction))
            {
                return;
            }
            infiltratorPlanetFaction.IsPublic = true;
            foreach (Region region in promised.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(infiltrator.Id, out RegionFaction infiltratorRegionFaction))
                {
                    FactionRevealService.Reveal(infiltratorRegionFaction);
                    if (infiltratorRegionFaction.Organization < 100)
                    {
                        infiltratorRegionFaction.Organization = 100;
                    }
                }
            }
        }

        // §3.1 — the promised world is Imperial-habitable but invaded. We pick a default-faction
        // world in a tuned population band, excluding governance capitals (too central for a
        // first objective). Among the band, the world nearest the sector edge is chosen: the
        // opening invasion sits on the frontier, which both reads correctly (a rimward incursion
        // the over-stretched Imperium can't spare a regiment for) and keeps the first objective off
        // the populous sector core. Fallbacks widen the band and, ultimately, reuse the old
        // lowest-population-enemy rule so generation can never fail.
        // The promised-world infiltrator has already infiltrated local government and PDF command.
        // Give it strong per-region belief about every public non-infiltrator force on the planet so its opening
        // decisions model an insider revolt rather than a blind invader scouting from scratch.
        private static void SeedPromisedWorldInfiltratorIntel(
            Planet promised,
            Faction infiltrator,
            ScenarioProfile profile)
        {
            if (!promised.PlanetFactionMap.TryGetValue(infiltrator.Id, out PlanetFaction infiltratorPlanetFaction))
            {
                return;
            }

            // The infiltrator knows its home ground intimately: give it strong awareness of every region
            // holding a public non-infiltrator force, so its opening decisions — and the strategic ambush
            // edge it enjoys attacking from within (the attacker-vs-defender intel differential in
            // StrategicCombatResolver) — model an insider revolt rather than a blind invader. The PDF,
            // having built no listening posts, starts blind to these same regions.
            foreach (Region region in promised.Regions
                         .Where(region => region.RegionFactionMap.Values
                              .Any(rf => rf.PlanetFaction.Faction.Id != infiltrator.Id && rf.IsPublic)))
            {
                infiltratorPlanetFaction.AddRegionAwareness(
                    region,
                    profile.PromisedWorldInfiltratorStartingIntel);

                foreach (RegionFaction target in region.RegionFactionMap.Values
                    .Where(regionFaction => regionFaction.PlanetFaction.Faction.Id != infiltrator.Id
                        && regionFaction.IsPublic)
                    .OrderBy(regionFaction => regionFaction.PlanetFaction.Faction.Id))
                {
                    infiltratorPlanetFaction.SeedTargetBelief(
                        region,
                        target.PlanetFaction.Faction,
                        FactionIntelligenceRules.ConfirmedThreshold,
                        target.Population,
                        target.GetDeployedStrength(),
                        0,
                        IntelObservationSource.Scenario);
                }
            }
        }

        private static Planet SelectPromisedWorld(
            List<Planet> planetList,
            GameRulesData data,
            ScenarioProfile profile)
        {
            List<Planet> eligible = planetList
                .Where(p => p.GetControllingFaction()?.IsDefaultFaction == true
                            && p.GovernanceTier == GovernanceTier.Planetary
                            && data.PlanetTemplateEligibility.IsEligible(
                                PlanetTemplateEligibilityKeys.PromisedWorld,
                                p.Template.Id)
                            && p.Population <= profile.MaxPromisedWorldPopulation)
                .ToList();

            if (eligible.Count == 0)
            {
                // Widen: any non-capital Imperial world of an eligible type, regardless of the
                // population ceiling. The type exclusion (no Hive/Forge) is a hard rule, so it is
                // kept even in the fallback — only the size ceiling is relaxed.
                eligible = planetList
                    .Where(p => p.GetControllingFaction()?.IsDefaultFaction == true
                                && p.GovernanceTier == GovernanceTier.Planetary
                                && data.PlanetTemplateEligibility.IsEligible(
                                    PlanetTemplateEligibilityKeys.PromisedWorld,
                                    p.Template.Id))
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
                .Where(p => p.GetControllingFaction()?.IsDefaultFaction == false)
                .OrderBy(p => p.Population).ThenBy(p => p.Id)
                .First();
        }

        // Chebyshev distance from the planet's grid cell to the nearest sector boundary. Smaller
        // means closer to the edge; a corner world is 0. Used to bias the opening invasion rimward.
        private static int EdgeDistance(Planet planet, GameRulesData data)
        {
            SectorGenerationProfile profile = data.SectorGenerationProfile;
            int maxX = profile.SectorWidth - 1;
            int maxY = profile.SectorHeight - 1;
            int x = planet.Position.X;
            int y = planet.Position.Y;
            return Math.Min(Math.Min(x, maxX - x), Math.Min(y, maxY - y));
        }

        // §3.1a — the selected scenario infiltrator must always be present on the target world,
        // whether or not ordinary planet generation happened to seed it there. This runs before
        // StampInvaderPresence so the infiltrator carves its population out of the intact host
        // regions, not the reduced post-incursion remnant.
        private static void EnsureInfiltrator(
            Planet promised,
            Faction infiltrator,
            ScenarioProfile profile)
        {
            if (promised.PlanetFactionMap.ContainsKey(infiltrator.Id))
            {
                return;
            }
            PlanetBuilder.ApplyFactionPresence(
                infiltrator,
                promised,
                new FactionPlanetPresenceRule(
                    SectorGenerationProfileKeys.Standard,
                    infiltrator.Id,
                    promised.Template.Id,
                    FactionPresenceMode.Hidden,
                    1.0,
                    profile.InitialInfiltratorPopulationShareMin,
                    profile.InitialInfiltratorPopulationShareMax,
                    profile.InitialInfiltratorGarrisonPerPopulation));
        }

        // §3.2 — confine the selected invader to a contiguous cluster of N regions, leaving the rest of
        // the world default-Imperial. Each stamped region gets a public invader RegionFaction
        // with tuned strength and a sub-1 growth throttle; the local Imperial garrison is broken
        // and its civilians reduced to a hidden, displaced remnant so the region resolves to
        // single invader control.
        private static void StampInvaderPresence(
            Planet promised,
            GameRulesData data,
            Faction invaderFaction,
            ScenarioProfile profile)
        {
            if (!promised.PlanetFactionMap.TryGetValue(invaderFaction.Id, out PlanetFaction invaderPlanetFaction))
            {
                invaderPlanetFaction = new PlanetFaction(invaderFaction);
                promised.PlanetFactionMap[invaderFaction.Id] = invaderPlanetFaction;
            }
            // The Navy already identified the incursion; the world is known to be invaded.
            invaderPlanetFaction.IsPublic = true;

            int regionCount = RNG.GetIntBelowMax(
                profile.MinInvaderRegions, profile.MaxInvaderRegions + 1);
            int startIndex = RNG.GetIntBelowMax(0, promised.Regions.Length);

            // Size the invader relative to the world's own host garrison (measured before the stamp), so the
            // fight scales across the wide promised-world population band rather than being fixed by
            // an absolute headcount that is meaningless on a hive-scale world (§8).
            // The region count is drawn first because the planetary total is split across it; the
            // Draw order is unchanged because ScaledInvaderStrength consumes no randomness.
            long invaderPopulation = ScaledInvaderStrength(
                promised, data, profile, regionCount);

            for (int i = 0; i < regionCount; i++)
            {
                Region region = promised.Regions[(startIndex + i) % promised.Regions.Length];

                if (region.RegionFactionMap.TryGetValue(data.DefaultFaction.Id, out RegionFaction imperial))
                {
                    imperial.Garrison = 0;
                    imperial.Population = (long)(imperial.Population * profile.ImperialRemnantFraction);
                    // Displaced remnant: hidden, so the region reads as invader-controlled rather
                    // than as two-public-faction (which has no single controlling faction).
                    imperial.IsPublic = false;
                }

                // The world-average-scaled invader population can exceed a specific region's
                // carrying capacity (regions vary in size); clamp it so the stamped invader plus every
                // population already in the region (the displaced Imperial remnant and the hidden
                // infiltrator seeded by EnsureInfiltrator) never overpopulate it — a generation
                // invariant (no region starts above capacity). The invader faction is not
                // added yet, so region.Population is the current headcount to leave room for. Garrison
                // is not population, so it is left unclamped.
                long existingPopulation = region.Population;
                long regionInvaderPopulation = Math.Max(0L,
                    Math.Min(invaderPopulation, region.CarryingCapacity - existingPopulation));

                RegionFaction invader = new RegionFaction(invaderPlanetFaction, region)
                {
                    IsPublic = true,
                    Population = regionInvaderPopulation,
                    // A landed swarm is fully mobilized: Organization is a 0-100 percentage and the
                    // whole brood feeds and fights, so the beachhead starts at 100. (This was 1,
                    // written when 1 was mistaken for "100%"; at the true scale that left only 1% of
                    // the swarm eating/attacking — the cause of the glacial post-landing consumption.)
                    // Restraint on spread comes from Entrenchment=0 (raiders, not dug-in) and the
                    // finite stranded biomass budget, not from throttling how much of the swarm acts.
                    Organization = 100,
                    Entrenchment = 0,
                    ListeningPost = 0,
                    AntiAir = 0
                    // No GrowthMultiplier throttle: the invader's simulation behavior governs its
                    // population change. Winnability
                    // comes from the finite, stranded biomass budget, not a growth throttle.
                };
                region.RegionFactionMap[invaderFaction.Id] = invader;
            }
        }

        // Invader per-region starting population: the planet's whole pre-stamp Imperial garrison
        // scaled by InvaderGarrisonStrengthMultiple, then split evenly across the stamped regions
        // (§8). Garrison rather than civilian population because the opening's invader strength is
        // an army-against-army ratio.
        // Returns at least 1 so a stamped region is never empty even on a tiny world.
        private static long ScaledInvaderStrength(
            Planet promised,
            GameRulesData data,
            ScenarioProfile profile,
            int regionCount)
        {
            List<RegionFaction> imperialRegions = promised.Regions
                .Where(r => r.RegionFactionMap.ContainsKey(data.DefaultFaction.Id))
                .Select(r => r.RegionFactionMap[data.DefaultFaction.Id])
                .ToList();
            if (imperialRegions.Count == 0 || regionCount <= 0)
            {
                return 1L;
            }
            long planetaryGarrison = imperialRegions.Sum(rf => rf.Garrison);
            double totalStrength = planetaryGarrison * profile.InvaderGarrisonStrengthMultiple;
            return Math.Max(1L, (long)(totalStrength / regionCount));
        }

        // §3.3 — park the chapter in orbit. Squads stay embarked (no CurrentRegion, no
        // LandedSquads); the player's first action is to land them via the Planet Tactical screen.
        // "Embarked" is a real ship assignment, not just the absence of a region: the Planet
        // Tactical screen's landing/loading actions both pivot off a squad's current state
        // (BoardedLocation to land, a region's LandedSquads to load) and have no path for a
        // squad that is in neither, so every squad must be placed onto a ship here.
        private static void PlaceFleetInOrbit(Sector sector, PlayerForce playerForce, Planet promised)
        {
            List<Ship> playerShips = playerForce.Fleet.TaskForces
                .SelectMany(taskForce => taskForce.Ships)
                .ToList();
            Ship flagship = new FlagshipService().SelectInitialFlagship(
                playerForce.Faction, playerShips);
            AdministrativeStationResult stationResult = new AdministrativeStationService()
                .SeatAll(playerForce.Army.OrderOfBattle, flagship);
            if (!stationResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Unable to seat Chapter administrative formations: {stationResult.Message}");
            }

            // Administrative stations consume the same ship capacity as their unposted members,
            // but they are not loaded combat squads. Seat them before embarking manoeuvre
            // formations so the embark pass fills only the capacity that remains available.
            IEnumerator<Squad> squads = playerForce.Army.SquadMap.Values
                .Where(s => s.CanMoveAsFormation && s.Members.Count > 0).GetEnumerator();
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
                .Where(p => p.GetControllingFaction()?.IsDefaultFaction == true && p.Governor != null)
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
        private static string ComposeBriefing(
            Sector sector,
            Planet promised,
            Character authority,
            GovernanceTier authorityTier,
            PlayerForce playerForce,
            Faction invader,
            Date currentDate)
        {
            string chapterName = playerForce.Army.OrderOfBattle.Name;
            string authorityTitle = BriefingComposer.GetAuthorityTitle(authorityTier);
            string enemyName = invader.Name;
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
        // Subsector.Name is derived during governance assignment as "{Capital} Subsector",
        // with a stable fallback when no capital is seated.
        private static string ResolveSubsectorName(Sector sector, Planet promised)
        {
            Subsector subsector = sector.Subsectors.FirstOrDefault(s => s.Planets.Contains(promised));
            return subsector?.Name ?? promised.Name;
        }
    }
}
