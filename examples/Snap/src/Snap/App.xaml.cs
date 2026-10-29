using Snap.UI;
using Snap.Windowing;
using Microsoft.UI.Xaml;

namespace Snap;

// Snap
//   Snap.exe                     open Settings, start the tray icon and the shortcuts
//   Snap.exe --background        tray icon and shortcuts only (the Run key at sign-in uses this)
//   APP_STORE=<file> Snap.exe    a test run: settings next to <file>, which must be in %TEMP%; no tray icon, no single instance
//   APP_SNAPSHOT=<dir> Snap.exe  save a PNG of every Settings page in light and dark, then quit
//   APP_SNAPSHOT_PAGES=shortcuts,layout   limit the snapshot pages
//   Snap.exe --selftest          run the self-test on a %TEMP% store, exit 0 or 1 (tools/selftest.ps1)
//   APP_ACCENT=<id> Snap.exe     run with another accent from Theme.All (UI/Theme.cs)
//   APP_GRID=1 Snap.exe          draw the 8 epx grid and the layout probes over the window
public partial class App : Application
{
    public static AppModel Model { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    private Mutex? _oneCopy;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AgentLog.Line($"CRASH {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A tray app lives on after its last window closes.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        var testRun = AppPaths.IsOverridden;

        // One copy. A second start asks the first to show Settings, then quits.
        // Test runs skip this, so they never join the copy the person is using.
        if (!testRun)
        {
            _oneCopy = new Mutex(true, @"Local\Snap-one-copy", out var first);
            if (!first)
            {
                var other = Native.FindWindowW(TrayHost.ClassName, null);
                if (other != 0) Native.PostMessageW(other, TrayHost.ShowSettingsMessage, 0, 0);
                Exit();
                return;
            }
        }

        // The accent must be in the resources before the first control loads.
        // Not in the constructor: Application.Current.Resources throws E_UNEXPECTED there.
        Theme.ApplyAccent(Environment.GetEnvironmentVariable("APP_ACCENT") ?? Prefs.Current.Accent);
        Model = new AppModel(withTrayIcon: !testRun);
        MainWindow = new MainWindow(Model);
        Model.ShowSettingsRequested += MainWindow.ShowSettings;
        Model.QuitRequested += Quit;

        if (!Environment.GetCommandLineArgs().Contains("--background")) MainWindow.Activate();
        // Agent modes run once the window has drawn its first frame.
        MainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await SelfTest.RunIfRequestedAsync(MainWindow, Model);
            await Snapshot.RunIfRequestedAsync(MainWindow, Model);
        });
    }

    public void Quit()
    {
        Model.Dispose();
        MainWindow.CloseForGood();
        _oneCopy?.Dispose();
        Exit();
    }
}
