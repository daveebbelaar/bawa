namespace Snap.Core;

/// A rectangle in physical pixels, the unit Win32 window and monitor rects use.
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static Box FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public Box Intersect(Box other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? default : FromEdges(left, top, right, bottom);
    }

    /// True when every edge is within the tolerance. Windows round sizes, so compare with 2 px.
    public bool IsNear(Box other, int tolerance) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance &&
        Math.Abs(Right - other.Right) <= tolerance && Math.Abs(Bottom - other.Bottom) <= tolerance;

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}
