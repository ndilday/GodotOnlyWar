using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;

namespace OnlyWar.Models
{
    /// <summary>
    /// A campaign location is exactly one embarked ship or one landed region.
    /// Keeping this as a value prevents callers from accidentally treating a soldier as
    /// occupying both places at once.
    /// </summary>
    public sealed record CampaignLocation
    {
        public Ship Ship { get; }
        public Region Region { get; }
        public bool IsShip => Ship != null;
        public bool IsRegion => Region != null;

        private CampaignLocation(Ship ship, Region region)
        {
            Ship = ship;
            Region = region;
        }

        public static CampaignLocation Aboard(Ship ship) =>
            ship == null ? null : new CampaignLocation(ship, null);

        public static CampaignLocation Landed(Region region) =>
            region == null ? null : new CampaignLocation(null, region);

        public bool IsSamePlace(CampaignLocation other) =>
            other != null
            && ((Ship != null && ReferenceEquals(Ship, other.Ship))
                || (Region != null && ReferenceEquals(Region, other.Region)));

        public override string ToString() => Ship?.Name ?? Region?.Name ?? "Unknown";
    }
}
