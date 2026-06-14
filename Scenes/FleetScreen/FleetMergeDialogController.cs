using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetMergeDialogController : DialogController
{
    private FleetMergeDialogView _view;
    private TaskForce _taskForce;
    private TaskForce _selectedTarget;

    public event EventHandler FleetsMerged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetMergeDialogView>("FleetMergeDialogView");
        _view.TargetSelected += OnTargetSelected;
        _view.MergePressed += OnMergePressed;
    }

    public void SetTaskForce(TaskForce taskForce)
    {
        _taskForce = taskForce;
        _selectedTarget = null;
        _view.SetHeader($"Task Force {taskForce.Id} — Merge");

        List<KeyValuePair<int, string>> targets = GetMergeCandidates(taskForce)
            .Select(candidate => new KeyValuePair<int, string>(
                candidate.Id, $"Task Force {candidate.Id} ({candidate.Ships.Count} ship(s))"))
            .ToList();
        _view.PopulateTargets(targets);
    }

    public static IEnumerable<TaskForce> GetMergeCandidates(TaskForce taskForce)
    {
        return GameDataSingleton.Instance.Sector.Fleets.Values
            .Where(other => other.Id != taskForce.Id
                && other.Faction == taskForce.Faction
                && other.Planet == taskForce.Planet
                && other.TravelPhase == FleetTravelPhase.InOrbit)
            .OrderBy(other => other.Id);
    }

    private void OnTargetSelected(object sender, int fleetId)
    {
        _selectedTarget = GameDataSingleton.Instance.Sector.Fleets[fleetId];
        _view.SetDetail(
            $"Merge Task Force {_selectedTarget.Id} ({_selectedTarget.Ships.Count} ship(s)) "
            + $"into Task Force {_taskForce.Id}.", true);
    }

    private void OnMergePressed(object sender, EventArgs e)
    {
        if (_taskForce == null || _selectedTarget == null) return;

        // The clicked task force is retained; the selected target is folded into it.
        GameDataSingleton.Instance.Sector.CombineFleets(_taskForce, _selectedTarget);
        FleetsMerged?.Invoke(this, EventArgs.Empty);
    }
}
