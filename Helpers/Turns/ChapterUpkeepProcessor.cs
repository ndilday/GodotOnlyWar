using OnlyWar.Helpers.Medical;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Events;
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
            PlayerForce force = sector.PlayerForce;
            Army army = force?.Army;
            if (army == null)
            {
                return;
            }

            IEnumerable<ISoldier> members = army.OrderOfBattle?.GetAllMembers();
            Dictionary<int, OpenNearDeathEpisode> openEpisodes = force.CampaignEventLedger
                .OpenNearDeathEpisodes
                .Values
                .ToDictionary(episode => episode.SoldierId);
            Dictionary<int, bool> deployabilityBefore = openEpisodes.Values
                .Where(episode => army.PlayerSoldierMap.ContainsKey(episode.SoldierId))
                .ToDictionary(
                    episode => episode.SoldierId,
                    episode => army.PlayerSoldierMap[episode.SoldierId].IsDeployable);
            // Days outside a mission get their daily pass too. Weeks with no combat orders never
            // enter MissionDayScheduler at all, and even an active week ends its day loop as soon
            // as the last mission finishes, so this is the garrison half of the Astartes daily
            // clear (Design/Reference/CasualtyRealism.md §2.5). One call covers however many quiet
            // days there were, because clearing an already-clear band does nothing.
            //
            // Subsumed by ApplyWeeklyHealing today, which clears Negligible and Minor outright for
            // everyone. Kept explicit so the daily rule does not silently depend on that: it is
            // stated where it belongs and survives any future change to the weekly cascade.
            MedicalTurnProcessor.ApplyDailyHealing(members);
            // GARRISON FIELD CARE (Design/Reference/CasualtyRealism.md §2.6): the Apothecarium at rest,
            // which is where most convalescence actually happens. An Apothecary NOT on a mission
            // treats co-located brothers who are likewise not on a mission, on the same capacity and
            // the same triage as his forward counterpart -- so an Apothecary sent out with an
            // assault is visibly an Apothecary not clearing the backlog at home, and both effects
            // land on the same screen.
            //
            // Run BEFORE the weekly cascade so the week's demotions are in place when it ticks.
            FieldCareService.ApplyGarrisonFieldCare(
                members?.OfType<PlayerSoldier>(),
                FieldCareService.ResolveMedicalSkills(
                    _session.Rules?.RatingDefinitions, _session.Rules?.BaseSkillMap));
            MedicalTurnProcessor.ApplyWeeklyHealing(members);
            IReadOnlyList<CompletedMedicalProcedure> completedProcedures =
                MedicalTurnProcessor.ResolveProcedures(army.MedicalProcedures, army.PlayerSoldierMap);
            RecordCompletedReplacements(force, completedProcedures);
            RecordNearDeathRecoveries(force, openEpisodes, deployabilityBefore, completedProcedures);
        }

        private void RecordCompletedReplacements(
            PlayerForce force,
            IReadOnlyList<CompletedMedicalProcedure> completedProcedures)
        {
            foreach (CompletedMedicalProcedure completion in completedProcedures ?? [])
            {
                OpenNearDeathEpisode episode = force.CampaignEventLedger
                    .GetOpenNearDeathEpisode(completion.Soldier.Id);
                BattleEventContextSnapshot context = episode == null
                    ? null
                    : (force.CampaignEventLedger.GetById(episode.SourceIncapacitationEventId)
                        ?.Payload as IncapacitatedPayload)?.BattleContext;
                force.CampaignEventRecorder.RecordBodyPartReplacement(
                    completion.Soldier,
                    _session.CurrentDate,
                    new BodyPartReplacementPayload(
                        completion.PrimaryHitLocationTemplateId,
                        completion.PrimaryHitLocationName,
                        completion.ProcedureType,
                        completion.WasAlreadyCybernetic,
                        completion.ProcedureDurationWeeks,
                        completion.RequisitionCost,
                        episode?.SourceIncapacitationEventId),
                    context);
            }
        }

        private void RecordNearDeathRecoveries(
            PlayerForce force,
            IReadOnlyDictionary<int, OpenNearDeathEpisode> openEpisodes,
            IReadOnlyDictionary<int, bool> deployabilityBefore,
            IReadOnlyList<CompletedMedicalProcedure> completedProcedures)
        {
            foreach (OpenNearDeathEpisode episode in openEpisodes.Values)
            {
                if (!force.Army.PlayerSoldierMap.TryGetValue(
                        episode.SoldierId,
                        out PlayerSoldier soldier))
                {
                    force.CampaignEventLedger.CloseOpenNearDeathEpisode(
                        episode.SoldierId,
                        episode.SourceIncapacitationEventId);
                    continue;
                }
                if (!deployabilityBefore.TryGetValue(episode.SoldierId, out bool wasDeployable)
                    || wasDeployable
                    || !soldier.IsDeployable)
                {
                    continue;
                }

                CampaignEvent source = force.CampaignEventLedger
                    .GetById(episode.SourceIncapacitationEventId);
                if (source?.Payload is not IncapacitatedPayload incapacitated)
                {
                    continue;
                }
                CompletedMedicalProcedure procedure = (completedProcedures ?? [])
                    .Where(completion => completion.Soldier.Id == soldier.Id)
                    .OrderBy(completion => completion.PrimaryHitLocationTemplateId)
                    .FirstOrDefault();
                NearDeathRecoveryMethod method = procedure?.ProcedureType switch
                {
                    MedicalProcedureType.Cybernetic => NearDeathRecoveryMethod.Cybernetic,
                    MedicalProcedureType.VatGrown => NearDeathRecoveryMethod.VatGrown,
                    _ => NearDeathRecoveryMethod.NaturalOrFieldCare
                };
                int durationWeeks = Math.Max(
                    0,
                    _session.CurrentDate.GetTotalWeeks() - source.OccurredWeek);
                force.CampaignEventRecorder.RecordNearDeathRecovery(
                    soldier,
                    _session.CurrentDate,
                    new NearDeathRecoveryPayload(
                        episode.SourceIncapacitationEventId,
                        durationWeeks,
                        incapacitated.DefiningHitLocationTemplateId,
                        incapacitated.DefiningHitLocationName,
                        method,
                        soldier.Body.HitLocations.Any(location => location.Wounds.WoundTotal > 0),
                        incapacitated.BattleContext));
            }
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

        // The Scout HQ (SquadTypes.HQ | SquadTypes.Scout) carries the Scout flag but is a command
        // element, not a training squad: TrainingUnitScreenController.IsTrainingSquad excludes it, so
        // the player can neither see it on the training screen nor set its TrainingFocus. Matching that
        // exclusion here keeps the two from disagreeing - previously the Scout HQ was silently drilled
        // as scouts every week against an unset focus (which TrainScouts expands to all four areas),
        // and its scout sergeants developed against focus profiles the player never chose. Excluded
        // here, it falls into the garrison work-experience path with every other HQ squad.
        internal static bool IsScoutSquad(Squad squad)
        {
            SquadTypes type = squad.SquadTemplate.SquadType;
            return (type & SquadTypes.Scout) == SquadTypes.Scout
                && (type & SquadTypes.HQ) == 0;
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
