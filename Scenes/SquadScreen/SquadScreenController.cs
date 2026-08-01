using OnlyWar.Helpers;
using OnlyWar.Models.Squads;
using System;
using System.Linq;

public partial class SquadScreenController : MainScreenController
{
    private Squad _squad;
    private SquadScreenView _view;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
        _view.LoadoutChanged += OnLoadoutChanged;
        _view.ReturnToDoctrinePressed += OnReturnToDoctrine;
        _view.ClosePressed += (_, _) => RequestClose();
    }

    public void SetSquad(Squad squad)
    {
        _squad = squad;
        Refresh();
    }

    private void OnLoadoutChanged(object sender, EventArgs e)
    {
        if (_squad == null) return;
        if (_squad.UsesLoadoutDoctrine)
        {
            LoadoutDoctrineService.Customize(_squad);
        }
        _squad.Loadout = _view.WorkingLoadout.ToList();
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        _view.SetDoctrineState("Custom loadout", true);
    }

    private void OnReturnToDoctrine(object sender, EventArgs e)
    {
        if (_squad == null) return;
        LoadoutDoctrineService.ReturnToDoctrine(_squad);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void Refresh()
    {
        if (_squad == null || _view == null) return;
        EffectiveLoadout effective = LoadoutDoctrineService.Resolve(_squad);
        int ableBodied = _squad.Members.Count(member => member.CanFight);
        string location = _squad.CurrentRegion?.Planet?.Name
            ?? _squad.BoardedLocation?.Fleet?.Planet?.Name
            ?? "No active theater";
        _view.Display(
            _squad.Name,
            $"{_squad.SquadTemplate.Name} · {ableBodied} combat-ready · {location}",
            LoadoutDoctrineService.DescribeSource(effective),
            _squad.SquadTemplate,
            effective.WeaponSets,
            ableBodied,
            !_squad.UsesLoadoutDoctrine);
    }
}
