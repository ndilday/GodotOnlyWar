using Godot;
using System;
using System.Collections.Generic;

public enum CompanyType
{
    Veteran,
    Tactical,
    ReserveTactical,
    ReserveAssault,
    ReserveDevastator,
    Scout
}

public partial class ChapterView : Control
{
    private VBoxContainer _companyVBox;
    private VBoxContainer _squadVBox;
    private VBoxContainer _soldierVBox;
    private ButtonGroup _companyButtonGroup;
    private ButtonGroup _squadButtonGroup;

    public event EventHandler<int> CompanyButtonPressed;
    public event EventHandler<int> SquadButtonPressed;
    public event EventHandler<int> SoldierButtonPressed;
    public override void _Ready()
    {
        // Called every time the node is added to the scene
        _companyVBox = GetNode<VBoxContainer>("CompanyList/VBoxContainer");
        _squadVBox = GetNode<VBoxContainer>("SquadList/ScrollContainer/VBoxContainer");
        _soldierVBox = GetNode<VBoxContainer>("SoldierList/VBoxContainer");
        _companyButtonGroup = new ButtonGroup();
        _squadButtonGroup = new ButtonGroup();
    }

    public void PopulateCompanyList(IReadOnlyList<Tuple<int, CompanyType, string>> companies)
    {
        ClearVBox(_soldierVBox);
        ClearVBox(_squadVBox);
        ClearVBox(_companyVBox);
        foreach (Tuple<int, CompanyType, string> company in companies)
        {
            AddCompany(company.Item1, company.Item2, company.Item3);
        }
    }

    private void AddCompany(int id, CompanyType type, string name)
    {
        Button companyButton = new Button();
        companyButton.Text = name;
        companyButton.SetMeta("id", id);
        companyButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        companyButton.IconAlignment = HorizontalAlignment.Left;
        companyButton.ExpandIcon = true;
        companyButton.Pressed += () => CompanyButtonPressed?.Invoke(this, id);
        companyButton.ToggleMode = true;
        companyButton.ButtonGroup = _companyButtonGroup;
        switch (type)
        {
            case CompanyType.Veteran:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/elite-icon.jpg");
                break;
            case CompanyType.Tactical:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/boltgun_chainsword_icon.png");
                break;
            case CompanyType.ReserveTactical:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/tactical_reserve.png");
                break;
            case CompanyType.ReserveAssault:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/assault-reserve-icon.png");
                break;
            case CompanyType.ReserveDevastator:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/devastator-reserve-icon.png");
                break;
            case CompanyType.Scout:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/scout-reserve-icon.png");
                break;
        }
        _companyVBox.AddChild(companyButton);

    }

    public void PopulateSquadList(IReadOnlyList<Tuple<int, string>> squads)
    {
        ClearVBox(_squadVBox);
        foreach (Tuple<int, string> squad in squads)
        {
            AddSquad(squad.Item1, squad.Item2);
        }
    }

    private void ClearVBox(VBoxContainer vbox)
    {
        var existingButtons = vbox.GetChildren();
        if (existingButtons != null)
        {
            foreach (var child in existingButtons)
            {
                vbox.RemoveChild(child);
                child.QueueFree();
            }
        }
    }

    private void AddSquad(int id, string name)
    {
        Button squadButton = new Button();
        squadButton.Text = name;
        squadButton.SetMeta("id", id);
        squadButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        squadButton.Pressed += () => SquadButtonPressed?.Invoke(this, id);
        squadButton.ToggleMode = true;
        squadButton.ButtonGroup = _squadButtonGroup;
        _squadVBox.AddChild(squadButton);
    }

    public void PopulateSoldierList(IReadOnlyList<Tuple<int, string>> soldiers)
    {
        ClearVBox(_soldierVBox);
        if (soldiers == null || soldiers.Count == 0)
        {
            _soldierVBox.AddChild(new Label() { Text = "No soldiers in this squad" });
        }
        else
        {
            foreach (Tuple<int, string> soldier in soldiers)
            {
                AddSoldier(soldier.Item1, soldier.Item2);
            }
        }
    }

    private void AddSoldier(int id, string name)
    {
        Button soldierButton = new Button();
        soldierButton.Text = name;
        soldierButton.SetMeta("id", id);
        soldierButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        soldierButton.Pressed += () => SoldierButtonPressed?.Invoke(this, id);
        _soldierVBox.AddChild(soldierButton);
    }

    //private void 
}
