using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Application;

internal static class MedicalProcedureChoiceMapping
{
    internal static MedicalProcedureChoice ToChoice(MedicalProcedureType value) => value switch
    {
        MedicalProcedureType.Cybernetic => MedicalProcedureChoice.Cybernetic,
        MedicalProcedureType.VatGrown => MedicalProcedureChoice.VatGrown,
        _ => throw new System.ArgumentOutOfRangeException(nameof(value), value, null)
    };

    internal static MedicalProcedureType? ToDomain(MedicalProcedureChoice? value) => value switch
    {
        MedicalProcedureChoice.Cybernetic => MedicalProcedureType.Cybernetic,
        MedicalProcedureChoice.VatGrown => MedicalProcedureType.VatGrown,
        _ => null
    };

    internal static MedicalWoundLevel ToView(WoundLevel value) => value switch
    {
        WoundLevel.Negligible => MedicalWoundLevel.Negligible,
        WoundLevel.Minor => MedicalWoundLevel.Minor,
        WoundLevel.Moderate => MedicalWoundLevel.Moderate,
        WoundLevel.Major => MedicalWoundLevel.Major,
        WoundLevel.Critical => MedicalWoundLevel.Critical,
        WoundLevel.Massive => MedicalWoundLevel.Massive,
        WoundLevel.Mortal => MedicalWoundLevel.Mortal,
        WoundLevel.Unsurvivable => MedicalWoundLevel.Unsurvivable,
        _ => MedicalWoundLevel.None
    };
}
