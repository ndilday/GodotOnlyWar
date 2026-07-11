using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

// One card in a dossier-style detail panel (hostile faction / local force / region / squad ...).
// Shared by the Region Ops dossier and the Planet Detail context panel so both read identically.
public class DossierCardData
{
    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<Tuple<string, string>> Rows { get; }
    public Color AccentColor { get; }
    public float? BarFraction { get; }

    public DossierCardData(string title, string subtitle, IReadOnlyList<Tuple<string, string>> rows, Color accentColor, float? barFraction = null)
    {
        Title = title;
        Subtitle = subtitle;
        Rows = rows;
        AccentColor = accentColor;
        BarFraction = barFraction;
    }
}

public static class DossierCard
{
    // Builds the accent-tinted card panel shared by the Region Ops dossier and the Planet Detail
    // right-hand panel: uppercase muted category title, accent-colored subtitle, muted label/value
    // rows, and an optional accent strength bar. An empty subtitle is skipped so category-only cards
    // (e.g. "Region") don't leave a blank line.
    public static Control Create(DossierCardData card)
    {
        PanelContainer cardPanel = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyTintedListRow(cardPanel, false, card.AccentColor);

        VBoxContainer cardStack = new();
        cardStack.AddThemeConstantOverride("separation", 4);
        cardPanel.AddChild(cardStack);

        Label titleLabel = new() { Text = card.Title.ToUpperInvariant() };
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        cardStack.AddChild(titleLabel);

        if (!string.IsNullOrEmpty(card.Subtitle))
        {
            Label subtitleLabel = new() { Text = card.Subtitle };
            subtitleLabel.AddThemeFontSizeOverride("font_size", 16);
            subtitleLabel.AddThemeColorOverride("font_color", card.AccentColor);
            cardStack.AddChild(subtitleLabel);
        }

        foreach (Tuple<string, string> row in card.Rows)
        {
            HBoxContainer rowBox = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Label label = new() { Text = row.Item1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            Label value = new() { Text = row.Item2, HorizontalAlignment = HorizontalAlignment.Right };
            value.AddThemeFontSizeOverride("font_size", 12);
            rowBox.AddChild(label);
            rowBox.AddChild(value);
            cardStack.AddChild(rowBox);
        }

        if (card.BarFraction.HasValue)
        {
            ProgressBar bar = new()
            {
                MinValue = 0,
                MaxValue = 1,
                Value = Math.Clamp(card.BarFraction.Value, 0, 1),
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 8)
            };
            StyleBoxFlat fill = new() { BgColor = card.AccentColor, CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1, CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1 };
            bar.AddThemeStyleboxOverride("fill", fill);
            cardStack.AddChild(bar);
        }

        return cardPanel;
    }
}
