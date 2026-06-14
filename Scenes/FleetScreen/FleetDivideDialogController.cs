using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetDivideDialogController : DialogController
{
    private FleetDivideDialogView _view;
    private TaskForce _taskForce;

    public event EventHandler FleetDivided;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetDivideDialogView>("FleetDivideDialogView");
        _view.SelectionChanged += OnSelectionChanged;
        _view.DividePressed += OnDividePressed;
    }

    public void SetTaskForce(TaskForce taskForce)
    {
        _taskForce = taskForce;
        _view.SetHeader($"Task Force {taskForce.Id} — Divide");
        List<KeyValuePair<int, string>> ships = taskForce.Ships
            .OrderBy(ship => ship.Template.Id)
            .Select(ship => new KeyValuePair<int, string>(
                ship.Id, $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})"))
            .ToList();
        _view.PopulateShips(ships);
    }

    private void OnSelectionChanged(object sender, EventArgs e)
    {
        int selectedCount = _view.GetSelectedShipIds().Count;
        int total = _taskForce.Ships.Count;

        if (selectedCount == 0)
        {
            _view.SetDetail("Select the ships to peel off into a new task force.", false);
        }
        else if (selectedCount >= total)
        {
            _view.SetDetail("At least one ship must remain in the original task force.", false);
        }
        else
        {
            _view.SetDetail(
                $"{selectedCount} ship(s) will form a new task force; {total - selectedCount} will remain.", true);
        }
    }

    private void OnDividePressed(object sender, EventArgs e)
    {
        IReadOnlyList<int> selectedIds = _view.GetSelectedShipIds();
        if (selectedIds.Count == 0 || selectedIds.Count >= _taskForce.Ships.Count) return;

        HashSet<int> idSet = selectedIds.ToHashSet();
        List<Ship> shipsToSplit = _taskForce.Ships.Where(ship => idSet.Contains(ship.Id)).ToList();

        GameDataSingleton.Instance.Sector.SplitOffNewFleet(_taskForce, shipsToSplit);
        FleetDivided?.Invoke(this, EventArgs.Empty);
    }
}
