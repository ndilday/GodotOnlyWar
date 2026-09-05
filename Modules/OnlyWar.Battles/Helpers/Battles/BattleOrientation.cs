namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Eight-way battle facing. Zero points toward increasing Y and headings advance in
    /// 45-degree steps. Grid footprints remain axis-aligned; only due-east/due-west facings
    /// swap a non-square figure's width and depth.
    /// </summary>
    public static class BattleOrientation
    {
        public const ushort HeadingCount = 8;

        public static bool IsFootprintRotated(ushort orientation)
        {
            ushort heading = (ushort)(orientation % HeadingCount);
            return heading == 2 || heading == 6;
        }
    }
}
