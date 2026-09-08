using System.Collections.Generic;

namespace OnlyWar.Application;

public sealed class SectorMapApplication : CampaignScreenApplication, ISectorMapApplication
{
    private SectorMapContext Screen => Context.SectorMap;

    public SectorMapApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasCampaign => Screen != null;

    public bool TryQuerySectorGrid(out int width, out int height)
    {
        if (Screen == null)
        {
            width = 0;
            height = 0;
            return false;
        }

        return Screen.TryQuerySectorGrid(out width, out height);
    }

    public SectorMapGeometryView QuerySectorMapGeometry(bool useVoronoiBorders) =>
        Screen?.QuerySectorMapGeometry(useVoronoiBorders) ?? SectorMapGeometryView.Empty;

    public IReadOnlyList<SectorMapFleetMarker> QuerySectorMapFleets() =>
        Screen?.QuerySectorMapFleets() ?? [];

    public IReadOnlyList<SectorMapPlanetLabelFacts> QuerySectorMapPlanetLabels() =>
        Screen?.QuerySectorMapPlanetLabels() ?? [];

    public SectorMapSelectionView QuerySectorMapSelection(int planetId) =>
        Screen?.QuerySectorMapSelection(planetId) ?? SectorMapSelectionView.Missing;
}
