using System;
using System.Collections.Generic;

namespace OnlyWar.Application
{
    /// <summary>
    /// Presentation data for a row in a reusable hierarchy tree. The model deliberately contains
    /// no domain types: screens decide what a key means and the tree only owns rendering and state.
    /// Badges carry a <see cref="UiAccent"/> classification rather than a colour, so a query can
    /// build a whole tree without referencing the host palette. Rows may provide multiple badge
    /// lines when compact stacked metadata is more legible than one long summary.
    /// </summary>
    public sealed class HierarchyTreeItem
    {
        public string Key { get; }
        public string Text { get; }
        public IReadOnlyList<HierarchyTreeItem> Children { get; }
        public string IconKey { get; }
        public string Badge { get; }
        public IReadOnlyList<string> BadgeLines { get; }
        public string Tooltip { get; }
        public bool Selectable { get; }
        public bool IsSelected { get; }
        public UiAccent? BadgeAccent { get; }
        public int IconMaxWidth { get; }
        public int RowHeight { get; }
        public bool CollapsedByDefault { get; }
        public SquadRowViewModel SquadRow { get; }

        public HierarchyTreeItem(
            string key,
            string text,
            IReadOnlyList<HierarchyTreeItem> children = null,
            string iconKey = null,
            string badge = null,
            string tooltip = null,
            bool selectable = true,
            bool isSelected = false,
            UiAccent? badgeAccent = null,
            int iconMaxWidth = 0,
            int rowHeight = 0,
            bool collapsedByDefault = false,
            SquadRowViewModel squadRow = null,
            IReadOnlyList<string> badgeLines = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Text = text ?? "";
            Children = children ?? Array.Empty<HierarchyTreeItem>();
            IconKey = iconKey;
            Badge = badge ?? (badgeLines != null && badgeLines.Count > 0
                ? badgeLines[0] : null);
            BadgeLines = badgeLines ?? (string.IsNullOrWhiteSpace(Badge)
                ? Array.Empty<string>()
                : new[] { Badge });
            Tooltip = tooltip;
            Selectable = selectable;
            IsSelected = isSelected;
            BadgeAccent = badgeAccent;
            IconMaxWidth = iconMaxWidth;
            RowHeight = rowHeight;
            CollapsedByDefault = collapsedByDefault;
            SquadRow = squadRow;
        }
    }
}
