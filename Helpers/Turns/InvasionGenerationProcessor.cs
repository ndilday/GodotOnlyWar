using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>Creates and attracts persistent invasion forces.</summary>
    internal sealed class InvasionGenerationProcessor
    {
        private readonly StrategicInvasionLifecycleProcessor _lifecycle;

        internal InvasionGenerationProcessor(StrategicInvasionLifecycleProcessor lifecycle)
        {
            _lifecycle = lifecycle;
        }

        internal void ProcessAttractionAndFragmentation(Sector sector) =>
            _lifecycle.ProcessAttractionAndFragmentation(sector);

        internal StrategicInvasionForce EstablishOpeningInvasion(
            Sector sector,
            Planet planet,
            Faction faction) => _lifecycle.EstablishOpeningInvasion(sector, planet, faction);
    }
}
