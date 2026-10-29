using __APP_NAME__.Core;
using __APP_NAME__.UI;
using Microsoft.UI.Xaml;

namespace __APP_NAME__;

// __APP_NAME__
//   __APP_NAME__.exe                         the app
//   APP_STORE=<file> __APP_NAME__.exe        use another database (tests, experiments); must be in %TEMP%
//   APP_SNAPSHOT=<dir> __APP_NAME__.exe      save a PNG of every page and Settings in light and dark, then quit
//   APP_SNAPSHOT_PAGES=all,settings          limit the snapshot pages
//   APP_SEED=1 __APP_NAME__.exe              fill an empty %TEMP% store with sample items (tools/snap.ps1)
//   __APP_NAME__.exe --selftest              run the self-test on a %TEMP% store, exit 0 or 1 (tools/selftest.ps1)
//   APP_ACCENT=<id> __APP_NAME__.exe         run with another accent from Theme.All (UI/Theme.cs)
//   APP_GRID=1 __APP_NAME__.exe              draw the 8 epx grid and the layout probes over the window
public partial class App : Application
{
    public static AppModel Model { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AgentLog.Line($"CRASH {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The accent must be in the resources before the first control loads.
        // Not in the constructor: Application.Current.Resources throws E_UNEXPECTED there.
        Theme.ApplyAccent(Environment.GetEnvironmentVariable("APP_ACCENT") ?? Prefs.Current.Accent);
        var store = new ItemStore(AppPaths.Store);
        SeedData.InsertIfRequested(store);
        Model = new AppModel(store);
        MainWindow = new MainWindow(Model);
        MainWindow.Activate();
        // Agent modes run once the window has drawn its first frame.
        MainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await SelfTest.RunIfRequestedAsync(MainWindow, Model);
            await Snapshot.RunIfRequestedAsync(MainWindow, Model);
        });
    }
}
