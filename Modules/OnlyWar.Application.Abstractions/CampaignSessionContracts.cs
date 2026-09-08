using OnlyWar.Models;
using OnlyWar.Abstractions;

namespace OnlyWar.Application.Abstractions;

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
