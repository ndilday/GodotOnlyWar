using System;

namespace OnlyWar.Application;

/// <summary>
/// Mandates, legacy and Chapter Master succession (TDD §6.15). Reads are
/// detached views; every command carries the session token it was read under, so a command from a
/// replaced campaign is refused rather than applied to the new one.
/// </summary>
public interface IMandateScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    MandateBoardView QueryMandates();
    MandateCommandResult SwearMandate(Guid sessionToken, string optionKey);
    MandateCommandResult AbandonMandate(Guid sessionToken);
    MandateCommandResult RenounceChapterWorld(Guid sessionToken);

    ChapterMasterSuccessionView QueryChapterMasterSuccession();
    ChapterPrompt DescribeChapterMasterPromotion(int soldierId);
    MandateCommandResult PromoteToChapterMaster(Guid sessionToken, int soldierId);
}

public sealed class MandateScreenApplication : CampaignScreenApplication, IMandateScreenApplication
{
    private const string StaleSessionMessage = "The campaign changed. Reopen the screen.";

    private MandateScreenContext Screen => Context.Mandates;

    public MandateScreenApplication(CampaignApplicationContext context) : base(context) { }

    public MandateBoardView QueryMandates() => Screen?.QueryBoard() ?? MandateBoardView.Empty;

    public MandateCommandResult SwearMandate(Guid sessionToken, string optionKey) =>
        Run(sessionToken, screen => screen.Swear(optionKey));

    public MandateCommandResult AbandonMandate(Guid sessionToken) =>
        Run(sessionToken, screen => screen.Abandon());

    public MandateCommandResult RenounceChapterWorld(Guid sessionToken) =>
        Run(sessionToken, screen => screen.RenounceChapterWorld());

    public ChapterMasterSuccessionView QueryChapterMasterSuccession() =>
        Screen?.QuerySuccession()
        ?? new ChapterMasterSuccessionView(false, null, [], "No campaign is loaded.");

    public ChapterPrompt DescribeChapterMasterPromotion(int soldierId) =>
        Screen?.DescribeChapterMasterPromotion(soldierId)
        ?? new ChapterPrompt(ChapterPromptKind.Blocked, "Promotion Blocked", "No campaign is loaded.");

    public MandateCommandResult PromoteToChapterMaster(Guid sessionToken, int soldierId) =>
        Run(sessionToken, screen => screen.PromoteToChapterMaster(soldierId));

    private MandateCommandResult Run(Guid sessionToken, Func<MandateScreenContext, MandateCommandResult> command)
    {
        if (!IsCurrentSession(sessionToken) || Screen == null)
        {
            return new MandateCommandResult(false, StaleSessionMessage);
        }
        MandateCommandResult result = command(Screen);
        if (result.Succeeded) RecordChange();
        return result;
    }
}
