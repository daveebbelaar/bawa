using System.Runtime.InteropServices;
using Snap.Core;

namespace Snap.Windowing;

public enum SnapResult { Moved, Restored, NoWindow, Minimized, Refused, NothingStored, OneDisplay }

/// Picks the window to move, turns a command into a frame with Layout, and applies it.
/// Works in visible frames; converts to the outer rect only for SetWindowPos.
public sealed class WindowManager : IDisposable
{
    private static WindowManager? _current;
    private static readonly HashSet<string> ShellClasses =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];

    private readonly RestoreBook _book = new();
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private readonly nint _hook;
    private nint _lastOther;

    public WindowManager()
    {
        _current = this;
        // Out of context: the callback runs on this (the UI) thread through its message loop.
        unsafe { _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, 0, &OnWinEvent, 0, 0, Native.WINEVENT_OUTOFCONTEXT); }
    }

    public SnapResult? LastResult { get; private set; }
    public nint LastTarget { get; private set; }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (_current is { } manager && root != 0 && !manager.IsOwnOrShell(root)) manager._lastOther = root;
    }

    private bool IsOwnOrShell(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == _ownProcess || ShellClasses.Contains(Native.ClassName(hwnd));
    }

    /// The foreground window, unless that is Snap itself (a tray menu click) or the shell:
    /// then the last other window that was in front.
    public nint TargetWindow()
    {
        var front = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
        if (front != 0 && !IsOwnOrShell(front)) return front;
        return Native.IsWindow(_lastOther) ? _lastOther : 0;
    }

    /// Runs a command on a window (the target window when none is given). Beeps on any failure.
    public async Task<SnapResult> RunAsync(SnapCommand command, double wide, int gapEpx, nint window = 0)
    {
        var hwnd = window != 0 ? window : TargetWindow();
        LastTarget = hwnd;
        var result = await RunCoreAsync(command, wide, gapEpx, hwnd);
        if (result is not (SnapResult.Moved or SnapResult.Restored)) Native.MessageBeep(0);
        LastResult = result;
        return result;
    }

    private async Task<SnapResult> RunCoreAsync(SnapCommand command, double wide, int gapEpx, nint hwnd)
    {
        if (hwnd == 0 || !Native.IsWindow(hwnd)) return SnapResult.NoWindow;
        if (Native.IsIconic(hwnd)) return SnapResult.Minimized;
        // A maximized window ignores new sizes. Restore it to a normal window first.
        if (Native.IsZoomed(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);

        var visible = Native.VisibleFrame(hwnd);
        var displays = Native.Displays();
        if (displays.Count == 0) return SnapResult.NoWindow;
        var index = Math.Max(0, SnapLayout.PickDisplay(visible, displays.Select(d => d.Monitor).ToList()));
        var display = displays[index];

        Box target;
        switch (command)
        {
            case SnapCommand.Restore:
                if (_book.Take(hwnd) is not { } before) return SnapResult.NothingStored;
                target = before;
                break;
            case SnapCommand.NextDisplay or SnapCommand.PreviousDisplay:
                var next = SnapLayout.NeighbourDisplay(displays.Select(d => d.Monitor).ToList(), index, command == SnapCommand.NextDisplay ? 1 : -1);
                if (next is not { } n) return SnapResult.OneDisplay;
                target = SnapLayout.MoveToDisplay(visible, display.Work, displays[n].Work);
                break;
            default:
                var gap = (int)Math.Round(gapEpx * display.Dpi / 96.0);
                target = SnapLayout.Target(command, display.Work, wide, gap)!.Value;
                break;
        }
        if (command != SnapCommand.Restore) _book.RememberIfNew(hwnd, visible);

        if (!Apply(hwnd, target)) return SnapResult.Refused;
        // Moves across displays settle late: the window rescales for the new DPI after the first set.
        var now = Native.VisibleFrame(hwnd);
        if (!now.IsNear(target, 2))
        {
            await Task.Delay(80);
            Apply(hwnd, target);
            now = Native.VisibleFrame(hwnd);
        }
        // An app with a minimum size stays larger than the target: keep it on screen at the edge it was sent to.
        if (now.Width > target.Width + 2 || now.Height > target.Height + 2) Apply(hwnd, SnapLayout.PinToEdge(now, target, command));
        return command == SnapCommand.Restore ? SnapResult.Restored : SnapResult.Moved;
    }

    private static bool Apply(nint hwnd, Box target)
    {
        var outer = SnapLayout.OuterFor(target, Native.OuterRect(hwnd), Native.VisibleFrame(hwnd));
        // Fails with ERROR_ACCESS_DENIED for windows of elevated apps (UIPI).
        return Native.SetWindowPos(hwnd, 0, outer.X, outer.Y, outer.Width, outer.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    public void Dispose()
    {
        if (_hook != 0) Native.UnhookWinEvent(_hook);
        if (_current == this) _current = null;
    }
}
