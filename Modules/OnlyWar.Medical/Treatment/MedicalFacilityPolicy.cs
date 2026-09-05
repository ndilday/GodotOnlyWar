using OnlyWar.Contracts.Medical;

namespace OnlyWar.Medical.Treatment;

public static class MedicalFacilityPolicy
{
    public const long MinimumImperialPopulation = 1_000_000;

    public static bool SupportsMajorSurgery(in MedicalFacilityFacts facts) =>
        facts.IsShipFacility
            ? facts.HasSoldierCapacity
            : facts.IsImperialControlled
                && facts.PublicImperialPopulation >= MinimumImperialPopulation
                && facts.WorldType is "Hive" or "Forge" or "Civilised";
}
