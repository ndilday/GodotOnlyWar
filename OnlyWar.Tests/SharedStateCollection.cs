using Xunit;

namespace OnlyWar.Tests;

public static class TestCollections
{
    public const string SharedState = "Shared state";
}

[CollectionDefinition(TestCollections.SharedState, DisableParallelization = true)]
public sealed class SharedStateCollection
{
}
