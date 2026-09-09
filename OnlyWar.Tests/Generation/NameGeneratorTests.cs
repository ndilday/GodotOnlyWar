using System;
using System.Collections.Generic;
using OnlyWar.Domain;
using OnlyWar.Abstractions;
using OnlyWar.Runtime.Random;
using Xunit;
using RuntimeNameGenerator = OnlyWar.Runtime.Naming.NameGenerator;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class NameGeneratorTests
{
    [Fact]
    public void FoundingChapterDrawHasNoRepeatedGivenNamesOrSurnames()
    {
        const int foundingSize = 1000;
        RuntimeNameGenerator generator = new();
        IRNG random = new SeededRNG(20260728);
        generator.Reset(random);

        HashSet<string> givenNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> surnames = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < foundingSize; i++)
        {
            string[] parts = generator.GetFullName(random).Split(' ');

            Assert.Equal(2, parts.Length);
            Assert.True(givenNames.Add(parts[0]), $"Given name repeated: {parts[0]}");
            Assert.True(surnames.Add(parts[1]), $"Surname repeated: {parts[1]}");
        }
    }

    [Fact]
    public void ResetAfterRngResetReproducesTheSameSequence()
    {
        RuntimeNameGenerator generator = new();
        IRNG firstRandom = new SeededRNG(8675309);
        generator.Reset(firstRandom);
        string[] firstSequence = GenerateNames(generator, firstRandom, 25);

        IRNG secondRandom = new SeededRNG(8675309);
        generator.Reset(secondRandom);
        string[] secondSequence = GenerateNames(generator, secondRandom, 25);

        Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public void ExhaustedGivenNamePoolReshufflesWithoutFailing()
    {
        RuntimeNameGenerator generator = new();
        IRNG random = new SeededRNG(42);
        generator.Reset(random);

        string lastName = null;
        for (int i = 0; i <= generator.GivenNameCount; i++)
        {
            lastName = generator.GetFullName(random);
        }

        Assert.False(string.IsNullOrWhiteSpace(lastName));
    }

    private static string[] GenerateNames(
        RuntimeNameGenerator generator,
        IRNG random,
        int count)
    {
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = generator.GetFullName(random);
        }
        return names;
    }
}
