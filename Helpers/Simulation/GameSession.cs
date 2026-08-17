using OnlyWar.Models;
using OnlyWar.Helpers;
using OnlyWar.Models.Events;
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
        private readonly NamedRandomStreamFactory _explicitNamedRandomStreams;
        private NamedRandomStreamFactory _namedRandomStreams;
        private int _namedRandomStreamWeek = -1;
        internal NamedRandomStreamFactory NamedRandomStreams
        {
            get
            {
                int week = CurrentDate.GetTotalWeeks();
                if (_explicitNamedRandomStreams != null) return _explicitNamedRandomStreams;
                if (_namedRandomStreams == null || _namedRandomStreamWeek != week)
                {
                    _namedRandomStreams = new NamedRandomStreamFactory(
                        Sector.PlayerForce?.CampaignIdentity ?? CampaignIdentity.Empty,
                        week);
                    _namedRandomStreamWeek = week;
                }
                return _namedRandomStreams;
            }
        }

        internal GameSession(
            GameRulesData rules,
            Sector sector,
            Date currentDate,
            IRNG random,
            NamedRandomStreamFactory namedRandomStreams = null)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Sector = sector ?? throw new ArgumentNullException(nameof(sector));
            CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            _explicitNamedRandomStreams = namedRandomStreams;
        }
    }
}
