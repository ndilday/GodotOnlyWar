using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
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

        /// <param name="missionDaysBySquad">
        /// Days each squad actually spent on a mission this turn, keyed by squad id. Days a mission did
        /// NOT need convert to training credit: previously any squad with an order got nothing at all, so
        /// a two-day mission cost a full week of development. Credit is unconditional - a force that
        /// aborted on day one banks nearly the whole week, because failure is an argument for more
        /// training rather than less. A squad absent from the map with a standing order (patrol, defence,
        /// construction, a show of force) occupies its whole week and earns nothing, as before.
        /// </param>
        internal void TrainNonDeployedPlayerForces(
            Sector sector,
            IReadOnlyDictionary<int, int> missionDaysBySquad = null)
        {
            ISoldierTrainingService trainingService = _trainingService ?? CreateTrainingService();
            List<Squad> squads = (sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>()).ToList();

            List<Squad> scoutSquads = squads.Where(s => IsScoutSquad(s) && CanTrainThisCampaignWeek(s)).ToList();
            Dictionary<int, TrainingFocuses> scoutFocusMap = scoutSquads.ToDictionary(s => s.Id, s => s.TrainingFocus);
            Dictionary<int, SoldierProgressLog.ProgressSnapshot> scoutSnapshots =
                scoutSquads.ToDictionary(s => s.Id, s => SoldierProgressLog.Capture(s.Members));
            Dictionary<int, float> scoutPoints = BuildLeftoverPoints(scoutSquads, missionDaysBySquad);
            trainingService.TrainScouts(scoutSquads, scoutFocusMap, WeeklyTrainingPoints, scoutPoints);
            foreach (Squad squad in scoutSquads)
            {
                SoldierProgressLog.LogDelta(
                    $"Training XP [scout drills] {squad.Name}", squad.Members, scoutSnapshots[squad.Id]);
            }

            foreach (Squad squad in squads.Where(s => !IsScoutSquad(s) && CanTrainThisCampaignWeek(s)))
            {
                float points = LeftoverTrainingPoints(squad, missionDaysBySquad);
                if (points <= 0f) continue;

                SoldierProgressLog.ProgressSnapshot before = SoldierProgressLog.Capture(squad.Members);
                foreach (ISoldier soldier in squad.Members)
                {
                    trainingService.ApplySoldierWorkExperience(soldier, squad, points);
                }
                SoldierProgressLog.LogDelta(
                    $"Training XP [garrison] {squad.Name}", squad.Members, before);
            }
        }

        // Only squads that actually ran a mission get an entry, so TrainScouts keeps its original rule
        // for everyone else.
        private static Dictionary<int, float> BuildLeftoverPoints(
            IEnumerable<Squad> squads,
            IReadOnlyDictionary<int, int> missionDaysBySquad)
        {
            Dictionary<int, float> points = new();
            if (missionDaysBySquad == null) return points;
            foreach (Squad squad in squads)
            {
                if (!missionDaysBySquad.ContainsKey(squad.Id)) continue;
                points[squad.Id] = LeftoverTrainingPoints(squad, missionDaysBySquad);
            }
            return points;
        }

        private static float LeftoverTrainingPoints(
            Squad squad,
            IReadOnlyDictionary<int, int> missionDaysBySquad)
        {
            if (missionDaysBySquad != null
                && missionDaysBySquad.TryGetValue(squad.Id, out int daysUsed))
            {
                int spare = Math.Max(0, MissionContext.MissionDurationDays - daysUsed);
                return WeeklyTrainingPoints * spare / MissionContext.MissionDurationDays;
            }
            // No mission context for this squad: either it is idle (full week of drill) or it is holding
            // a standing order that occupies the whole week (no drill at all).
            return squad.CurrentOrders == null ? WeeklyTrainingPoints : 0f;
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
            Dictionary<int, SoldierProgressLog.ProgressSnapshot> scoutSnapshots =
                scoutSquads.ToDictionary(s => s.Id, s => SoldierProgressLog.Capture(s.Members));
            trainingService.TrainScouts(scoutSquads, scoutFocusMap, points);
            foreach (Squad squad in scoutSquads)
            {
                SoldierProgressLog.LogDelta(
                    $"Training XP [scout drills, {subjectiveWeeks:F1}w warp] {squad.Name}",
                    squad.Members, scoutSnapshots[squad.Id]);
            }

            foreach (Squad squad in embarkedSquads.Where(squad => !IsScoutSquad(squad)))
            {
                SoldierProgressLog.ProgressSnapshot before = SoldierProgressLog.Capture(squad.Members);
                foreach (ISoldier soldier in squad.Members)
                {
                    trainingService.ApplySoldierWorkExperience(soldier, squad, points);
                }
                SoldierProgressLog.LogDelta(
                    $"Training XP [garrison, {subjectiveWeeks:F1}w warp] {squad.Name}",
                    squad.Members, before);
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
