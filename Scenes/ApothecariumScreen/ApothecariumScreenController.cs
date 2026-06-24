using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Linq;

public partial class ApothecariumScreenController : DialogController
{
    private readonly ApothecariumMedicalRecordBuilder _recordBuilder = new();
    private ApothecariumScreenView _apothecariumView;
    private ApothecariumSelectionKind _selectedKind = ApothecariumSelectionKind.Vault;
    private int? _selectedId;

    public override void _Ready()
    {
        base._Ready();
        _apothecariumView = GetNode<ApothecariumScreenView>("ApothecariumScreenView");
        _apothecariumView.VaultButtonPressed += OnVaultButtonPressed;
        _apothecariumView.TreeSelectionChanged += OnTreeSelectionChanged;
        _apothecariumView.ReplacementOptionPressed += OnReplacementOptionPressed;
        Render();
    }

    public override void _ExitTree()
    {
        if (_apothecariumView != null)
        {
            _apothecariumView.VaultButtonPressed -= OnVaultButtonPressed;
            _apothecariumView.TreeSelectionChanged -= OnTreeSelectionChanged;
            _apothecariumView.ReplacementOptionPressed -= OnReplacementOptionPressed;
        }
    }

    private void OnVaultButtonPressed(object sender, EventArgs e)
    {
        _selectedKind = ApothecariumSelectionKind.Vault;
        _selectedId = null;
        Render();
    }

    private void OnTreeSelectionChanged(object sender, ApothecariumSelection selection)
    {
        _selectedKind = selection.Kind;
        _selectedId = selection.Id;
        Render();
    }

    private void OnReplacementOptionPressed(object sender, ReplacementOption option)
    {
        // First pass is presentation-only. Persisted procedures are the next PRD milestone.
        GD.Print($"Medical procedure selected: {option.Title} for hit location {option.HitLocationId}.");
    }

    private void Render()
    {
        PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        Unit chapter = force?.Army?.OrderOfBattle;
        if (force == null || chapter == null)
        {
            return;
        }

        _apothecariumView.SetVaultSelected(_selectedKind == ApothecariumSelectionKind.Vault);
        _apothecariumView.SetTree(_recordBuilder.BuildTree(chapter, _selectedKind, _selectedId, woundedOnly: true));

        switch (_selectedKind)
        {
            case ApothecariumSelectionKind.Unit:
                RenderUnit(chapter);
                break;
            case ApothecariumSelectionKind.Squad:
                RenderSquad(chapter);
                break;
            case ApothecariumSelectionKind.Soldier:
                RenderSoldier(chapter);
                break;
            default:
                RenderVault(force);
                break;
        }
    }

    private void RenderVault(PlayerForce force)
    {
        _apothecariumView.ShowVault(_recordBuilder.BuildVault(force, GameDataSingleton.Instance.Date));
    }

    private void RenderUnit(Unit chapter)
    {
        Unit unit = chapter.Id == _selectedId ? chapter : chapter.ChildUnits.SelectMany(FlattenUnits).FirstOrDefault(u => u.Id == _selectedId);
        if (unit == null)
        {
            RenderVault(GameDataSingleton.Instance.Sector.PlayerForce);
            return;
        }

        _apothecariumView.ShowRollup(_recordBuilder.BuildUnitSummary(unit));
    }

    private void RenderSquad(Unit chapter)
    {
        Squad squad = chapter.GetAllSquads().FirstOrDefault(s => s.Id == _selectedId);
        if (squad == null)
        {
            RenderVault(GameDataSingleton.Instance.Sector.PlayerForce);
            return;
        }

        _apothecariumView.ShowRollup(_recordBuilder.BuildSquadSummary(squad));
    }

    private void RenderSoldier(Unit chapter)
    {
        ISoldier soldier = chapter.GetAllMembers().FirstOrDefault(s => s.Id == _selectedId);
        if (soldier == null)
        {
            RenderVault(GameDataSingleton.Instance.Sector.PlayerForce);
            return;
        }

        _apothecariumView.ShowSoldier(_recordBuilder.BuildSoldierSummary(soldier));
    }

    private static System.Collections.Generic.IEnumerable<Unit> FlattenUnits(Unit unit)
    {
        yield return unit;
        foreach (Unit child in unit.ChildUnits ?? [])
        {
            foreach (Unit descendant in FlattenUnits(child))
            {
                yield return descendant;
            }
        }
    }
}
