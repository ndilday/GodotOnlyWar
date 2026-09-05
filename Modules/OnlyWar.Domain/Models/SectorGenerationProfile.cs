using System;

namespace OnlyWar.Models
{
    /// <summary>
    /// Data-owned inputs for sector generation. The generation algorithms remain code-owned;
    /// this profile only supplies their validated dimensions, density, and subsector scale.
    /// All dimensions are measured in sector grid cells (one cell is one light year in the
    /// current coordinate model), and PlanetSpawnProbability is normalized to [0, 1].
    /// </summary>
    public sealed class SectorGenerationProfile
    {
        public string Key { get; }
        public ushort SectorWidth { get; }
        public ushort SectorHeight { get; }
        public double PlanetSpawnProbability { get; }
        public ushort MaxSubsectorDiameter { get; }
        public bool IsDefault { get; }

        public SectorGenerationProfile(
            string key,
            int sectorWidth,
            int sectorHeight,
            double planetSpawnProbability,
            int maxSubsectorDiameter,
            bool isDefault)
        {
            Key = key;
            SectorWidth = ValidateDimension(sectorWidth, nameof(sectorWidth));
            SectorHeight = ValidateDimension(sectorHeight, nameof(sectorHeight));
            MaxSubsectorDiameter = ValidateDimension(
                maxSubsectorDiameter,
                nameof(maxSubsectorDiameter));

            if (double.IsNaN(planetSpawnProbability)
                || double.IsInfinity(planetSpawnProbability)
                || planetSpawnProbability < 0
                || planetSpawnProbability > 1)
            {
                throw new InvalidOperationException(
                    $"Sector generation profile '{key}' has an invalid planet spawn probability "
                    + $"'{planetSpawnProbability}'. Expected a value between 0 and 1.");
            }

            PlanetSpawnProbability = planetSpawnProbability;
            IsDefault = isDefault;
        }

        private static ushort ValidateDimension(int value, string fieldName)
        {
            if (value <= 0 || value > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Sector generation profile field '{fieldName}' must be between 1 and "
                    + $"{ushort.MaxValue}; found {value}.");
            }

            return (ushort)value;
        }
    }
}
