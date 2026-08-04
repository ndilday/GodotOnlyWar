using OnlyWar.Helpers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SquadScreenController : MainScreenController
{
    private readonly SoldierDossierService _dossierService = new();
    private Squad _squad;
    private SquadScreenView _view;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
        _view.LoadoutChanged += OnLoadoutChanged;
        _view.ReturnToDoctrinePressed += OnReturnToDoctrine;
        _view.CharacterLoadoutSelected += OnCharacterLoadoutSelected;
        _view.CharacterLoadoutReset += OnCharacterLoadoutReset;
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

    private void OnCharacterLoadoutSelected(object sender, (int SoldierId, WeaponSet WeaponSet) change)
    {
        ISoldier soldier = FindMember(change.SoldierId);
        if (soldier == null) return;
        CharacterLoadoutService.SetPersonalLoadout(soldier, change.WeaponSet);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void OnCharacterLoadoutReset(object sender, int soldierId)
    {
        ISoldier soldier = FindMember(soldierId);
        if (soldier == null) return;
        CharacterLoadoutService.ClearPersonalLoadout(soldier);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private ISoldier FindMember(int soldierId) =>
        _squad?.Members.FirstOrDefault(member => member.Id == soldierId);

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
            effective.WeaponSets,
            !_squad.UsesLoadoutDoctrine,
            BuildCharacterRows(),
            ElementLoadoutSections.Build(
                _squad.SquadTemplate,
                // Capacity is THIS element's own able-bodied bodies, not the squad's — a
                // sergeant's individually-equipped slot must not eat into the trooper pool.
                element => _squad.Members.Count(
                    member => member.CanFight && member.Template == element.SoldierTemplate)));
    }

    // Characters carry kit chosen for the individual, so they get a row each rather than a share
    // of the squad's pooled counts. Ordered the way the squad roster reads: leader, then rank.
    private List<CharacterLoadoutRowData> BuildCharacterRows()
    {
        List<CharacterLoadoutRowData> rows = [];
        if (_squad?.Members == null) return rows;

        foreach (ISoldier soldier in _squad.Members
                     .Where(CharacterLoadoutService.IsCharacter)
                     .OrderByDescending(member => member.Template.IsSquadLeader)
                     .ThenByDescending(member => member.Template.Rank)
                     .ThenBy(member => member.Name))
        {
            EffectiveCharacterLoadout resolved = CharacterLoadoutService.Resolve(soldier);
            SquadTemplateElement element = _squad.SquadTemplate.Elements
                .FirstOrDefault(candidate => candidate.SoldierTemplate == soldier.Template);
            rows.Add(new CharacterLoadoutRowData(
                soldier.Id,
                $"{soldier.Template.Name} {soldier.Name}",
                DescribeRow(soldier, resolved),
                element?.GetMenu(CharacterLoadoutService.CommandWeaponGroup) ?? [],
                resolved?.WeaponSet,
                resolved?.Source == CharacterLoadoutSource.Personal));
        }
        return rows;
    }

    // Pairs the loadout's provenance with the brother's gun and blade honors, so the player can
    // judge a weapon choice against his record. Honors rather than skill values on purpose: raw
    // soldier stats are never surfaced. They also stay put as the dropdown changes, since they
    // describe the man and not the weapon currently selected for him.
    private string DescribeRow(ISoldier soldier, EffectiveCharacterLoadout resolved)
    {
        string source = CharacterLoadoutService.DescribeSource(resolved);
        if (soldier is not PlayerSoldier playerSoldier)
        {
            return source;
        }
        IReadOnlyList<string> honors = _dossierService.BuildCombatHonorNames(playerSoldier);
        return honors.Count == 0 ? source : $"{source} · {string.Join(" · ", honors)}";
    }
}
