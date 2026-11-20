using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The Chapter's running legacy total over time (PRD §4.25). Each point
/// is a week that changed the total; a gold ring marks a week in which a vow was kept or broken.
/// Hover a point for its date, total and any vow resolved that week.
/// </summary>
public partial class LegacyGraph : Control
{
    private const float PadLeft = 44f;
    private const float PadRight = 12f;
    private const float PadTop = 10f;
    private const float PadBottom = 20f;
    private const float HoverRadius = 12f;

    private IReadOnlyList<LegacyPointView> _points = [];

    public LegacyGraph()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // A tooltip is only requested when this is not empty; _GetTooltip supplies the real text.
        TooltipText = "Legacy";
    }

    public void SetSeries(IReadOnlyList<LegacyPointView> points)
    {
        _points = points ?? [];
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 plot = PlotRect();
        DrawRect(plot, OnlyWarStyle.WithAlpha(OnlyWarStyle.MapBackground, 0.6f));
        if (_points.Count == 0) return;

        (int min, int max) = Range();
        Font font = GetThemeDefaultFont();
        int fontSize = 11;

        // The zero line, then the top and bottom of the range.
        float zeroY = ValueToY(0, min, max, plot);
        DrawLine(new Vector2(plot.Position.X, zeroY), new Vector2(plot.End.X, zeroY),
            OnlyWarStyle.WithAlpha(OnlyWarStyle.MutedText, 0.6f), 1f);
        DrawString(font, new Vector2(2, ValueToY(max, min, max, plot) + 4), Signed(max),
            HorizontalAlignment.Left, PadLeft - 6, fontSize, OnlyWarStyle.MutedText);
        DrawString(font, new Vector2(2, ValueToY(min, min, max, plot) + 4), Signed(min),
            HorizontalAlignment.Left, PadLeft - 6, fontSize, OnlyWarStyle.MutedText);
        if (min < 0 && max > 0)
        {
            DrawString(font, new Vector2(2, zeroY + 4), "0",
                HorizontalAlignment.Left, PadLeft - 6, fontSize, OnlyWarStyle.MutedText);
        }

        Vector2[] positions = Positions(plot, min, max);
        for (int index = 1; index < positions.Length; index++)
        {
            // A step line: the total holds until the next week that changes it.
            Vector2 previous = positions[index - 1];
            Vector2 corner = new(positions[index].X, previous.Y);
            DrawLine(previous, corner, OnlyWarStyle.Gold, 2f);
            DrawLine(corner, positions[index], OnlyWarStyle.Gold, 2f);
        }
        for (int index = 0; index < positions.Length; index++)
        {
            LegacyPointView point = _points[index];
            Color color = point.Total >= 0 ? OnlyWarStyle.MedicalStable : OnlyWarStyle.Critical;
            DrawCircle(positions[index], 3f, color);
            if (!string.IsNullOrEmpty(point.MarkerLabel))
            {
                DrawArc(positions[index], 6.5f, 0, Mathf.Tau, 20, OnlyWarStyle.Gold, 1.5f);
            }
        }

        DrawString(font, new Vector2(plot.Position.X, Size.Y - 4), _points[0].DateLabel,
            HorizontalAlignment.Left, plot.Size.X / 2, fontSize, OnlyWarStyle.MutedText);
        if (_points.Count > 1)
        {
            DrawString(font, new Vector2(plot.Position.X + plot.Size.X / 2, Size.Y - 4), _points[^1].DateLabel,
                HorizontalAlignment.Right, plot.Size.X / 2, fontSize, OnlyWarStyle.MutedText);
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (_points.Count == 0) return "No legacy recorded yet.";
        Rect2 plot = PlotRect();
        (int min, int max) = Range();
        Vector2[] positions = Positions(plot, min, max);
        int nearest = -1;
        float best = float.MaxValue;
        for (int index = 0; index < positions.Length; index++)
        {
            float distance = Mathf.Abs(positions[index].X - atPosition.X);
            if (distance < best)
            {
                best = distance;
                nearest = index;
            }
        }
        if (nearest < 0 || best > HoverRadius * 3) return "Hover a point to see the legacy at that date.";
        LegacyPointView point = _points[nearest];
        string text = $"{point.DateLabel}: legacy {Signed(point.Total)}";
        return string.IsNullOrEmpty(point.MarkerLabel) ? text : $"{text}\n{point.MarkerLabel}";
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }

    private Rect2 PlotRect() => new(
        PadLeft,
        PadTop,
        Math.Max(1f, Size.X - PadLeft - PadRight),
        Math.Max(1f, Size.Y - PadTop - PadBottom));

    private (int Min, int Max) Range()
    {
        int min = Math.Min(0, _points.Min(point => point.Total));
        int max = Math.Max(0, _points.Max(point => point.Total));
        if (min == max) max = min + 1;
        return (min, max);
    }

    private Vector2[] Positions(Rect2 plot, int min, int max)
    {
        int firstWeek = _points[0].Week;
        int span = Math.Max(1, _points[^1].Week - firstWeek);
        return _points
            .Select(point => new Vector2(
                _points.Count == 1
                    ? plot.Position.X + plot.Size.X / 2
                    : plot.Position.X + plot.Size.X * (point.Week - firstWeek) / span,
                ValueToY(point.Total, min, max, plot)))
            .ToArray();
    }

    private static float ValueToY(int value, int min, int max, Rect2 plot) =>
        plot.End.Y - plot.Size.Y * (value - min) / (max - min);

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
}
