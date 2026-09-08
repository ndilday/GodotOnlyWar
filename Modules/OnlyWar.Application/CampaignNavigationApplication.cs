namespace OnlyWar.Application;

public sealed class CampaignNavigationApplication : CampaignScreenApplication,
    ICampaignNavigationApplication
{
    private CampaignNavigationContext Navigation => Context.Navigation;

    public CampaignNavigationApplication(CampaignApplicationContext context) : base(context) { }

    public CampaignNavigationRoute ResolveNavigation(
        CampaignNavigationTargetKind kind, int? primaryId)
        => Navigation?.ResolveNavigation(kind, primaryId)
            ?? CampaignNavigationRoute.None;

    public CampaignNavigationRoute ResolveSquadLocation(int squadId)
        => Navigation?.ResolveSquadLocation(squadId)
            ?? CampaignNavigationRoute.None;

    public int? QueryRegionPlanet(int regionId) =>
        Navigation?.QueryRegionPlanet(regionId);

    public string QueryPlanetName(int planetId) =>
        Navigation?.QueryPlanetName(planetId);
}
