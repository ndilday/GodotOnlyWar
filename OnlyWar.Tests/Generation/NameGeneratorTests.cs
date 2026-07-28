using System;
using System.Collections.Generic;
using OnlyWar.Helpers;
using Xunit;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class NameGeneratorTests
{
    [Fact]
    public void EmbeddedPoolsMeetRequiredSizes()
    {
        Assert.True(NameGenerator.GivenNameCount >= 1000);
        Assert.True(NameGenerator.SurnameCount >= 2000);
    }

    [Fact]
    public void FoundingChapterDrawHasNoRepeatedGivenNamesOrSurnames()
    {
        const int foundingSize = 1000;
        RNG.Reset(20260728);
        NameGenerator.Reset();

        HashSet<string> givenNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> surnames = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < foundingSize; i++)
        {
            string[] parts = NameGenerator.GetFullName().Split(' ');

            Assert.Equal(2, parts.Length);
            Assert.True(givenNames.Add(parts[0]), $"Given name repeated: {parts[0]}");
            Assert.True(surnames.Add(parts[1]), $"Surname repeated: {parts[1]}");
        }
    }

    [Fact]
    public void ResetAfterRngResetReproducesTheSameSequence()
    {
        RNG.Reset(8675309);
        NameGenerator.Reset();
        string[] firstSequence = GenerateNames(25);

        RNG.Reset(8675309);
        NameGenerator.Reset();
        string[] secondSequence = GenerateNames(25);

        Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public void ExhaustedGivenNamePoolReshufflesWithoutFailing()
    {
        RNG.Reset(42);
        NameGenerator.Reset();

        string lastName = null;
        for (int i = 0; i <= NameGenerator.GivenNameCount; i++)
        {
            lastName = NameGenerator.GetFullName();
        }

        Assert.False(string.IsNullOrWhiteSpace(lastName));
    }

    private static string[] GenerateNames(int count)
    {
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = NameGenerator.GetFullName();
        }
        return names;
    }
}
