using Godot;
using OnlyWar.Models.Squads;
using System;

public partial class RegionScreenController : DialogController
{
    public event EventHandler<Squad> SquadClicked;
    public event EventHandler<Squad> SquadDoubleClicked;
}
