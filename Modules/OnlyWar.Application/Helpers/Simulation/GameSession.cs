using OnlyWar.Domain;
using OnlyWar.Application.Abstractions;
using OnlyWar.Runtime.Allocators;
using System;

namespace OnlyWar.Application.Session
{
    /// <summary>
    /// Immutable dependencies shared by simulations belonging to one loaded game session.
    /// </summary>
    public sealed class GameSession : ICampaignSimulationSession
    {
        public GameRulesData Rules { get; }
        public Sector Sector { get; }
        public Date CurrentDate { get; }
        public IRNG Random { get; }
        public bool UpgradePending { get; internal set; }

        public GameSession(
            GameRulesData rules,
            Sector sector,
            Date currentDate,
            IRNG random,
            IPersistentIdAllocator identity = null)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Sector = sector ?? throw new ArgumentNullException(nameof(sector));
            CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Identity = identity ?? new PersistentIdAllocator();
            CampaignEventBindings.Attach(Sector, CurrentDate);
        }

        public IPersistentIdAllocator Identity { get; }
    }
}
