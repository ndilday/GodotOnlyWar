using OnlyWar.Models;
using System;

namespace OnlyWar.Helpers.Simulation
{
    /// <summary>
    /// Immutable dependencies shared by simulations belonging to one loaded game session.
    /// </summary>
    internal sealed class GameSession
    {
        internal GameRulesData Rules { get; }
        internal Sector Sector { get; }
        internal Date CurrentDate { get; }
        internal IRNG Random { get; }

        internal GameSession(
            GameRulesData rules,
            Sector sector,
            Date currentDate,
            IRNG random)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Sector = sector ?? throw new ArgumentNullException(nameof(sector));
            CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
            Random = random ?? throw new ArgumentNullException(nameof(random));
        }
    }
}
