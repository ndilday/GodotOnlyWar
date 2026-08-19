namespace OnlyWar.Helpers.Database.GameState
{
    internal static class SaveFormat
    {
        // Bump this whenever SaveStructure.sql changes shape. The writer always recreates the
        // schema from scratch, so a save/load round-trip inside one build passes regardless -- but
        // an OLDER save read by a NEWER build will be missing the new tables and fault deep in the
        // loader. EnsureCompatibleSaveVersion exists to turn that into a clean, early rejection,
        // and it can only do so if this constant moves with the schema.
        //
        // 6: OrderSoldier table (order-level specialist attachment,
        //    Design/Reference/SpecialistAttachment.md). Added 2026-08-07 after a v5 save crashed with
        //    "no such table: OrderSoldier" instead of being reported as incompatible.
        // 7: LastTurnReport table (bounded JSON snapshot of the latest resolved turn report).
        // 8: canonical CampaignEvent/ChapterChronicle tables and persisted campaign RNG identity.
        // 9: faction relationships, authored behavior flags, regional-awareness rename, and
        //    target-specific intelligence beliefs. This is a deliberate save break: v8 is rejected.
        // 10: itemized equipment role/personal loadout tables and mission equipment foundations.
        //     This is a deliberate save break: v9 and older saves are rejected.
        // 11: persisted world-control narrative episode state.
        internal const int CurrentVersion = 11;
        internal const int FirstMigratableVersion = 11;
    }
}
