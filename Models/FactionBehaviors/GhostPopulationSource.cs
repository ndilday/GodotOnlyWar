using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Models.FactionBehaviors
{
    /// <summary>
    /// An off-map latent population source. It is not a Planet and is independent of visibility.
    /// </summary>
    public class GhostPopulationSource
    {
        public int Id { get; set; }
        public int? FactionId { get; }
        public Coordinate Position { get; }
        public PlanetTemplate WorldType { get; }
        public long Population { get; set; }
        public long PopulationCapacity { get; set; }
        public double Consolidation { get; set; }

        public GhostPopulationSource(int id, Coordinate position, PlanetTemplate worldType,
            long population, long populationCapacity, double consolidation, Faction faction = null)
        {
            Id = id;
            FactionId = faction?.Id;
            Position = position;
            WorldType = worldType ?? throw new ArgumentNullException(nameof(worldType));
            Population = Math.Max(0, population);
            PopulationCapacity = Math.Max(1, populationCapacity);
            Consolidation = Math.Clamp(consolidation, 0.0, 1.0);
        }
    }
}
