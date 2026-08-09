using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OnlyWar.Helpers.UI;

/// <summary>
/// The three world-space label bands used by the sector map.
/// </summary>
public enum SectorMapLabelBand
{
    A,
    B,
    C
}

/// <summary>
/// A rectangle in world coordinates. Positions are top-left based so the result can be
/// passed directly to the map renderer after adding the font ascent to the baseline.
/// </summary>
public readonly record struct SectorMapLabelBounds(float Left, float Top, float Right, float Bottom)
{
    public float Width => Math.Max(0, Right - Left);
    public float Height => Math.Max(0, Bottom - Top);

    public bool Contains(Vector2 position, Vector2 size) =>
        position.X >= Left
        && position.Y >= Top
        && position.X + size.X <= Right
        && position.Y + size.Y <= Bottom;
}

/// <summary>
/// Font-independent input to the sector-map label solver.
/// </summary>
public readonly record struct SectorMapLabelCandidate(
    int Id,
    Vector2 Anchor,
    long Priority,
    Vector2 Size,
    float Scale = 1.0f,
    IReadOnlyList<IReadOnlyList<Vector2>> AllowedRegions = null);

/// <summary>
/// A label placement returned by <see cref="SectorMapLabelLayout.Place"/>.
/// </summary>
public readonly record struct SectorMapLabelPlacement(
    int Id,
    Vector2 Position,
    Vector2 Size,
    long Priority,
    float Scale);

/// <summary>
/// Stable priority data for a planet label. The final planet-id tiebreak is part of the
/// value rather than being left to dictionary or source-enumeration order.
/// </summary>
public readonly record struct SectorMapPlanetLabelPriority(
    int PlanetId,
    bool HasActiveWork,
    RequestSeverity RequestSeverity,
    bool IsGovernanceSeat,
    int Importance)
{
    public long Rank =>
        (HasActiveWork ? 3_000_000_000L : 0L)
        + (long)RequestSeverity * 1_000_000L
        + (IsGovernanceSeat ? 1_000L : 0L)
        + Importance;
}

/// <summary>
/// Pure geometry and ordering rules for the sector map label layer. It deliberately has no
/// Godot dependency: font measurement happens at the edge, and the solver only receives the
/// measured world-space extents.
/// </summary>
public static class SectorMapLabelLayout
{
    public const float DefaultLabelGap = 4.0f;

    private static readonly Vector2[] AnchorOffsets =
    [
        new Vector2(0.0f, 1.0f),   // below
        new Vector2(0.0f, -1.0f),  // above
        new Vector2(1.0f, 0.0f),   // right
        new Vector2(-1.0f, 0.0f)   // left
    ];

    public static SectorMapLabelBand SelectBand(
        float zoom,
        float bandABoundary = 1.1f,
        float bandBBoundary = 3.5f)
    {
        if (bandBBoundary <= bandABoundary)
            throw new ArgumentOutOfRangeException(nameof(bandBBoundary));

        if (zoom < bandABoundary) return SectorMapLabelBand.A;
        return zoom < bandBBoundary ? SectorMapLabelBand.B : SectorMapLabelBand.C;
    }

    /// <summary>
    /// Places candidates greedily in descending priority order. The offsets are intentionally
    /// fixed and documented: below, above, right, left. Touching edges are allowed, while a
    /// positive-area overlap rejects a candidate.
    /// </summary>
    public static IReadOnlyList<SectorMapLabelPlacement> Place(
        IEnumerable<SectorMapLabelCandidate> candidates,
        SectorMapLabelBounds mapBounds,
        float gap = DefaultLabelGap)
    {
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));

        List<SectorMapLabelPlacement> placements = [];
        foreach (SectorMapLabelCandidate candidate in candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Id))
        {
            if (candidate.Size.X <= 0 || candidate.Size.Y <= 0) continue;

            foreach (Vector2 offset in AnchorOffsets)
            {
                Vector2 position = GetPosition(candidate.Anchor, candidate.Size, offset, gap);
                if (!mapBounds.Contains(position, candidate.Size)) continue;
                if (!IsInsideAllowedRegion(candidate, position)) continue;

                bool collides = placements.Any(placed => Intersects(
                    position,
                    candidate.Size,
                    placed.Position,
                    placed.Size));
                if (collides) continue;

                placements.Add(new SectorMapLabelPlacement(
                    candidate.Id,
                    position,
                    candidate.Size,
                    candidate.Priority,
                    candidate.Scale));
                break;
            }
        }

        return placements;
    }

    /// <summary>
    /// Returns the proportional size used when a label is wider than its region's inscribed
    /// width. Height is reduced with width so the label remains horizontal and undistorted.
    /// </summary>
    public static Vector2 ClampExtentToWidth(Vector2 extent, float maxWidth)
    {
        if (extent.X <= 0 || extent.Y <= 0 || maxWidth <= 0) return Vector2.Zero;
        if (extent.X <= maxWidth) return extent;

        float scale = maxWidth / extent.X;
        return extent * scale;
    }

    public static long GetPlanetPriority(
        bool hasActiveWork,
        RequestSeverity requestSeverity,
        bool isGovernanceSeat,
        int importance)
    {
        return new SectorMapPlanetLabelPriority(
            PlanetId: 0,
            hasActiveWork,
            requestSeverity,
            isGovernanceSeat,
            importance).Rank;
    }

    public static IReadOnlyList<SectorMapPlanetLabelPriority> OrderPlanetPriorities(
        IEnumerable<SectorMapPlanetLabelPriority> priorities)
    {
        if (priorities == null) throw new ArgumentNullException(nameof(priorities));

        return priorities
            .OrderByDescending(priority => priority.HasActiveWork)
            .ThenByDescending(priority => priority.RequestSeverity)
            .ThenByDescending(priority => priority.IsGovernanceSeat)
            .ThenByDescending(priority => priority.Importance)
            .ThenBy(priority => priority.PlanetId)
            .ToList();
    }

    private static Vector2 GetPosition(Vector2 anchor, Vector2 size, Vector2 offset, float gap)
    {
        if (offset.X == 0 && offset.Y > 0)
        {
            return new Vector2(anchor.X - size.X / 2.0f, anchor.Y + gap);
        }

        if (offset.X == 0 && offset.Y < 0)
        {
            return new Vector2(anchor.X - size.X / 2.0f, anchor.Y - size.Y - gap);
        }

        if (offset.X > 0)
        {
            return new Vector2(anchor.X + gap, anchor.Y - size.Y / 2.0f);
        }

        return new Vector2(anchor.X - size.X - gap, anchor.Y - size.Y / 2.0f);
    }

    private static bool Intersects(Vector2 leftPosition, Vector2 leftSize, Vector2 rightPosition, Vector2 rightSize)
    {
        return leftPosition.X < rightPosition.X + rightSize.X
            && leftPosition.X + leftSize.X > rightPosition.X
            && leftPosition.Y < rightPosition.Y + rightSize.Y
            && leftPosition.Y + leftSize.Y > rightPosition.Y;
    }

    private static bool IsInsideAllowedRegion(SectorMapLabelCandidate candidate, Vector2 position)
    {
        if (candidate.AllowedRegions == null || candidate.AllowedRegions.Count == 0)
            return true;

        Vector2[] corners =
        [
            position,
            position + new Vector2(candidate.Size.X, 0),
            position + candidate.Size,
            position + new Vector2(0, candidate.Size.Y)
        ];
        Vector2[] samples =
        [
            corners[0],
            corners[1],
            corners[2],
            corners[3],
            (corners[0] + corners[1]) / 2.0f,
            (corners[1] + corners[2]) / 2.0f,
            (corners[2] + corners[3]) / 2.0f,
            (corners[3] + corners[0]) / 2.0f,
            position + candidate.Size / 2.0f
        ];

        IReadOnlyList<Vector2> containingRegion = candidate.AllowedRegions
            .FirstOrDefault(region => ContainsPoint(region, samples[^1]));
        if (containingRegion == null) return false;

        if (samples.Any(sample => !ContainsPoint(containingRegion, sample))) return false;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 start = corners[i];
            Vector2 end = corners[(i + 1) % corners.Length];
            for (int j = 0; j < containingRegion.Count; j++)
            {
                Vector2 boundaryStart = containingRegion[j];
                Vector2 boundaryEnd = containingRegion[(j + 1) % containingRegion.Count];
                if (ProperlyIntersects(start, end, boundaryStart, boundaryEnd)) return false;
            }
        }

        return true;
    }

    private static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        if (polygon == null || polygon.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 current = polygon[i];
            Vector2 previous = polygon[j];
            if (IsOnSegment(previous, current, point)) return true;

            bool crosses = (current.Y > point.Y) != (previous.Y > point.Y);
            if (!crosses) continue;

            float intersectionX = (previous.X - current.X)
                * (point.Y - current.Y)
                / (previous.Y - current.Y)
                + current.X;
            if (point.X < intersectionX) inside = !inside;
        }

        return inside;
    }

    private static bool ProperlyIntersects(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
    {
        float first = Cross(firstEnd - firstStart, secondStart - firstStart);
        float second = Cross(firstEnd - firstStart, secondEnd - firstStart);
        float third = Cross(secondEnd - secondStart, firstStart - secondStart);
        float fourth = Cross(secondEnd - secondStart, firstEnd - secondStart);
        const float epsilon = 0.001f;

        return ((first > epsilon && second < -epsilon) || (first < -epsilon && second > epsilon))
            && ((third > epsilon && fourth < -epsilon) || (third < -epsilon && fourth > epsilon));
    }

    private static bool IsOnSegment(Vector2 start, Vector2 end, Vector2 point)
    {
        const float epsilon = 0.001f;
        if (Math.Abs(Cross(end - start, point - start)) > epsilon) return false;

        return point.X >= Math.Min(start.X, end.X) - epsilon
            && point.X <= Math.Max(start.X, end.X) + epsilon
            && point.Y >= Math.Min(start.Y, end.Y) - epsilon
            && point.Y <= Math.Max(start.Y, end.Y) + epsilon;
    }

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;
}
