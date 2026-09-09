using OnlyWar.Domain;
using OnlyWar.Abstractions;

namespace OnlyWar.Application.Abstractions;

/// <summary>
/// The common identity capability shared by a live campaign session.
///
/// Feature and simulation code should depend on its own narrower context. The full simulation
/// aggregate is available only through <see cref="ICampaignSimulationSession"/> for the legacy
/// turn/policy pipeline that still needs it.
/// </summary>
public interface ICampaignSession
{
    IPersistentIdAllocator Identity { get; }
}

/// <summary>
/// The explicit live state required by the campaign simulation pipeline. This is deliberately
/// separate from <see cref="ICampaignSession"/> so a feature boundary cannot accidentally acquire
/// rules, world graph and randomness by depending on the common session seam.
/// Implementations belong to Application; campaign policies must not select the active session.
/// </summary>
public interface ICampaignSimulationSession : ICampaignSession
{
    GameRulesData Rules { get; }
    Sector Sector { get; }
    Date CurrentDate { get; }
    IRNG Random { get; }
}
