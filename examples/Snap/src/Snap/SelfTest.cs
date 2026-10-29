using Snap.Core;
using Snap.Pages;
using Snap.UI;
using Snap.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Snap;

/// Self-test for agents: runs Snap's real functions, prints PASS or FAIL per check, and exits 0 or 1.
/// It snaps a test window of its own, so it never moves the windows of the person's other apps.
///   tools/selftest.ps1
/// Add a check for every story with logic.
public static class SelfTest
{
    public static bool Requested => Environment.GetCommandLineArgs().Contains("--selftest");

    private static int _failures, _count;

    public static async Task RunIfRequestedAsync(MainWindow window, AppModel model)
    {
        if (!Requested) return;
        if (!AppPaths.IsTestStore)
        {
            AgentLog.Line("SELFTEST refused: set APP_STORE to a file in %TEMP%");
            AgentLog.Exit(1);
        }
        await Task.Delay(500);

        // Hotkeys need a real window to land on. RegisterHotKey on handle 0 still "succeeds", and then
        // WM_HOTKEY goes to the thread queue where nothing reads it: every shortcut is dead.
        Check("the tray window exists for hotkeys and the tray icon", model.Tray.Handle != 0 && Native.IsWindow(model.Tray.Handle));

        // S-005: every default shortcut is registered, or reported as failed. None is silently missing.
        var bound = SnapCommands.All.Where(c => model.Shortcuts[c] is not null).ToHashSet();
        Check("every shortcut is registered or reported", bound.SetEquals(model.Hotkeys.Registered.Union(model.Hotkeys.Failed)));
        foreach (var failed in model.Hotkeys.Failed) AgentLog.Line($"INFO {failed.Title()} ({model.Shortcuts[failed]}) is owned by another app");

        // S-008: recording turns every hotkey off, and they come back after.
        var registered = model.Hotkeys.Registered.Count;
        model.SuspendHotkeys();
        Check("recording unregisters every hotkey", model.Hotkeys.Registered.Count == 0);
        model.ResumeHotkeys();
        Check("hotkeys come back after recording", model.Hotkeys.Registered.Count == registered);

        // S-001 to S-004, S-011: snap a real window and measure where it landed.
        var test = new Window { Title = "Snap test window", Content = new Grid() };
        test.SizeAndCenter(640, 480);
        test.Activate();
        await Task.Delay(400);
        var hwnd = test.Handle();
        var before = Native.VisibleFrame(hwnd);
        var display = Native.Displays().OrderByDescending(d => d.Monitor.Intersect(before).Area).First();
        AgentLog.Line($"INFO test window on a display with work area {display.Work} at {display.Dpi} dpi");

        // S-001 the whole way in: a WM_HOTKEY on the tray window runs the command. Only the physical key press is left
        // out, which needs a person (an agent's injected keys may never reach the hotkey system).
        model.HotkeyTarget = hwnd;
        Native.PostMessageW(model.Tray.Handle, Native.WM_HOTKEY, HotkeyCenter.Id(SnapCommand.Left), 0);
        await Task.Delay(500);
        Check("a hotkey message snaps the window", Native.VisibleFrame(hwnd).IsNear(SnapLayout.Target(SnapCommand.Left, display.Work, model.Wide, 0)!.Value, 2),
            $"got {Native.VisibleFrame(hwnd)}");
        model.HotkeyTarget = 0;

        foreach (var command in new[] { SnapCommand.Left, SnapCommand.Right, SnapCommand.LeftWide, SnapCommand.RightWide, SnapCommand.Maximize })
        {
            var result = await model.RunAsync(command, hwnd);
            var expected = SnapLayout.Target(command, display.Work, model.Wide, 0)!.Value;
            var actual = Native.VisibleFrame(hwnd);
            Check($"{command.Title(model.Wide)} lands on its target", result == SnapResult.Moved && actual.IsNear(expected, 2),
                $"{result}, expected {expected}, got {actual}");
        }

        Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
        await Task.Delay(300);
        await model.RunAsync(SnapCommand.Left, hwnd);
        Check("a maximized window is restored, then snapped", !Native.IsZoomed(hwnd) &&
            Native.VisibleFrame(hwnd).IsNear(SnapLayout.Target(SnapCommand.Left, display.Work, model.Wide, 0)!.Value, 2));

        // S-009: the gap is scaled by the display's DPI and leaves a full gap at the screen edge.
        var gapBefore = model.Gap;
        model.Gap = 10;
        await model.RunAsync(SnapCommand.Left, hwnd);
        var gapPx = (int)Math.Round(10 * display.Dpi / 96.0);
        var withGap = Native.VisibleFrame(hwnd);
        Check($"a 10 epx gap is {gapPx} px at the screen edge",
            Math.Abs(withGap.X - display.Work.X - gapPx) <= 1 && Math.Abs(withGap.Y - display.Work.Y - gapPx) <= 1, $"got {withGap}");
        model.Gap = gapBefore;

        // S-010: one display has no next display; more than one moves the window there.
        var next = await model.RunAsync(SnapCommand.NextDisplay, hwnd);
        Check("next display does the right thing for this many displays",
            Native.Displays().Count == 1 ? next == SnapResult.OneDisplay : next == SnapResult.Moved, $"{next}");

        // S-011: restore goes back to the frame from before the first snap, then has nothing left.
        var restored = await model.RunAsync(SnapCommand.Restore, hwnd);
        Check("restore puts the window back", restored == SnapResult.Restored && Native.VisibleFrame(hwnd).IsNear(before, 2),
            $"{restored}, expected {before}, got {Native.VisibleFrame(hwnd)}");
        Check("a second restore has nothing stored", await model.RunAsync(SnapCommand.Restore, hwnd) == SnapResult.NothingStored);
        Check("no window means no move", await model.RunAsync(SnapCommand.Left, 1) == SnapResult.NoWindow);
        test.Close();

        // S-012: the sign-in switch writes and removes its Run key value (a test-only value name).
        LoginItem.Set(true);
        var on = LoginItem.IsOn;
        LoginItem.Set(false);
        Check("open at sign-in writes and removes the Run key", on && !LoginItem.IsOn);

        // S-006: closing Settings hides it, and the app keeps running.
        window.ShowSettings();
        await Task.Delay(200);
        Native.PostMessageW(window.Handle(), Native.WM_CLOSE, 0, 0);
        await Task.Delay(400);
        Check("closing Settings hides it to the tray", !window.AppWindow.IsVisible);
        window.ShowSettings();
        await Task.Delay(300);

        // Every page opens and shows its title; every command has a card with a shortcut button.
        foreach (var page in Enum.GetValues<SidebarPage>())
        {
            window.Show(page.Id());
            await Task.Delay(300);
            Check($"page {page.Id()} shows its title", Find<TextBlock>(window.RootElement).Any(t => t.Text == page.Title()));
        }
        window.Show(SidebarPage.Shortcuts.Id());
        await Task.Delay(300);
        Check("every command has a shortcut button", Find<ShortcutButton>(window.RootElement).Count() == SnapCommands.All.Length);

        model.Hotkeys.UnregisterAll();
        AgentLog.Line(_failures == 0 ? $"SELFTEST PASS {_count} checks" : $"SELFTEST FAIL {_failures} of {_count} checks");
        AgentLog.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        _count++;
        if (!ok) _failures++;
        AgentLog.Line(ok ? $"PASS {name}" : $"FAIL {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    public static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var d in Find<T>(child)) yield return d;
        }
    }
}
