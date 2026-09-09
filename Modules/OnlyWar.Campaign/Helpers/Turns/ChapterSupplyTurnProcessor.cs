using OnlyWar.Campaign.Simulation;
using OnlyWar.Campaign.Supply;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Supply;
using System;

namespace OnlyWar.Campaign.Turns
{
    internal sealed class ChapterSupplyTurnProcessor
    {
        private readonly CampaignTurnContext _turn;

        internal ChapterSupplyTurnProcessor(ICampaignSimulationSession session)
            : this(CampaignTurnContext.From(session))
        {
        }

        internal ChapterSupplyTurnProcessor(CampaignTurnContext turn)
        {
            _turn = turn ?? throw new ArgumentNullException(nameof(turn));
        }

        internal void ProcessDeliveries()
        {
            var pledges = _turn.Sector.PlayerForce.Pledges;
            for (int index = 0; index < pledges.Count; index++)
            {
                Pledge pledge = pledges[index];
                bool sourceAvailable = IsSourceFriendlyAndControlled(pledge.SourcePlanetId);
                PledgeDeliveryResult result = PledgeDeliveryProcessor.Process(
                    pledge, _turn.CurrentDate, sourceAvailable);
                pledges[index] = result.Pledge;
                _turn.Sector.PlayerForce.Army.Requisition += result.DeliveredRequisition;
            }
        }

        private bool IsSourceFriendlyAndControlled(int sourcePlanetId)
        {
            if (!_turn.Sector.Planets.TryGetValue(sourcePlanetId, out Planet source))
            {
                return false;
            }

            Faction controller = source.GetControllingFaction();
            if (controller == null) return false;
            return FactionRelationshipService.IsImperial(controller);
        }
    }
}

