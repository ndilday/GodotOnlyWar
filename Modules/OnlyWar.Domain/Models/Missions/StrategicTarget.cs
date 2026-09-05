using OnlyWar.Models.Planets;

namespace OnlyWar.Models.Missions
{
    /// <summary>
    /// A mission target identified by intelligence rather than by a guaranteed current presence.
    /// The optional RegionFaction is resolved only when the world currently contains that faction;
    /// a missing presence is a valid no-contact search target.
    /// </summary>
    public sealed class StrategicTarget
    {
        public Region Region { get; }
        public Faction TargetFaction { get; }
        public FactionIntelBelief Belief { get; }
        public RegionFaction CurrentPresence { get; }
        public bool HasCurrentPresence => CurrentPresence != null;

        public StrategicTarget(
            Region region,
            Faction targetFaction,
            RegionFaction currentPresence = null,
            FactionIntelBelief belief = null)
        {
            Region = region ?? throw new System.ArgumentNullException(nameof(region));
            TargetFaction = targetFaction ?? throw new System.ArgumentNullException(nameof(targetFaction));
            if (currentPresence != null
                && (!ReferenceEquals(currentPresence.Region, region)
                    || currentPresence.PlanetFaction?.Faction?.Id != targetFaction.Id))
            {
                throw new System.ArgumentException(
                    "The current target presence does not match the strategic target.",
                    nameof(currentPresence));
            }

            CurrentPresence = currentPresence;
            Belief = belief;
        }
    }
}
