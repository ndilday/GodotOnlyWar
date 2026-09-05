using OnlyWar.Helpers;

namespace OnlyWar;

/// <summary>Legacy root façade; Runtime owns the implementation and resources.</summary>
public static class NameGenerator
{
    private static readonly Runtime.Naming.NameGenerator Generator = new();

    internal static int GivenNameCount => Generator.GivenNameCount;
    internal static int SurnameCount => Generator.SurnameCount;
    public static void Reset() => Generator.Reset(StaticRNG.Instance);
    public static string GetFullName() => Generator.GetFullName(StaticRNG.Instance);
}

