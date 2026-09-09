using OnlyWar.Battles.Abstractions;

namespace OnlyWar.Operations.Abstractions;

/// <summary>
/// Neutral mission narration emitted by Operations and consumed by Application report projection.
/// The replay handle remains opaque; only the composition root may resolve it for a review screen.
/// </summary>
public class MissionDebriefLine
{
    public string Text { get; }
    public IBattleReplay BattleHistory { get; }
    public BattleDebriefReport BattleReport { get; }
    public ushort? Day { get; }
    public string SquadName { get; }

    /// <summary>
    /// A loaded report can retain the compact report without the full replay graph. Both are battle
    /// content; only the history-backed form can open a replay.
    /// </summary>
    public bool HasBattle => BattleHistory != null || BattleReport != null;

    public MissionDebriefLine(
        string text,
        IBattleReplay battleHistory = null,
        BattleDebriefReport battleReport = null,
        ushort? day = null,
        string squadName = null)
    {
        Text = text ?? string.Empty;
        BattleHistory = battleHistory;
        BattleReport = battleReport;
        Day = day;
        SquadName = squadName;
    }
}

/// <summary>
/// Why an ambush never got to spring. The full name is retained for source and binary compatibility
/// with the earlier Operations model namespace while ownership now lives in Operations.Abstractions.
/// </summary>
public enum AmbushSpoilStage
{
    NotSpoiled = 0,
    DuringSetup,
    BeforeSpringing
}
