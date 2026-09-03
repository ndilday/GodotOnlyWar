using OnlyWar.Models;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves dormant-population culling, consolidation, growth, and emergence in weekly order.
    /// </summary>
    internal sealed class DormantPopulationProcessor
    {
        private readonly StrategicInvasionLifecycleProcessor _lifecycle;

        internal DormantPopulationProcessor(StrategicInvasionLifecycleProcessor lifecycle)
        {
            _lifecycle = lifecycle;
        }

        internal void ProcessWeeklyState(Sector sector) => _lifecycle.ProcessWeeklyState(sector);
    }
}
