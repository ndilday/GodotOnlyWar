using OnlyWar.Models;
using OnlyWar.Helpers;

namespace OnlyWar.Contracts.Application;

/// <summary>
/// The bounded state and capabilities a campaign policy needs while resolving one session.
/// Implementations belong to Application; campaign policies must not select the active session.
/// </summary>
public interface ICampaignSession
{
    GameRulesData Rules { get; }
    Sector Sector { get; }
    Date CurrentDate { get; }
    IRNG Random { get; }
}
