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
    // Too wide: shrunk just enough to fit.
    [InlineData(250f, 100f, 1.0f, 0.4f)]
    // Already fits: full size.
    [InlineData(50f, 100f, 1.0f, 1.0f)]
    // No width constraint.
    [InlineData(250f, 0f, 1.0f, 1.0f)]
    // A caller's own limit already fits, so it stands.
    [InlineData(250f, 100f, 0.3f, 0.3f)]
    // The limit is held to 0.05-1.
    [InlineData(50f, 0f, 2.0f, 1.0f)]
    [InlineData(50f, 0f, 0.0f, 0.05f)]
    public void FitScale_ShrinksOnlyAsFarAsTheWidthRequires(
        float measuredWidth, float maxWidth, float scaleLimit, float expected)
    {
        Assert.Equal(
            expected,
            SectorMapLabelLayout.FitScale(measuredWidth, maxWidth, scaleLimit),
            precision: 5);
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

    // Planet labels are placed in Rank order, planet id breaking ties - which is how SectorMap
    // orders them. Importance is authored in the thousands, so it must not reach past the seat
    // bonus: a governance seat on a Civilised world outranks a far more important Feral one.
    [Fact]
    public void PlanetLabels_PlaceByRequestsThenSeatsThenImportanceThenId()
    {
        SectorMapPlanetLabelPriority[] priorities =
        [
            new(5, false, SectorMapRequestSeverity.Concerned, false, 99),
            new(4, false, SectorMapRequestSeverity.Concerned, false, 6100),
            new(3, false, SectorMapRequestSeverity.Concerned, true, 1005),
            new(2, true, SectorMapRequestSeverity.Serious, false, 1),
            new(1, true, SectorMapRequestSeverity.Serious, false, 1)
        ];

        // Anchors far apart, so every label fits and the placement order is the processing order.
        var placements = SectorMapLabelLayout.Place(
            priorities.Select(priority => new SectorMapLabelCandidate(
                priority.PlanetId,
                new Vector2(100 * priority.PlanetId, 50),
                priority.Rank,
                new Vector2(20, 10))),
            new SectorMapLabelBounds(0, 0, 1000, 100));

        Assert.Equal([1, 2, 3, 4, 5], placements.Select(placement => placement.Id));
    }
}
