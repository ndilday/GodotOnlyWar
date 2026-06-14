using Godot;
using System;
using System.Collections.Generic;

public partial class FleetMoveDialogView : DialogView
{
    private ItemList _destinationList;
    private Label _routeDetailLabel;
    private Button _plotCourseButton;

    public event EventHandler<int> DestinationSelected;
    public event EventHandler PlotCoursePressed;

    public override void _Ready()
    {
        base._Ready();
        _destinationList = GetNode<ItemList>("Panel/DestinationList");
        _routeDetailLabel = GetNode<Label>("Panel/RouteDetailLabel");
        _plotCourseButton = GetNode<Button>("Panel/PlotCourseButton");

        _destinationList.ItemSelected += OnDestinationItemSelected;
        _plotCourseButton.Pressed += () => PlotCoursePressed?.Invoke(this, EventArgs.Empty);
        _plotCourseButton.Disabled = true;
    }

    public void SetHeader(string text)
    {
        GetNode<Label>("Panel/Header/Label").Text = text;
    }

    public void PopulateDestinations(IReadOnlyList<KeyValuePair<int, string>> destinations)
    {
        _destinationList.Clear();
        foreach (KeyValuePair<int, string> destination in destinations)
        {
            int index = _destinationList.AddItem(destination.Value);
            _destinationList.SetItemMetadata(index, destination.Key);
        }
        _routeDetailLabel.Text = "Select a destination to plot a course.";
        _plotCourseButton.Disabled = true;
    }

    public void SetRouteDetail(string text, bool canPlot)
    {
        _routeDetailLabel.Text = text;
        _plotCourseButton.Disabled = !canPlot;
    }

    private void OnDestinationItemSelected(long index)
    {
        int planetId = (int)_destinationList.GetItemMetadata((int)index);
        DestinationSelected?.Invoke(this, planetId);
    }
}
