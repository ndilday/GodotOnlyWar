namespace OnlyWar.Domain
{
    /// <summary>
    /// Intrinsic faction role identity. This is a property of the faction itself, not a
    /// relationship question, so it lives with the entity rather than in the relationship service
    /// (which answers "are these two hostile?" and keeps that separate on purpose).
    /// </summary>
    public static class FactionRoles
    {
        /// <summary>
        /// True for the player's Chapter and for the Imperial default faction. Useful for
        /// governance and Chapter supply; never use it to decide whether two factions are enemies.
        /// </summary>
        public static bool IsImperial(Faction faction) =>
            faction != null && (faction.IsPlayerFaction || faction.IsDefaultFaction);
    }
}
