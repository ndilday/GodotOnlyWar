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
    public event EventHandler<int> CompanyButtonPressed;
    public event EventHandler<int> SquadButtonPressed;
    public override void _Ready()
    {
        // Called every time the node is added to the scene
        _companyVBox = GetNode<VBoxContainer>("CompanyList/VBoxContainer");
        _squadVBox = GetNode<VBoxContainer>("SquadList/VBoxContainer");
    }

    public void PopulateCompanyList(IReadOnlyList<Tuple<int, CompanyType, string>> companies)
    {
        var existingButtons = _companyVBox.GetChildren();
        if(existingButtons != null)
        {
            foreach (var child in existingButtons)
            {
                _companyVBox.RemoveChild(child);
                child.QueueFree();
            }
        }
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
        /*companyLabel.Connect("mouse_entered", this, nameof(OnCompanyMouseEnter), new Godot.Collections.Array { id });
        companyLabel.Connect("mouse_exited", this, nameof(OnCompanyMouseExit), new Godot.Collections.Array { id });
        companyLabel.Connect("mouse_button_pressed", this, nameof(OnCompanyMousePressed), new Godot.Collections.Array { id });*/
        _companyVBox.AddChild(companyButton);
        
    }

    public void PopulateSquadList(IReadOnlyList<Tuple<int, string>> squads)
    {
        var existingButtons = _squadVBox.GetChildren();
        if (existingButtons != null)
        {
            foreach (var child in existingButtons)
            {
                _squadVBox.RemoveChild(child);
                child.QueueFree();
            }
        }
        foreach (Tuple<int, string> squad in squads)
        {
            AddSquad(squad.Item1, squad.Item2);
        }
    }

    private void AddSquad(int id, string name)
    {
        Button squadButton = new Button();
        squadButton.Text = name;
        squadButton.SetMeta("id", id);
        squadButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        squadButton.Pressed += () => SquadButtonPressed?.Invoke(this, id);
        _squadVBox.AddChild(squadButton);

    }
}
