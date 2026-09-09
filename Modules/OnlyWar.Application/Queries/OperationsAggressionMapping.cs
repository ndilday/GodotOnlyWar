using OnlyWar.Domain.Orders;

namespace OnlyWar.Application;

internal static class OperationsAggressionMapping
{
    internal static OperationsAggression ToView(Aggression value) => value switch
    {
        Aggression.Avoid => OperationsAggression.Avoid,
        Aggression.Cautious => OperationsAggression.Cautious,
        Aggression.Attritional => OperationsAggression.Attritional,
        Aggression.Aggressive => OperationsAggression.Aggressive,
        _ => OperationsAggression.Normal
    };

    internal static Aggression ToDomain(OperationsAggression value) => value switch
    {
        OperationsAggression.Avoid => Aggression.Avoid,
        OperationsAggression.Cautious => Aggression.Cautious,
        OperationsAggression.Attritional => Aggression.Attritional,
        OperationsAggression.Aggressive => Aggression.Aggressive,
        _ => Aggression.Normal
    };
}
