using System.Collections.Generic;
using System;
using System.Linq;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.Supply;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Reports;
using OnlyWar.Models.Events;

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
        public LoadoutDoctrine LoadoutDoctrine { get; } = new();
        // Command staff and specialists are equipped by role and by individual rather than by
        // squad type, so their kit lives outside the squad doctrine hierarchy.
        public CharacterLoadoutDoctrine CharacterLoadoutDoctrine { get; } = new();
        // Itemized role and personal loadouts. This is deliberately separate from the legacy
        // weapon-set doctrine while existing pooled-squad UI and saves are retired.
        public EquipmentLoadoutDoctrine EquipmentLoadoutDoctrine { get; } = new();
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

        public void RegisterSquad(Squad squad)
        {
            if (squad == null) return;
            PopulateSquadMap();
            SquadMap[squad.Id] = squad;
        }

        public void UnregisterSquad(Squad squad)
        {
            if (squad == null) return;
            PopulateSquadMap();
            SquadMap.Remove(squad.Id);
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
        public List<Pledge> Pledges { get; }
        public SectorForce(Faction faction, Character leader, Army army, Fleet fleet)
        {
            Faction = faction;
            Leader = leader;
            Army = army;
            Fleet = fleet;
            _battleHistory = [];
            Requests = [];
            Pledges = [];
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
        public CampaignEventLedger CampaignEventLedger { get; }
        public CampaignEventLedger EventLedger => CampaignEventLedger;
        public CampaignEventRecorder CampaignEventRecorder { get; }
        public CampaignEventRecorder EventRecorder => CampaignEventRecorder;
        public TurnEventBuffer CurrentTurnEvents { get; } = new();
        public ChapterChronicleLedger ChapterChronicle { get; } = new();
        public WorldControlEpisodeTracker WorldControlEpisodes { get; private set; } = new();
        private CampaignIdentity _campaignIdentity;
        public CampaignIdentity CampaignIdentity
        {
            get => _campaignIdentity;
            set
            {
                _campaignIdentity = value ?? CampaignIdentity.Empty;
                CampaignEventRecorder?.SetCampaignIdentity(_campaignIdentity);
            }
        }

        public ushort GeneseedStockpile { get; set; }

        // The most recently resolved turn report. This is deliberately a bounded presentation
        // snapshot rather than the live mission/result graph, so it can safely travel through the
        // save path and remain available after a campaign reload.
        public LastTurnReportSnapshot LastTurnReportSnapshot { get; set; }

        // The planet granted to the Chapter by the opening scenario. Recruitment v1
        // deliberately has exactly one source world; later recruitment-right support can
        // broaden that without weakening the Home World's first-class identity.
        public int? HomeWorldPlanetId { get; set; }
        public RecruitmentProgram RecruitmentProgram { get; set; }

        // Count-weighted aggregate purity (0..1) of the sealed gene-seed in the vault
        // (PRD 4.8). Tracked and persisted now; consumed when initiate creation lands
        // (PRD 4.9, post-0.7). Defaults to pristine; the stockpile starts empty.
        public float GeneseedPurity { get; set; }

        public PlayerForce(Faction faction, Army army, Fleet fleet)
            : base(faction, null, army, fleet)
        {
            CampaignIdentity = OnlyWar.Models.Events.CampaignIdentity.CreateNew(1);
            CampaignEventLedger = new CampaignEventLedger(id =>
                army?.PlayerSoldierMap.GetValueOrDefault(id)
                ?? army?.FallenBrothers.GetValueOrDefault(id));
            CampaignEventRecorder = new CampaignEventRecorder(
                CampaignEventLedger,
                turnBuffer: CurrentTurnEvents,
                chronicle: ChapterChronicle,
                soldierResolver: id =>
                    army?.PlayerSoldierMap.GetValueOrDefault(id)
                    ?? army?.FallenBrothers.GetValueOrDefault(id));
            foreach (PlayerSoldier soldier in (army?.PlayerSoldierMap.Values
                                               ?? Enumerable.Empty<PlayerSoldier>())
                         .Concat(army?.FallenBrothers.Values
                             ?? Enumerable.Empty<PlayerSoldier>()))
            {
                soldier.AttachCampaignEventRecorder(CampaignEventRecorder);
            }
            CampaignEventRecorder.SetCampaignIdentity(CampaignIdentity);
            GeneseedStockpile = 0;
            GeneseedPurity = 1.0f;
            HomeWorldPlanetId = null;
            RecruitmentProgram = null;
        }

        internal void AttachCampaignEventRecorder(PlayerSoldier soldier)
        {
            soldier?.AttachCampaignEventRecorder(CampaignEventRecorder);
        }

        internal void RestoreWorldControlEpisodes(IEnumerable<WorldControlEpisodeState> states) =>
            WorldControlEpisodes = new WorldControlEpisodeTracker(states);

        // Production callers use this boundary for the canonical battle fact. The legacy
        // AddToBattleHistory method remains available to the load/migration compatibility path,
        // but new-format persistence must not depend on its free-text representation.
        internal CampaignEvent RecordBattleResolved(
            Date date,
            string title,
            IReadOnlyList<string> subEvents,
            string correlationKey,
            string dedupeKey)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("A battle title is required.", nameof(title));
            if (string.IsNullOrWhiteSpace(correlationKey))
                throw new ArgumentException("A battle correlation key is required.", nameof(correlationKey));
            if (string.IsNullOrWhiteSpace(dedupeKey))
                throw new ArgumentException("A battle dedupe key is required.", nameof(dedupeKey));

            List<string> entries = subEvents?.ToList() ?? [];
            return CampaignEventRecorder.Record(new CampaignEventCandidate(
                CampaignEventType.BattleResolved,
                date.GetTotalWeeks(),
                date.GetTotalWeeks(),
                correlationKey,
                dedupeKey,
                1,
                new BattleResolvedPayload(title, string.Join(" ", entries))));
        }

        internal CampaignEvent RecordChapterFounded(
            Date date,
            ChapterFoundedPayload payload,
            int? chapterMasterId,
            string chapterMasterName,
            int promisedPlanetId,
            string promisedPlanetName)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            List<CampaignEventEntityRef> entities =
            [
                new CampaignEventEntityRef(
                    CampaignEntityKind.Chapter,
                    0,
                    CampaignEventEntityRole.Subject,
                    payload.ChapterName),
                new CampaignEventEntityRef(
                    CampaignEntityKind.Planet,
                    promisedPlanetId,
                    CampaignEventEntityRole.Location,
                    promisedPlanetName)
            ];
            if (chapterMasterId.HasValue && !string.IsNullOrWhiteSpace(chapterMasterName))
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    chapterMasterId.Value,
                    CampaignEventEntityRole.Authority,
                    chapterMasterName));
            }
            return CampaignEventRecorder.Record(new CampaignEventCandidate(
                CampaignEventType.ChapterFounded,
                date.GetTotalWeeks(),
                date.GetTotalWeeks(),
                null,
                "chapter/founded",
                1,
                payload,
                entities,
                surfaceHint: CampaignEventSurfaceFlags.ChapterChronicle,
                importanceHint: CampaignEventImportance.Defining,
                chronicleTreatmentHint: CampaignEventChronicleTreatment.Standalone));
        }

        internal CampaignEvent RecordProceduralDeath(
            Date date,
            PlayerSoldier soldier,
            string detail)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            int serviceStartWeek = soldier.SoldierEvents
                .Select(entry => entry.Date?.GetTotalWeeks())
                .Where(week => week.HasValue)
                .Select(week => week.Value)
                .DefaultIfEmpty(0)
                .Min();
            return CampaignEventRecorder.RecordDeath(
                soldier,
                date,
                new DeathPayload(
                    null,
                    DeathDisposition.NonBattleProcedural,
                    null,
                    null,
                    null,
                    null,
                    soldier.Template?.Id,
                    soldier.Template?.Name,
                    soldier.Template?.Rank,
                    serviceStartWeek,
                    soldier.FactionCasualtyCountMap.Values.Sum(value => (int)value),
                    null,
                    null,
                    false,
                    false,
                    detail,
                    SoldierSubrank: soldier.Template?.Subrank));
        }

        internal CampaignEvent RecordProceduralGeneseedRecovery(
            Date date,
            PlayerSoldier soldier,
            long sourceDeathEventId,
            GeneseedRecoveryOutcome outcome,
            float? purity = null)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            return CampaignEventRecorder.RecordGeneseedRecovery(
                soldier,
                date,
                new GeneseedRecoveryPayload(null, sourceDeathEventId, outcome, purity));
        }

        internal CampaignEvent RecordMentorAssigned(
            Date date,
            PlayerSoldier mentee,
            PlayerSoldier mentor,
            Squad scoutSquad)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (mentee == null) throw new ArgumentNullException(nameof(mentee));
            if (mentor == null) throw new ArgumentNullException(nameof(mentor));
            if (scoutSquad == null) throw new ArgumentNullException(nameof(scoutSquad));
            return CampaignEventRecorder.RecordMentorAssigned(
                mentee,
                date,
                new MentorAssignedPayload(
                    MentorRelationshipKind.ScoutMentor,
                    MentorAssignmentContext.NeophytePlacement,
                    scoutSquad.Id,
                    scoutSquad.Name,
                    mentor.Id,
                    mentor.Name));
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
