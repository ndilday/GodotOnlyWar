using OnlyWar.Builders;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Helpers.Recruitment;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves campaign-scenario objectives after all other weekly simulation has settled.
    /// </summary>
    internal sealed class ScenarioTurnProcessor
    {
        private readonly GameSession _session;

        internal ScenarioTurnProcessor(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal bool TryResolve(Sector sector, out string notification)
        {
            notification = null;
            CampaignScenario scenario = sector.Scenario;
            if (scenario is not { State: ObjectiveState.Pending })
            {
                return false;
            }

            Planet promised = sector.GetPlanet(scenario.PromisedPlanetId);
            Faction player = sector.PlayerForce.Faction;

            // Both outcomes are measured by who holds ground OPENLY, not by headcount. An earlier
            // rule required every non-Imperial faction on the world to reach zero population and
            // garrison, which no campaign could ever reach: the stamp seeds a Genestealer Cult in
            // all sixteen regions (ScenarioRules.PromisedWorldCultStrengthFraction), a cult driven
            // below the suppression threshold goes to ground with its population intact and can no
            // longer be targeted at all, and organic growth replaces it between turns. The mirror
            // defect sat on the lapse side: the displaced Imperial remnant left by the stamp
            // (ImperialRemnantFraction) is hidden but alive, so "no Imperial presence" never held
            // either. Reading public control instead makes both ends reachable and matches how the
            // rest of the simulation decides who owns a region.
            bool loyalistsHoldGround = HasPublicPresence(promised, FactionRelationshipService.IsImperial);
            if (!loyalistsHoldGround)
            {
                scenario.State = ObjectiveState.Lapsed;
                Character sectorLord = sector.GetSectorLord();
                if (sectorLord != null)
                {
                    sectorLord.OpinionOfPlayerForce -= ScenarioRules.SectorLordOpinionPenalty;
                }

                notification =
                    $"[b]The Promised World is lost.[/b]\n\n{promised.Name} has fallen — no Imperial "
                    + "or Astartes force holds ground there any longer. The promise is withdrawn, and "
                    + "your standing with the Sector Lord suffers for it. The war goes on.";
                return true;
            }

            if (!HasPublicPresence(promised, faction => !FactionRelationshipService.IsImperial(faction)))
            {
                scenario.State = ObjectiveState.Won;
                SectorBuilder.ReplaceChapterPlanetFaction(promised, player);
                sector.PlayerForce.HomeWorldPlanetId = promised.Id;
                sector.PlayerForce.RecruitmentProgram = CreateFoundingRecruitmentProgram(promised);
                SetTenthCompanyHeadquartersAdministrative(sector, promised);
                Character lord = sector.GetSectorLord();
                if (lord != null)
                {
                    lord.OpinionOfPlayerForce += ScenarioRules.SectorLordOpinionReward;
                }

                // Deliberately not "cleansed": a cult that has gone to ground survives the grant,
                // and is now the player's problem on their own Chapter World.
                notification =
                    $"[b]The Promised World is liberated.[/b]\n\nNo enemy holds ground on "
                    + $"{promised.Name} any longer, and the world is granted to your Chapter. It is "
                    + "your home now — hold it in the Emperor's name.";
                return true;
            }

            return false;
        }

        private RecruitmentProgram CreateFoundingRecruitmentProgram(Planet homeWorld)
        {
            Date established = new(
                _session.CurrentDate.Millenium,
                _session.CurrentDate.Year,
                _session.CurrentDate.Week);
            RecruitmentProgram program = new()
            {
                Id = 1,
                HomeWorldPlanetId = homeWorld.Id,
                EstablishedDate = established,
                IsSetupComplete = false,
                WorldType = GetRecruitmentWorldType(homeWorld)
            };

            // The liberation bootstrap represents the surviving male children who are already
            // ages ten through twelve. It is an aggregate pool, not hundreds of thousands of
            // individual records; children become individuals only after passing all screens.
            long chapterPopulation =
                homeWorld.Regions.Sum(region =>
                    region.RegionFactionMap.TryGetValue(
                        _session.Sector.PlayerForce.Faction.Id,
                        out RegionFaction chapterRegion)
                            && chapterRegion.IsPublic
                            ? chapterRegion.Population
                            : 0);
            program.UnscreenedCohorts.Add(new RecruitmentCohort
            {
                Id = 1,
                CreatedDate = established,
                RemainingPopulation =
                    RecruitmentRules.CalculateFoundingCohortPopulation(chapterPopulation),
                MinimumAgeAtCreation = RecruitmentRules.FoundingCohortMinimumAge,
                MaximumAgeAtCreation = RecruitmentRules.FoundingCohortMaximumAge,
                IsFoundingCohort = true
            });
            program.ProgramEvents.Add(new RecruitmentProgramEvent
            {
                Date = established,
                Type = RecruitmentEventType.ProgramEstablished,
                Count = 1,
                Detail = $"The Chapter established its first recruitment program on {homeWorld.Name}."
            });
            return program;
        }

        private void SetTenthCompanyHeadquartersAdministrative(Sector sector, Planet homeWorld)
        {
            var scoutCompany = sector.PlayerForce.Army.OrderOfBattle.ChildUnits
                .FirstOrDefault(unit =>
                    unit.UnitTemplate == _session.Rules.ChapterTemplates.ScoutCompany);
            if (scoutCompany?.HQSquad != null)
            {
                var headquarters = scoutCompany.HQSquad;
                var previousOrder = headquarters.CurrentOrders;
                headquarters.IsAdministrative = true;
                if (previousOrder?.AssignedSquads.Count == 0)
                {
                    sector.RemoveOrder(previousOrder);
                }
                Region capital = homeWorld.Regions.FirstOrDefault(
                    region => region.Id == homeWorld.CapitalRegionId)
                    ?? homeWorld.Regions.First();
                headquarters.CurrentRegion = capital;
                if (capital.RegionFactionMap.TryGetValue(
                    sector.PlayerForce.Faction.Id, out RegionFaction chapterPresence)
                    && !chapterPresence.LandedSquads.Contains(headquarters))
                {
                    chapterPresence.LandedSquads.Add(headquarters);
                }
            }
        }

        private static RecruitmentWorldType GetRecruitmentWorldType(Planet planet)
        {
            return planet?.Template?.Name switch
            {
                "Feral" => RecruitmentWorldType.Feral,
                "Death" => RecruitmentWorldType.Death,
                _ => RecruitmentWorldType.Standard
            };
        }

        // Whether any faction matching the predicate openly holds ground somewhere on the world.
        // Hidden presences are excluded on purpose: an infiltrator that has gone to ground is not
        // holding the region, and a displaced remnant in hiding is not defending it.
        private static bool HasPublicPresence(Planet planet, Func<Faction, bool> factionMatches)
        {
            return planet.Regions.Any(region =>
                region.RegionFactionMap.Values.Any(regionFaction =>
                    regionFaction.IsPublic
                    && factionMatches(regionFaction.PlanetFaction.Faction)
                    && (regionFaction.Population > 0 || regionFaction.Garrison > 0)));
        }
    }
}
