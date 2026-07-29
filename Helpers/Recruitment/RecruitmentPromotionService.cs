using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Recruitment
{
    internal sealed record RecruitmentPromotionResult(
        bool Succeeded,
        string Message,
        int? SoldierId = null);

    internal sealed record BlackCarapacePlanResult(
        bool Succeeded,
        string Message,
        int? ApothecarySoldierId = null,
        string ApothecaryName = null,
        float GeneticCompatibility = 0);

    /// <summary>
    /// Handles the two player-directed boundaries in the recruitment pipeline:
    /// Phase 12 aspirant placement is an immediate administrative promotion, while
    /// Phase 13 reserves an Apothecary and Devastator seat for a one-week procedure.
    /// </summary>
    internal sealed class RecruitmentPromotionService
    {
        private const float BlackCarapaceReadinessRangedRating = 105f;

        private readonly GameSession _session;

        internal RecruitmentPromotionService(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal RecruitmentPromotionResult PromoteAspirantToNeophyte(
            int aspirantId,
            int scoutSquadId)
        {
            PlayerForce force = _session.Sector.PlayerForce;
            RecruitmentProgram program = force?.RecruitmentProgram;
            RecruitmentAspirant aspirant = program?.Aspirants
                .FirstOrDefault(item => item.Id == aspirantId);
            if (aspirant == null || aspirant.Phase != RecruitmentPhase.Phase12)
            {
                return Failure("The selected aspirant is not awaiting Phase 12 placement.");
            }

            Squad target = FindSquad(force, scoutSquadId);
            if (!IsEligibleTarget(
                    target,
                    _session.Rules.ChapterTemplates.ScoutSquad,
                    _session.Rules.ChapterTemplates.ScoutMarine,
                    program.HomeWorldPlanetId,
                    out string targetError))
            {
                return Failure(targetError);
            }

            Soldier generated = SoldierFactory.Instance.GenerateNewSoldier(
                _session.Rules.ChapterTemplates.ScoutMarine,
                _session.Random);
            generated.Template = _session.Rules.ChapterTemplates.ScoutMarine;
            generated.Strength = aspirant.Attributes.Strength;
            generated.Constitution = aspirant.Attributes.Constitution;
            generated.Intelligence = aspirant.Attributes.Intelligence;
            generated.Dexterity = aspirant.Attributes.Dexterity;
            generated.Ego = aspirant.Attributes.Ego;
            foreach ((int skillId, float points) in aspirant.SkillPoints)
            {
                if (_session.Rules.BaseSkillMap.TryGetValue(
                        skillId, out BaseSkill baseSkill)
                    && points > 0)
                {
                    generated.AddSkillPoints(baseSkill, points);
                }
            }

            PlayerSoldier neophyte = new(generated, NameGenerator.GetFullName())
            {
                ProgenoidImplantDate = CopyDate(aspirant.PhaseStartedDate),
                GeneticCompatibility = aspirant.GeneticCompatibility,
                RecruitmentBirthDate = CopyDate(aspirant.BirthDate)
            };
            neophyte.AddEvent(new SoldierEvent(
                CopyDate(aspirant.AdmittedDate),
                SoldierEventType.AcceptedToTraining,
                $"accepted as aspirant {aspirant.InductionDesignation}"));
            neophyte.AddEvent(new SoldierEvent(
                CopyDate(_session.CurrentDate),
                SoldierEventType.Promotion,
                $"promoted to Scout Marine and assigned to {target.Name}"));

            target.AddSquadMember(neophyte);
            force.Army.PlayerSoldierMap[neophyte.Id] = neophyte;
            program.Aspirants.Remove(aspirant);
            program.ProgramEvents.Add(new RecruitmentProgramEvent
            {
                Date = CopyDate(_session.CurrentDate),
                Type = RecruitmentEventType.NeophytePromoted,
                Count = 1,
                Detail = $"{neophyte.Name} entered {target.Name} as a neophyte."
            });
            return new RecruitmentPromotionResult(
                true,
                $"{neophyte.Name} has joined {target.Name}.",
                neophyte.Id);
        }

        internal RecruitmentPromotionResult ScheduleBlackCarapace(
            int soldierId,
            int devastatorSquadId)
        {
            BlackCarapacePlanResult plan = EvaluateBlackCarapace(
                soldierId, devastatorSquadId);
            if (!plan.Succeeded)
            {
                return Failure(plan.Message);
            }

            PlayerForce force = _session.Sector.PlayerForce;
            RecruitmentProgram program = force.RecruitmentProgram;
            PlayerSoldier neophyte = force.Army.PlayerSoldierMap[soldierId];

            int id = program.Procedures.Select(procedure => procedure.Id)
                .DefaultIfEmpty(0).Max() + 1;
            program.Procedures.Add(new RecruitmentProcedure
            {
                Id = id,
                SubjectId = soldierId,
                Type = RecruitmentProcedureType.BlackCarapace,
                Phase = RecruitmentPhase.Phase13BlackCarapace,
                Status = RecruitmentProcedureStatus.InProgress,
                AssignedApothecarySoldierId = plan.ApothecarySoldierId.Value,
                WeeksRemaining = 1,
                ReservedSquadId = devastatorSquadId,
                GeneticCompatibility = plan.GeneticCompatibility
            });
            return new RecruitmentPromotionResult(
                true,
                $"{neophyte.Name}'s Black Carapace procedure is scheduled.",
                soldierId);
        }

        internal BlackCarapacePlanResult EvaluateBlackCarapace(
            int soldierId,
            int devastatorSquadId)
        {
            PlayerForce force = _session.Sector.PlayerForce;
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program == null)
            {
                return PlanFailure("The Chapter has no recruitment program.");
            }
            new RecruitmentStaffService().Synchronize(force, _session.Rules);
            if (!force.Army.PlayerSoldierMap.TryGetValue(
                    soldierId, out PlayerSoldier neophyte)
                || neophyte.Template != _session.Rules.ChapterTemplates.ScoutMarine)
            {
                return PlanFailure("Only a Scout Marine can receive the Black Carapace.");
            }
            if (!neophyte.GeneticCompatibility.HasValue)
            {
                return PlanFailure(
                    "This founding Scout does not require a campaign-era Black Carapace procedure.");
            }
            if (neophyte.RecruitmentBirthDate == null)
            {
                return PlanFailure(
                    "The neophyte's induction age record is missing, so surgery cannot be authorized.");
            }
            double age = _session.CurrentDate.GetWeeksDifference(
                neophyte.RecruitmentBirthDate) / 52.0;
            if (!RecruitmentRules.GetPhaseAgeWindow(
                    RecruitmentPhase.Phase13BlackCarapace).Contains(age))
            {
                return PlanFailure(
                    $"The neophyte is {Math.Max(0, age):0.0} years old and is outside "
                    + "the Phase 13 implantation window.");
            }
            if (program.Procedures.Any(procedure =>
                    procedure.Type == RecruitmentProcedureType.BlackCarapace
                    && procedure.SubjectId == soldierId))
            {
                return PlanFailure("That neophyte already has a Black Carapace procedure scheduled.");
            }
            if (neophyte.AssignedSquad?.CurrentOrders != null)
            {
                return PlanFailure("Remove the neophyte's squad from its current orders first.");
            }
            if (GetSquadPlanet(neophyte.AssignedSquad)?.Id
                != program.HomeWorldPlanetId)
            {
                return PlanFailure(
                    "The neophyte must be on or in orbit of the Home World.");
            }
            SoldierEvaluation latest = neophyte.SoldierEvaluationHistory.LastOrDefault();
            if (latest == null
                || latest.RangedRating <= BlackCarapaceReadinessRangedRating)
            {
                return PlanFailure("That neophyte is not yet ready for the Black Carapace.");
            }

            Squad target = FindSquad(force, devastatorSquadId);
            if (!IsEligibleTarget(
                    target,
                    _session.Rules.ChapterTemplates.DevastatorSquad,
                    _session.Rules.ChapterTemplates.DevastatorMarine,
                    program.HomeWorldPlanetId,
                    out string targetError,
                    CountReservedSeats(program, devastatorSquadId)))
            {
                return PlanFailure(targetError);
            }

            RecruitmentStaffAssignment apothecary = program.StaffAssignments
                .Where(staff => staff.Role == RecruitmentStaffRole.Apothecary)
                .Where(staff => !program.Procedures.Any(procedure =>
                    procedure.AssignedApothecarySoldierId == staff.SoldierId))
                .OrderByDescending(staff => staff.MedicalRating)
                .ThenBy(staff => staff.SoldierId)
                .FirstOrDefault();
            if (apothecary == null)
            {
                return PlanFailure("No Administrative Squad Apothecary is available.");
            }

            string apothecaryName = force.Army.PlayerSoldierMap.TryGetValue(
                apothecary.SoldierId, out PlayerSoldier assigned)
                    ? assigned.Name
                    : $"Apothecary {apothecary.SoldierId}";
            return new BlackCarapacePlanResult(
                true,
                null,
                apothecary.SoldierId,
                apothecaryName,
                Math.Clamp(neophyte.GeneticCompatibility.Value, 0, 1));
        }

        internal static bool IsSoldierInBlackCarapaceProcedure(
            RecruitmentProgram program,
            int soldierId) =>
            program?.Procedures.Any(procedure =>
                procedure.Type == RecruitmentProcedureType.BlackCarapace
                && procedure.SubjectId == soldierId) == true;

        private static int CountReservedSeats(
            RecruitmentProgram program,
            int squadId) =>
            program.Procedures.Count(procedure =>
                procedure.Type == RecruitmentProcedureType.BlackCarapace
                && procedure.ReservedSquadId == squadId);

        private static Squad FindSquad(PlayerForce force, int squadId)
        {
            if (force?.Army == null)
            {
                return null;
            }
            force.Army.PopulateSquadMap();
            return force.Army.SquadMap.TryGetValue(squadId, out Squad squad)
                ? squad
                : null;
        }

        private static bool IsEligibleTarget(
            Squad squad,
            SquadTemplate requiredSquadTemplate,
            SoldierTemplate requiredSoldierTemplate,
            int homeWorldPlanetId,
            out string error,
            int reservedSeats = 0)
        {
            if (squad == null
                || !squad.IsOperational
                || squad.SquadTemplate != requiredSquadTemplate)
            {
                error = $"Select an operational {requiredSquadTemplate.Name}.";
                return false;
            }
            Planet planet = GetSquadPlanet(squad);
            if (planet?.Id != homeWorldPlanetId)
            {
                error = $"{squad.Name} must be on or in orbit of the Home World.";
                return false;
            }

            SquadTemplateElement element = squad.SquadTemplate.Elements
                .FirstOrDefault(item => item.SoldierTemplate == requiredSoldierTemplate);
            int assigned = squad.Members.Count(
                member => member.Template == requiredSoldierTemplate);
            if (element == null || assigned + reservedSeats >= element.MaximumNumber)
            {
                error = $"{squad.Name} has no available {requiredSoldierTemplate.Name} seat.";
                return false;
            }
            if (squad.BoardedLocation != null
                && squad.BoardedLocation.AvailableCapacity <= reservedSeats)
            {
                error = $"{squad.BoardedLocation.Name} has no transport capacity for another marine.";
                return false;
            }

            error = null;
            return true;
        }

        private static Planet GetSquadPlanet(Squad squad) =>
            squad?.CurrentRegion?.Planet ?? squad?.BoardedLocation?.Fleet?.Planet;

        private static RecruitmentPromotionResult Failure(string message) =>
            new(false, message);

        private static BlackCarapacePlanResult PlanFailure(string message) =>
            new(false, message);

        private static Date CopyDate(Date date) =>
            date == null ? null : new Date(date.Millenium, date.Year, date.Week);
    }
}
