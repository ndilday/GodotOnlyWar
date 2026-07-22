using System;
using System.Linq;
using OnlyWar.Models;

namespace OnlyWar.Helpers.Battles.Placers
{
    /// <summary>
    /// Shared geometry for the loose, sawtooth formation used by every battle placer.
    /// Keeping the offsets and reported bounds together prevents army placers from
    /// packing squads into the empty cells inside another squad's bounding box.
    /// </summary>
    internal readonly record struct SquadFormationGeometry(
        int RankCount,
        int MembersPerRank,
        int CellWidth,
        int CellDepth,
        int LateralPitch,
        Coordinate Bounds)
    {
        public static SquadFormationGeometry For(BattleSquad squad)
        {
            ArgumentNullException.ThrowIfNull(squad);
            var ableSoldiers = squad.AbleSoldiers;
            if (ableSoldiers.Count == 0)
            {
                throw new InvalidOperationException($"No soldiers in {squad.Name} to place");
            }

            int rankCount = ableSoldiers.Count >= 30
                ? 3
                : ableSoldiers.Count > 7 ? 2 : 1;
            int membersPerRank = (int)Math.Ceiling((double)ableSoldiers.Count / rankCount);
            int cellWidth = ableSoldiers.Max(soldier =>
                (int)soldier.Soldier.Template.Species.Width);
            int cellDepth = ableSoldiers.Max(soldier =>
                (int)soldier.Soldier.Template.Species.Depth);
            int lateralPitch = cellWidth + 1;

            // Files have one empty lateral cell between them. Alternating ranks are
            // shifted one cell, so multi-rank formations need one additional cell of
            // frontage to contain the shifted final file.
            int frontage = ((membersPerRank - 1) * lateralPitch) + cellWidth
                + (rankCount > 1 ? 1 : 0);
            int depth = rankCount * cellDepth;

            return new SquadFormationGeometry(
                rankCount,
                membersPerRank,
                cellWidth,
                cellDepth,
                lateralPitch,
                new Coordinate((ushort)frontage, (ushort)depth));
        }
    }
}
