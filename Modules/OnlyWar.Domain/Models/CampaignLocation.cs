using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;

namespace OnlyWar.Domain
{
    /// <summary>
    /// A campaign location is exactly one embarked ship, one landed region, or off-sector.
    /// Keeping this as a value prevents callers from accidentally treating a soldier as
    /// occupying both places at once.
    ///
    /// Off-sector is a place outside the campaign map (a brother training on Mars). It has no
    /// ship and no region, so it is never <see cref="IsSamePlace"/> as anything, including
    /// itself: nobody in the sector is co-located with it.
    /// </summary>
    public sealed record CampaignLocation
    {
        public Ship Ship { get; }
        public Region Region { get; }
        public bool IsShip => Ship != null;
        public bool IsRegion => Region != null;
        public bool IsOffSector { get; }

        private CampaignLocation(Ship ship, Region region, bool isOffSector)
        {
            Ship = ship;
            Region = region;
            IsOffSector = isOffSector;
        }

        public static CampaignLocation Aboard(Ship ship) =>
            ship == null ? null : new CampaignLocation(ship, null, false);

        public static CampaignLocation Landed(Region region) =>
            region == null ? null : new CampaignLocation(null, region, false);

        public static CampaignLocation OffSector { get; } = new(null, null, true);

        public bool IsSamePlace(CampaignLocation other) =>
            other != null
            && ((Ship != null && ReferenceEquals(Ship, other.Ship))
                || (Region != null && ReferenceEquals(Region, other.Region)));

        public override string ToString() =>
            Ship?.Name ?? Region?.Name ?? (IsOffSector ? "Off-sector" : "Unknown");
    }
}
