using Godot;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.UI
{
    /// <summary>
    /// Presentation data for a row in a reusable hierarchy tree. The model deliberately contains
    /// no domain types: screens decide what a key means and the tree only owns rendering and state.
    /// </summary>
    public sealed class HierarchyTreeItem
    {
        public string Key { get; }
        public string Text { get; }
        public IReadOnlyList<HierarchyTreeItem> Children { get; }
        public string IconKey { get; }
        public string Badge { get; }
        public string Tooltip { get; }
        public bool Selectable { get; }
        public bool IsSelected { get; }
        public Color? BadgeColor { get; }
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
            Color? badgeColor = null,
            int iconMaxWidth = 0,
            int rowHeight = 0,
            bool collapsedByDefault = false,
            SquadRowViewModel squadRow = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Text = text ?? "";
            Children = children ?? Array.Empty<HierarchyTreeItem>();
            IconKey = iconKey;
            Badge = badge;
            Tooltip = tooltip;
            Selectable = selectable;
            IsSelected = isSelected;
            BadgeColor = badgeColor;
            IconMaxWidth = iconMaxWidth;
            RowHeight = rowHeight;
            CollapsedByDefault = collapsedByDefault;
            SquadRow = squadRow;
        }
    }
}
