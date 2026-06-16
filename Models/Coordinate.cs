using System;

namespace OnlyWar.Models
{
    /// <summary>
    /// An (X, Y) grid position. A value type with proper value equality, so two
    /// coordinates with the same components compare equal via == and Equals and
    /// hash consistently when used as dictionary keys.
    /// </summary>
    public readonly struct Coordinate : IEquatable<Coordinate>
    {
        public ushort X { get; }
        public ushort Y { get; }

        public Coordinate(ushort x, ushort y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Coordinate other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is Coordinate other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public static bool operator ==(Coordinate left, Coordinate right) => left.Equals(right);

        public static bool operator !=(Coordinate left, Coordinate right) => !left.Equals(right);

        public void Deconstruct(out ushort x, out ushort y)
        {
            x = X;
            y = Y;
        }

        public override string ToString() => $"({X}, {Y})";
    }
}
