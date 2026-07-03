using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.UI
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

        public CommandTreeNode(string key, string text, IReadOnlyList<CommandTreeNode> children = null)
        {
            Key = key;
            Text = text;
            Children = children ?? Array.Empty<CommandTreeNode>();
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

    public static class RosterFormat
    {
        public static string SquadLabel(Squad squad)
        {
            string strength = $"{squad.Members.Count(member => member.CanFight)}/{squad.Members.Count}";
            string order = squad.CurrentOrders != null ? squad.CurrentOrders.Mission.MissionType.ToString() : "Unassigned";
            return $"{squad.Name} | {strength} | {order}";
        }

        public static bool MatchesFilter(Squad squad, string filterKey)
        {
            return filterKey switch
            {
                "unassigned" => squad.CurrentOrders == null,
                "injured" => squad.Members.Count(member => member.CanFight) < 5,
                _ => true
            };
        }
    }
}
