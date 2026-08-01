using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    public enum EndTurnWarningCategory
    {
        IdleDeployableSquads,
        LeaderlessSquads,
        ActionableTaskForces,
        SpecialMissionOpportunities,
        RecruitmentProgram
    }

    public sealed class EndTurnAttentionItem
    {
        public EndTurnWarningCategory Category { get; }
        public int EntityId { get; }
        public string Title { get; }
        public string Detail { get; }

        public EndTurnAttentionItem(
            EndTurnWarningCategory category,
            int entityId,
            string title,
            string detail)
        {
            Category = category;
            EntityId = entityId;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class EndTurnPreflightReport
    {
        private readonly IReadOnlyList<EndTurnAttentionItem> _items;

        public IReadOnlyList<EndTurnAttentionItem> Items => _items;
        public bool RequiresConfirmation => _items.Count > 0;

        public EndTurnPreflightReport(IEnumerable<EndTurnAttentionItem> items)
        {
            _items = (items ?? Enumerable.Empty<EndTurnAttentionItem>()).ToList().AsReadOnly();
        }

        public IReadOnlyList<EndTurnAttentionItem> ForCategory(EndTurnWarningCategory category)
        {
            return _items.Where(item => item.Category == category).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Finds campaign state that merits a conditional pause before turn resolution. It deliberately
    /// reports only actions the player can take now, and never creates an unconditional confirmation.
    /// </summary>
    public static class EndTurnPreflight
    {
        public static EndTurnPreflightReport Evaluate(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences)
        {
            return EvaluateCore(sector, preferences, null);
        }

        internal static EndTurnPreflightReport EvaluateWithRules(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences,
            GameRulesData rules)
        {
            return EvaluateCore(sector, preferences, rules);
        }

        private static EndTurnPreflightReport EvaluateCore(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences,
            GameRulesData rules)
        {
            if (sector == null)
            {
                throw new ArgumentNullException(nameof(sector));
            }

            preferences ??= new Settings.EndTurnWarningPreferences();
            List<EndTurnAttentionItem> items = [];
            List<Squad> playerSquads = GetPlayerSquads(sector).ToList();

            if (preferences.WarnIdleDeployableSquads)
            {
                items.AddRange(playerSquads
                    .Where(IsIdleDeployableSquad)
                    .OrderBy(squad => squad.CurrentRegion?.Planet?.Name
                        ?? squad.BoardedLocation?.Fleet?.Planet?.Name)
                    .ThenBy(squad => squad.CurrentRegion?.Name
                        ?? squad.BoardedLocation?.Name)
                    .ThenBy(squad => squad.Name)
                    .Select(BuildSquadItem));
            }

            if (preferences.WarnLeaderlessSquads)
            {
                items.AddRange(playerSquads
                    .Where(IsLeaderlessSquad)
                    .OrderBy(squad => squad.ParentUnit?.Name)
                    .ThenBy(squad => squad.Name)
                    .Select(BuildLeaderlessSquadItem));
            }

            if (preferences.WarnActionableTaskForces)
            {
                items.AddRange(sector.Fleets.Values
                    .Where(fleet => IsActionableTaskForceWithoutOrders(sector, fleet))
                    .OrderBy(fleet => fleet.Planet?.Name)
                    .ThenBy(fleet => fleet.Id)
                    .Select(BuildTaskForceItem));
            }

            if (preferences.WarnSpecialMissionOpportunities)
            {
                HashSet<int> assignedMissionIds = playerSquads
                    .Select(squad => squad.CurrentOrders)
                    .Where(order => order?.Mission != null)
                    .Select(order => order.Mission.Id)
                    .ToHashSet();

                items.AddRange(sector.Planets.Values
                    .SelectMany(planet => planet.Regions.Where(region => region != null))
                    .SelectMany(region => region.SpecialMissions)
                    .Where(mission => mission != null && !assignedMissionIds.Contains(mission.Id))
                    .OrderBy(mission => mission.RegionFaction?.Region?.Planet?.Name)
                    .ThenBy(mission => mission.RegionFaction?.Region?.Name)
                    .ThenBy(mission => mission.MissionType)
                    .ThenBy(mission => mission.Id)
                    .Select(BuildSpecialMissionItem));
            }

            if (preferences.WarnRecruitmentProgram)
            {
                items.AddRange(BuildRecruitmentItems(sector, rules));
            }

            return new EndTurnPreflightReport(items);
        }

        public static string GetCategoryTitle(EndTurnWarningCategory category)
        {
            return category switch
            {
                EndTurnWarningCategory.IdleDeployableSquads => "Idle deployed squads",
                EndTurnWarningCategory.LeaderlessSquads => "Squads without a leader",
                EndTurnWarningCategory.ActionableTaskForces => "Task forces awaiting orders",
                EndTurnWarningCategory.SpecialMissionOpportunities => "Opportunities at risk",
                EndTurnWarningCategory.RecruitmentProgram => "Recruitment program",
                _ => "Unresolved attention"
            };
        }

        public static string GetPreferenceLabel(EndTurnWarningCategory category)
        {
            return category switch
            {
                EndTurnWarningCategory.IdleDeployableSquads => "Warn about idle deployed squads",
                EndTurnWarningCategory.LeaderlessSquads => "Warn about squads missing a leader",
                EndTurnWarningCategory.ActionableTaskForces => "Warn about task forces without destinations",
                EndTurnWarningCategory.SpecialMissionOpportunities => "Warn about unassigned special missions",
                EndTurnWarningCategory.RecruitmentProgram => "Warn about recruitment decisions and funding",
                _ => "Warn about this category"
            };
        }

        private static IEnumerable<EndTurnAttentionItem> BuildRecruitmentItems(
            Sector sector,
            GameRulesData rules)
        {
            PlayerForce force = sector.PlayerForce;
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program is not { IsSetupComplete: true })
            {
                yield break;
            }
            if (rules != null)
            {
                new RecruitmentStaffService().Synchronize(force, rules);
            }

            int weeklyCost = new RecruitmentForecastService().Calculate(
                program,
                new RecruitmentForecastInput()).WeeklyRequisitionCost;
            if (force.Army.Requisition < weeklyCost)
            {
                yield return new EndTurnAttentionItem(
                    EndTurnWarningCategory.RecruitmentProgram,
                    program.Id,
                    "Recruitment cannot be funded",
                    $"The program requires {weeklyCost:N0} Requisition this week, but "
                    + $"only {force.Army.Requisition:N0} is available. The turn may still "
                    + "advance, but screening, training, and implantation will pause.");
            }

            int phaseTwelve = program.Aspirants.Count(
                aspirant => aspirant.Phase == RecruitmentPhase.Phase12);
            if (phaseTwelve > 0)
            {
                yield return new EndTurnAttentionItem(
                    EndTurnWarningCategory.RecruitmentProgram,
                    program.Id,
                    "Aspirants await neophyte placement",
                    $"{phaseTwelve:N0} Phase 12 aspirant"
                    + $"{(phaseTwelve == 1 ? " is" : "s are")} ready for immediate "
                    + "administrative placement in a Home World Scout Squad.");
            }

            if (program.QualifiedCandidates.Count > 0
                && program.Aspirants.Count
                    >= RecruitmentForecastService.CalculateTrainingCapacity(program))
            {
                yield return new EndTurnAttentionItem(
                    EndTurnWarningCategory.RecruitmentProgram,
                    program.Id,
                    "Aspirant capacity is full",
                    $"{program.QualifiedCandidates.Count:N0} qualified candidate"
                    + $"{(program.QualifiedCandidates.Count == 1 ? " is" : "s are")} "
                    + "waiting while all training places are occupied.");
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
                    yield return new EndTurnAttentionItem(
                        EndTurnWarningCategory.RecruitmentProgram,
                        program.Id,
                        "Neophytes await the Black Carapace",
                        $"{readyScouts:N0} ready neophyte"
                        + $"{(readyScouts == 1 ? " can" : "s can")} begin the one-week "
                        + "procedure if an Apothecary and Devastator seat are available.");
                }
            }
        }

        private static IEnumerable<Squad> GetPlayerSquads(Sector sector)
        {
            return sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>();
        }

        private static bool IsIdleDeployableSquad(Squad squad)
        {
            bool canDeployFromCurrentLocation = squad?.CurrentRegion != null
                || squad?.BoardedLocation?.Fleet is
                {
                    TravelPhase: FleetTravelPhase.InOrbit,
                    Planet: not null
                };

            return squad?.Faction?.IsPlayerFaction == true
                && squad.IsOperational
                && squad.CurrentOrders == null
                && canDeployFromCurrentLocation
                && squad.Members.Any(member => member.CanFight);
        }

        // A squad only counts as leaderless if its template actually calls for a leader.
        // Some formations are defined without one (e.g. Ravener packs), and an empty squad
        // that is being kept alive for later staffing is not a problem the player must fix.
        private static bool IsLeaderlessSquad(Squad squad)
        {
            return squad?.Faction?.IsPlayerFaction == true
                && squad.IsOperational
                && squad.Members.Count > 0
                && squad.SquadLeader == null
                && squad.SquadTemplate.Elements.Any(element => element.SoldierTemplate.IsSquadLeader);
        }

        private static bool IsActionableTaskForceWithoutOrders(Sector sector, TaskForce fleet)
        {
            return fleet != null
                && fleet.Faction == sector.PlayerForce?.Faction
                && fleet.TravelPhase == FleetTravelPhase.InOrbit
                && fleet.Planet != null
                && fleet.Destination == null
                && fleet.Ships.Count > 0;
        }

        private static EndTurnAttentionItem BuildSquadItem(Squad squad)
        {
            string unit = string.IsNullOrWhiteSpace(squad.ParentUnit?.Name)
                ? string.Empty
                : $" - {squad.ParentUnit.Name}";
            int combatReady = squad.Members.Count(member => member.CanFight);
            string location = SquadLocationFormatter.Format(squad);
            return new EndTurnAttentionItem(
                EndTurnWarningCategory.IdleDeployableSquads,
                squad.Id,
                $"{squad.Name}{unit}",
                $"{combatReady}/{squad.Members.Count} combat-ready in {location}; no orders are assigned.");
        }

        private static EndTurnAttentionItem BuildLeaderlessSquadItem(Squad squad)
        {
            string unit = string.IsNullOrWhiteSpace(squad.ParentUnit?.Name)
                ? string.Empty
                : $" - {squad.ParentUnit.Name}";
            string leaderRole = squad.SquadTemplate.Elements
                .FirstOrDefault(element => element.SoldierTemplate.IsSquadLeader)
                ?.SoldierTemplate.Name ?? "squad leader";
            string location = SquadLocationFormatter.Format(squad);

            // Scout squads take a mechanical penalty every week they go unled, so call that
            // out specifically; for line squads the cost is felt on mission checks and in battle.
            string consequence = ChapterUpkeepProcessor.IsScoutSquad(squad)
                ? "With no instructor, the squad trains at half rate and its scouts fall further "
                  + "behind every week until a new sergeant is assigned."
                : "Leadership-based mission checks fall back to an ordinary battle-brother, and "
                  + "the squad fights without a leader's command presence.";

            return new EndTurnAttentionItem(
                EndTurnWarningCategory.LeaderlessSquads,
                squad.Id,
                $"{squad.Name}{unit}",
                $"{squad.Members.Count} brother{(squad.Members.Count == 1 ? string.Empty : "s")} "
                + $"in {location} with no {leaderRole}. {consequence}");
        }

        private static EndTurnAttentionItem BuildTaskForceItem(TaskForce fleet)
        {
            int embarkedSquads = fleet.Ships.Sum(ship => ship.LoadedSquads.Count);
            string embarked = embarkedSquads == 0
                ? "no squads embarked"
                : $"{embarkedSquads} squad{(embarkedSquads == 1 ? string.Empty : "s")} embarked";
            return new EndTurnAttentionItem(
                EndTurnWarningCategory.ActionableTaskForces,
                fleet.Id,
                $"Task Force {fleet.Id}",
                $"{fleet.Ships.Count} ship{(fleet.Ships.Count == 1 ? string.Empty : "s")}, {embarked}, "
                + $"orbiting {fleet.Planet.Name}; no destination is plotted.");
        }

        private static EndTurnAttentionItem BuildSpecialMissionItem(Mission mission)
        {
            Region region = mission.RegionFaction?.Region;
            string location = region == null
                ? "an unknown location"
                : $"{region.Name}, {region.Planet?.Name ?? "unknown planet"}";

            // A Show of Force is a standing petition, not an intelligence find: it is exempt from
            // both the weekly expiry roll and the low-intel wipe (PlanetTurnProcessor.
            // UpdateIntelligence), so the generic risk text below would be a plain falsehood.
            // What it does risk is the request's own deadline, and the governor's regard erodes
            // every week it goes unanswered.
            if (mission.MissionType == MissionType.ShowOfForce)
            {
                return new EndTurnAttentionItem(
                    EndTurnWarningCategory.SpecialMissionOpportunities,
                    mission.Id,
                    "Governor's request unanswered",
                    $"{location}. No squad is holding the requested Show of Force. The petition "
                    + "stands until its deadline, but the governor's regard for the Chapter "
                    + "falls every week it goes unanswered.");
            }

            bool insufficientVisibleIntel = region?.GetPlayerVisibleIntel() < 1f;
            string risk = insufficientVisibleIntel
                ? "Regional intelligence is below 1, so this opportunity will be cleared when the turn advances."
                : "This opportunity has an independent 25% chance to disappear when the turn advances; "
                  + "it is also lost if regional intelligence falls below 1.";

            return new EndTurnAttentionItem(
                EndTurnWarningCategory.SpecialMissionOpportunities,
                mission.Id,
                $"{mission.MissionType} opportunity",
                $"{location}. {risk}");
        }
    }
}
