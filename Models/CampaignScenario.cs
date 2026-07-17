namespace OnlyWar.Models
{
    // Which framed opening a campaign was started with. None covers a future
    // plain-sandbox mode; when Sector.Scenario is null the game behaves as it did
    // before the Opening Scenario work (Design/OpeningScenario.md §2.1).
    public enum ScenarioType { None = 0, PromisedWorld = 1 }

    // Live state of the scenario objective. Resolved by the (later-session) ProcessScenario
    // turn-loop step; Pending until the promised world is liberated (Won) or overrun (Lapsed).
    public enum ObjectiveState { Pending = 0, Won = 1, Lapsed = 2 }

    // Persistent objective state carried through the campaign and across save/load
    // (Design/OpeningScenario.md §2.1). Attached to Sector as a nullable property and
    // persisted on the extended GlobalData row (§7). BriefingText is composed once at
    // generation and stored so the popup, Chapter screen, and any recap read the same
    // authored text. The mechanically-relevant authority is always the *current* Sector
    // Lord (resolved on demand via Sector.GetSectorLord), so only the original promiser's
    // id is stored here, for narrative continuity.
    public class CampaignScenario
    {
        public ScenarioType Type { get; }
        public int PromisedPlanetId { get; }
        public ObjectiveState State { get; set; }
        // One-shot guard for the briefing popup. Persisted, so the popup never shows twice
        // and never on load without a separate "is new game" flag (§5).
        public bool BriefingAcknowledged { get; set; }
        public string BriefingText { get; }
        // The Sector Lord who first promised the world (flavor/continuity only; may dangle
        // harmlessly if that character is later removed — §2.1).
        public int OriginalAuthorityCharacterId { get; }

        public CampaignScenario(ScenarioType type, int promisedPlanetId, string briefingText,
                                int originalAuthorityCharacterId,
                                ObjectiveState state = ObjectiveState.Pending,
                                bool briefingAcknowledged = false)
        {
            Type = type;
            PromisedPlanetId = promisedPlanetId;
            BriefingText = briefingText;
            OriginalAuthorityCharacterId = originalAuthorityCharacterId;
            State = state;
            BriefingAcknowledged = briefingAcknowledged;
        }
    }
}
