using Godot;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SquadScreenController : DialogController
{
    private Squad _squad;
    private SquadScreenView _view;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
    }

    public void SetSquad(Squad squad)
    {
        _squad = squad;
        PopulateSquadDetails();
        PopulateSquadLoadout();
    }

    private void PopulateSquadDetails()
    {
        List<Tuple<string, string>> lines = [];
        lines.Add(new Tuple<string, string>("Squad Name", _squad.Name));
        lines.Add(new Tuple<string, string>("Squad Type", _squad.SquadTemplate.Name));
        if (_squad.CurrentRegion != null)
        {
            lines.Add(new Tuple<string, string>("Location", _squad.CurrentRegion.Name));
        }
        else if (_squad.BoardedLocation != null)
        {
            lines.Add(new Tuple<string, string>("Ship", _squad.BoardedLocation.Name));
        }
        else
        {
            lines.Add(new Tuple<string, string>("Location", "Unknown"));
        }
        lines.Add(new Tuple<string, string>("Squad Size", _squad.Members.Count.ToString()));
        lines.Add(new Tuple<string, string>("Combat Ready Brothers", _squad.Members.Where(s => CanFight(s)).Count().ToString()));

        _view.PopulateSquadData(lines);
    }

    private void PopulateSquadLoadout()
    {
        List<Tuple<List<string>, int, int>> weaponSets = new List<Tuple<List<string>, int, int>>();
        foreach (var weaponOptions in _squad.SquadTemplate.WeaponOptions)
        {
            Tuple<List<string>, int, int> options =
                new Tuple<List<string>, int, int>(
                    weaponOptions.Options.Select(o => o.Name).ToList(),
                    weaponOptions.MinNumber,
                    weaponOptions.MaxNumber);
            weaponSets.Add(options);
        }
        _view.PopulateSquadLoadout(weaponSets);
    }

    private bool CanFight(ISoldier soldier)
    {
        bool canWalk = !soldier.Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFuncion = !soldier.Body.HitLocations.Where(hl => hl.Template.IsVital)
                                                    .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFight = !soldier.Body.HitLocations.Where(hl => hl.Template.IsRangedWeaponHolder)
                                                .All(hl => hl.IsCrippled || hl.IsSevered);
        return canWalk && canFuncion && canFight;
    }
}
