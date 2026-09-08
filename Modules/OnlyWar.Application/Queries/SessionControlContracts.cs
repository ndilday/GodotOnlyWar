using System;

namespace OnlyWar.Application;

/// <summary>
/// The campaign's identity and recoverability as the host presents them, without exposing the
/// tracker or the campaign graph itself.
/// </summary>
public sealed record CampaignStatusView(
    bool HasCampaign,
    bool IsDirty,
    string CampaignName);

/// <summary>A save the application performed on the host's behalf.</summary>
public sealed record SaveCampaignResult(
    bool Succeeded,
    string Message,
    string DisplayName = null,
    DateTime? WrittenLocal = null);

public enum SaveCampaignKind
{
    Manual,
    Overwrite,
    ProtectedPreTurn,
    PostTurnAutosave,
    InitialAutosave
}

public sealed record SaveCampaignCommand(
    Guid SessionToken,
    SaveCampaignKind Kind,
    string DisplayName = null,
    string OverwriteFilePath = null);

/// <summary>
/// Session lifetime and turn control as the host consumes it. The host still owns navigation,
/// dialogs and the save file catalog; the application owns what the campaign is, whether it is
/// recoverable, and every write to it.
/// </summary>
public interface ISessionControlApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    /// <summary>Raised when the campaign's recoverable/dirty state changes.</summary>
    event EventHandler CampaignStatusChanged;

    CampaignStatusView QueryStatus();

    /// <summary>Records that the player changed something that is not yet in a save.</summary>
    void MarkChanged();

    /// <summary>
    /// Writes a save through the storage manager composed at startup and, only on success, marks
    /// the campaign recoverable at the revision captured before the write. A failed write leaves
    /// the previous recovery point and the dirty flag exactly as they were.
    /// </summary>
    SaveCampaignResult SaveCampaign(SaveCampaignCommand command);

    /// <summary>
    /// Whether the mandatory recruitment setup still blocks ending the turn. This is a campaign
    /// rule, so the screen asks rather than reading the program itself.
    /// </summary>
    bool RequiresRecruitmentSetup();

    EndTurnPreflightReport QueryEndTurnPreflight(EndTurnWarningPreferences preferences);
}
