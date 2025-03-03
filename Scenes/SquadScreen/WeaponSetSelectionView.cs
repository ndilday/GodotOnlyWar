using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class WeaponSetSelectionView : PanelContainer
{
    private List<Tuple<string, int>> _weaponSets;
    private List<WeaponSetRowView> _weaponSetRows;
    private VBoxContainer _weaponSetVBox;
    private RichTextLabel _header;
    private int _minimumCount;
    private int _maximumCount;
    private int _temporaryMaximumCount;
    public string OptionName { get; private set; }

    public event EventHandler<Tuple<string, int>> WeaponSetCountChanged;
    public override void _Ready()
    {
        _weaponSetVBox = GetNode<VBoxContainer>("VBoxContainer");
        _header = GetNode<RichTextLabel>("VBoxContainer/RichTextLabel");
        _weaponSetRows = new List<WeaponSetRowView>();
    }

    public void Initialize(List<Tuple<string, int>> weaponSets, string name, int minimumCount, int maximumCount)
    {
        OptionName = name;
        ClearWeaponSetRows();
        _weaponSets = weaponSets;
        _minimumCount = minimumCount;
        _maximumCount = maximumCount;
        
        foreach(Tuple<string, int> weaponSet in _weaponSets)
        {
            PackedScene weaponSetRowScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/weapon_set_row.tscn");
            WeaponSetRowView row = (WeaponSetRowView)weaponSetRowScene.Instantiate();
            _weaponSetVBox.AddChild(row);
            row.SetWeaponSetName(weaponSet.Item1);
            row.MinimumCount = _minimumCount;
            row.MaximumCount = _maximumCount;
            row.SetCount(weaponSet.Item2);
            row.CountChanged += OnCountChanged;
            
            _weaponSetRows.Add(row);
        }
        UpdateHeaderText();
    }

    public List<Tuple<string, int>> GetWeaponSetCounts()
    {
        List<Tuple<string, int>> weaponSetCounts = new List<Tuple<string, int>>();
        foreach (WeaponSetRowView row in _weaponSetRows)
        {
            weaponSetCounts.Add(new Tuple<string, int>(row.WeaponSetName, row.Count));
        }
        return weaponSetCounts;
    }

    public void DisableInrease(bool disable)
    {
        int total = _weaponSetRows.Sum(row => row.Count);
        foreach (WeaponSetRowView row in _weaponSetRows)
        {
            row.MaximumCount = disable ? row.Count : row.Count + (_maximumCount - total);
        }
    }

    private void ClearWeaponSetRows()
    {
        if (_weaponSetRows != null)
        {
            foreach (WeaponSetRowView row in _weaponSetRows)
            {
                _weaponSetVBox.RemoveChild(row);
                row.CountChanged -= OnCountChanged;
                row.QueueFree();
            }
        }
    }

    private void UpdateHeaderText()
    {
        int total = 0;
        foreach (WeaponSetRowView child in _weaponSetRows)
        {
            total += child.Count;
        }
        _header.Text = $"Minimum: {_minimumCount}, Maximum: {_maximumCount}, Current Total: {total}";
    }

    private void OnCountChanged(object sender, int newCount)
    {
        WeaponSetRowView row = (WeaponSetRowView)sender;
        // sum all the counts
        int total = 0;
        foreach (WeaponSetRowView child in _weaponSetRows)
        {
            total += child.Count;
        }
        // the below will break if maximum = minimum, probably want to replace with some sort of warning when outside limits
        if (total >= _maximumCount)
        {
            // set the max of each weapon set row to its current value
            foreach (WeaponSetRowView child in _weaponSetRows)
            {
                child.MaximumCount = child.Count;
            }
        }
        else if (total <= _minimumCount)
        {
            // set the min of each weapon set row to its current value
            foreach (WeaponSetRowView child in _weaponSetRows)
            {
                child.MinimumCount = child.Count;
            }
        }
        else
        {
            // set the min of each weapon set to 0, and the max to the current count + the difference between the total and the max
            foreach (WeaponSetRowView child in _weaponSetRows)
            {
                child.MinimumCount = 0;
                child.MaximumCount = child.Count + (_maximumCount - total);
            }
        }
        UpdateHeaderText();
        Tuple<string, int> eventData = new Tuple<string, int>(row.WeaponSetName, newCount);
        WeaponSetCountChanged.Invoke(this, eventData);
    }
}
