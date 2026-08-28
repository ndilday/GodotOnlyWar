using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RecoveryOperationsView : Control
{
    private VBoxContainer _queue;
    private VBoxContainer _patientHeader;
    private VBoxContainer _pathway;
    private VBoxContainer _plan;
    private Label _queueCount;
    private Label _planSummary;
    private Button _confirm;
    private WoundBodyMapView _bodyMap;
    private RecoveryOperationsViewModel _model;
    private RecoverySortMode _sort = RecoverySortMode.Severity;
    private bool _ascending;

    public event EventHandler BackPressed;
    public event EventHandler<int> PatientSelected;
    public event EventHandler<RecoverySortRequest> SortChanged;
    public event EventHandler<CampaignLocation> DestinationSelected;
    public event EventHandler<RecoveryMovementChoice> MovementSelected;
    public event EventHandler<ReplacementOption> TreatmentSelected;
    public event EventHandler ConfirmPressed;

    public override void _Ready() => BuildLayout();

    public void SetModel(RecoveryOperationsViewModel model)
    {
        _model = model;
        if (_queue == null) return;
        _queueCount.Text = $"{model?.Queue?.Count ?? 0} / {model?.Queue?.Count ?? 0}";
        PopulateQueue();
        PopulatePatient();
        PopulatePathway();
        PopulatePlan();
    }

    private void BuildLayout()
    {
        VBoxContainer shell = new()
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 12,
            OffsetTop = 12,
            OffsetRight = -12,
            OffsetBottom = -12
        };
        shell.AddThemeConstantOverride("separation", 10);
        AddChild(shell);

        HBoxContainer toolbar = new();
        Button back = new() { Text = "‹ APOTHECARIUM", MouseDefaultCursorShape = CursorShape.PointingHand };
        back.Pressed += () => BackPressed?.Invoke(this, EventArgs.Empty);
        toolbar.AddChild(back);
        Label title = new() { Text = "RECOVERY OPERATIONS", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        toolbar.AddChild(title);
        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(142, 0) });
        shell.AddChild(toolbar);

        HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 10);
        shell.AddChild(columns);
        columns.AddChild(BuildQueuePanel());
        columns.AddChild(BuildPatientPanel());
        columns.AddChild(BuildPlanPanel());
        if (_model != null) SetModel(_model);
    }

    private Control BuildQueuePanel()
    {
        PanelContainer panel = Panel(new Vector2(310, 0));
        VBoxContainer stack = Stack(panel);
        HBoxContainer header = new();
        Label title = Section("RECOVERY QUEUE");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        _queueCount = Section("0 / 0");
        header.AddChild(_queueCount);
        stack.AddChild(header);
        Button sort = new()
        {
            Text = "SORT: SEVERITY ▼",
            TooltipText = "Sort the recovery queue. Click once to reverse the current order; click again to advance to the next sort mode in descending order.",
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        IconAtlas.Apply(sort, "sort");
        sort.Pressed += () =>
        {
            if (_ascending) _sort = (RecoverySortMode)(((int)_sort + 1) % 4);
            _ascending = !_ascending;
            sort.Text = $"SORT: {_sort.ToString().ToUpperInvariant()} {(_ascending ? "▲" : "▼")}";
            IconAtlas.Apply(sort, _sort == RecoverySortMode.RecoveryTime ? "recovery_time" : "sort");
            SortChanged?.Invoke(this, new(_sort, _ascending));
        };
        stack.AddChild(sort);
        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        _queue = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _queue.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_queue);
        stack.AddChild(scroll);
        return panel;
    }

    private Control BuildPatientPanel()
    {
        PanelContainer panel = Panel(Vector2.Zero);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        VBoxContainer stack = Stack(panel);
        _patientHeader = new VBoxContainer();
        stack.AddChild(_patientHeader);
        HSeparator separator = new();
        separator.AddThemeColorOverride("separator", OnlyWarStyle.Gold);
        stack.AddChild(separator);
        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        _pathway = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _pathway.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_pathway);
        stack.AddChild(scroll);
        return panel;
    }

    private Control BuildPlanPanel()
    {
        PanelContainer panel = Panel(new Vector2(320, 0));
        VBoxContainer stack = Stack(panel);
        stack.AddChild(Section("RECOVERY PLAN"));
        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        _plan = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _plan.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(_plan);
        stack.AddChild(scroll);
        _planSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _planSummary.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        stack.AddChild(_planSummary);
        _confirm = new Button { Text = "REVIEW & CONFIRM RECOVERY PLAN", CustomMinimumSize = new Vector2(0, 44) };
        _confirm.Pressed += () => ConfirmPressed?.Invoke(this, EventArgs.Empty);
        stack.AddChild(_confirm);
        return panel;
    }

    private void PopulateQueue()
    {
        Clear(_queue);
        foreach (RecoveryQueueRow row in _model?.Queue ?? [])
        {
            Button button = new()
            {
                Text = $"{row.Name}\n{row.Home}\n{row.Location}\n{row.WorstWound} · {row.RecoveryWeeks} wk"
                    + (string.IsNullOrEmpty(row.PostingStatus) ? string.Empty : $" · {row.PostingStatus}"),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 84),
                MouseDefaultCursorShape = CursorShape.PointingHand
            };
            button.TooltipText = BuildQueueTooltip(row);
            IconAtlas.Apply(button, row.IconKey);
            OnlyWarStyle.ApplyAccentButtonRow(button, _model.Patient?.SoldierId == row.SoldierId,
                WoundPresentationPalette.For(row.WoundLevel));
            int id = row.SoldierId;
            button.Pressed += () => PatientSelected?.Invoke(this, id);
            _queue.AddChild(button);
        }
        if ((_model?.Queue?.Count ?? 0) == 0) _queue.AddChild(Info("No casualties require recovery action."));
    }

    private void PopulatePatient()
    {
        Clear(_patientHeader);
        if (_model?.Patient == null) return;
        HBoxContainer row = new() { CustomMinimumSize = new Vector2(0, 250) };
        row.AddThemeConstantOverride("separation", 10);

        VBoxContainer ledger = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Label name = new() { Text = _model.Patient.Name };
        name.AddThemeFontSizeOverride("font_size", 25);
        ledger.AddChild(name);
        ledger.AddChild(Section("INJURY LEDGER"));
        foreach (WoundLocationSummary wound in _model.Patient.Wounds
            .Where(wound => wound.PrincipalWoundLevel != WoundLevel.None || wound.IsSevered || wound.IsCrippled || wound.IsCybernetic)
            .OrderByDescending(wound => wound.IsSevered).ThenByDescending(wound => wound.PrincipalWoundLevel))
        {
            ledger.AddChild(Info($"{wound.LocationName} — {wound.Status} — {wound.Recovery}"));
        }
        row.AddChild(ledger);

        VBoxContainer mapStack = new() { CustomMinimumSize = new Vector2(260, 0) };
        mapStack.AddChild(Section("WOUND STATUS"));
        _bodyMap = new WoundBodyMapView { CustomMinimumSize = new Vector2(260, 210), SizeFlagsVertical = SizeFlags.ExpandFill };
        _bodyMap.SetWounds(_model.Patient.Wounds);
        mapStack.AddChild(_bodyMap);
        row.AddChild(mapStack);

        VBoxContainer status = new() { CustomMinimumSize = new Vector2(225, 0) };
        status.AddChild(Section("SQUAD STATUS"));
        AddKeyValue(status, "Squad", _model.SquadStatus.Squad);
        AddKeyValue(status, "Company", _model.SquadStatus.Company);
        AddKeyValue(status, "Strength", _model.SquadStatus.Strength);
        AddKeyValue(status, "Location", _model.SquadStatus.Location);
        AddKeyValue(status, "Order", _model.SquadStatus.Order);
        row.AddChild(status);
        _patientHeader.AddChild(row);
    }

    private void PopulatePathway()
    {
        Clear(_pathway);
        if (_model?.Patient == null) return;
        VBoxContainer treatment = CardStack("1  TREATMENT NEEDS");
        if (_model.Patient.ReplacementOptions.Count == 0)
        {
            treatment.AddChild(Info("Natural recovery only; no replacement procedure required."));
        }
        foreach (ReplacementOption option in _model.Patient.ReplacementOptions)
        {
            Button choice = new()
            {
                Text = $"{option.Title} · {option.Weeks} weeks · {option.RequisitionCost} requisition\nSurgery · Apothecary · Techmarine",
                Alignment = HorizontalAlignment.Left,
                TooltipText = BuildTreatmentTooltip(option),
                MouseDefaultCursorShape = CursorShape.PointingHand
            };
            bool selected = _model.SelectedTreatment?.HitLocationId == option.HitLocationId
                && _model.SelectedTreatment.Type == option.Type;
            IconAtlas.Apply(choice, "limb_replacement");
            OnlyWarStyle.ApplyAccentButtonRow(choice, selected, OnlyWarStyle.PlayerAccent);
            ReplacementOption captured = option;
            choice.Pressed += () => TreatmentSelected?.Invoke(this, captured);
            treatment.AddChild(choice);
        }
        _pathway.AddChild(treatment.GetParent());

        VBoxContainer destination = CardStack("2  CARE DESTINATION");
        if (_model.Destinations.Count == 0) destination.AddChild(Info("NO DESTINATION SELECTED — no procedure destination is required."));
        foreach (CareDestinationCandidate site in _model.Destinations)
        {
            Button button = new()
            {
                Text = $"{site.State.ToString().ToUpperInvariant()}  {site.Name} ({site.SiteType})\n"
                    + (site.Reasons.Count == 0 ? "All requirements met" : string.Join("; ", site.Reasons.Select(reason => reason.Message))),
                Alignment = HorizontalAlignment.Left,
                Disabled = site.State == CareDestinationState.Ineligible,
                TooltipText = BuildDestinationTooltip(site),
                MouseDefaultCursorShape = CursorShape.PointingHand
            };
            IconAtlas.Apply(button, site.Location.Ship != null ? "ship" : "map_pin");
            CampaignLocation location = site.Location;
            button.Pressed += () => DestinationSelected?.Invoke(this, location);
            destination.AddChild(button);
        }
        _pathway.AddChild(destination.GetParent());

        VBoxContainer movement = CardStack("3  PATIENT MOVEMENT");
        Button detach = new()
        {
            Text = "DETACH CASUALTY (RECOMMENDED)\nSquad continues; one passenger moves.",
            Alignment = HorizontalAlignment.Left,
            TooltipText = $"Temporarily post {_model.Patient.Name} away from the home squad.\n"
                + "The squad remains in place and continues its order, while present and deployable strength fall by one.\n"
                + "Only the casualty consumes passenger capacity when movement is required."
        };
        IconAtlas.Apply(detach, "individual_posting");
        OnlyWarStyle.ApplyAccentButtonRow(detach,
            _model.Movement == RecoveryMovementChoice.DetachCasualty, OnlyWarStyle.PlayerAccent);
        detach.Pressed += () => MovementSelected?.Invoke(this, RecoveryMovementChoice.DetachCasualty);
        movement.AddChild(detach);
        Button whole = new()
        {
            Text = "MOVE WHOLE SQUAD\nOrdinary formation movement; no individual posting.",
            Alignment = HorizontalAlignment.Left,
            TooltipText = $"Move {_model.SquadStatus.Squad} and every present member to the selected care destination.\n"
                + "No individual medical posting is created. The formation's current location and availability may change."
        };
        IconAtlas.Apply(whole, "fleet_rebalance");
        OnlyWarStyle.ApplyAccentButtonRow(whole,
            _model.Movement == RecoveryMovementChoice.MoveWholeSquad, OnlyWarStyle.PlayerAccent);
        whole.Pressed += () => MovementSelected?.Invoke(this, RecoveryMovementChoice.MoveWholeSquad);
        movement.AddChild(whole);
        _pathway.AddChild(movement.GetParent());

        VBoxContainer dependent = CardStack("4  DEPENDENT ACTIONS");
        foreach (RecoveryAction action in _model.Actions)
        {
            Label actionLabel = Info($"{Glyph(action.State)} {action.Title} — {action.Detail}", ColorFor(action.State));
            actionLabel.TooltipText = BuildActionTooltip(action);
            dependent.AddChild(actionLabel);
        }
        _pathway.AddChild(dependent.GetParent());
    }

    private void PopulatePlan()
    {
        Clear(_plan);
        foreach (RecoveryAction action in _model?.Actions ?? [])
        {
            PanelContainer card = new();
            OnlyWarStyle.ApplyTintedListRow(card, false, ColorFor(action.State));
            VBoxContainer stack = new();
            Label heading = Info($"{Glyph(action.State)} {action.Title}", ColorFor(action.State));
            Label detail = Info(action.Detail);
            string tooltip = BuildActionTooltip(action);
            heading.TooltipText = tooltip;
            detail.TooltipText = tooltip;
            card.TooltipText = tooltip;
            stack.AddChild(heading);
            stack.AddChild(detail);
            card.AddChild(stack);
            _plan.AddChild(card);
        }
        _planSummary.Text = _model == null ? string.Empty
            : $"{_model.Actions.Count} staged actions · {_model.TotalRequisition} requisition\n{_model.PlanStatus}";
        if (_model != null)
        {
            _planSummary.TooltipText = BuildPlanStatusTooltip(_model);
            _confirm.TooltipText = BuildConfirmTooltip(_model);
        }
        IconAtlas.Apply(_confirm,
            _model?.Actions?.Any(action => action.Key == "rejoin") == true
                ? "reunion"
                : "medical_detachment");
        _confirm.Disabled = _model?.CanConfirm != true;
    }

    private static PanelContainer Panel(Vector2 minimum)
    {
        PanelContainer panel = new() { CustomMinimumSize = minimum, SizeFlagsVertical = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyContentPanel(panel);
        return panel;
    }

    private static VBoxContainer Stack(PanelContainer panel)
    {
        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);
        panel.AddChild(stack);
        return stack;
    }

    private static Control Card(string title, string body)
    {
        VBoxContainer stack = CardStack(title);
        stack.AddChild(Info(body));
        return (Control)stack.GetParent();
    }

    private static VBoxContainer CardStack(string title)
    {
        PanelContainer panel = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyInsetPanel(panel);
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 5);
        stack.AddChild(Section(title));
        panel.AddChild(stack);
        return stack;
    }

    private static Label Section(string text)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        return label;
    }

    private static Label Info(string text, Color? color = null)
    {
        Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", color ?? OnlyWarStyle.MutedText);
        return label;
    }

    private static void AddKeyValue(Container parent, string key, string value)
    {
        HBoxContainer row = new();
        Label keyLabel = Info(key);
        keyLabel.CustomMinimumSize = new Vector2(72, 0);
        row.AddChild(keyLabel);
        Label valueLabel = Info(value, Colors.White);
        valueLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(valueLabel);
        parent.AddChild(row);
    }

    private static string Glyph(RecoveryActionState state) => state switch
    {
        RecoveryActionState.Met => "✓",
        RecoveryActionState.Pending => "!",
        _ => "✗"
    };

    private static Color ColorFor(RecoveryActionState state) => state switch
    {
        RecoveryActionState.Met => OnlyWarStyle.MedicalStable,
        RecoveryActionState.Pending => WoundPresentationPalette.Minor,
        _ => WoundPresentationPalette.Critical
    };

    private static string BuildQueueTooltip(RecoveryQueueRow row)
    {
        List<string> lines =
        [
            row.Name,
            $"Home: {row.Home}",
            $"Current location: {row.Location}",
            $"Worst wound: {row.WorstWound}",
            $"Maximum recovery: {row.RecoveryWeeks} {(row.RecoveryWeeks == 1 ? "week" : "weeks")}"
        ];
        if (!string.IsNullOrWhiteSpace(row.PostingStatus)) lines.Add($"Posting: {row.PostingStatus}");
        if (row.CareGaps.Count > 0)
        {
            lines.Add("Unmet local-care requirements:");
            lines.AddRange(row.CareGaps.Select(gap => $"• {gap}"));
        }
        else
        {
            lines.Add("No unmet procedure requirements are currently identified.");
        }
        return string.Join("\n", lines);
    }

    private static string BuildTreatmentTooltip(ReplacementOption option)
    {
        List<string> lines =
        [
            $"Affected location: {option.LocationName}",
            $"Procedure: {option.Title}",
            option.Description,
            $"Procedure time: {option.Weeks} {(option.Weeks == 1 ? "week" : "weeks")}",
            $"Cost: {option.RequisitionCost} requisition",
            "Requires a surgery-capable site, a co-located Apothecary, and a co-located Techmarine."
        ];
        return string.Join("\n", lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildDestinationTooltip(CareDestinationCandidate site)
    {
        List<string> lines =
        [
            $"{site.Name} — {site.SiteType}",
            $"Eligibility: {site.State}",
            $"Passenger berths: {site.AvailableBerths} available; {site.RequiredBerths} required",
            $"Apothecary: {site.Apothecary?.Name ?? "not present"}",
            $"Techmarine: {site.Techmarine?.Name ?? "not present"}"
        ];
        if (site.Reasons.Count == 0)
        {
            lines.Add("All treatment requirements are currently met.");
        }
        else
        {
            lines.Add("Requirements:");
            lines.AddRange(site.Reasons.Select(reason =>
                $"• {reason.Message} {(reason.IsResolvable ? "This will be staged." : "This blocks selection.")}"));
        }
        return string.Join("\n", lines);
    }

    private static string BuildActionTooltip(RecoveryAction action)
    {
        string state = action.State switch
        {
            RecoveryActionState.Met => "✓ Met — already satisfied; no additional resolution is required.",
            RecoveryActionState.Pending => "! Pending — this change will be applied when the recovery plan is confirmed.",
            _ => "✗ Blocked — resolve this requirement before confirming the recovery plan."
        };
        return $"{action.Title}\n{action.Detail}\n{state}";
    }

    private static string BuildPlanStatusTooltip(RecoveryOperationsViewModel model)
    {
        List<string> lines = [$"Plan status: {model.PlanStatus}"];
        IReadOnlyList<RecoveryAction> blockers = model.Actions
            .Where(action => action.State == RecoveryActionState.Blocked)
            .ToList();
        if (blockers.Count > 0)
        {
            lines.Add("Blocking requirements:");
            lines.AddRange(blockers.Select(action => $"• {action.Title}: {action.Detail}"));
        }
        return string.Join("\n", lines);
    }

    private static string BuildConfirmTooltip(RecoveryOperationsViewModel model)
    {
        if (model.CanConfirm)
        {
            return $"Review {model.Actions.Count} staged recovery actions costing {model.TotalRequisition} requisition before committing them.";
        }

        List<string> reasons = [];
        if (model.SelectedTreatment != null && model.SelectedDestination == null)
            reasons.Add("Select a legal care destination.");
        if (model.SelectedTreatment != null && model.Movement == RecoveryMovementChoice.None)
            reasons.Add("Choose how the patient will move.");
        reasons.AddRange(model.Actions
            .Where(action => action.State == RecoveryActionState.Blocked)
            .Select(action => $"{action.Title}: {action.Detail}"));
        if (reasons.Count == 0) reasons.Add(model.PlanStatus);
        return "Recovery plan cannot be confirmed:\n" + string.Join("\n", reasons.Distinct().Select(reason => $"• {reason}"));
    }

    private static void Clear(Node container)
    {
        if (container == null) return;
        foreach (Node child in container.GetChildren()) child.QueueFree();
    }
}
