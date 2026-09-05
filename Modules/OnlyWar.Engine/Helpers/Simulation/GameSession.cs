using OnlyWar.Models;
using System;

namespace OnlyWar.Helpers.Simulation
{
    /// <summary>
    /// Immutable dependencies shared by simulations belonging to one loaded game session.
    /// </summary>
    public sealed class GameSession
    {
        public GameRulesData Rules { get; }
        public Sector Sector { get; }
        public Date CurrentDate { get; }
        public IRNG Random { get; }

        public GameSession(
            GameRulesData rules,
            Sector sector,
            Date currentDate,
            IRNG random)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Sector = sector ?? throw new ArgumentNullException(nameof(sector));
            CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            CampaignEventBindings.Attach(Sector, CurrentDate);
        }
    }
}
