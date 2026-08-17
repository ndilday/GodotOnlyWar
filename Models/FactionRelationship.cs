using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OnlyWar.Models
{
    public enum FactionStance
    {
        Hostile = 0,
        Neutral = 1,
        Allied = 2
    }

    /// <summary>
    /// The canonical key for a relationship. Input order is deliberately irrelevant.
    /// </summary>
    public readonly record struct FactionPair
    {
        public int LowerFactionId { get; }
        public int HigherFactionId { get; }

        public FactionPair(int firstFactionId, int secondFactionId)
        {
            if (firstFactionId == secondFactionId)
            {
                throw new ArgumentException("A faction relationship cannot pair a faction with itself.");
            }

            if (firstFactionId < 0 || secondFactionId < 0)
            {
                throw new ArgumentOutOfRangeException(
                    firstFactionId < 0 ? nameof(firstFactionId) : nameof(secondFactionId));
            }

            LowerFactionId = Math.Min(firstFactionId, secondFactionId);
            HigherFactionId = Math.Max(firstFactionId, secondFactionId);
        }

        public FactionPair(Faction first, Faction second)
            : this(
                first?.Id ?? throw new ArgumentNullException(nameof(first)),
                second?.Id ?? throw new ArgumentNullException(nameof(second)))
        {
        }
    }

    public sealed class FactionRelationshipChangedEventArgs : EventArgs
    {
        public FactionPair Pair { get; }
        public FactionStance PreviousStance { get; }
        public FactionStance CurrentStance { get; }

        internal FactionRelationshipChangedEventArgs(
            FactionPair pair,
            FactionStance previousStance,
            FactionStance currentStance)
        {
            Pair = pair;
            PreviousStance = previousStance;
            CurrentStance = currentStance;
        }
    }

    /// <summary>
    /// Sector-wide relationship state. Hostile is the implicit default, so only Neutral and Allied
    /// entries are materialized or persisted.
    /// </summary>
    public sealed class FactionRelationshipLedger
    {
        private readonly Dictionary<FactionPair, FactionStance> _entries = new();
        private readonly Dictionary<int, Faction> _knownFactions = new();

        public IReadOnlyDictionary<FactionPair, FactionStance> Entries { get; }

        public event EventHandler<FactionRelationshipChangedEventArgs> StanceChanged;

        public IReadOnlyDictionary<int, Faction> KnownFactions =>
            new ReadOnlyDictionary<int, Faction>(_knownFactions);

        public FactionRelationshipLedger()
        {
            Entries = new ReadOnlyDictionary<FactionPair, FactionStance>(_entries);
        }

        public FactionRelationshipLedger(
            IEnumerable<KeyValuePair<FactionPair, FactionStance>> entries)
            : this()
        {
            foreach (KeyValuePair<FactionPair, FactionStance> entry in
                entries ?? Enumerable.Empty<KeyValuePair<FactionPair, FactionStance>>())
            {
                SetEntry(entry.Key, entry.Value);
            }
        }

        public FactionStance GetStance(Faction first, Faction second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            RegisterFaction(first);
            RegisterFaction(second);
            if (first.Id == second.Id) return FactionStance.Allied;
            if (first.HasBehavior(FactionBehavior.UniversallyHostile)
                || second.HasBehavior(FactionBehavior.UniversallyHostile))
            {
                return FactionStance.Hostile;
            }

            return _entries.TryGetValue(new FactionPair(first, second), out FactionStance stance)
                ? stance
                : FactionStance.Hostile;
        }

        public void SetStance(Faction first, Faction second, FactionStance stance)
        {
            ValidateFactions(first, second);
            RegisterFaction(first);
            RegisterFaction(second);
            SetEntry(new FactionPair(first, second), stance, first, second);
        }

        public void RegisterFaction(Faction faction)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            _knownFactions[faction.Id] = faction;
        }

        /// <summary>
        /// Loads one already-validated rules-data pair. Kept public so the save data boundary can
        /// validate and reconstruct the ledger without a second relationship representation.
        /// </summary>
        public void LoadEntry(int lowerFactionId, int higherFactionId, FactionStance stance)
        {
            SetEntry(new FactionPair(lowerFactionId, higherFactionId), stance);
        }

        private void SetEntry(
            FactionPair pair,
            FactionStance stance,
            Faction first = null,
            Faction second = null)
        {
            if (stance is not FactionStance.Hostile
                and not FactionStance.Neutral
                and not FactionStance.Allied)
            {
                throw new ArgumentOutOfRangeException(nameof(stance), stance, "Unknown faction stance.");
            }

            if (stance != FactionStance.Hostile
                && first != null
                && (first.HasBehavior(FactionBehavior.UniversallyHostile)
                    || second?.HasBehavior(FactionBehavior.UniversallyHostile) == true))
            {
                throw new InvalidOperationException(
                    "A universally hostile faction cannot be Neutral or Allied with another faction.");
            }

            FactionStance previous = _entries.TryGetValue(pair, out FactionStance current)
                ? current
                : FactionStance.Hostile;
            if (stance == FactionStance.Hostile)
            {
                _entries.Remove(pair);
            }
            else
            {
                _entries[pair] = stance;
            }

            if (previous != stance)
            {
                StanceChanged?.Invoke(
                    this,
                    new FactionRelationshipChangedEventArgs(pair, previous, stance));
            }
        }

        private static void ValidateFactions(Faction first, Faction second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (first.Id == second.Id)
            {
                throw new ArgumentException("A faction relationship cannot pair a faction with itself.");
            }
        }
    }

    /// <summary>
    /// The intelligence ladder is derived from continuous evidence. It is intentionally not stored
    /// in a save row, which keeps thresholds centralized and makes balance changes explicit.
    /// </summary>
    public enum IntelLevel
    {
        None = 0,
        Rumor = 1,
        Suspected = 2,
        Confirmed = 3,
        Located = 4
    }

    public enum IntelObservationSource
    {
        Scenario = 0,
        PublicActivity = 1,
        ListeningPost = 2,
        PatrolContact = 3,
        Recon = 4,
        BattleContact = 5,
        GovernorInvestigation = 6,
        GovernorParanoia = 7,
        AllyReport = 8,
        Disinformation = 9,
        Decay = 10
    }
}
