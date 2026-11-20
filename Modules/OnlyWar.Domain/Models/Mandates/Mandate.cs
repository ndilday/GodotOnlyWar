using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Domain.Mandates
{
    /// <summary>
    /// The six mandate families of PRD §4.25. Chapter World has two
    /// entries, Establish and Secure. Persisted values are append-only.
    /// </summary>
    public enum MandateFamily
    {
        EstablishChapterWorld = 0,
        SecureChapterWorld = 1,
        RebuildCompany = 2,
        ProtectStrategicWorlds = 3,
        EradicateThreat = 4,
        Reputation = 5,
        SupplyNetwork = 6
    }

    /// <summary>Persisted values are append-only.</summary>
    public enum MandateState
    {
        Active = 0,
        Fulfilled = 1,
        Failed = 2,
        Abandoned = 3
    }

    /// <summary>What an Eradicate mandate names. Persisted values are append-only.</summary>
    public enum MandateThreatKind
    {
        None = 0,
        // A persistent strategic invasion force: a Waaagh!. Judged on every world.
        InvasionForce = 1,
        // One hostile faction's public presence on one world: a swarm, an insurrection, a cult.
        FactionOnWorld = 2
    }

    /// <summary>
    /// One goal the Chapter Master swore (PRD §4.25, TDD §6.15). A mandate keeps the
    /// identities it named when it was sworn; display names are snapshots so the Chronicle and
    /// the Brief can still describe it after the named entity is gone.
    /// </summary>
    public sealed class Mandate
    {
        private readonly List<int> _planetIds;

        public int Id { get; }
        public MandateFamily Family { get; }
        // The Promised World is the first mandate. It has no deadline and the scenario
        // processor, not the mandate evaluator, decides its result.
        public bool IsPromisedWorld { get; }
        public string Title { get; }
        public string SubjectName { get; }
        public int StartWeek { get; }
        public int? DeadlineWeek { get; }
        public IReadOnlyList<int> PlanetIds => _planetIds;
        public int? UnitId { get; }
        public int? FactionId { get; }
        public long? InvasionForceId { get; }
        public MandateThreatKind ThreatKind { get; }
        // Supply Network counts pledges with an id above this (pledge ids only grow).
        public int BaselinePledgeId { get; }
        // The family's legacy value when the mandate was sworn, so a later change to the
        // constants does not change what an old vow was worth.
        public int LegacyValue { get; }
        public MandateState State { get; private set; }
        public int? ResolvedWeek { get; private set; }
        // Secure the Chapter World: consecutive weeks with no public hostile presence.
        public int CleanWeeks { get; set; }

        public bool IsActive => State == MandateState.Active;

        public Mandate(
            int id,
            MandateFamily family,
            string title,
            string subjectName,
            int startWeek,
            int? deadlineWeek,
            int legacyValue,
            IEnumerable<int> planetIds = null,
            int? unitId = null,
            int? factionId = null,
            long? invasionForceId = null,
            MandateThreatKind threatKind = MandateThreatKind.None,
            int baselinePledgeId = -1,
            bool isPromisedWorld = false,
            MandateState state = MandateState.Active,
            int? resolvedWeek = null,
            int cleanWeeks = 0)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A title is required.", nameof(title));
            if (startWeek < 0) throw new ArgumentOutOfRangeException(nameof(startWeek));
            if (deadlineWeek.HasValue && deadlineWeek.Value < startWeek)
                throw new ArgumentOutOfRangeException(nameof(deadlineWeek));
            if (legacyValue < 0) throw new ArgumentOutOfRangeException(nameof(legacyValue));
            if (state != MandateState.Active && !resolvedWeek.HasValue)
                throw new ArgumentException("A resolved mandate needs its resolved week.", nameof(resolvedWeek));

            Id = id;
            Family = family;
            Title = title;
            SubjectName = string.IsNullOrWhiteSpace(subjectName) ? title : subjectName;
            StartWeek = startWeek;
            DeadlineWeek = deadlineWeek;
            LegacyValue = legacyValue;
            _planetIds = (planetIds ?? []).Distinct().ToList();
            UnitId = unitId;
            FactionId = factionId;
            InvasionForceId = invasionForceId;
            ThreatKind = threatKind;
            BaselinePledgeId = baselinePledgeId;
            IsPromisedWorld = isPromisedWorld;
            State = state;
            ResolvedWeek = resolvedWeek;
            CleanWeeks = System.Math.Max(0, cleanWeeks);
        }

        public void Resolve(MandateState state, int week)
        {
            if (state == MandateState.Active)
                throw new ArgumentException("A mandate cannot resolve to Active.", nameof(state));
            if (State != MandateState.Active)
                throw new InvalidOperationException($"Mandate {Id} is already {State}.");
            State = state;
            ResolvedWeek = week;
        }
    }

    /// <summary>What a legacy ledger entry records. Persisted values are append-only.</summary>
    public enum LegacySourceKind
    {
        MandateFulfilled = 0,
        MandateFailed = 1,
        WorldSaved = 2,
        WorldLost = 3,
        RequestFulfilled = 4,
        RequestFailed = 5,
        LargeBattleWon = 6,
        LargeBattleLost = 7,
        BrotherKilled = 8
    }

    /// <summary>
    /// One gain or loss of legacy (TDD §6.15 *State*). SourceKey is unique, so
    /// one source outcome writes exactly one entry. ChapterMasterId is the master in office when
    /// the entry was written, or null while the office was empty.
    /// </summary>
    public sealed record LegacyLedgerEntry(
        long Id,
        int Week,
        int Amount,
        LegacySourceKind SourceKind,
        string SourceKey,
        long? CampaignEventId,
        int? ChapterMasterId,
        string Description);

    /// <summary>One Chapter Master's time in office (§6.4). EndWeek is null while he holds it.</summary>
    public sealed class ChapterMasterTenure
    {
        public int SoldierId { get; }
        public string SoldierName { get; }
        public int StartWeek { get; }
        public int? EndWeek { get; private set; }
        public bool IsOpen => !EndWeek.HasValue;

        public ChapterMasterTenure(int soldierId, string soldierName, int startWeek, int? endWeek = null)
        {
            if (string.IsNullOrWhiteSpace(soldierName))
                throw new ArgumentException("A name snapshot is required.", nameof(soldierName));
            if (endWeek.HasValue && endWeek.Value < startWeek)
                throw new ArgumentOutOfRangeException(nameof(endWeek));
            SoldierId = soldierId;
            SoldierName = soldierName;
            StartWeek = startWeek;
            EndWeek = endWeek;
        }

        public void End(int week)
        {
            if (!IsOpen) throw new InvalidOperationException("The tenure has already ended.");
            EndWeek = System.Math.Max(StartWeek, week);
        }
    }

    /// <summary>
    /// The Chapter's mandates, its legacy ledger and its Chapter Masters' tenures. Totals are
    /// always sums over the ledger; nothing else stores a total (§6.1).
    /// </summary>
    public sealed class ChapterMandates
    {
        private readonly List<Mandate> _mandates = [];
        private readonly List<LegacyLedgerEntry> _ledger = [];
        private readonly Dictionary<string, LegacyLedgerEntry> _ledgerByKey = new(StringComparer.Ordinal);
        private readonly List<ChapterMasterTenure> _tenures = [];
        private readonly HashSet<int> _raisedCompanyIds = [];

        public IReadOnlyList<Mandate> All => _mandates;
        public IReadOnlyList<LegacyLedgerEntry> Ledger => _ledger;
        public IReadOnlyList<ChapterMasterTenure> Tenures => _tenures;
        public IReadOnlyCollection<int> RaisedCompanyIds => _raisedCompanyIds;

        public Mandate Active => _mandates.FirstOrDefault(mandate => mandate.IsActive);
        public ChapterMasterTenure CurrentTenure => _tenures.LastOrDefault(tenure => tenure.IsOpen);
        public int NextMandateId => _mandates.Count == 0 ? 1 : _mandates.Max(mandate => mandate.Id) + 1;
        public int ChapterTotal => _ledger.Sum(entry => entry.Amount);

        public int TotalFor(int? chapterMasterId) =>
            _ledger.Where(entry => entry.ChapterMasterId == chapterMasterId).Sum(entry => entry.Amount);

        public Mandate Get(int id) => _mandates.FirstOrDefault(mandate => mandate.Id == id);

        public void Add(Mandate mandate)
        {
            if (mandate == null) throw new ArgumentNullException(nameof(mandate));
            if (_mandates.Any(existing => existing.Id == mandate.Id))
                throw new InvalidOperationException($"Mandate {mandate.Id} already exists.");
            if (mandate.IsActive && Active != null)
                throw new InvalidOperationException("Only one mandate can be active at a time.");
            _mandates.Add(mandate);
        }

        public bool HasLegacyEntry(string sourceKey) =>
            sourceKey != null && _ledgerByKey.ContainsKey(sourceKey);

        /// <summary>
        /// Writes one ledger entry, attributed to the master now in office. A second write for the
        /// same source key returns the first entry and adds nothing.
        /// </summary>
        public LegacyLedgerEntry AddLegacy(
            int week,
            int amount,
            LegacySourceKind kind,
            string sourceKey,
            long? campaignEventId,
            string description)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                throw new ArgumentException("A source key is required.", nameof(sourceKey));
            if (_ledgerByKey.TryGetValue(sourceKey, out LegacyLedgerEntry existing)) return existing;
            long id = _ledger.Count == 0 ? 1 : _ledger.Max(entry => entry.Id) + 1;
            LegacyLedgerEntry entry = new(
                id, week, amount, kind, sourceKey, campaignEventId,
                CurrentTenure?.SoldierId, description ?? string.Empty);
            Restore(entry);
            return entry;
        }

        public void BeginTenure(int soldierId, string soldierName, int week)
        {
            if (CurrentTenure != null)
                throw new InvalidOperationException("The office of Chapter Master is already held.");
            _tenures.Add(new ChapterMasterTenure(soldierId, soldierName, week));
        }

        public void EndTenure(int week) => CurrentTenure?.End(week);

        public bool MarkCompanyRaised(int unitId) => _raisedCompanyIds.Add(unitId);

        public bool WasCompanyRaised(int unitId) => _raisedCompanyIds.Contains(unitId);

        // Load path only.
        public void Restore(LegacyLedgerEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!_ledgerByKey.TryAdd(entry.SourceKey, entry))
                throw new InvalidOperationException($"Legacy source '{entry.SourceKey}' is already recorded.");
            _ledger.Add(entry);
        }

        // Load path only.
        public void Restore(ChapterMasterTenure tenure)
        {
            if (tenure == null) throw new ArgumentNullException(nameof(tenure));
            if (tenure.IsOpen && CurrentTenure != null)
                throw new InvalidOperationException("A save cannot hold two open Chapter Master tenures.");
            _tenures.Add(tenure);
        }
    }
}
