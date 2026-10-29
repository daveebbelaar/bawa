using Snap.Core;
using Xunit;

// One test per acceptance line in docs/STORIES.md, named after the story.
public class LayoutTests
{
    private static readonly Box Area = new(0, 0, 1920, 1032);   // 1080p minus a 48 px taskbar

    [Fact] // S-001
    public void LeftHalfFillsTheLeftHalf() =>
        Assert.Equal(new Box(0, 0, 960, 1032), SnapLayout.Target(SnapCommand.Left, Area, SnapLayout.DefaultWide, 0));

    [Fact] // S-001
    public void OuterRectAndBackGivesTheOriginal()
    {
        var visible = new Box(107, 50, 800, 600);
        var outer = new Box(100, 50, 814, 607);   // a 7 px invisible border left, right, and bottom
        var target = new Box(0, 0, 960, 1032);
        var outerTarget = SnapLayout.OuterFor(target, outer, visible);
        Assert.Equal(new Box(-7, 0, 974, 1039), outerTarget);
        // The border is the same size at the new spot, so cutting it off again gives the target.
        Assert.Equal(target, Box.FromEdges(outerTarget.X + 7, outerTarget.Y, outerTarget.Right - 7, outerTarget.Bottom - 7));
    }

    [Fact] // S-002
    public void HalvesCoverAnOddWidthExactly()
    {
        var odd = new Box(-1921, 0, 1921, 1032);   // a display left of the primary has negative x
        var left = SnapLayout.Target(SnapCommand.Left, odd, SnapLayout.DefaultWide, 0)!.Value;
        var right = SnapLayout.Target(SnapCommand.Right, odd, SnapLayout.DefaultWide, 0)!.Value;
        Assert.Equal(odd.X, left.X);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(odd.Right, right.Right);
    }

    [Fact] // S-003
    public void MaximizeFillsTheWorkArea() => Assert.Equal(Area, SnapLayout.Target(SnapCommand.Maximize, Area, SnapLayout.DefaultWide, 0));

    [Fact] // S-004
    public void RightWideEndsAtTheRightEdge()
    {
        var box = SnapLayout.Target(SnapCommand.RightWide, Area, 2.0 / 3, 0)!.Value;
        Assert.Equal(Area.Right, box.Right);
        Assert.Equal(1280, box.Width);
    }

    [Fact] // S-004
    public void WideFractionIsClamped() => Assert.Equal(0.9, SnapLayout.ClampWide(0.95));

    [Fact] // S-009
    public void GapLeavesOneGapEverywhere()
    {
        var left = SnapLayout.Target(SnapCommand.Left, Area, SnapLayout.DefaultWide, 10)!.Value;
        var right = SnapLayout.Target(SnapCommand.Right, Area, SnapLayout.DefaultWide, 10)!.Value;
        Assert.Equal(10, left.X - Area.X);
        Assert.Equal(10, left.Y - Area.Y);
        Assert.Equal(10, Area.Right - right.Right);
        Assert.Equal(10, Area.Bottom - left.Bottom);
        Assert.Equal(10, right.X - left.Right);
    }

    [Fact] // S-009
    public void OddGapStillLeavesExactlyOneGapBetween()
    {
        var left = SnapLayout.Target(SnapCommand.Left, Area, SnapLayout.DefaultWide, 15)!.Value;   // 10 epx at 150%
        var right = SnapLayout.Target(SnapCommand.Right, Area, SnapLayout.DefaultWide, 15)!.Value;
        Assert.Equal(15, right.X - left.Right);
    }

    [Fact] // S-009
    public void WideTitleShowsTheFraction() => Assert.Equal("Left wide (3/4)", SnapCommand.LeftWide.Title(0.75));

    [Fact] // S-010
    public void LeftHalfStaysALeftHalfOnABiggerDisplay()
    {
        var small = new Box(0, 0, 1920, 1032);
        var big = new Box(1920, 0, 2560, 1392);
        var leftHalf = SnapLayout.Target(SnapCommand.Left, small, SnapLayout.DefaultWide, 0)!.Value;
        Assert.Equal(SnapLayout.Target(SnapCommand.Left, big, SnapLayout.DefaultWide, 0), SnapLayout.MoveToDisplay(leftHalf, small, big));
    }

    [Fact] // S-010
    public void OneDisplayHasNoNeighbour() => Assert.Null(SnapLayout.NeighbourDisplay([Area], 0, 1));

    [Fact] // S-010
    public void NeighboursGoLeftToRightAndWrap()
    {
        Box[] monitors = [new(1920, 0, 2560, 1440), new(-1920, 0, 1920, 1080), new(0, 0, 1920, 1080)];
        Assert.Equal(0, SnapLayout.NeighbourDisplay(monitors, 2, 1));
        Assert.Equal(1, SnapLayout.NeighbourDisplay(monitors, 0, 1));
        Assert.Equal(0, SnapLayout.NeighbourDisplay(monitors, 1, -1));
    }

    [Fact]
    public void PicksTheDisplayWithTheLargestPart()
    {
        Box[] monitors = [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080)];
        Assert.Equal(1, SnapLayout.PickDisplay(new Box(1800, 100, 800, 600), monitors));
        Assert.Equal(-1, SnapLayout.PickDisplay(new Box(5000, 0, 100, 100), monitors));
    }

    [Fact]
    public void ATooLargeWindowIsPinnedToTheEdgeItWasSentTo()
    {
        var target = new Box(960, 0, 960, 1032);
        Assert.Equal(new Box(820, 0, 1100, 1032), SnapLayout.PinToEdge(new Box(960, 0, 1100, 1032), target, SnapCommand.Right));
    }
}

public class ShortcutTests
{
    [Fact] // S-008
    public void AssigningATakenComboRemovesItFromTheOtherCommand()
    {
        var map = new ShortcutMap();
        var combo = map[SnapCommand.Left]!.Value;
        Assert.True(map.Assign(SnapCommand.Maximize, combo));
        Assert.Null(map[SnapCommand.Left]);
        Assert.Equal(combo, map[SnapCommand.Maximize]);
    }

    [Theory] // S-008
    [InlineData(Mods.None, 0x41, false)]
    [InlineData(Mods.Shift, 0x41, false)]
    [InlineData(Mods.None, KeyCombo.F1 + 4, true)]
    [InlineData(Mods.Ctrl | Mods.Alt, 0x41, true)]
    [InlineData(Mods.Win | Mods.Shift, 0x41, true)]
    public void GlobalShortcutsNeedAModifier(Mods mods, int key, bool valid)
    {
        var map = new ShortcutMap();
        Assert.Equal(valid, map.Assign(SnapCommand.Left, new KeyCombo(mods, key)));
    }

    [Fact]
    public void ShortcutsReadTheWindowsWay() =>
        Assert.Equal("Ctrl+Alt+Shift+Right", new ShortcutMap()[SnapCommand.NextDisplay]!.Value.ToString());

    [Fact] // S-011
    public void TwoSnapsInARowKeepTheFirstFrame()
    {
        var book = new RestoreBook();
        book.RememberIfNew(42, new Box(100, 100, 800, 600));
        book.RememberIfNew(42, new Box(0, 0, 960, 1032));
        Assert.Equal(new Box(100, 100, 800, 600), book.Take(42));
        Assert.Null(book.Take(42));
    }
}
