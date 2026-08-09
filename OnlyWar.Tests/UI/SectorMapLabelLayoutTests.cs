using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace OnlyWar.Tests.UI;

public class SectorMapLabelLayoutTests
{
    [Fact]
    public void Place_UsesBelowBeforeOtherAnchorOffsets()
    {
        var placements = SectorMapLabelLayout.Place(
        [
            new SectorMapLabelCandidate(7, new Vector2(50, 50), 10, new Vector2(20, 10))
        ],
        new SectorMapLabelBounds(0, 0, 100, 100),
        gap: 2);

        var placement = Assert.Single(placements);
        Assert.Equal(new Vector2(40, 52), placement.Position);
    }

    [Fact]
    public void Place_IsPriorityOrderedAndUsesPlanetIdAsStableTiebreak()
    {
        var placements = SectorMapLabelLayout.Place(
        [
            new SectorMapLabelCandidate(20, new Vector2(30, 30), 5, new Vector2(20, 10)),
            new SectorMapLabelCandidate(10, new Vector2(30, 30), 5, new Vector2(20, 10))
        ],
        new SectorMapLabelBounds(0, 0, 100, 100));

        Assert.Equal(2, placements.Count);
        Assert.Equal(10, placements[0].Id);
        Assert.Equal(20, placements[1].Id);
    }

    [Theory]
    [InlineData(0.33, SectorMapLabelBand.A)]
    [InlineData(1.1, SectorMapLabelBand.B)]
    [InlineData(3.5, SectorMapLabelBand.C)]
    [InlineData(10.0, SectorMapLabelBand.C)]
    public void SelectBand_UsesTheSpecifiedZoomBoundaries(float zoom, SectorMapLabelBand expected)
    {
        Assert.Equal(expected, SectorMapLabelLayout.SelectBand(zoom));
    }

    [Fact]
    public void ClampExtentToWidth_PreservesAspectRatio()
    {
        Assert.Equal(
            new Vector2(100, 20),
            SectorMapLabelLayout.ClampExtentToWidth(new Vector2(250, 50), 100));
    }

    [Fact]
    public void Place_DropsLabelThatCannotFitInsideItsAllowedRegion()
    {
        IReadOnlyList<IReadOnlyList<Vector2>> regions =
        [
            new Vector2[]
            {
                new(0, 0), new(100, 0), new(100, 100), new(0, 100)
            }
        ];

        var placements = SectorMapLabelLayout.Place(
        [
            new SectorMapLabelCandidate(
                1,
                new Vector2(50, 50),
                1,
                new Vector2(120, 10),
                AllowedRegions: regions)
        ],
        new SectorMapLabelBounds(0, 0, 200, 200));

        Assert.Empty(placements);
    }

    [Fact]
    public void OrderPlanetPriorities_UsesRequestsThenSeatsThenImportanceThenId()
    {
        var ordered = SectorMapLabelLayout.OrderPlanetPriorities(
        [
            new SectorMapPlanetLabelPriority(4, false, RequestSeverity.Concerned, false, 99),
            new SectorMapPlanetLabelPriority(3, false, RequestSeverity.Concerned, true, 1),
            new SectorMapPlanetLabelPriority(2, true, RequestSeverity.Serious, false, 1),
            new SectorMapPlanetLabelPriority(1, true, RequestSeverity.Serious, false, 1)
        ]);

        Assert.Equal([1, 2, 3, 4], ordered.Select(priority => priority.PlanetId));
    }
}
