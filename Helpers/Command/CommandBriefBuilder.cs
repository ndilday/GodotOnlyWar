using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Command;
using OnlyWar.Models.Events;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Reports;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Command
{
    /// <summary>
    /// Builds the live operational projection. It reads current state and a bounded recent-event
    /// collection only; no Brief card is persisted and no historical prose is regenerated here.
    /// </summary>
    internal sealed class CommandBriefBuilder
    {
        internal CommandBriefModel Build(
            Date currentDate,
            Sector sector,
            GameRulesData rules,
            LastTurnReportSnapshot lastTurnReport,
            IEnumerable<CampaignEvent> recentEvents = null)
        {
            if (currentDate == null) throw new ArgumentNullException(nameof(currentDate));
            if (sector == null) throw new ArgumentNullException(nameof(sector));

            List<CommandBriefItem> items = [];
            IReadOnlyList<CommandAttentionFact> attention =
                CommandAttentionEvaluator.Evaluate(sector, rules);
            items.AddRange(attention.Select(ToAttentionItem));
            AddActiveRequests(items, currentDate, sector);
            AddOperationsUnderway(items, sector);
            AddRecoveryAndReinforcement(items, currentDate, sector);
            AddStrategicSituation(items, currentDate, sector, lastTurnReport, recentEvents);
            AddMandates(items, currentDate, sector);

            List<CommandBriefItem> ordered = items
                .GroupBy(item => item.StableKey, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(item => item.Priority)
                    .ThenBy(item => item.SortWeek ?? int.MaxValue)
                    .ThenBy(item => item.SortDomainKey, StringComparer.Ordinal)
                    .First())
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.SortWeek ?? int.MaxValue)
                .ThenBy(item => (int)item.Category)
                .ThenBy(item => item.SortDomainKey, StringComparer.Ordinal)
                .ToList();
            IReadOnlyList<CommandBriefCategory> categories = ordered
                .Select(item => item.Category)
                .Distinct()
                .OrderBy(category => (int)category)
                .ToList();
            return new CommandBriefModel(ordered, categories);
        }

        // Mandates are intentionally a provider seam in Alpha 0.8. The category remains part of
        // the shared workspace contract, but no mandate facts are invented until the campaign
        // has an authoritative mandate substrate.
        private static void AddMandates(
            List<CommandBriefItem> items,
            Date currentDate,
            Sector sector)
        {
        }

        private static CommandBriefItem ToAttentionItem(CommandAttentionFact fact)
        {
            (CommandBriefCategory category, CommandBriefPriority priority, string icon, string action) =
                fact.Kind switch
                {
                    CommandAttentionKind.IdleDeployableSquad =>
                        (CommandBriefCategory.RequiresOrders, CommandBriefPriority.Actionable, "squad", "Review Squad"),
                    CommandAttentionKind.LeaderlessSquad =>
                        (CommandBriefCategory.RequiresOrders, CommandBriefPriority.Actionable, "squad", "Review Squad"),
                    CommandAttentionKind.ActionableTaskForce =>
                        (CommandBriefCategory.RequiresOrders, CommandBriefPriority.Actionable, "fleet", "Open Classis"),
                    CommandAttentionKind.SpecialMissionOpportunity =>
                        (CommandBriefCategory.PetitionsAndOpportunities,
                            fact.DeadlineWeek.HasValue
                                ? CommandBriefPriority.Critical
                                : CommandBriefPriority.Actionable,
                            "mission", "View Opportunity"),
                    CommandAttentionKind.RecruitmentDecision =>
                        (CommandBriefCategory.RecoveryAndReinforcement,
                            fact.StableKey.EndsWith("/funding", StringComparison.Ordinal)
                                ? CommandBriefPriority.Critical
                                : CommandBriefPriority.Actionable,
                            "recruitment", "Open Recruitment"),
                    _ => (CommandBriefCategory.StrategicSituation, CommandBriefPriority.Monitor, "archive", "Review")
                };

            return new CommandBriefItem(
                fact.StableKey,
                category,
                priority,
                fact.Title,
                fact.Detail,
                fact.DeadlineWeek.HasValue ? "At risk on End Turn" : "Requires attention",
                icon,
                true,
                fact.NavigationTarget,
                action,
                sortWeek: fact.DeadlineWeek,
                sortDomainKey: fact.StableKey);
        }

        private static void AddOperationsUnderway(List<CommandBriefItem> items, Sector sector)
        {
            HashSet<int> orders = [];
            foreach (Squad squad in sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>())
            {
                Order order = squad.CurrentOrders;
                if (order?.Mission == null || !orders.Add(order.Id)) continue;
                Region region = order.Mission.RegionFaction?.Region;
                string location = region == null
                    ? "an unreported location"
                    : $"{region.Name}, {region.Planet?.Name ?? "unknown world"}";
                int squadCount = order.AssignedSquads?.Count ?? 0;
                CampaignNavigationTarget orderTarget = region == null
                    ? CampaignNavigationTarget.UnavailableFor(
                        CampaignNavigationTargetKind.Region,
                        location)
                    : new CampaignNavigationTarget(
                        CampaignNavigationTargetKind.Region,
                        region.Id,
                        FocusId: order.Mission.Id,
                        Fallback: $"Open {location}.",
                        DisplayNameSnapshot: location);
                items.Add(new CommandBriefItem(
                    $"order/{order.Id}/underway",
                    CommandBriefCategory.OperationsUnderway,
                    CommandBriefPriority.Monitor,
                    $"{order.Mission.MissionType} underway",
                    $"{squadCount} assigned squad{(squadCount == 1 ? string.Empty : "s")} "
                        + $"are committed at {location}. The operation's result will be known when the turn resolves.",
                    "Active order",
                    "mission",
                    false,
                    orderTarget,
                    "Review Operation",
                    sortDomainKey: $"order/{order.Id:D10}"));
            }

            foreach (TaskForce fleet in sector.Fleets.Values
                .Where(fleet => fleet.Faction == sector.PlayerForce?.Faction
                    && fleet.Destination != null
                    && fleet.TravelPhase != FleetTravelPhase.InOrbit)
                .OrderBy(fleet => fleet.TravelWeeksRemaining)
                .ThenBy(fleet => fleet.Id))
            {
                string phase = fleet.TravelPhase switch
                {
                    FleetTravelPhase.InWarp => "in the Warp",
                    FleetTravelPhase.OutboundSystemTransit => "outbound system transit",
                    FleetTravelPhase.InboundSystemTransit => "inbound system transit",
                    _ => "in transit"
                };
                string arrival = fleet.TravelWeeksRemaining > 0
                    ? $"Known travel estimate: {fleet.TravelWeeksRemaining} week"
                        + $"{(fleet.TravelWeeksRemaining == 1 ? string.Empty : "s")} remaining."
                    : "Arrival timing is not currently available.";
                items.Add(new CommandBriefItem(
                    $"fleet/{fleet.Id}/transit",
                    CommandBriefCategory.OperationsUnderway,
                    CommandBriefPriority.Monitor,
                    $"Task Force {fleet.Id} in transit",
                    $"Bound for {fleet.Destination.Name}; currently {phase}. {arrival}",
                    "Movement underway",
                    "fleet",
                    false,
                    new CampaignNavigationTarget(
                        CampaignNavigationTargetKind.Fleet,
                        fleet.Id,
                        Fallback: $"Open Task Force {fleet.Id}.",
                        DisplayNameSnapshot: $"Task Force {fleet.Id}"),
                    "Review Fleet",
                    sortWeek: fleet.TravelWeeksRemaining,
                    sortDomainKey: $"fleet/{fleet.Id:D10}"));
            }
        }

        private static void AddActiveRequests(
            List<CommandBriefItem> items,
            Date currentDate,
            Sector sector)
        {
            foreach (IRequest request in sector.PlayerForce?.Requests
                ?.Where(request => request.Status is RequestStatus.Open or RequestStatus.InProgress)
                .OrderBy(request => request.Deadline?.GetTotalWeeks() ?? int.MaxValue)
                .ThenBy(request => request.Id)
                ?? Enumerable.Empty<IRequest>())
            {
                int? deadlineWeek = request.Deadline?.GetTotalWeeks();
                int? weeksRemaining = deadlineWeek - currentDate.GetTotalWeeks();
                CommandBriefPriority priority = weeksRemaining.HasValue && weeksRemaining <= 1
                    ? CommandBriefPriority.Critical
                    : CommandBriefPriority.Actionable;
                string requester = request.Requester?.Name ?? "Unknown governor";
                string world = request.TargetPlanet?.Name ?? "unknown world";
                string progress = FormatRequestProgress(request);
                GovernorRequestNarrative narrative = GovernorRequestNarrator.Compose(request);
                CampaignNavigationTarget target = new(
                    CampaignNavigationTargetKind.Diplomacy,
                    request.Id,
                    FocusId: request.TargetPlanet?.Id,
                    Fallback: $"Open the petition from {requester}.",
                    DisplayNameSnapshot: $"{requester}, {world}");
                List<CommandBriefRelatedLink> related = request.TargetPlanet == null
                    ? []
                    :
                    [
                        new CommandBriefRelatedLink(
                            $"request/{request.Id}/world",
                            $"View {world}",
                            new CampaignNavigationTarget(
                                CampaignNavigationTargetKind.Planet,
                                request.TargetPlanet.Id,
                                Fallback: $"View {world}.",
                                DisplayNameSnapshot: world))
                    ];
                items.Add(new CommandBriefItem(
                    $"request/{request.Id}/active",
                    CommandBriefCategory.PetitionsAndOpportunities,
                    priority,
                    $"Petition from {requester}",
                    $"{narrative.Flavor} {narrative.MechanicalSummary} {progress} {request.HasPlayerResponded switch
                    {
                        true => "The Chapter has answered and the commitment is underway.",
                        _ => "No response has been recorded from the Chapter yet."
                    }}",
                    !weeksRemaining.HasValue
                        ? "No fixed deadline recorded"
                        : weeksRemaining <= 0
                        ? "Deadline this turn"
                        : $"Deadline in {weeksRemaining} week{(weeksRemaining == 1 ? string.Empty : "s")}",
                    "diplomacy",
                    true,
                    target,
                    "Review Petition",
                    related,
                    deadlineWeek,
                    $"request/{request.Id:D10}"));
            }
        }

        private static string FormatRequestProgress(IRequest request)
        {
            if (request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed)
            {
                return "Progress requires suppressing the identified threat.";
            }

            decimal packageWeeks = request.Commitment.ReferenceBattleValuePerPackage <= 0
                ? 0
                : (decimal)request.ProgressBattleValueTime
                    / request.Commitment.ReferenceBattleValuePerPackage;
            decimal required = request.Commitment.PackageCount * request.Commitment.ServiceWeeks;
            return $"Progress: {packageWeeks:0.#} of {required:0.#} squad-weeks.";
        }

        private static void AddRecoveryAndReinforcement(
            List<CommandBriefItem> items,
            Date currentDate,
            Sector sector)
        {
            PlayerForce force = sector.PlayerForce;
            foreach (OpenNearDeathEpisode episode in force?.CampaignEventLedger?.OpenNearDeathEpisodes.Values
                .OrderBy(episode => episode.OccurredWeek)
                .ThenBy(episode => episode.SoldierId)
                ?? Enumerable.Empty<OpenNearDeathEpisode>())
            {
                PlayerSoldier soldier = force.Army.PlayerSoldierMap.GetValueOrDefault(episode.SoldierId)
                    ?? force.Army.FallenBrothers.GetValueOrDefault(episode.SoldierId);
                string soldierName = soldier?.Name ?? $"Brother {episode.SoldierId}";
                items.Add(new CommandBriefItem(
                    $"medical/near-death/{episode.SoldierId}/{episode.SourceIncapacitationEventId}",
                    CommandBriefCategory.RecoveryAndReinforcement,
                    CommandBriefPriority.Monitor,
                    $"{soldierName} is recovering from near death",
                    $"A crippled vital location ({episode.DefiningVitalLocationName ?? "vital"}) remains an open recovery episode.",
                    "Open recovery episode",
                    "medical",
                    false,
                    BuildSoldierTarget(force, episode.SoldierId, soldierName),
                    "Open Dossier",
                    sortWeek: episode.OccurredWeek,
                    sortDomainKey: $"medical/near-death/{episode.SoldierId:D10}"));
            }
            foreach (MedicalProcedure procedure in force?.Army?.MedicalProcedures
                ?? Enumerable.Empty<MedicalProcedure>())
            {
                PlayerSoldier soldier = force.Army.PlayerSoldierMap.GetValueOrDefault(procedure.SoldierId)
                    ?? force.Army.FallenBrothers.GetValueOrDefault(procedure.SoldierId);
                string soldierName = soldier?.Name ?? $"Brother {procedure.SoldierId}";
                string method = procedure.ProcedureType == MedicalProcedureType.Cybernetic
                    ? "cybernetic replacement"
                    : "vat-grown replacement";
                items.Add(new CommandBriefItem(
                    $"medical/{procedure.SoldierId}/{procedure.HitLocationTemplateId}",
                    CommandBriefCategory.RecoveryAndReinforcement,
                    CommandBriefPriority.Monitor,
                    $"{soldierName} in the Apothecarium",
                    $"A {method} is underway with {procedure.WeeksRemaining} week"
                        + $"{(procedure.WeeksRemaining == 1 ? string.Empty : "s")} remaining.",
                    "Procedure underway",
                    "medical",
                    false,
                    force?.Army?.PlayerSoldierMap.ContainsKey(procedure.SoldierId) == true
                        ? new CampaignNavigationTarget(
                            CampaignNavigationTargetKind.Apothecarium,
                            procedure.SoldierId,
                            Fallback: "Open the Apothecarium.",
                            DisplayNameSnapshot: soldierName)
                        : CampaignNavigationTarget.UnavailableFor(
                            CampaignNavigationTargetKind.Apothecarium,
                            soldierName),
                    "Review Recovery",
                    sortWeek: procedure.WeeksRemaining,
                    sortDomainKey: $"medical/{procedure.SoldierId:D10}"));
            }

            List<PlayerSoldier> wounded = force?.Army?.OrderOfBattle?.GetAllMembers()
                .OfType<PlayerSoldier>()
                .Where(soldier => !soldier.IsCombatEffective)
                .OrderBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(soldier => soldier.Id)
                .ToList() ?? [];
            if (wounded.Count > 0)
            {
                items.Add(new CommandBriefItem(
                    "medical/wounded-roster",
                    CommandBriefCategory.RecoveryAndReinforcement,
                    CommandBriefPriority.Monitor,
                    "Wounded brothers require review",
                    $"{wounded.Count} active brother{(wounded.Count == 1 ? " is" : "s are")} "
                        + "not combat-effective. The Apothecarium shows the authoritative recovery state.",
                    "Current readiness",
                    "medical",
                    false,
                    new CampaignNavigationTarget(
                        CampaignNavigationTargetKind.Apothecarium,
                        Fallback: "Open the Apothecarium."),
                    "Open Apothecarium",
                    sortDomainKey: "medical/wounded-roster"));
            }

            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program is { IsSetupComplete: true }
                && program.Aspirants.Count > 0)
            {
                items.Add(new CommandBriefItem(
                    $"recruitment/{program.Id}/progress",
                    CommandBriefCategory.RecoveryAndReinforcement,
                    CommandBriefPriority.Monitor,
                    "10th Company recruitment underway",
                    $"{program.Aspirants.Count} aspirant"
                        + $"{(program.Aspirants.Count == 1 ? " is" : "s are")} in the training pipeline. "
                        + "Placement and procedure decisions remain in the Brief when they become actionable.",
                    "Program active",
                    "recruitment",
                    false,
                    new CampaignNavigationTarget(
                        CampaignNavigationTargetKind.Recruitment,
                        program.Id,
                        Fallback: "Open 10th Company recruitment.",
                        DisplayNameSnapshot: "10th Company recruitment"),
                    "Review Recruitment",
                    sortDomainKey: $"recruitment/{program.Id:D10}/progress"));
            }
        }

        private static CampaignNavigationTarget BuildSoldierTarget(
            PlayerForce force,
            int soldierId,
            string soldierName)
        {
            bool available = force?.Army?.PlayerSoldierMap.ContainsKey(soldierId) == true
                || force?.Army?.FallenBrothers.ContainsKey(soldierId) == true;
            return available
                ? new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Soldier,
                    soldierId,
                    Fallback: $"Open the preserved dossier for {soldierName}.",
                    DisplayNameSnapshot: soldierName)
                : CampaignNavigationTarget.UnavailableFor(
                    CampaignNavigationTargetKind.Soldier,
                    soldierName);
        }

        private static void AddStrategicSituation(
            List<CommandBriefItem> items,
            Date currentDate,
            Sector sector,
            LastTurnReportSnapshot lastTurnReport,
            IEnumerable<CampaignEvent> recentEvents)
        {
            CampaignScenario scenario = sector.Scenario;
            if (scenario != null)
            {
                Planet promisedWorld = sector.Planets.GetValueOrDefault(scenario.PromisedPlanetId);
                string state = scenario.State switch
                {
                    ObjectiveState.Won => "Objective resolved: world secured.",
                    ObjectiveState.Lapsed => "Objective resolved: directive lapsed.",
                    _ => "Objective pending."
                };
                string text = string.IsNullOrWhiteSpace(scenario.BriefingText)
                    ? "Command has issued no additional directive."
                    : scenario.BriefingText;
                items.Add(new CommandBriefItem(
                    "scenario/founding-directive",
                    CommandBriefCategory.StrategicSituation,
                    scenario.State == ObjectiveState.Pending
                        ? CommandBriefPriority.Actionable
                        : CommandBriefPriority.Monitor,
                    scenario.State == ObjectiveState.Pending
                        ? "Founding Directive"
                        : "Founding Directive — Resolved",
                    $"{text} {state}",
                    state,
                    "chapter",
                    scenario.State == ObjectiveState.Pending,
                    promisedWorld == null
                        ? new CampaignNavigationTarget(
                            CampaignNavigationTargetKind.Unavailable,
                            Fallback: "The promised world is unavailable in this save.",
                            DisplayNameSnapshot: "Promised World")
                        : new CampaignNavigationTarget(
                            CampaignNavigationTargetKind.Planet,
                            promisedWorld.Id,
                            Fallback: $"View {promisedWorld.Name}.",
                            DisplayNameSnapshot: promisedWorld.Name),
                    "View World",
                    sortWeek: currentDate.GetTotalWeeks(),
                    sortDomainKey: "scenario/founding-directive"));

                if (scenario.State == ObjectiveState.Pending)
                {
                    AddFoundingChecklist(items, currentDate, sector, scenario, promisedWorld);
                }
            }

            int currentWeek = currentDate.GetTotalWeeks();
            IEnumerable<CampaignEvent> boundedEvents = (recentEvents
                ?? Enumerable.Empty<CampaignEvent>())
                .Where(@event => @event.OccurredWeek == currentWeek
                    && @event.Publication.PublishesToTurnReport)
                .OrderBy(@event => @event.OccurredWeek)
                .ThenBy(@event => @event.Id)
                .Take(12);
            foreach (CampaignEvent @event in boundedEvents)
            {
                string key = $"event/{@event.Id}";
                bool commandDisruption = @event.Type == CampaignEventType.SquadLeaderUnavailable;
                items.Add(new CommandBriefItem(
                    key,
                    commandDisruption
                        ? CommandBriefCategory.RequiresOrders
                        : CommandBriefCategory.StrategicSituation,
                    commandDisruption ? CommandBriefPriority.Actionable : CommandBriefPriority.Monitor,
                    @event.Type switch
                    {
                        CampaignEventType.FirstBlood => "First Blood",
                        CampaignEventType.SquadLeaderUnavailable => "Squad leader unavailable",
                        CampaignEventType.WorldSaved => "World restored",
                        CampaignEventType.WorldLost => "World lost",
                        CampaignEventType.HiddenCultRevealed => "Hidden cult revealed",
                        _ => "Known Campaign Event"
                    },
                    CampaignEventNarrator.RenderCommandBrief(@event, sector.PlayerForce?.CampaignIdentity),
                    commandDisruption ? "Requires attention" : "Recorded this week",
                    "archive",
                    false,
                    BuildEventTarget(@event, sector),
                    "Review Record",
                    sortWeek: @event.OccurredWeek,
                    sortDomainKey: key));
            }

            if (lastTurnReport != null)
            {
                int reportCount = lastTurnReport.Entries?.Count ?? 0;
                string reportDate = lastTurnReport.ResolvedDate > 0
                    ? Date.FromTotalWeeks(lastTurnReport.ResolvedDate).ToString()
                    : "the previous turn";
                items.Add(new CommandBriefItem(
                    "report/last-turn",
                    CommandBriefCategory.StrategicSituation,
                    CommandBriefPriority.Monitor,
                    "Last Turn Report",
                    $"{reportCount} report{(reportCount == 1 ? string.Empty : "s")} from {reportDate} remain available for review.",
                    "Latest report only",
                    "archive",
                    false,
                    new CampaignNavigationTarget(
                        CampaignNavigationTargetKind.LastTurnReport,
                        Fallback: "Open the last turn report."),
                    "Review Last Turn Report",
                    sortDomainKey: "report/last-turn"));
            }
        }

        private static void AddFoundingChecklist(
            List<CommandBriefItem> items,
            Date currentDate,
            Sector sector,
            CampaignScenario scenario,
            Planet promisedWorld)
        {
            bool hasPositionedForce = sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                .Any(squad => squad.CurrentRegion?.Planet?.Id == scenario.PromisedPlanetId
                    || squad.BoardedLocation?.Fleet?.Planet?.Id == scenario.PromisedPlanetId) == true;
            bool hasRelevantOrder = sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                .Any(squad => squad.CurrentOrders?.Mission?.RegionFaction?.Region?.Planet?.Id
                    == scenario.PromisedPlanetId) == true;
            CampaignEvent foundingEvent = sector.PlayerForce?.CampaignEventLedger
                ?.GetByDedupeKey("chapter/founded");
            bool hasResolvedTurn = foundingEvent != null
                && currentDate.GetTotalWeeks() > foundingEvent.OccurredWeek;
            string world = promisedWorld?.Name ?? "the Promised World";
            CampaignNavigationTarget worldTarget = promisedWorld == null
                ? CampaignNavigationTarget.UnavailableFor(
                    CampaignNavigationTargetKind.Planet,
                    "Promised World")
                : new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Planet,
                    promisedWorld.Id,
                    Fallback: $"View {world}.",
                    DisplayNameSnapshot: world);
            items.Add(new CommandBriefItem(
                "scenario/checklist/position-forces",
                CommandBriefCategory.StrategicSituation,
                hasPositionedForce ? CommandBriefPriority.Monitor : CommandBriefPriority.Actionable,
                $"{(hasPositionedForce ? "[x]" : "[ ]")} Position forces at {world}",
                hasPositionedForce
                    ? "Operational forces are already positioned on or in orbit of the promised world."
                    : "Land or move an operational squad to the promised world before issuing its first commitment.",
                hasPositionedForce ? "Complete" : "Founding checklist",
                "map_pin",
                !hasPositionedForce,
                worldTarget,
                "View World",
                sortWeek: currentDate.GetTotalWeeks(),
                sortDomainKey: "scenario/checklist/position-forces"));
            items.Add(new CommandBriefItem(
                "scenario/checklist/issue-order",
                CommandBriefCategory.StrategicSituation,
                hasRelevantOrder ? CommandBriefPriority.Monitor : CommandBriefPriority.Actionable,
                $"{(hasRelevantOrder ? "[x]" : "[ ]")} Issue a relevant order",
                hasRelevantOrder
                    ? "A squad is committed to an operation on the promised world."
                    : "Assign a mission or defensive commitment so the Chapter's first turn has a clear purpose.",
                hasRelevantOrder ? "Complete" : "Founding checklist",
                "mission",
                !hasRelevantOrder,
                worldTarget with
                {
                    Fallback = $"View {world} and choose an action."
                },
                "Choose Action",
                sortWeek: currentDate.GetTotalWeeks(),
                sortDomainKey: "scenario/checklist/issue-order"));
            items.Add(new CommandBriefItem(
                "scenario/checklist/end-turn",
                CommandBriefCategory.StrategicSituation,
                hasResolvedTurn ? CommandBriefPriority.Monitor : CommandBriefPriority.Actionable,
                $"{(hasResolvedTurn ? "[x]" : "[ ]")} Resolve the first commitment",
                hasResolvedTurn
                    ? "The campaign has advanced beyond its founding week; End Turn has resolved a commitment."
                    : "End Turn advances the campaign and resolves the commitments currently in force.",
                hasResolvedTurn ? "Complete" : "Founding checklist",
                "end_turn",
                false,
                new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.SectorMap,
                    Fallback: "Return to the Sector Map before ending the turn.",
                    DisplayNameSnapshot: "Sector Map"),
                "Review Map",
                sortWeek: currentDate.GetTotalWeeks(),
                sortDomainKey: "scenario/checklist/end-turn"));
        }

        private static CampaignNavigationTarget BuildEventTarget(
            CampaignEvent @event,
            Sector sector)
        {
            CampaignEventEntityRef entity = @event.Entities
                .FirstOrDefault(item => item.Role == CampaignEventEntityRole.Location);
            if (entity == null)
            {
                return CampaignNavigationTarget.UnavailableFor(
                    CampaignNavigationTargetKind.SectorMap,
                    "This campaign event");
            }
            CampaignNavigationTargetKind kind = entity.Kind switch
            {
                CampaignEntityKind.Planet => CampaignNavigationTargetKind.Planet,
                CampaignEntityKind.Region => CampaignNavigationTargetKind.Region,
                CampaignEntityKind.Squad => CampaignNavigationTargetKind.Squad,
                CampaignEntityKind.Soldier => CampaignNavigationTargetKind.Soldier,
                _ => CampaignNavigationTargetKind.SectorMap
            };
            if (!IsEventTargetAvailable(entity, kind, sector))
            {
                return CampaignNavigationTarget.UnavailableFor(kind, entity.DisplayNameSnapshot);
            }
            return new CampaignNavigationTarget(
                kind,
                entity.EntityId,
                DisplayNameSnapshot: entity.DisplayNameSnapshot,
                Fallback: $"Open {entity.DisplayNameSnapshot}.");
        }

        private static bool IsEventTargetAvailable(
            CampaignEventEntityRef entity,
            CampaignNavigationTargetKind targetKind,
            Sector sector)
        {
            if (targetKind == CampaignNavigationTargetKind.SectorMap)
            {
                return true;
            }

            if (sector == null || entity == null)
            {
                return false;
            }

            return entity.Kind switch
            {
                CampaignEntityKind.Planet => sector.Planets.ContainsKey(entity.EntityId),
                CampaignEntityKind.Region => sector.Planets.Values
                    .SelectMany(planet => planet.Regions)
                    .Any(region => region?.Id == entity.EntityId),
                CampaignEntityKind.Soldier => sector.PlayerForce?.Army?.PlayerSoldierMap
                    .ContainsKey(entity.EntityId) == true
                    || sector.PlayerForce?.Army?.FallenBrothers.ContainsKey(entity.EntityId) == true,
                CampaignEntityKind.Squad => sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                    .Any(squad => squad.Id == entity.EntityId) == true,
                _ => true
            };
        }
    }
}
