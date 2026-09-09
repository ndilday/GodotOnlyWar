namespace OnlyWar.Domain.Geometry;

/// <summary>Signed integral cell/dimensions, independent of rendering coordinates.</summary>
public readonly record struct GridCell(int X, int Y)
{
    public static implicit operator PlanePoint(GridCell cell) => new(cell.X, cell.Y);
}

/// <summary>Single-precision geometry; operations retain the existing scalar evaluation order.</summary>
public readonly record struct PlanePoint(float X, float Y)
{
    public static PlanePoint operator -(PlanePoint a, PlanePoint b) => new(a.X - b.X, a.Y - b.Y);
    public PlanePoint Lerp(PlanePoint to, float weight) =>
        new(X + (to.X - X) * weight, Y + (to.Y - Y) * weight);
}
