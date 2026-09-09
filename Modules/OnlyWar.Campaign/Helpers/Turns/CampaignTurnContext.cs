using System;
using OnlyWar.Abstractions;
using OnlyWar.Application.Abstractions;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;

namespace OnlyWar.Campaign.Turns;

/// <summary>
/// The stable campaign inputs shared by turn processors.
/// </summary>
/// <remarks>
/// This is deliberately composed from the values processors need. It prevents
/// turn features from depending on the live <see cref="GameSession"/> graph
/// while leaving session ownership with the application aggregate boundary.
/// </remarks>
internal sealed class CampaignTurnContext
{
    internal Sector Sector { get; }

    internal GameRulesData Rules { get; }

    internal Date CurrentDate { get; }

    internal IRNG Random { get; }

    internal IPersistentIdAllocator Identity { get; }

    internal CampaignTurnContext(
        Sector sector,
        GameRulesData rules,
        Date currentDate,
        IRNG random,
        IPersistentIdAllocator identity)
    {
        Sector = sector ?? throw new ArgumentNullException(nameof(sector));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    internal static CampaignTurnContext From(ICampaignSimulationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new CampaignTurnContext(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            session.Random,
            session.Identity);
    }
}
