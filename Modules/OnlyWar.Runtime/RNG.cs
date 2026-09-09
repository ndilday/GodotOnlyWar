using System;

namespace OnlyWar.Runtime.Random
{
    public static class RNG
    {
        private static System.Random _random = new System.Random();

        public static void Reset(int seed)
        {
            _random = new System.Random(seed);
        }
        public static double NextRandomZValue()
        {
            double u1 = 1.0 - _random.NextDouble(); //uniform(0,1] random doubles
            double u2 = 1.0 - _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
        }

        public static int GetIntBelowMax(int min, int max)
        {
            return _random.Next(min, max);
        }
        
        public static double GetLinearDouble()
        {
            return _random.NextDouble();
        }

        public static double GetDoubleInRange(double lowerBound, double upperBound)
        {
            return _random.NextDouble() * (upperBound - lowerBound) + lowerBound;
        }
    }
}
