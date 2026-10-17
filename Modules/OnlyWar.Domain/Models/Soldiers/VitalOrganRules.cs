namespace OnlyWar.Domain.Soldiers;

/// <summary>
/// The tunables of vital-location replacement (OnlyWar_TDD.md §5.3 and
/// PRD §4.8). Code constants, not rules data. They sit in the Domain, not beside
/// <c>MedicalProcedureRules</c>, because the battle module reads them too and does not reference
/// the Medical module.
/// </summary>
public static class VitalOrganRules
{
    /// <summary>
    /// The chance that a severed location marked
    /// <see cref="HitLocationTemplate.SeveredMayNeedOrganReplacement"/> (the Vitals) is not fatal.
    /// </summary>
    public const double SeveredSurvivalChance = 0.25;

    /// <summary>
    /// The chance that a location marked
    /// <see cref="HitLocationTemplate.CrippledMayNeedOrganReplacement"/> (the Torso), crippled in
    /// battle, does not heal on its own.
    /// </summary>
    public const double CrippledReplacementChance = 0.25;

    /// <summary>The share of penetrating hits to a location that strike each augmetic organ.</summary>
    public const double HitChancePerAugmeticOrgan = 0.20;

    /// <summary>The most augmetic organs one location can hold.</summary>
    public const int MaximumAugmeticOrgansPerLocation = 3;

    /// <summary>
    /// The chance that a penetrating hit to a location with <paramref name="organCount"/> augmetic
    /// organs strikes one of them: p(n) = n x the per-organ chance.
    /// </summary>
    public static double GetAugmeticOrganHitChance(int organCount) =>
        System.Math.Clamp(organCount, 0, MaximumAugmeticOrgansPerLocation)
            * HitChancePerAugmeticOrgan;

    /// <summary>
    /// The locations that can hold augmetic organs: those the rules data marks with either
    /// organ-replacement flag (<see cref="HitLocationTemplate.SeveredMayNeedOrganReplacement"/>,
    /// <see cref="HitLocationTemplate.CrippledMayNeedOrganReplacement"/>).
    /// </summary>
    public static bool CanHoldAugmeticOrgans(HitLocationTemplate template) =>
        template != null
        && (template.SeveredMayNeedOrganReplacement || template.CrippledMayNeedOrganReplacement);
}
