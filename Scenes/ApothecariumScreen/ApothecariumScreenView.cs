using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ApothecariumScreenView : DialogView
{
    private Button _vaultButton;
    private Tree _unitTree;
    private VBoxContainer _vaultMetricGrid;
    private VBoxContainer _vaultRows;
    private VBoxContainer _vaultFormationRows;
    private VBoxContainer _rollupMetrics;
    private VBoxContainer _rollupRows;
    private Label _rollupTitle;
    private Label _rollupSubtitle;
    private TextureRect _rollupIcon;
    private Label _soldierTitle;
    private Label _soldierSubtitle;
    private TextureRect _soldierIcon;
    private VBoxContainer _soldierMetrics;
    private WoundDiagramView _woundDiagram;
    private VBoxContainer _woundRows;
    private VBoxContainer _replacementRows;
    private Control _vaultPanel;
    private Control _rollupPanel;
    private Control _soldierPanel;
    private Button _closeButton;

    public event EventHandler VaultButtonPressed;
    public event EventHandler<ApothecariumSelection> TreeSelectionChanged;
    public event EventHandler<ReplacementOption> ReplacementOptionPressed;

    public override void _Ready()
    {
        Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        base._Ready();
        BuildLayout();
    }

    public void SetVaultSelected(bool selected)
    {
        OnlyWarStyle.ApplyAccentButtonRow(_vaultButton, selected, OnlyWarStyle.MedicalStable);
    }

    public void SetTree(IReadOnlyList<ApothecariumTreeItem> items)
    {
        _unitTree.Clear();
        _unitTree.HideRoot = true;
        TreeItem root = _unitTree.CreateItem();
        foreach (ApothecariumTreeItem item in items ?? [])
        {
            AddTreeItem(root, item);
        }
    }

    public void ShowVault(GeneSeedVaultSummary summary)
    {
        ShowPanel(_vaultPanel);
        PopulateMetrics(_vaultMetricGrid, [
            ("Stockpile", summary.Stockpile.ToString(), MedicalSeverity.Stable),
            ("Requisition", summary.Requisition.ToString(), MedicalSeverity.Stable),
            ("Purity", summary.Stockpile > 0 ? $"{summary.AggregatePurity:P0} ({summary.PurityStatus})" : summary.PurityStatus, summary.PuritySeverity),
            ("Mature Implanted", summary.MatureImplanted.ToString(), MedicalSeverity.Stable),
            ("Immature Implanted", summary.ImmatureImplanted.ToString(), MedicalSeverity.Watch),
            ("At Risk", summary.AtRiskImplanted.ToString(), summary.AtRiskImplanted > 0 ? MedicalSeverity.Critical : MedicalSeverity.Stable)
        ]);

        ClearContainer(_vaultRows);
        foreach (GeneSeedVaultRow row in summary.Rows)
        {
            _vaultRows.AddChild(CreateDataRow(row.Title, row.Subtitle, row.Value, row.Severity));
        }

        ClearContainer(_vaultFormationRows);
        foreach (GeneSeedFormationSummary formation in summary.FormationSummaries)
        {
            _vaultFormationRows.AddChild(CreateDataRow(
                formation.Formation,
                $"{formation.MatureImplanted} mature / {formation.ImmatureImplanted} immature / {formation.AtRisk} at risk",
                formation.PurityStatus,
                formation.Severity));
        }
    }

    public void ShowRollup(MedicalUnitSummary summary)
    {
        ShowPanel(_rollupPanel);
        _rollupIcon.Texture = IconAtlas.GetIcon(summary.IconKey);
        _rollupTitle.Text = summary.Title;
        _rollupSubtitle.Text = summary.Subtitle;
        PopulateMetrics(_rollupMetrics, [
            ("Healthy", summary.HealthyCount.ToString(), MedicalSeverity.Stable),
            ("Wounded", summary.WoundedCount.ToString(), MedicalSeverity.Watch),
            ("Out", summary.OutOfActionCount.ToString(), summary.OutOfActionCount > 0 ? MedicalSeverity.Critical : MedicalSeverity.Stable),
            ("Ready Next", summary.ReadyNextCount.ToString(), MedicalSeverity.Stable),
            ("Max Recovery", $"{summary.MaxRecoveryWeeks} wk", summary.MaxRecoveryWeeks > 0 ? MedicalSeverity.Watch : MedicalSeverity.Stable)
        ]);

        ClearContainer(_rollupRows);
        if (summary.SeriousWounds.Count == 0)
        {
            _rollupRows.AddChild(CreateInfoLabel("No medical issues in this formation."));
            return;
        }

        foreach (MedicalSeriousWoundRow row in summary.SeriousWounds)
        {
            _rollupRows.AddChild(CreateDataRow(row.SoldierName, $"{row.Wound} - {row.Recommendation}", row.OutOfAction, row.Severity));
        }
    }

    public void ShowSoldier(MedicalSoldierSummary summary)
    {
        ShowPanel(_soldierPanel);
        _soldierIcon.Texture = IconAtlas.GetIcon(summary.IconKey);
        _soldierTitle.Text = summary.Name;
        _soldierSubtitle.Text = summary.Assignment;
        PopulateMetrics(_soldierMetrics, [
            ("Status", summary.CanFight ? "Ready" : "Out", summary.CanFight ? MedicalSeverity.Stable : MedicalSeverity.Critical),
            ("Recovery", summary.ReplacementOptions.Count > 0 ? "Replacement" : $"{summary.MaxRecoveryWeeks} wk", summary.ReplacementOptions.Count > 0 ? MedicalSeverity.Critical : MedicalSeverity.Watch),
            ("Gene-seed", summary.GeneSeedStatus, summary.GeneSeedStatus == "Safe" ? MedicalSeverity.Stable : MedicalSeverity.Critical),
            ("Wounds", summary.Wounds.Count(w => w.Severity > MedicalSeverity.None).ToString(), summary.WorstSeverity)
        ]);

        _woundDiagram.SetWounds(summary.Wounds);

        ClearContainer(_woundRows);
        foreach (WoundLocationSummary wound in summary.Wounds.Where(w => w.Severity > MedicalSeverity.None || w.HoldsProgenoid || w.IsCybernetic))
        {
            string subtitle = wound.HoldsProgenoid
                ? $"{wound.Recovery} - progenoid-bearing"
                : wound.Recovery;
            _woundRows.AddChild(CreateDataRow(wound.LocationName, subtitle, wound.Status, wound.Severity));
        }

        ClearContainer(_replacementRows);
        if (summary.ReplacementOptions.Count == 0)
        {
            _replacementRows.AddChild(CreateInfoLabel("No replacement procedure required."));
            return;
        }

        foreach (ReplacementOption option in summary.ReplacementOptions)
        {
            _replacementRows.AddChild(CreateReplacementCard(option));
        }
    }

    private void BuildLayout()
    {
        _closeButton = GetNode<Button>("CloseButton");
        IconAtlas.ApplyIconButton(_closeButton, "close", 40, 28);

        HBoxContainer root = new()
        {
            Name = "ApothecariumContent",
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 16,
            OffsetTop = 76,
            OffsetRight = -16,
            OffsetBottom = -16
        };
        root.AddThemeConstantOverride("separation", 12);
        AddChild(root);

        root.AddChild(BuildLeftPanel());
        root.AddChild(BuildRightPanel());
    }

    private Control BuildLeftPanel()
    {
        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(374, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyContentPanel(panel);

        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 10);
        panel.AddChild(stack);

        _vaultButton = new Button
        {
            Text = "Gene Seed Vault\nstockpile, purity, implanted progenoids",
            CustomMinimumSize = new Vector2(0, 58),
            Alignment = HorizontalAlignment.Left,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        IconAtlas.Apply(_vaultButton, "medical");
        _vaultButton.Pressed += () => VaultButtonPressed?.Invoke(this, EventArgs.Empty);
        stack.AddChild(_vaultButton);

        HBoxContainer filterRow = new();
        filterRow.AddThemeConstantOverride("separation", 6);
        Label title = new() { Text = "UNIT TREE", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 13);
        title.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        filterRow.AddChild(title);
        filterRow.AddChild(CreateSmallChip("Wounded only", MedicalSeverity.Stable));
        filterRow.AddChild(CreateSmallChip("Out-of-action", MedicalSeverity.Critical));
        stack.AddChild(filterRow);

        _unitTree = new Tree
        {
            HideRoot = true,
            Columns = 2,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = Tree.SelectModeEnum.Single
        };
        _unitTree.SetColumnTitle(0, "Unit");
        _unitTree.SetColumnTitle(1, "Status");
        _unitTree.SetColumnExpand(0, true);
        _unitTree.SetColumnExpand(1, false);
        _unitTree.SetColumnCustomMinimumWidth(1, 88);
        _unitTree.ItemSelected += OnTreeItemSelected;
        stack.AddChild(_unitTree);

        return panel;
    }

    private Control BuildRightPanel()
    {
        Control holder = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };

        _vaultPanel = BuildVaultPanel();
        _rollupPanel = BuildRollupPanel();
        _soldierPanel = BuildSoldierPanel();
        holder.AddChild(_vaultPanel);
        holder.AddChild(_rollupPanel);
        holder.AddChild(_soldierPanel);
        return holder;
    }

    private Control BuildVaultPanel()
    {
        PanelContainer panel = CreateFillPanel();
        VBoxContainer stack = CreatePanelStack(panel);

        HBoxContainer hero = CreateHero("medical", "Gene Seed Vault", "Stockpile, purity, and implanted progenoids", out _, out _, out _);
        stack.AddChild(hero);

        _vaultMetricGrid = CreateMetricRow();
        stack.AddChild(_vaultMetricGrid);

        HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        stack.AddChild(columns);

        _vaultRows = CreateSection(columns, "Stockpile Ledger");
        _vaultFormationRows = CreateSection(columns, "Currently Implanted");
        return panel;
    }

    private Control BuildRollupPanel()
    {
        PanelContainer panel = CreateFillPanel();
        VBoxContainer stack = CreatePanelStack(panel);
        HBoxContainer hero = CreateHero("infantry", "Unit Rollup", "Medical readiness summary", out _rollupIcon, out _rollupTitle, out _rollupSubtitle);
        stack.AddChild(hero);
        _rollupMetrics = CreateMetricRow();
        stack.AddChild(_rollupMetrics);
        _rollupRows = CreateSection(stack, "Serious Wounds");
        return panel;
    }

    private Control BuildSoldierPanel()
    {
        PanelContainer panel = CreateFillPanel();
        VBoxContainer stack = CreatePanelStack(panel);
        HBoxContainer hero = CreateHero("wounded", "Selected Soldier", "Medical record", out _soldierIcon, out _soldierTitle, out _soldierSubtitle);
        stack.AddChild(hero);
        _soldierMetrics = CreateMetricRow();
        stack.AddChild(_soldierMetrics);

        HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        stack.AddChild(columns);

        PanelContainer diagramPanel = CreateInsetPanel();
        diagramPanel.CustomMinimumSize = new Vector2(360, 0);
        VBoxContainer diagramStack = new();
        diagramStack.AddThemeConstantOverride("separation", 8);
        diagramPanel.AddChild(diagramStack);
        diagramStack.AddChild(CreateSectionLabel("Vitruvian Wound Status"));
        _woundDiagram = new WoundDiagramView { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(340, 360) };
        diagramStack.AddChild(_woundDiagram);
        columns.AddChild(diagramPanel);

        _woundRows = CreateSection(columns, "Wound Ledger");
        _replacementRows = CreateSection(columns, "Replacement Assignment");
        return panel;
    }

    private HBoxContainer CreateHero(string iconKey, string title, string subtitle, out TextureRect icon, out Label titleLabel, out Label subtitleLabel)
    {
        HBoxContainer hero = new() { CustomMinimumSize = new Vector2(0, 92) };
        hero.AddThemeConstantOverride("separation", 12);
        icon = new TextureRect
        {
            Texture = IconAtlas.GetIcon(iconKey),
            CustomMinimumSize = new Vector2(64, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        hero.AddChild(icon);

        VBoxContainer titleStack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titleStack.AddThemeConstantOverride("separation", 3);
        titleLabel = new Label { Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        titleLabel.AddThemeFontSizeOverride("font_size", 28);
        subtitleLabel = new Label { Text = subtitle, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        subtitleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        titleStack.AddChild(titleLabel);
        titleStack.AddChild(subtitleLabel);
        hero.AddChild(titleStack);
        return hero;
    }

    private VBoxContainer CreateMetricRow()
    {
        VBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 8);
        return row;
    }

    private void PopulateMetrics(VBoxContainer container, IReadOnlyList<(string Label, string Value, MedicalSeverity Severity)> metrics)
    {
        ClearContainer(container);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        container.AddChild(row);
        foreach ((string label, string value, MedicalSeverity severity) in metrics)
        {
            row.AddChild(CreateMetricPanel(label, value, severity));
        }
    }

    private VBoxContainer CreateSection(Container parent, string title)
    {
        PanelContainer panel = CreateInsetPanel();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);
        panel.AddChild(stack);
        stack.AddChild(CreateSectionLabel(title));
        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        VBoxContainer rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(rows);
        stack.AddChild(scroll);
        parent.AddChild(panel);
        return rows;
    }

    private Control CreateMetricPanel(string label, string value, MedicalSeverity severity)
    {
        PanelContainer panel = CreateInsetPanel();
        panel.CustomMinimumSize = new Vector2(150, 64);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        VBoxContainer stack = new();
        panel.AddChild(stack);
        Label labelNode = CreateSectionLabel(label);
        Label valueNode = new() { Text = value, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        valueNode.AddThemeFontSizeOverride("font_size", 20);
        valueNode.AddThemeColorOverride("font_color", ColorFor(severity));
        stack.AddChild(labelNode);
        stack.AddChild(valueNode);
        return panel;
    }

    private Control CreateDataRow(string title, string subtitle, string status, MedicalSeverity severity)
    {
        PanelContainer panel = new() { CustomMinimumSize = new Vector2(0, 58), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyTintedListRow(panel, false, ColorFor(severity, 0.75f));
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);
        VBoxContainer textStack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        textStack.AddThemeConstantOverride("separation", 0);
        Label titleLabel = new() { Text = title, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = title };
        Label subtitleLabel = new() { Text = subtitle, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = subtitle };
        subtitleLabel.AddThemeFontSizeOverride("font_size", 12);
        subtitleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        textStack.AddChild(titleLabel);
        textStack.AddChild(subtitleLabel);
        row.AddChild(textStack);
        Label statusLabel = new()
        {
            Text = status,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(118, 0)
        };
        statusLabel.AddThemeColorOverride("font_color", ColorFor(severity));
        row.AddChild(statusLabel);
        return panel;
    }

    private Control CreateReplacementCard(ReplacementOption option)
    {
        PanelContainer panel = new() { CustomMinimumSize = new Vector2(0, 132), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyTintedListRow(panel, false, ColorFor(option.Type == MedicalProcedureType.Cybernetic ? MedicalSeverity.Watch : MedicalSeverity.Critical, 0.75f));
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);
        Label title = new() { Text = option.Title };
        title.AddThemeFontSizeOverride("font_size", 18);
        stack.AddChild(title);
        Label description = new() { Text = option.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        description.AddThemeFontSizeOverride("font_size", 13);
        description.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        stack.AddChild(description);
        // Every prerequisite is listed explicitly (PRD 4.8): met in green, unmet in red, so
        // the player can see at a glance both that a procedure is blocked and exactly why.
        foreach (ProcedureRequisite requisite in option.Requisites ?? [])
        {
            Label line = new()
            {
                Text = $"{(requisite.IsMet ? "✓" : "✗")} {requisite.Label}"
            };
            line.AddThemeFontSizeOverride("font_size", 13);
            line.AddThemeColorOverride("font_color",
                ColorFor(requisite.IsMet ? MedicalSeverity.Stable : MedicalSeverity.Critical));
            stack.AddChild(line);
        }
        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 8);
        actions.AddChild(CreateSmallChip($"{option.Weeks} weeks", MedicalSeverity.Watch));
        actions.AddChild(CreateSmallChip($"{option.RequisitionCost} Req", MedicalSeverity.Critical));
        Button assignButton = new()
        {
            Text = option.Type == MedicalProcedureType.Cybernetic ? "Assign Cybernetic" : "Request Vat Growth",
            Disabled = !option.CanAssign,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        assignButton.Pressed += () => ReplacementOptionPressed?.Invoke(this, option);
        actions.AddChild(assignButton);
        stack.AddChild(actions);
        return panel;
    }

    private Control CreateSmallChip(string text, MedicalSeverity severity)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 26)
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", ColorFor(severity));
        label.AddThemeStyleboxOverride("normal", OnlyWarStyle.GetInsetPanelStyle());
        return label;
    }

    private Label CreateSectionLabel(string text)
    {
        Label label = new() { Text = text.ToUpperInvariant() };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

    private Label CreateInfoLabel(string text)
    {
        Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

    private PanelContainer CreateFillPanel()
    {
        PanelContainer panel = new()
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyContentPanel(panel);
        return panel;
    }

    private VBoxContainer CreatePanelStack(PanelContainer panel)
    {
        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 12);
        panel.AddChild(stack);
        return stack;
    }

    private PanelContainer CreateInsetPanel()
    {
        PanelContainer panel = new();
        OnlyWarStyle.ApplyInsetPanel(panel);
        return panel;
    }

    private void ShowPanel(Control panel)
    {
        _vaultPanel.Visible = panel == _vaultPanel;
        _rollupPanel.Visible = panel == _rollupPanel;
        _soldierPanel.Visible = panel == _soldierPanel;
    }

    private void AddTreeItem(TreeItem parent, ApothecariumTreeItem item)
    {
        TreeItem node = _unitTree.CreateItem(parent);
        node.SetText(0, item.Title);
        node.SetText(1, item.Status);
        node.SetTooltipText(0, item.Subtitle);
        node.SetTooltipText(1, item.Status);
        node.SetIcon(0, IconAtlas.GetIcon(item.IconKey));
        node.SetMetadata(0, Variant.From(new Vector2I((int)item.Kind, item.Id)));
        node.SetCustomColor(1, ColorFor(item.Severity));
        if (item.IsSelected)
        {
            _unitTree.SetSelected(node, 0);
        }

        foreach (ApothecariumTreeItem child in item.Children ?? [])
        {
            AddTreeItem(node, child);
        }
    }

    private void OnTreeItemSelected()
    {
        TreeItem selected = _unitTree.GetSelected();
        if (selected == null)
        {
            return;
        }

        Vector2I meta = selected.GetMetadata(0).As<Vector2I>();
        TreeSelectionChanged?.Invoke(this, new ApothecariumSelection((ApothecariumSelectionKind)meta.X, meta.Y));
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Color ColorFor(MedicalSeverity severity, float alpha = 1f)
    {
        Color color = severity switch
        {
            MedicalSeverity.Lost => OnlyWarStyle.Critical,
            MedicalSeverity.Critical => OnlyWarStyle.Critical,
            MedicalSeverity.Serious => OnlyWarStyle.MedicalWarning,
            MedicalSeverity.Watch => OnlyWarStyle.MedicalWarning,
            MedicalSeverity.Stable => OnlyWarStyle.MedicalStable,
            _ => OnlyWarStyle.BodyText
        };
        color.A = alpha;
        return color;
    }
}
