using System;

namespace OnlyWar.Application;

/// <summary>
/// The Armory screen's contract (TDD §6.14). Reads are
/// detached <see cref="ArmoryOverview"/> values; every command carries the session token it was
/// read under, so a command from a replaced campaign is refused rather than applied to the new one.
/// </summary>
public interface IArmoryScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    ArmoryOverview QueryArmory();
    ArmoryPrompt DescribeSendToMars(int soldierId);
    ArmoryCommandResult SendToMars(Guid sessionToken, int soldierId);
    ArmoryPrompt DescribePromotion(int soldierId, int templateId);
    ArmoryCommandResult Promote(Guid sessionToken, int soldierId, int templateId);
    ArmoryCommandResult SetMarsReturnDestination(Guid sessionToken, string destinationKey);
}

public sealed class ArmoryScreenApplication : CampaignScreenApplication, IArmoryScreenApplication
{
    private const string StaleSessionMessage = "The campaign changed. Reopen the Armory.";

    private ArmoryScreenContext Screen => Context.Armory;

    public ArmoryScreenApplication(CampaignApplicationContext context) : base(context) { }

    public ArmoryOverview QueryArmory() =>
        Screen?.Query(SessionToken) ?? ArmoryOverview.Empty(SessionToken);

    public ArmoryPrompt DescribeSendToMars(int soldierId) =>
        Screen?.DescribeSendToMars(soldierId)
        ?? ArmoryPrompt.Blocked("Send to Mars", "No campaign is loaded.");

    public ArmoryCommandResult SendToMars(Guid sessionToken, int soldierId) =>
        Run(sessionToken, screen => screen.SendToMars(soldierId));

    public ArmoryPrompt DescribePromotion(int soldierId, int templateId) =>
        Screen?.DescribePromotion(soldierId, templateId)
        ?? ArmoryPrompt.Blocked("Promotion Blocked", "No campaign is loaded.");

    public ArmoryCommandResult Promote(Guid sessionToken, int soldierId, int templateId) =>
        Run(sessionToken, screen => screen.Promote(soldierId, templateId));

    public ArmoryCommandResult SetMarsReturnDestination(Guid sessionToken, string destinationKey) =>
        Run(sessionToken, screen => screen.SetMarsReturnDestination(destinationKey));

    private ArmoryCommandResult Run(Guid sessionToken, Func<ArmoryScreenContext, ArmoryCommandResult> command)
    {
        if (!IsCurrentSession(sessionToken) || Screen == null)
        {
            return new ArmoryCommandResult(false, StaleSessionMessage);
        }
        ArmoryCommandResult result = command(Screen);
        if (result.Succeeded) RecordChange();
        return result;
    }
}
