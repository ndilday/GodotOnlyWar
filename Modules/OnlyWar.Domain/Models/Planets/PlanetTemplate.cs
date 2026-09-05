namespace OnlyWar.Models.Planets
{
    public class PlanetTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public int Probability { get; }
        public LogNormalValueTemplate PopulationRange { get; }
        // The absolute population a world of this type can sustain. Authored per type so
        // that dense biomes (Hive, Forge) sit close to their starting population while
        // sparse ones (Agri, Feral) leave generous headroom for growth. Starting
        // population is generated as a fraction of this value, never above it.
        public LogNormalValueTemplate CarryingCapacityRange { get; }
        public NormalizedValueTemplate ImportanceRange { get; }
        public LinearValueTemplate TaxRange { get; }

        public PlanetTemplate(int id, string name, int prob, LogNormalValueTemplate population,
                              LogNormalValueTemplate carryingCapacity, NormalizedValueTemplate importance,
                              LinearValueTemplate tax)
        {
            Id = id;
            Name = name;
            Probability = prob;
            PopulationRange = population;
            CarryingCapacityRange = carryingCapacity;
            ImportanceRange = importance;
            TaxRange = tax;
        }
    }
}
