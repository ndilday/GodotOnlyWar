using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Narrative;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DiplomacyScreenController : MainScreenController
{
    private DiplomacyScreenView _view;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<DiplomacyScreenView>("DiplomacyScreenView");
        PopulateRequestData();
    }

    public void PopulateRequestData()
    {
        if (_view == null) return;

        Sector sector = GameDataSingleton.Instance.Sector;

        List<IRequest> activeRequests = sector.PlayerForce.Requests
            .Where(request => request.Status is RequestStatus.Open or RequestStatus.InProgress)
            .OrderBy(request => request.DateRequestMade.GetTotalWeeks())
            .ToList();

        List<TreeNode> nodes = activeRequests.Count == 0
            ? [new TreeNode(0, "No outstanding requests from the sector's governors.", [], selectable: false)]
            : activeRequests.Select(CreateRequestNode).ToList();

        List<Pledge> pledges = sector.PlayerForce.Pledges
            .Where(pledge => pledge.Status != PledgeStatus.Completed)
            .OrderBy(pledge => pledge.NextDeliveryDate)
            .ToList();
        if (pledges.Count > 0)
        {
            nodes.Add(new TreeNode(
                0,
                "Outstanding pledges",
                pledges.Select(CreatePledgeNode).ToList(),
                selectable: false));
        }

        // The Sector Lord's promise sits above the governors' petitions: it is the standing
        // obligation the whole opening is framed around, and it remains available through the
        // Command Brief/Chronicle surfaces after the initial directive is acknowledged.
        TreeNode promiseNode = CreatePromisedWorldNode(sector);
        if (promiseNode != null)
        {
            nodes.Insert(0, promiseNode);
        }

        _view.PopulateRequestTree(nodes);
    }

    /// <summary>
    /// Selects a petition requested by another workspace without changing its owning-surface
    /// rendering or inventing a second request-detail view.
    /// </summary>
    public void FocusRequest(int requestId)
    {
        _view?.FocusRequest(requestId);
    }

    /// <summary>
    /// The standing "Promised World" obligation, or null when there is nothing live to show —
    /// a plain-sandbox sector, or a scenario that has already resolved (a settled promise belongs
    /// to the Chapter's history, not to the board of outstanding business).
    /// </summary>
    private static TreeNode CreatePromisedWorldNode(Sector sector)
    {
        CampaignScenario scenario = sector.Scenario;
        if (scenario is not { Type: ScenarioType.PromisedWorld, State: ObjectiveState.Pending })
        {
            return null;
        }

        Planet promised = sector.Planets.TryGetValue(scenario.PromisedPlanetId, out Planet planet)
            ? planet
            : null;
        string planetName = promised?.Name ?? "Unknown world";

        // The obligation follows the seat, not the person, so this resolves whoever holds it now
        // rather than the character who originally made the promise.
        Planet capital = sector.GetSectorCapital();
        Character lord = capital?.Governor;
        string authority = lord == null
            ? "Pledged by: the sector throne (currently vacant)"
            : $"Pledged by: {BriefingComposer.GetAuthorityTitle(capital.GovernanceTier)} {lord.Name}";

        List<TreeNode> details =
        [
            new TreeNode(0, authority, [], selectable: false),
            new TreeNode(0, $"Terms: liberate {planetName} and it is granted to the Chapter as its home world", [], selectable: false),
            new TreeNode(0, "Fulfilled when: no enemy holds ground openly anywhere on the world", [], selectable: false),
            .. DescribeLiberationProgress(promised)
        ];

        return new TreeNode(0, $"The Promised World — {planetName}", details, selectable: false);
    }

    /// <summary>
    /// How far the liberation has actually got, measured the same way the objective resolves it
    /// (ScenarioTurnProcessor): by which regions an enemy still openly holds.
    /// </summary>
    private static List<TreeNode> DescribeLiberationProgress(Planet promised)
    {
        if (promised == null)
        {
            return [new TreeNode(0, "Progress: unknown — no report from the world", [], selectable: false)];
        }

        List<RegionFaction> enemyHoldings = promised.Regions
            .SelectMany(region => region.RegionFactionMap.Values)
            .Where(regionFaction => regionFaction.IsPublic
                && !FactionRelationshipService.IsImperial(regionFaction.PlanetFaction.Faction)
                && (regionFaction.Population > 0 || regionFaction.Garrison > 0))
            .ToList();

        int contestedRegions = enemyHoldings.Select(rf => rf.Region).Distinct().Count();
        if (contestedRegions == 0)
        {
            return
            [
                new TreeNode(0,
                    "Progress: no enemy holds ground — the world is liberated as of the coming turn",
                    [], selectable: false)
            ];
        }

        List<TreeNode> byFaction = enemyHoldings
            .GroupBy(rf => rf.PlanetFaction.Faction.Name)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new TreeNode(
                0,
                $"{group.Key}: {group.Count()} {Pluralize("region", group.Count())}",
                [],
                selectable: false))
            .ToList();

        return
        [
            new TreeNode(0,
                $"Progress: enemy present in {contestedRegions} of {promised.Regions.Length} regions",
                byFaction,
                selectable: false)
        ];
    }

    private static TreeNode CreateRequestNode(IRequest request)
    {
        string requesterName = request.Requester?.Name ?? "Unknown";
        string planetName = request.TargetPlanet?.Name ?? "Unknown";

        string concern = request.ThreatFaction != null
            ? $"Concern: {request.ThreatFaction.Name} in open revolt"
            : "Concern: unverified threat (no confirmed enemy presence)";
        string status = request.IsRequestStarted()
            ? "Status: Astartes engaged"
            : "Status: awaiting response";

        List<TreeNode> details =
        [
            new TreeNode(0, concern, [], selectable: false),
            new TreeNode(0,
                $"Commitment: {request.Commitment.PackageCount} {Pluralize(request.Commitment.DisplayUnitName, request.Commitment.PackageCount)} for {request.Commitment.ServiceWeeks} weeks",
                [], selectable: false),
            new TreeNode(0, $"Deadline: {FormatDate(request.Deadline)}", [], selectable: false),
            new TreeNode(0, $"Severity: {request.Severity}; risk: {request.Hazard}", [], selectable: false),
            new TreeNode(0, FormatProgress(request), [], selectable: false),
            new TreeNode(0, FormatOffer(request), [], selectable: false),
            new TreeNode(0, $"Requested: {FormatDate(request.DateRequestMade)}", [], selectable: false),
            new TreeNode(0, status, [], selectable: false)
        ];

        return new TreeNode(request.Id, $"{requesterName}, Governor of {planetName}", details, selectable: true);
    }

    private static string FormatOffer(IRequest request) => request.OfferedScheduleKind switch
    {
        PledgeScheduleKind.Standing =>
            $"Offer: standing tithe of {request.OfferedRequisition:N0} Requisition every {request.OfferedCadenceWeeks} weeks",
        _ => $"Offer: one-off pledge of {request.OfferedRequisition:N0} Requisition"
    };

    private static TreeNode CreatePledgeNode(Pledge pledge)
    {
        Sector sector = GameDataSingleton.Instance.Sector;
        string source = sector.Planets.TryGetValue(pledge.SourcePlanetId, out var planet)
            ? planet.Name
            : "Unknown world";
        string schedule = pledge.ScheduleKind == PledgeScheduleKind.OneOff
            ? $"Delivery: {FormatDate(pledge.NextDeliveryDate)}"
            : $"Next tithe: {FormatDate(pledge.NextDeliveryDate)}; every {pledge.CadenceWeeks} weeks";
        List<TreeNode> details =
        [
            new TreeNode(0, $"Source: {source}", [], selectable: false),
            new TreeNode(0, $"Status: {pledge.Status}", [], selectable: false),
            new TreeNode(0, schedule, [], selectable: false)
        ];
        string name = pledge.ScheduleKind == PledgeScheduleKind.OneOff
            ? $"{pledge.Payload.Amount:N0} Requisition — one-off"
            : $"{pledge.Payload.Amount:N0} Requisition — standing tithe";
        return new TreeNode(pledge.Id, name, details, selectable: false);
    }

    private static string FormatProgress(IRequest request)
    {
        if (request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed)
        {
            return "Progress: suppress the identified threat";
        }

        decimal packageWeeks = request.Commitment.ReferenceBattleValuePerPackage <= 0
            ? 0
            : (decimal)request.ProgressBattleValueTime
                / request.Commitment.ReferenceBattleValuePerPackage;
        decimal required = request.Commitment.PackageCount * request.Commitment.ServiceWeeks;
        string acceleration = request.Commitment.MaximumEffectivePackageCount
            > request.Commitment.PackageCount
                ? $"; up to {request.Commitment.MaximumEffectivePackageCount} squads contribute"
                : "";
        return $"Progress: {packageWeeks:0.#} of {required:0.#} squad-weeks{acceleration}";
    }

    private static string Pluralize(string unit, int count) => count == 1 ? unit : unit + "s";

    private static string FormatDate(Date date)
    {
        if (date == null) return "Unknown";
        // 41st-millennium style, e.g. "500.M41"
        return $"{date.Year:000}.M{date.Millenium} (week {date.Week})";
    }
}
