using OnlyWar.Domain.Fleets;

namespace OnlyWar.Application;

public sealed class SystemInspectorApplication : CampaignScreenApplication,
    ISystemInspectorApplication
{
    private readonly IOperationsScreenQueries _operationsQueries;
    private readonly IFleetScreenApplication _fleet;

    public SystemInspectorApplication(
        CampaignApplicationContext context,
        IOperationsScreenQueries operationsQueries,
        IFleetScreenApplication fleet) : base(context)
    {
        _operationsQueries = operationsQueries
            ?? throw new System.ArgumentNullException(nameof(operationsQueries));
        _fleet = fleet ?? throw new System.ArgumentNullException(nameof(fleet));
    }

    private SystemInspectorContext Screen =>
        Context.CreateSystemInspectorContext(_operationsQueries, _fleet);

    public SystemInspectorView QuerySystemInspector(
        int? planetId, int? selectedFleetId, bool includeDossier) =>
        Screen?.QuerySystemInspector(planetId, selectedFleetId, includeDossier)
        ?? SystemInspectorView.Empty;

    public int? QueryFleetContextPlanet(int fleetId) =>
        Screen?.QueryFleetContextPlanet(fleetId);
}
