using System;
using System.Collections.Generic;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Application;

public sealed record TrainingCommandResult(bool Succeeded, string Message = null)
{
    public static TrainingCommandResult Ok() => new(true);
    public static TrainingCommandResult Failed(string message) => new(false, message);
}

/// <summary>
/// The 10th Company screen. The staged doctrine is a plain draft the screen holds; every
/// recruitment fact, forecast, validation message and write lives here.
/// </summary>
public interface ITrainingScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    /// <summary>The doctrine currently recorded in the program, or null when locked.</summary>
    RecruitmentDoctrineDraft QueryDoctrineDraft();

    bool IsRecruitmentSetupComplete { get; }

    /// <summary>
    /// The whole screen for the supplied staged doctrine. A null draft means "use the program's
    /// own values". Synchronizing the 10th Company HQ staff is part of building this view.
    /// </summary>
    RecruitmentScreenSnapshot QueryRecruitmentScreen(
        RecruitmentDoctrineDraft draft, int? selectedSquadId);

    /// <summary>Just the forecast for a staged doctrine, for the live preview.</summary>
    RecruitmentForecastView PreviewForecast(RecruitmentDoctrineDraft draft);

    IReadOnlyList<ScoutSquadRow> QueryScoutSquads(int? selectedSquadId);

    TrainingCommandResult ConfirmDoctrine(Guid sessionToken, RecruitmentDoctrineDraft draft);

    TrainingCommandResult SetScoutTrainingOption(
        Guid sessionToken, int squadId, string optionKey);
}

/// <summary>
/// Token and lifecycle adapter for the 10th Company screen. Recruitment facts, projections and
/// writes stay in the focused TrainingScreenContext.
/// </summary>
public sealed class TrainingScreenApplication : CampaignScreenApplication,
    ITrainingScreenApplication
{
    private const string NoCampaignMessage = "No campaign is active.";
    private const string StaleSessionMessage = "This campaign is no longer active.";
    private const string RecruitmentLockedMessage =
        "The Chapter has no Home World. The 10th Company will establish its "
        + "recruitment program when the Promised World is liberated.";

    private TrainingScreenContext Screen => Context.TrainingScreen;

    public TrainingScreenApplication(CampaignApplicationContext context) : base(context) { }

    public bool IsRecruitmentSetupComplete =>
        Screen?.IsRecruitmentSetupComplete == true;

    public RecruitmentDoctrineDraft QueryDoctrineDraft() =>
        Screen?.QueryDoctrineDraft();

    public RecruitmentScreenSnapshot QueryRecruitmentScreen(
        RecruitmentDoctrineDraft draft, int? selectedSquadId) =>
        Screen?.QueryRecruitmentScreen(draft, selectedSquadId)
        ?? new RecruitmentScreenSnapshot
        {
            IsUnlocked = false,
            LockedMessage = RecruitmentLockedMessage,
            ScoutSquads = []
        };

    public RecruitmentForecastView PreviewForecast(RecruitmentDoctrineDraft draft) =>
        Screen?.PreviewForecast(draft);

    public IReadOnlyList<ScoutSquadRow> QueryScoutSquads(int? selectedSquadId) =>
        Screen?.QueryScoutSquads(selectedSquadId) ?? [];

    public TrainingCommandResult ConfirmDoctrine(
        Guid sessionToken, RecruitmentDoctrineDraft draft)
    {
        if (RejectTrainingCommand(sessionToken) is TrainingCommandResult rejection)
            return rejection;
        return Screen.ConfirmDoctrine(draft);
    }

    public TrainingCommandResult SetScoutTrainingOption(
        Guid sessionToken, int squadId, string optionKey)
    {
        if (RejectTrainingCommand(sessionToken) is TrainingCommandResult rejection)
            return rejection;
        return Screen.SetScoutTrainingOption(squadId, optionKey);
    }

    private TrainingCommandResult RejectTrainingCommand(Guid sessionToken)
    {
        if (Screen == null) return TrainingCommandResult.Failed(NoCampaignMessage);
        if (!IsCurrentSession(sessionToken)) return TrainingCommandResult.Failed(StaleSessionMessage);
        return null;
    }
}
