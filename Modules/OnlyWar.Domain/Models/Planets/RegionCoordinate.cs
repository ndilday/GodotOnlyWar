using System;

namespace OnlyWar.Models.Planets
{
    /// <summary>
    /// A region's position within a planet's diamond-shaped territory grid.
    /// Signed because adjacency math offsets coordinates by +/-1. A value type
    /// with proper value equality, so coordinates compare by value via == and
    /// Equals and hash consistently as dictionary keys.
    /// </summary>
    public readonly struct RegionCoordinate : IEquatable<RegionCoordinate>
    {
        public int X { get; }
        public int Y { get; }

        public RegionCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(RegionCoordinate other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is RegionCoordinate other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public static bool operator ==(RegionCoordinate left, RegionCoordinate right) => left.Equals(right);

        public static bool operator !=(RegionCoordinate left, RegionCoordinate right) => !left.Equals(right);

        public void Deconstruct(out int x, out int y)
        {
            x = X;
            y = Y;
        }

        public override string ToString() => $"({X}, {Y})";
    }
}
