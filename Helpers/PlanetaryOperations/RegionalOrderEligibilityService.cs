using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public enum SquadEligibilityExclusion
    {
        None,
        Embarked,
        OutOfArea,
        NonOperational,
        EmptyFormation,
        PersonnelPool,
        ProcedureBlocked,
        AssignedElsewhere,
        MissionUnavailable
    }

    public sealed record RegionalSquadCandidate(
        Squad Squad,
        Region Origin,
        bool IsTargetOrigin,
        bool IsAssignedToContext,
        bool IsSelectable,
        SquadEligibilityExclusion Exclusion);

    public sealed record RegionalSquadGroup(
        Region Origin,
        bool IsTargetOrigin,
        IReadOnlyList<RegionalSquadCandidate> Candidates);

    public sealed record RegionalEligibilityResult(
        Region Target,
        IReadOnlyList<RegionalSquadGroup> Groups,
        IReadOnlyList<RegionalSquadCandidate> Excluded)
    {
        public IReadOnlyList<RegionalSquadCandidate> Candidates =>
            Groups.SelectMany(group => group.Candidates).ToList();
    }

    /// <summary>
    /// Builds the mission-scoped force list for Planetary Operations. Screen position never
    /// participates in legality: the target and Region.GetAdjacentRegions() are the only origins.
    /// </summary>
    public static class RegionalOrderEligibilityService
    {
        public static RegionalEligibilityResult Build(
            Sector sector,
            Region target,
            AvailableMission selectedMission = null,
            Order contextOrder = null)
        {
            if (sector?.PlayerForce?.Faction == null || target == null)
            {
                return new RegionalEligibilityResult(
                    target, [], []);
            }

            Faction playerFaction = sector.PlayerForce.Faction;
            List<Region> origins = [target];
            origins.AddRange(target.GetAdjacentRegions()
                .Where(region => region != null)
                .OrderBy(region => region.Name)
                .ThenBy(region => region.Id));

            RecruitmentProgram program = sector.PlayerForce.RecruitmentProgram;
            List<RegionalSquadGroup> groups = [];
            List<RegionalSquadCandidate> excluded = [];

            foreach (Region origin in origins)
            {
                List<RegionalSquadCandidate> visible = [];
                if (origin.RegionFactionMap.TryGetValue(
                        playerFaction.Id, out RegionFaction playerPresence))
                {
                    foreach (Squad squad in playerPresence.LandedSquads
                        .Where(squad => squad != null)
                        .DistinctBy(squad => squad.Id)
                        .OrderBy(squad => squad.ParentUnit?.Name)
                        .ThenBy(squad => squad.Name)
                        .ThenBy(squad => squad.Id))
                    {
                        RegionalSquadCandidate candidate = Evaluate(
                            squad, origin, target, selectedMission, contextOrder, program);
                        if (candidate.Exclusion == SquadEligibilityExclusion.None
                            || candidate.IsAssignedToContext)
                        {
                            visible.Add(candidate);
                        }
                        else
                        {
                            excluded.Add(candidate);
                        }
                    }
                }

                groups.Add(new RegionalSquadGroup(
                    origin,
                    ReferenceEquals(origin, target),
                    visible));
            }

            return new RegionalEligibilityResult(target, groups, excluded);
        }

        public static bool IsMissionAvailableFrom(
            Region origin,
            Region target,
            AvailableMission selectedMission)
        {
            if (selectedMission == null) return true;
            return MissionAvailability.GetAvailableMissions(origin, target)
                .Any(option => option.RepresentsSameOption(selectedMission));
        }

        private static RegionalSquadCandidate Evaluate(
            Squad squad,
            Region origin,
            Region target,
            AvailableMission selectedMission,
            Order contextOrder,
            RecruitmentProgram program)
        {
            bool assignedToContext = contextOrder != null
                && ReferenceEquals(squad.CurrentOrders, contextOrder)
                && contextOrder.AssignedSquads.Contains(squad);
            SquadEligibilityExclusion exclusion = GetExclusion(
                squad, origin, target, selectedMission, contextOrder, program);
            return new RegionalSquadCandidate(
                squad,
                origin,
                ReferenceEquals(origin, target),
                assignedToContext,
                exclusion == SquadEligibilityExclusion.None && !assignedToContext,
                exclusion);
        }

        private static SquadEligibilityExclusion GetExclusion(
            Squad squad,
            Region origin,
            Region target,
            AvailableMission selectedMission,
            Order contextOrder,
            RecruitmentProgram program)
        {
            if (squad.BoardedLocation != null)
            {
                return SquadEligibilityExclusion.Embarked;
            }
            if (!ReferenceEquals(squad.CurrentRegion, origin)
                || (!ReferenceEquals(origin, target)
                    && !target.GetAdjacentRegions().Contains(origin)))
            {
                return SquadEligibilityExclusion.OutOfArea;
            }
            if (!squad.IsOperational)
            {
                return SquadEligibilityExclusion.NonOperational;
            }
            if (squad.Members.Count == 0)
            {
                return SquadEligibilityExclusion.EmptyFormation;
            }
            if (!SpecialistAvailability.IsDeployableFormation(squad))
            {
                return SquadEligibilityExclusion.PersonnelPool;
            }
            if (squad.Members.Any(member =>
                    RecruitmentPromotionService.IsSoldierInBlackCarapaceProcedure(
                        program, member.Id)))
            {
                return SquadEligibilityExclusion.ProcedureBlocked;
            }
            if (squad.CurrentOrders != null
                && !ReferenceEquals(squad.CurrentOrders, contextOrder))
            {
                return SquadEligibilityExclusion.AssignedElsewhere;
            }
            if (!IsMissionAvailableFrom(origin, target, selectedMission))
            {
                return SquadEligibilityExclusion.MissionUnavailable;
            }
            return SquadEligibilityExclusion.None;
        }
    }
}
