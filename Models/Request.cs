using OnlyWar.Models.Planets;
using System.Linq;

namespace OnlyWar.Models
{
    public interface IRequest
    {
        int Id { get; }
        Planet TargetPlanet { get; }
        Character Requester { get; }
        // the public, open-war faction the governor wants dealt with; null for a false alarm
        Faction ThreatFaction { get; }
        Date DateRequestMade { get; }
        Date DateRequestFulfilled { get; }
        bool IsRequestStarted();
        bool IsRequestCompleted();
    }

    public class PresenceRequest : IRequest
    {
        // a false-alarm request is fulfilled by a sustained Astartes presence
        private const int DWELL_WEEKS = 4;
        private bool _completed;
        // the first week (in the current unbroken streak) a player squad has been landed,
        // used to measure sustained presence for false-alarm requests
        private Date _dwellStart;
        public int Id { get; private set; }
        public Planet TargetPlanet { get; private set; }

        public Character Requester { get; private set; }

        public Faction ThreatFaction { get; private set; }

        public Date DateRequestMade { get; private set; }

        public Date DateRequestFulfilled { get; private set; }

        public PresenceRequest(int id, Planet planet, Character requester, Faction threatFaction,
                               Date dateRequestMade,  Date fulfilledDate = null)
        {
            Id = id;
            TargetPlanet = planet;
            Requester = requester;
            ThreatFaction = threatFaction;
            DateRequestMade = dateRequestMade;
            DateRequestFulfilled = fulfilledDate;
            _completed = fulfilledDate != null;
        }

        public bool IsRequestStarted()
        {
            if (_completed) return true;
            return ThreatFaction != null ? IsPlayerPresent() : _dwellStart != null;
        }

        public bool IsRequestCompleted()
        {
            if (_completed) return true;
            if (ThreatFaction != null)
            {
                // real threat: fulfilled when the open revolt has been put down, i.e. the
                // threat faction is no longer publicly in open war on the planet (driven back
                // into hiding, or eliminated entirely)
                if (!TargetPlanet.PlanetFactionMap.TryGetValue(ThreatFaction.Id, out PlanetFaction threat)
                    || !threat.IsPublic)
                {
                    MarkCompleted();
                    return true;
                }
                return false;
            }
            // false alarm: there is no real threat to break, so fulfillment requires a
            // sustained Astartes presence on the planet for several weeks
            if (!IsPlayerPresent())
            {
                _dwellStart = null;
                return false;
            }
            Date now = GameDataSingleton.Instance.Date;
            if (_dwellStart == null)
            {
                // capture a snapshot; the live game date mutates each week
                _dwellStart = new Date(now.GetTotalWeeks());
            }
            else if (now.GetWeeksDifference(_dwellStart) >= DWELL_WEEKS)
            {
                MarkCompleted();
                return true;
            }
            return false;
        }

        private void MarkCompleted()
        {
            _completed = true;
            DateRequestFulfilled = new Date(GameDataSingleton.Instance.Date.GetTotalWeeks());
        }

        private bool IsPlayerPresent()
        {
            Faction playerFaction = GameDataSingleton.Instance.GameRulesData.PlayerFaction;
            if (!TargetPlanet.PlanetFactionMap.ContainsKey(playerFaction.Id)) return false;
            return TargetPlanet.Regions
                .Where(r => r.RegionFactionMap.ContainsKey(playerFaction.Id))
                .Any(r => r.RegionFactionMap[playerFaction.Id].LandedSquads.Any());
        }
    }
}
