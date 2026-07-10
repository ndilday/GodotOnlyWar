using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SoldierController : Control
{
    private readonly SoldierDetailBuilder _detailBuilder = new();
    private readonly SoldierTransferService _transferService = new();
    private List<SoldierTransferOption> _transferOptions = [];
    private PlayerSoldier _selectedSoldier;
    private SoldierTransferOption _pendingTransferOption;
    private ConfirmationDialog _transferConfirmationDialog;
    private AcceptDialog _transferBlockedDialog;

    public SoldierView SoldierView { get; set; }

    public event EventHandler SoldierTransferred;

    public override void _Ready()
    {
        if (SoldierView == null)
        {
            SoldierView = GetNode<SoldierView>("SoldierView");
        }

        SoldierView.TransferTargetSelected += OnTransferTargetSelected;
        _transferConfirmationDialog = new ConfirmationDialog
        {
            Title = "Confirm Transfer"
        };
        _transferConfirmationDialog.Confirmed += OnTransferConfirmed;
        AddChild(_transferConfirmationDialog);

        _transferBlockedDialog = new AcceptDialog
        {
            Title = "Transfer Blocked"
        };
        AddChild(_transferBlockedDialog);
    }

    public override void _ExitTree()
    {
        if (SoldierView != null)
        {
            SoldierView.TransferTargetSelected -= OnTransferTargetSelected;
        }
        if (_transferConfirmationDialog != null)
        {
            _transferConfirmationDialog.Confirmed -= OnTransferConfirmed;
        }
    }

    public void DisplaySoldierData(PlayerSoldier soldier)
    {
        _selectedSoldier = soldier;
        RenderSoldier();
    }

    private void RenderSoldier()
    {
        if (_selectedSoldier == null)
        {
            return;
        }

        SoldierView.SetDetail(_detailBuilder.Build(_selectedSoldier, false));
        _transferOptions = _transferService.GetTransferOptions(
            GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle,
            _selectedSoldier);
        SoldierView.SetTransferOptions(_transferOptions.Select(option => option.DisplayName).ToList());
    }

    private void OnTransferTargetSelected(object sender, int index)
    {
        if (_selectedSoldier == null || index < 0 || index >= _transferOptions.Count)
        {
            return;
        }

        SoldierTransferOption option = _transferOptions[index];
        GameDataSingleton.Instance.Sector.PlayerForce.Army.PopulateSquadMap();
        if (_transferService.WouldExceedShipCapacity(
                _selectedSoldier, option, GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap))
        {
            string transferTarget = _transferService.FormatBlockedTransferTarget(
                option,
                GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap);
            _transferBlockedDialog.DialogText =
                $"{transferTarget} has no room aboard its ship. Free up space before transferring {_selectedSoldier.Name} there.";
            _transferBlockedDialog.PopupCentered();
            return;
        }

        _pendingTransferOption = option;
        _transferConfirmationDialog.DialogText =
            $"Transfer {_selectedSoldier.Template.Name} {_selectedSoldier.Name} to {_pendingTransferOption.DisplayName}?";
        _transferConfirmationDialog.PopupCentered();
    }

    private void OnTransferConfirmed()
    {
        if (_selectedSoldier == null || _pendingTransferOption == null)
        {
            return;
        }

        GameDataSingleton.Instance.Sector.PlayerForce.Army.PopulateSquadMap();
        bool didTransfer = _transferService.ApplyTransfer(
            _selectedSoldier,
            _pendingTransferOption,
            GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap,
            GameDataSingleton.Instance.Date);

        _pendingTransferOption = null;
        if (didTransfer)
        {
            RenderSoldier();
            SoldierTransferred?.Invoke(this, EventArgs.Empty);
        }
    }
}
