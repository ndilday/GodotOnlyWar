using System;
using System.Collections.Generic;

namespace OnlyWar.Host.Presentation.UI
{
    [Flags]
    public enum MapLayer
    {
        None = 0,
        Forces = 1,
        Orders = 2,
        Intel = 4
    }

    public class CommandTreeNode
    {
        public string Key { get; }
        public string Text { get; }
        public IReadOnlyList<CommandTreeNode> Children { get; }
        public string IconKey { get; }
        public string Badge { get; }
        public bool Selectable { get; }

        public CommandTreeNode(string key, string text, IReadOnlyList<CommandTreeNode> children = null)
        {
            Key = key;
            Text = text;
            Children = children ?? Array.Empty<CommandTreeNode>();
            IconKey = null;
            Badge = null;
            Selectable = true;
        }

        public CommandTreeNode(
            string key,
            string text,
            IReadOnlyList<CommandTreeNode> children,
            string iconKey,
            string badge,
            bool selectable = true)
        {
            Key = key;
            Text = text;
            Children = children ?? Array.Empty<CommandTreeNode>();
            IconKey = iconKey;
            Badge = badge;
            Selectable = selectable;
        }
    }

    public class CommandAction
    {
        public string Key { get; }
        public string Text { get; }
        public string IconKey { get; }
        public bool Enabled { get; }

        public CommandAction(string key, string text, string iconKey, bool enabled)
        {
            Key = key;
            Text = text;
            IconKey = iconKey;
            Enabled = enabled;
        }
    }
}
