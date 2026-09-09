using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;

namespace OnlyWar.Campaign.Simulation;

/// <summary>
/// Public Campaign boundary for generation-time faction seeding. The detailed capability
/// processor remains internal to Campaign; Generation and Application only receive the two
/// operations needed by the authored opening.
/// </summary>
public static class CampaignGenerationSeeding
{
    public static void SeedGhostSources(Sector sector, GameRulesData rules, IRNG random) =>
        FactionCapabilityCampaignProcessor.SeedGhostSources(sector, rules, random);

    public static StrategicInvasionForce EstablishOpeningInvasion(
        ICampaignSimulationSession session,
        Sector sector,
        Planet planet,
        Faction invasionFaction) =>
        new FactionCapabilityCampaignProcessor(session)
            .EstablishOpeningInvasion(sector, planet, invasionFaction);
}
