namespace Snap.Core;

/// Pure functions: a command, a window frame, and a work area in, a target frame out.
/// Every frame here is a visible frame (what people see), never the outer rect with its invisible border.
public static class SnapLayout
{
    public const double DefaultWide = 2.0 / 3;
    public static readonly double[] WideChoices = [2.0 / 3, 3.0 / 4, 5.0 / 6];

    public static double ClampWide(double fraction) => Math.Clamp(fraction, 0.5, 0.9);

    public static string FractionName(double fraction) => fraction switch
    {
        _ when Math.Abs(fraction - 3.0 / 4) < 0.01 => "3/4",
        _ when Math.Abs(fraction - 5.0 / 6) < 0.01 => "5/6",
        _ when Math.Abs(fraction - 2.0 / 3) < 0.01 => "2/3",
        _ => $"{Math.Round(fraction * 100)}%",
    };

    /// The frame for a zone command in a work area, with the gap applied. Null for commands that are not zones.
    public static Box? Target(SnapCommand command, Box area, double wide, int gap)
    {
        var half = area.Width / 2;
        var wideWidth = (int)Math.Round(area.Width * ClampWide(wide));
        Box? box = command switch
        {
            SnapCommand.Left => new Box(area.X, area.Y, half, area.Height),
            SnapCommand.Right => new Box(area.X + half, area.Y, area.Width - half, area.Height),
            SnapCommand.Maximize => area,
            SnapCommand.LeftWide => new Box(area.X, area.Y, wideWidth, area.Height),
            SnapCommand.RightWide => new Box(area.Right - wideWidth, area.Y, wideWidth, area.Height),
            _ => null,
        };
        return box is { } b ? ApplyGap(b, area, gap) : null;
    }

    /// A full gap on a side that touches the area edge, half a gap on a side that meets a neighbour.
    /// Left sides take the larger half and right sides the smaller, so two neighbours leave exactly one gap.
    public static Box ApplyGap(Box box, Box area, int gap)
    {
        if (gap <= 0) return box;
        int Inset(bool touchesEdge, bool leading) => touchesEdge ? gap : leading ? gap - gap / 2 : gap / 2;
        var left = box.X + Inset(Math.Abs(box.X - area.X) <= 1, true);
        var top = box.Y + Inset(Math.Abs(box.Y - area.Y) <= 1, true);
        var right = box.Right - Inset(Math.Abs(box.Right - area.Right) <= 1, false);
        var bottom = box.Bottom - Inset(Math.Abs(box.Bottom - area.Bottom) <= 1, false);
        return Box.FromEdges(left, top, right, bottom);
    }

    /// The display that shows the largest part of the window. -1 when it is on none of them.
    public static int PickDisplay(Box window, IReadOnlyList<Box> monitors)
    {
        var best = -1;
        long bestArea = 0;
        for (var i = 0; i < monitors.Count; i++)
        {
            var area = window.Intersect(monitors[i]).Area;
            if (area > bestArea) { best = i; bestArea = area; }
        }
        return best;
    }

    /// The next (step 1) or previous (step -1) display, ordered left to right then top to bottom, wrapping around.
    /// Null with one display.
    public static int? NeighbourDisplay(IReadOnlyList<Box> monitors, int current, int step)
    {
        if (monitors.Count < 2 || current < 0) return null;
        var order = Enumerable.Range(0, monitors.Count).OrderBy(i => monitors[i].X).ThenBy(i => monitors[i].Y).ToList();
        var position = order.IndexOf(current);
        return order[((position + step) % order.Count + order.Count) % order.Count];
    }

    /// Keeps the window's relative position and size on another display.
    public static Box MoveToDisplay(Box window, Box from, Box to)
    {
        double sx = (double)to.Width / from.Width, sy = (double)to.Height / from.Height;
        var moved = new Box(
            to.X + (int)Math.Round((window.X - from.X) * sx),
            to.Y + (int)Math.Round((window.Y - from.Y) * sy),
            (int)Math.Round(window.Width * sx),
            (int)Math.Round(window.Height * sy));
        var inside = moved.Intersect(to);
        return inside.IsEmpty ? to : inside;
    }

    /// Windows draws an invisible resize border around most windows. GetWindowRect includes it;
    /// people see the DWM frame. This is the outer rect SetWindowPos needs so the visible frame lands on target.
    public static Box OuterFor(Box target, Box outer, Box visible) => new(
        target.X - (visible.X - outer.X),
        target.Y - (visible.Y - outer.Y),
        target.Width + (outer.Width - visible.Width),
        target.Height + (outer.Height - visible.Height));

    /// A window with a minimum size can end up larger than its target. Keep it on the screen against the
    /// edge it was sent to: right-side commands align its right edge, every command aligns its top.
    public static Box PinToEdge(Box actual, Box target, SnapCommand command)
    {
        var alignRight = command is SnapCommand.Right or SnapCommand.RightWide;
        return new Box(alignRight ? target.Right - actual.Width : target.X, target.Y, actual.Width, actual.Height);
    }
}
