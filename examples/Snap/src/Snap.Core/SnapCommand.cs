namespace Snap.Core;

public enum SnapCommand { Left, Right, Maximize, LeftWide, RightWide, NextDisplay, PreviousDisplay, Restore }

public static class SnapCommands
{
    public static readonly SnapCommand[] All = Enum.GetValues<SnapCommand>();

    /// The title in Settings and the tray menu. Wide titles show the fraction: "Left wide (3/4)".
    public static string Title(this SnapCommand command, double wide = SnapLayout.DefaultWide) => command switch
    {
        SnapCommand.Left => "Left half",
        SnapCommand.Right => "Right half",
        SnapCommand.Maximize => "Maximize",
        SnapCommand.LeftWide => $"Left wide ({SnapLayout.FractionName(wide)})",
        SnapCommand.RightWide => $"Right wide ({SnapLayout.FractionName(wide)})",
        SnapCommand.NextDisplay => "Next display",
        SnapCommand.PreviousDisplay => "Previous display",
        SnapCommand.Restore => "Restore",
        _ => command.ToString(),
    };

    /// The zone a command fills, as fractions of the screen, for the layout glyph. Null for commands with no zone.
    public static (double Left, double Width)? Zone(this SnapCommand command, double wide = SnapLayout.DefaultWide) => command switch
    {
        SnapCommand.Left => (0, 0.5),
        SnapCommand.Right => (0.5, 0.5),
        SnapCommand.Maximize => (0, 1),
        SnapCommand.LeftWide => (0, SnapLayout.ClampWide(wide)),
        SnapCommand.RightWide => (1 - SnapLayout.ClampWide(wide), SnapLayout.ClampWide(wide)),
        _ => null,
    };
}
