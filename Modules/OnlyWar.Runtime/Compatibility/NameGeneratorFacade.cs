using OnlyWar.Runtime.Random;

namespace OnlyWar;

/// <summary>
/// Compatibility façade for the historical root API. New code should use
/// <see cref="Runtime.Naming.NameGenerator"/> directly.
/// </summary>
public static class NameGenerator
{
    private static readonly Runtime.Naming.NameGenerator Generator = new();

    internal static int GivenNameCount => Generator.GivenNameCount;
    internal static int SurnameCount => Generator.SurnameCount;
    public static void Reset() => Generator.Reset(new StaticRNG());
    public static string GetFullName() => Generator.GetFullName(new StaticRNG());
}
