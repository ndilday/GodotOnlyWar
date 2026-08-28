using OnlyWar.Models;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Pins the ambient campaign state that <c>BattleSquad.MarkEquipmentValueSource</c> reads while it
/// allocates equipment.
///
/// <para>That method decides a soldier's <c>UsesItemizedEquipment</c> flag — and therefore whether
/// <c>EffectiveBattleValue</c> is the itemized equipment value or the intrinsic
/// <c>SoldierTemplate.BattleValue</c> — from <see cref="GameDataSingleton"/>, a process-wide mutable
/// singleton that no test restores. Any suite that publishes a real rules blob together with a
/// Sector (ScenarioTurnTests, GovernanceHierarchyTests, NewGameSaveTests, …) leaves the singleton
/// initialized for the rest of the process, so a legacy WeaponSet fixture that runs afterwards
/// silently switches to itemized battle values: a TestModelFactory marine is worth 6 instead of 2,
/// and every battle-value-denominated assertion moves with it.</para>
///
/// <para>Marking a class <c>[Collection(TestCollections.SharedState)]</c> only removes the
/// concurrency race — it does not undo the poisoning, because ordering inside the collection still
/// decides who runs first. Fixtures that need intrinsic values must clear the campaign themselves,
/// which is why this call belongs in the constructor (xUnit runs it once per test) rather than in a
/// one-time fixture. Callers MUST also join the shared-state collection so the clear cannot land in
/// the middle of a test that is using the singleton.</para>
/// </summary>
internal static class LegacyWeaponSetBattleFixture
{
    public static void UseIntrinsicBattleValues()
    {
        GameDataSingleton.Instance.ClearCampaign();
    }
}
