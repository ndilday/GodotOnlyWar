using Godot;
using OnlyWar.Helpers;
using System.Collections.Generic;
using System.Linq;

public partial class WoundDiagramView : Control
{
    private readonly Dictionary<int, WoundLocationSummary> _woundsByLocationId = [];

    private static readonly Dictionary<string, Vector2> LocationPoints = new()
    {
        ["Brain"] = new Vector2(0.50f, 0.20f),
        ["Eyes"] = new Vector2(0.50f, 0.21f),
        ["Face"] = new Vector2(0.50f, 0.25f),
        ["Torso"] = new Vector2(0.50f, 0.47f),
        ["Vitals"] = new Vector2(0.50f, 0.40f),
        ["Left Arm"] = new Vector2(0.28f, 0.43f),
        ["Right Arm"] = new Vector2(0.72f, 0.43f),
        ["Left Hand"] = new Vector2(0.17f, 0.52f),
        ["Right Hand"] = new Vector2(0.83f, 0.52f),
        ["Left Leg"] = new Vector2(0.42f, 0.75f),
        ["Right Leg"] = new Vector2(0.58f, 0.75f),
        ["Left Foot"] = new Vector2(0.34f, 0.90f),
        ["Right Foot"] = new Vector2(0.66f, 0.90f)
    };

    public void SetWounds(IReadOnlyList<WoundLocationSummary> wounds)
    {
        _woundsByLocationId.Clear();
        foreach (WoundLocationSummary wound in wounds ?? [])
        {
            _woundsByLocationId[wound.HitLocationId] = wound;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 rect = GetRect();
        Vector2 center = rect.Size / 2f;
        float radius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.38f;
        Color line = new(0.58f, 0.52f, 0.40f, 0.85f);
        Color goldDim = new(0.44f, 0.35f, 0.20f, 0.78f);

        DrawCircle(center, radius, new Color(0, 0, 0, 0), false, 2f, true);
        DrawArc(center, radius, 0, Mathf.Tau, 96, goldDim, 2f, true);
        DrawRect(new Rect2(center.X - radius * 0.70f, center.Y - radius, radius * 1.40f, radius * 2f), goldDim, false, 1f);

        DrawCircle(center + new Vector2(0, -radius * 0.52f), radius * 0.16f, new Color(0, 0, 0, 0), false, 3f, true);
        DrawRoundRect(center + new Vector2(-radius * 0.25f, -radius * 0.33f), new Vector2(radius * 0.50f, radius * 0.74f), line);

        DrawLine(center + new Vector2(-radius * 0.22f, -radius * 0.20f), center + new Vector2(-radius * 0.82f, -radius * 0.58f), line, 4f, true);
        DrawLine(center + new Vector2(radius * 0.22f, -radius * 0.20f), center + new Vector2(radius * 0.82f, -radius * 0.58f), line, 4f, true);
        DrawLine(center + new Vector2(-radius * 0.22f, -radius * 0.10f), center + new Vector2(-radius * 0.95f, radius * 0.02f), line, 4f, true);
        DrawLine(center + new Vector2(radius * 0.22f, -radius * 0.10f), center + new Vector2(radius * 0.95f, radius * 0.02f), line, 4f, true);
        DrawLine(center + new Vector2(-radius * 0.16f, radius * 0.38f), center + new Vector2(-radius * 0.42f, radius * 0.92f), line, 4f, true);
        DrawLine(center + new Vector2(radius * 0.16f, radius * 0.38f), center + new Vector2(radius * 0.42f, radius * 0.92f), line, 4f, true);
        DrawLine(center + new Vector2(-radius * 0.09f, radius * 0.38f), center + new Vector2(-radius * 0.72f, radius * 0.80f), line, 4f, true);
        DrawLine(center + new Vector2(radius * 0.09f, radius * 0.38f), center + new Vector2(radius * 0.72f, radius * 0.80f), line, 4f, true);

        foreach (WoundLocationSummary wound in _woundsByLocationId.Values.Where(w => w.Severity > MedicalSeverity.None || w.IsCybernetic))
        {
            if (!LocationPoints.TryGetValue(wound.LocationName, out Vector2 normalized))
            {
                continue;
            }

            Vector2 point = new(rect.Size.X * normalized.X, rect.Size.Y * normalized.Y);
            Color color = ColorFor(wound.Severity, wound.IsCybernetic);
            DrawCircle(point, 8f, color);
            DrawArc(point, 10f, 0, Mathf.Tau, 24, new Color(0.96f, 0.84f, 0.52f), 1f, true);
        }
    }

    private void DrawRoundRect(Vector2 position, Vector2 size, Color color)
    {
        DrawRect(new Rect2(position, size), color, false, 3f);
    }

    private static Color ColorFor(MedicalSeverity severity, bool isCybernetic)
    {
        if (isCybernetic)
        {
            return new Color(0.30f, 0.70f, 0.78f);
        }

        return severity switch
        {
            MedicalSeverity.Lost => new Color(0.74f, 0.23f, 0.17f),
            MedicalSeverity.Critical => new Color(0.74f, 0.23f, 0.17f),
            MedicalSeverity.Serious => new Color(0.83f, 0.63f, 0.31f),
            MedicalSeverity.Watch => new Color(0.83f, 0.63f, 0.31f),
            MedicalSeverity.Stable => new Color(0.34f, 0.64f, 0.37f),
            _ => new Color(0.52f, 0.48f, 0.38f)
        };
    }
}
