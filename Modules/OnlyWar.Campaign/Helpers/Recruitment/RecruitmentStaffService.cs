using System;
using OnlyWar.Medical.Readiness;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Soldiers.Ratings;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Operations.Orders;
using OnlyWar.Domain.Planets;

namespace OnlyWar.Campaign.Recruitment
{
    /// <summary>
    /// Projects the members of the 10th Company administrative HQ into the staffing
    /// values used by the recruitment simulation. The Chapter screen remains the sole
    /// reassignment UI: moving an eligible brother into or out of that squad changes the
    /// program the next time it is previewed or processed.
    /// </summary>
    public sealed class RecruitmentStaffService
    {
        public Squad GetAdministrativeSquad(PlayerForce force, GameRulesData rules)
        {
            return force?.Army?.OrderOfBattle?.GetAllSquads()
                .SingleOrDefault(squad => squad.SquadTemplate
                    == rules?.ChapterDoctrine?.ScoutCompanyHeadquarters);
        }

        public void Synchronize(PlayerForce force, GameRulesData rules, Sector sector,
            IReadinessDecisions readiness = null,
            IPersistentIdAllocator identity = null)
        {
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program == null)
            {
                return;
            }

            Order taskOrder = EnsureTaskOrder(force, program, sector, identity);
            program.StaffAssignments.Clear();
            Squad administrative = GetAdministrativeSquad(force, rules);
            if (administrative == null)
            {
                return;
            }

            foreach (PlayerSoldier soldier in administrative.Members.OfType<PlayerSoldier>())
            {
                RecruitmentStaffRole? role = ResolveRole(soldier, rules);
                bool eligible = role.HasValue
                    && soldier.IsCombatEffective
                    && CampaignLocationService.AreCoLocated(soldier, administrative)
                    && (soldier.CurrentOrder == null
                        || ReferenceEquals(soldier.CurrentOrder, taskOrder));
                if (ReferenceEquals(soldier.CurrentOrder, taskOrder) && !eligible)
                {
                    OrderForceService.RemoveCharacter(taskOrder, soldier);
                }
                if (!eligible)
                {
                    continue;
                }
                if (taskOrder != null && soldier.CurrentOrder == null
                    && !OrderForceService.AssignCharacter(
                        taskOrder,
                        soldier,
                        readiness ?? throw new ArgumentNullException(nameof(readiness))))
                {
                    continue;
                }
                if (!ReferenceEquals(soldier.CurrentOrder, taskOrder)) continue;

                // The Captain is the Master of Recruitment, but is not one of the
                // throughput-producing staff posts charged by the weekly program.

                SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory.LastOrDefault();
                program.StaffAssignments.Add(new RecruitmentStaffAssignment(
                    soldier.Id,
                    role.Value,
                    rules.RatingConsumers.Get(evaluation, RatingConsumerRole.CommandLeadership),
                    rules.RatingConsumers.Get(evaluation, RatingConsumerRole.MedicalCapacity),
                    rules.RatingConsumers.Get(evaluation, RatingConsumerRole.SpiritualCapability)));
            }
        }

        public static Order EnsureTaskOrder(
            PlayerForce force,
            RecruitmentProgram program,
            Sector sector,
            IPersistentIdAllocator identity = null)
        {
            if (force == null || program == null || sector == null)
            {
                return program?.TaskOrder;
            }
            if (program.TaskOrder != null)
            {
                sector.AddNewOrder(program.TaskOrder);
                return program.TaskOrder;
            }

            Planet homeWorld = sector.GetPlanet(program.HomeWorldPlanetId);
            Region capital = homeWorld?.Regions.FirstOrDefault(region =>
                region.Id == homeWorld.CapitalRegionId) ?? homeWorld?.Regions.FirstOrDefault();
            if (capital == null) return null;

            Order existing = sector.Orders.Values.FirstOrDefault(order =>
                order.Mission?.MissionType == MissionType.Recruitment
                && order.OwnerFaction == force.Faction
                && order.Mission.Region == capital);
            if (existing != null)
            {
                program.TaskOrder = existing;
                return existing;
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            program.TaskOrder = new Order(
                identity.GetNextOrderId(),
                [],
                isQuiet: true,
                isActivelyEngaging: false,
                Aggression.Avoid,
                new Mission(identity.GetNextMissionId(), MissionType.Recruitment, capital, force.Faction, 0),
                force.Faction);
            sector.AddNewOrder(program.TaskOrder);
            return program.TaskOrder;
        }

        private static RecruitmentStaffRole? ResolveRole(
            PlayerSoldier soldier,
            GameRulesData rules)
        {
            if (soldier?.Template == null || rules?.ChapterDoctrine == null)
            {
                return null;
            }

            if (soldier.Template == rules.ChapterDoctrine.ScoutSergeant)
            {
                return RecruitmentStaffRole.ScoutSergeant;
            }
            if (soldier.Template == rules.ChapterDoctrine.Apothecary)
            {
                return RecruitmentStaffRole.Apothecary;
            }
            if (soldier.Template == rules.ChapterDoctrine.Chaplain
                || soldier.Template == rules.ChapterDoctrine.Judiciar)
            {
                return RecruitmentStaffRole.Chaplain;
            }

            return null;
        }
    }
}
