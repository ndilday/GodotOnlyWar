using OnlyWar.Models.Events;
using System;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Project-owned PCG-XSH-RR 32 stream. The initialization sequence and multiplier are part of
    /// algorithm version 1; this class deliberately does not share state with SeededRNG or RNG.
    /// </summary>
    public sealed class Pcg32Rng : IRNG
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private ulong _state;
        private readonly ulong _increment;
        private bool _hasGaussianSpare;
        private double _gaussianSpare;

        public Pcg32Rng(ulong initialState, ulong streamSelector)
        {
            _state = 0;
            _increment = (streamSelector << 1) | 1UL;
            NextUInt();
            _state += initialState;
            NextUInt();
        }

        public static Pcg32Rng ForStream(
            CampaignIdentity identity,
            int turn,
            string streamKey,
            int streamVersion = 1)
        {
            (ulong initialState, ulong streamSelector) = StableDerivation.DeriveState(
                identity, turn, streamKey, streamVersion);
            return new Pcg32Rng(initialState, streamSelector);
        }

        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + _increment);
            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotation = (int)(oldState >> 59);
            return (xorshifted >> rotation)
                | (xorshifted << ((-rotation) & 31));
        }

        public double GetDoubleInRange(double lowerBound, double upperBound)
        {
            if (upperBound < lowerBound) throw new ArgumentOutOfRangeException(nameof(upperBound));
            return lowerBound + GetLinearDouble() * (upperBound - lowerBound);
        }

        public double GetLinearDouble() => NextUInt() / 4294967296.0;

        public int GetIntBelowMax(int min, int max)
        {
            if (max <= min) throw new ArgumentOutOfRangeException(nameof(max));
            uint range = (uint)((long)max - min);
            uint threshold = unchecked(0u - range) % range;
            uint sample;
            do
            {
                sample = NextUInt();
            }
            while (sample < threshold);
            long value = (long)min + (sample % range);
            return (int)value;
        }

        public double NextRandomZValue()
        {
            if (_hasGaussianSpare)
            {
                _hasGaussianSpare = false;
                return _gaussianSpare;
            }

            double u1 = 1.0 - GetLinearDouble();
            double u2 = 1.0 - GetLinearDouble();
            double radius = Math.Sqrt(-2.0 * Math.Log(u1));
            double angle = 2.0 * Math.PI * u2;
            _gaussianSpare = radius * Math.Cos(angle);
            _hasGaussianSpare = true;
            return radius * Math.Sin(angle);
        }
    }
}
