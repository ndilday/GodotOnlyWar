using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DiplomacyScreenController : DialogController
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

        List<IRequest> activeRequests = GameDataSingleton.Instance.Sector.PlayerForce.Requests
            .Where(request => request.Status is RequestStatus.Open or RequestStatus.InProgress)
            .OrderBy(request => request.DateRequestMade.GetTotalWeeks())
            .ToList();

        List<TreeNode> nodes = activeRequests.Count == 0
            ? [new TreeNode(0, "No outstanding requests from the sector's governors.", [], selectable: false)]
            : activeRequests.Select(CreateRequestNode).ToList();

        List<Pledge> pledges = GameDataSingleton.Instance.Sector.PlayerForce.Pledges
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

        _view.PopulateRequestTree(nodes);
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

        return new TreeNode(request.Id, $"{requesterName}, Governor of {planetName}", details, selectable: false);
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
