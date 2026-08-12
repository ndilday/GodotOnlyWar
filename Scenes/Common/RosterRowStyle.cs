using Godot;
using OnlyWar.Helpers.UI;

/// <summary>
/// Shared dimensions and small construction helpers for roster rows that use the Chapter screen's
/// compact 48px icon treatment.
/// </summary>
public static class RosterRowStyle
{
    public const int IconSize = 48;
    public const int StandardRowHeight = 58;
    public const int SoldierRowHeight = 56;
    public const int SoldierReplacementRowHeight = 72;
    public const int SoldierInfoButtonSize = 24;
    public const int SoldierRowVerticalPadding = 4;
    public const int ContentSeparation = 8;

    public static int GetRowHeight(bool isSoldierEntry, bool hasMultilineStatus = false) =>
        isSoldierEntry
            ? hasMultilineStatus ? SoldierReplacementRowHeight : SoldierRowHeight
            : StandardRowHeight;

    public static void ApplyCompactSoldierRow(PanelContainer row, bool selected)
    {
        StyleBoxFlat compactRowStyle = OnlyWarStyle.GetListRowStyle(selected);
        compactRowStyle.ContentMarginTop = SoldierRowVerticalPadding;
        compactRowStyle.ContentMarginBottom = SoldierRowVerticalPadding;
        row.AddThemeStyleboxOverride("panel", compactRowStyle);
    }

    public static TextureRect CreateIconRect(string iconKey, int size = IconSize)
    {
        return new TextureRect
        {
            Texture = IconAtlas.GetIcon(iconKey),
            CustomMinimumSize = new Vector2(size, size),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
    }
}
