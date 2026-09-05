using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OnlyWar.Helpers;

namespace OnlyWar.Runtime.Naming;

/// <summary>
/// Runtime-owned soldier naming.  The RNG is supplied by the caller, while the resource pools are
/// owned by this assembly so generation does not depend on the Godot host or Engine.
/// </summary>
public sealed class NameGenerator
{
    public const string GivenNamesResource = "OnlyWar.SoldierNames.Given";
    public const string SurnamesResource = "OnlyWar.SoldierNames.Surnames";

    private readonly string[] _givenNames;
    private readonly string[] _surnames;
    private readonly int[] _givenIndexes;
    private readonly int[] _surnameIndexes;
    private int _remainingGiven;
    private int _remainingSurname;

    public NameGenerator()
    {
        _givenNames = LoadPool(GivenNamesResource);
        _surnames = LoadPool(SurnamesResource);
        _givenIndexes = new int[_givenNames.Length];
        _surnameIndexes = new int[_surnames.Length];
    }

    public int GivenNameCount => _givenNames.Length;
    public int SurnameCount => _surnames.Length;

    public void Reset(IRNG random)
    {
        ArgumentNullException.ThrowIfNull(random);
        FillAndShuffle(_givenIndexes, random);
        FillAndShuffle(_surnameIndexes, random);
        _remainingGiven = _givenIndexes.Length;
        _remainingSurname = _surnameIndexes.Length;
    }

    public string GetFullName(IRNG random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (_remainingGiven == 0) RefillGiven(random);
        if (_remainingSurname == 0) RefillSurname(random);
        return $"{_givenNames[_givenIndexes[--_remainingGiven]]} "
            + _surnames[_surnameIndexes[--_remainingSurname]];
    }

    private void RefillGiven(IRNG random)
    {
        FillAndShuffle(_givenIndexes, random);
        _remainingGiven = _givenIndexes.Length;
    }

    private void RefillSurname(IRNG random)
    {
        FillAndShuffle(_surnameIndexes, random);
        _remainingSurname = _surnameIndexes.Length;
    }

    private static void FillAndShuffle(int[] indexes, IRNG random)
    {
        for (int i = 0; i < indexes.Length; i++) indexes[i] = i;
        for (int i = indexes.Length - 1; i > 0; i--)
        {
            int j = random.GetIntBelowMax(0, i + 1);
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
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            string name = line.Trim();
            if (name.Length > 0 && unique.Add(name)) names.Add(name);
        }
        if (names.Count == 0)
            throw new InvalidDataException($"Embedded soldier-name resource '{resourceName}' is empty.");
        return names.ToArray();
    }
}

