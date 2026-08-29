using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Command;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    public enum CommandAttentionKind
    {
        IdleDeployableSquad,
        LeaderlessSquad,
        ActionableTaskForce,
        SpecialMissionOpportunity,
        RecruitmentDecision
    }

    /// <summary>
    /// A preference-free fact about an entity that deserves command attention. End Turn preflight
    /// decides whether to interrupt from these facts; the Command Brief always sees them.
    /// </summary>
    public sealed class CommandAttentionFact
    {
        public string StableKey { get; }
        public CommandAttentionKind Kind { get; }
        public EndTurnWarningCategory WarningCategory { get; }
        public int EntityId { get; }
        public string Title { get; }
        public string Detail { get; }
        public int? DeadlineWeek { get; }
        public CampaignNavigationTarget NavigationTarget { get; }

        public CommandAttentionFact(
            string stableKey,
            CommandAttentionKind kind,
            EndTurnWarningCategory warningCategory,
            int entityId,
            string title,
            string detail,
            CampaignNavigationTarget navigationTarget,
            int? deadlineWeek = null)
        {
            StableKey = stableKey ?? throw new ArgumentNullException(nameof(stableKey));
            Kind = kind;
            WarningCategory = warningCategory;
            EntityId = entityId;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            NavigationTarget = navigationTarget;
            DeadlineWeek = deadlineWeek;
        }
    }

    /// <summary>
    /// Owns the factual attention checks shared by the live Brief and End Turn preflight. It makes
    /// no preference decisions and does not synchronize or mutate recruitment state.
    /// </summary>
    public static class CommandAttentionEvaluator
    {
        internal static IReadOnlyList<CommandAttentionFact> Evaluate(
            Sector sector,
            GameRulesData rules = null)
        {
            if (sector == null) throw new ArgumentNullException(nameof(sector));

            List<CommandAttentionFact> facts = [];
            List<Squad> playerSquads = GetPlayerSquads(sector).ToList();

            facts.AddRange(playerSquads
                .Where(IsIdleDeployableSquad)
                .OrderBy(squad => squad.CurrentRegion?.Planet?.Name
                    ?? squad.BoardedLocation?.Fleet?.Planet?.Name)
                .ThenBy(squad => squad.CurrentRegion?.Name
                    ?? squad.BoardedLocation?.Name)
                .ThenBy(squad => squad.Name)
                .ThenBy(squad => squad.Id)
                .Select(BuildSquadFact));

            facts.AddRange(playerSquads
                .Where(IsLeaderlessSquad)
                .OrderBy(squad => squad.ParentUnit?.Name)
                .ThenBy(squad => squad.Name)
                .ThenBy(squad => squad.Id)
                .Select(BuildLeaderlessSquadFact));

            facts.AddRange(sector.Fleets.Values
                .Where(fleet => IsActionableTaskForceWithoutOrders(sector, fleet))
                .OrderBy(fleet => fleet.Planet?.Name)
                .ThenBy(fleet => fleet.Id)
                .Select(BuildTaskForceFact));

            HashSet<int> assignedMissionIds = playerSquads
                .Select(squad => squad.CurrentOrders)
                .Where(order => order?.Mission != null)
                .Select(order => order.Mission.Id)
                .ToHashSet();

            facts.AddRange(sector.Planets.Values
                .SelectMany(planet => planet.Regions.Where(region => region != null))
                .SelectMany(region => region.SpecialMissions)
                .Where(mission => mission != null
                    && (mission.MissionType != MissionType.Extermination
                        || mission.RegionFaction != null && !mission.RegionFaction.IsPublic)
                    && !assignedMissionIds.Contains(mission.Id))
                .OrderBy(mission => mission.RegionFaction?.Region?.Planet?.Name)
                .ThenBy(mission => mission.RegionFaction?.Region?.Name)
                .ThenBy(mission => mission.MissionType)
                .ThenBy(mission => mission.Id)
                .Select(BuildSpecialMissionFact));

            facts.AddRange(BuildRecruitmentFacts(sector, rules));
            return facts.AsReadOnly();
        }

        public static IReadOnlyList<EndTurnAttentionItem> ToPreflightItems(
            IEnumerable<CommandAttentionFact> facts,
            Settings.EndTurnWarningPreferences preferences)
        {
            preferences ??= new Settings.EndTurnWarningPreferences();
            return (facts ?? Enumerable.Empty<CommandAttentionFact>())
                .Where(fact => preferences.IsEnabled(fact.WarningCategory))
                .Select(fact => new EndTurnAttentionItem(
                    fact.WarningCategory,
                    fact.EntityId,
                    fact.Title,
                    fact.Detail,
                    fact.StableKey,
                    fact.NavigationTarget,
                    fact.DeadlineWeek))
                .ToList()
                .AsReadOnly();
        }

        private static IEnumerable<CommandAttentionFact> BuildRecruitmentFacts(
            Sector sector,
            GameRulesData rules)
        {
            PlayerForce force = sector.PlayerForce;
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program is not { IsSetupComplete: true })
            {
                yield break;
            }

            int weeklyCost = new RecruitmentForecastService().Calculate(
                program,
                new RecruitmentForecastInput()).WeeklyRequisitionCost;
            CampaignNavigationTarget target = new(
                CampaignNavigationTargetKind.Recruitment,
                program.Id,
                Fallback: "Open 10th Company recruitment.",
                DisplayNameSnapshot: "10th Company recruitment");

            if (force.Army.Requisition < weeklyCost)
            {
                yield return new CommandAttentionFact(
                    $"recruitment/{program.Id}/funding",
                    CommandAttentionKind.RecruitmentDecision,
                    EndTurnWarningCategory.RecruitmentProgram,
                    program.Id,
                    "Recruitment cannot be funded",
                    $"The program requires {weeklyCost:N0} Requisition this week, but only "
                        + $"{force.Army.Requisition:N0} is available. The turn may still advance, "
                        + "but screening, training, and implantation will pause.",
                    target);
            }

            int phaseTwelve = program.Aspirants.Count(
                aspirant => aspirant.Phase == RecruitmentPhase.Phase12);
            if (phaseTwelve > 0)
            {
                yield return new CommandAttentionFact(
                    $"recruitment/{program.Id}/placement",
                    CommandAttentionKind.RecruitmentDecision,
                    EndTurnWarningCategory.RecruitmentProgram,
                    program.Id,
                    "Aspirants await neophyte placement",
                    $"{phaseTwelve:N0} Phase 12 aspirant"
                        + $"{(phaseTwelve == 1 ? " is" : "s are")} ready for immediate "
                        + "administrative placement in a Home World Scout Squad.",
                    target);
            }

            if (program.QualifiedCandidates.Count > 0
                && program.Aspirants.Count
                    >= RecruitmentForecastService.CalculateTrainingCapacity(program))
            {
                yield return new CommandAttentionFact(
                    $"recruitment/{program.Id}/capacity",
                    CommandAttentionKind.RecruitmentDecision,
                    EndTurnWarningCategory.RecruitmentProgram,
                    program.Id,
                    "Aspirant capacity is full",
                    $"{program.QualifiedCandidates.Count:N0} qualified candidate"
                        + $"{(program.QualifiedCandidates.Count == 1 ? " is" : "s are")} waiting "
                        + "while all training places are occupied.",
                    target);
            }

            if (rules != null)
            {
                int readyScouts = force.Army.PlayerSoldierMap.Values.Count(soldier =>
                    soldier.Template == rules.ChapterTemplates.ScoutMarine
                    && soldier.GeneticCompatibility.HasValue
                    && soldier.SoldierEvaluationHistory.LastOrDefault()?.RangedRating > 105
                    && !RecruitmentPromotionService.IsSoldierInBlackCarapaceProcedure(
                        program, soldier.Id));
                if (readyScouts > 0)
                {
                    yield return new CommandAttentionFact(
                        $"recruitment/{program.Id}/black-carapace",
                        CommandAttentionKind.RecruitmentDecision,
                        EndTurnWarningCategory.RecruitmentProgram,
                        program.Id,
                        "Neophytes await the Black Carapace",
                        $"{readyScouts:N0} ready neophyte"
                            + $"{(readyScouts == 1 ? " can" : "s can")} begin the one-week "
                            + "procedure if an Apothecary and Devastator seat are available.",
                        target);
                }
            }
        }

        private static IEnumerable<Squad> GetPlayerSquads(Sector sector) =>
            sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>();

        private static bool IsIdleDeployableSquad(Squad squad)
        {
            bool canDeployFromCurrentLocation = squad?.CurrentRegion != null
                || squad?.BoardedLocation?.Fleet is
                {
                    TravelPhase: FleetTravelPhase.InOrbit,
                    Planet: not null
                };

            return squad?.Faction?.IsPlayerFaction == true
                && squad.CanAcceptSquadOrder
                && squad.CurrentOrders == null
                && !squad.PermitsIndividualDeployment
                && !OrderAttachment.HasAttachedMembers(squad)
                && canDeployFromCurrentLocation
                && squad.Members.Any(member => member.IsCombatEffective);
        }

        private static bool IsLeaderlessSquad(Squad squad) =>
            squad?.Faction?.IsPlayerFaction == true
                && squad.CanAcceptSquadOrder
                && squad.Members.Count > 0
                && squad.SquadLeader == null
                && squad.SquadTemplate.Elements.Any(element => element.SoldierTemplate.IsSquadLeader);

        private static bool IsActionableTaskForceWithoutOrders(Sector sector, TaskForce fleet) =>
            fleet != null
                && fleet.Faction == sector.PlayerForce?.Faction
                && fleet.TravelPhase == FleetTravelPhase.InOrbit
                && fleet.Planet != null
                && fleet.Destination == null
                && fleet.Ships.Count > 0;

        private static CommandAttentionFact BuildSquadFact(Squad squad)
        {
            string unit = string.IsNullOrWhiteSpace(squad.ParentUnit?.Name)
                ? string.Empty
                : $" - {squad.ParentUnit.Name}";
            int combatReady = squad.Members.Count(member => member.IsCombatEffective);
            string location = SquadLocationFormatter.Format(squad);
            return new CommandAttentionFact(
                $"squad/{squad.Id}/idle",
                CommandAttentionKind.IdleDeployableSquad,
                EndTurnWarningCategory.IdleDeployableSquads,
                squad.Id,
                $"{squad.Name}{unit}",
                $"{combatReady}/{squad.Members.Count} combat-ready in {location}; no orders are assigned.",
                new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Squad,
                    squad.Id,
                    Fallback: $"Open {squad.Name}.",
                    DisplayNameSnapshot: squad.Name));
        }

        private static CommandAttentionFact BuildLeaderlessSquadFact(Squad squad)
        {
            string unit = string.IsNullOrWhiteSpace(squad.ParentUnit?.Name)
                ? string.Empty
                : $" - {squad.ParentUnit.Name}";
            string leaderRole = squad.SquadTemplate.Elements
                .FirstOrDefault(element => element.SoldierTemplate.IsSquadLeader)
                ?.SoldierTemplate.Name ?? "squad leader";
            string location = SquadLocationFormatter.Format(squad);
            string consequence = ChapterUpkeepProcessor.IsScoutSquad(squad)
                ? "With no instructor, the squad trains at three-quarter rate and its scouts fall "
                    + "further behind every week until a new sergeant is assigned."
                : "Leadership-based mission checks fall back to an ordinary battle-brother, and "
                    + "the squad fights without a leader's command presence.";
            return new CommandAttentionFact(
                $"squad/{squad.Id}/leader",
                CommandAttentionKind.LeaderlessSquad,
                EndTurnWarningCategory.LeaderlessSquads,
                squad.Id,
                $"{squad.Name}{unit}",
                $"{squad.Members.Count} brother{(squad.Members.Count == 1 ? string.Empty : "s")} "
                    + $"in {location} with no {leaderRole}. {consequence}",
                new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Squad,
                    squad.Id,
                    Fallback: $"Open {squad.Name}.",
                    DisplayNameSnapshot: squad.Name));
        }

        private static CommandAttentionFact BuildTaskForceFact(TaskForce fleet)
        {
            int embarkedSquads = fleet.Ships.Sum(ship => ship.LoadedSquads.Count);
            string embarked = embarkedSquads == 0
                ? "no squads embarked"
                : $"{embarkedSquads} squad{(embarkedSquads == 1 ? string.Empty : "s")} embarked";
            return new CommandAttentionFact(
                $"fleet/{fleet.Id}/destination",
                CommandAttentionKind.ActionableTaskForce,
                EndTurnWarningCategory.ActionableTaskForces,
                fleet.Id,
                $"Task Force {fleet.Id}",
                $"{fleet.Ships.Count} ship{(fleet.Ships.Count == 1 ? string.Empty : "s")}, {embarked}, "
                    + $"orbiting {fleet.Planet.Name}; no destination is plotted.",
                new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Fleet,
                    fleet.Id,
                    Fallback: $"Open Task Force {fleet.Id}.",
                    DisplayNameSnapshot: $"Task Force {fleet.Id}"));
        }

        private static CommandAttentionFact BuildSpecialMissionFact(Mission mission)
        {
            Region region = mission.RegionFaction?.Region;
            string location = region == null
                ? "an unknown location"
                : $"{region.Name}, {region.Planet?.Name ?? "unknown planet"}";
            CampaignNavigationTarget target = region == null
                ? CampaignNavigationTarget.UnavailableFor(
                    CampaignNavigationTargetKind.Region,
                    location)
                : new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Region,
                    region.Id,
                    FocusId: mission.Id,
                    Fallback: $"Open {location}.",
                    DisplayNameSnapshot: location);

            if (mission.MissionType == MissionType.ShowOfForce)
            {
                return new CommandAttentionFact(
                    $"mission/{mission.Id}/show-of-force",
                    CommandAttentionKind.SpecialMissionOpportunity,
                    EndTurnWarningCategory.SpecialMissionOpportunities,
                    mission.Id,
                    "Governor's request unanswered",
                    $"{location}. No squad is holding the requested Show of Force. The petition "
                        + "stands until its deadline, but the governor's regard for the Chapter "
                        + "falls every week it goes unanswered.",
                    target);
            }

            bool insufficientVisibleIntel = region?.GetPlayerVisibleIntel() < 1f;
            string risk = insufficientVisibleIntel
                ? "Regional intelligence is below 1, so this opportunity will be cleared when the turn advances."
                : "This opportunity has an independent 25% chance to disappear when the turn advances; "
                    + "it is also lost if regional intelligence falls below 1.";
            return new CommandAttentionFact(
                $"mission/{mission.Id}/opportunity",
                CommandAttentionKind.SpecialMissionOpportunity,
                EndTurnWarningCategory.SpecialMissionOpportunities,
                mission.Id,
                $"{SpecialMissionPresentation.GetMissionTypeLabel(mission.MissionType)} opportunity",
                $"{location}. {risk}",
                target,
                insufficientVisibleIntel ? 1 : null);
        }
    }
}
