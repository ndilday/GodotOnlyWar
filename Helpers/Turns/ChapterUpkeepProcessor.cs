using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves the Chapter's weekly medical recovery and training. Fleet travel delegates
    /// warp-subjective training here so campaign-week and warp-time training share the same
    /// rules and injected training service.
    /// </summary>
    internal sealed class ChapterUpkeepProcessor
    {
        private const float WeeklyTrainingPoints = 0.2f;
        private readonly GameSession _session;
        private readonly ISoldierTrainingService _trainingService;

        internal ChapterUpkeepProcessor(
            GameSession session,
            ISoldierTrainingService trainingService = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _trainingService = trainingService;
        }

        // Weekly medical resolution: wounds knit closed over time for the whole chapter
        // (deployed or not -- a week passes for everyone), except locations that require a
        // replacement procedure.
        internal void ProcessMedical(Sector sector)
        {
            Army army = sector.PlayerForce?.Army;
            if (army == null)
            {
                return;
            }

            MedicalTurnProcessor.ApplyWeeklyHealing(army.OrderOfBattle?.GetAllMembers());
            MedicalTurnProcessor.ResolveProcedures(army.MedicalProcedures, army.PlayerSoldierMap);
        }

        internal void TrainNonDeployedPlayerForces(Sector sector)
        {
            ISoldierTrainingService trainingService = _trainingService ?? CreateTrainingService();
            List<Squad> squads = (sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>()).ToList();

            List<Squad> scoutSquads = squads.Where(s => IsScoutSquad(s) && CanTrainThisCampaignWeek(s)).ToList();
            Dictionary<int, TrainingFocuses> scoutFocusMap = scoutSquads.ToDictionary(s => s.Id, s => s.TrainingFocus);
            trainingService.TrainScouts(scoutSquads, scoutFocusMap, WeeklyTrainingPoints);

            foreach (Squad squad in squads.Where(s => !IsScoutSquad(s) && CanTrainThisCampaignWeek(s)))
            {
                if (squad.CurrentOrders != null) continue;

                foreach (ISoldier soldier in squad.Members)
                {
                    trainingService.ApplySoldierWorkExperience(soldier, squad, WeeklyTrainingPoints);
                }
            }
        }

        internal void ApplyWarpSubjectiveTraining(TaskForce taskForce, double subjectiveWeeks)
        {
            if (subjectiveWeeks <= 0) return;

            ISoldierTrainingService trainingService = _trainingService ?? CreateTrainingService();
            List<Squad> embarkedSquads = taskForce.Ships
                .SelectMany(ship => ship.LoadedSquads)
                .Where(squad => squad.CurrentOrders == null)
                .ToList();
            float points = (float)(WeeklyTrainingPoints * subjectiveWeeks);

            List<Squad> scoutSquads = embarkedSquads.Where(IsScoutSquad).ToList();
            Dictionary<int, TrainingFocuses> scoutFocusMap = scoutSquads.ToDictionary(s => s.Id, s => s.TrainingFocus);
            trainingService.TrainScouts(scoutSquads, scoutFocusMap, points);

            foreach (Squad squad in embarkedSquads.Where(squad => !IsScoutSquad(squad)))
            {
                foreach (ISoldier soldier in squad.Members)
                {
                    trainingService.ApplySoldierWorkExperience(soldier, squad, points);
                }
            }
        }

        internal static bool CanTrainThisCampaignWeek(Squad squad)
        {
            return squad.BoardedLocation?.Fleet?.TravelPhase != FleetTravelPhase.InWarp;
        }

        internal static bool IsScoutSquad(Squad squad)
        {
            return (squad.SquadTemplate.SquadType & SquadTypes.Scout) == SquadTypes.Scout;
        }

        private ISoldierTrainingService CreateTrainingService()
        {
            GameRulesData rules = _session.Rules;
            RatingCalculator ratingCalculator = new(rules.RatingDefinitions, rules.RatingAwardTiers,
                                                    rules.BaseSkillMap, _session.Random);
            return new SoldierTrainingCalculator(rules.BaseSkillMap.Values, rules.TrainingProfiles.Values,
                                                 ratingCalculator);
        }
    }
}
