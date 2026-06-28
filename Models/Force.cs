using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Models
{
    public class EventHistory
    {
        public string EventTitle { get; set; }
        public List<string> SubEvents { get; private set; }
        public EventHistory()
        {
            SubEvents = [];
        }
    }

    public class MilitaryTopLevel
    {
        public string ForceName { get; }
        public Character Leader { get; }
        public string LeaderTitle { get; }
        public MilitaryTopLevel(string forceName, Character leader, string title)
        {
            ForceName = forceName;
            Leader = leader;
            LeaderTitle = title;
        }
    }

    public class Fleet : MilitaryTopLevel
    {
        public List<TaskForce> TaskForces { get; }

        public Fleet(string fleetName, Character leader, string title)
            : base(fleetName, leader, title)
        {
            TaskForces = [];
        }
    }

    public class Army : MilitaryTopLevel
    {
        public Unit OrderOfBattle { get; }
        public Dictionary<int, PlayerSoldier> PlayerSoldierMap { get; }
        public Dictionary<int, Squad> SquadMap { get; private set; }
        // The chapter's abstract supply/favor currency (PRD 4.23): earned from request
        // fulfillment, spent on medical procedures and (later) other materiel sinks.
        public int Requisition { get; set; }
        // Medical procedures in progress in the Apothecarium (PRD 4.8 / 5.3), resolved
        // each turn until complete.
        public List<MedicalProcedure> MedicalProcedures { get; }
        // Brothers who have fallen are removed from the active roster but their dossiers
        // (history, kills, awards) are retained here so the chapter can honor them
        // (PRD 4.12). These soldiers belong to no squad.
        public Dictionary<int, PlayerSoldier> FallenBrothers { get; }

        public Army(string armyName, Character leader, string title, Unit unit, IEnumerable<PlayerSoldier> soldiers)
            : base(armyName, leader, title)
        {
            PlayerSoldierMap = soldiers.ToDictionary(s => s.Id);
            FallenBrothers = [];
            MedicalProcedures = [];
            OrderOfBattle = unit;
        }

        public void PopulateSquadMap()
        {
            if (SquadMap == null)
            {
                SquadMap = [];
                foreach (Squad squad in OrderOfBattle.Squads)
                {
                    SquadMap[squad.Id] = squad;
                }
                foreach (Unit company in OrderOfBattle.ChildUnits)
                {
                    foreach (Squad squad in company.Squads)
                    {
                        SquadMap[squad.Id] = squad;
                    }
                }
            }
        }
    }

    public class SectorForce
    {
        private readonly Dictionary<Date, List<EventHistory>> _battleHistory;
        public IReadOnlyDictionary<Date, List<EventHistory>> BattleHistory => _battleHistory;
        public Faction Faction { get; }
        public Army Army { get; }
        public Character Leader { get; }
        public Fleet Fleet { get; }
        public List<IRequest> Requests { get; }
        public SectorForce(Faction faction, Character leader, Army army, Fleet fleet)
        {
            Faction = faction;
            Leader = leader;
            Army = army;
            Fleet = fleet;
            _battleHistory = [];
            Requests = [];
        }

        public void AddToBattleHistory(Date date, string title, List<string> events)
        {
            if (!_battleHistory.ContainsKey(date))
            {
                _battleHistory[date] = [];
            }
            EventHistory history = new EventHistory
            {
                EventTitle = title
            };
            history.SubEvents.AddRange(events);
            _battleHistory[date].Add(history);
        }
    }

    public class PlayerForce : SectorForce
    {
        public ushort GeneseedStockpile { get; set; }

        // Count-weighted aggregate purity (0..1) of the sealed gene-seed in the vault
        // (PRD 4.8). Tracked and persisted now; consumed when initiate creation lands
        // (PRD 4.9, post-0.7). Defaults to pristine; the stockpile starts empty.
        public float GeneseedPurity { get; set; }

        public PlayerForce(Faction faction, Army army, Fleet fleet)
            : base(faction, null, army, fleet)
        {
            GeneseedStockpile = 0;
            GeneseedPurity = 1.0f;
        }

        // Adds one recovered gland of the given purity to the stockpile, folding it into the
        // count-weighted aggregate purity before incrementing the count.
        public void AddRecoveredGeneseed(float purity)
        {
            GeneseedPurity = (GeneseedPurity * GeneseedStockpile + purity) / (GeneseedStockpile + 1);
            GeneseedStockpile++;
        }
    }
}
