using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed record MissionDebriefLineGroup(
        ushort? Day,
        IReadOnlyList<MissionDebriefLine> Lines);

    public static class MissionDebriefLineGrouper
    {
        public static IReadOnlyList<MissionDebriefLineGroup> GroupByDay(
            IReadOnlyList<MissionDebriefLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return Array.Empty<MissionDebriefLineGroup>();
            }

            List<MissionDebriefLineGroup> groups = new();
            foreach (IGrouping<ushort, MissionDebriefLine> group in lines.Where(line => line.Day.HasValue)
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
