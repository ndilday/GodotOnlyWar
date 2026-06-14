using Godot;
using System;
using System.Collections.Generic;

public partial class FleetDivideDialogView : DialogView
{
    private ItemList _shipList;
    private Label _detailLabel;
    private Button _divideButton;

    public event EventHandler SelectionChanged;
    public event EventHandler DividePressed;

    public override void _Ready()
    {
        base._Ready();
        _shipList = GetNode<ItemList>("Panel/ShipList");
        _detailLabel = GetNode<Label>("Panel/DetailLabel");
        _divideButton = GetNode<Button>("Panel/DivideButton");

        _shipList.MultiSelected += (long _, bool _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _divideButton.Pressed += () => DividePressed?.Invoke(this, EventArgs.Empty);
        _divideButton.Disabled = true;
    }

    public void SetHeader(string text)
    {
        GetNode<Label>("Panel/Header/Label").Text = text;
    }

    public void PopulateShips(IReadOnlyList<KeyValuePair<int, string>> ships)
    {
        _shipList.Clear();
        foreach (KeyValuePair<int, string> ship in ships)
        {
            int index = _shipList.AddItem(ship.Value);
            _shipList.SetItemMetadata(index, ship.Key);
        }
        _detailLabel.Text = "Select the ships to peel off into a new task force.";
        _divideButton.Disabled = true;
    }

    public IReadOnlyList<int> GetSelectedShipIds()
    {
        List<int> selected = [];
        foreach (int index in _shipList.GetSelectedItems())
        {
            selected.Add((int)_shipList.GetItemMetadata(index));
        }
        return selected;
    }

    public void SetDetail(string text, bool canDivide)
    {
        _detailLabel.Text = text;
        _divideButton.Disabled = !canDivide;
    }
}
