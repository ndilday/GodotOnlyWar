using OnlyWar.Models.Events;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// A turn/battle-scoped cache of named deterministic streams. The scope owns mutable streams;
    /// reconstructing it for the same campaign, turn, key, and version restarts the sequence.
    /// </summary>
    public sealed class NamedRandomStreamFactory
    {
        private readonly CampaignIdentity _identity;
        private readonly int _turn;
        private readonly Dictionary<(string Key, int Version), Pcg32Rng> _streams = new();

        public NamedRandomStreamFactory(CampaignIdentity identity, int turn)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _turn = turn;
        }

        public Pcg32Rng Get(string streamKey, int streamVersion = 1)
        {
            if (string.IsNullOrWhiteSpace(streamKey))
                throw new ArgumentException("A stream key is required.", nameof(streamKey));
            if (streamVersion <= 0) throw new ArgumentOutOfRangeException(nameof(streamVersion));
            if (!_streams.TryGetValue((streamKey, streamVersion), out Pcg32Rng stream))
            {
                stream = Pcg32Rng.ForStream(_identity, _turn, streamKey, streamVersion);
                _streams.Add((streamKey, streamVersion), stream);
            }
            return stream;
        }

        public Pcg32Rng GetStream(string streamKey, int streamVersion = 1) =>
            Get(streamKey, streamVersion);

        public int Turn => _turn;
        public int Count => _streams.Count;
    }
}
