using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ApothecariumScreenController : DialogController
{
    private readonly ApothecariumMedicalRecordBuilder _recordBuilder = new();
    private readonly MedicalProcedureService _procedureService = new();
    private ApothecariumScreenView _apothecariumView;
    private ApothecariumSelectionKind _selectedKind = ApothecariumSelectionKind.Vault;
    private int? _selectedId;

    public event EventHandler CampaignChanged;

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
        PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        Unit chapter = force?.Army?.OrderOfBattle;
        if (force == null || chapter == null)
        {
            return;
        }
        ISoldier soldier = chapter.GetAllMembers().FirstOrDefault(s => s.Id == _selectedId);
        if (soldier == null)
        {
            return;
        }
        if (_procedureService.TryAssign(force, soldier, option))
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
            // The procedure now holds the location; refresh so it drops out of the offered
            // options, the Requisition spend is reflected, and the soldier reads as in care.
            Render();
        }
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

        MedicalSoldierSummary summary = _recordBuilder.BuildSoldierSummary(soldier);
        summary = EnrichWithRequisites(GameDataSingleton.Instance.Sector.PlayerForce, soldier, summary);
        _apothecariumView.ShowSoldier(summary);
    }

    // Fills each replacement option's requisite breakdown (rendered green/red by the view)
    // and drops any location already under an active procedure.
    private MedicalSoldierSummary EnrichWithRequisites(PlayerForce force, ISoldier soldier, MedicalSoldierSummary summary)
    {
        if (summary.ReplacementOptions.Count == 0)
        {
            return summary;
        }
        List<ReplacementOption> enriched = [];
        foreach (ReplacementOption option in summary.ReplacementOptions)
        {
            if (_procedureService.HasProcedureInProgress(force, soldier.Id, option.HitLocationId))
            {
                continue;
            }
            IReadOnlyList<ProcedureRequisite> requisites = _procedureService.EvaluateRequisites(force, soldier, option);
            enriched.Add(option with { Requisites = requisites, CanAssign = requisites.All(r => r.IsMet) });
        }
        return summary with { ReplacementOptions = enriched };
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
