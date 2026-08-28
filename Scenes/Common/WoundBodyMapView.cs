using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class WoundBodyMapView : Control
{
    private sealed record RegionDefinition(string Name, string[] Locations, Vector2[] Polygon, Vector2 Label);
    private sealed record RegionState(WoundLevel Level, bool Severed, bool Crippled, bool HealthyCybernetic);

    private IReadOnlyList<WoundLocationSummary> _wounds = [];
    public IReadOnlyList<string> UnmappedLocations { get; private set; } = [];

    private static readonly RegionDefinition[] Regions =
    [
        new("HEAD", ["Brain", "Eyes", "Face"],
            [new(.43f,.08f),new(.57f,.08f),new(.62f,.17f),new(.58f,.27f),new(.42f,.27f),new(.38f,.17f)], new(.68f,.14f)),
        new("TORSO", ["Torso", "Vitals"],
            [new(.36f,.28f),new(.64f,.28f),new(.68f,.55f),new(.58f,.62f),new(.42f,.62f),new(.32f,.55f)], new(.73f,.43f)),
        new("LEFT ARM", ["Left Arm", "Left Hand"],
            [new(.35f,.29f),new(.27f,.29f),new(.17f,.48f),new(.11f,.65f),new(.18f,.68f),new(.27f,.52f),new(.40f,.38f)], new(.02f,.39f)),
        new("RIGHT ARM", ["Right Arm", "Right Hand"],
            [new(.65f,.29f),new(.73f,.29f),new(.83f,.48f),new(.89f,.65f),new(.82f,.68f),new(.73f,.52f),new(.60f,.38f)], new(.73f,.33f)),
        new("LEFT LEG", ["Left Leg", "Left Foot"],
            [new(.42f,.60f),new(.50f,.61f),new(.48f,.82f),new(.42f,.96f),new(.31f,.96f),new(.37f,.80f)], new(.02f,.79f)),
        new("RIGHT LEG", ["Right Leg", "Right Foot"],
            [new(.50f,.61f),new(.58f,.60f),new(.63f,.80f),new(.69f,.96f),new(.58f,.96f),new(.52f,.82f)], new(.73f,.79f))
    ];

    public void SetWounds(IReadOnlyList<WoundLocationSummary> wounds)
    {
        _wounds = wounds ?? [];
        HashSet<string> mapped = Regions.SelectMany(region => region.Locations)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        UnmappedLocations = _wounds
            .Where(wound => (wound.PrincipalWoundLevel != WoundLevel.None || wound.IsSevered || wound.IsCrippled)
                && !mapped.Contains(wound.LocationName))
            .Select(wound => wound.LocationName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        if (size.X <= 0 || size.Y <= 0) return;
        Color outline = OnlyWarStyle.Gold;
        foreach (RegionDefinition region in Regions)
        {
            RegionState state = Aggregate(region);
            Vector2[] polygon = region.Polygon.Select(point => new Vector2(point.X * size.X, point.Y * size.Y)).ToArray();
            Color fill = WoundPresentationPalette.For(state.Level, state.Severed, state.HealthyCybernetic);
            DrawColoredPolygon(polygon, fill);
            Vector2[] closed = polygon.Concat([polygon[0]]).ToArray();
            float width = state.Level >= WoundLevel.Massive || state.Severed ? 4f : 2f;
            DrawPolyline(closed, outline, width, true);
            if (state.Level >= WoundLevel.Mortal || state.Severed)
            {
                DrawPolyline(closed, new Color(outline, .55f), width + 4f, true);
            }
            if (state.Severed) DrawHatch(polygon, fill.Lightened(.30f));
            DrawRegionLabel(region, polygon, state, size, outline);
        }

        if (UnmappedLocations.Count > 0)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(8, size.Y - 6),
                "! UNMAPPED LOCATION", HorizontalAlignment.Left, -1, 11, WoundPresentationPalette.Minor);
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (Size.X <= 0 || Size.Y <= 0) return string.Empty;
        foreach (RegionDefinition region in Regions)
        {
            Vector2[] polygon = region.Polygon
                .Select(point => new Vector2(point.X * Size.X, point.Y * Size.Y))
                .ToArray();
            if (!Geometry2D.IsPointInPolygon(atPosition, polygon)) continue;

            List<WoundLocationSummary> wounds = _wounds
                .Where(wound => region.Locations.Contains(wound.LocationName, StringComparer.OrdinalIgnoreCase))
                .Where(wound => wound.PrincipalWoundLevel != WoundLevel.None
                    || wound.IsSevered || wound.IsCrippled || wound.IsCybernetic)
                .ToList();
            if (wounds.Count == 0) return $"{region.Name}\nNo recorded injury or replacement.";

            return region.Name + "\n" + string.Join("\n", wounds.Select(wound =>
                $"• {wound.LocationName}: {wound.Status}; {wound.Recovery}"
                + (wound.IsSevered ? "; lost" : string.Empty)
                + (wound.IsCrippled ? "; crippled" : string.Empty)
                + (wound.IsCybernetic ? "; cybernetic" : string.Empty)));
        }
        return string.Empty;
    }

    private RegionState Aggregate(RegionDefinition region)
    {
        List<WoundLocationSummary> wounds = _wounds
            .Where(wound => region.Locations.Contains(wound.LocationName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return new RegionState(
            wounds.Select(wound => wound.PrincipalWoundLevel).DefaultIfEmpty(WoundLevel.None).Max(),
            wounds.Any(wound => wound.IsSevered),
            wounds.Any(wound => wound.IsCrippled),
            wounds.Count > 0 && wounds.All(wound => wound.IsCybernetic && wound.PrincipalWoundLevel == WoundLevel.None));
    }

    private void DrawRegionLabel(RegionDefinition region, Vector2[] polygon, RegionState state, Vector2 size, Color color)
    {
        Vector2 center = polygon.Aggregate(Vector2.Zero, (sum, point) => sum + point) / polygon.Length;
        Vector2 label = new(region.Label.X * size.X, region.Label.Y * size.Y);
        DrawLine(center, label, color, 1f, true);
        string suffix = state.Severed ? "  LOST" : state.Crippled ? "  CRIPPLED" : string.Empty;
        DrawString(ThemeDB.FallbackFont, label, region.Name + suffix,
            HorizontalAlignment.Left, -1, 10, color);
    }

    private void DrawHatch(Vector2[] polygon, Color color)
    {
        float minX = polygon.Min(point => point.X);
        float maxX = polygon.Max(point => point.X);
        float minY = polygon.Min(point => point.Y);
        float maxY = polygon.Max(point => point.Y);
        for (float x = minX - (maxY - minY); x < maxX; x += 9f)
        {
            DrawLine(new Vector2(x, maxY), new Vector2(x + (maxY - minY), minY), color, 1.5f, true);
        }
    }
}
