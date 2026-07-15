using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Supply;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;

namespace OnlyWar.Helpers.Turns
{
    internal sealed class ChapterSupplyTurnProcessor
    {
        private readonly GameSession _session;

        internal ChapterSupplyTurnProcessor(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal void ProcessDeliveries()
        {
            var pledges = _session.Sector.PlayerForce.Pledges;
            for (int index = 0; index < pledges.Count; index++)
            {
                Pledge pledge = pledges[index];
                bool sourceAvailable = IsSourceFriendlyAndControlled(pledge.SourcePlanetId);
                PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
                    pledge, _session.CurrentDate, sourceAvailable);
                pledges[index] = result.Pledge;
                _session.Sector.PlayerForce.Army.Requisition += result.DeliveredRequisition;
            }
        }

        private bool IsSourceFriendlyAndControlled(int sourcePlanetId)
        {
            if (!_session.Sector.Planets.TryGetValue(sourcePlanetId, out Planet source))
            {
                return false;
            }

            Faction controller = source.GetControllingFaction();
            if (controller == null) return false;
            bool friendly = controller.IsDefaultFaction || controller.IsPlayerFaction;
            return friendly;
        }
    }
}
