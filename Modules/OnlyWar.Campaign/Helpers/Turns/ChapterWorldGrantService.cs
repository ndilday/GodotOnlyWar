using OnlyWar.Campaign.Recruitment;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Generation.World;
using OnlyWar.Operations.Abstractions;
using System;
using System.Linq;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// Grants a liberated world to the Chapter as its Chapter World. The Promised World win and an
    /// Establish mandate share this one path (TDD §6.15 *Renouncing and granting*). Opinion
    /// changes are the caller's, because their amounts differ.
    /// </summary>
    internal sealed class ChapterWorldGrantService
    {
        private readonly CampaignTurnContext _turn;
        private readonly IOperationsPersonnelSurface _personnel;
        private readonly IOrderCommitmentSurface _commitments;

        internal ChapterWorldGrantService(
            CampaignTurnContext turn,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
        {
            _turn = turn ?? throw new ArgumentNullException(nameof(turn));
            _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
            _commitments = commitments ?? throw new ArgumentNullException(nameof(commitments));
        }

        internal void Grant(Sector sector, Planet world)
        {
            if (sector?.PlayerForce == null) throw new ArgumentNullException(nameof(sector));
            if (world == null) throw new ArgumentNullException(nameof(world));
            PlayerForce force = sector.PlayerForce;
            ChapterHomeworldService.ReplaceChapterPlanetFaction(world, force.Faction);
            force.HomeWorldPlanetId = world.Id;
            // A Chapter that renounced an earlier world keeps the recruitment program it built
            // there; only a Chapter with no program gets a founding one.
            force.RecruitmentProgram ??= CreateFoundingRecruitmentProgram(world);
            RelocateAdministrativeStationsToHomeWorld(sector, world);
        }

        private RecruitmentProgram CreateFoundingRecruitmentProgram(Planet homeWorld)
        {
            Date established = new(
                _turn.CurrentDate.Millenium,
                _turn.CurrentDate.Year,
                _turn.CurrentDate.Week);
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
                        _turn.Sector.PlayerForce.Faction.Id,
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
            RecruitmentStaffService.EnsureTaskOrder(
                _turn.Sector.PlayerForce,
                program,
                _turn.Sector,
                _turn.Identity);
            return program;
        }

        private void RelocateAdministrativeStationsToHomeWorld(Sector sector, Planet homeWorld)
        {
            Region capital = homeWorld.Regions.FirstOrDefault(
                region => region.Id == homeWorld.CapitalRegionId)
                ?? homeWorld.Regions.First();
            AdministrativeStationResult result = new AdministrativeStationService(
                    _personnel, _commitments)
                .MoveAllToRegion(sector.PlayerForce.Army.OrderOfBattle, capital);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Unable to relocate Chapter administrative stations: {result.Message}");
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
    }
}
