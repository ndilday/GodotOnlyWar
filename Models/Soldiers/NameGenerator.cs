using OnlyWar.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace OnlyWar
{
    /// <summary>
    /// Builds full soldier names from separate embedded given-name and surname pools.
    /// Each pool is shuffled once and then drawn without replacement in O(1) time.
    /// </summary>
    public static class NameGenerator
    {
        private const string GIVEN_NAMES_RESOURCE = "OnlyWar.SoldierNames.Given";
        private const string SURNAMES_RESOURCE = "OnlyWar.SoldierNames.Surnames";

        private static readonly string[] _givenNames = LoadPool(GIVEN_NAMES_RESOURCE);
        private static readonly string[] _surnames = LoadPool(SURNAMES_RESOURCE);
        private static readonly int[] _shuffledGivenNameIndexes = new int[_givenNames.Length];
        private static readonly int[] _shuffledSurnameIndexes = new int[_surnames.Length];

        private static int _remainingGivenNames;
        private static int _remainingSurnames;

        internal static int GivenNameCount => _givenNames.Length;
        internal static int SurnameCount => _surnames.Length;

        /// <summary>
        /// Starts a fresh naming sequence by independently shuffling both pools.
        /// Call this once before generating a player chapter. It should be called
        /// after <see cref="RNG.Reset(int)"/> when seeded determinism is required.
        /// </summary>
        public static void Reset()
        {
            RefillGivenNames();
            RefillSurnames();
        }

        /// <summary>
        /// Returns a two-part name. Given names and surnames do not repeat until their
        /// respective pool is exhausted, at which point only that pool is reshuffled.
        /// </summary>
        public static string GetFullName()
        {
            return $"{TakeGivenName()} {TakeSurname()}";
        }

        private static string TakeGivenName()
        {
            if (_remainingGivenNames == 0)
            {
                RefillGivenNames();
            }

            int nameIndex = _shuffledGivenNameIndexes[--_remainingGivenNames];
            return _givenNames[nameIndex];
        }

        private static string TakeSurname()
        {
            if (_remainingSurnames == 0)
            {
                RefillSurnames();
            }

            int nameIndex = _shuffledSurnameIndexes[--_remainingSurnames];
            return _surnames[nameIndex];
        }

        private static void RefillGivenNames()
        {
            FillAndShuffle(_shuffledGivenNameIndexes);
            _remainingGivenNames = _shuffledGivenNameIndexes.Length;
        }

        private static void RefillSurnames()
        {
            FillAndShuffle(_shuffledSurnameIndexes);
            _remainingSurnames = _shuffledSurnameIndexes.Length;
        }

        private static void FillAndShuffle(int[] indexes)
        {
            for (int i = 0; i < indexes.Length; i++)
            {
                indexes[i] = i;
            }

            for (int i = indexes.Length - 1; i > 0; i--)
            {
                int j = RNG.GetIntBelowMax(0, i + 1);
                (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
            }
        }

        private static string[] LoadPool(string resourceName)
        {
            Assembly assembly = typeof(NameGenerator).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded soldier-name resource '{resourceName}' was not found.");
            using StreamReader reader = new(stream);

            List<string> names = [];
            HashSet<string> uniqueNames = new(StringComparer.OrdinalIgnoreCase);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string name = line.Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                if (!uniqueNames.Add(name))
                {
                    throw new InvalidDataException(
                        $"Soldier-name resource '{resourceName}' contains duplicate name '{name}'.");
                }
                names.Add(name);
            }

            if (names.Count == 0)
            {
                throw new InvalidDataException(
                    $"Soldier-name resource '{resourceName}' contains no names.");
            }

            return names.ToArray();
        }
    }
}
