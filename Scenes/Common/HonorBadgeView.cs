using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Soldiers.Ratings;
using System;
using System.Collections.Generic;

public partial class HonorBadgeView : HBoxContainer
{
    private static readonly Color Bronze = Color.Color8(181, 111, 58);
    private static readonly Color Silver = Color.Color8(190, 205, 214);
    private static readonly Color Gold = Color.Color8(240, 194, 72);
    private static readonly Color WhiteGold = Color.Color8(255, 244, 211);

    public void SetHonors(IReadOnlyList<HonorBadgeModel> honors)
    {
        // RemoveChild before freeing: QueueFree alone leaves the node parented until the end of the
        // frame, so stale badges keep taking part in this frame's container sort and minimum-size
        // propagation. Matches the clear-and-rebuild pattern used by the other screens.
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        foreach (HonorBadgeModel honor in honors ?? [])
        {
            int tier = Math.Clamp(honor.Level, (ushort)1, (ushort)4);
            Color tint = TierTint(tier);
            string tooltip = FormatTooltip(tier, honor.Name);
            VBoxContainer badge = new()
            {
                CustomMinimumSize = new Vector2(44, 44),
                TooltipText = tooltip
            };
            TextureRect icon = new()
            {
                CustomMinimumSize = new Vector2(44, 44),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Modulate = tint,
                MouseFilter = MouseFilterEnum.Pass
            };
            string iconKey = honor.IconAssetKey
                ?? AwardFamilyCatalog.CreateDefault().Get(honor.Type).IconAssetKey;
            icon.Texture = IconAtlas.GetIcon(IconAtlas.HasIcon(iconKey) ? iconKey : "award");
            badge.AddChild(icon);
            AddChild(badge);
        }
        if (honors == null || honors.Count == 0)
        {
            Label none = new() { Text = "—", TooltipText = "No recorded honors" };
            none.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            AddChild(none);
        }
    }

    public static Color TierTint(int tier) => tier switch
    {
        1 => Bronze,
        2 => Silver,
        3 => Gold,
        _ => WhiteGold
    };

    public static string TierName(int tier) => tier switch
    {
        1 => "Bronze",
        2 => "Silver",
        3 => "Gold",
        _ => "Adamantium white-gold"
    };

    private static string FormatTooltip(int tier, string honorName)
    {
        string tierName = TierName(tier);
        if (string.IsNullOrWhiteSpace(honorName)) return tierName;
        return honorName.StartsWith(tierName + " ", StringComparison.OrdinalIgnoreCase)
            ? honorName
            : $"{tierName} {honorName}";
    }

}
