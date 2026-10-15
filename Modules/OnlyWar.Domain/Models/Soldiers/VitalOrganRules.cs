namespace OnlyWar.Domain.Soldiers;

/// <summary>
/// The tunables of vital-location replacement (Design/Active/VitalLocationReplacement.md D2, D3,
/// D7a). Code constants, not rules data. They sit in the Domain, not beside
/// <c>MedicalProcedureRules</c>, because the battle module reads them too and does not reference
/// the Medical module.
/// </summary>
public static class VitalOrganRules
{
    /// <summary>D2: the chance that a severed Vitals is not fatal.</summary>
    public const double SeveredVitalsSurvivalChance = 0.25;

    /// <summary>D3: the chance that a Torso crippled in battle does not heal on its own.</summary>
    public const double CrippledTorsoReplacementChance = 0.25;

    /// <summary>D7a: the share of penetrating hits to a location that strike each augmetic organ.</summary>
    public const double HitChancePerAugmeticOrgan = 0.20;

    /// <summary>D7a: the most augmetic organs one location can hold.</summary>
    public const int MaximumAugmeticOrgansPerLocation = 3;

    /// <summary>The rules-data id of the vital-organ augmetic tier (the CyberneticOption table).</summary>
    public const int VitalOrganCyberneticOptionId = 2;

    private const string VitalsName = "Vitals";
    private const string TorsoName = "Torso";

    /// <summary>
    /// The chance that a penetrating hit to a location with <paramref name="organCount"/> augmetic
    /// organs strikes one of them: p(n) = n x the per-organ chance.
    /// </summary>
    public static double GetAugmeticOrganHitChance(int organCount) =>
        System.Math.Clamp(organCount, 0, MaximumAugmeticOrgansPerLocation)
            * HitChancePerAugmeticOrgan;

    // Identified by name: Brain and Vitals share every anatomical value, so nothing else tells
    // them apart. Every body in the rules data names its locations this way.
    public static bool IsVitals(HitLocationTemplate template) =>
        template?.IsVital == true && template.Name == VitalsName;

    public static bool IsTorso(HitLocationTemplate template) =>
        template?.IsVital == true && template.Name == TorsoName;

    /// <summary>The locations that can hold augmetic organs: the Torso and the Vitals.</summary>
    public static bool CanHoldAugmeticOrgans(HitLocationTemplate template) =>
        IsVitals(template) || IsTorso(template);
}
