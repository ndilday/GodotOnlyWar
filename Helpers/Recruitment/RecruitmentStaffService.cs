using System;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.Recruitment
{
    /// <summary>
    /// Projects the members of the 10th Company administrative HQ into the staffing
    /// values used by the recruitment simulation. The Chapter screen remains the sole
    /// reassignment UI: moving an eligible brother into or out of that squad changes the
    /// program the next time it is previewed or processed.
    /// </summary>
    internal sealed class RecruitmentStaffService
    {
        internal Squad GetAdministrativeSquad(PlayerForce force, GameRulesData rules)
        {
            return force?.Army?.OrderOfBattle?.GetAllSquads()
                .SingleOrDefault(squad => squad.SquadTemplate
                    == rules?.ChapterTemplates?.ScoutCompanyHeadquarters);
        }

        internal void Synchronize(PlayerForce force, GameRulesData rules, Sector sector = null)
        {
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program == null)
            {
                return;
            }

            sector ??= GameDataSingleton.Instance?.Sector;
            Order taskOrder = EnsureTaskOrder(force, program, sector);
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
                    && !OrderForceService.AssignCharacter(taskOrder, soldier))
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
                    evaluation?[RatingKeys.Leadership] ?? 0,
                    evaluation?[RatingKeys.Medical] ?? 0,
                    evaluation?[RatingKeys.Piety] ?? 0));
            }
        }

        internal static Order EnsureTaskOrder(
            PlayerForce force,
            RecruitmentProgram program,
            Sector sector)
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

            program.TaskOrder = new Order(
                [],
                isQuiet: true,
                isActivelyEngaging: false,
                Aggression.Avoid,
                new Mission(MissionType.Recruitment, capital, force.Faction, 0),
                force.Faction);
            sector.AddNewOrder(program.TaskOrder);
            return program.TaskOrder;
        }

        private static RecruitmentStaffRole? ResolveRole(
            PlayerSoldier soldier,
            GameRulesData rules)
        {
            if (soldier?.Template == null || rules?.ChapterTemplates == null)
            {
                return null;
            }

            if (soldier.Template == rules.ChapterTemplates.ScoutSergeant)
            {
                return RecruitmentStaffRole.ScoutSergeant;
            }
            if (soldier.Template == rules.ChapterTemplates.Apothecary)
            {
                return RecruitmentStaffRole.Apothecary;
            }
            if (soldier.Template == rules.ChapterTemplates.Chaplain
                || soldier.Template == rules.ChapterTemplates.Judiciar)
            {
                return RecruitmentStaffRole.Chaplain;
            }

            return null;
        }
    }
}
