using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Host.Presentation
{
    public sealed record MissionDebriefLineGroup(
        ushort? Day,
        IReadOnlyList<MissionDebriefLineView> Lines);

    public static class MissionDebriefLineGrouper
    {
        public static IReadOnlyList<MissionDebriefLineGroup> GroupByDay(
            IReadOnlyList<MissionDebriefLineView> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return Array.Empty<MissionDebriefLineGroup>();
            }

            List<MissionDebriefLineGroup> groups = new();
            foreach (IGrouping<ushort, MissionDebriefLineView> group in lines.Where(line => line.Day.HasValue)
                         .GroupBy(line => line.Day.Value)
                         .OrderBy(group => group.Key))
            {
                groups.Add(new MissionDebriefLineGroup(group.Key, group.ToList()));
            }

            groups.AddRange(lines
                .Where(line => !line.Day.HasValue)
                .Select(line => new MissionDebriefLineGroup(null, new[] { line })));
            return groups;
        }
    }
}
