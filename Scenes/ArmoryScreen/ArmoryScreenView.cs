using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;

/// <summary>
/// The Armory workspace (TDD §6.14). The left rail lists the
/// Armory's sections; only Techmarines exists today, and the armour, weapon and vehicle counts
/// (PRD §6.9) have their places reserved there. The view raises intents and renders the
/// application's detached rows; it decides nothing.
/// </summary>
public partial class ArmoryScreenView : MainScreenView
{
    /// <summary>The player asked to promote this brother to this Armory rank.</summary>
    public readonly record struct PromotionRequest(int SoldierId, int TemplateId);

    private TextureRect _heroIcon;
    private Label _title;
    private Label _subtitle;
    private HBoxContainer _metrics;
    private Label _loanLabel;
    private VBoxContainer _atHomeRows;
    private VBoxContainer _onMarsRows;
    private VBoxContainer _candidateRows;
    private OptionButton _destinationPicker;
    private Label _destinationResolved;
    private Label _emptyLabel;
    private Control _techmarineSection;
    private readonly List<string> _destinationKeys = [];
    private bool _populatingDestinations;

    public event EventHandler<int> SendToMarsPressed;
    public event EventHandler<PromotionRequest> PromotePressed;
    public event EventHandler<string> ReturnDestinationSelected;

    public override void _Ready()
    {
        Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        base._Ready();
        BuildLayout();
    }

    public void ShowArmory(ArmoryOverview view)
    {
        if (view == null) return;
        _techmarineSection.Visible = view.HasArmory;
        _emptyLabel.Visible = !view.HasArmory;
        _title.Text = view.Title;
        _subtitle.Text = view.HasArmory
            ? $"Duty station: {view.DutyStation}   ·   Master of the Forge: {view.MasterOfTheForge ?? "vacant"}"
            : "The chapter has no Armory.";

        ClearContainer(_metrics);
        _metrics.AddChild(CreateMetricPanel("At Home", view.AtHome.Count.ToString(), UiAccent.Body));
        _metrics.AddChild(CreateMetricPanel("On Mars", view.OnMars.Count.ToString(), UiAccent.Body));
        _metrics.AddChild(CreateMetricPanel(
            "Next Return",
            view.OnMars.Count > 0 ? view.OnMars[0].Returns : "None",
            UiAccent.Body));
        _metrics.AddChild(CreateMetricPanel(
            "Mechanicus Loan",
            view.Loan.IsActive ? "Active" : "Ended",
            view.Loan.IsActive ? UiAccent.Stable : UiAccent.Warning));
        _loanLabel.Text = view.Loan.Summary;

        ClearContainer(_atHomeRows);
        if (view.AtHome.Count == 0)
        {
            _atHomeRows.AddChild(CreateInfoLabel("No Techmarine is at home. The first return from Mars brings the chapter its own."));
        }
        foreach (ArmoryTechmarineRow row in view.AtHome)
        {
            _atHomeRows.AddChild(CreateTechmarineRow(row));
        }

        ClearContainer(_onMarsRows);
        if (view.OnMars.Count == 0)
        {
            _onMarsRows.AddChild(CreateInfoLabel("No brother is training on Mars."));
        }
        foreach (ArmoryMarsRow row in view.OnMars)
        {
            _onMarsRows.AddChild(CreateDataRow(
                row.Name,
                $"Departed {row.Departed}",
                $"Returns {row.Returns}\n{row.WeeksRemaining} weeks",
                UiAccent.Body));
        }

        ClearContainer(_candidateRows);
        if (view.MarsCandidates.Count == 0)
        {
            _candidateRows.AddChild(CreateInfoLabel("No brother can be sent to Mars now."));
        }
        foreach (ArmoryMarsCandidateRow row in view.MarsCandidates)
        {
            _candidateRows.AddChild(CreateCandidateRow(row));
        }

        PopulateDestinations(view.ReturnDestination);
    }

    private void BuildLayout()
    {
        HBoxContainer root = new()
        {
            Name = "ArmoryContent",
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 16,
            OffsetTop = 16,
            OffsetRight = -16,
            OffsetBottom = -16
        };
        root.AddThemeConstantOverride("separation", 12);
        AddChild(root);

        root.AddChild(BuildSectionRail());
        root.AddChild(BuildTechmarinePanel());
    }

    // The Armory's sections. Armour, weapons and vehicles are reserved places for the chapter's
    // equipment counts; they stay disabled until that work lands.
    private Control BuildSectionRail()
    {
        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(280, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyContentPanel(panel);
        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 10);
        panel.AddChild(stack);
        stack.AddChild(CreateSectionLabel("Armory Sections"));

        Button techmarines = CreateSectionButton("Techmarines\nranks, Mars, candidates", "armamentarium", false);
        OnlyWarStyle.ApplyAccentButtonRow(techmarines, true, OnlyWarStyle.Gold);
        stack.AddChild(techmarines);
        stack.AddChild(CreateSectionButton("Armour\nnot yet tracked", "locked", true));
        stack.AddChild(CreateSectionButton("Weapons\nnot yet tracked", "locked", true));
        stack.AddChild(CreateSectionButton("Vehicles\nnot yet tracked", "vehicle", true));
        return panel;
    }

    private Control BuildTechmarinePanel()
    {
        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyContentPanel(panel);
        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 12);
        panel.AddChild(stack);

        stack.AddChild(BuildHero());
        _emptyLabel = CreateInfoLabel("Techmarine records appear here once a chapter with an Armory is loaded.");
        _emptyLabel.Visible = false;
        stack.AddChild(_emptyLabel);

        VBoxContainer section = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        section.AddThemeConstantOverride("separation", 12);
        _techmarineSection = section;
        stack.AddChild(section);

        _metrics = new HBoxContainer();
        _metrics.AddThemeConstantOverride("separation", 10);
        section.AddChild(_metrics);
        _loanLabel = CreateInfoLabel(string.Empty);
        section.AddChild(_loanLabel);

        HBoxContainer upper = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        upper.AddThemeConstantOverride("separation", 12);
        section.AddChild(upper);
        _atHomeRows = CreateSection(upper, "Techmarines at Home");
        _onMarsRows = CreateSection(upper, "Training on Mars");

        HBoxContainer lower = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        lower.AddThemeConstantOverride("separation", 12);
        section.AddChild(lower);
        _candidateRows = CreateSection(lower, "Send to Mars");
        lower.AddChild(BuildDestinationPanel());
        return panel;
    }

    private HBoxContainer BuildHero()
    {
        HBoxContainer hero = new() { CustomMinimumSize = new Vector2(0, 80) };
        hero.AddThemeConstantOverride("separation", 12);
        _heroIcon = new TextureRect
        {
            Texture = IconAtlas.GetIcon("armamentarium"),
            CustomMinimumSize = new Vector2(64, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        hero.AddChild(_heroIcon);
        VBoxContainer titles = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 3);
        _title = new Label { Text = "Armory" };
        _title.AddThemeFontSizeOverride("font_size", 28);
        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        titles.AddChild(_title);
        titles.AddChild(_subtitle);
        hero.AddChild(titles);
        return hero;
    }

    private Control BuildDestinationPanel()
    {
        PanelContainer panel = CreateInsetPanel();
        panel.CustomMinimumSize = new Vector2(340, 0);
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 8);
        panel.AddChild(stack);
        stack.AddChild(CreateSectionLabel("Returnees Report To"));
        _destinationPicker = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        _destinationPicker.ItemSelected += OnDestinationItemSelected;
        stack.AddChild(_destinationPicker);
        _destinationResolved = CreateInfoLabel(string.Empty);
        stack.AddChild(_destinationResolved);
        stack.AddChild(CreateInfoLabel(
            "If the chosen place no longer exists when a brother returns, he reports to the "
            + "Armory's duty station."));
        return panel;
    }

    private void PopulateDestinations(ArmoryReturnDestinationView destination)
    {
        _populatingDestinations = true;
        _destinationPicker.Clear();
        _destinationKeys.Clear();
        int selected = 0;
        foreach (ArmoryDestinationOption option in destination?.Options ?? [])
        {
            if (option.Key == destination.SelectedKey) selected = _destinationKeys.Count;
            _destinationPicker.AddItem(option.Label, _destinationKeys.Count);
            _destinationKeys.Add(option.Key);
        }
        if (_destinationKeys.Count > 0) _destinationPicker.Select(selected);
        _destinationPicker.Disabled = _destinationKeys.Count == 0;
        _destinationResolved.Text = destination?.Resolved ?? string.Empty;
        _populatingDestinations = false;
    }

    private void OnDestinationItemSelected(long index)
    {
        if (_populatingDestinations || index < 0 || index >= _destinationKeys.Count) return;
        ReturnDestinationSelected?.Invoke(this, _destinationKeys[(int)index]);
    }

    private Control CreateTechmarineRow(ArmoryTechmarineRow row)
    {
        PanelContainer panel = CreateRowPanel(UiAccent.Body);
        HBoxContainer line = new();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);
        line.AddChild(CreateTextStack($"{row.Role} {row.Name}", row.Location));
        line.AddChild(CreateStatusLabel(row.Status, row.Status == "Duty-ready" ? UiAccent.Stable : UiAccent.Warning));
        if (row.Promotions.Count > 0)
        {
            VBoxContainer buttons = new() { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            buttons.AddThemeConstantOverride("separation", 4);
            foreach (ArmoryPromotionOption option in row.Promotions)
            {
                Button promote = new()
                {
                    Text = $"Promote to {option.RoleName}",
                    Disabled = !option.CanPromote,
                    TooltipText = option.BlockedReason ?? string.Empty,
                    MouseDefaultCursorShape = CursorShape.PointingHand
                };
                PromotionRequest request = new(row.SoldierId, option.TemplateId);
                promote.Pressed += () => PromotePressed?.Invoke(this, request);
                buttons.AddChild(promote);
            }
            line.AddChild(buttons);
        }
        return panel;
    }

    private Control CreateCandidateRow(ArmoryMarsCandidateRow row)
    {
        PanelContainer panel = CreateRowPanel(UiAccent.Stable);
        HBoxContainer line = new();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);
        line.AddChild(CreateTextStack(
            $"{row.Role} {row.Name}",
            $"{row.Formation} · {row.Location} · Tech {row.Tech}"));
        Button send = new()
        {
            Text = "Send to Mars",
            MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        int soldierId = row.SoldierId;
        send.Pressed += () => SendToMarsPressed?.Invoke(this, soldierId);
        line.AddChild(send);
        return panel;
    }

    private Control CreateDataRow(string title, string subtitle, string status, UiAccent accent)
    {
        PanelContainer panel = CreateRowPanel(accent);
        HBoxContainer line = new();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);
        line.AddChild(CreateTextStack(title, subtitle));
        line.AddChild(CreateStatusLabel(status, accent));
        return panel;
    }

    private static PanelContainer CreateRowPanel(UiAccent accent)
    {
        PanelContainer panel = new() { CustomMinimumSize = new Vector2(0, 58), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyTintedListRow(panel, false, OnlyWarStyle.WithAlpha(OnlyWarStyle.Resolve(accent), 0.75f));
        return panel;
    }

    private static VBoxContainer CreateTextStack(string title, string subtitle)
    {
        VBoxContainer stack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 0);
        Label titleLabel = new() { Text = title, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = title };
        Label subtitleLabel = new() { Text = subtitle, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = subtitle };
        subtitleLabel.AddThemeFontSizeOverride("font_size", 12);
        subtitleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        stack.AddChild(titleLabel);
        stack.AddChild(subtitleLabel);
        return stack;
    }

    private static Label CreateStatusLabel(string text, UiAccent accent)
    {
        Label label = new()
        {
            Text = text ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(118, 0)
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.Resolve(accent));
        return label;
    }

    private static Button CreateSectionButton(string text, string iconKey, bool disabled)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 58),
            Alignment = HorizontalAlignment.Left,
            Disabled = disabled,
            MouseDefaultCursorShape = disabled ? CursorShape.Arrow : CursorShape.PointingHand
        };
        IconAtlas.Apply(button, iconKey);
        return button;
    }

    private static VBoxContainer CreateSection(Container parent, string title)
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

    private static Control CreateMetricPanel(string label, string value, UiAccent accent)
    {
        PanelContainer panel = CreateInsetPanel();
        panel.CustomMinimumSize = new Vector2(150, 64);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        VBoxContainer stack = new();
        panel.AddChild(stack);
        Label valueNode = new() { Text = value, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        valueNode.AddThemeFontSizeOverride("font_size", 20);
        valueNode.AddThemeColorOverride("font_color", accent == UiAccent.Body ? OnlyWarStyle.BodyText : OnlyWarStyle.Resolve(accent));
        stack.AddChild(CreateSectionLabel(label));
        stack.AddChild(valueNode);
        return panel;
    }

    private static PanelContainer CreateInsetPanel()
    {
        PanelContainer panel = new();
        OnlyWarStyle.ApplyInsetPanel(panel);
        return panel;
    }

    private static Label CreateSectionLabel(string text)
    {
        Label label = new() { Text = text.ToUpperInvariant() };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

    private static Label CreateInfoLabel(string text)
    {
        Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
