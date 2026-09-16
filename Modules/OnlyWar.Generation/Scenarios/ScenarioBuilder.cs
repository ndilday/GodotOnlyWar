using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Generation.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Events;
using OnlyWar.Runtime.Naming;

namespace OnlyWar.Generation.Scenarios
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
            Sector sector, GameRulesData data, Date currentDate, GenerationSupport support,
            PlayerForce playerForce, List<Planet> planetList, List<Character> characterList,
            NameGenerator nameGenerator,
            ScenarioFactionSelection invaderSelection = null)
        {
            invaderSelection ??= ScenarioFactionSelection.Default;
            ScenarioProfile profile = SelectPromisedWorldProfile(
                data, invaderSelection, out Faction invader);
            ScenarioInfiltratorOverride infiltratorOverride = profile.InfiltratorOverride;
            Faction infiltrator = infiltratorOverride == null
                ? null
                : data.Factions.First(faction => faction.Id == infiltratorOverride.FactionId);

            // The opening plays out as a timed sequence during generation rather than being stamped
            // as a static board (Design/Reference/OpeningScenario.md): the
            // world the player inherits is emergent — sometimes a fresh invader beachhead, sometimes a
            // month-eaten ruin — from the same simulation that runs during play. All draws come from
            // the already-seeded RNG stream, so seed + scenario still reproduces the same opening.
            Planet promised = SelectPromisedWorld(planetList, data, profile);

            // Seed the hidden infiltrator, pull it up to landing-site strength (this world was
            // chosen because its infiltrator is deep and ready), then have it rise in open revolt.
            if (infiltrator != null)
            {
                EnsureInfiltrator(promised, infiltrator, infiltratorOverride);
                StrengthenPromisedWorldInfiltrator(promised, infiltrator, infiltratorOverride);
                RevealInfiltrator(promised, infiltrator, support);
                SeedPromisedWorldInfiltratorIntel(promised, infiltrator, infiltratorOverride);
            }

            // Both planet-scoped sims run through the warm-up port (no player upkeep, no other
            // planets, no scenario resolution). The implementation builds a session over the
            // candidate sector, so warm-up never needs it published as the active campaign, and
            // generation never references turn simulation (SB-09).
            ICandidateWarmupSimulator warmup = support.Warmup;
            warmup.BeginCandidate(sector, data, currentDate);

            // Pre-landing: the revealed infiltrator wars against the host faction, weakening the
            // defenders the invader will land into.
            if (infiltrator != null)
            {
                warmup.SimulatePlanetForward(
                    sector, promised, infiltratorOverride.PreLandingTurns);
            }

            // The authored beachhead makes planetfall onto the now-weakened board.
            StampInvaderPresence(promised, data, invader, profile);
            if (FactionCapabilities.GeneratesInvasions(invader))
            {
                support.Seeding.EstablishOpeningInvasion(sector, promised, invader);
            }

            // Post-landing: the stranded swarm eats and spreads for a Gaussian-random stretch (the
            // Navy strands it — no reinforcement mechanism exists) before the player arrives.
            warmup.SimulatePlanetForward(sector, promised, PostLandingTurns(profile));

            // The player arrives last, into whatever state the sims produced.
            PlaceFleetInOrbit(sector, playerForce, promised, support);
            Character authority = ResolveAuthority(sector, planetList, characterList, data,
                                                   support.Identity,
                                                   nameGenerator,
                                                   out GovernanceTier authorityTier);
            string briefingText = ComposeBriefing(sector, promised, authority, authorityTier,
                                                  playerForce, invader, currentDate, support);

            PlayerSoldier chapterMaster = playerForce.Army.OrderOfBattle
                .GetAllMembers()
                .OfType<PlayerSoldier>()
                .FirstOrDefault(soldier => soldier.Template == data.ChapterDoctrine.ChapterMaster);
            support.Narrative.RecordChapterFounding(
                playerForce,
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
                authority.Id,
                invaderFactionId: invader.Id);
        }

        private static ScenarioProfile SelectPromisedWorldProfile(
            GameRulesData data,
            ScenarioFactionSelection selection,
            out Faction primaryFaction)
        {
            IReadOnlyList<ScenarioProfile> profiles = data.ScenarioProfiles.GetForScenario(
                ScenarioKeys.PromisedWorld);
            if (profiles.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Scenario '{ScenarioKeys.PromisedWorld}' has no eligible profiles.");
            }

            ScenarioProfile selected;
            if (selection.FactionId.HasValue)
            {
                selected = profiles.FirstOrDefault(profile =>
                    profile.PrimaryFactionId == selection.FactionId.Value);
                if (selected == null)
                {
                    throw new InvalidOperationException(
                        $"Faction id {selection.FactionId.Value} is not eligible for the "
                        + $"'{ScenarioKeys.PromisedWorld}' primary faction.");
                }
            }
            else if (selection.IsRandom)
            {
                selected = SelectWeightedScenarioProfile(profiles);
            }
            else
            {
                // The sector role remains the stable default primary faction. The fallback keeps
                // hand-built/test rules usable when they omit that role's profile.
                selected = profiles.FirstOrDefault(profile =>
                    profile.PrimaryFactionId == data.SectorFactions.Invader.Id)
                    ?? profiles[0];
            }

            primaryFaction = data.Factions.First(faction => faction.Id == selected.PrimaryFactionId);
            return selected;
        }

        private static ScenarioProfile SelectWeightedScenarioProfile(
            IReadOnlyList<ScenarioProfile> profiles)
        {
            if (profiles.Count == 1)
            {
                return profiles[0];
            }

            double totalWeight = profiles.Sum(profile => profile.PrimarySelectionWeight);
            double roll = RNG.GetLinearDouble() * totalWeight;
            foreach (ScenarioProfile profile in profiles)
            {
                if (roll < profile.PrimarySelectionWeight)
                {
                    return profile;
                }
                roll -= profile.PrimarySelectionWeight;
            }

            return profiles[^1];
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

        // Pulls the promised world's infiltrator up to landing-site strength: in each region the
        // infiltrator takes the override's strength fraction of the combined
        // population and garrison, carving the increase out of the Imperial owner — the deep
        // infiltration that hollowed out this world's PDF and drew the swarm (§4.24). Only ever adds
        // to the infiltrator (a region where a random roll already seeded a larger presence is left alone).
        private static void StrengthenPromisedWorldInfiltrator(
            Planet promised,
            Faction infiltrator,
            ScenarioInfiltratorOverride infiltratorOverride)
        {
            Faction imperialFaction = promised.Regions
                .SelectMany(region => region.RegionFactionMap.Values)
                .Select(regionFaction => regionFaction.PlanetFaction.Faction)
                .FirstOrDefault(faction => faction.IsDefaultFaction);
            if (imperialFaction == null) return;

            float share = infiltratorOverride.StrengthFraction;
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
        private static void RevealInfiltrator(
            Planet promised, Faction infiltrator, GenerationSupport support)
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
                    support.Seeding.RevealRegionFaction(infiltratorRegionFaction);
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
            ScenarioInfiltratorOverride infiltratorOverride)
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
                    infiltratorOverride.StartingIntel);

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
            ScenarioInfiltratorOverride infiltratorOverride)
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
                    infiltratorOverride.InitialPopulationShareMin,
                    infiltratorOverride.InitialPopulationShareMax,
                    infiltratorOverride.InitialGarrisonPerPopulation));
        }

        // §3.2 — confine the selected invader to a contiguous cluster of N regions, leaving the rest of
        // the world default-Imperial. Each stamped region gets a public invader RegionFaction
        // with tuned strength. The existing Imperial presence is deliberately left intact and
        // public: planetfall creates a contested region immediately, and ordinary combat/turn
        // processing decides whether the defenders later hold, break, or go to ground.
        internal static void StampInvaderPresence(
            Planet promised,
            GameRulesData data,
            Faction invaderFaction,
            ScenarioProfile profile)
        {
            if (!promised.PlanetFactionMap.TryGetValue(invaderFaction.Id, out PlanetFaction invaderPlanetFaction))
            {
                invaderPlanetFaction = new PlanetFaction(invaderFaction);
                promised.PlanetFactionMap[invaderFaction.Id] = invaderPlanetFaction;
                promised.NotifyPlanetFactionAdded(invaderPlanetFaction);
            }
            // The Navy already identified the incursion; the world is known to be invaded.
            invaderPlanetFaction.IsPublic = true;

            GrantHomeGroundAwareness(promised, data);

            int regionCount = RNG.GetIntBelowMax(
                profile.MinInvaderRegions, profile.MaxInvaderRegions + 1);
            int startIndex = RNG.GetIntBelowMax(0, promised.Regions.Length);

            // Land where there is room to land. The cluster used to start at the drawn index
            // regardless, and each region's allocation was then clamped to its own spare capacity —
            // so on a world whose regions sit near capacity the invader simply evaporated. On seed 1
            // that meant a designed force of 3,856 (twice the planetary PDF, per
            // InvaderGarrisonStrengthMultiple) arriving as 593: one real beachhead of 560, a token
            // 33, and a third region stamped with nothing at all, which the first turn then deleted
            // as an empty presence. The scan starts from the drawn index so equally roomy worlds
            // still vary.
            int bestStart = startIndex;
            long bestHeadroom = -1L;
            for (int offset = 0; offset < promised.Regions.Length; offset++)
            {
                int candidateStart = (startIndex + offset) % promised.Regions.Length;
                long headroom = 0L;
                for (int i = 0; i < regionCount; i++)
                {
                    Region candidate = promised.Regions[
                        (candidateStart + i) % promised.Regions.Length];
                    headroom += Math.Max(0L, candidate.CarryingCapacity - candidate.Population);
                }
                if (headroom > bestHeadroom)
                {
                    bestHeadroom = headroom;
                    bestStart = candidateStart;
                }
            }
            startIndex = bestStart;

            // Size the invader relative to the world's own host garrison (measured before the stamp), so the
            // fight scales across the wide promised-world population band rather than being fixed by
            // an absolute headcount that is meaningless on a hive-scale world (§8).
            // The region count is drawn first because the planetary total is split across it; the
            // Draw order is unchanged because ScaledInvaderStrength consumes no randomness.
            long invaderPopulation = ScaledInvaderStrength(
                promised, data, profile, regionCount);

            // The authored planetary force. Every point of it is landed: what a region cannot absorb
            // is carried to the next, and whatever is still unplaced after all of them is shared out
            // among the beachheads even though that puts them over capacity. An army arriving from
            // orbit does not size itself to the farmland it lands on; over-capacity regions are
            // resolved afterwards by the ordinary consumption and starvation rules.
            long carriedShortfall = 0L;

            // Roomiest first, so the carry-forward lands as much as possible inside capacity before
            // anything has to overflow.
            List<RegionFaction> beachheads = Enumerable.Range(0, regionCount)
                .Select(i => promised.Regions[(startIndex + i) % promised.Regions.Length])
                .OrderByDescending(region => Math.Max(0L, region.CarryingCapacity - region.Population))
                .ThenBy(region => region.Id)
                .Select(region => EnsureInvaderPresence(region, invaderPlanetFaction, invaderFaction))
                .ToList();

            foreach (RegionFaction invader in beachheads)
            {
                // Each beachhead still wants its authored share; only what a region cannot absorb
                // moves on. Taking headroom greedily instead would let the roomiest region swallow
                // the whole force and leave its neighbours empty, which is the failure the old
                // clamp produced from the other direction.
                long wanted = invaderPopulation + carriedShortfall;

                // The invader faction's own presence is already counted in Region.Population when it
                // was seeded dormant, so headroom is measured against the region as it stands.
                long headroom = Math.Max(0L,
                    invader.Region.CarryingCapacity - invader.Region.Population);
                long placed = Math.Min(wanted, headroom);
                if (placed > 0) invader.AddMilitaryStrength(placed);
                carriedShortfall = wanted - placed;
            }

            // No room left anywhere: the rest comes down on the beachheads regardless. This is what
            // guarantees the authored planetary total actually lands, and it is why a beachhead is
            // never stamped empty even when its ground was already full.
            if (carriedShortfall > 0 && beachheads.Count > 0)
            {
                long share = carriedShortfall / beachheads.Count;
                long remainder = carriedShortfall - share * beachheads.Count;
                for (int i = 0; i < beachheads.Count; i++)
                {
                    long overflow = share + (i < remainder ? 1 : 0);
                    if (overflow > 0) beachheads[i].AddMilitaryStrength(overflow);
                }
            }
        }

        // What a world's own defence force knows about its own ground when the invasion begins. One
        // point of awareness is one significant figure of precision
        // (FactionIntelligenceRules.CoarsenEstimate), which is the difference between reading an
        // ork warband of 1,285 as "under two thousand" and reading it as "under ten thousand".
        private const float HomeGroundAwareness = 1.0f;

        /// <summary>
        /// Gives the world's own defence force a working knowledge of every region on it.
        /// </summary>
        /// <remarks>
        /// Awareness of zero means "has never looked at this ground", which is the wrong starting
        /// point for a garrison that has held the planet for generations. Left at zero, the PDF
        /// reads every invader at the top of its decade, sizes its reserve against that, commits
        /// every region to holding, and is then left with no spare force to scout with - and
        /// scouting is the only thing that would correct the estimate. It plans itself into
        /// paralysis on turn one and cannot plan its way out.
        ///
        /// This is a floor, not an assignment: a region already better known keeps what it has. It
        /// also decays like any other awareness, so it is a starting position rather than a
        /// permanent grant - the PDF still has to look after itself.
        /// </remarks>
        private static void GrantHomeGroundAwareness(Planet promised, GameRulesData data)
        {
            if (data?.DefaultFaction == null) return;
            if (!promised.PlanetFactionMap.TryGetValue(
                    data.DefaultFaction.Id,
                    out PlanetFaction defender))
            {
                return;
            }

            foreach (Region region in promised.Regions.Where(region => region != null))
            {
                if (defender.GetRegionAwareness(region) < HomeGroundAwareness)
                {
                    defender.SetRegionAwareness(region, HomeGroundAwareness);
                }
            }
        }

        /// <summary>
        /// The invader's public presence in one stamped region, created if this is planetfall and
        /// reused when a dormant presence was already seeded there.
        /// </summary>
        /// <remarks>
        /// An explicit invasion opening incorporates a naturally seeded dormant presence rather than
        /// replacing that indelible object, so the beachhead allocation is added on top of whatever
        /// is already there.
        /// </remarks>
        private static RegionFaction EnsureInvaderPresence(
            Region region,
            PlanetFaction invaderPlanetFaction,
            Faction invaderFaction)
        {
            RegionFaction invader = region.RegionFactionMap.GetValueOrDefault(invaderFaction.Id);
            if (invader == null)
            {
                invader = new RegionFaction(invaderPlanetFaction, region)
                {
                    IsPublic = true,
                    Entrenchment = 0,
                    ListeningPost = 0,
                    AntiAir = 0
                    // No GrowthMultiplier throttle: the invader's simulation behavior governs
                    // its population change. Winnability comes from the finite, stranded
                    // biomass budget, not a growth throttle.
                };
                region.RegionFactionMap[invaderFaction.Id] = invader;
            }

            invader.IsPublic = true;
            invader.Organization = 100;
            invader.DormantConsolidation = FactionCapabilities.GeneratesInvasions(invaderFaction)
                ? 1.0
                : invader.DormantConsolidation;
            return invader;
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
        private static void PlaceFleetInOrbit(
            Sector sector, PlayerForce playerForce, Planet promised, GenerationSupport support)
        {
            List<Ship> playerShips = playerForce.Fleet.TaskForces
                .SelectMany(taskForce => taskForce.Ships)
                .ToList();
            Ship flagship = support.Fleet.SelectInitialFlagship(playerForce.Faction, playerShips);
            support.Fleet.SeatAdministrativeFormations(playerForce.Army.OrderOfBattle, flagship);

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
                                                  IPersistentIdAllocator identity,
                                                  NameGenerator nameGenerator,
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
            Character authority = CharacterBuilder.GenerateCharacter(
                identity.GetNextCharacterId(), data.DefaultFaction, nameGenerator);
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
            Date currentDate,
            GenerationSupport support)
        {
            string chapterName = playerForce.Army.OrderOfBattle.Name;
            string authorityTitle = support.Narrative.GetAuthorityTitle(authorityTier);
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

            string briefingText = support.Narrative.ComposeOpeningBriefing(
                tokens, FactionCapabilities.GeneratesInvasions(invader));

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
