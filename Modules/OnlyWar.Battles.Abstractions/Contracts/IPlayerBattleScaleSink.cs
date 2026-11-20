namespace OnlyWar.Battles.Abstractions
{
    /// <summary>
    /// The size and result of one battle, for the legacy ledger's large-battle source
    /// (TDD §6.15 *Turn step*). StartingStrategicValue is the strategic Battle
    /// Value of every able soldier on both sides when the battle began. PlayerHeldField is null
    /// when no side held the field.
    /// </summary>
    public sealed record BattleScaleSnapshot(long StartingStrategicValue, bool? PlayerHeldField);

    /// <summary>
    /// Optional companion to <see cref="IPlayerBattleAftermathSink"/>. The aftermath policy calls
    /// it, when the sink implements it, before AddToBattleHistory for the same battle.
    /// </summary>
    public interface IPlayerBattleScaleSink
    {
        void RecordBattleScale(BattleScaleSnapshot scale);
    }
}
