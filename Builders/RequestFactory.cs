using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;

namespace OnlyWar.Builders
{
    class RequestFactory
    {
        private RequestFactory() { }
        private static RequestFactory _instance;
        private static int _nextId = 0;
        public static RequestFactory Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new RequestFactory();
                }
                return _instance;
            }
        }

        public void SetCurrentHighestRequestId(int highestId)
        {
            _nextId = highestId + 1;
        }

        public IRequest GenerateNewRequest(Planet planet, Character requester, Faction threatFaction,
                                           Date dateRequestMade, Date fulfilledDate = null)
        {
            return new PresenceRequest(_nextId++, planet, requester, threatFaction,
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
                _nextId++, planet, requester, threatFaction, dateRequestMade, deadline,
                commitment, offeredRequisition, offeredScheduleKind, offeredCadenceWeeks,
                offeredDeliveryDelayWeeks, severity, hazard);
        }
    }
}
