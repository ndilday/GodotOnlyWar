using System;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Supply;

namespace OnlyWar.Campaign.Builders
{
    internal sealed class RequestFactory
    {
        private readonly IPersistentIdAllocator _identity;

        public RequestFactory(IPersistentIdAllocator identity)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        }

        public IRequest GenerateNewRequest(Planet planet, Character requester, Faction threatFaction,
                                           Date dateRequestMade, Date fulfilledDate = null)
        {
            return new PresenceRequest(_identity.GetNextRequestId(), planet, requester, threatFaction,
                                       dateRequestMade, fulfilledDate);
        }

        public IRequest GenerateNewRequest(
            Planet planet,
            Character requester,
            Faction threatFaction,
            Date dateRequestMade,
            Date deadline,
            ForceCommitmentPackage commitment,
            int offeredRequisition,
            PledgeScheduleKind offeredScheduleKind,
            int offeredCadenceWeeks,
            int offeredDeliveryDelayWeeks,
            RequestSeverity severity,
            RequestHazard hazard)
        {
            return new PresenceRequest(
                _identity.GetNextRequestId(),
                planet,
                requester,
                threatFaction,
                dateRequestMade,
                deadline,
                commitment,
                offeredRequisition,
                offeredScheduleKind,
                offeredCadenceWeeks,
                offeredDeliveryDelayWeeks,
                severity,
                hazard);
        }
    }
}
