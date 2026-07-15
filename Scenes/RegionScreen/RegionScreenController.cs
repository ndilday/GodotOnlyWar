using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RegionScreenController : DialogController
{
    private static readonly (string Key, string Label)[] RosterFilters =
    [
        ("all", "All"),
        ("unassigned", "Unassigned"),
        ("injured", "Injured")
    ];

    private RegionScreenView _view;
    private Region _currentRegion;
    private Region _targetRegion;
    private readonly List<Squad> _selectedSquads = [];
    private AvailableMission _selectedMission;
    private Aggression _aggression = Aggression.Normal;
    private int _selectedTargetFactionId = -1;
    private OrderDialogController _orderDialog;
    private string _rosterFilter = "all";

    public event EventHandler<Squad> SquadDoubleClicked;
    public event EventHandler<Region> AdjacentRegionChangeRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<RegionScreenView>("DialogView");
        _orderDialog = GetNode<OrderDialogController>("DialogView/OrderDialogController");

        _view.SelectionTreeItemSelected += OnSelectionTreeItemSelected;
        _view.SelectionTreeItemActivated += OnSelectionTreeItemActivated;
        _view.AdjacentRegionClicked += OnAdjacentRegionClicked;
        _view.TargetRegionSelected += OnTargetRegionSelected;
        _view.MissionSelected += OnMissionSelected;
        _view.AggressionChanged += OnAggressionChanged;
        _view.AssignPressed += OnAssignPressed;
        _view.UnassignPressed += OnUnassignPressed;
        _view.TargetFactionSelected += OnTargetFactionSelected;
        _view.InboundOrderActivated += OnInboundOrderActivated;
        _view.RosterFilterSelected += OnRosterFilterSelected;
        _orderDialog.OrdersConfirmed += OnOrdersConfirmed;

        _view.SetRosterFilters(RosterFilters);
        _view.SetActiveRosterFilter(_rosterFilter);
        _view.SetSelectionMultiSelect(true);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_view))
        {
            _view.SelectionTreeItemSelected -= OnSelectionTreeItemSelected;
            _view.SelectionTreeItemActivated -= OnSelectionTreeItemActivated;
            _view.AdjacentRegionClicked -= OnAdjacentRegionClicked;
            _view.TargetRegionSelected -= OnTargetRegionSelected;
            _view.MissionSelected -= OnMissionSelected;
            _view.AggressionChanged -= OnAggressionChanged;
            _view.AssignPressed -= OnAssignPressed;
            _view.UnassignPressed -= OnUnassignPressed;
            _view.TargetFactionSelected -= OnTargetFactionSelected;
            _view.InboundOrderActivated -= OnInboundOrderActivated;
            _view.RosterFilterSelected -= OnRosterFilterSelected;
        }
        if (GodotObject.IsInstanceValid(_orderDialog))
        {
            _orderDialog.OrdersConfirmed -= OnOrdersConfirmed;
        }
    }

    private void OnRosterFilterSelected(object sender, string key)
    {
        _rosterFilter = key;
        _view.SetActiveRosterFilter(_rosterFilter);
        RefreshWorkspace();
    }

    public void DisplayRegion(Region region)
    {
        _currentRegion = region;
        _targetRegion = region;
        _selectedSquads.Clear();
        _selectedMission = null;
        _selectedTargetFactionId = -1;
        _aggression = Aggression.Normal;
        RefreshWorkspace();
    }

    public void RefreshFromExternalChange()
    {
        RefreshWorkspace();
    }

    private void OnSelectionTreeItemSelected(object sender, string key)
    {
        RecomputeSelectedSquads();
        UpdateSelectionSummary();
        RefreshCommitBar();
    }

    private void OnSelectionTreeItemActivated(object sender, string key)
    {
        Squad squad = ResolveSquadFromKey(key);
        if (squad != null)
        {
            SquadDoubleClicked?.Invoke(this, squad);
        }
    }

    private void OnUnassignPressed(object sender, EventArgs e)
    {
        UnassignSelectedSquads();
    }

    private void OnAdjacentRegionClicked(object sender, Region region)
    {
        AdjacentRegionChangeRequested?.Invoke(this, region);
    }

    private void OnTargetRegionSelected(object sender, Region region)
    {
        if (region == null || region == _targetRegion) return;

        _targetRegion = region;
        _selectedMission = null;
        _selectedTargetFactionId = -1;
        RefreshTargetPicker();
        RefreshMissionsAndOrders();
        RefreshDossier();
        RefreshCommitBar();
    }

    private void OnMissionSelected(object sender, AvailableMission mission)
    {
        _selectedMission = mission;
        _selectedTargetFactionId = -1;
        RefreshTargetFactionSelector();
        RefreshCommitBar();
    }

    private void OnAggressionChanged(object sender, Aggression aggression)
    {
        _aggression = aggression;
        RefreshCommitBar();
    }

    private void OnTargetFactionSelected(object sender, int targetFactionId)
    {
        _selectedTargetFactionId = targetFactionId;
        RefreshCommitBar();
    }

    private void OnInboundOrderActivated(object sender, Order order)
    {
        Squad squad = order.AssignedSquads.FirstOrDefault();
        if (squad == null) return;

        OpenOrdersDialog(squad);
    }

    private void OnAssignPressed(object sender, EventArgs e)
    {
        if (_selectedSquads.Count == 0 || _selectedMission == null || _targetRegion == null) return;

        int targetFactionId = ResolveTargetFactionId();
        Order newOrder = OrderAssignment.AssignSquadsToMission(
            _selectedSquads.ToList(), _targetRegion, _selectedMission, targetFactionId, _aggression);
        if (newOrder == null)
        {
            GD.PushWarning($"Could not assign squads to {_selectedMission.Label} vs {_targetRegion.Name}: mission target could not be resolved.");
            return;
        }

        _selectedSquads.Clear();
        _view.ClearSelection();
        RefreshWorkspace();
    }

    private void OnOrdersConfirmed(object sender, EventArgs e)
    {
        RefreshWorkspace();
    }

    private void RefreshWorkspace()
    {
        if (_currentRegion == null) return;

        _view.SetHeader($"{_currentRegion.Planet.Name} / {_currentRegion.Name}");
        _view.SetSelectionTitle("ROSTER", "Select squads. Pick a target hex and mission, then assign.");
        _view.PopulateSelectionTree(BuildRoster());
        RecomputeSelectedSquads();
        UpdateSelectionSummary();

        RefreshTargetPicker();
        RefreshMissionsAndOrders();
        RefreshTargetFactionSelector();
        RefreshDossier();
        RefreshCommitBar();
    }

    private void RefreshTargetPicker()
    {
        if (_currentRegion == null) return;

        Dictionary<string, Region> adjacentRegionMap = _currentRegion.GetAdjacentRegions()
            .Select(region => new { Direction = GetDirectionFromCurrentToNeighbour(_currentRegion, region), Region = region })
            .Where(entry => entry.Direction != null)
            .ToDictionary(entry => entry.Direction, entry => entry.Region);

        _view.PopulateAdjacentRegions(_currentRegion, adjacentRegionMap, _targetRegion);
    }

    private void RefreshMissionsAndOrders()
    {
        if (_currentRegion == null || _targetRegion == null) return;

        IReadOnlyList<AvailableMission> missions = MissionAvailability.GetAvailableMissions(_currentRegion, _targetRegion);
        if (_selectedMission != null && !missions.Any(m => m.Kind == _selectedMission.Kind && m.Label == _selectedMission.Label))
        {
            _selectedMission = null;
        }

        _view.SetMissionsHeader(_targetRegion.Name, BuildMissionsFlagTexts(_targetRegion));
        _view.SetMissions(missions, _selectedMission);
    }

    internal static IReadOnlyList<string> BuildMissionsFlagTexts(Region targetRegion)
    {
        return GetPublicEnemyRegionFactions(targetRegion)
            .Select(regionFaction => regionFaction.PlanetFaction.Faction.Name)
            .ToList();
    }

    private void RefreshTargetFactionSelector()
    {
        bool relevant = _selectedMission != null
            && (_selectedMission.Kind == MissionAvailabilityKind.Attack || _selectedMission.Kind == MissionAvailabilityKind.Diversion)
            && _targetRegion != null;

        List<RegionFaction> enemies = relevant ? GetPublicEnemyRegionFactions(_targetRegion) : [];

        if (!relevant || enemies.Count <= 1)
        {
            _selectedTargetFactionId = enemies.Count == 1 ? enemies[0].PlanetFaction.Faction.Id : -1;
            _view.SetTargetFactionOptions(Array.Empty<(string, int)>(), false);
            return;
        }

        List<(string Name, int Id)> options = enemies
            .Select(rf => (Name: $"{rf.PlanetFaction.Faction.Name} — {rf.GetForceMagnitudeDescription()}", Id: rf.PlanetFaction.Faction.Id))
            .ToList();
        if (_selectedTargetFactionId < 0 || enemies.All(rf => rf.PlanetFaction.Faction.Id != _selectedTargetFactionId))
        {
            _selectedTargetFactionId = options[0].Id;
        }
        _view.SetTargetFactionOptions(options, true);
    }

    private int ResolveTargetFactionId()
    {
        if (_selectedMission == null) return -1;
        if (_selectedMission.Kind != MissionAvailabilityKind.Attack && _selectedMission.Kind != MissionAvailabilityKind.Diversion) return -1;
        return _selectedTargetFactionId;
    }

    private void RefreshCommitBar()
    {
        _view.SetAggression(_aggression);
        int count = _selectedSquads.Count;
        bool enabled = count > 0 && _selectedMission != null;
        _view.SetAssignButton(count == 1 ? "Assign 1 Squad" : $"Assign {count} Squads", enabled);
        _view.SetUnassignButton(_selectedSquads.Any(squad => squad.CurrentOrders != null));
    }

    private void RefreshDossier()
    {
        Region target = _targetRegion ?? _currentRegion;
        if (target == null) return;

        List<DossierCardData> cards = [];
        float visibleIntel = target.GetPlayerVisibleIntel();
        List<RegionFaction> enemyFactions = GetPublicEnemyRegionFactions(target);

        // Region first, then the single Local Force card, then any number of hostile faction
        // cards last - so the fixed cards stay anchored at the top regardless of how many
        // enemies contest the region.
        List<Tuple<string, string>> regionRows =
        [
            Row("Control", GetRegionControlLabel(target)),
            Row("Intel rating", $"{visibleIntel:0.##}"),
            Row("Civilians", target.HasHiddenDefaultFaction() ? "Unknown" : target.GetVisibleCivilianPopulation().ToString("N0"))
        ];
        cards.Add(new DossierCardData("Region", target.Name, regionRows, OnlyWarStyle.Gold));

        long garrison = target.PlanetaryDefenseForces;
        List<Tuple<string, string>> localRows = [Row("Strength", garrison > 0 ? garrison.ToString("N0") : "None")];
        RegionFaction alliedFaction = target.RegionFactionMap.Values
            .FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        if (alliedFaction != null)
        {
            // Allied (PDF/Imperial) defenses are always visible to the player - no intel gate,
            // unlike the hostile cards which only reveal fortifications once intel is sufficient.
            localRows.Add(Row("Entrenchment", RegionFactionExtensions.GetDefenseLevelDescription(alliedFaction.Entrenchment)));
            localRows.Add(Row("Listening Posts", RegionFactionExtensions.GetDefenseLevelDescription(alliedFaction.ListeningPost)));
            localRows.Add(Row("Anti-Air", RegionFactionExtensions.GetDefenseLevelDescription(alliedFaction.AntiAir)));
        }
        cards.Add(new DossierCardData("Local Force", "PDF Garrison", localRows, OnlyWarStyle.MedicalStable));

        foreach (RegionFaction enemyFaction in enemyFactions)
        {
            List<Tuple<string, string>> rows = [Row("Force magnitude", enemyFaction.GetForceMagnitudeDescription())];
            if (visibleIntel > 1)
            {
                rows.Add(Row("Entrenchment", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.Entrenchment)));
                rows.Add(Row("Listening Posts", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.ListeningPost)));
                rows.Add(Row("Anti-Air", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.AntiAir)));
            }
            cards.Add(new DossierCardData(
                "Hostile Faction",
                enemyFaction.PlanetFaction.Faction.Name,
                rows,
                OnlyWarStyle.OpposingAccent,
                GetMagnitudeBarFraction(enemyFaction.GetForceMagnitudeDescription())));
        }

        _view.SetDossier(target.Name, cards);
        _view.SetInboundOrders(InboundOrders.ForRegion(target));
    }

    private static float? GetMagnitudeBarFraction(string magnitudeWord)
    {
        return magnitudeWord switch
        {
            "None" => 0f,
            "Handful" => 0.15f,
            "Dozens" => 0.3f,
            "Hundreds" => 0.5f,
            "Thousands" => 0.7f,
            "Millions" => 0.85f,
            "Billions" => 1f,
            _ => null
        };
    }

    private IReadOnlyList<CommandTreeNode> BuildRoster()
    {
        if (_currentRegion == null) return Array.Empty<CommandTreeNode>();

        return BuildUnitNodes();
    }

    private List<CommandTreeNode> BuildUnitNodes()
    {
        RegionFaction playerFaction = GetPlayerRegionFaction();
        List<CommandTreeNode> units = [];
        if (playerFaction != null)
        {
            foreach (IGrouping<OnlyWar.Models.Units.Unit, Squad> group in playerFaction.LandedSquads
                .Where(squad => squad.Members.Count > 0)
                .GroupBy(squad => squad.ParentUnit))
            {
                List<CommandTreeNode> squadNodes = group
                    .Where(squad => RosterFormat.MatchesFilter(squad, _rosterFilter))
                    .Select(squad => new CommandTreeNode(
                        SquadKey(squad.Id),
                        SquadRosterLabel(squad),
                        null,
                        IconAtlas.GetSquadIconKey(squad.SquadTemplate),
                        squad.CurrentOrders?.Mission.RegionFaction.Region.Name ?? "None"))
                    .ToList();

                if (squadNodes.Count == 0) continue;

                units.Add(new CommandTreeNode(
                    $"unit:{group.Key.Id}",
                    $"{group.Key.Name} · {group.Sum(squad => squad.Members.Count)} marines",
                    squadNodes,
                    null,
                    null,
                    false));
            }
        }

        return units;
    }

    private static string SquadRosterLabel(Squad squad)
    {
        string strength = $"{squad.Members.Count(member => member.CanFight)}/{squad.Members.Count}";
        return $"{squad.Name} | {strength}";
    }

    private void UpdateSelectionSummary()
    {
        int count = _selectedSquads.Count;
        int fighting = _selectedSquads.Sum(squad => squad.Members.Count(member => member.CanFight));
        string summary = count == 0
            ? "Select squads. Pick a target hex and mission, then assign."
            : $"{count} squad{(count == 1 ? "" : "s")} selected · {fighting} fighting";
        _view.SetSelectionTitle("ROSTER", summary);
    }

    private void RecomputeSelectedSquads()
    {
        _selectedSquads.Clear();
        RegionFaction playerFaction = GetPlayerRegionFaction();
        if (playerFaction == null) return;

        HashSet<int> selectedIds = _view.GetSelectedKeys()
            .Where(key => key.StartsWith("squad:"))
            .Select(key => int.Parse(key.Split(':')[1]))
            .ToHashSet();
        if (selectedIds.Count == 0) return;

        _selectedSquads.AddRange(playerFaction.LandedSquads.Where(squad => selectedIds.Contains(squad.Id)));
    }

    private Squad ResolveSquadFromKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("squad:")) return null;
        int squadId = int.Parse(key.Split(':')[1]);
        return GetPlayerRegionFaction()?.LandedSquads.FirstOrDefault(squad => squad.Id == squadId);
    }

    private void OpenOrdersDialog(Squad squad)
    {
        if (squad != null)
        {
            _orderDialog.PopulateOrderData(squad);
            _orderDialog.Visible = true;
        }
        else
        {
            GD.PrintErr("Cannot open orders dialog: No squad selected.");
        }
    }

    private void UnassignSelectedSquads()
    {
        List<Squad> squadsWithOrders = _selectedSquads.Where(squad => squad.CurrentOrders != null).ToList();
        if (squadsWithOrders.Count == 0) return;

        foreach (Squad squad in squadsWithOrders)
        {
            Order order = squad.CurrentOrders;
            order.AssignedSquads.Remove(squad);
            squad.CurrentOrders = null;
            if (order.AssignedSquads.Count == 0)
            {
                GameDataSingleton.Instance.Sector.RemoveOrder(order);
            }
        }

        RefreshWorkspace();
    }

    private RegionFaction GetPlayerRegionFaction()
    {
        return GetPlayerRegionFaction(_currentRegion);
    }

    private static RegionFaction GetPlayerRegionFaction(Region region)
    {
        if (region == null) return null;
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction regionFaction);
        return regionFaction;
    }

    private static List<RegionFaction> GetPublicEnemyRegionFactions(Region region)
    {
        return region.RegionFactionMap.Values
            .Where(rf => rf.IsPublic && !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction)
            .OrderBy(rf => rf.PlanetFaction.Faction.Name)
            .ThenBy(rf => rf.PlanetFaction.Faction.Id)
            .ToList();
    }

    private static string GetRegionControlLabel(Region region)
    {
        return region.ControllingFaction?.PlanetFaction.Faction.Name ?? "Contested";
    }

    private static Tuple<string, string> Row(string label, string value)
    {
        return new Tuple<string, string>(label, value);
    }

    private static string SquadKey(int squadId) => $"squad:{squadId}";

    private string GetDirectionFromCurrentToNeighbour(Region currentRegion, Region neighbourRegion)
    {
        // Hex board: row is X (increasing downward = south) and horizontal offset is (2*Y - X).
        // These six offsets match the flat-top tiling used by the planet-detail map; see
        // RegionExtensions.GetAdjacentRegions.
        int dx = neighbourRegion.Coordinates.X - currentRegion.Coordinates.X;
        int dy = neighbourRegion.Coordinates.Y - currentRegion.Coordinates.Y;

        if (dx == -2 && dy == -1) return "N";
        if (dx == -1 && dy == 0) return "NE";
        if (dx == 1 && dy == 1) return "SE";
        if (dx == 2 && dy == 1) return "S";
        if (dx == 1 && dy == 0) return "SW";
        if (dx == -1 && dy == -1) return "NW";

        return null;
    }
}
