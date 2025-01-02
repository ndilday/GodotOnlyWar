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
    public void _Ready()
    {
        // Called every time the node is added to the scene
        _companyVBox = GetNode<VBoxContainer>("CompanyPanel/VBoxContainer");
    }

    public void PopulateCompanyList(IReadOnlyList<Tuple<int, CompanyType, string>> companies)
    {
        foreach (Tuple<int, CompanyType, string> company in companies)
        {
            AddCompany(company.Item1, company.Item2, company.Item3);
        }
    }

    private void AddCompany(int id, CompanyType type, string name)
    {
        Button companyButton = new Button();
        companyButton.Text = name;
        companyButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        companyButton.IconAlignment = HorizontalAlignment.Left;
        switch (type)
        {
            case CompanyType.Veteran:
                companyButton.Icon = GD.Load<Texture2D>("res://Assets/elite-icon.png");
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
}
