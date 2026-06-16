using Godot;
using OnlyWar.Models;
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
            .Where(request => !request.IsRequestCompleted())
            .OrderBy(request => request.DateRequestMade.GetTotalWeeks())
            .ToList();

        List<TreeNode> nodes = activeRequests.Count == 0
            ? [new TreeNode(0, "No outstanding requests from the sector's governors.", [], selectable: false)]
            : activeRequests.Select(CreateRequestNode).ToList();

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
            new TreeNode(0, $"Requested: {FormatDate(request.DateRequestMade)}", [], selectable: false),
            new TreeNode(0, status, [], selectable: false)
        ];

        return new TreeNode(request.Id, $"{requesterName}, Governor of {planetName}", details, selectable: false);
    }

    private static string FormatDate(Date date)
    {
        if (date == null) return "Unknown";
        // 41st-millennium style, e.g. "500.M41"
        return $"{date.Year:000}.M{date.Millenium} (week {date.Week})";
    }
}
